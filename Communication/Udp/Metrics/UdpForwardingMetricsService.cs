using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace GrpcHttp3Demo.Communication.Udp.Metrics
{
    public readonly record struct ForwardEdgeKey(string PublisherSessionId, string TargetSessionId);

    public sealed class ForwardEdgeCounter
    {
        private long _videoPacketsTotal;
        private long _videoBytesTotal;
        private long _posePacketsTotal;
        private long _poseBytesTotal;
        private long _audioPacketsTotal;
        private long _audioBytesTotal;
        private long _telemetryPacketsTotal;
        private long _telemetryBytesTotal;
        private long _feedbackPacketsTotal;
        private long _feedbackBytesTotal;

        private long _videoPacketsThisSecond;
        private long _videoBytesThisSecond;
        private long _posePacketsThisSecond;
        private long _poseBytesThisSecond;
        private long _audioPacketsThisSecond;
        private long _audioBytesThisSecond;
        private long _telemetryPacketsThisSecond;
        private long _telemetryBytesThisSecond;
        private long _feedbackPacketsThisSecond;
        private long _feedbackBytesThisSecond;

        private long _lastVideoPps;
        private long _lastVideoBps;
        private long _lastPosePps;
        private long _lastPoseBps;
        private long _lastAudioPps;
        private long _lastAudioBps;
        private long _lastTelemetryPps;
        private long _lastTelemetryBps;
        private long _lastFeedbackPps;
        private long _lastFeedbackBps;

        internal void Tick()
        {
            _lastVideoPps = Interlocked.Exchange(ref _videoPacketsThisSecond, 0);
            _lastVideoBps = Interlocked.Exchange(ref _videoBytesThisSecond, 0);
            _lastPosePps = Interlocked.Exchange(ref _posePacketsThisSecond, 0);
            _lastPoseBps = Interlocked.Exchange(ref _poseBytesThisSecond, 0);
            _lastAudioPps = Interlocked.Exchange(ref _audioPacketsThisSecond, 0);
            _lastAudioBps = Interlocked.Exchange(ref _audioBytesThisSecond, 0);
            _lastTelemetryPps = Interlocked.Exchange(ref _telemetryPacketsThisSecond, 0);
            _lastTelemetryBps = Interlocked.Exchange(ref _telemetryBytesThisSecond, 0);
            _lastFeedbackPps = Interlocked.Exchange(ref _feedbackPacketsThisSecond, 0);
            _lastFeedbackBps = Interlocked.Exchange(ref _feedbackBytesThisSecond, 0);
        }

        public void RecordVideo(int bytes)
        {
            Interlocked.Increment(ref _videoPacketsTotal);
            Interlocked.Add(ref _videoBytesTotal, bytes);
            Interlocked.Increment(ref _videoPacketsThisSecond);
            Interlocked.Add(ref _videoBytesThisSecond, bytes);
        }

        public void RecordPose(int bytes)
        {
            Interlocked.Increment(ref _posePacketsTotal);
            Interlocked.Add(ref _poseBytesTotal, bytes);
            Interlocked.Increment(ref _posePacketsThisSecond);
            Interlocked.Add(ref _poseBytesThisSecond, bytes);
        }

        public void RecordAudio(int bytes)
        {
            Interlocked.Increment(ref _audioPacketsTotal);
            Interlocked.Add(ref _audioBytesTotal, bytes);
            Interlocked.Increment(ref _audioPacketsThisSecond);
            Interlocked.Add(ref _audioBytesThisSecond, bytes);
        }

        public void RecordTelemetry(int bytes)
        {
            Interlocked.Increment(ref _telemetryPacketsTotal);
            Interlocked.Add(ref _telemetryBytesTotal, bytes);
            Interlocked.Increment(ref _telemetryPacketsThisSecond);
            Interlocked.Add(ref _telemetryBytesThisSecond, bytes);
        }

        public void RecordFeedback(int bytes)
        {
            Interlocked.Increment(ref _feedbackPacketsTotal);
            Interlocked.Add(ref _feedbackBytesTotal, bytes);
            Interlocked.Increment(ref _feedbackPacketsThisSecond);
            Interlocked.Add(ref _feedbackBytesThisSecond, bytes);
        }

        public object Snapshot()
        {
            return new
            {
                perSecond = new
                {
                    videoPps = Interlocked.Read(ref _lastVideoPps),
                    videoBps = Interlocked.Read(ref _lastVideoBps),
                    posePps = Interlocked.Read(ref _lastPosePps),
                    poseBps = Interlocked.Read(ref _lastPoseBps),
                    audioPps = Interlocked.Read(ref _lastAudioPps),
                    audioBps = Interlocked.Read(ref _lastAudioBps),
                    telemetryPps = Interlocked.Read(ref _lastTelemetryPps),
                    telemetryBps = Interlocked.Read(ref _lastTelemetryBps),
                    feedbackPps = Interlocked.Read(ref _lastFeedbackPps),
                    feedbackBps = Interlocked.Read(ref _lastFeedbackBps)
                },
                totals = new
                {
                    videoPackets = Interlocked.Read(ref _videoPacketsTotal),
                    videoBytes = Interlocked.Read(ref _videoBytesTotal),
                    posePackets = Interlocked.Read(ref _posePacketsTotal),
                    poseBytes = Interlocked.Read(ref _poseBytesTotal),
                    audioPackets = Interlocked.Read(ref _audioPacketsTotal),
                    audioBytes = Interlocked.Read(ref _audioBytesTotal),
                    telemetryPackets = Interlocked.Read(ref _telemetryPacketsTotal),
                    telemetryBytes = Interlocked.Read(ref _telemetryBytesTotal),
                    feedbackPackets = Interlocked.Read(ref _feedbackPacketsTotal),
                    feedbackBytes = Interlocked.Read(ref _feedbackBytesTotal)
                }
            };
        }
    }

    public readonly struct UdpForwardTarget
    {
        public IPEndPoint Endpoint { get; }
        public string TargetSessionId { get; }
        public ForwardEdgeCounter Counter { get; }

        public UdpForwardTarget(IPEndPoint endpoint, string targetSessionId, ForwardEdgeCounter counter)
        {
            Endpoint = endpoint;
            TargetSessionId = targetSessionId;
            Counter = counter;
        }
    }

    public readonly struct UdpFeedbackForwardTarget
    {
        public IPEndPoint RobotEndpoint { get; }
        public string RobotSessionId { get; }
        public ForwardEdgeCounter Counter { get; }

        public UdpFeedbackForwardTarget(IPEndPoint robotEndpoint, string robotSessionId, ForwardEdgeCounter counter)
        {
            RobotEndpoint = robotEndpoint;
            RobotSessionId = robotSessionId;
            Counter = counter;
        }
    }

    public sealed class UdpSourceRoute
    {
        private long _nextDataActivityTickMs;
        private long _nextDackTickMs;

        public UdpSourceRoute(
            string sourceSessionId,
            IPEndPoint sourceEndpoint,
            ImmutableArray<UdpForwardTarget> videoTargets,
            ImmutableArray<UdpForwardTarget> poseTargets,
            ImmutableArray<UdpForwardTarget> audioTargets,
            ImmutableArray<UdpForwardTarget> telemetryLowRateTargets,
            ImmutableArray<UdpForwardTarget> telemetryHighRateTargets)
        {
            SourceSessionId = sourceSessionId;
            SourceEndpoint = sourceEndpoint;
            VideoTargets = videoTargets;
            PoseTargets = poseTargets;
            AudioTargets = audioTargets;
            TelemetryLowRateTargets = telemetryLowRateTargets;
            TelemetryHighRateTargets = telemetryHighRateTargets;
        }

        public string SourceSessionId { get; }
        public IPEndPoint SourceEndpoint { get; }
        public ImmutableArray<UdpForwardTarget> VideoTargets { get; }
        public ImmutableArray<UdpForwardTarget> PoseTargets { get; }
        public ImmutableArray<UdpForwardTarget> AudioTargets { get; }
        public ImmutableArray<UdpForwardTarget> TelemetryLowRateTargets { get; }
        public ImmutableArray<UdpForwardTarget> TelemetryHighRateTargets { get; }

        public bool TryMarkDataActivityDue(long nowTickMs, long intervalMs)
        {
            return TryAdvanceDue(ref _nextDataActivityTickMs, nowTickMs, intervalMs);
        }

        public bool TryMarkDackDue(long nowTickMs, long intervalMs)
        {
            return TryAdvanceDue(ref _nextDackTickMs, nowTickMs, intervalMs);
        }

        private static bool TryAdvanceDue(ref long nextTickMs, long nowTickMs, long intervalMs)
        {
            var next = Volatile.Read(ref nextTickMs);
            if (nowTickMs < next)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref nextTickMs, nowTickMs + intervalMs, next) == next;
        }
    }

    public sealed class UdpForwardingMetricsService : IDisposable
    {
        private readonly ILogger<UdpForwardingMetricsService> _logger;
        private readonly ConcurrentDictionary<ForwardEdgeKey, ForwardEdgeCounter> _edges = new();
        private readonly Timer _timer;
        private DateTime _lastTickUtc;

        public UdpForwardingMetricsService(ILogger<UdpForwardingMetricsService> logger)
        {
            _logger = logger;
            _lastTickUtc = DateTime.UtcNow;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public ForwardEdgeCounter GetOrCreateEdge(string publisherSessionId, string targetSessionId)
        {
            return _edges.GetOrAdd(new ForwardEdgeKey(publisherSessionId, targetSessionId), _ => new ForwardEdgeCounter());
        }

        public bool TryGetEdge(string publisherSessionId, string targetSessionId, out ForwardEdgeCounter? counter)
        {
            if (_edges.TryGetValue(new ForwardEdgeKey(publisherSessionId, targetSessionId), out var c))
            {
                counter = c;
                return true;
            }

            counter = null;
            return false;
        }

        private void Tick()
        {
            try
            {
                foreach (var edge in _edges.Values)
                {
                    edge.Tick();
                }

                _lastTickUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP forwarding metrics tick failed");
            }
        }

        public DateTime LastTickUtc => _lastTickUtc;

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}

