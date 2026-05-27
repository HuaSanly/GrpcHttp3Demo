using System;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace GrpcHttp3Demo.Communication.Udp.Metrics
{
    public sealed class UdpMetricsService : IDisposable
    {
        private readonly ILogger<UdpMetricsService> _logger;
        private readonly Timer _timer;

        // RX
        private long _packetsTotal;
        private long _bytesTotal;

        private long _controlTotal;
        private long _videoTotal;
        private long _poseTotal;
        private long _audioTotal;
        private long _feedbackTotal;
        private long _systemTotal;
        private long _unknownTotal;

        private long _packetsThisSecond;
        private long _bytesThisSecond;

        private long _controlThisSecond;
        private long _videoThisSecond;
        private long _poseThisSecond;
        private long _audioThisSecond;
        private long _feedbackThisSecond;
        private long _systemThisSecond;
        private long _unknownThisSecond;

        private long _lastPacketsPerSecond;
        private long _lastBytesPerSecond;

        private long _lastControlPerSecond;
        private long _lastVideoPerSecond;
        private long _lastPosePerSecond;
        private long _lastAudioPerSecond;
        private long _lastFeedbackPerSecond;
        private long _lastSystemPerSecond;
        private long _lastUnknownPerSecond;

        // TX
        private long _txPacketsTotal;
        private long _txBytesTotal;

        private long _txControlTotal;
        private long _txVideoTotal;
        private long _txPoseTotal;
        private long _txAudioTotal;
        private long _txFeedbackTotal;
        private long _txSystemTotal;
        private long _txUnknownTotal;

        private long _txPacketsThisSecond;
        private long _txBytesThisSecond;

        private long _txControlThisSecond;
        private long _txVideoThisSecond;
        private long _txPoseThisSecond;
        private long _txAudioThisSecond;
        private long _txFeedbackThisSecond;
        private long _txSystemThisSecond;
        private long _txUnknownThisSecond;

        private long _txLastPacketsPerSecond;
        private long _txLastBytesPerSecond;

        private long _txLastControlPerSecond;
        private long _txLastVideoPerSecond;
        private long _txLastPosePerSecond;
        private long _txLastAudioPerSecond;
        private long _txLastFeedbackPerSecond;
        private long _txLastSystemPerSecond;
        private long _txLastUnknownPerSecond;

        // TX Success (actual send completed)
        private long _txOkPacketsTotal;
        private long _txOkBytesTotal;

        private long _txOkControlTotal;
        private long _txOkVideoTotal;
        private long _txOkPoseTotal;
        private long _txOkAudioTotal;
        private long _txOkFeedbackTotal;
        private long _txOkSystemTotal;
        private long _txOkUnknownTotal;

        private long _txOkPacketsThisSecond;
        private long _txOkBytesThisSecond;

        private long _txOkControlThisSecond;
        private long _txOkVideoThisSecond;
        private long _txOkPoseThisSecond;
        private long _txOkAudioThisSecond;
        private long _txOkFeedbackThisSecond;
        private long _txOkSystemThisSecond;
        private long _txOkUnknownThisSecond;

        private long _txOkLastPacketsPerSecond;
        private long _txOkLastBytesPerSecond;

        private long _txOkLastControlPerSecond;
        private long _txOkLastVideoPerSecond;
        private long _txOkLastPosePerSecond;
        private long _txOkLastAudioPerSecond;
        private long _txOkLastFeedbackPerSecond;
        private long _txOkLastSystemPerSecond;
        private long _txOkLastUnknownPerSecond;

        // TX Failure (send threw)
        private long _txFailPacketsTotal;
        private long _txFailBytesTotal;

        private long _txFailControlTotal;
        private long _txFailVideoTotal;
        private long _txFailPoseTotal;
        private long _txFailAudioTotal;
        private long _txFailFeedbackTotal;
        private long _txFailSystemTotal;
        private long _txFailUnknownTotal;

        private long _txFailPacketsThisSecond;
        private long _txFailBytesThisSecond;

        private long _txFailControlThisSecond;
        private long _txFailVideoThisSecond;
        private long _txFailPoseThisSecond;
        private long _txFailAudioThisSecond;
        private long _txFailFeedbackThisSecond;
        private long _txFailSystemThisSecond;
        private long _txFailUnknownThisSecond;

        private long _txFailLastPacketsPerSecond;
        private long _txFailLastBytesPerSecond;

        private long _txFailLastControlPerSecond;
        private long _txFailLastVideoPerSecond;
        private long _txFailLastPosePerSecond;
        private long _txFailLastAudioPerSecond;
        private long _txFailLastFeedbackPerSecond;
        private long _txFailLastSystemPerSecond;
        private long _txFailLastUnknownPerSecond;

        // Failure special: NoBufferSpaceAvailable
        private long _txFailNoBufferTotal;
        private long _txFailNoBufferThisSecond;
        private long _txFailNoBufferLastPerSecond;

        // Retry
        private long _txRetryTotal;
        private long _txRetryThisSecond;
        private long _txRetryLastPerSecond;

        // Forwarding queue drops (when bounded queue is full)
        private long _fwdQueueDropPacketsTotal;
        private long _fwdQueueDropBytesTotal;

        private long _fwdQueueDropPacketsThisSecond;
        private long _fwdQueueDropBytesThisSecond;

        private long _fwdQueueDropLastPacketsPerSecond;
        private long _fwdQueueDropLastBytesPerSecond;

        private long _fwdQueueDropVideoTotal;
        private long _fwdQueueDropPoseTotal;
        private long _fwdQueueDropAudioTotal;
        private long _fwdQueueDropFeedbackTotal;
        private long _fwdQueueDropSystemTotal;
        private long _fwdQueueDropUnknownTotal;

        private long _fwdQueueDropVideoThisSecond;
        private long _fwdQueueDropPoseThisSecond;
        private long _fwdQueueDropAudioThisSecond;
        private long _fwdQueueDropFeedbackThisSecond;
        private long _fwdQueueDropSystemThisSecond;
        private long _fwdQueueDropUnknownThisSecond;

        private long _fwdQueueDropLastVideoPerSecond;
        private long _fwdQueueDropLastPosePerSecond;
        private long _fwdQueueDropLastAudioPerSecond;
        private long _fwdQueueDropLastFeedbackPerSecond;
        private long _fwdQueueDropLastSystemPerSecond;
        private long _fwdQueueDropLastUnknownPerSecond;

        private DateTime _lastTickUtc;

        public UdpMetricsService(ILogger<UdpMetricsService> logger)
        {
            _logger = logger;
            _lastTickUtc = DateTime.UtcNow;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void RecordRxPacket(int lengthBytes, byte prefix)
        {
            Interlocked.Increment(ref _packetsTotal);
            Interlocked.Add(ref _bytesTotal, lengthBytes);

            Interlocked.Increment(ref _packetsThisSecond);
            Interlocked.Add(ref _bytesThisSecond, lengthBytes);

            if (prefix == (byte)'H' || prefix == (byte)'P')
            {
                Interlocked.Increment(ref _controlTotal);
                Interlocked.Increment(ref _controlThisSecond);
                return;
            }

            switch (prefix)
            {
                case 0x01:
                    Interlocked.Increment(ref _videoTotal);
                    Interlocked.Increment(ref _videoThisSecond);
                    return;
                case 0x02:
                    Interlocked.Increment(ref _poseTotal);
                    Interlocked.Increment(ref _poseThisSecond);
                    return;
                case 0x04:
                    Interlocked.Increment(ref _audioTotal);
                    Interlocked.Increment(ref _audioThisSecond);
                    return;
                case 0x03:
                    Interlocked.Increment(ref _feedbackTotal);
                    Interlocked.Increment(ref _feedbackThisSecond);
                    return;
                case 0x07:
                    Interlocked.Increment(ref _systemTotal);
                    Interlocked.Increment(ref _systemThisSecond);
                    return;
                default:
                    Interlocked.Increment(ref _unknownTotal);
                    Interlocked.Increment(ref _unknownThisSecond);
                    return;
            }
        }

        /// <summary>
        /// Backward-compatible TX counter (historically: "we attempted to send").
        /// Prefer <see cref="RecordTxAttempt"/> + <see cref="RecordTxSuccess"/>.
        /// </summary>
        public void RecordTxPacket(int lengthBytes, byte prefix)
        {
            RecordTxAttempt(lengthBytes, prefix);
        }

        public void RecordTxAttempt(int lengthBytes, byte prefix)
        {
            Interlocked.Increment(ref _txPacketsTotal);
            Interlocked.Add(ref _txBytesTotal, lengthBytes);

            Interlocked.Increment(ref _txPacketsThisSecond);
            Interlocked.Add(ref _txBytesThisSecond, lengthBytes);

            // TX 控制面：ACK/PONG/DACK 等。
            // - RX 控制面只统计 'H'/'P'
            // - TX 这里扩展到 'A'(ACK), 'P'(PONG), 'D'(DACK)
            if (prefix == (byte)'H' || prefix == (byte)'P' || prefix == (byte)'A' || prefix == (byte)'D')
            {
                Interlocked.Increment(ref _txControlTotal);
                Interlocked.Increment(ref _txControlThisSecond);
                return;
            }

            switch (prefix)
            {
                case 0x01:
                    Interlocked.Increment(ref _txVideoTotal);
                    Interlocked.Increment(ref _txVideoThisSecond);
                    return;
                case 0x02:
                    Interlocked.Increment(ref _txPoseTotal);
                    Interlocked.Increment(ref _txPoseThisSecond);
                    return;
                case 0x04:
                    Interlocked.Increment(ref _txAudioTotal);
                    Interlocked.Increment(ref _txAudioThisSecond);
                    return;
                case 0x03:
                    Interlocked.Increment(ref _txFeedbackTotal);
                    Interlocked.Increment(ref _txFeedbackThisSecond);
                    return;
                case 0x07:
                    Interlocked.Increment(ref _txSystemTotal);
                    Interlocked.Increment(ref _txSystemThisSecond);
                    return;
                default:
                    Interlocked.Increment(ref _txUnknownTotal);
                    Interlocked.Increment(ref _txUnknownThisSecond);
                    return;
            }
        }

        public void RecordTxSuccess(int lengthBytes, byte prefix)
        {
            Interlocked.Increment(ref _txOkPacketsTotal);
            Interlocked.Add(ref _txOkBytesTotal, lengthBytes);

            Interlocked.Increment(ref _txOkPacketsThisSecond);
            Interlocked.Add(ref _txOkBytesThisSecond, lengthBytes);

            if (prefix == (byte)'H' || prefix == (byte)'P' || prefix == (byte)'A' || prefix == (byte)'D')
            {
                Interlocked.Increment(ref _txOkControlTotal);
                Interlocked.Increment(ref _txOkControlThisSecond);
                return;
            }

            switch (prefix)
            {
                case 0x01:
                    Interlocked.Increment(ref _txOkVideoTotal);
                    Interlocked.Increment(ref _txOkVideoThisSecond);
                    return;
                case 0x02:
                    Interlocked.Increment(ref _txOkPoseTotal);
                    Interlocked.Increment(ref _txOkPoseThisSecond);
                    return;
                case 0x04:
                    Interlocked.Increment(ref _txOkAudioTotal);
                    Interlocked.Increment(ref _txOkAudioThisSecond);
                    return;
                case 0x03:
                    Interlocked.Increment(ref _txOkFeedbackTotal);
                    Interlocked.Increment(ref _txOkFeedbackThisSecond);
                    return;
                case 0x07:
                    Interlocked.Increment(ref _txOkSystemTotal);
                    Interlocked.Increment(ref _txOkSystemThisSecond);
                    return;
                default:
                    Interlocked.Increment(ref _txOkUnknownTotal);
                    Interlocked.Increment(ref _txOkUnknownThisSecond);
                    return;
            }
        }

        public void RecordTxFailure(int lengthBytes, byte prefix, SocketError error)
        {
            Interlocked.Increment(ref _txFailPacketsTotal);
            Interlocked.Add(ref _txFailBytesTotal, lengthBytes);

            Interlocked.Increment(ref _txFailPacketsThisSecond);
            Interlocked.Add(ref _txFailBytesThisSecond, lengthBytes);

            if (error == SocketError.NoBufferSpaceAvailable)
            {
                Interlocked.Increment(ref _txFailNoBufferTotal);
                Interlocked.Increment(ref _txFailNoBufferThisSecond);
            }

            if (prefix == (byte)'H' || prefix == (byte)'P' || prefix == (byte)'A' || prefix == (byte)'D')
            {
                Interlocked.Increment(ref _txFailControlTotal);
                Interlocked.Increment(ref _txFailControlThisSecond);
                return;
            }

            switch (prefix)
            {
                case 0x01:
                    Interlocked.Increment(ref _txFailVideoTotal);
                    Interlocked.Increment(ref _txFailVideoThisSecond);
                    return;
                case 0x02:
                    Interlocked.Increment(ref _txFailPoseTotal);
                    Interlocked.Increment(ref _txFailPoseThisSecond);
                    return;
                case 0x04:
                    Interlocked.Increment(ref _txFailAudioTotal);
                    Interlocked.Increment(ref _txFailAudioThisSecond);
                    return;
                case 0x03:
                    Interlocked.Increment(ref _txFailFeedbackTotal);
                    Interlocked.Increment(ref _txFailFeedbackThisSecond);
                    return;
                case 0x07:
                    Interlocked.Increment(ref _txFailSystemTotal);
                    Interlocked.Increment(ref _txFailSystemThisSecond);
                    return;
                default:
                    Interlocked.Increment(ref _txFailUnknownTotal);
                    Interlocked.Increment(ref _txFailUnknownThisSecond);
                    return;
            }
        }

        public void RecordTxRetry(int lengthBytes, byte prefix, SocketError error)
        {
            Interlocked.Increment(ref _txRetryTotal);
            Interlocked.Increment(ref _txRetryThisSecond);
        }

        public void RecordForwardQueueDrop(int lengthBytes, byte prefix)
        {
            Interlocked.Increment(ref _fwdQueueDropPacketsTotal);
            Interlocked.Add(ref _fwdQueueDropBytesTotal, lengthBytes);
            Interlocked.Increment(ref _fwdQueueDropPacketsThisSecond);
            Interlocked.Add(ref _fwdQueueDropBytesThisSecond, lengthBytes);

            switch (prefix)
            {
                case 0x01:
                    Interlocked.Increment(ref _fwdQueueDropVideoTotal);
                    Interlocked.Increment(ref _fwdQueueDropVideoThisSecond);
                    return;
                case 0x02:
                    Interlocked.Increment(ref _fwdQueueDropPoseTotal);
                    Interlocked.Increment(ref _fwdQueueDropPoseThisSecond);
                    return;
                case 0x04:
                    Interlocked.Increment(ref _fwdQueueDropAudioTotal);
                    Interlocked.Increment(ref _fwdQueueDropAudioThisSecond);
                    return;
                case 0x03:
                    Interlocked.Increment(ref _fwdQueueDropFeedbackTotal);
                    Interlocked.Increment(ref _fwdQueueDropFeedbackThisSecond);
                    return;
                case 0x07:
                    Interlocked.Increment(ref _fwdQueueDropSystemTotal);
                    Interlocked.Increment(ref _fwdQueueDropSystemThisSecond);
                    return;
                default:
                    Interlocked.Increment(ref _fwdQueueDropUnknownTotal);
                    Interlocked.Increment(ref _fwdQueueDropUnknownThisSecond);
                    return;
            }
        }

        private void Tick()
        {
            try
            {
                _lastPacketsPerSecond = Interlocked.Exchange(ref _packetsThisSecond, 0);
                _lastBytesPerSecond = Interlocked.Exchange(ref _bytesThisSecond, 0);

                _lastControlPerSecond = Interlocked.Exchange(ref _controlThisSecond, 0);
                _lastVideoPerSecond = Interlocked.Exchange(ref _videoThisSecond, 0);
                _lastPosePerSecond = Interlocked.Exchange(ref _poseThisSecond, 0);
                _lastAudioPerSecond = Interlocked.Exchange(ref _audioThisSecond, 0);
                _lastFeedbackPerSecond = Interlocked.Exchange(ref _feedbackThisSecond, 0);
                _lastSystemPerSecond = Interlocked.Exchange(ref _systemThisSecond, 0);
                _lastUnknownPerSecond = Interlocked.Exchange(ref _unknownThisSecond, 0);

                _txLastPacketsPerSecond = Interlocked.Exchange(ref _txPacketsThisSecond, 0);
                _txLastBytesPerSecond = Interlocked.Exchange(ref _txBytesThisSecond, 0);

                _txLastControlPerSecond = Interlocked.Exchange(ref _txControlThisSecond, 0);
                _txLastVideoPerSecond = Interlocked.Exchange(ref _txVideoThisSecond, 0);
                _txLastPosePerSecond = Interlocked.Exchange(ref _txPoseThisSecond, 0);
                _txLastAudioPerSecond = Interlocked.Exchange(ref _txAudioThisSecond, 0);
                _txLastFeedbackPerSecond = Interlocked.Exchange(ref _txFeedbackThisSecond, 0);
                _txLastSystemPerSecond = Interlocked.Exchange(ref _txSystemThisSecond, 0);
                _txLastUnknownPerSecond = Interlocked.Exchange(ref _txUnknownThisSecond, 0);

                _txOkLastPacketsPerSecond = Interlocked.Exchange(ref _txOkPacketsThisSecond, 0);
                _txOkLastBytesPerSecond = Interlocked.Exchange(ref _txOkBytesThisSecond, 0);
                _txOkLastControlPerSecond = Interlocked.Exchange(ref _txOkControlThisSecond, 0);
                _txOkLastVideoPerSecond = Interlocked.Exchange(ref _txOkVideoThisSecond, 0);
                _txOkLastPosePerSecond = Interlocked.Exchange(ref _txOkPoseThisSecond, 0);
                _txOkLastAudioPerSecond = Interlocked.Exchange(ref _txOkAudioThisSecond, 0);
                _txOkLastFeedbackPerSecond = Interlocked.Exchange(ref _txOkFeedbackThisSecond, 0);
                _txOkLastSystemPerSecond = Interlocked.Exchange(ref _txOkSystemThisSecond, 0);
                _txOkLastUnknownPerSecond = Interlocked.Exchange(ref _txOkUnknownThisSecond, 0);

                _txFailLastPacketsPerSecond = Interlocked.Exchange(ref _txFailPacketsThisSecond, 0);
                _txFailLastBytesPerSecond = Interlocked.Exchange(ref _txFailBytesThisSecond, 0);
                _txFailLastControlPerSecond = Interlocked.Exchange(ref _txFailControlThisSecond, 0);
                _txFailLastVideoPerSecond = Interlocked.Exchange(ref _txFailVideoThisSecond, 0);
                _txFailLastPosePerSecond = Interlocked.Exchange(ref _txFailPoseThisSecond, 0);
                _txFailLastAudioPerSecond = Interlocked.Exchange(ref _txFailAudioThisSecond, 0);
                _txFailLastFeedbackPerSecond = Interlocked.Exchange(ref _txFailFeedbackThisSecond, 0);
                _txFailLastSystemPerSecond = Interlocked.Exchange(ref _txFailSystemThisSecond, 0);
                _txFailLastUnknownPerSecond = Interlocked.Exchange(ref _txFailUnknownThisSecond, 0);
                _txFailNoBufferLastPerSecond = Interlocked.Exchange(ref _txFailNoBufferThisSecond, 0);

                _txRetryLastPerSecond = Interlocked.Exchange(ref _txRetryThisSecond, 0);

                _fwdQueueDropLastPacketsPerSecond = Interlocked.Exchange(ref _fwdQueueDropPacketsThisSecond, 0);
                _fwdQueueDropLastBytesPerSecond = Interlocked.Exchange(ref _fwdQueueDropBytesThisSecond, 0);
                _fwdQueueDropLastVideoPerSecond = Interlocked.Exchange(ref _fwdQueueDropVideoThisSecond, 0);
                _fwdQueueDropLastPosePerSecond = Interlocked.Exchange(ref _fwdQueueDropPoseThisSecond, 0);
                _fwdQueueDropLastAudioPerSecond = Interlocked.Exchange(ref _fwdQueueDropAudioThisSecond, 0);
                _fwdQueueDropLastFeedbackPerSecond = Interlocked.Exchange(ref _fwdQueueDropFeedbackThisSecond, 0);
                _fwdQueueDropLastSystemPerSecond = Interlocked.Exchange(ref _fwdQueueDropSystemThisSecond, 0);
                _fwdQueueDropLastUnknownPerSecond = Interlocked.Exchange(ref _fwdQueueDropUnknownThisSecond, 0);

                _lastTickUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP metrics tick failed");
            }
        }

        public object Snapshot()
        {
            return new
            {
                updatedUtc = _lastTickUtc,
                rx = new
                {
                    pps = _lastPacketsPerSecond,
                    bps = _lastBytesPerSecond,
                    perSecond = new
                    {
                        control = _lastControlPerSecond,
                        video = _lastVideoPerSecond,
                        pose = _lastPosePerSecond,
                        audio = _lastAudioPerSecond,
                        feedback = _lastFeedbackPerSecond,
                        system = _lastSystemPerSecond,
                        unknown = _lastUnknownPerSecond
                    },
                    totals = new
                    {
                        packets = Interlocked.Read(ref _packetsTotal),
                        bytes = Interlocked.Read(ref _bytesTotal),
                        control = Interlocked.Read(ref _controlTotal),
                        video = Interlocked.Read(ref _videoTotal),
                        pose = Interlocked.Read(ref _poseTotal),
                        audio = Interlocked.Read(ref _audioTotal),
                        feedback = Interlocked.Read(ref _feedbackTotal),
                        system = Interlocked.Read(ref _systemTotal),
                        unknown = Interlocked.Read(ref _unknownTotal)
                    }
                },
                tx = new
                {
                    pps = _txLastPacketsPerSecond,
                    bps = _txLastBytesPerSecond,
                    perSecond = new
                    {
                        control = _txLastControlPerSecond,
                        video = _txLastVideoPerSecond,
                        pose = _txLastPosePerSecond,
                        audio = _txLastAudioPerSecond,
                        feedback = _txLastFeedbackPerSecond,
                        system = _txLastSystemPerSecond,
                        unknown = _txLastUnknownPerSecond
                    },
                    totals = new
                    {
                        packets = Interlocked.Read(ref _txPacketsTotal),
                        bytes = Interlocked.Read(ref _txBytesTotal),
                        control = Interlocked.Read(ref _txControlTotal),
                        video = Interlocked.Read(ref _txVideoTotal),
                        pose = Interlocked.Read(ref _txPoseTotal),
                        audio = Interlocked.Read(ref _txAudioTotal),
                        feedback = Interlocked.Read(ref _txFeedbackTotal),
                        system = Interlocked.Read(ref _txSystemTotal),
                        unknown = Interlocked.Read(ref _txUnknownTotal)
                    }
                },
                txOk = new
                {
                    pps = _txOkLastPacketsPerSecond,
                    bps = _txOkLastBytesPerSecond,
                    perSecond = new
                    {
                        control = _txOkLastControlPerSecond,
                        video = _txOkLastVideoPerSecond,
                        pose = _txOkLastPosePerSecond,
                        audio = _txOkLastAudioPerSecond,
                        feedback = _txOkLastFeedbackPerSecond,
                        system = _txOkLastSystemPerSecond,
                        unknown = _txOkLastUnknownPerSecond
                    },
                    totals = new
                    {
                        packets = Interlocked.Read(ref _txOkPacketsTotal),
                        bytes = Interlocked.Read(ref _txOkBytesTotal),
                        control = Interlocked.Read(ref _txOkControlTotal),
                        video = Interlocked.Read(ref _txOkVideoTotal),
                        pose = Interlocked.Read(ref _txOkPoseTotal),
                        audio = Interlocked.Read(ref _txOkAudioTotal),
                        feedback = Interlocked.Read(ref _txOkFeedbackTotal),
                        system = Interlocked.Read(ref _txOkSystemTotal),
                        unknown = Interlocked.Read(ref _txOkUnknownTotal)
                    }
                },
                txFail = new
                {
                    pps = _txFailLastPacketsPerSecond,
                    bps = _txFailLastBytesPerSecond,
                    perSecond = new
                    {
                        control = _txFailLastControlPerSecond,
                        video = _txFailLastVideoPerSecond,
                        pose = _txFailLastPosePerSecond,
                        audio = _txFailLastAudioPerSecond,
                        feedback = _txFailLastFeedbackPerSecond,
                        system = _txFailLastSystemPerSecond,
                        unknown = _txFailLastUnknownPerSecond,
                        noBuffer = _txFailNoBufferLastPerSecond
                    },
                    totals = new
                    {
                        packets = Interlocked.Read(ref _txFailPacketsTotal),
                        bytes = Interlocked.Read(ref _txFailBytesTotal),
                        control = Interlocked.Read(ref _txFailControlTotal),
                        video = Interlocked.Read(ref _txFailVideoTotal),
                        pose = Interlocked.Read(ref _txFailPoseTotal),
                        audio = Interlocked.Read(ref _txFailAudioTotal),
                        feedback = Interlocked.Read(ref _txFailFeedbackTotal),
                        system = Interlocked.Read(ref _txFailSystemTotal),
                        unknown = Interlocked.Read(ref _txFailUnknownTotal),
                        noBuffer = Interlocked.Read(ref _txFailNoBufferTotal)
                    }
                },
                txRetry = new
                {
                    pps = _txRetryLastPerSecond,
                    totals = new
                    {
                        count = Interlocked.Read(ref _txRetryTotal)
                    }
                },
                forwarding = new
                {
                    queueDrop = new
                    {
                        pps = _fwdQueueDropLastPacketsPerSecond,
                        bps = _fwdQueueDropLastBytesPerSecond,
                        perSecond = new
                        {
                            video = _fwdQueueDropLastVideoPerSecond,
                            pose = _fwdQueueDropLastPosePerSecond,
                            audio = _fwdQueueDropLastAudioPerSecond,
                            feedback = _fwdQueueDropLastFeedbackPerSecond,
                            system = _fwdQueueDropLastSystemPerSecond,
                            unknown = _fwdQueueDropLastUnknownPerSecond
                        },
                        totals = new
                        {
                            packets = Interlocked.Read(ref _fwdQueueDropPacketsTotal),
                            bytes = Interlocked.Read(ref _fwdQueueDropBytesTotal),
                            video = Interlocked.Read(ref _fwdQueueDropVideoTotal),
                            pose = Interlocked.Read(ref _fwdQueueDropPoseTotal),
                            audio = Interlocked.Read(ref _fwdQueueDropAudioTotal),
                            feedback = Interlocked.Read(ref _fwdQueueDropFeedbackTotal),
                            system = Interlocked.Read(ref _fwdQueueDropSystemTotal),
                            unknown = Interlocked.Read(ref _fwdQueueDropUnknownTotal)
                        }
                    }
                }
            };
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}

