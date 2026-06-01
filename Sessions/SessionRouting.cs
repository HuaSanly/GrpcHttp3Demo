using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionRouting
    {
        private readonly SessionMemoryStore _memory;
        private readonly UdpForwardingMetricsService _forwardingMetrics;
        private readonly UdpLinkMetricsService _linkMetrics;
        private static readonly UdpLinkMediaKind[] ForwardedMediaKinds =
        {
            UdpLinkMediaKind.Video,
            UdpLinkMediaKind.Pose,
            UdpLinkMediaKind.Audio,
            UdpLinkMediaKind.TelemetryLowRate,
            UdpLinkMediaKind.TelemetryHighRate
        };

        public SessionRouting(SessionMemoryStore memory, UdpForwardingMetricsService forwardingMetrics, UdpLinkMetricsService linkMetrics)
        {
            _memory = memory;
            _forwardingMetrics = forwardingMetrics;
            _linkMetrics = linkMetrics;
        }

        public void ReconcileLinksBetween(string publisherId, string subscriberId)
        {
            if (!_memory.Sessions.TryGetValue(publisherId, out var publisher)) return;
            if (!_memory.Sessions.TryGetValue(subscriberId, out var subscriber)) return;
            if (string.Equals(publisherId, subscriberId, StringComparison.Ordinal)) return;

            var wantVideo = false;
            var wantPose = false;
            var wantAudio = false;
            var wantTelemetryLowRate = false;
            var wantTelemetryHighRate = false;
            var wantFeedback = false;

            if (_memory.SubscriptionMeta.TryGetValue(publisherId, out var subscriptions)
                && subscriptions.TryGetValue(subscriberId, out var meta))
            {
                wantVideo = meta.SubVideo;
                wantPose = meta.SubPose;
                wantAudio = meta.SubAudio;
                wantTelemetryLowRate = meta.SubTelemetryLowRate;
                wantTelemetryHighRate = meta.SubTelemetryHighRate;
            }

            if (publisher.Role == RegisterRequest.Types.EndpointType.Vr
                && subscriber.Role == RegisterRequest.Types.EndpointType.Robot
                && _memory.Pairings.TryGetValue(publisherId, out var fbTarget)
                && string.Equals(fbTarget, subscriberId, StringComparison.Ordinal))
            {
                wantFeedback = true;
            }

            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.Video, wantVideo);
            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.Pose, wantPose);
            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.Audio, wantAudio);
            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.TelemetryLowRate, wantTelemetryLowRate);
            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.TelemetryHighRate, wantTelemetryHighRate);
            ReconcileEgressMedia(publisherId, subscriberId, UdpLinkMediaKind.Feedback, wantFeedback);

            ReconcilePublisherIngress(publisherId);
        }

        private void ReconcileEgressMedia(string publisherId, string subscriberId, UdpLinkMediaKind mediaKind, bool wanted)
        {
            if (wanted)
                _linkMetrics.GetOrCreateEgressLink(publisherId, subscriberId, mediaKind);
            else
                _linkMetrics.DeleteEgressLink(publisherId, subscriberId, mediaKind);
        }

        public void ReconcilePublisherIngress(string publisherId)
        {
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.Video);
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.Pose);
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.Audio);
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.TelemetryLowRate);
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.TelemetryHighRate);
            ReconcileIngressMedia(publisherId, UdpLinkMediaKind.Feedback);
        }

        private void ReconcileIngressMedia(string publisherId, UdpLinkMediaKind mediaKind)
        {
            if (_linkMetrics.HasAnyEgressLink(publisherId, mediaKind))
                _linkMetrics.GetOrCreateIngressLink(publisherId, mediaKind);
            else
                _linkMetrics.DeleteIngressLink(publisherId, mediaKind);
        }

        public void RebuildForwardingForPublisher(string publisherSessionId)
        {
            RebuildForwardingForPublisher(publisherSessionId, ForwardedMediaKinds);
        }

        public void RebuildForwardingForPublisher(string publisherSessionId, IReadOnlyCollection<UdpLinkMediaKind> mediaKinds)
        {
            if (!_memory.Sessions.TryGetValue(publisherSessionId, out var publisher) || publisher.UdpEndpoint == null)
            {
                if (_memory.SessionEndpointIndex.TryGetValue(publisherSessionId, out var oldEndpoint))
                {
                    _memory.SourceRouteTable.TryRemove(oldEndpoint, out _);
                }
                return;
            }

            var sourceEndpoint = publisher.UdpEndpoint;
            var sourceRoute = _memory.GetOrCreateSourceRoute(sourceEndpoint, publisherSessionId);

            foreach (var mediaKind in mediaKinds)
            {
                RebuildForwardingForPublisherMedia(publisherSessionId, sourceEndpoint, sourceRoute, mediaKind);
            }
        }

        private void RebuildForwardingForPublisherMedia(string publisherSessionId, IPEndPoint sourceEndpoint, UdpSourceRoute sourceRoute, UdpLinkMediaKind mediaKind)
        {
            if (!TryGetForwardingPrefix(mediaKind, out var prefix)) return;

            var hasSubscription = false;
            var targets = new List<UdpForwardTarget>();

            if (_memory.SubscriptionMeta.TryGetValue(publisherSessionId, out var subscriptions))
            {
                foreach (var item in subscriptions)
                {
                    var targetSessionId = item.Key;
                    var meta = item.Value;
                    if (!IsSubscribed(meta, mediaKind)) continue;

                    hasSubscription = true;
                    if (string.IsNullOrEmpty(targetSessionId)) continue;
                    if (!_memory.Sessions.TryGetValue(targetSessionId, out var target) || target.UdpEndpoint == null) continue;

                    var endpoint = target.UdpEndpoint;
                    if (endpoint.Equals(sourceEndpoint)) continue;

                    var counter = _forwardingMetrics.GetOrCreateEdge(publisherSessionId, targetSessionId);
                    var link = _linkMetrics.GetOrCreateEgressLink(publisherSessionId, targetSessionId, mediaKind);
                    targets.Add(new UdpForwardTarget(endpoint, targetSessionId, counter, link));
                }
            }

            if (!hasSubscription)
            {
                sourceRoute.RemoveRoute(prefix);
                return;
            }

            var ingressLink = _linkMetrics.GetOrCreateIngressLink(publisherSessionId, mediaKind);
            sourceRoute.SetRoute(prefix, new UdpMediaForwardRoute(targets.ToImmutableArray(), ingressLink));
        }

        private static bool IsSubscribed(SubscriptionMeta meta, UdpLinkMediaKind mediaKind)
        {
            return mediaKind switch
            {
                UdpLinkMediaKind.Video => meta.SubVideo,
                UdpLinkMediaKind.Pose => meta.SubPose,
                UdpLinkMediaKind.Audio => meta.SubAudio,
                UdpLinkMediaKind.TelemetryLowRate => meta.SubTelemetryLowRate,
                UdpLinkMediaKind.TelemetryHighRate => meta.SubTelemetryHighRate,
                _ => false
            };
        }

        private static bool TryGetForwardingPrefix(UdpLinkMediaKind mediaKind, out byte prefix)
        {
            switch (mediaKind)
            {
                case UdpLinkMediaKind.Video:
                    prefix = 0x01;
                    return true;
                case UdpLinkMediaKind.Pose:
                    prefix = 0x02;
                    return true;
                case UdpLinkMediaKind.Audio:
                    prefix = 0x04;
                    return true;
                case UdpLinkMediaKind.TelemetryLowRate:
                    prefix = 0x05;
                    return true;
                case UdpLinkMediaKind.TelemetryHighRate:
                    prefix = 0x06;
                    return true;
                default:
                    prefix = default;
                    return false;
            }
        }

        public void RebuildForwardingForSubscriber(string subscriberSessionId)
        {
            foreach (var item in _memory.SubscriptionMeta)
            {
                if (item.Value.ContainsKey(subscriberSessionId))
                {
                    RebuildForwardingForPublisher(item.Key);
                }
            }
        }

        public void RefreshFeedbackRoute(string sessionId)
        {
            if (!_memory.Sessions.TryGetValue(sessionId, out var session) || session.UdpEndpoint == null)
            {
                return;
            }

            if (!_memory.Pairings.TryGetValue(sessionId, out var partnerSessionId)) return;
            if (!_memory.Sessions.TryGetValue(partnerSessionId, out var partner) || partner.UdpEndpoint == null) return;

            IPEndPoint? vrEndpoint = null;
            IPEndPoint? robotEndpoint = null;
            string? vrSessionId = null;
            string? robotSessionId = null;

            if (session.Role == RegisterRequest.Types.EndpointType.Vr && partner.Role == RegisterRequest.Types.EndpointType.Robot)
            {
                vrEndpoint = session.UdpEndpoint;
                robotEndpoint = partner.UdpEndpoint;
                vrSessionId = sessionId;
                robotSessionId = partnerSessionId;
            }
            else if (session.Role == RegisterRequest.Types.EndpointType.Robot && partner.Role == RegisterRequest.Types.EndpointType.Vr)
            {
                vrEndpoint = partner.UdpEndpoint;
                robotEndpoint = session.UdpEndpoint;
                vrSessionId = partnerSessionId;
                robotSessionId = sessionId;
            }

            if (vrEndpoint != null && robotEndpoint != null && !string.IsNullOrEmpty(vrSessionId) && !string.IsNullOrEmpty(robotSessionId))
            {
                var counter = _forwardingMetrics.GetOrCreateEdge(vrSessionId, robotSessionId);
                var ingressLink = _linkMetrics.GetOrCreateIngressLink(vrSessionId, UdpLinkMediaKind.Feedback);
                var egressLink = _linkMetrics.GetOrCreateEgressLink(vrSessionId, robotSessionId, UdpLinkMediaKind.Feedback);
                _memory.FeedbackRoute[vrEndpoint] = new UdpFeedbackForwardTarget(robotEndpoint, robotSessionId, counter, ingressLink, egressLink);
            }
        }

        public void RemoveFeedbackRoute(string sessionId, string? partnerSessionOverride = null)
        {
            var partnerSessionId = partnerSessionOverride;
            if (string.IsNullOrEmpty(partnerSessionId))
            {
                _memory.Pairings.TryGetValue(sessionId, out partnerSessionId);
            }

            if (_memory.Sessions.TryGetValue(sessionId, out var currentSession) && currentSession.UdpEndpoint != null)
            {
                _memory.FeedbackRoute.TryRemove(currentSession.UdpEndpoint, out _);
            }

            if (!string.IsNullOrEmpty(partnerSessionId) &&
                _memory.Sessions.TryGetValue(partnerSessionId, out var currentPartner) &&
                currentPartner.UdpEndpoint != null)
            {
                _memory.FeedbackRoute.TryRemove(currentPartner.UdpEndpoint, out _);
            }
        }
    }
}