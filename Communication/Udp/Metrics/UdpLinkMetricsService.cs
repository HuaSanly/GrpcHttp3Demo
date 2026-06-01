using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using GrpcHttp3Demo.Sessions;

namespace GrpcHttp3Demo.Communication.Udp.Metrics
{
    public enum UdpLinkDirection
    {
        Ingress = 1,
        Egress = 2
    }

    public enum UdpLinkMediaKind
    {
        Video = 1,
        Pose = 2,
        Audio = 3,
        TelemetryLowRate = 4,
        TelemetryHighRate = 5,
        Feedback = 6,
        SystemMonitor = 7,
        TopologyMonitor = 8
    }

    public enum UdpLinkFailureKind
    {
        None = 0,
        NoRoute = 1,
        NoTarget = 2,
        QueueFull = 3,
        NoBufferSpace = 4,
        HostUnreachable = 5,
        NetworkUnreachable = 6,
        TimedOut = 7,
        SocketError = 8,
        Unknown = 9
    }

    public readonly record struct UdpLinkKey(
        string OriginSessionId,
        string SourceNodeId,
        string TargetNodeId,
        UdpLinkDirection Direction,
        UdpLinkMediaKind MediaKind);

    public sealed record UdpTopologyEdgeSnapshot(
        string SourceNodeId,
        string TargetNodeId,
        bool Active,
        string[] Flows);

    public sealed record UdpTopologySnapshot(
        string TopologyId,
        bool Active,
        DateTime FirstSeenUtc,
        DateTime LastSeenUtc,
        string[] NodeIds,
        UdpTopologyEdgeSnapshot[] Edges);

    public sealed class UdpLinkMetricsService : IDisposable
    {
        public const string ServerNodeId = "system:udp";

        private readonly ILogger<UdpLinkMetricsService> _logger;
        private readonly ConcurrentDictionary<UdpLinkKey, UdpRuntimeLink> _links = new();
        private readonly Timer _timer;
        private DateTime _lastTickUtc;

        public UdpLinkMetricsService(ILogger<UdpLinkMetricsService> logger)
        {
            _logger = logger;
            _lastTickUtc = DateTime.UtcNow;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public DateTime LastTickUtc => _lastTickUtc;

        public UdpRuntimeLink GetOrCreateIngressLink(string originSessionId, UdpLinkMediaKind mediaKind)
        {
            return GetOrCreate(new UdpLinkKey(originSessionId, originSessionId, ServerNodeId, UdpLinkDirection.Ingress, mediaKind));
        }

        public bool TryGetIngressLink(string originSessionId, UdpLinkMediaKind mediaKind, out UdpRuntimeLink? link)
        {
            return _links.TryGetValue(new UdpLinkKey(originSessionId, originSessionId, ServerNodeId, UdpLinkDirection.Ingress, mediaKind), out link);
        }

        public UdpRuntimeLink GetOrCreateEgressLink(string originSessionId, string targetSessionId, UdpLinkMediaKind mediaKind)
        {
            return GetOrCreate(new UdpLinkKey(originSessionId, ServerNodeId, targetSessionId, UdpLinkDirection.Egress, mediaKind));
        }

        public UdpRuntimeLink GetOrCreateSystemMonitorLink(string targetSessionId)
        {
            return GetOrCreate(new UdpLinkKey(SystemPublishers.MonitorPublisherSessionId, ServerNodeId, targetSessionId, UdpLinkDirection.Egress, UdpLinkMediaKind.SystemMonitor));
        }

        public UdpRuntimeLink GetOrCreateTopologyMonitorLink(string targetSessionId)
        {
            return GetOrCreateTopologyMonitorLink(SystemPublishers.MonitorPublisherSessionId, targetSessionId);
        }

        public UdpRuntimeLink GetOrCreateTopologyMonitorLink(string originSessionId, string targetSessionId)
        {
            return GetOrCreate(new UdpLinkKey(originSessionId, ServerNodeId, targetSessionId, UdpLinkDirection.Egress, UdpLinkMediaKind.TopologyMonitor));
        }

        public void DeleteLinksForOrigin(string originSessionId)
        {
            var keysToRemove = new List<UdpLinkKey>();
            foreach (var kvp in _links)
            {
                if (string.Equals(kvp.Key.OriginSessionId, originSessionId, StringComparison.Ordinal))
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove)
                _links.TryRemove(key, out _);
        }

        public void DeleteLinksForSession(string sessionId)
        {
            var keysToRemove = new List<UdpLinkKey>();
            foreach (var kvp in _links)
            {
                var key = kvp.Key;
                if (string.Equals(key.OriginSessionId, sessionId, StringComparison.Ordinal)
                    || string.Equals(key.SourceNodeId, sessionId, StringComparison.Ordinal)
                    || string.Equals(key.TargetNodeId, sessionId, StringComparison.Ordinal))
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                _links.TryRemove(key, out _);
        }

        public void DeleteEgressLink(string originSessionId, string targetSessionId, UdpLinkMediaKind mediaKind)
        {
            var key = new UdpLinkKey(originSessionId, ServerNodeId, targetSessionId, UdpLinkDirection.Egress, mediaKind);
            _links.TryRemove(key, out _);
        }

        public void DeleteIngressLink(string originSessionId, UdpLinkMediaKind mediaKind)
        {
            var key = new UdpLinkKey(originSessionId, originSessionId, ServerNodeId, UdpLinkDirection.Ingress, mediaKind);
            _links.TryRemove(key, out _);
        }

        public bool HasAnyEgressLink(string originSessionId, UdpLinkMediaKind mediaKind)
        {
            return _links.Keys.Any(key =>
                string.Equals(key.OriginSessionId, originSessionId, StringComparison.Ordinal)
                && key.Direction == UdpLinkDirection.Egress
                && key.MediaKind == mediaKind);
        }

        public IReadOnlyCollection<object> SnapshotAll()
        {
            return _links.Values
                .OrderBy(link => link.OriginSessionId, StringComparer.Ordinal)
                .ThenBy(link => link.Direction)
                .ThenBy(link => link.MediaKind)
                .ThenBy(link => link.TargetNodeId, StringComparer.Ordinal)
                .Select(link => link.Snapshot())
                .ToArray<object>();
        }

        public IReadOnlyCollection<object> SnapshotByLinkId(string? linkId)
        {
            if (string.IsNullOrWhiteSpace(linkId))
            {
                return Array.Empty<object>();
            }

            return _links.Values
                .Where(link => string.Equals(link.LinkId, linkId, StringComparison.Ordinal))
                .OrderBy(link => link.OriginSessionId, StringComparer.Ordinal)
                .ThenBy(link => link.Direction)
                .ThenBy(link => link.MediaKind)
                .ThenBy(link => link.TargetNodeId, StringComparer.Ordinal)
                .Select(link => link.Snapshot())
                .ToArray<object>();
        }

        public IReadOnlyCollection<UdpTopologySnapshot> SnapshotTopologies()
        {
            return BuildTopologyGroups()
                .Select(group => group.ToSnapshot())
                .ToArray();
        }

        public IReadOnlyCollection<object> SnapshotByTopologyId(string? topologyId)
        {
            if (string.IsNullOrWhiteSpace(topologyId))
            {
                return Array.Empty<object>();
            }

            var group = BuildTopologyGroups()
                .FirstOrDefault(item => string.Equals(item.TopologyId, topologyId, StringComparison.Ordinal));

            if (group == null)
            {
                return Array.Empty<object>();
            }

            return group.Links
                .OrderBy(link => link.OriginSessionId, StringComparer.Ordinal)
                .ThenBy(link => link.Direction)
                .ThenBy(link => link.MediaKind)
                .ThenBy(link => link.TargetNodeId, StringComparer.Ordinal)
                .Select(link => link.Snapshot())
                .ToArray<object>();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private List<UdpTopologyGroup> BuildTopologyGroups()
        {
            var links = _links.Values.ToArray();

            if (links.Length == 0)
            {
                return new List<UdpTopologyGroup>();
            }

            var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var peerNodes = new HashSet<string>(StringComparer.Ordinal);
            var monitorOnlyNodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var link in links)
            {
                if (IsMonitorMediaKind(link.MediaKind))
                {
                    if (IsPeerNodeId(link.TargetNodeId))
                    {
                        monitorOnlyNodes.Add(link.TargetNodeId);
                    }

                    continue;
                }

                if (IsPeerNodeId(link.OriginSessionId))
                {
                    peerNodes.Add(link.OriginSessionId);
                }

                if (link.Direction == UdpLinkDirection.Egress
                    && IsPeerNodeId(link.OriginSessionId)
                    && IsPeerNodeId(link.TargetNodeId))
                {
                    peerNodes.Add(link.TargetNodeId);
                    ConnectPeerNodes(adjacency, link.OriginSessionId, link.TargetNodeId);
                }
            }

            var groups = new List<UdpTopologyGroup>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var assignedMonitorNodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var startNode in peerNodes.OrderBy(node => node, StringComparer.Ordinal))
            {
                if (!visited.Add(startNode))
                {
                    continue;
                }

                var component = new HashSet<string>(StringComparer.Ordinal) { startNode };
                var stack = new Stack<string>();
                stack.Push(startNode);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (!adjacency.TryGetValue(current, out var neighbors))
                    {
                        continue;
                    }

                    foreach (var neighbor in neighbors)
                    {
                        if (!visited.Add(neighbor))
                        {
                            continue;
                        }

                        component.Add(neighbor);
                        stack.Push(neighbor);
                    }
                }

                foreach (var node in component)
                {
                    assignedMonitorNodes.Add(node);
                }

                var groupLinks = links
                    .Where(link => BelongsToTopology(link, component))
                    .ToArray();

                if (groupLinks.Length == 0)
                {
                    continue;
                }

                groups.Add(new UdpTopologyGroup(CreateTopologyId(component), component, groupLinks));
            }

            foreach (var node in monitorOnlyNodes.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (assignedMonitorNodes.Contains(node))
                {
                    continue;
                }

                var component = new HashSet<string>(StringComparer.Ordinal) { node };
                var groupLinks = links
                    .Where(link => IsMonitorMediaKind(link.MediaKind)
                        && string.Equals(link.TargetNodeId, node, StringComparison.Ordinal))
                    .ToArray();

                if (groupLinks.Length == 0)
                {
                    continue;
                }

                groups.Add(new UdpTopologyGroup(CreateTopologyId(component), component, groupLinks));
            }

            return groups
                .OrderBy(group => group.TopologyId, StringComparer.Ordinal)
                .ToList();
        }

        private static void ConnectPeerNodes(
            IDictionary<string, HashSet<string>> adjacency,
            string sourceNodeId,
            string targetNodeId)
        {
            if (!adjacency.TryGetValue(sourceNodeId, out var sourceNeighbors))
            {
                sourceNeighbors = new HashSet<string>(StringComparer.Ordinal);
                adjacency[sourceNodeId] = sourceNeighbors;
            }

            sourceNeighbors.Add(targetNodeId);

            if (!adjacency.TryGetValue(targetNodeId, out var targetNeighbors))
            {
                targetNeighbors = new HashSet<string>(StringComparer.Ordinal);
                adjacency[targetNodeId] = targetNeighbors;
            }

            targetNeighbors.Add(sourceNodeId);
        }

        private static bool BelongsToTopology(UdpRuntimeLink link, IReadOnlySet<string> topologyPeerNodeIds)
        {
            if (IsMonitorMediaKind(link.MediaKind))
            {
                return topologyPeerNodeIds.Contains(link.TargetNodeId);
            }

            if (topologyPeerNodeIds.Contains(link.OriginSessionId))
            {
                return true;
            }

            return link.Direction == UdpLinkDirection.Egress && topologyPeerNodeIds.Contains(link.TargetNodeId);
        }

        private static bool IsPeerNodeId(string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) && !IsSystemNodeId(nodeId);
        }

        private static bool IsSystemNodeId(string nodeId)
        {
            return string.Equals(nodeId, ServerNodeId, StringComparison.Ordinal)
                || string.Equals(nodeId, SystemPublishers.MonitorPublisherSessionId, StringComparison.Ordinal)
                || string.Equals(nodeId, SystemPublishers.LinkMonitorPublisherSessionId, StringComparison.Ordinal);
        }

        private static bool IsMonitorMediaKind(UdpLinkMediaKind mediaKind)
        {
            return mediaKind == UdpLinkMediaKind.SystemMonitor || mediaKind == UdpLinkMediaKind.TopologyMonitor;
        }

        private static string CreateTopologyId(IEnumerable<string> peerNodeIds)
        {
            var joined = string.Join("|", peerNodeIds.OrderBy(value => value, StringComparer.Ordinal));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
            return $"top_{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
        }

        internal static string ToWireMediaName(UdpLinkMediaKind mediaKind)
        {
            return mediaKind switch
            {
                UdpLinkMediaKind.Video => "video",
                UdpLinkMediaKind.Pose => "pose",
                UdpLinkMediaKind.Audio => "audio",
                UdpLinkMediaKind.TelemetryLowRate => "telemetry_low_rate",
                UdpLinkMediaKind.TelemetryHighRate => "telemetry_high_rate",
                UdpLinkMediaKind.Feedback => "feedback",
                UdpLinkMediaKind.SystemMonitor => "system_monitor",
                UdpLinkMediaKind.TopologyMonitor => "topology_monitor",
                _ => "unknown"
            };
        }

        private UdpRuntimeLink GetOrCreate(UdpLinkKey key)
        {
            var link = _links.GetOrAdd(key, static k => new UdpRuntimeLink(k));
            return link;
        }

        private void Tick()
        {
            try
            {
                foreach (var link in _links.Values)
                {
                    link.Tick();
                }

                _lastTickUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP link metrics tick failed");
            }
        }
    }

    internal sealed class UdpTopologyGroup
    {
        public UdpTopologyGroup(string topologyId, IReadOnlySet<string> peerNodeIds, IReadOnlyCollection<UdpRuntimeLink> links)
        {
            TopologyId = topologyId;
            PeerNodeIds = peerNodeIds;
            Links = links;
        }

        public string TopologyId { get; }
        public IReadOnlySet<string> PeerNodeIds { get; }
        public IReadOnlyCollection<UdpRuntimeLink> Links { get; }

        public UdpTopologySnapshot ToSnapshot()
        {
            var nodeIds = Links
                .SelectMany(link => new[] { link.SourceNodeId, link.TargetNodeId })
                .Concat(PeerNodeIds)
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .ToArray();

            var edges = Links
                .GroupBy(link => (link.SourceNodeId, link.TargetNodeId))
                .Select(group => new UdpTopologyEdgeSnapshot(
                    group.Key.SourceNodeId,
                    group.Key.TargetNodeId,
                    true,
                    group
                        .Select(link => UdpLinkMetricsService.ToWireMediaName(link.MediaKind))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(flow => flow, StringComparer.Ordinal)
                        .ToArray()))
                .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
                .ToArray();

            return new UdpTopologySnapshot(
                TopologyId,
                Links.Count > 0,
                Links.Min(link => link.FirstSeenUtc),
                Links.Max(link => link.LastSeenUtc),
                nodeIds,
                edges);
        }
    }

    public sealed class UdpRuntimeLink
    {
        private readonly UdpTrafficCounter _received = new();
        private readonly UdpTrafficCounter _routeMatched = new();
        private readonly UdpTrafficCounter _routeMiss = new();
        private readonly UdpTrafficCounter _forwardPlanned = new();
        private readonly UdpTrafficCounter _queueEnqueued = new();
        private readonly UdpTrafficCounter _queueDropped = new();
        private readonly UdpTrafficCounter _sendAttempt = new();
        private readonly UdpTrafficCounter _sendSuccess = new();
        private readonly UdpTrafficCounter _sendFail = new();
        private readonly UdpTrafficCounter _retry = new();

        private readonly UdpEventCounter _noRoute = new();
        private readonly UdpEventCounter _noTarget = new();
        private readonly UdpEventCounter _queueFull = new();
        private readonly UdpEventCounter _noBufferSpace = new();
        private readonly UdpEventCounter _hostUnreachable = new();
        private readonly UdpEventCounter _networkUnreachable = new();
        private readonly UdpEventCounter _timedOut = new();
        private readonly UdpEventCounter _socketError = new();
        private readonly UdpEventCounter _unknownError = new();

        private long _lastSeenUnixMs;

        internal UdpRuntimeLink(UdpLinkKey key)
        {
            LinkId = $"lk_{Guid.NewGuid():N}";
            OriginSessionId = key.OriginSessionId;
            SourceNodeId = key.SourceNodeId;
            TargetNodeId = key.TargetNodeId;
            Direction = key.Direction;
            MediaKind = key.MediaKind;
            FirstSeenUtc = DateTime.UtcNow;
            _lastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public string LinkId { get; }
        public string OriginSessionId { get; }
        public string SourceNodeId { get; }
        public string TargetNodeId { get; }
        public UdpLinkDirection Direction { get; }
        public UdpLinkMediaKind MediaKind { get; }
        public DateTime FirstSeenUtc { get; }
        internal DateTime LastSeenUtc => DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref _lastSeenUnixMs)).UtcDateTime;

        public void RecordReceived(int bytes)
        {
            Touch();
            _received.Add(bytes);
        }

        public void RecordRouteMatched(int bytes)
        {
            Touch();
            _routeMatched.Add(bytes);
        }

        public void RecordRouteMiss(int bytes, UdpLinkFailureKind reason)
        {
            Touch();
            _routeMiss.Add(bytes);
            GetFailureCounter(reason).Add();
        }

        public void RecordForwardPlanned(int bytes)
        {
            Touch();
            _forwardPlanned.Add(bytes);
        }

        public void RecordQueueEnqueued(int bytes)
        {
            Touch();
            _queueEnqueued.Add(bytes);
        }

        public void RecordQueueDropped(int bytes)
        {
            Touch();
            _queueDropped.Add(bytes);
            _queueFull.Add();
        }

        public void RecordSendAttempt(int bytes)
        {
            Touch();
            _sendAttempt.Add(bytes);
        }

        public void RecordSendSuccess(int bytes)
        {
            Touch();
            _sendSuccess.Add(bytes);
        }

        public void RecordSendFailure(int bytes, SocketError error)
        {
            Touch();
            _sendFail.Add(bytes);
            GetFailureCounter(MapSocketError(error)).Add();
        }

        public void RecordRetry(int bytes)
        {
            Touch();
            _retry.Add(bytes);
        }

        public object Snapshot()
        {
            return new
            {
                linkId = LinkId,
                originSessionId = OriginSessionId,
                sourceNodeId = SourceNodeId,
                targetNodeId = TargetNodeId,
                direction = Direction == UdpLinkDirection.Ingress ? "ingress" : "egress",
                mediaKind = ToWireMediaName(MediaKind),
                firstSeenUtc = FirstSeenUtc,
                lastSeenUtc = LastSeenUtc,
                received = _received.Snapshot(),
                routeMatched = _routeMatched.Snapshot(),
                routeMiss = _routeMiss.Snapshot(),
                forwardPlanned = _forwardPlanned.Snapshot(),
                queueEnqueued = _queueEnqueued.Snapshot(),
                queueDropped = _queueDropped.Snapshot(),
                sendAttempt = _sendAttempt.Snapshot(),
                sendSuccess = _sendSuccess.Snapshot(),
                sendFail = _sendFail.Snapshot(),
                retry = _retry.Snapshot(),
                failureReasons = new
                {
                    noRoute = _noRoute.Snapshot(),
                    noTarget = _noTarget.Snapshot(),
                    queueFull = _queueFull.Snapshot(),
                    noBufferSpace = _noBufferSpace.Snapshot(),
                    hostUnreachable = _hostUnreachable.Snapshot(),
                    networkUnreachable = _networkUnreachable.Snapshot(),
                    timedOut = _timedOut.Snapshot(),
                    socketError = _socketError.Snapshot(),
                    unknown = _unknownError.Snapshot()
                }
            };
        }

        internal void Tick()
        {
            _received.Tick();
            _routeMatched.Tick();
            _routeMiss.Tick();
            _forwardPlanned.Tick();
            _queueEnqueued.Tick();
            _queueDropped.Tick();
            _sendAttempt.Tick();
            _sendSuccess.Tick();
            _sendFail.Tick();
            _retry.Tick();

            _noRoute.Tick();
            _noTarget.Tick();
            _queueFull.Tick();
            _noBufferSpace.Tick();
            _hostUnreachable.Tick();
            _networkUnreachable.Tick();
            _timedOut.Tick();
            _socketError.Tick();
            _unknownError.Tick();
        }

        private void Touch()
        {
            Volatile.Write(ref _lastSeenUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        private static string ToWireMediaName(UdpLinkMediaKind mediaKind)
        {
            return mediaKind switch
            {
                UdpLinkMediaKind.Video => "video",
                UdpLinkMediaKind.Pose => "pose",
                UdpLinkMediaKind.Audio => "audio",
                UdpLinkMediaKind.TelemetryLowRate => "telemetry_low_rate",
                UdpLinkMediaKind.TelemetryHighRate => "telemetry_high_rate",
                UdpLinkMediaKind.Feedback => "feedback",
                UdpLinkMediaKind.SystemMonitor => "system_monitor",
                UdpLinkMediaKind.TopologyMonitor => "topology_monitor",
                _ => "unknown"
            };
        }

        private UdpEventCounter GetFailureCounter(UdpLinkFailureKind reason)
        {
            return reason switch
            {
                UdpLinkFailureKind.NoRoute => _noRoute,
                UdpLinkFailureKind.NoTarget => _noTarget,
                UdpLinkFailureKind.QueueFull => _queueFull,
                UdpLinkFailureKind.NoBufferSpace => _noBufferSpace,
                UdpLinkFailureKind.HostUnreachable => _hostUnreachable,
                UdpLinkFailureKind.NetworkUnreachable => _networkUnreachable,
                UdpLinkFailureKind.TimedOut => _timedOut,
                UdpLinkFailureKind.SocketError => _socketError,
                _ => _unknownError
            };
        }

        private static UdpLinkFailureKind MapSocketError(SocketError error)
        {
            return error switch
            {
                SocketError.NoBufferSpaceAvailable => UdpLinkFailureKind.NoBufferSpace,
                SocketError.HostUnreachable => UdpLinkFailureKind.HostUnreachable,
                SocketError.NetworkUnreachable => UdpLinkFailureKind.NetworkUnreachable,
                SocketError.TimedOut => UdpLinkFailureKind.TimedOut,
                SocketError.SocketError => UdpLinkFailureKind.SocketError,
                _ => UdpLinkFailureKind.Unknown
            };
        }
    }

    internal sealed class UdpTrafficCounter
    {
        private long _packetsTotal;
        private long _bytesTotal;
        private long _packetsThisSecond;
        private long _bytesThisSecond;
        private long _lastPacketsPerSecond;
        private long _lastBytesPerSecond;

        public void Add(int bytes)
        {
            Interlocked.Increment(ref _packetsTotal);
            Interlocked.Add(ref _bytesTotal, bytes);
            Interlocked.Increment(ref _packetsThisSecond);
            Interlocked.Add(ref _bytesThisSecond, bytes);
        }

        public object Snapshot()
        {
            return new
            {
                perSecond = new
                {
                    packets = Volatile.Read(ref _lastPacketsPerSecond),
                    bytes = Volatile.Read(ref _lastBytesPerSecond)
                },
                totals = new
                {
                    packets = Interlocked.Read(ref _packetsTotal),
                    bytes = Interlocked.Read(ref _bytesTotal)
                }
            };
        }

        public void Tick()
        {
            _lastPacketsPerSecond = Interlocked.Exchange(ref _packetsThisSecond, 0);
            _lastBytesPerSecond = Interlocked.Exchange(ref _bytesThisSecond, 0);
        }

        internal bool HasObserved => Interlocked.Read(ref _packetsTotal) > 0 || Interlocked.Read(ref _bytesTotal) > 0;
    }

    internal sealed class UdpEventCounter
    {
        private long _total;
        private long _thisSecond;
        private long _lastPerSecond;

        public void Add()
        {
            Interlocked.Increment(ref _total);
            Interlocked.Increment(ref _thisSecond);
        }

        public object Snapshot()
        {
            return new
            {
                perSecond = Volatile.Read(ref _lastPerSecond),
                total = Interlocked.Read(ref _total)
            };
        }

        public void Tick()
        {
            _lastPerSecond = Interlocked.Exchange(ref _thisSecond, 0);
        }

        internal bool HasObserved => Interlocked.Read(ref _total) > 0;
    }
}