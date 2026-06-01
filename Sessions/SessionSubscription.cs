using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionSubscription
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionRuntimeProjection _projection;

        public SessionSubscription(SessionMemoryStore memory, SessionRuntimeProjection projection)
        {
            _memory = memory;
            _projection = projection;
        }

        public void UpdateSubscription(string publisherSessionId, string subscriberSessionId, bool isSub, bool subVideo, bool subPose, bool subAudio, bool subTelemetryLowRate, bool subTelemetryHighRate)
        {
            if (!_memory.Sessions.ContainsKey(publisherSessionId))
            {
                return;
            }

            _memory.SubscriptionMeta.TryGetValue(publisherSessionId, out var existingMeta);
            SubscriptionMeta? previousMeta = null;
            existingMeta?.TryGetValue(subscriberSessionId, out previousMeta);
            var changedMediaKinds = GetChangedMediaKinds(
                previousMeta,
                isSub,
                subVideo,
                subPose,
                subAudio,
                subTelemetryLowRate,
                subTelemetryHighRate);

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

                _projection.OnSubscriptionChanged(publisherSessionId, subscriberSessionId, changedMediaKinds);
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

            _projection.OnSubscriptionChanged(publisherSessionId, subscriberSessionId, changedMediaKinds);
            Console.WriteLine($"[SessionSubscription] {subscriberSessionId} unsubscribed from {publisherSessionId}");
        }

        private static UdpLinkMediaKind[] GetChangedMediaKinds(SubscriptionMeta? previousMeta, bool isSub, bool subVideo, bool subPose, bool subAudio, bool subTelemetryLowRate, bool subTelemetryHighRate)
        {
            var result = new List<UdpLinkMediaKind>(5);
            AddIfChanged(result, UdpLinkMediaKind.Video, previousMeta?.SubVideo ?? false, isSub && subVideo);
            AddIfChanged(result, UdpLinkMediaKind.Pose, previousMeta?.SubPose ?? false, isSub && subPose);
            AddIfChanged(result, UdpLinkMediaKind.Audio, previousMeta?.SubAudio ?? false, isSub && subAudio);
            AddIfChanged(result, UdpLinkMediaKind.TelemetryLowRate, previousMeta?.SubTelemetryLowRate ?? false, isSub && subTelemetryLowRate);
            AddIfChanged(result, UdpLinkMediaKind.TelemetryHighRate, previousMeta?.SubTelemetryHighRate ?? false, isSub && subTelemetryHighRate);
            return result.ToArray();
        }

        private static void AddIfChanged(List<UdpLinkMediaKind> result, UdpLinkMediaKind mediaKind, bool previous, bool current)
        {
            if (previous != current)
            {
                result.Add(mediaKind);
            }
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

                _projection.ReconcileSystemMonitorTargets();
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

            _projection.ReconcileSystemMonitorTargets();
            message = "Unsubscribed";
            return true;
        }

        public bool TryUpdateTopologyMonitorSubscription(string subscriberSessionId, bool isSub, int intervalMs, string? topologyId, out string message)
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

            var normalizedTopologyId = NormalizeTopologyId(topologyId);
            if (isSub && string.IsNullOrWhiteSpace(normalizedTopologyId))
            {
                message = "Missing topologyId";
                return false;
            }

            if (isSub)
            {
                var effectiveIntervalMs = Math.Clamp(intervalMs, 250, 10_000);
                var details = _memory.SubscriptionDetails.GetOrAdd(SystemPublishers.LinkMonitorPublisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionDetail>());
                details[subscriberSessionId] = new SubscriptionDetail
                {
                    SubscriberId = subscriberSessionId,
                    SystemMonitorIntervalMs = effectiveIntervalMs,
                    LinkMonitorTopologyId = normalizedTopologyId
                };

                var meta = _memory.SubscriptionMeta.GetOrAdd(SystemPublishers.LinkMonitorPublisherSessionId, _ => new ConcurrentDictionary<string, SubscriptionMeta>());
                meta[subscriberSessionId] = new SubscriptionMeta
                {
                    SubscriberId = subscriberSessionId,
                    SystemMonitorIntervalMs = effectiveIntervalMs,
                    LinkMonitorTopologyId = normalizedTopologyId,
                    LastUpdatedUtc = DateTime.UtcNow
                };

                _projection.ReconcileTopologyMonitorTargets();
                message = "Subscribed";
                return true;
            }

            if (_memory.SubscriptionDetails.TryGetValue(SystemPublishers.LinkMonitorPublisherSessionId, out var currentDetails))
            {
                currentDetails.TryRemove(subscriberSessionId, out _);
            }

            if (_memory.SubscriptionMeta.TryGetValue(SystemPublishers.LinkMonitorPublisherSessionId, out var currentMeta))
            {
                currentMeta.TryRemove(subscriberSessionId, out _);
            }

            _projection.ReconcileTopologyMonitorTargets();
            message = "Unsubscribed";
            return true;
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
                    prefixes = ToPrefixes(subscription.SystemMonitorTopics),
                    topics = ToTopicNames(subscription.SystemMonitorTopics),
                    intervalMs = Math.Clamp(subscription.SystemMonitorIntervalMs, 250, 10_000)
                };
            }).ToArray<object>();
        }

        public IReadOnlyCollection<object> ListTopologyMonitorSubscriptions()
        {
            if (!_memory.SubscriptionDetails.TryGetValue(SystemPublishers.LinkMonitorPublisherSessionId, out var subscriptions))
            {
                return Array.Empty<object>();
            }

            return subscriptions.Values.Select(subscription =>
            {
                _memory.Sessions.TryGetValue(subscription.SubscriberId, out var subscriber);
                return new
                {
                    publisherSessionId = SystemPublishers.LinkMonitorPublisherSessionId,
                    subscriberSessionId = subscription.SubscriberId,
                    subscriberDeviceId = subscriber?.DeviceId,
                    subscriberRole = subscriber?.Role.ToString(),
                    udpEndpoint = subscriber?.UdpEndpoint?.ToString(),
                    prefix = "0x08",
                    topic = "udp_link_metrics",
                    topologyId = NormalizeTopologyId(subscription.LinkMonitorTopologyId),
                    intervalMs = Math.Clamp(subscription.SystemMonitorIntervalMs, 250, 10_000)
                };
            }).ToArray<object>();
        }

        private static string? NormalizeTopologyId(string? topologyId)
        {
            var normalized = topologyId?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        public static string[] ToTopicNames(SystemMonitorTopicMask topics)
        {
            var names = new List<string>(5);
            if ((topics & SystemMonitorTopicMask.UdpGlobal) != 0) names.Add("udp_global");
            if ((topics & SystemMonitorTopicMask.SignalingRates) != 0) names.Add("signaling_rates");
            if ((topics & SystemMonitorTopicMask.OnlineSummary) != 0) names.Add("online_summary");
            if ((topics & SystemMonitorTopicMask.RuntimeTables) != 0) names.Add("runtime_tables");
            return names.ToArray();
        }

        public static string[] ToPrefixes(SystemMonitorTopicMask topics)
        {
            var prefixes = new List<string>(1);
            if ((topics & SystemMonitorTopicMask.Standard) != 0) prefixes.Add("0x07");
            return prefixes.ToArray();
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