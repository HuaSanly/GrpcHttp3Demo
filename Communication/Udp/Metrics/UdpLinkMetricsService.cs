using System.Collections.Concurrent;
using System.Net.Sockets;
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

        public void DeactivateLinksForOrigin(string originSessionId)
        {
            foreach (var link in _links.Values)
            {
                if (string.Equals(link.OriginSessionId, originSessionId, StringComparison.Ordinal))
                {
                    link.SetActive(false);
                }
            }
        }

        public void DeactivateLink(UdpLinkKey key)
        {
            if (_links.TryGetValue(key, out var link))
            {
                link.SetActive(false);
            }
        }

        public IReadOnlyCollection<object> SnapshotAll(bool activeOnly)
        {
            return _links.Values
                .Where(link => !activeOnly || link.Active)
                .OrderBy(link => link.OriginSessionId, StringComparer.Ordinal)
                .ThenBy(link => link.Direction)
                .ThenBy(link => link.MediaKind)
                .ThenBy(link => link.TargetNodeId, StringComparer.Ordinal)
                .Select(link => link.Snapshot())
                .ToArray<object>();
        }

        public IReadOnlyCollection<object> SnapshotByLinkId(string? linkId, bool activeOnly)
        {
            if (string.IsNullOrWhiteSpace(linkId))
            {
                return Array.Empty<object>();
            }

            return _links.Values
                .Where(link => string.Equals(link.LinkId, linkId, StringComparison.Ordinal) && (!activeOnly || link.Active))
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

        private UdpRuntimeLink GetOrCreate(UdpLinkKey key)
        {
            var link = _links.GetOrAdd(key, static k => new UdpRuntimeLink(k));
            link.SetActive(true);
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

        private long _active = 1;
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
        public bool Active => Volatile.Read(ref _active) == 1;

        public void SetActive(bool active)
        {
            Volatile.Write(ref _active, active ? 1L : 0L);
        }

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
                active = Active,
                firstSeenUtc = FirstSeenUtc,
                lastSeenUtc = DateTimeOffset.FromUnixTimeMilliseconds(Volatile.Read(ref _lastSeenUnixMs)).UtcDateTime,
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
    }
}