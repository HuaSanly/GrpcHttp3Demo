using System.Collections.Generic;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionPresence
    {
        private readonly SessionMemoryStore _memory;
        private readonly PushChannelRegistry _pushChannels;
        private readonly SessionRegistry _registry;
        private readonly NotificationService _notifications;
        private readonly SessionLivenessOptions _options;

        public SessionPresence(SessionMemoryStore memory, PushChannelRegistry pushChannels, SessionRegistry registry, NotificationService notifications, SessionLivenessOptions options)
        {
            _memory = memory;
            _pushChannels = pushChannels;
            _registry = registry;
            _notifications = notifications;
            _options = options;
        }

        public void UpdateHeartbeat(string sessionId)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                context.LastHeartbeatUtc = DateTime.UtcNow;
                context.GrpcRescueCount = 0;
            }
        }

        public void MarkTransportConnected(string sessionId)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                context.LastTransportConnectedUtc = DateTime.UtcNow;
                context.LastTransportDisconnectedUtc = DateTime.MinValue;
                context.LastTransportDisconnectReason = null;
                context.GrpcRescueCount = 0;
            }
        }

        public void MarkTransportDisconnected(string sessionId, string reason)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                if (context.LastTransportDisconnectedUtc == DateTime.MinValue)
                {
                    context.LastTransportDisconnectedUtc = DateTime.UtcNow;
                }

                context.LastTransportDisconnectReason = reason;
            }
        }

        public bool IsSessionOnline(string sessionId, DeviceContext context, TimeSpan onlineTimeout, DateTime? now = null)
        {
            var current = now ?? DateTime.UtcNow;
            if (current - context.LastHeartbeatUtc > onlineTimeout)
            {
                return false;
            }

            if (context.LastTransportConnectedUtc == DateTime.MinValue)
            {
                return true;
            }

            return _pushChannels.IsConnected(sessionId);
        }

        public object GetOnlineRoleSnapshot(TimeSpan onlineTimeout)
        {
            var now = DateTime.UtcNow;
            var totalRegistered = 0;
            var totalOnline = 0;
            var robotOnline = 0;
            var vrOnline = 0;
            var clientOnline = 0;
            var unknownOnline = 0;
            var pushConnectedOnline = 0;

            foreach (var item in _memory.Sessions)
            {
                totalRegistered++;
                var context = item.Value;

                if (!IsSessionOnline(item.Key, context, onlineTimeout, now))
                {
                    continue;
                }

                totalOnline++;
                if (_pushChannels.IsConnected(item.Key)) pushConnectedOnline++;

                switch (context.Role)
                {
                    case RegisterRequest.Types.EndpointType.Robot:
                        robotOnline++;
                        break;
                    case RegisterRequest.Types.EndpointType.Vr:
                        vrOnline++;
                        break;
                    case RegisterRequest.Types.EndpointType.Client:
                        clientOnline++;
                        break;
                    default:
                        unknownOnline++;
                        break;
                }
            }

            return new
            {
                timeoutSeconds = (int)Math.Max(1, onlineTimeout.TotalSeconds),
                registered = totalRegistered,
                online = totalOnline,
                onlineByRole = new
                {
                    robot = robotOnline,
                    vr = vrOnline,
                    client = clientOnline,
                    unknown = unknownOnline
                },
                pushConnectedOnline
            };
        }

        public async Task CheckAndRescueSessionsAsync()
        {
            var now = DateTime.UtcNow;
            var lostSessions = new List<(string SessionId, string Reason)>();

            foreach (var item in _memory.Sessions)
            {
                var sessionId = item.Key;
                var context = item.Value;
                var heartbeatHealthy = now - context.LastHeartbeatUtc <= _options.Timeout;
                var pushConnected = _pushChannels.IsConnected(sessionId);
                var transportObserved = context.LastTransportConnectedUtc != DateTime.MinValue;

                if (pushConnected)
                {
                    if (context.LastTransportDisconnectedUtc != DateTime.MinValue)
                    {
                        context.LastTransportDisconnectedUtc = DateTime.MinValue;
                        context.LastTransportDisconnectReason = null;
                    }

                    if (!heartbeatHealthy)
                    {
                        lostSessions.Add((sessionId, "heartbeat_timeout"));
                    }

                    continue;
                }

                if (!transportObserved)
                {
                    if (!heartbeatHealthy)
                    {
                        lostSessions.Add((sessionId, "heartbeat_timeout"));
                    }

                    continue;
                }

                if (context.LastTransportDisconnectedUtc == DateTime.MinValue)
                {
                    context.LastTransportDisconnectedUtc = now;
                    context.LastTransportDisconnectReason ??= "transport_disconnected";
                    continue;
                }

                if (now - context.LastTransportDisconnectedUtc <= _options.Timeout)
                {
                    continue;
                }

                lostSessions.Add((sessionId, context.LastTransportDisconnectReason ?? "transport_timeout"));
            }

            foreach (var lost in lostSessions)
            {
                await TerminateSessionAsync(lost.SessionId, lost.Reason);
            }
        }

        private async Task TerminateSessionAsync(string sessionId, string reason)
        {
            if (_memory.Pairings.TryGetValue(sessionId, out var partnerSessionId) && !string.IsNullOrEmpty(partnerSessionId))
            {
                await _notifications.SendUnpairAsync(sessionId, partnerSessionId);
            }

            Console.WriteLine($"[SessionPresence] Session terminated: {sessionId}, reason={reason}");
            _registry.UnregisterGrpc(sessionId);
        }
    }
}