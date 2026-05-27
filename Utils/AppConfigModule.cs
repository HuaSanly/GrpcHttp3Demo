namespace GrpcHttp3Demo.Utils
{
    /// <summary>
    /// 配置模块：集中放置全局配置初始化逻辑
    /// </summary>
    public static class AppConfigModule
    {
        public static WebApplication UseAppConfigModule(this WebApplication app)
        {
            AppConfig.Initialize(app.Configuration, app.Environment);
            return app;
        }
    }
}

