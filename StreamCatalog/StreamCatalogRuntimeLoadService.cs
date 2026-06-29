namespace GrpcHttp3Demo.StreamCatalog
{
    /// <summary>
    /// 应用启动时加载 UDP 流注册表到运行时内存镜像。
    /// </summary>
    /// <remarks>
    /// 该服务只做加载，不接入 UDP 转发。若数据库不可用，会记录日志并允许应用继续启动。
    /// </remarks>
    public sealed class StreamCatalogRuntimeLoadService : IHostedService
    {
        private readonly StreamCatalogRuntimeRegistry _registry;
        private readonly ILogger<StreamCatalogRuntimeLoadService> _logger;

        public StreamCatalogRuntimeLoadService(
            StreamCatalogRuntimeRegistry registry,
            ILogger<StreamCatalogRuntimeLoadService> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        /// <summary>
        /// 应用启动时从数据库全量加载 <c>stream_catalog</c>。
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var result = _registry.ReloadFromDatabase();
            if (!result.Available)
            {
                _logger.LogWarning("Skip stream catalog runtime load: {Message}", result.Message);
            }
            else if (!result.Success)
            {
                _logger.LogError("Stream catalog runtime load failed: {Message}", result.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Stream catalog runtime load completed. Count={Count}, Version={Version}",
                    result.Count,
                    result.Version);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 应用停止时无需额外处理。
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
