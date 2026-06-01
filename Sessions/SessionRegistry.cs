using System.Linq;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionRegistry
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionPairing _pairing;
        private readonly PushChannelRegistry _pushChannels;
        private readonly SessionRuntimeProjection _projection;

        public SessionRegistry(SessionMemoryStore memory, SessionPairing pairing, PushChannelRegistry pushChannels, SessionRuntimeProjection projection)
        {
            _memory = memory;
            _pairing = pairing;
            _pushChannels = pushChannels;
            _projection = projection;
        }

        public DeviceContext? GetSession(string sessionId)
        {
            return _memory.GetSession(sessionId);
        }

        public void RegisterGrpc(string sessionId, string deviceId, RegisterRequest.Types.EndpointType role, string clientIp, int clientPort, int robotGeneration, string? vrVersion)
        {
            vrVersion ??= string.Empty;

            var identityKey = SessionIdentityKey.From(deviceId, role);
            if (identityKey.IsValid)
            {
                var gate = _memory.IdentityGates.GetOrAdd(identityKey, _ => new object());
                lock (gate)
                {
                    var duplicateSessions = _memory.Sessions
                        .Where(item =>
                            item.Key != sessionId &&
                            item.Value.Role == role &&
                            string.Equals(item.Value.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Key)
                        .ToArray();

                    foreach (var oldSessionId in duplicateSessions)
                    {
                        Console.WriteLine($"[SessionRegistry] Duplicate identity detected. Kicking old session: {oldSessionId} (device={deviceId} role={role})");
                        UnregisterGrpc(oldSessionId);
                    }

                    _memory.IdentityIndex[identityKey] = sessionId;
                    UpsertSession(sessionId, deviceId, role, clientIp, clientPort, robotGeneration, vrVersion);
                    return;
                }
            }

            UpsertSession(sessionId, deviceId, role, clientIp, clientPort, robotGeneration, vrVersion);
        }

        public void UnregisterGrpc(string sessionId)
        {
            var affectedPublishers = _memory.SubscriptionMeta
                .Where(item => item.Value.ContainsKey(sessionId))
                .Select(item => item.Key)
                .ToArray();

            if (!_memory.Sessions.TryRemove(sessionId, out var context))
            {
                return;
            }

            var identityKey = SessionIdentityKey.From(context.DeviceId, context.Role);
            if (identityKey.IsValid && _memory.IdentityIndex.TryGetValue(identityKey, out var owner) && owner == sessionId)
            {
                _memory.IdentityIndex.TryRemove(identityKey, out _);
            }

            _pushChannels.Detach(sessionId);
            _projection.DeleteAllLinksForSession(sessionId);
            _pairing.UnpairSession(sessionId);

            if (context.UdpEndpoint != null)
            {
                _memory.EndpointIndex.TryRemove(context.UdpEndpoint, out _);
                _memory.SourceRouteTable.TryRemove(context.UdpEndpoint, out _);
                _memory.FeedbackRoute.TryRemove(context.UdpEndpoint, out _);
            }

            _memory.SessionEndpointIndex.TryRemove(sessionId, out _);
            _memory.SubscriptionDetails.TryRemove(sessionId, out _);

            foreach (var publisherSessionId in affectedPublishers)
            {
                if (_memory.SubscriptionDetails.TryGetValue(publisherSessionId, out var details))
                {
                    details.TryRemove(sessionId, out _);
                }

                if (_memory.SubscriptionMeta.TryGetValue(publisherSessionId, out var meta))
                {
                    meta.TryRemove(sessionId, out _);
                }
            }

            _memory.SubscriptionMeta.TryRemove(sessionId, out _);

            foreach (var publisherSessionId in affectedPublishers)
            {
                _projection.ReconcilePublisherAfterSubscriberRemoved(publisherSessionId);
            }

            _projection.ReconcileMonitorTargets();

            Console.WriteLine($"[SessionRegistry] Unregistered session: {sessionId}");
        }

        private void UpsertSession(string sessionId, string deviceId, RegisterRequest.Types.EndpointType role, string clientIp, int clientPort, int robotGeneration, string vrVersion)
        {
            var context = _memory.Sessions.GetOrAdd(sessionId, _ => new DeviceContext
            {
                DeviceId = deviceId,
                SessionId = sessionId
            });

            context.SessionId = sessionId;
            context.DeviceId = deviceId;
            context.Role = role;
            context.RobotGeneration = robotGeneration;
            context.VrVersion = vrVersion;
            context.ClientIp = clientIp;
            context.ClientPort = clientPort;

            Console.WriteLine($"[SessionRegistry] gRPC Registered: session={sessionId} device={deviceId} role={role} gen={robotGeneration} vr={vrVersion}");
        }
    }
}