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
        private static readonly string[] TrafficCounterKeys =
        {
            "received",
            "routeMatched",
            "routeMiss",
            "forwardPlanned",
            "queueEnqueued",
            "queueDropped",
            "sendAttempt",
            "sendSuccess",
            "sendFail",
            "retry"
        };

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

            var aggregated = AggregateByPhysicalPath(raw, activeOnly);

            return Ok(new
            {
                updatedUtc = _links.LastTickUtc,
                activeOnly,
                items = aggregated
            });
        }

        private List<object> AggregateByPhysicalPath(
            List<Dictionary<string, object?>> raw, bool activeOnly)
        {
            // Group by (clientNode, serverNode) — the physical node pair
            // For ingress: client = sourceNodeId, server = targetNodeId (usually system:udp)
            // For egress:  client = targetNodeId, server = sourceNodeId
            var pathGroups = new Dictionary<string, PhysicalPathBuilder>(StringComparer.Ordinal);

            foreach (var item in raw)
            {
                var source = GetString(item, "sourceNodeId") ?? "";
                var target = GetString(item, "targetNodeId") ?? "";
                var direction = GetString(item, "direction") ?? "";

                // Determine the client-side node and server-side node
                string clientNode;
                string serverNode;

                if (IsSystemNodeId(source))
                {
                    // egress: server → client
                    clientNode = target;
                    serverNode = source;
                }
                else
                {
                    // ingress: client → server
                    clientNode = source;
                    serverNode = target;
                }

                if (string.IsNullOrWhiteSpace(clientNode)) continue;

                var pathKey = clientNode;

                if (!pathGroups.TryGetValue(pathKey, out var builder))
                {
                    builder = new PhysicalPathBuilder
                    {
                        ClientNodeId = clientNode,
                        ServerNodeId = serverNode
                    };
                    pathGroups[pathKey] = builder;
                }

                if (direction == "ingress")
                    builder.IngressFlows.Add(item);
                else
                    builder.EgressFlows.Add(item);
            }

            var result = new List<object>();
            foreach (var builder in pathGroups.Values)
            {
                var merged = builder.Build();
                var hasAnyActive = builder.IngressFlows.Any(f => GetBool(f, "active"))
                    || builder.EgressFlows.Any(f => GetBool(f, "active"));
                var hasAnyMetric = builder.IngressFlows.Any(f => HasObservedMetrics(f))
                    || builder.EgressFlows.Any(f => HasObservedMetrics(f));

                // Filter zero-traffic placeholders
                if (!hasAnyMetric) continue;
                if (activeOnly && !hasAnyActive) continue;

                merged["client"] = BuildNodeDescriptor(builder.ClientNodeId);
                merged["server"] = BuildNodeDescriptor(builder.ServerNodeId);

                result.Add(merged);
            }

            return result;
        }

        private sealed class PhysicalPathBuilder
        {
            public string ClientNodeId { get; set; } = "";
            public string ServerNodeId { get; set; } = "";
            public List<Dictionary<string, object?>> IngressFlows { get; } = new();
            public List<Dictionary<string, object?>> EgressFlows { get; } = new();

            public Dictionary<string, object?> Build()
            {
                var anyActive = IngressFlows.Any(f => GetBool(f, "active"))
                    || EgressFlows.Any(f => GetBool(f, "active"));

                var allTimes = IngressFlows.Concat(EgressFlows)
                    .Select(f => f.TryGetValue("firstSeenUtc", out var v) && v is DateTime dt ? dt : (DateTime?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToList();

                var firstSeen = allTimes.Count > 0 ? allTimes.Min() : DateTime.MinValue;
                var lastSeen = allTimes.Count > 0 ? allTimes.Max() : DateTime.MinValue;

                var hasIngress = IngressFlows.Count > 0;
                var hasEgress = EgressFlows.Count > 0;

                var merged = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["linkId"] = $"physical_{ClientNodeId}",
                    ["active"] = anyActive,
                    ["direction"] = (hasIngress, hasEgress) switch
                    {
                        (true, true) => "bidirectional",
                        (true, false) => "ingress",
                        (false, true) => "egress",
                        _ => "unknown"
                    },
                    ["firstSeenUtc"] = firstSeen,
                    ["lastSeenUtc"] = lastSeen,
                    ["ingress"] = IngressFlows.Select(f => new
                    {
                        linkId = GetString(f, "linkId"),
                        mediaKind = GetString(f, "mediaKind"),
                        active = GetBool(f, "active"),
                        firstSeenUtc = f.GetValueOrDefault("firstSeenUtc"),
                        lastSeenUtc = f.GetValueOrDefault("lastSeenUtc"),
                        received = f.GetValueOrDefault("received"),
                        routeMatched = f.GetValueOrDefault("routeMatched"),
                        routeMiss = f.GetValueOrDefault("routeMiss"),
                        failureReasons = f.GetValueOrDefault("failureReasons")
                    }).ToList(),
                    ["egress"] = EgressFlows.Select(f => new
                    {
                        linkId = GetString(f, "linkId"),
                        mediaKind = GetString(f, "mediaKind"),
                        active = GetBool(f, "active"),
                        firstSeenUtc = f.GetValueOrDefault("firstSeenUtc"),
                        lastSeenUtc = f.GetValueOrDefault("lastSeenUtc"),
                        forwardPlanned = f.GetValueOrDefault("forwardPlanned"),
                        queueEnqueued = f.GetValueOrDefault("queueEnqueued"),
                        queueDropped = f.GetValueOrDefault("queueDropped"),
                        sendAttempt = f.GetValueOrDefault("sendAttempt"),
                        sendSuccess = f.GetValueOrDefault("sendSuccess"),
                        sendFail = f.GetValueOrDefault("sendFail"),
                        retry = f.GetValueOrDefault("retry"),
                        failureReasons = f.GetValueOrDefault("failureReasons")
                    }).ToList()
                };
                return merged;
            }
        }

        private static bool IsSystemNodeId(string nodeId)
        {
            return string.Equals(nodeId, UdpLinkMetricsService.ServerNodeId, StringComparison.Ordinal)
                || string.Equals(nodeId, SystemPublishers.MonitorPublisherSessionId, StringComparison.Ordinal)
                || string.Equals(nodeId, SystemPublishers.LinkMonitorPublisherSessionId, StringComparison.Ordinal);
        }

        private static bool GetBool(Dictionary<string, object?>? item, string key)
        {
            if (item == null) return false;
            return item.TryGetValue(key, out var value) && value is true;
        }

        private static bool HasObservedMetrics(Dictionary<string, object?> item)
        {
            foreach (var key in TrafficCounterKeys)
            {
                if (item.TryGetValue(key, out var counter) && HasPositiveNumber(counter))
                {
                    return true;
                }
            }

            return item.TryGetValue("failureReasons", out var failures) && HasPositiveNumber(failures);
        }

        private static bool HasPositiveNumber(object? value)
        {
            if (value == null)
            {
                return false;
            }

            switch (value)
            {
                case byte number:
                    return number > 0;
                case sbyte number:
                    return number > 0;
                case short number:
                    return number > 0;
                case ushort number:
                    return number > 0;
                case int number:
                    return number > 0;
                case uint number:
                    return number > 0;
                case long number:
                    return number > 0;
                case ulong number:
                    return number > 0;
                case float number:
                    return number > 0;
                case double number:
                    return number > 0;
                case decimal number:
                    return number > 0;
                case bool:
                case string:
                case DateTime:
                case DateTimeOffset:
                    return false;
            }

            foreach (var property in value.GetType().GetProperties())
            {
                if (HasPositiveNumber(property.GetValue(value)))
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<string, object?> ToDictionary(object source)
        {
            return source
                .GetType()
                .GetProperties()
                .ToDictionary(property => property.Name, property => property.GetValue(source), StringComparer.Ordinal);
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