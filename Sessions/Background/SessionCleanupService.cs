using GrpcHttp3Demo.Communication.Udp.Binding;
using GrpcHttp3Demo.Sessions;

namespace GrpcHttp3Demo.Sessions.Background
{
    public class SessionCleanupService : BackgroundService
    {
        private readonly SessionPresence _presence;
        private readonly UdpSessionBindingService _udpBindings;
        private readonly ILogger<SessionCleanupService> _logger;
        private readonly SessionLivenessOptions _livenessOptions;
        private readonly TimeSpan _udpControlTimeout;
        private readonly TimeSpan _udpRescueCooldown;
        private readonly int _udpMaxRescues;

        public SessionCleanupService(SessionPresence presence, UdpSessionBindingService udpBindings, ILogger<SessionCleanupService> logger, IConfiguration configuration, SessionLivenessOptions livenessOptions)
        {
            _presence = presence;
            _udpBindings = udpBindings;
            _logger = logger;
            _livenessOptions = livenessOptions;

            // UDP 端点映射过期与救援参数（默认值与文档策略保持一致，可在配置中覆盖）
            var udpControlTimeoutSeconds = configuration.GetValue<int>("MediaServer:UdpControlTimeoutSeconds", 15);
            var udpRescueCooldownSeconds = configuration.GetValue<int>("MediaServer:UdpRescueCooldownSeconds", 10);
            _udpMaxRescues = configuration.GetValue<int>("MediaServer:UdpMaxRescues", 3);

            _udpControlTimeout = TimeSpan.FromSeconds(Math.Max(1, udpControlTimeoutSeconds));
            _udpRescueCooldown = TimeSpan.FromSeconds(Math.Max(1, udpRescueCooldownSeconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Session Cleanup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_livenessOptions.CleanupCheckInterval, stoppingToken);
                    await _presence.CheckAndRescueSessionsAsync();

                    // UDP 端点映射过期检测与救援（不影响会话在线判定）
                    _udpBindings.CheckAndRescueMappings(_udpControlTimeout, _udpRescueCooldown, _udpMaxRescues);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during session cleanup");
                }
            }
        }
    }
}


