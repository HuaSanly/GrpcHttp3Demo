using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;
using GrpcHttp3Demo.Utils;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionRouting
    {
        private readonly SessionMemoryStore _memory;
        private readonly UdpForwardingMetricsService _forwardingMetrics;

        public SessionRouting(SessionMemoryStore memory, UdpForwardingMetricsService forwardingMetrics)
        {
            _memory = memory;
            _forwardingMetrics = forwardingMetrics;
        }

        public void RebuildForwardingForPublisher(string publisherSessionId)
        {
            if (!_memory.Sessions.TryGetValue(publisherSessionId, out var publisher) || publisher.UdpEndpoint == null)
            {
                if (_memory.SessionEndpointIndex.TryGetValue(publisherSessionId, out var oldEndpoint))
                {
                    _memory.ForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.PoseForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.AudioForwardingTable.TryRemove(oldEndpoint, out _);
                    _memory.SourceRouteTable.TryRemove(oldEndpoint, out _);
                }
                return;
            }

            var sourceEndpoint = publisher.UdpEndpoint;
            var targets = new Dictionary<string, (IPEndPoint endpoint, ForwardEdgeCounter counter, bool video, bool pose, bool audio)>();

            void AddTarget(string targetSessionId, bool wantVideo, bool wantPose, bool wantAudio)
            {
                if (string.IsNullOrEmpty(targetSessionId)) return;
                if (!_memory.Sessions.TryGetValue(targetSessionId, out var target) || target.UdpEndpoint == null) return;

                var endpoint = target.UdpEndpoint;
                if (endpoint.Equals(sourceEndpoint)) return;

                if (targets.TryGetValue(targetSessionId, out var existing))
                {
                    targets[targetSessionId] = (existing.endpoint, existing.counter, existing.video || wantVideo, existing.pose || wantPose, existing.audio || wantAudio);
                    return;
                }

                var counter = _forwardingMetrics.GetOrCreateEdge(publisherSessionId, targetSessionId);
                targets[targetSessionId] = (endpoint, counter, wantVideo, wantPose, wantAudio);
            }

            if (_memory.Pairings.TryGetValue(publisherSessionId, out var pairedSessionId))
            {
                AddTarget(pairedSessionId, wantVideo: true, wantPose: true, wantAudio: true);
            }

            if (_memory.SubscriptionMeta.TryGetValue(publisherSessionId, out var subscriptions))
            {
                foreach (var item in subscriptions)
                {
                    var meta = item.Value;
                    AddTarget(item.Key, wantVideo: meta.SubVideo, wantPose: meta.SubPose, wantAudio: meta.SubAudio);
                }
            }

            if (AppConfig.IsBroadcastToAll)
            {
                foreach (var session in _memory.Sessions.Values)
                {
                    if (session.UdpEndpoint == null) continue;
                    if (session.SessionId == publisherSessionId) continue;
                    AddTarget(session.SessionId, wantVideo: true, wantPose: true, wantAudio: true);
                }
            }

            var video = new List<UdpForwardTarget>(targets.Count);
            var pose = new List<UdpForwardTarget>(targets.Count);
            var audio = new List<UdpForwardTarget>(targets.Count);

            foreach (var item in targets)
            {
                var targetSessionId = item.Key;
                var (endpoint, counter, wantVideo, wantPose, wantAudio) = item.Value;

                if (wantVideo) video.Add(new UdpForwardTarget(endpoint, targetSessionId, counter));
                if (wantPose) pose.Add(new UdpForwardTarget(endpoint, targetSessionId, counter));
                if (wantAudio) audio.Add(new UdpForwardTarget(endpoint, targetSessionId, counter));
            }

            var videoTargets = video.ToImmutableArray();
            var poseTargets = pose.ToImmutableArray();
            var audioTargets = audio.ToImmutableArray();

            _memory.ForwardingTable[sourceEndpoint] = videoTargets;
            _memory.PoseForwardingTable[sourceEndpoint] = poseTargets;
            _memory.AudioForwardingTable[sourceEndpoint] = audioTargets;
            _memory.SourceRouteTable[sourceEndpoint] = new UdpSourceRoute(publisherSessionId, sourceEndpoint, videoTargets, poseTargets, audioTargets);
        }

        public void RebuildForwardingForSubscriber(string subscriberSessionId)
        {
            if (AppConfig.IsBroadcastToAll)
            {
                foreach (var publisherSessionId in _memory.Sessions.Keys)
                {
                    RebuildForwardingForPublisher(publisherSessionId);
                }
                return;
            }

            if (_memory.Pairings.TryGetValue(subscriberSessionId, out var partnerSessionId))
            {
                RebuildForwardingForPublisher(partnerSessionId);
            }

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
                _memory.FeedbackRoute[vrEndpoint] = new UdpFeedbackForwardTarget(robotEndpoint, robotSessionId, counter);
            }
        }

        public void RemoveFeedbackRoute(string sessionId)
        {
            if (_memory.Sessions.TryGetValue(sessionId, out var session) && session.UdpEndpoint != null)
            {
                _memory.FeedbackRoute.TryRemove(session.UdpEndpoint, out _);
            }

            if (_memory.Pairings.TryGetValue(sessionId, out var partnerSessionId) &&
                _memory.Sessions.TryGetValue(partnerSessionId, out var partner) &&
                partner.UdpEndpoint != null)
            {
                _memory.FeedbackRoute.TryRemove(partner.UdpEndpoint, out _);
            }
        }
    }
}