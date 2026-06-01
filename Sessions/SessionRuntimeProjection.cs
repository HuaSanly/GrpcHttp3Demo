using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionRuntimeProjection
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionRouting _routing;
        private readonly UdpLinkMetricsService _linkMetrics;
        private readonly PairingAutoSubscribeService _autoSubscribe;

        public SessionRuntimeProjection(SessionMemoryStore memory, SessionRouting routing, UdpLinkMetricsService linkMetrics, PairingAutoSubscribeService autoSubscribe)
        {
            _memory = memory;
            _routing = routing;
            _linkMetrics = linkMetrics;
            _autoSubscribe = autoSubscribe;
        }

        public void OnSubscriptionChanged(string publisherId, string subscriberId, IReadOnlyCollection<UdpLinkMediaKind>? changedMediaKinds = null)
        {
            _routing.ReconcileLinksBetween(publisherId, subscriberId);
            if (changedMediaKinds == null || changedMediaKinds.Count > 0)
            {
                if (changedMediaKinds == null)
                    _routing.RebuildForwardingForPublisher(publisherId);
                else
                    _routing.RebuildForwardingForPublisher(publisherId, changedMediaKinds);
            }
        }

        public void OnPaired(string sessionA, string sessionB)
        {
            _autoSubscribe.ApplyAutoSubscribe(sessionA, sessionB);
            // Feedback is pairing-driven (not subscription-driven); reconcile separately.
            _routing.ReconcileLinksBetween(sessionA, sessionB);
            _routing.ReconcileLinksBetween(sessionB, sessionA);
            _routing.RefreshFeedbackRoute(sessionA);
            _routing.RefreshFeedbackRoute(sessionB);
            _routing.RebuildForwardingForPublisher(sessionA);
            _routing.RebuildForwardingForPublisher(sessionB);
        }

        public void OnUnpaired(string sessionA, string sessionB)
        {
            _routing.RemoveFeedbackRoute(sessionA, sessionB);
            _routing.RemoveFeedbackRoute(sessionB, sessionA);
            _autoSubscribe.ApplyAutoUnsubscribe(sessionA, sessionB);
            // OnSubscriptionChanged is triggered internally by ApplyAutoUnsubscribe → UpdateSubscription.
            // It handles both media unsubscription and Feedback link cleanup
            // (pairings are already removed by UnpairSession before OnUnpaired is called).
        }

        public void DeleteAllLinksForSession(string sessionId)
        {
            _linkMetrics.DeleteLinksForSession(sessionId);
        }

        public void ReconcilePublisherAfterSubscriberRemoved(string publisherId)
        {
            _routing.ReconcilePublisherIngress(publisherId);
            _routing.RebuildForwardingForPublisher(publisherId);
        }

        public void ReconcileUdpEndpointChanged(string sessionId)
        {
            _routing.RefreshFeedbackRoute(sessionId);
            if (_memory.Pairings.TryGetValue(sessionId, out var partnerSessionId))
            {
                _routing.RefreshFeedbackRoute(partnerSessionId);
            }

            _routing.RebuildForwardingForPublisher(sessionId);
            _routing.RebuildForwardingForSubscriber(sessionId);
            ReconcileMonitorTargets();
        }

        public void ReconcileUdpEndpointExpired(string sessionId)
        {
            _routing.RemoveFeedbackRoute(sessionId);
            _routing.RebuildForwardingForPublisher(sessionId);
            _routing.RebuildForwardingForSubscriber(sessionId);
            ReconcileMonitorTargets();
        }

        public void ReconcileMonitorTargets()
        {
            ReconcileSystemMonitorTargets();
            ReconcileTopologyMonitorTargets();
        }

        public void ReconcileSystemMonitorTargets()
        {
            _linkMetrics.DeleteLinksForOrigin(SystemPublishers.MonitorPublisherSessionId);

            if (!_memory.SubscriptionDetails.TryGetValue(SystemPublishers.MonitorPublisherSessionId, out var subscriptions))
            {
                _memory.SetSystemMonitorTargets(Array.Empty<UdpSystemMonitorTarget>());
                return;
            }

            var targets = new List<UdpSystemMonitorTarget>(subscriptions.Count);
            foreach (var item in subscriptions.Values)
            {
                if (item.SystemMonitorTopics == SystemMonitorTopicMask.None) continue;
                if (!_memory.Sessions.TryGetValue(item.SubscriberId, out var subscriber)) continue;

                var link = _linkMetrics.GetOrCreateSystemMonitorLink(item.SubscriberId);

                if (subscriber.UdpEndpoint == null) continue;

                targets.Add(new UdpSystemMonitorTarget(
                    item.SubscriberId,
                    subscriber.UdpEndpoint,
                    item.SystemMonitorTopics,
                    item.SystemMonitorIntervalMs,
                    link));
            }

            _memory.SetSystemMonitorTargets(targets.ToArray());
        }

        public void ReconcileTopologyMonitorTargets()
        {
            _linkMetrics.DeleteLinksForOrigin(SystemPublishers.LinkMonitorPublisherSessionId);

            if (!_memory.SubscriptionDetails.TryGetValue(SystemPublishers.LinkMonitorPublisherSessionId, out var subscriptions))
            {
                _memory.SetLinkMonitorTargets(Array.Empty<UdpTopologyMonitorTarget>());
                return;
            }

            var targets = new List<UdpTopologyMonitorTarget>(subscriptions.Count);
            foreach (var item in subscriptions.Values)
            {
                if (!_memory.Sessions.TryGetValue(item.SubscriberId, out var subscriber)) continue;
                var normalizedTopologyId = NormalizeTopologyId(item.LinkMonitorTopologyId);
                if (string.IsNullOrWhiteSpace(normalizedTopologyId)) continue;

                var link = _linkMetrics.GetOrCreateTopologyMonitorLink(SystemPublishers.LinkMonitorPublisherSessionId, item.SubscriberId);

                if (subscriber.UdpEndpoint == null) continue;

                targets.Add(new UdpTopologyMonitorTarget(
                    item.SubscriberId,
                    subscriber.UdpEndpoint,
                    item.SystemMonitorIntervalMs,
                    normalizedTopologyId,
                    link));
            }

            _memory.SetLinkMonitorTargets(targets.ToArray());
        }

        private static string? NormalizeTopologyId(string? topologyId)
        {
            var normalized = topologyId?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }
}