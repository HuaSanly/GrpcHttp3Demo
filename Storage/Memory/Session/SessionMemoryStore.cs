using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Communication.Udp.Metrics;

namespace GrpcHttp3Demo.Storage.Memory.Session
{
    public sealed class SessionMemoryStore
    {
        private readonly ConcurrentDictionary<string, DeviceContext> _sessions = new();
        private readonly ConcurrentDictionary<IPEndPoint, string> _endpointIndex = new();
        private readonly ConcurrentDictionary<string, IPEndPoint> _sessionEndpointIndex = new();
        private readonly ConcurrentDictionary<string, string> _pairings = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SubscriptionDetail>> _subscriptionDetails = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SubscriptionMeta>> _subscriptionMeta = new();
        private readonly ConcurrentDictionary<SessionIdentityKey, string> _identityIndex = new();
        private readonly ConcurrentDictionary<SessionIdentityKey, object> _identityGates = new();
        private readonly ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> _forwardingTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> _poseForwardingTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> _audioForwardingTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> _telemetryLowRateForwardingTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> _telemetryHighRateForwardingTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, UdpSourceRoute> _sourceRouteTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, UdpFeedbackForwardTarget> _feedbackRoute = new();
        private readonly ConcurrentDictionary<string, byte[]> _p2pSharedKeys = new();
        private UdpSystemMonitorTarget[] _systemMonitorTargets = Array.Empty<UdpSystemMonitorTarget>();
        private UdpTopologyMonitorTarget[] _linkMonitorTargets = Array.Empty<UdpTopologyMonitorTarget>();

        internal ConcurrentDictionary<string, DeviceContext> Sessions => _sessions;
        internal ConcurrentDictionary<IPEndPoint, string> EndpointIndex => _endpointIndex;
        internal ConcurrentDictionary<string, IPEndPoint> SessionEndpointIndex => _sessionEndpointIndex;
        internal ConcurrentDictionary<string, string> Pairings => _pairings;
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, SubscriptionDetail>> SubscriptionDetails => _subscriptionDetails;
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, SubscriptionMeta>> SubscriptionMeta => _subscriptionMeta;
        internal ConcurrentDictionary<SessionIdentityKey, string> IdentityIndex => _identityIndex;
        internal ConcurrentDictionary<SessionIdentityKey, object> IdentityGates => _identityGates;
        internal ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> ForwardingTable => _forwardingTable;
        internal ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> PoseForwardingTable => _poseForwardingTable;
        internal ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> AudioForwardingTable => _audioForwardingTable;
        internal ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> TelemetryLowRateForwardingTable => _telemetryLowRateForwardingTable;
        internal ConcurrentDictionary<IPEndPoint, ImmutableArray<UdpForwardTarget>> TelemetryHighRateForwardingTable => _telemetryHighRateForwardingTable;
        internal ConcurrentDictionary<IPEndPoint, UdpSourceRoute> SourceRouteTable => _sourceRouteTable;
        internal ConcurrentDictionary<IPEndPoint, UdpFeedbackForwardTarget> FeedbackRoute => _feedbackRoute;
        internal ConcurrentDictionary<string, byte[]> P2pSharedKeys => _p2pSharedKeys;
        internal UdpSystemMonitorTarget[] SystemMonitorTargets => Volatile.Read(ref _systemMonitorTargets);
        internal UdpTopologyMonitorTarget[] LinkMonitorTargets => Volatile.Read(ref _linkMonitorTargets);

        internal void SetSystemMonitorTargets(UdpSystemMonitorTarget[] targets)
        {
            Volatile.Write(ref _systemMonitorTargets, targets);
        }

        internal void SetLinkMonitorTargets(UdpTopologyMonitorTarget[] targets)
        {
            Volatile.Write(ref _linkMonitorTargets, targets);
        }

        public DeviceContext? GetSession(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var ctx) ? ctx : null;
        }

        public string? GetSessionByEndpoint(IPEndPoint endpoint)
        {
            return _endpointIndex.TryGetValue(endpoint, out var sessionId) ? sessionId : null;
        }

        public bool TryGetEndpointBySession(string sessionId, out IPEndPoint? endpoint)
        {
            endpoint = null;
            var ok = _sessionEndpointIndex.TryGetValue(sessionId, out var value);
            endpoint = value;
            return ok;
        }

        public bool TryGetForwardTargets(IPEndPoint sourceEndpoint, out ImmutableArray<UdpForwardTarget> targets)
        {
            return _forwardingTable.TryGetValue(sourceEndpoint, out targets);
        }

        public bool TryGetPoseForwardTargets(IPEndPoint sourceEndpoint, out ImmutableArray<UdpForwardTarget> targets)
        {
            return _poseForwardingTable.TryGetValue(sourceEndpoint, out targets);
        }

        public bool TryGetAudioForwardTargets(IPEndPoint sourceEndpoint, out ImmutableArray<UdpForwardTarget> targets)
        {
            return _audioForwardingTable.TryGetValue(sourceEndpoint, out targets);
        }

        public bool TryGetTelemetryLowRateForwardTargets(IPEndPoint sourceEndpoint, out ImmutableArray<UdpForwardTarget> targets)
        {
            return _telemetryLowRateForwardingTable.TryGetValue(sourceEndpoint, out targets);
        }

        public bool TryGetTelemetryHighRateForwardTargets(IPEndPoint sourceEndpoint, out ImmutableArray<UdpForwardTarget> targets)
        {
            return _telemetryHighRateForwardingTable.TryGetValue(sourceEndpoint, out targets);
        }

        public bool TryGetSourceRoute(IPEndPoint sourceEndpoint, out UdpSourceRoute? route)
        {
            return _sourceRouteTable.TryGetValue(sourceEndpoint, out route);
        }

        public bool TryGetFeedbackForward(IPEndPoint vrEndpoint, out UdpFeedbackForwardTarget target)
        {
            if (!_feedbackRoute.TryGetValue(vrEndpoint, out target))
            {
                target = default;
                return false;
            }
            return true;
        }

        public object Snapshot()
        {
            return new
            {
                sessions = _sessions.Count,
                endpointIndex = _endpointIndex.Count,
                sessionEndpointIndex = _sessionEndpointIndex.Count,
                pairings = _pairings.Count,
                publishersWithSubscriptionDetails = _subscriptionDetails.Count,
                publishersWithSubscriptions = _subscriptionMeta.Count,
                identityIndex = _identityIndex.Count,
                forwardingTable = _forwardingTable.Count,
                poseForwardingTable = _poseForwardingTable.Count,
                audioForwardingTable = _audioForwardingTable.Count,
                telemetryLowRateForwardingTable = _telemetryLowRateForwardingTable.Count,
                telemetryHighRateForwardingTable = _telemetryHighRateForwardingTable.Count,
                sourceRouteTable = _sourceRouteTable.Count,
                systemMonitorTargets = SystemMonitorTargets.Length,
                linkMonitorTargets = LinkMonitorTargets.Length,
                feedbackRoute = _feedbackRoute.Count,
                p2pSharedKeys = _p2pSharedKeys.Count
            };
        }
    }
}
