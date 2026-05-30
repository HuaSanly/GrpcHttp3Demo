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

            var raw = _links.SnapshotAll(activeOnly: false)
                .Select(ToDictionary)
                .ToList();

            var aggregated = AggregateLinks(raw, activeOnly);

            return Ok(new
            {
                updatedUtc = _links.LastTickUtc,
                activeOnly,
                items = aggregated.Select(BuildAggregatedLinkItem).ToArray()
            });
        }

        private List<Dictionary<string, object?>> AggregateLinks(
            List<Dictionary<string, object?>> raw, bool activeOnly)
        {
            var groups = raw
                .GroupBy(d => (
                    origin: GetString(d, "originSessionId") ?? "",
                    media: GetString(d, "mediaKind") ?? ""))
                .ToList();

            var result = new List<Dictionary<string, object?>>();

            foreach (var group in groups)
            {
                var ingressItems = group
                    .Where(d => IsDirection(d, "ingress"))
                    .ToList();
                var egressItems = group
                    .Where(d => IsDirection(d, "egress"))
                    .ToList();

                foreach (var egress in egressItems)
                {
                    var ingress = ingressItems.FirstOrDefault();
                    var merged = MergeLinkPair(ingress, egress);
                    if (!activeOnly || IsActiveInMerged(merged))
                        result.Add(merged);
                }

                if (egressItems.Count == 0)
                {
                    foreach (var ingress in ingressItems)
                    {
                        var merged = MergeLinkPair(ingress, null);
                        if (!activeOnly || IsActiveInMerged(merged))
                            result.Add(merged);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, object?> MergeLinkPair(
            Dictionary<string, object?>? ingress,
            Dictionary<string, object?>? egress)
        {
            var primary = ingress ?? egress!;
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["linkId"] = ingress?.GetValueOrDefault("linkId") ?? egress!.GetValueOrDefault("linkId"),
                ["originSessionId"] = primary.GetValueOrDefault("originSessionId"),
                ["mediaKind"] = primary.GetValueOrDefault("mediaKind"),
                ["direction"] = (ingress, egress) switch
                {
                    (not null, not null) => "bidirectional",
                    (not null, null) => "ingress",
                    (null, not null) => "egress",
                    _ => "unknown"
                },
                ["active"] = GetBool(ingress, "active") || GetBool(egress, "active"),
                ["firstSeenUtc"] = MinDateTime(ingress, egress, "firstSeenUtc"),
                ["lastSeenUtc"] = MaxDateTime(ingress, egress, "lastSeenUtc"),
                ["sourceNodeId"] = ingress?.GetValueOrDefault("sourceNodeId")
                    ?? egress?.GetValueOrDefault("sourceNodeId"),
                ["targetNodeId"] = egress?.GetValueOrDefault("targetNodeId")
                    ?? ingress?.GetValueOrDefault("targetNodeId"),
                ["ingress"] = ingress,
                ["egress"] = egress
            };
            return merged;
        }

        private object BuildAggregatedLinkItem(Dictionary<string, object?> merged)
        {
            var originSessionId = GetString(merged, "originSessionId");
            var sourceNodeId = GetString(merged, "sourceNodeId");
            var targetNodeId = GetString(merged, "targetNodeId");

            merged["origin"] = BuildOriginDescriptor(originSessionId);
            merged["source"] = BuildNodeDescriptor(sourceNodeId);
            merged["target"] = BuildNodeDescriptor(targetNodeId);
            return merged;
        }

        private static bool IsDirection(Dictionary<string, object?> item, string direction)
        {
            return string.Equals(
                GetString(item, "direction"),
                direction,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool GetBool(Dictionary<string, object?>? item, string key)
        {
            if (item == null) return false;
            return item.TryGetValue(key, out var value) && value is true;
        }

        private static DateTime? GetDateTime(Dictionary<string, object?>? item, string key)
        {
            if (item == null) return null;
            return item.TryGetValue(key, out var value) && value is DateTime dt ? dt : null;
        }

        private static DateTime MinDateTime(Dictionary<string, object?>? a, Dictionary<string, object?>? b, string key)
        {
            var da = GetDateTime(a, key);
            var db = GetDateTime(b, key);
            if (da == null) return db ?? DateTime.MinValue;
            if (db == null) return da.Value;
            return da.Value < db.Value ? da.Value : db.Value;
        }

        private static DateTime MaxDateTime(Dictionary<string, object?>? a, Dictionary<string, object?>? b, string key)
        {
            var da = GetDateTime(a, key);
            var db = GetDateTime(b, key);
            if (da == null) return db ?? DateTime.MinValue;
            if (db == null) return da.Value;
            return da.Value > db.Value ? da.Value : db.Value;
        }

        private static bool IsActiveInMerged(Dictionary<string, object?> merged)
        {
            return GetBool(merged, "active");
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