using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Utils;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Monitoring
{
    [ApiController]
    [Route("api/monitor/system")]
    public class SystemMonitorController : ControllerBase
    {
        private readonly SessionPresence _presence;
        private readonly UdpMetricsService _udpMetrics;
        private readonly SignalingMetricsService _signalingMetrics;
        private readonly IHostEnvironment _environment;

        public SystemMonitorController(
            SessionPresence presence,
            UdpMetricsService udpMetrics,
            SignalingMetricsService signalingMetrics,
            IHostEnvironment environment)
        {
            _presence = presence;
            _udpMetrics = udpMetrics;
            _signalingMetrics = signalingMetrics;
            _environment = environment;
        }

        [HttpGet("stats")]
        public IActionResult GetSystemStats()
        {
            // 在线判定与 SessionCleanupService 的超时策略保持一致
            var onlineTimeout = TimeSpan.FromSeconds(30);

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                environment = new
                {
                    name = _environment.EnvironmentName,
                    isDevelopment = _environment.IsDevelopment(),
                    isProduction = _environment.IsProduction()
                },
                mode = new
                {
                    broadcastToAllEffective = AppConfig.IsBroadcastToAll
                },
                online = _presence.GetOnlineRoleSnapshot(onlineTimeout),
                signaling = _signalingMetrics.Snapshot(),
                udp = _udpMetrics.Snapshot()
            });
        }
    }
}

