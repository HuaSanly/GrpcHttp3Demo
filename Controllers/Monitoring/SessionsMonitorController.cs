using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Monitoring
{
    [ApiController]
    [Route("api/monitor/sessions")]
    public sealed class SessionsMonitorController : ControllerBase
    {
        private readonly SessionQueries _sessions;
        private readonly UdpForwardingMetricsService _forwardingMetrics;
        private readonly SessionLivenessOptions _livenessOptions;

        public SessionsMonitorController(SessionQueries sessions, UdpForwardingMetricsService forwardingMetrics, SessionLivenessOptions livenessOptions)
        {
            _sessions = sessions;
            _forwardingMetrics = forwardingMetrics;
            _livenessOptions = livenessOptions;
        }

        // 粗略列表：用于前端列表页
        // Query:
        // - role: robot|vr|client|unknown (optional)
        // - onlineOnly: true|false (optional, default false)
        [HttpGet]
        public IActionResult List([FromQuery] string? role, [FromQuery] bool onlineOnly = false)
        {
            var onlineTimeout = _livenessOptions.Timeout;
            var roleFilter = ParseRole(role);

            var items = _sessions.ListSessions(onlineTimeout, roleFilter, onlineOnly);
            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                timeoutSeconds = (int)onlineTimeout.TotalSeconds,
                role = roleFilter?.ToString(),
                onlineOnly,
                items
            });
        }

        // 详情：用于点进某个 session 查看详细信息
        [HttpGet("{sessionId}")]
        public IActionResult Detail([FromRoute] string sessionId)
        {
            var onlineTimeout = _livenessOptions.Timeout;
            var detail = _sessions.GetSessionDetail(sessionId, onlineTimeout, _forwardingMetrics);
            if (detail == null) return NotFound(new { message = $"Session not found: {sessionId}" });
            return Ok(new { updatedUtc = DateTime.UtcNow, timeoutSeconds = (int)onlineTimeout.TotalSeconds, detail });
        }

        private static RegisterRequest.Types.EndpointType? ParseRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return null;

            switch (role.Trim().ToLowerInvariant())
            {
                case "robot":
                    return RegisterRequest.Types.EndpointType.Robot;
                case "vr":
                    return RegisterRequest.Types.EndpointType.Vr;
                case "client":
                    return RegisterRequest.Types.EndpointType.Client;
                case "unknown":
                    return RegisterRequest.Types.EndpointType.Unknown;
                default:
                    return null;
            }
        }
    }
}

