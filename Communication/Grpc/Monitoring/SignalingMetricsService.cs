using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Communication.Grpc.Monitoring
{
    public sealed class SignalingMetricsService : IDisposable
    {
        private readonly ILogger<SignalingMetricsService> _logger;
        private readonly Timer _timer;

        private long _inRegisterTotal;
        private long _inPingTotal;
        private long _inPairTotal;
        private long _inSubscribeTotal;
        private long _inListUnpairedTotal;
        private long _inEventStreamTotal;

        private long _inWsBadProtoTotal;
        private long _inWsUnsupportedTotal;
        private long _inWsNoSessionTotal;

        private long _outRegisterResponseTotal;
        private long _outPingAckTotal;
        private long _outPairResponseTotal;
        private long _outSubscribeResponseTotal;
        private long _outListUnpairedResponseTotal;
        private long _outWsErrorTotal;

        private long _outEventsTotal;
        private long _outPairEventsTotal;
        private long _outSystemCommandsTotal;

        private long _inRegisterThisSecond;
        private long _inPingThisSecond;
        private long _inPairThisSecond;
        private long _inSubscribeThisSecond;
        private long _inListUnpairedThisSecond;
        private long _inEventStreamThisSecond;

        private long _inWsBadProtoThisSecond;
        private long _inWsUnsupportedThisSecond;
        private long _inWsNoSessionThisSecond;

        private long _outRegisterResponseThisSecond;
        private long _outPingAckThisSecond;
        private long _outPairResponseThisSecond;
        private long _outSubscribeResponseThisSecond;
        private long _outListUnpairedResponseThisSecond;
        private long _outWsErrorThisSecond;

        private long _outEventsThisSecond;
        private long _outPairEventsThisSecond;
        private long _outSystemCommandsThisSecond;

        private long _lastInRegisterPps;
        private long _lastInPingPps;
        private long _lastInPairPps;
        private long _lastInSubscribePps;
        private long _lastInListUnpairedPps;
        private long _lastInEventStreamPps;

        private long _lastInWsBadProtoPps;
        private long _lastInWsUnsupportedPps;
        private long _lastInWsNoSessionPps;

        private long _lastOutRegisterResponsePps;
        private long _lastOutPingAckPps;
        private long _lastOutPairResponsePps;
        private long _lastOutSubscribeResponsePps;
        private long _lastOutListUnpairedResponsePps;
        private long _lastOutWsErrorPps;

        private long _lastOutEventsPps;
        private long _lastOutPairEventsPps;
        private long _lastOutSystemCommandsPps;

        private DateTime _lastTickUtc;

        public SignalingMetricsService(ILogger<SignalingMetricsService> logger)
        {
            _logger = logger;
            _lastTickUtc = DateTime.UtcNow;
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void RecordInboundRegister() { Interlocked.Increment(ref _inRegisterTotal); Interlocked.Increment(ref _inRegisterThisSecond); }
        public void RecordInboundPing() { Interlocked.Increment(ref _inPingTotal); Interlocked.Increment(ref _inPingThisSecond); }
        public void RecordInboundPair() { Interlocked.Increment(ref _inPairTotal); Interlocked.Increment(ref _inPairThisSecond); }
        public void RecordInboundSubscribe() { Interlocked.Increment(ref _inSubscribeTotal); Interlocked.Increment(ref _inSubscribeThisSecond); }
        public void RecordInboundListUnpaired() { Interlocked.Increment(ref _inListUnpairedTotal); Interlocked.Increment(ref _inListUnpairedThisSecond); }
        public void RecordInboundEventStream() { Interlocked.Increment(ref _inEventStreamTotal); Interlocked.Increment(ref _inEventStreamThisSecond); }

        public void RecordInboundWsBadProto() { Interlocked.Increment(ref _inWsBadProtoTotal); Interlocked.Increment(ref _inWsBadProtoThisSecond); }
        public void RecordInboundWsUnsupported() { Interlocked.Increment(ref _inWsUnsupportedTotal); Interlocked.Increment(ref _inWsUnsupportedThisSecond); }
        public void RecordInboundWsNoSession() { Interlocked.Increment(ref _inWsNoSessionTotal); Interlocked.Increment(ref _inWsNoSessionThisSecond); }

        public void RecordOutboundRegisterResponse() { Interlocked.Increment(ref _outRegisterResponseTotal); Interlocked.Increment(ref _outRegisterResponseThisSecond); }
        public void RecordOutboundPingAck() { Interlocked.Increment(ref _outPingAckTotal); Interlocked.Increment(ref _outPingAckThisSecond); }
        public void RecordOutboundPairResponse() { Interlocked.Increment(ref _outPairResponseTotal); Interlocked.Increment(ref _outPairResponseThisSecond); }
        public void RecordOutboundSubscribeResponse() { Interlocked.Increment(ref _outSubscribeResponseTotal); Interlocked.Increment(ref _outSubscribeResponseThisSecond); }
        public void RecordOutboundListUnpairedResponse() { Interlocked.Increment(ref _outListUnpairedResponseTotal); Interlocked.Increment(ref _outListUnpairedResponseThisSecond); }
        public void RecordOutboundWsError() { Interlocked.Increment(ref _outWsErrorTotal); Interlocked.Increment(ref _outWsErrorThisSecond); }

        public void RecordOutboundEvent(EventMessage message)
        {
            Interlocked.Increment(ref _outEventsTotal);
            Interlocked.Increment(ref _outEventsThisSecond);

            if (message.System != null)
            {
                Interlocked.Increment(ref _outSystemCommandsTotal);
                Interlocked.Increment(ref _outSystemCommandsThisSecond);
                return;
            }

            if (message.Pair != null)
            {
                Interlocked.Increment(ref _outPairEventsTotal);
                Interlocked.Increment(ref _outPairEventsThisSecond);
            }
        }

        private void Tick()
        {
            try
            {
                _lastInRegisterPps = Interlocked.Exchange(ref _inRegisterThisSecond, 0);
                _lastInPingPps = Interlocked.Exchange(ref _inPingThisSecond, 0);
                _lastInPairPps = Interlocked.Exchange(ref _inPairThisSecond, 0);
                _lastInSubscribePps = Interlocked.Exchange(ref _inSubscribeThisSecond, 0);
                _lastInListUnpairedPps = Interlocked.Exchange(ref _inListUnpairedThisSecond, 0);
                _lastInEventStreamPps = Interlocked.Exchange(ref _inEventStreamThisSecond, 0);

                _lastInWsBadProtoPps = Interlocked.Exchange(ref _inWsBadProtoThisSecond, 0);
                _lastInWsUnsupportedPps = Interlocked.Exchange(ref _inWsUnsupportedThisSecond, 0);
                _lastInWsNoSessionPps = Interlocked.Exchange(ref _inWsNoSessionThisSecond, 0);

                _lastOutRegisterResponsePps = Interlocked.Exchange(ref _outRegisterResponseThisSecond, 0);
                _lastOutPingAckPps = Interlocked.Exchange(ref _outPingAckThisSecond, 0);
                _lastOutPairResponsePps = Interlocked.Exchange(ref _outPairResponseThisSecond, 0);
                _lastOutSubscribeResponsePps = Interlocked.Exchange(ref _outSubscribeResponseThisSecond, 0);
                _lastOutListUnpairedResponsePps = Interlocked.Exchange(ref _outListUnpairedResponseThisSecond, 0);
                _lastOutWsErrorPps = Interlocked.Exchange(ref _outWsErrorThisSecond, 0);

                _lastOutEventsPps = Interlocked.Exchange(ref _outEventsThisSecond, 0);
                _lastOutPairEventsPps = Interlocked.Exchange(ref _outPairEventsThisSecond, 0);
                _lastOutSystemCommandsPps = Interlocked.Exchange(ref _outSystemCommandsThisSecond, 0);

                _lastTickUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signaling metrics tick failed");
            }
        }

        public object Snapshot()
        {
            return new
            {
                updatedUtc = _lastTickUtc,
                inboundPps = new
                {
                    register = _lastInRegisterPps,
                    ping = _lastInPingPps,
                    pair = _lastInPairPps,
                    subscribe = _lastInSubscribePps,
                    listUnpaired = _lastInListUnpairedPps,
                    eventStream = _lastInEventStreamPps,
                    wsBadProto = _lastInWsBadProtoPps,
                    wsUnsupported = _lastInWsUnsupportedPps,
                    wsNoSession = _lastInWsNoSessionPps
                },
                outboundPps = new
                {
                    registerResponse = _lastOutRegisterResponsePps,
                    pingAck = _lastOutPingAckPps,
                    pairResponse = _lastOutPairResponsePps,
                    subscribeResponse = _lastOutSubscribeResponsePps,
                    listUnpairedResponse = _lastOutListUnpairedResponsePps,
                    wsError = _lastOutWsErrorPps,
                    events = _lastOutEventsPps,
                    pairEvents = _lastOutPairEventsPps,
                    systemCommands = _lastOutSystemCommandsPps
                },
                totals = new
                {
                    inbound = new
                    {
                        register = Interlocked.Read(ref _inRegisterTotal),
                        ping = Interlocked.Read(ref _inPingTotal),
                        pair = Interlocked.Read(ref _inPairTotal),
                        subscribe = Interlocked.Read(ref _inSubscribeTotal),
                        listUnpaired = Interlocked.Read(ref _inListUnpairedTotal),
                        eventStream = Interlocked.Read(ref _inEventStreamTotal),
                        wsBadProto = Interlocked.Read(ref _inWsBadProtoTotal),
                        wsUnsupported = Interlocked.Read(ref _inWsUnsupportedTotal),
                        wsNoSession = Interlocked.Read(ref _inWsNoSessionTotal)
                    },
                    outbound = new
                    {
                        registerResponse = Interlocked.Read(ref _outRegisterResponseTotal),
                        pingAck = Interlocked.Read(ref _outPingAckTotal),
                        pairResponse = Interlocked.Read(ref _outPairResponseTotal),
                        subscribeResponse = Interlocked.Read(ref _outSubscribeResponseTotal),
                        listUnpairedResponse = Interlocked.Read(ref _outListUnpairedResponseTotal),
                        wsError = Interlocked.Read(ref _outWsErrorTotal),
                        events = Interlocked.Read(ref _outEventsTotal),
                        pairEvents = Interlocked.Read(ref _outPairEventsTotal),
                        systemCommands = Interlocked.Read(ref _outSystemCommandsTotal)
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

