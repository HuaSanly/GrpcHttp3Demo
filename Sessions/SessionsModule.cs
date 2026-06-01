using GrpcHttp3Demo.Sessions.Background;

namespace GrpcHttp3Demo.Sessions
{
    /// <summary>
    /// 会话模块：注册会话域的核心依赖。
    /// </summary>
    public static class SessionsModule
    {
        public static IServiceCollection AddSessionsModule(this IServiceCollection services)
        {
            services.AddSingleton(sp => SessionLivenessOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
            services.AddSingleton(sp => PairingAutoSubscribeOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
            services.AddSingleton<PairingAutoSubscribeService>();
            services.AddSingleton<SessionRouting>();
            services.AddSingleton<SessionRuntimeProjection>();
            services.AddSingleton<SessionPairing>();
            services.AddSingleton<SessionSubscription>();
            services.AddSingleton<SessionRegistry>();
            services.AddSingleton<SessionPresence>();
            services.AddSingleton<SessionQueries>();
            services.AddHostedService<SessionCleanupService>();
            return services;
        }
    }
}

