using System.Collections.Generic;
using System.Security.Cryptography;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionPairing
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionRuntimeProjection _projection;

        public SessionPairing(SessionMemoryStore memory, SessionRuntimeProjection projection)
        {
            _memory = memory;
            _projection = projection;
        }

        public byte[] GetOrCreateP2pSharedKey(string sessionA, string sessionB)
        {
            var key = CreatePairKey(sessionA, sessionB);
            return _memory.P2pSharedKeys.GetOrAdd(key, _ =>
            {
                var bytes = new byte[32];
                RandomNumberGenerator.Fill(bytes);
                return bytes;
            });
        }

        public string? GetPairedSession(string sessionId)
        {
            return _memory.Pairings.TryGetValue(sessionId, out var partnerSessionId) ? partnerSessionId : null;
        }

        public List<UnpairedEndpoint> GetUnpairedByRole(RegisterRequest.Types.EndpointType desiredRole)
        {
            var list = new List<UnpairedEndpoint>();
            foreach (var session in _memory.Sessions.Values)
            {
                if (session.Role != desiredRole) continue;
                if (_memory.Pairings.ContainsKey(session.SessionId)) continue;

                list.Add(new UnpairedEndpoint
                {
                    SessionId = session.SessionId,
                    DeviceId = session.DeviceId,
                    Role = session.Role,
                    RobotGeneration = session.RobotGeneration,
                    VrVersion = session.VrVersion
                });
            }

            return list;
        }

        public void PairSessions(string sessionA, string sessionB)
        {
            if (GetPairedSession(sessionA) == sessionB && GetPairedSession(sessionB) == sessionA)
            {
                return;
            }

            UnpairSession(sessionA);
            UnpairSession(sessionB);

            _memory.Pairings[sessionA] = sessionB;
            _memory.Pairings[sessionB] = sessionA;

            if (_memory.Sessions.TryGetValue(sessionA, out var contextA)) contextA.PairedDeviceId = ResolveDeviceId(sessionB);
            if (_memory.Sessions.TryGetValue(sessionB, out var contextB)) contextB.PairedDeviceId = ResolveDeviceId(sessionA);

            Console.WriteLine($"[SessionPairing] Paired sessions: {sessionA} <-> {sessionB}");

            _projection.OnPaired(sessionA, sessionB);
        }

        public void UnpairSession(string sessionId)
        {
            if (!_memory.Pairings.TryRemove(sessionId, out var partnerSessionId))
            {
                return;
            }

            _memory.Pairings.TryRemove(partnerSessionId, out _);
            RemoveP2pSharedKey(sessionId, partnerSessionId);

            if (_memory.Sessions.TryGetValue(sessionId, out var context)) context.PairedDeviceId = null;
            if (_memory.Sessions.TryGetValue(partnerSessionId, out var partner)) partner.PairedDeviceId = null;

            _projection.OnUnpaired(sessionId, partnerSessionId);

            Console.WriteLine($"[SessionPairing] Unpaired sessions: {sessionId} <-> {partnerSessionId}");
        }

        private static string CreatePairKey(string sessionA, string sessionB)
        {
            return string.CompareOrdinal(sessionA, sessionB) <= 0 ? $"{sessionA}|{sessionB}" : $"{sessionB}|{sessionA}";
        }

        private void RemoveP2pSharedKey(string sessionA, string sessionB)
        {
            _memory.P2pSharedKeys.TryRemove(CreatePairKey(sessionA, sessionB), out _);
        }

        private string? ResolveDeviceId(string sessionId)
        {
            return _memory.Sessions.TryGetValue(sessionId, out var session) ? session.DeviceId : null;
        }
    }
}