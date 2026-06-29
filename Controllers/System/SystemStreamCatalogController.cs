using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Models.Database;
using GrpcHttp3Demo.Models.HttpRequest.System;
using GrpcHttp3Demo.Models.HttpResponse.System;
using GrpcHttp3Demo.Storage.Postgres;
using GrpcHttp3Demo.StreamCatalog;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.System
{
    /// <summary>
    /// UDP 流注册表管理接口。
    /// </summary>
    /// <remarks>
    /// 用于维护 <c>stream_catalog</c> 表中的协议前缀、流代码、包长约束、UDP socket 组等元数据。
    /// 当前接口只负责数据库增删改查，不直接改变运行时 UDP 转发链。
    /// </remarks>
    [ApiController]
    [Route("api/system/stream-catalog")]
public sealed class SystemStreamCatalogController(
    ClientAuthService authService,
    PostgresConnectionOptions options,
    PostgresSqlSugarFactory factory,
    StreamCatalogRuntimeRegistry runtimeRegistry) : ControllerBase
{
    private readonly ClientAuthService _authService = authService;
    private readonly PostgresConnectionOptions _options = options;
    private readonly PostgresSqlSugarFactory _factory = factory;
    private readonly StreamCatalogRuntimeRegistry _runtimeRegistry = runtimeRegistry;

        /// <summary>
        /// 查询 UDP 流注册表列表。
        /// </summary>
        /// <param name="enabledOnly">是否只返回已启用的流。</param>
        /// <returns>按排序值和前缀排序后的流注册项列表。</returns>
        [HttpGet]
        public IActionResult List([FromQuery] bool enabledOnly = false)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (!CanUseDatabase(out var unavailable)) return unavailable;

            var db = _factory.CreateClient();
            var query = db.Queryable<StreamCatalogRecord>();
            if (enabledOnly)
            {
                query = query.Where(item => item.Enabled);
            }

            var items = query
                .OrderBy(item => item.SortOrder)
                .OrderBy(item => item.Prefix)
                .ToList()
                .Select(ToResponse)
                .ToArray();

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                count = items.Length,
                items
            });
        }

        /// <summary>
        /// 查询当前已经加载到内存的 UDP 流注册表。
        /// </summary>
        /// <returns>运行时内存镜像中的流注册项列表。</returns>
        [HttpGet("runtime")]
        public IActionResult GetRuntime()
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });

            var items = _runtimeRegistry.Snapshot()
                .Select(ToResponse)
                .ToArray();

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                version = _runtimeRegistry.Version,
                loadedUtc = _runtimeRegistry.LoadedUtc,
                runtimeUpdatedUtc = _runtimeRegistry.UpdatedUtc,
                count = items.Length,
                items
            });
        }

        /// <summary>
        /// 从数据库全量重载 UDP 流注册表到内存。
        /// </summary>
        /// <returns>重载结果以及重载后的内存镜像。</returns>
        [HttpPost("runtime/reload")]
        public IActionResult ReloadRuntime()
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });

            var result = _runtimeRegistry.ReloadFromDatabase();
            if (!result.Available)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    updatedUtc = DateTime.UtcNow,
                    ok = false,
                    message = result.Message
                });
            }

            if (!result.Success)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    updatedUtc = DateTime.UtcNow,
                    ok = false,
                    message = result.Message
                });
            }

            var items = _runtimeRegistry.Snapshot()
                .Select(ToResponse)
                .ToArray();

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                ok = true,
                message = result.Message,
                version = result.Version,
                count = result.Count,
                items
            });
        }

        /// <summary>
        /// 按 UDP 前缀查询单个流注册项。
        /// </summary>
        /// <param name="prefix">UDP 数据包首字节类型值，支持十进制或十六进制格式，例如 <c>9</c> 或 <c>0x09</c>。</param>
        /// <returns>匹配的流注册项。</returns>
        [HttpGet("{prefix}")]
        public IActionResult Get([FromRoute] string prefix)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (!CanUseDatabase(out var unavailable)) return unavailable;
            if (!TryParsePrefix(prefix, out var prefixValue, out var prefixError))
            {
                return BadRequest(new { message = prefixError });
            }

            var db = _factory.CreateClient();
            var record = db.Queryable<StreamCatalogRecord>()
                .First(item => item.Prefix == prefixValue);

            return record == null
                ? NotFound(new { message = $"Stream catalog item not found: {FormatPrefix(prefixValue)}" })
                : Ok(new { updatedUtc = DateTime.UtcNow, item = ToResponse(record) });
        }

        /// <summary>
        /// 新建 UDP 流注册项。
        /// </summary>
        /// <param name="request">流注册项创建请求。</param>
        /// <returns>创建后的流注册项和同步后的运行时版本。</returns>
        [HttpPost]
        public IActionResult Create([FromBody] StreamCatalogUpsertRequest? request)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (!CanUseDatabase(out var unavailable)) return unavailable;
            if (request == null) return BadRequest(new { message = "Missing request body" });
            if (!TryValidateRequest(request, requirePrefix: true, out var validationError))
            {
                return BadRequest(new { message = validationError });
            }

            var now = DateTime.UtcNow;
            var record = ToRecord(request, request.Prefix!.Value, now, now);
            var db = _factory.CreateClient();

            if (db.Queryable<StreamCatalogRecord>().Any(item => item.Prefix == record.Prefix))
            {
                return Conflict(new { message = $"Stream catalog item already exists: {FormatPrefix(record.Prefix)}" });
            }

            db.Insertable(record).ExecuteCommand();
            var runtimeVersion = _runtimeRegistry.ReplaceFromDatabase(record);

            return CreatedAtAction(
                nameof(Get),
                new { prefix = FormatPrefix(record.Prefix) },
                new
                {
                    updatedUtc = DateTime.UtcNow,
                    runtimeVersion,
                    item = ToResponse(record)
                });
        }

        /// <summary>
        /// 替换式写入 UDP 流注册项。
        /// </summary>
        /// <param name="prefix">UDP 数据包首字节类型值，支持十进制或十六进制格式，例如 <c>9</c> 或 <c>0x09</c>。</param>
        /// <param name="request">流注册项完整定义；如果请求体也传入 Prefix，必须与路由值一致。</param>
        /// <returns>写入后的流注册项和同步后的运行时版本。</returns>
        /// <remarks>
        /// 该接口按“先删后增”的替换语义执行：数据库先删除同 Prefix 旧记录，再插入新记录；
        /// 数据库成功后，内存镜像也无条件先移除旧 Prefix，再写入最新记录。
        /// </remarks>
        [HttpPut("{prefix}")]
        public IActionResult Replace([FromRoute] string prefix, [FromBody] StreamCatalogUpsertRequest? request)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (!CanUseDatabase(out var unavailable)) return unavailable;
            if (!TryParsePrefix(prefix, out var prefixValue, out var prefixError))
            {
                return BadRequest(new { message = prefixError });
            }

            if (request == null) return BadRequest(new { message = "Missing request body" });
            if (request.Prefix.HasValue && request.Prefix.Value != prefixValue)
            {
                return BadRequest(new { message = "Route prefix and body prefix do not match" });
            }

            request.Prefix = prefixValue;
            if (!TryValidateRequest(request, requirePrefix: true, out var validationError))
            {
                return BadRequest(new { message = validationError });
            }

            var db = _factory.CreateClient();
            var existing = db.Queryable<StreamCatalogRecord>()
                .First(item => item.Prefix == prefixValue);

            var now = DateTime.UtcNow;
            var createdUtc = existing?.CreatedUtc ?? now;
            var record = ToRecord(request, prefixValue, createdUtc, now);

            db.Ado.BeginTran();
            try
            {
                db.Deleteable<StreamCatalogRecord>()
                    .Where(item => item.Prefix == prefixValue)
                    .ExecuteCommand();
                db.Insertable(record).ExecuteCommand();
                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }

            var runtimeVersion = _runtimeRegistry.ReplaceFromDatabase(record);

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                replaced = true,
                runtimeVersion,
                item = ToResponse(record)
            });
        }

        /// <summary>
        /// 删除 UDP 流注册项。
        /// </summary>
        /// <param name="prefix">UDP 数据包首字节类型值，支持十进制或十六进制格式，例如 <c>9</c> 或 <c>0x09</c>。</param>
        /// <returns>删除结果。</returns>
        [HttpDelete("{prefix}")]
        public IActionResult Delete([FromRoute] string prefix)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (!CanUseDatabase(out var unavailable)) return unavailable;
            if (!TryParsePrefix(prefix, out var prefixValue, out var prefixError))
            {
                return BadRequest(new { message = prefixError });
            }

            var db = _factory.CreateClient();
            var affected = db.Deleteable<StreamCatalogRecord>()
                .Where(item => item.Prefix == prefixValue)
                .ExecuteCommand();

            var runtimeVersion = _runtimeRegistry.Remove(prefixValue);

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                deleted = affected > 0,
                databaseAffectedRows = affected,
                runtimeVersion,
                prefix = prefixValue,
                prefixHex = FormatPrefix(prefixValue)
            });
        }

        private bool Authorize()
        {
            return _authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out _);
        }

        private bool CanUseDatabase(out IActionResult unavailable)
        {
            if (!_options.Enabled)
            {
                unavailable = StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Postgres is disabled" });
                return false;
            }

            if (!_factory.CanConnect())
            {
                unavailable = StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Postgres configuration is incomplete" });
                return false;
            }

            unavailable = Ok();
            return true;
        }

        private static bool TryValidateRequest(StreamCatalogUpsertRequest request, bool requirePrefix, out string? error)
        {
            error = null;

            if (requirePrefix && request.Prefix == null)
            {
                error = "Missing prefix";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.StreamCode))
            {
                error = "Missing streamCode";
                return false;
            }

            if (request.StreamCode.Length > 64)
            {
                error = "streamCode is too long; max length is 64";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                error = "Missing displayName";
                return false;
            }

            if (request.DisplayName.Length > 128)
            {
                error = "displayName is too long; max length is 128";
                return false;
            }

            if (!string.IsNullOrEmpty(request.Description) && request.Description.Length > 512)
            {
                error = "description is too long; max length is 512";
                return false;
            }

            if (request.ProtocolVersion < 0)
            {
                error = "protocolVersion must be greater than or equal to 0";
                return false;
            }

            if (request.UdpSocketGroupId < 0)
            {
                error = "udpSocketGroupId must be greater than or equal to 0";
                return false;
            }

            if (request.FixedDatagramBytes <= 0)
            {
                error = "fixedDatagramBytes must be greater than 0 when provided";
                return false;
            }

            if (request.MinDatagramBytes <= 0)
            {
                error = "minDatagramBytes must be greater than 0 when provided";
                return false;
            }

            if (request.MaxDatagramBytes <= 0)
            {
                error = "maxDatagramBytes must be greater than 0 when provided";
                return false;
            }

            if (request.MinDatagramBytes.HasValue &&
                request.MaxDatagramBytes.HasValue &&
                request.MinDatagramBytes.Value > request.MaxDatagramBytes.Value)
            {
                error = "minDatagramBytes must be less than or equal to maxDatagramBytes";
                return false;
            }

            if (request.FixedDatagramBytes.HasValue &&
                request.MinDatagramBytes.HasValue &&
                request.FixedDatagramBytes.Value < request.MinDatagramBytes.Value)
            {
                error = "fixedDatagramBytes must be greater than or equal to minDatagramBytes";
                return false;
            }

            if (request.FixedDatagramBytes.HasValue &&
                request.MaxDatagramBytes.HasValue &&
                request.FixedDatagramBytes.Value > request.MaxDatagramBytes.Value)
            {
                error = "fixedDatagramBytes must be less than or equal to maxDatagramBytes";
                return false;
            }

            return true;
        }

        private static StreamCatalogRecord ToRecord(StreamCatalogUpsertRequest request, byte prefix, DateTime createdUtc, DateTime updatedUtc)
        {
            return new StreamCatalogRecord
            {
                Prefix = prefix,
                StreamCode = request.StreamCode.Trim(),
                DisplayName = request.DisplayName.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                ProtocolVersion = request.ProtocolVersion,
                FixedDatagramBytes = request.FixedDatagramBytes,
                MinDatagramBytes = request.MinDatagramBytes,
                MaxDatagramBytes = request.MaxDatagramBytes,
                UdpSocketGroupId = request.UdpSocketGroupId,
                Enabled = request.Enabled,
                ForwardingEnabled = request.ForwardingEnabled,
                MetricsEnabled = request.MetricsEnabled,
                PublisherRole = request.PublisherRole,
                SubscriberRoleMask = request.SubscriberRoleMask,
                SortOrder = request.SortOrder,
                CreatedUtc = createdUtc,
                UpdatedUtc = updatedUtc
            };
        }

        private static StreamCatalogResponse ToResponse(StreamCatalogRecord record)
        {
            return new StreamCatalogResponse
            {
                Prefix = record.Prefix,
                PrefixHex = FormatPrefix(record.Prefix),
                StreamCode = record.StreamCode,
                DisplayName = record.DisplayName,
                Description = record.Description,
                ProtocolVersion = record.ProtocolVersion,
                FixedDatagramBytes = record.FixedDatagramBytes,
                MinDatagramBytes = record.MinDatagramBytes,
                MaxDatagramBytes = record.MaxDatagramBytes,
                UdpSocketGroupId = record.UdpSocketGroupId,
                Enabled = record.Enabled,
                ForwardingEnabled = record.ForwardingEnabled,
                MetricsEnabled = record.MetricsEnabled,
                PublisherRole = record.PublisherRole.ToString(),
                PublisherRoleValue = (int)record.PublisherRole,
                SubscriberRoleMask = record.SubscriberRoleMask,
                SortOrder = record.SortOrder,
                CreatedUtc = record.CreatedUtc,
                UpdatedUtc = record.UpdatedUtc
            };
        }

        private static bool TryParsePrefix(string value, out byte prefix, out string? error)
        {
            prefix = default;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Missing prefix";
                return false;
            }

            var normalized = value.Trim();
            var style = global::System.Globalization.NumberStyles.Integer;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[2..];
                style = global::System.Globalization.NumberStyles.HexNumber;
            }

            if (!byte.TryParse(normalized, style, global::System.Globalization.CultureInfo.InvariantCulture, out prefix))
            {
                error = $"Invalid prefix: {value}";
                return false;
            }

            return true;
        }

        private static string FormatPrefix(byte prefix)
        {
            return $"0x{prefix:X2}";
        }
    }
}
