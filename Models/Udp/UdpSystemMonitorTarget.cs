using System.Net;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Session;

namespace GrpcHttp3Demo.Models.Udp
{
    public sealed class UdpSystemMonitorTarget
    {
        private long _nextDueTickMs;
        private long _nextMonitorSequence;

        public UdpSystemMonitorTarget(string sessionId, IPEndPoint endpoint, SystemMonitorTopicMask topics, int intervalMs, UdpRuntimeLink link)
        {
            SessionId = sessionId;
            Endpoint = endpoint;
            Topics = topics;
            IntervalMs = Math.Clamp(intervalMs, 250, 10_000);
            Link = link;
        }

        public string SessionId { get; }
        public IPEndPoint Endpoint { get; }
        public SystemMonitorTopicMask Topics { get; }
        public int IntervalMs { get; }
        public UdpRuntimeLink Link { get; }

        public bool TryMarkDue(long nowTickMs)
        {
            var next = Volatile.Read(ref _nextDueTickMs);
            if (nowTickMs < next)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref _nextDueTickMs, nowTickMs + IntervalMs, next) == next;
        }

        public long NextSequence()
        {
            return Interlocked.Increment(ref _nextMonitorSequence);
        }
    }
}