namespace GrpcHttp3Demo.StreamCatalog
{
    /// <summary>
    /// UDP 流注册表模块。
    /// </summary>
    /// <remarks>
    /// 负责注册协议流目录的运行时内存镜像和启动加载服务。
    /// 该模块依赖 Storage 提供的数据库基础设施，但不属于 Storage 层。
    /// </remarks>
    public static class StreamCatalogModule
    {
        /// <summary>
        /// 注册 UDP 流注册表相关服务。
        /// </summary>
        public static IServiceCollection AddStreamCatalogModule(this IServiceCollection services)
        {
            services.AddSingleton<StreamCatalogRuntimeRegistry>();
            services.AddHostedService<StreamCatalogRuntimeLoadService>();
            return services;
        }
    }
}
