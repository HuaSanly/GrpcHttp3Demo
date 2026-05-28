using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionSubscription
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionRouting _routing;

        public SessionSubscription(SessionMemoryStore memory, SessionRouting routing)
        {
            _memory = memory;
            _routing = routing;
        }

        public void UpdateSubscription(string publisherSessionId, string subscriberSessionId, bool isSub, bool subVideo, bool subPose, bool subAudio, bool subTelemetryLowRate, bool subTelemetryHighRate)
        {
            if (!_memory.Sessions.ContainsKey(publisherSessionId))
            {
                return;
            }

            if (isSub)
            {
                var details = _memory.SubscriptionDetails.GetOrAdd(publisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionDetail>());
                details[subscriberSessionId] = new SubscriptionDetail
                {
                    SubscriberId = subscriberSessionId,
                    SubVideo = subVideo,
                    SubPose = subPose,
                    SubAudio = subAudio,
                    SubTelemetryLowRate = subTelemetryLowRate,
                    SubTelemetryHighRate = subTelemetryHighRate
                };

                var meta = _memory.SubscriptionMeta.GetOrAdd(publisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionMeta>());
                meta[subscriberSessionId] = new SubscriptionMeta
                {
                    SubscriberId = subscriberSessionId,
                    SubVideo = subVideo,
                    SubPose = subPose,
                    SubAudio = subAudio,
                    SubTelemetryLowRate = subTelemetryLowRate,
                    SubTelemetryHighRate = subTelemetryHighRate,
                    LastUpdatedUtc = DateTime.UtcNow
                };

                _routing.RebuildForwardingForPublisher(publisherSessionId);
                Console.WriteLine($"[SessionSubscription] {subscriberSessionId} subscribed to {publisherSessionId} (V:{subVideo} P:{subPose} A:{subAudio} TL:{subTelemetryLowRate} TH:{subTelemetryHighRate})");
                return;
            }

            if (_memory.SubscriptionDetails.TryGetValue(publisherSessionId, out var currentDetails))
            {
                currentDetails.TryRemove(subscriberSessionId, out _);
            }

            if (_memory.SubscriptionMeta.TryGetValue(publisherSessionId, out var currentMeta))
            {
                currentMeta.TryRemove(subscriberSessionId, out _);
            }

            _routing.RebuildForwardingForPublisher(publisherSessionId);
            Console.WriteLine($"[SessionSubscription] {subscriberSessionId} unsubscribed from {publisherSessionId}");
        }

        public List<string> GetSubscribers(string publisherSessionId)
        {
            return _memory.SubscriptionDetails.TryGetValue(publisherSessionId, out var details)
                ? details.Keys.ToList()
                : new List<string>();
        }

        public IReadOnlyCollection<string> GetVideoConfigTargets(string publisherSessionId)
        {
            return GetConfigTargets(publisherSessionId, sub => sub.SubVideo);
        }

        public IReadOnlyCollection<string> GetAudioConfigTargets(string publisherSessionId)
        {
            return GetConfigTargets(publisherSessionId, sub => sub.SubAudio);
        }

        public bool TryUpdateSystemMonitorSubscription(string subscriberSessionId, bool isSub, SystemMonitorTopicMask topics, int intervalMs, out string message)
        {
            if (!_memory.Sessions.TryGetValue(subscriberSessionId, out var subscriber))
            {
                message = $"Session not found: {subscriberSessionId}";
                return false;
            }

            if (isSub && subscriber.UdpEndpoint == null)
            {
                message = $"Session has no UDP endpoint: {subscriberSessionId}";
                return false;
            }

            if (isSub && topics == SystemMonitorTopicMask.None)
            {
                message = "Missing monitor topics";
                return false;
            }

            if (isSub)
            {
                var effectiveIntervalMs = Math.Clamp(intervalMs, 250, 10_000);
                var details = _memory.SubscriptionDetails.GetOrAdd(SystemPublishers.MonitorPublisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionDetail>());
                details[subscriberSessionId] = new SubscriptionDetail
                {
                    SubscriberId = subscriberSessionId,
                    SystemMonitorTopics = topics,
                    SystemMonitorIntervalMs = effectiveIntervalMs
                };

                var meta = _memory.SubscriptionMeta.GetOrAdd(SystemPublishers.MonitorPublisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionMeta>());
                meta[subscriberSessionId] = new SubscriptionMeta
                {
                    SubscriberId = subscriberSessionId,
                    SystemMonitorTopics = topics,
                    SystemMonitorIntervalMs = effectiveIntervalMs,
                    LastUpdatedUtc = DateTime.UtcNow
                };

                RebuildSystemMonitorTargets();
                message = "Subscribed";
                return true;
            }

            if (_memory.SubscriptionDetails.TryGetValue(SystemPublishers.MonitorPublisherSessionId, out var currentDetails))
            {
                currentDetails.TryRemove(subscriberSessionId, out _);
            }

            if (_memory.SubscriptionMeta.TryGetValue(SystemPublishers.MonitorPublisherSessionId, out var currentMeta))
            {
                currentMeta.TryRemove(subscriberSessionId, out _);
            }

            RebuildSystemMonitorTargets();
            message = "Unsubscribed";
            return true;
        }

        public void RebuildSystemMonitorTargets()
        {
            if (!_memory.SubscriptionDetails.TryGetValue(SystemPublishers.MonitorPublisherSessionId, out var subscriptions))
            {
                _memory.SetSystemMonitorTargets(Array.Empty<UdpSystemMonitorTarget>());
                return;
            }

            var targets = new List<UdpSystemMonitorTarget>(subscriptions.Count);
            foreach (var item in subscriptions.Values)
            {
                if (item.SystemMonitorTopics == SystemMonitorTopicMask.None) continue;
                if (!_memory.Sessions.TryGetValue(item.SubscriberId, out var subscriber) || subscriber.UdpEndpoint == null) continue;

                targets.Add(new UdpSystemMonitorTarget(
                    item.SubscriberId,
                    subscriber.UdpEndpoint,
                    item.SystemMonitorTopics,
                    item.SystemMonitorIntervalMs));
            }

            _memory.SetSystemMonitorTargets(targets.ToArray());
        }

        public IReadOnlyCollection<object> ListSystemMonitorSubscriptions()
        {
            if (!_memory.SubscriptionDetails.TryGetValue(SystemPublishers.MonitorPublisherSessionId, out var subscriptions))
            {
                return Array.Empty<object>();
            }

            return subscriptions.Values.Select(subscription =>
            {
                _memory.Sessions.TryGetValue(subscription.SubscriberId, out var subscriber);
                return new
                {
                    publisherSessionId = SystemPublishers.MonitorPublisherSessionId,
                    subscriberSessionId = subscription.SubscriberId,
                    subscriberDeviceId = subscriber?.DeviceId,
                    subscriberRole = subscriber?.Role.ToString(),
                    udpEndpoint = subscriber?.UdpEndpoint?.ToString(),
                    topics = ToTopicNames(subscription.SystemMonitorTopics),
                    intervalMs = Math.Clamp(subscription.SystemMonitorIntervalMs, 250, 10_000)
                };
            }).ToArray<object>();
        }

        public static string[] ToTopicNames(SystemMonitorTopicMask topics)
        {
            var names = new List<string>(4);
            if ((topics & SystemMonitorTopicMask.UdpGlobal) != 0) names.Add("udp_global");
            if ((topics & SystemMonitorTopicMask.SignalingRates) != 0) names.Add("signaling_rates");
            if ((topics & SystemMonitorTopicMask.OnlineSummary) != 0) names.Add("online_summary");
            if ((topics & SystemMonitorTopicMask.RuntimeTables) != 0) names.Add("runtime_tables");
            return names.ToArray();
        }

        private IReadOnlyCollection<string> GetConfigTargets(string publisherSessionId, Func<SubscriptionDetail, bool> include)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);

            if (_memory.Pairings.TryGetValue(publisherSessionId, out var pairedSessionId) && !string.IsNullOrEmpty(pairedSessionId))
            {
                targets.Add(pairedSessionId);
            }

            if (_memory.SubscriptionDetails.TryGetValue(publisherSessionId, out var details))
            {
                foreach (var subscription in details.Values)
                {
                    if (include(subscription) && !string.IsNullOrEmpty(subscription.SubscriberId))
                    {
                        targets.Add(subscription.SubscriberId);
                    }
                }
            }

            targets.Remove(publisherSessionId);
            return targets.ToList();
        }
    }
}