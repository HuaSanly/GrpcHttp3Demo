using System.Net;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Communication.Udp.Binding
{
    public sealed class UdpSessionBindingService
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionRouting _routing;
        private readonly SessionSubscription _subscriptions;
        private readonly PushChannelRegistry _pushChannels;

        public UdpSessionBindingService(SessionMemoryStore memory, SessionRouting routing, SessionSubscription subscriptions, PushChannelRegistry pushChannels)
        {
            _memory = memory;
            _routing = routing;
            _subscriptions = subscriptions;
            _pushChannels = pushChannels;
        }

        public void RegisterEndpoint(string sessionId, IPEndPoint endpoint)
        {
            if (!_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                Console.WriteLine($"[UDP] Warning: UDP register for unknown session {sessionId}");
                return;
            }

            context.LastUdpControlUtc = DateTime.UtcNow;
            context.UdpRescueCount = 0;

            if (context.UdpEndpoint != null && context.UdpEndpoint.Equals(endpoint))
            {
                return;
            }

            if (context.UdpEndpoint != null)
            {
                _memory.EndpointIndex.TryRemove(context.UdpEndpoint, out _);
                _memory.ForwardingTable.TryRemove(context.UdpEndpoint, out _);
                _memory.PoseForwardingTable.TryRemove(context.UdpEndpoint, out _);
                _memory.AudioForwardingTable.TryRemove(context.UdpEndpoint, out _);
                _memory.SourceRouteTable.TryRemove(context.UdpEndpoint, out _);
                _memory.SessionEndpointIndex.TryRemove(sessionId, out _);
            }

            context.UdpEndpoint = endpoint;
            _memory.EndpointIndex[endpoint] = sessionId;
            _memory.SessionEndpointIndex[sessionId] = endpoint;

            _routing.RefreshFeedbackRoute(sessionId);
            if (_memory.Pairings.TryGetValue(sessionId, out var partnerSessionId)) _routing.RefreshFeedbackRoute(partnerSessionId);

            _routing.RebuildForwardingForPublisher(sessionId);
            _routing.RebuildForwardingForSubscriber(sessionId);
            _subscriptions.RebuildSystemMonitorTargets();
            _subscriptions.RebuildLinkMonitorTargets();

            Console.WriteLine($"[UDP] Registered endpoint: session {sessionId} -> {endpoint}");
        }

        public void UpdateDataActivity(string sessionId)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                context.LastUdpDataUtc = DateTime.UtcNow;
            }
        }

        public void CheckAndRescueMappings(TimeSpan controlTimeout, TimeSpan rescueCooldown, int maxRescues)
        {
            var now = DateTime.UtcNow;

            foreach (var item in _memory.Sessions)
            {
                var sessionId = item.Key;
                var context = item.Value;

                if (context.UdpEndpoint == null) continue;
                if (context.LastUdpControlUtc == DateTime.MinValue) continue;
                if (now - context.LastUdpControlUtc <= controlTimeout) continue;

                var oldEndpoint = context.UdpEndpoint;
                context.UdpEndpoint = null;

                if (oldEndpoint != null)
                {
                    _memory.EndpointIndex.TryRemove(oldEndpoint, out _);
                    _memory.ForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.PoseForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.AudioForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.SourceRouteTable.TryRemove(oldEndpoint, out _);
                    _memory.FeedbackRoute.TryRemove(oldEndpoint, out _);
                }

                _routing.RemoveFeedbackRoute(sessionId);

                _memory.SessionEndpointIndex.TryRemove(sessionId, out _);
                _subscriptions.RebuildSystemMonitorTargets();
                _subscriptions.RebuildLinkMonitorTargets();

                if (!_pushChannels.IsConnected(sessionId)) continue;
                if (context.UdpRescueCount >= maxRescues) continue;
                if (context.LastUdpRescueUtc != DateTime.MinValue && now - context.LastUdpRescueUtc < rescueCooldown) continue;

                context.UdpRescueCount++;
                context.LastUdpRescueUtc = now;

                var command = new EventMessage
                {
                    TargetSessionId = sessionId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    System = new SystemCommand { Action = SystemCommand.Types.Action.RequestUdpHello }
                };

                _ = _pushChannels.SendEventAsync(sessionId, command);
            }
        }
    }
}