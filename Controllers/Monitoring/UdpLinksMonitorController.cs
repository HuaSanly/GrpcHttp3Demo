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

        [HttpGet("/api/monitor/udp/topologies")]
        [HttpGet("/api/monitor/udp/links")]
        public IActionResult List([FromQuery] bool activeOnly = false)
        {
            var monitoringEnabled = _env.IsDevelopment() || _configuration.GetValue<bool>("Monitoring:Enabled", false);
            if (!monitoringEnabled) return NotFound();

            return Ok(new
            {
                updatedUtc = _links.LastTickUtc,
                activeOnly,
                items = _links.SnapshotTopologies(activeOnly)
                    .Select(BuildTopologyItem)
                    .ToArray()
            });
        }

        private object BuildTopologyItem(UdpTopologySnapshot topology)
        {
            var nodes = topology.NodeIds
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .Select(BuildNodeDescriptor)
                .ToArray();

            var edges = topology.Edges
                .Select(edge => new
                {
                    sourceNodeId = edge.SourceNodeId,
                    targetNodeId = edge.TargetNodeId,
                    active = edge.Active,
                    flows = edge.Flows
                })
                .ToArray();

            return new
            {
                topologyId = topology.TopologyId,
                active = topology.Active,
                firstSeenUtc = topology.FirstSeenUtc,
                lastSeenUtc = topology.LastSeenUtc,
                nodes,
                edges
            };
        }

        private object BuildNodeDescriptor(string nodeId)
        {
            var session = !string.IsNullOrWhiteSpace(nodeId)
                ? _sessions.GetSession(nodeId)
                : null;

            return new
            {
                nodeId,
                sessionId = session?.SessionId,
                deviceId = session?.DeviceId,
                displayName = ResolveNodeDisplayName(nodeId, session),
                nodeKind = ResolveNodeKind(nodeId, session)
            };
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

            return nodeId switch
            {
                UdpLinkMetricsService.ServerNodeId => "udp_service",
                SystemPublishers.MonitorPublisherSessionId => "system_monitor",
                SystemPublishers.LinkMonitorPublisherSessionId => "topology_monitor",
                _ => !string.IsNullOrWhiteSpace(nodeId) && nodeId.StartsWith("system:", StringComparison.Ordinal)
                    ? "system"
                    : "unknown"
            };
        }
    }
}