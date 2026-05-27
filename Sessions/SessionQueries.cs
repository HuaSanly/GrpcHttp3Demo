using System.Collections.Generic;
using System.Linq;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionQueries
    {
        private readonly SessionMemoryStore _memory;
        private readonly PushChannelRegistry _pushChannels;
        private readonly SessionPairing _pairing;

        public SessionQueries(SessionMemoryStore memory, PushChannelRegistry pushChannels, SessionPairing pairing)
        {
            _memory = memory;
            _pushChannels = pushChannels;
            _pairing = pairing;
        }

        public IEnumerable<object> ListSessions(TimeSpan onlineTimeout, RegisterRequest.Types.EndpointType? roleFilter, bool onlineOnly)
        {
            var now = DateTime.UtcNow;

            foreach (var item in _memory.Sessions)
            {
                var context = item.Value;
                var online = now - context.LastHeartbeatUtc <= onlineTimeout;

                if (onlineOnly && !online) continue;
                if (roleFilter.HasValue && roleFilter.Value != RegisterRequest.Types.EndpointType.Unknown && context.Role != roleFilter.Value) continue;

                yield return new
                {
                    sessionId = context.SessionId,
                    deviceId = context.DeviceId,
                    role = context.Role.ToString(),
                    robotGeneration = context.RobotGeneration,
                    vrVersion = context.VrVersion,
                    online,
                    pushConnected = _pushChannels.IsConnected(context.SessionId),
                    hasUdpEndpoint = context.UdpEndpoint != null,
                    pairedSessionId = _pairing.GetPairedSession(context.SessionId)
                };
            }
        }

        public object? GetSessionDetail(string sessionId, TimeSpan onlineTimeout, UdpForwardingMetricsService? forwardingMetrics = null)
        {
            if (!_memory.Sessions.TryGetValue(sessionId, out var context))
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var online = now - context.LastHeartbeatUtc <= onlineTimeout;
            var pairedSessionId = _pairing.GetPairedSession(sessionId);

            var subscribersMeta = Array.Empty<object>();
            if (_memory.SubscriptionMeta.TryGetValue(sessionId, out var subscriptions))
            {
                subscribersMeta = subscriptions.Values.Select(meta => new
                {
                    subscriberSessionId = meta.SubscriberId,
                    subscriberDeviceId = _memory.Sessions.TryGetValue(meta.SubscriberId, out var subscriber) ? subscriber.DeviceId : null,
                    subVideo = meta.SubVideo,
                    subPose = meta.SubPose,
                    subAudio = meta.SubAudio,
                    lastUpdatedUtc = meta.LastUpdatedUtc,
                    targetBitrateKbps = meta.TargetBitrateKbps,
                    subscriberBandwidthKbps = meta.SubscriberBandwidthKbps,
                    spsBytesLength = meta.Sps?.Length ?? 0,
                    ppsBytesLength = meta.Pps?.Length ?? 0
                }).ToArray<object>();
            }

            var subscribedTo = _memory.SubscriptionMeta
                .Select(item => new { PublisherSessionId = item.Key, Subscriptions = item.Value })
                .Where(item => item.Subscriptions.TryGetValue(sessionId, out _))
                .Select(item =>
                {
                    item.Subscriptions.TryGetValue(sessionId, out var meta);
                    return new
                    {
                        publisherSessionId = item.PublisherSessionId,
                        publisherDeviceId = _memory.Sessions.TryGetValue(item.PublisherSessionId, out var publisher) ? publisher.DeviceId : null,
                        subVideo = meta?.SubVideo ?? false,
                        subPose = meta?.SubPose ?? false,
                        subAudio = meta?.SubAudio ?? false,
                        lastUpdatedUtc = meta?.LastUpdatedUtc
                    };
                })
                .ToArray<object>();

            var subscriptionDetails = _memory.SubscriptionDetails.TryGetValue(sessionId, out var detailDict)
                ? detailDict.Values.ToArray()
                : Array.Empty<SubscriptionDetail>();

            var forwardToTargetSessionIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(pairedSessionId)) forwardToTargetSessionIds.Add(pairedSessionId);
            foreach (var subscription in subscriptionDetails) forwardToTargetSessionIds.Add(subscription.SubscriberId);

            var outboundTo = forwardToTargetSessionIds
                .Select(targetSessionId =>
                {
                    _memory.Sessions.TryGetValue(targetSessionId, out var target);

                    object? stats = null;
                    if (forwardingMetrics != null && forwardingMetrics.TryGetEdge(sessionId, targetSessionId, out var edge) && edge != null)
                    {
                        stats = edge.Snapshot();
                    }

                    return new
                    {
                        targetSessionId,
                        targetDeviceId = target?.DeviceId,
                        targetRole = target?.Role.ToString(),
                        targetHasUdpEndpoint = target?.UdpEndpoint != null,
                        stats
                    };
                })
                .ToArray<object>();

            return new
            {
                sessionId = context.SessionId,
                deviceId = context.DeviceId,
                role = context.Role.ToString(),
                robotGeneration = context.RobotGeneration,
                vrVersion = context.VrVersion,
                online,
                pushConnected = _pushChannels.IsConnected(context.SessionId),
                client = new { ip = context.ClientIp, port = context.ClientPort },
                udp = new
                {
                    endpoint = context.UdpEndpoint?.ToString(),
                    lastControlUtc = context.LastUdpControlUtc == DateTime.MinValue ? (DateTime?)null : context.LastUdpControlUtc,
                    lastDataUtc = context.LastUdpDataUtc == DateTime.MinValue ? (DateTime?)null : context.LastUdpDataUtc
                },
                heartbeat = new { lastHeartbeatUtc = context.LastHeartbeatUtc },
                pairing = new
                {
                    pairedSessionId,
                    pairedDeviceId = context.PairedDeviceId,
                    pairedRobotGeneration = !string.IsNullOrEmpty(pairedSessionId) && _memory.Sessions.TryGetValue(pairedSessionId, out var paired)
                        ? paired.RobotGeneration
                        : 0,
                    pairedVrVersion = !string.IsNullOrEmpty(pairedSessionId) && _memory.Sessions.TryGetValue(pairedSessionId, out paired)
                        ? paired.VrVersion
                        : string.Empty
                },
                subscriptions = new
                {
                    subscriberCount = subscriptionDetails.Length,
                    subscribers = subscriptionDetails.Select(subscription => new
                    {
                        subscriberId = subscription.SubscriberId,
                        subVideo = subscription.SubVideo,
                        subPose = subscription.SubPose,
                        subAudio = subscription.SubAudio
                    }).ToArray()
                },
                subscriptionMeta = new { subscribers = subscribersMeta, subscribedTo },
                forwarding = new { updatedUtc = forwardingMetrics?.LastTickUtc, outboundTo }
            };
        }
    }
}