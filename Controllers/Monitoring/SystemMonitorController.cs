using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Storage.Memory.Session;
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
        private readonly SessionMemoryStore _memory;
        private readonly IHostEnvironment _environment;
        private readonly SessionLivenessOptions _livenessOptions;

        public SystemMonitorController(
            SessionPresence presence,
            UdpMetricsService udpMetrics,
            SignalingMetricsService signalingMetrics,
            SessionMemoryStore memory,
            IHostEnvironment environment,
            SessionLivenessOptions livenessOptions)
        {
            _presence = presence;
            _udpMetrics = udpMetrics;
            _signalingMetrics = signalingMetrics;
            _memory = memory;
            _environment = environment;
            _livenessOptions = livenessOptions;
        }

        [HttpGet("stats")]
        public IActionResult GetSystemStats()
        {
            // 在线判定与 SessionCleanupService 的超时策略保持一致
            var onlineTimeout = _livenessOptions.Timeout;

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                environment = new
                {
                    name = _environment.EnvironmentName,
                    isDevelopment = _environment.IsDevelopment(),
                    isProduction = _environment.IsProduction()
                },
                online = _presence.GetOnlineRoleSnapshot(onlineTimeout),
                runtimeTables = _memory.Snapshot(),
                signaling = _signalingMetrics.Snapshot(),
                udp = _udpMetrics.Snapshot()
            });
        }
    }
}

