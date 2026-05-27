using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Communication.Grpc.ConfigSync;
using GrpcHttp3Demo.Sessions;

namespace GrpcHttp3Demo.Communication.Grpc.Push
{
    public class NotificationService
    {
        private readonly SessionRegistry _sessions;
        private readonly SessionSubscription _subscriptions;
        private readonly PushChannelRegistry _pushChannels;
        private readonly VideoConfigDeliveryService _videoDeliveryService;
        private readonly AudioConfigDeliveryService _audioDeliveryService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(SessionRegistry sessions, SessionSubscription subscriptions, PushChannelRegistry pushChannels, VideoConfigDeliveryService videoDeliveryService, AudioConfigDeliveryService audioDeliveryService, ILogger<NotificationService> logger)
        {
            _sessions = sessions;
            _subscriptions = subscriptions;
            _pushChannels = pushChannels;
            _videoDeliveryService = videoDeliveryService;
            _audioDeliveryService = audioDeliveryService;
            _logger = logger;
        }

        public async Task SendPairRequestAsync(string senderSessionId, string targetSessionId)
        {
            await SendPairEventAsync(senderSessionId, targetSessionId, PairEvent.Types.Op.Request);
        }

        public async Task BroadcastPairAcceptedAsync(string accepterSessionId, string peerSessionId)
        {
            await SendPairEventAsync(accepterSessionId, peerSessionId, PairEvent.Types.Op.Accept);
            await SendPairEventAsync(peerSessionId, accepterSessionId, PairEvent.Types.Op.Accept);
        }

        public async Task SendPairRejectAsync(string senderSessionId, string targetSessionId)
        {
            await SendPairEventAsync(senderSessionId, targetSessionId, PairEvent.Types.Op.Reject);
        }

        public async Task SendUnpairAsync(string senderSessionId, string targetSessionId)
        {
            await SendPairEventAsync(senderSessionId, targetSessionId, PairEvent.Types.Op.Unpair);
        }

        private async Task SendPairEventAsync(string senderSessionId, string targetSessionId, PairEvent.Types.Op op)
        {
            var sender = _sessions.GetSession(senderSessionId);

            var evt = new EventMessage
            {
                SenderSessionId = senderSessionId,
                TargetSessionId = targetSessionId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Pair = new PairEvent
                {
                    Op = op,
                    Peer = sender == null
                        ? null
                        : new EndpointInfo
                        {
                            SessionId = sender.SessionId,
                            DeviceId = sender.DeviceId,
                            Role = sender.Role,
                            RobotGeneration = sender.RobotGeneration,
                            VrVersion = sender.VrVersion
                        }
                }
            };

            await _pushChannels.SendEventAsync(targetSessionId, evt);
            _logger.LogInformation($"[Notification] Sent PairEvent {op} from {senderSessionId} to {targetSessionId}");
        }

        public async Task SendVideoConfigAsync(string senderSessionId, VideoConfig config)
        {
            var targets = _subscriptions.GetVideoConfigTargets(senderSessionId);
            if (targets.Count == 0)
            {
                _logger.LogInformation($"[Notification] VideoConfig has no targets. Sender={senderSessionId}");
                return;
            }

            _logger.LogInformation($"[Notification] Forwarding VideoConfig from {senderSessionId} to {targets.Count} targets with reliability check.");

            foreach (var targetSessionId in targets)
            {
                await SendVideoConfigToTargetAsync(senderSessionId, targetSessionId, config);
            }
        }

        public async Task SendVideoConfigToTargetAsync(string senderSessionId, string targetSessionId, VideoConfig config)
        {
            var tid = targetSessionId;
            var dispatchConfigId = Guid.NewGuid().ToString();

            var configToSend = config.Clone();
            configToSend.ConfigId = dispatchConfigId;

            _ = _videoDeliveryService.SendReliablyAsync(
                dispatchConfigId,
                tid,
                async () =>
                {
                    var evt = new EventMessage
                    {
                        SenderSessionId = senderSessionId,
                        TargetSessionId = tid,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        VideoConfig = configToSend
                    };
                    await _pushChannels.SendEventAsync(tid, evt);
                },
                async () =>
                {
                    _logger.LogWarning($"[Notification] Disconnecting {tid} due to VideoConfig delivery failure.");
                    _sessions.UnregisterGrpc(tid);
                    await Task.CompletedTask;
                }
            );

            await Task.CompletedTask;
        }

        public async Task SendAudioConfigAsync(string senderSessionId, IReadOnlyCollection<string> targets, AudioConfig config)
        {
            if (targets.Count == 0)
            {
                _logger.LogInformation($"[Notification] AudioConfig has no targets. Sender={senderSessionId}");
                return;
            }

            _logger.LogInformation($"[Notification] Forwarding AudioConfig from {senderSessionId} to {targets.Count} targets with reliability check.");

            foreach (var targetSessionId in targets)
            {
                await SendAudioConfigToTargetAsync(senderSessionId, targetSessionId, config);
            }
        }

        public async Task SendAudioConfigToTargetAsync(string senderSessionId, string targetSessionId, AudioConfig config)
        {
            var tid = targetSessionId;
            var dispatchConfigId = Guid.NewGuid().ToString();
            var configToSend = config.Clone();
            configToSend.ConfigId = dispatchConfigId;

            _ = _audioDeliveryService.SendReliablyAsync(
                dispatchConfigId,
                tid,
                async () =>
                {
                    var evt = new EventMessage
                    {
                        SenderSessionId = senderSessionId,
                        TargetSessionId = tid,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        AudioConfig = configToSend
                    };
                    await _pushChannels.SendEventAsync(tid, evt);
                },
                async () =>
                {
                    _logger.LogWarning($"[Notification] Disconnecting {tid} due to AudioConfig delivery failure.");
                    _sessions.UnregisterGrpc(tid);
                    await Task.CompletedTask;
                }
            );

            await Task.CompletedTask;
        }
    }
}

