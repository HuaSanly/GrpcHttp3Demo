using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Communication.Grpc.ConfigSync;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Communication.Grpc.Signaling
{
    /// <summary>
    /// 业务层信令服务：聚合 gRPC 与 WS(/ws/proto) 复用的“用例逻辑”。
    /// Transport（gRPC/WS）只负责鉴权/取 sessionId/编解码，业务流程统一在这里。
    /// </summary>
    public class SignalingAppService
    {
        private readonly SessionRegistry _sessions;
        private readonly SessionPresence _presence;
        private readonly SessionPairing _pairing;
        private readonly SessionSubscription _subscriptions;
        private readonly SessionMemoryStore _memory;
        private readonly PushChannelRegistry _pushChannels;
        private readonly NotificationService _notificationService;
        private readonly VideoConfigDeliveryService _videoDeliveryService;
        private readonly AudioConfigDeliveryService _audioDeliveryService;
        private readonly ILogger<SignalingAppService> _logger;

        public SignalingAppService(SessionRegistry sessions, SessionPresence presence, SessionPairing pairing, SessionSubscription subscriptions, SessionMemoryStore memory, PushChannelRegistry pushChannels, NotificationService notificationService, VideoConfigDeliveryService videoDeliveryService, AudioConfigDeliveryService audioDeliveryService, ILogger<SignalingAppService> logger)
        {
            _sessions = sessions;
            _presence = presence;
            _pairing = pairing;
            _subscriptions = subscriptions;
            _memory = memory;
            _pushChannels = pushChannels;
            _notificationService = notificationService;
            _videoDeliveryService = videoDeliveryService;
            _audioDeliveryService = audioDeliveryService;
            _logger = logger;
        }

        public RegisterResponse Register(RegisterRequest request, string clientIp, int clientPort)
        {
            _logger.LogInformation($"[Register] DeviceId={request.DeviceId}, Role={request.Role}, Generation={request.RobotGeneration}, VrVersion={request.VrVersion}, IP={clientIp}:{clientPort}");

            var sessionId = Guid.NewGuid().ToString();
            _sessions.RegisterGrpc(sessionId, request.DeviceId, request.Role, clientIp, clientPort, request.RobotGeneration, request.VrVersion);

            return new RegisterResponse
            {
                Success = true,
                Message = "Registered Successfully",
                SessionId = sessionId,
                ClientIp = clientIp,
                ClientPort = clientPort
            };
        }

        public HeartbeatAck Ping(string? sessionId, Heartbeat request)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _presence.UpdateHeartbeat(sessionId);
            }

            return new HeartbeatAck
            {
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PushConnected = !string.IsNullOrEmpty(sessionId) && _pushChannels.IsConnected(sessionId)
            };
        }

        public async Task<PairResponse> PairAsync(string? senderSessionId, PairRequest request)
        {
            _logger.LogInformation($"[Pair] Sender={senderSessionId}, Op={request.Op}, PeerSession={request.PeerSessionId}, State={request.State}");

            if (string.IsNullOrEmpty(senderSessionId))
            {
                return new PairResponse { Success = false, Message = "Missing session" };
            }

            if (string.IsNullOrEmpty(request.PeerSessionId))
            {
                return new PairResponse { Success = false, Message = "Missing peer_session_id" };
            }

            // 目标会话必须存在（否则事件通知/配对都无意义）
            if (_sessions.GetSession(request.PeerSessionId) == null)
            {
                return new PairResponse { Success = false, Message = $"Peer session not found: {request.PeerSessionId}" };
            }

            var resp = await HandlePairingAsync(senderSessionId, request);
            return resp;
        }

        public async Task<SubscribeResponse> SubscribeAsync(string? subscriberSessionId, SubscribeRequest request)
        {
            _logger.LogInformation($"[Subscribe] Subscriber={subscriberSessionId}, Op={request.Op}, Publisher={request.PublisherSessionId}, Video={request.SubVideo}, Pose={request.SubPose}, Audio={request.SubAudio}");

            if (string.IsNullOrEmpty(subscriberSessionId))
            {
                return new SubscribeResponse { Success = false, Message = "Missing session" };
            }

            await HandleSubscribeAsync(subscriberSessionId, request);
            return new SubscribeResponse
            {
                Success = true,
                Message = request.Op == SubscribeRequest.Types.Operation.Subscribe ? "Subscribed" : "Unsubscribed"
            };
        }

        public ListUnpairedResponse ListUnpaired(string? requesterSessionId, ListUnpairedRequest request)
        {
            _logger.LogInformation($"[ListUnpaired] Requester={requesterSessionId}, DesiredRole={request.DesiredRole}");

            if (string.IsNullOrEmpty(requesterSessionId))
            {
                return new ListUnpairedResponse();
            }

            var requester = _sessions.GetSession(requesterSessionId);
            if (requester == null)
            {
                return new ListUnpairedResponse();
            }

            var desiredRole = request.DesiredRole;
            if (desiredRole == RegisterRequest.Types.EndpointType.Unknown)
            {
                desiredRole = requester.Role == RegisterRequest.Types.EndpointType.Robot
                    ? RegisterRequest.Types.EndpointType.Vr
                    : RegisterRequest.Types.EndpointType.Robot;
            }

            var endpoints = _pairing.GetUnpairedByRole(desiredRole);

            var response = new ListUnpairedResponse();
            response.Endpoints.AddRange(endpoints);
            return response;
        }

        public GetP2pInfoResponse GetP2pInfo(string? callerSessionId)
        {
            if (string.IsNullOrEmpty(callerSessionId))
            {
                return new GetP2pInfoResponse { Ready = false };
            }

            var peerSessionId = _pairing.GetPairedSession(callerSessionId);
            if (string.IsNullOrEmpty(peerSessionId))
            {
                return new GetP2pInfoResponse { Ready = false };
            }

            // Ready only when BOTH sides have completed UDP HELLO to the server (server observed remote endpoint).
            if (!_memory.TryGetEndpointBySession(callerSessionId, out var callerEp) || callerEp == null)
            {
                return new GetP2pInfoResponse { Ready = false };
            }
            if (!_memory.TryGetEndpointBySession(peerSessionId, out var peerEp) || peerEp == null)
            {
                return new GetP2pInfoResponse { Ready = false };
            }

            var key = _pairing.GetOrCreateP2pSharedKey(callerSessionId, peerSessionId);
            return new GetP2pInfoResponse
            {
                Ready = true,
                PeerSessionId = peerSessionId,
                PeerPublicIp = peerEp.Address.ToString(),
                PeerPublicPort = peerEp.Port,
                SharedKey = Google.Protobuf.ByteString.CopyFrom(key)
            };
        }

        private async Task<PairResponse> HandlePairingAsync(string senderSessionId, PairRequest req)
        {
            var targetSessionId = req.PeerSessionId;
            if (string.IsNullOrEmpty(targetSessionId))
            {
                return new PairResponse { Success = false, Message = "Missing peer_session_id" };
            }

            var senderPaired = _pairing.GetPairedSession(senderSessionId);
            var targetPaired = _pairing.GetPairedSession(targetSessionId);

            bool IsPairedWith(string? paired, string expected) => !string.IsNullOrEmpty(paired) && string.Equals(paired, expected, StringComparison.Ordinal);

            // Unpair：优先以服务器当前配对关系为准，避免客户端传错 peer_session_id。
            if (req.Op == PairRequest.Types.Operation.Unpair)
            {
                var partner = _pairing.GetPairedSession(senderSessionId);
                if (!string.IsNullOrEmpty(partner))
                {
                    _pairing.UnpairSession(senderSessionId);
                    await _notificationService.SendUnpairAsync(senderSessionId, partner);
                }
                return new PairResponse { Success = true, Message = "Unpair processed" };
            }

            if (req.State == PairRequest.Types.PairState.Request)
            {
                // 已经配对（无论是自己还是对方），不允许再发起新的配对覆盖旧关系。
                if (!string.IsNullOrEmpty(senderPaired) && !IsPairedWith(senderPaired, targetSessionId))
                {
                    return new PairResponse { Success = false, Message = $"Sender already paired with {senderPaired}." };
                }

                if (!string.IsNullOrEmpty(targetPaired) && !IsPairedWith(targetPaired, senderSessionId))
                {
                    return new PairResponse { Success = false, Message = $"Peer already paired with {targetPaired}." };
                }

                // 如果已经是同一对，则视为幂等成功，不重复通知。
                if (IsPairedWith(senderPaired, targetSessionId) && IsPairedWith(targetPaired, senderSessionId))
                {
                    return new PairResponse { Success = true, Message = "Already paired" };
                }

                await _notificationService.SendPairRequestAsync(senderSessionId, targetSessionId);
                return new PairResponse { Success = true, Message = "Pair request sent" };
            }

            if (req.State == PairRequest.Types.PairState.Accept)
            {
                // 只允许“未配对 -> 配对”或“原配对双方重复 Accept（幂等）”。
                if (!string.IsNullOrEmpty(senderPaired) && !IsPairedWith(senderPaired, targetSessionId))
                {
                    return new PairResponse { Success = false, Message = $"Sender already paired with {senderPaired}." };
                }

                if (!string.IsNullOrEmpty(targetPaired) && !IsPairedWith(targetPaired, senderSessionId))
                {
                    return new PairResponse { Success = false, Message = $"Peer already paired with {targetPaired}." };
                }

                _pairing.PairSessions(senderSessionId, targetSessionId);
                await _notificationService.BroadcastPairAcceptedAsync(senderSessionId, targetSessionId);
                return new PairResponse { Success = true, Message = "Pair accepted" };
            }

            if (req.State == PairRequest.Types.PairState.Reject)
            {
                await _notificationService.SendPairRejectAsync(senderSessionId, targetSessionId);
                return new PairResponse { Success = true, Message = "Pair rejected" };
            }

            return new PairResponse { Success = true, Message = "Pair processed" };
        }

        private Task HandleSubscribeAsync(string subscriberSessionId, SubscribeRequest req)
        {
            var publisherSessionId = req.PublisherSessionId;
            if (string.IsNullOrEmpty(publisherSessionId)) return Task.CompletedTask;

            var isSub = req.Op == SubscribeRequest.Types.Operation.Subscribe;
            _subscriptions.UpdateSubscription(publisherSessionId, subscriberSessionId, isSub, req.SubVideo, req.SubPose, req.SubAudio);

            // [Replay] If this is a new video subscription, check for cached config
            if (isSub && req.SubVideo)
            {
                var pubSession = _sessions.GetSession(publisherSessionId);
                // Check if publisher has a cached config
                if (pubSession?.LastVideoConfig != null)
                {
                    _logger.LogInformation($"[Subscribe] Replaying cached VideoConfig from {publisherSessionId} to new subscriber {subscriberSessionId}");

                    var config = pubSession.LastVideoConfig.Clone();
                    _ = _notificationService.SendVideoConfigToTargetAsync(publisherSessionId, subscriberSessionId, config);
                }
            }

            if (isSub && req.SubAudio)
            {
                var pubSession = _sessions.GetSession(publisherSessionId);
                if (pubSession?.LastAudioConfig != null)
                {
                    _logger.LogInformation($"[Subscribe] Replaying cached AudioConfig from {publisherSessionId} to new subscriber {subscriberSessionId}");

                    var config = pubSession.LastAudioConfig.Clone();
                    _ = _notificationService.SendAudioConfigToTargetAsync(publisherSessionId, subscriberSessionId, config);
                }
            }

            return Task.CompletedTask;
        }

        public async Task<VideoConfigAck> PublishVideoConfigAsync(string? senderSessionId, VideoConfig request)
        {
            if (string.IsNullOrEmpty(senderSessionId))
            {
                return new VideoConfigAck { Success = false, Message = "Missing session" };
            }

            if (_sessions.GetSession(senderSessionId) == null)
            {
                return new VideoConfigAck { Success = false, Message = $"Session not found: {senderSessionId}" };
            }

            // [Cache] Store the last config for late subscribers
            var session = _sessions.GetSession(senderSessionId);
            if (session != null)
            {
                session.LastVideoConfig = request;
            }

            await _notificationService.SendVideoConfigAsync(senderSessionId, request);
            return new VideoConfigAck { Success = true, Message = "VideoConfig forwarded" };
        }

        public void AckVideoConfig(string? senderSessionId, VideoConfigAck request)
        {
            if (string.IsNullOrEmpty(senderSessionId)) return;
            _logger.LogInformation($"[AckVideoConfig] Received ACK from {senderSessionId} for ConfigId={request.ConfigId}");
            
            _videoDeliveryService.CompleteAck(request.ConfigId, request.Success);
        }

        public async Task<AudioConfigAck> PublishAudioConfigAsync(string? senderSessionId, AudioConfig request)
        {
            if (string.IsNullOrEmpty(senderSessionId))
            {
                return new AudioConfigAck { Success = false, Message = "Missing session" };
            }

            var session = _sessions.GetSession(senderSessionId);
            if (session == null)
            {
                return new AudioConfigAck { Success = false, Message = $"Session not found: {senderSessionId}" };
            }

            session.LastAudioConfig = request;

            var targets = _subscriptions.GetAudioConfigTargets(senderSessionId);
            await _notificationService.SendAudioConfigAsync(senderSessionId, targets, request);
            return new AudioConfigAck { Success = true, Message = "AudioConfig forwarded" };
        }

        public void AckAudioConfig(string? senderSessionId, AudioConfigAck request)
        {
            if (string.IsNullOrEmpty(senderSessionId)) return;
            _logger.LogInformation($"[AckAudioConfig] Received ACK from {senderSessionId} for ConfigId={request.ConfigId}");

            _audioDeliveryService.CompleteAck(request.ConfigId, request.Success);
        }
    }
}

