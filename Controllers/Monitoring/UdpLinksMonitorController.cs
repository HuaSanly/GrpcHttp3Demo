using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GrpcHttp3Demo.Controllers.Monitoring
{
    [ApiController]
    public sealed class UdpLinksMonitorController : ControllerBase
    {
        private readonly UdpLinkMetricsService _links;
        private readonly SessionRegistry _sessions;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public UdpLinksMonitorController(UdpLinkMetricsService links, SessionRegistry sessions, IHostEnvironment env, IConfiguration configuration)
        {
            _links = links;
            _sessions = sessions;
            _env = env;
            _configuration = configuration;
        }

        [HttpGet("/api/monitor/udp/links")]
        public IActionResult List([FromQuery] bool activeOnly = false)
        {
            var monitoringEnabled = _env.IsDevelopment() || _configuration.GetValue<bool>("Monitoring:Enabled", false);
            if (!monitoringEnabled) return NotFound();

            return Ok(new
            {
                updatedUtc = _links.LastTickUtc,
                activeOnly,
                items = _links.SnapshotAll(activeOnly).Select(BuildLinkListItem).ToArray()
            });
        }

        private object BuildLinkListItem(object snapshot)
        {
            var item = ToDictionary(snapshot);

            var originSessionId = GetString(item, "originSessionId");
            var sourceNodeId = GetString(item, "sourceNodeId");
            var targetNodeId = GetString(item, "targetNodeId");

            item["origin"] = BuildOriginDescriptor(originSessionId);
            item["source"] = BuildNodeDescriptor(sourceNodeId);
            item["target"] = BuildNodeDescriptor(targetNodeId);
            return item;
        }

        private Dictionary<string, object?> ToDictionary(object source)
        {
            return source
                .GetType()
                .GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(source), StringComparer.Ordinal);
        }

        private object BuildOriginDescriptor(string? sessionId)
        {
            var session = !string.IsNullOrWhiteSpace(sessionId)
                ? _sessions.GetSession(sessionId)
                : null;

            var role = session != null && session.Role != RegisterRequest.Types.EndpointType.Unknown
                ? session.Role.ToString().ToLowerInvariant()
                : null;

            return new
            {
                sessionId,
                deviceId = session?.DeviceId,
                displayName = ResolveSessionDisplayName(sessionId, session),
                role,
                nodeKind = ResolveNodeKind(sessionId, session)
            };
        }

        private object BuildNodeDescriptor(string? nodeId)
        {
            var session = !string.IsNullOrWhiteSpace(nodeId)
                ? _sessions.GetSession(nodeId)
                : null;

            var role = session != null && session.Role != RegisterRequest.Types.EndpointType.Unknown
                ? session.Role.ToString().ToLowerInvariant()
                : null;

            return new
            {
                nodeId,
                sessionId = session?.SessionId,
                deviceId = session?.DeviceId,
                displayName = ResolveNodeDisplayName(nodeId, session),
                role,
                nodeKind = ResolveNodeKind(nodeId, session)
            };
        }

        private static string? GetString(IReadOnlyDictionary<string, object?> item, string key)
        {
            return item.TryGetValue(key, out var value) ? value as string : null;
        }

        private static string ResolveSessionDisplayName(string? sessionId, Models.Session.DeviceContext? session)
        {
            if (session != null)
            {
                if (!string.IsNullOrWhiteSpace(session.DeviceId)) return session.DeviceId;
                if (!string.IsNullOrWhiteSpace(session.SessionId)) return session.SessionId;
            }

            return ResolveSystemDisplayName(sessionId);
        }

        private static string ResolveNodeDisplayName(string? nodeId, Models.Session.DeviceContext? session)
        {
            if (session != null)
            {
                if (!string.IsNullOrWhiteSpace(session.DeviceId)) return session.DeviceId;
                if (!string.IsNullOrWhiteSpace(session.SessionId)) return session.SessionId;
            }

            return ResolveSystemDisplayName(nodeId);
        }

        private static string ResolveSystemDisplayName(string? nodeId)
        {
            return nodeId switch
            {
                UdpLinkMetricsService.ServerNodeId => "UDP 转发服务",
                SystemPublishers.MonitorPublisherSessionId => "系统监控服务",
                SystemPublishers.LinkMonitorPublisherSessionId => "链路监控服务",
                null or "" => string.Empty,
                _ => nodeId
            };
        }

        private static string ResolveNodeKind(string? nodeId, Models.Session.DeviceContext? session)
        {
            if (session != null)
            {
                return session.Role switch
                {
                    RegisterRequest.Types.EndpointType.Robot => "robot",
                    RegisterRequest.Types.EndpointType.Vr => "vr",
                    RegisterRequest.Types.EndpointType.Client => "client",
                    _ => "unknown"
                };
            }

            return !string.IsNullOrWhiteSpace(nodeId) && nodeId.StartsWith("system:", StringComparison.Ordinal)
                ? "system"
                : "unknown";
        }
    }
}