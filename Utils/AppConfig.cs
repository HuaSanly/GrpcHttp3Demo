namespace GrpcHttp3Demo.Utils
{
    public static class AppConfig
    {
        private static IConfiguration? _configuration;
        private static IHostEnvironment? _environment;

        public static void Initialize(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public static bool IsBroadcastToAll
        {
            get
            {
                // 默认策略：只在 Development 环境允许广播。
                // 如需在非 Development 环境开启（例如现场联调），必须显式设置 AllowBroadcastInNonDevelopment=true。
                var broadcastConfigured = _configuration?.GetValue<bool>("DevSettings:BroadcastToAll") ?? false;
                if (!broadcastConfigured) return false;

                if (_environment?.IsDevelopment() == true) return true;

                var allowNonDev = _configuration?.GetValue<bool>("DevSettings:AllowBroadcastInNonDevelopment") ?? false;
                return allowNonDev;
            }
        }
    }
}

