using System.Net;
using GrpcHttp3Demo.Communication.Udp.Metrics;

namespace GrpcHttp3Demo.Models.Udp
{
    public sealed class UdpLinkMonitorTarget
    {
        private long _nextDueTickMs;
        private long _nextSequence;

        public UdpLinkMonitorTarget(string sessionId, IPEndPoint endpoint, int intervalMs, string linkId, UdpRuntimeLink link)
        {
            SessionId = sessionId;
            Endpoint = endpoint;
            IntervalMs = Math.Clamp(intervalMs, 250, 10_000);
            LinkId = linkId;
            Link = link;
        }

        public string SessionId { get; }
        public IPEndPoint Endpoint { get; }
        public int IntervalMs { get; }
        public string LinkId { get; }
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
            return Interlocked.Increment(ref _nextSequence);
        }
    }
}