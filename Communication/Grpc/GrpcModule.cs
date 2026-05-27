namespace GrpcHttp3Demo.Communication.Grpc
{
    using GrpcHttp3Demo.Communication.Grpc.ConfigSync;
    using GrpcHttp3Demo.Communication.Grpc.Monitoring;
    using GrpcHttp3Demo.Communication.Grpc.Server;
    using GrpcHttp3Demo.Communication.Grpc.Push;
    using GrpcHttp3Demo.Communication.Grpc.Signaling;

    /// <summary>
    /// gRPC 模块：注册 gRPC 与映射 gRPC 服务
    /// </summary>
    public static class GrpcModule
    {
        public static IServiceCollection AddGrpcModule(this IServiceCollection services)
        {
            // 二级顺序：模块内部注册顺序（显式）
            services.AddGrpc();
            services.AddHttpContextAccessor();
            services.AddSingleton<PushChannelRegistry>();
            services.AddSingleton<SignalingMetricsService>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<SignalingAppService>();
            services.AddSingleton<VideoConfigDeliveryService>();
            services.AddSingleton<AudioConfigDeliveryService>();

            return services;
        }

        public static WebApplication MapGrpcModule(this WebApplication app)
        {
            app.MapGrpcService<SignalingService>();
            return app;
        }
    }
}

