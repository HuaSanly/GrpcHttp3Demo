using GrpcHttp3Demo.Models.Database;
using GrpcHttp3Demo.Storage.Postgres;

namespace GrpcHttp3Demo.StreamCatalog
{
    /// <summary>
    /// UDP 流注册表的运行时内存镜像。
    /// </summary>
    /// <remarks>
    /// 数据库是唯一准绳，本类只保存运行时缓存。当前阶段只提供加载、查看和同步能力，
    /// 不参与 UDP 转发热路径，避免在验证注册表之前影响现有链路。
    /// </remarks>
    public sealed class StreamCatalogRuntimeRegistry
    {
        private readonly object _gate = new();
        private readonly PostgresConnectionOptions _options;
        private readonly PostgresSqlSugarFactory _factory;
        private readonly ILogger<StreamCatalogRuntimeRegistry> _logger;
        private Dictionary<byte, StreamCatalogRecord> _items = new();
        private long _version;
        private DateTime? _loadedUtc;
        private DateTime? _updatedUtc;

        public StreamCatalogRuntimeRegistry(
            PostgresConnectionOptions options,
            PostgresSqlSugarFactory factory,
            ILogger<StreamCatalogRuntimeRegistry> logger)
        {
            _options = options;
            _factory = factory;
            _logger = logger;
        }

        /// <summary>
        /// 当前内存镜像版本号；每次全量加载、替换或删除都会递增。
        /// </summary>
        public long Version
        {
            get
            {
                lock (_gate)
                {
                    return _version;
                }
            }
        }

        /// <summary>
        /// 最近一次全量加载时间，UTC。
        /// </summary>
        public DateTime? LoadedUtc
        {
            get
            {
                lock (_gate)
                {
                    return _loadedUtc;
                }
            }
        }

        /// <summary>
        /// 最近一次内存镜像更新时间，UTC。
        /// </summary>
        public DateTime? UpdatedUtc
        {
            get
            {
                lock (_gate)
                {
                    return _updatedUtc;
                }
            }
        }

        /// <summary>
        /// 从数据库全量加载并原子替换内存镜像。
        /// </summary>
        /// <returns>加载结果。</returns>
        public StreamCatalogRuntimeLoadResult ReloadFromDatabase()
        {
            if (!_options.Enabled)
            {
                return StreamCatalogRuntimeLoadResult.Unavailable("Postgres is disabled");
            }

            if (!_factory.CanConnect())
            {
                return StreamCatalogRuntimeLoadResult.Unavailable("Postgres configuration is incomplete");
            }

            try
            {
                var db = _factory.CreateClient();
                var records = db.Queryable<StreamCatalogRecord>()
                    .OrderBy(item => item.SortOrder)
                    .OrderBy(item => item.Prefix)
                    .ToList()
                    .Select(Clone)
                    .ToArray();

                var next = records.ToDictionary(item => item.Prefix);
                var now = DateTime.UtcNow;

                lock (_gate)
                {
                    _items = next;
                    _version++;
                    _loadedUtc = now;
                    _updatedUtc = now;
                }

                _logger.LogInformation("Stream catalog runtime registry reloaded. Count={Count}", records.Length);
                return StreamCatalogRuntimeLoadResult.Succeeded(records.Length, Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload stream catalog runtime registry.");
                return StreamCatalogRuntimeLoadResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// 将数据库中的最新记录同步到内存镜像。
        /// </summary>
        /// <remarks>
        /// 内存不作为判断依据。无论旧记录是否存在，都会先从内存移除该 Prefix，再写入新记录。
        /// </remarks>
        /// <param name="record">数据库操作成功后的最新记录。</param>
        /// <returns>同步后的内存版本号。</returns>
        public long ReplaceFromDatabase(StreamCatalogRecord record)
        {
            var copy = Clone(record);
            lock (_gate)
            {
                var next = new Dictionary<byte, StreamCatalogRecord>(_items);
                next.Remove(copy.Prefix);
                next[copy.Prefix] = copy;
                _items = next;
                _version++;
                _updatedUtc = DateTime.UtcNow;
                return _version;
            }
        }

        /// <summary>
        /// 从内存镜像移除指定 Prefix。
        /// </summary>
        /// <remarks>
        /// 删除是否存在以数据库操作结果为准；这里无条件尝试移除，用于让内存跟随数据库。
        /// </remarks>
        /// <param name="prefix">UDP 数据包首字节类型值。</param>
        /// <returns>同步后的内存版本号。</returns>
        public long Remove(byte prefix)
        {
            lock (_gate)
            {
                var next = new Dictionary<byte, StreamCatalogRecord>(_items);
                next.Remove(prefix);
                _items = next;
                _version++;
                _updatedUtc = DateTime.UtcNow;
                return _version;
            }
        }

        /// <summary>
        /// 获取当前内存镜像快照。
        /// </summary>
        /// <returns>内存中的流注册项副本。</returns>
        public StreamCatalogRecord[] Snapshot()
        {
            lock (_gate)
            {
                return _items.Values
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Prefix)
                    .Select(Clone)
                    .ToArray();
            }
        }

        private static StreamCatalogRecord Clone(StreamCatalogRecord source)
        {
            return new StreamCatalogRecord
            {
                Prefix = source.Prefix,
                StreamCode = source.StreamCode,
                DisplayName = source.DisplayName,
                Description = source.Description,
                ProtocolVersion = source.ProtocolVersion,
                FixedDatagramBytes = source.FixedDatagramBytes,
                MinDatagramBytes = source.MinDatagramBytes,
                MaxDatagramBytes = source.MaxDatagramBytes,
                UdpSocketGroupId = source.UdpSocketGroupId,
                Enabled = source.Enabled,
                ForwardingEnabled = source.ForwardingEnabled,
                MetricsEnabled = source.MetricsEnabled,
                PublisherRole = source.PublisherRole,
                SubscriberRoleMask = source.SubscriberRoleMask,
                SortOrder = source.SortOrder,
                CreatedUtc = source.CreatedUtc,
                UpdatedUtc = source.UpdatedUtc
            };
        }
    }

    /// <summary>
    /// UDP 流注册表运行时加载结果。
    /// </summary>
    public sealed class StreamCatalogRuntimeLoadResult
    {
        /// <summary>
        /// 是否已成功访问数据库配置。
        /// </summary>
        public bool Available { get; private init; }

        /// <summary>
        /// 是否加载成功。
        /// </summary>
        public bool Success { get; private init; }

        /// <summary>
        /// 加载结果说明。
        /// </summary>
        public string Message { get; private init; } = string.Empty;

        /// <summary>
        /// 加载到内存的记录数量。
        /// </summary>
        public int Count { get; private init; }

        /// <summary>
        /// 加载完成后的内存镜像版本号。
        /// </summary>
        public long Version { get; private init; }

        public static StreamCatalogRuntimeLoadResult Succeeded(int count, long version)
        {
            return new StreamCatalogRuntimeLoadResult
            {
                Available = true,
                Success = true,
                Message = "Stream catalog runtime registry reloaded.",
                Count = count,
                Version = version
            };
        }

        public static StreamCatalogRuntimeLoadResult Unavailable(string message)
        {
            return new StreamCatalogRuntimeLoadResult
            {
                Available = false,
                Success = false,
                Message = message
            };
        }

        public static StreamCatalogRuntimeLoadResult Failed(string message)
        {
            return new StreamCatalogRuntimeLoadResult
            {
                Available = true,
                Success = false,
                Message = message
            };
        }
    }
}
