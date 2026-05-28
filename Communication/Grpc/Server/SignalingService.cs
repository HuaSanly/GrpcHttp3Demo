using Grpc.Core;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Sessions;
using Google.Protobuf.WellKnownTypes;
using SignalingAppService = GrpcHttp3Demo.Communication.Grpc.Signaling.SignalingAppService;

namespace GrpcHttp3Demo.Communication.Grpc.Server
{
    public class SignalingService : global::GrpcHttp3Demo.Protos.Signaling.SignalingBase
    {
        private readonly SessionRegistry _sessions;
        private readonly PushChannelRegistry _pushChannels;
        private readonly SignalingAppService _appService;
        private readonly SessionPresence _presence;
        private readonly SignalingMetricsService _metrics;
        private readonly ILogger<SignalingService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SignalingService(SessionRegistry sessions, PushChannelRegistry pushChannels, SignalingAppService appService, SessionPresence presence, SignalingMetricsService metrics, ILogger<SignalingService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _sessions = sessions;
            _pushChannels = pushChannels;
            _appService = appService;
            _presence = presence;
            _metrics = metrics;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        // 模拟 Controller 的属性
        protected HttpContext Context => _httpContextAccessor.HttpContext!;

        private static string? TryGetSessionIdFromMetadata(ServerCallContext context)
        {
            // gRPC metadata 优先：比 HttpContext 更直接
            var value = context.RequestHeaders
                .FirstOrDefault(h => string.Equals(h.Key, "session-id", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private string? ResolveSessionId(ServerCallContext context)
        {
            // 1) gRPC metadata
            var fromMetadata = TryGetSessionIdFromMetadata(context);
            if (!string.IsNullOrEmpty(fromMetadata)) return fromMetadata;

            // 2) HttpContext headers（有些 hosting/代理场景下会把 metadata 映射进来）
            if (_httpContextAccessor.HttpContext != null)
            {
                var fromHttp = _httpContextAccessor.HttpContext.Request.Headers["session-id"].ToString();
                if (!string.IsNullOrEmpty(fromHttp)) return fromHttp;
            }

            return null;
        }

        private string RequireGrpcSessionId(ServerCallContext context)
        {
            var sessionId = ResolveSessionId(context);
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing session-id metadata"));
            }

            var session = _sessions.GetSession(sessionId);
            if (session == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Session not found: {sessionId}"));
            }

            return sessionId;
        }

        public override Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
        {
            _metrics.RecordInboundRegister();
            // 使用 Context 属性获取 IP，像 Controller 一样
            var ip = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var port = Context.Connection.RemotePort;

            var resp = _appService.Register(request, ip, port);
            _metrics.RecordOutboundRegisterResponse();
            return Task.FromResult(resp);
        }

        public override async Task<PairResponse> Pair(PairRequest request, ServerCallContext context)
        {
            _metrics.RecordInboundPair();
            var resp = await _appService.PairAsync(RequireGrpcSessionId(context), request);
            _metrics.RecordOutboundPairResponse();
            return resp;
        }

        public override async Task<SubscribeResponse> Subscribe(SubscribeRequest request, ServerCallContext context)
        {
            _metrics.RecordInboundSubscribe();
            var resp = await _appService.SubscribeAsync(RequireGrpcSessionId(context), request);
            _metrics.RecordOutboundSubscribeResponse();
            return resp;
        }

        public override async Task<ListUnpairedResponse> ListUnpaired(ListUnpairedRequest request, ServerCallContext context)
        {
            _metrics.RecordInboundListUnpaired();
            var resp = _appService.ListUnpaired(RequireGrpcSessionId(context), request);
            _metrics.RecordOutboundListUnpairedResponse();
            return resp;
        }

        public override Task<HeartbeatAck> Ping(Heartbeat request, ServerCallContext context)
        {
            _metrics.RecordInboundPing();
            var resp = _appService.Ping(RequireGrpcSessionId(context), request);
            _metrics.RecordOutboundPingAck();
            return Task.FromResult(resp);
        }

        public override Task<GetP2pInfoResponse> GetP2pInfo(Empty request, ServerCallContext context)
        {
            var sessionId = RequireGrpcSessionId(context);
            var resp = _appService.GetP2pInfo(sessionId);
            return Task.FromResult(resp);
        }

        public override async Task<VideoConfigAck> PublishVideoConfig(VideoConfig request, ServerCallContext context)
        {
            var senderSessionId = RequireGrpcSessionId(context);
            var resp = await _appService.PublishVideoConfigAsync(senderSessionId, request);
            return resp;
        }

        public override async Task<Google.Protobuf.WellKnownTypes.Empty> AckVideoConfig(VideoConfigAck request, ServerCallContext context)
        {
            var senderSessionId = RequireGrpcSessionId(context);
            // 这里处理接收端的 ACK
            // 比如通知 NotificationService 里的重试循环停止
            _appService.AckVideoConfig(senderSessionId, request);
            
            return new Google.Protobuf.WellKnownTypes.Empty();
        }

        public override async Task<AudioConfigAck> PublishAudioConfig(AudioConfig request, ServerCallContext context)
        {
            var senderSessionId = RequireGrpcSessionId(context);
            var resp = await _appService.PublishAudioConfigAsync(senderSessionId, request);
            return resp;
        }

        public override async Task<Google.Protobuf.WellKnownTypes.Empty> AckAudioConfig(AudioConfigAck request, ServerCallContext context)
        {
            var senderSessionId = RequireGrpcSessionId(context);
            _appService.AckAudioConfig(senderSessionId, request);

            return new Google.Protobuf.WellKnownTypes.Empty();
        }

        public override async Task EventStream(EventSubscribe request, IServerStreamWriter<EventMessage> responseStream, ServerCallContext context)
        {
            _metrics.RecordInboundEventStream();
            var sessionId = string.IsNullOrEmpty(request.SessionId) ? RequireGrpcSessionId(context) : request.SessionId;

            var session = _sessions.GetSession(sessionId);
            if (session == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Session not found: {sessionId}"));
            }

            var sender = new GrpcEventStreamSender(responseStream, _metrics);
            _pushChannels.Attach(sessionId, sender);
            _presence.MarkTransportConnected(sessionId);
            _logger.LogInformation($"[EventStream] Attached gRPC event stream sender: {sessionId}");

            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // client disconnected
            }
            finally
            {
                _presence.MarkTransportDisconnected(sessionId, "grpc_event_stream_closed");
                _pushChannels.Detach(sessionId, sender);
                _logger.LogInformation($"[EventStream] Detached gRPC event stream sender: {sessionId}");
            }
        }
    }
}

