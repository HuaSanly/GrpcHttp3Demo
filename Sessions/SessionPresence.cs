using System.Collections.Generic;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionPresence
    {
        private readonly SessionMemoryStore _memory;
        private readonly PushChannelRegistry _pushChannels;
        private readonly SessionRegistry _registry;

        public SessionPresence(SessionMemoryStore memory, PushChannelRegistry pushChannels, SessionRegistry registry)
        {
            _memory = memory;
            _pushChannels = pushChannels;
            _registry = registry;
        }

        public void UpdateHeartbeat(string sessionId)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                context.LastHeartbeatUtc = DateTime.UtcNow;
                context.GrpcRescueCount = 0;
            }
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

                if (now - context.LastHeartbeatUtc > onlineTimeout)
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

        public void CheckAndRescueSessions(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;
            var lostSessions = new List<string>();

            foreach (var item in _memory.Sessions)
            {
                var context = item.Value;
                if (now - context.LastHeartbeatUtc <= timeout) continue;

                if (_pushChannels.IsConnected(item.Key))
                {
                    if (context.GrpcRescueCount < 3)
                    {
                        context.GrpcRescueCount++;
                        Console.WriteLine($"[SessionPresence] Rescuing session {item.Key} (Attempt {context.GrpcRescueCount}) via push channel...");

                        var command = new EventMessage
                        {
                            TargetSessionId = item.Key,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            System = new SystemCommand { Action = SystemCommand.Types.Action.RequestPing }
                        };
                        _ = _pushChannels.SendEventAsync(item.Key, command);
                    }
                    else
                    {
                        lostSessions.Add(item.Key);
                    }
                }
                else
                {
                    lostSessions.Add(item.Key);
                }
            }

            foreach (var sessionId in lostSessions)
            {
                Console.WriteLine($"[SessionPresence] Session timed out: {sessionId}");
                _registry.UnregisterGrpc(sessionId);
            }
        }
    }
}