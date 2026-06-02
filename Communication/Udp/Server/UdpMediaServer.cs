using System.Net;
using System.Net.Sockets;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Communication.Udp.Binding;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Communication.Udp.Parsing;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Models.Udp;
using GrpcHttp3Demo.Sessions;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Communication.Udp.Server
{
    public class UdpMediaServer : BackgroundService
    {
        private readonly Socket _udpSocket;
        private readonly SessionMemoryStore _memory;
        private readonly UdpSessionBindingService _udpBindings;
        private readonly UdpMetricsService _metrics;
        private readonly UdpLinkMetricsService _linkMetrics;
        private readonly SignalingMetricsService _signalingMetrics;
        private readonly SessionPresence _presence;
        private readonly SessionLivenessOptions _livenessOptions;
        private readonly ILogger<UdpMediaServer> _logger;
        private readonly int _port;
        private readonly int _receiveDatagramBufferBytes;
        private readonly int? _receiveSocketBufferBytes;
        private readonly int? _sendSocketBufferBytes;
        private readonly Socket[] _receiveSockets;
        private readonly int _receivePipelineCount;
        private readonly bool _reusePortEnabled;
        private readonly bool _nonBlockingSend;

        // Optional data-plane keepalive ack (DACK): per remote endpoint, at most once per 5 seconds.
        private static readonly byte[] DackBytes = "DACK"u8.ToArray();
        private static readonly byte[] AckBytes = "ACK"u8.ToArray();
        private static readonly byte[] PongBytes = "PONG"u8.ToArray();
        private const byte SystemMonitorPrefix = 0x07;
        private const byte TopologyMonitorPrefix = 0x08;
        private const long DataActivityUpdateIntervalMs = 1000;
        private const long DackIntervalMs = 5000;
        private const int LinuxReusePortOption = 15;

        public UdpMediaServer(SessionMemoryStore memory, UdpSessionBindingService udpBindings, UdpMetricsService metrics, UdpLinkMetricsService linkMetrics, SignalingMetricsService signalingMetrics, SessionPresence presence, SessionLivenessOptions livenessOptions, ILogger<UdpMediaServer> logger, IConfiguration configuration)
        {
            _memory = memory;
            _udpBindings = udpBindings;
            _metrics = metrics;
            _linkMetrics = linkMetrics;
            _signalingMetrics = signalingMetrics;
            _presence = presence;
            _livenessOptions = livenessOptions;
            _logger = logger;
            _port = configuration.GetValue<int>("MediaServer:UdpPort", 7778);
            _receiveDatagramBufferBytes = Math.Clamp(
                configuration.GetValue<int?>("MediaServer:UdpSocket:ReceiveDatagramBufferBytes") ?? 65_535,
                2_048,
                65_535);
            _receiveSocketBufferBytes = configuration.GetValue<int?>("MediaServer:UdpSocket:ReceiveBufferBytes");
            _sendSocketBufferBytes = configuration.GetValue<int?>("MediaServer:UdpSocket:SendBufferBytes");

            var requestedPipelineCount = Math.Clamp(
                configuration.GetValue<int?>("MediaServer:UdpSocket:ReceivePipelineCount") ?? 1,
                1,
                Math.Max(1, Environment.ProcessorCount * 2));
            if (requestedPipelineCount > 1 && !OperatingSystem.IsLinux())
            {
                _logger.LogWarning("UDP receive pipeline count {PipelineCount} requested, but SO_REUSEPORT multi-bind is enabled only on Linux. Falling back to one receive pipeline.", requestedPipelineCount);
                requestedPipelineCount = 1;
            }

            var enableReusePort = requestedPipelineCount > 1;
            _receiveSockets = CreateReceiveSockets(requestedPipelineCount, enableReusePort);
            _udpSocket = _receiveSockets[0];
            _receivePipelineCount = _receiveSockets.Length;
            _reusePortEnabled = enableReusePort && _receivePipelineCount > 1;
            _nonBlockingSend = configuration.GetValue<bool?>("MediaServer:UdpSocket:NonBlockingSend") ?? _reusePortEnabled;
            if (_nonBlockingSend)
            {
                for (var i = 0; i < _receiveSockets.Length; i++)
                {
                    _receiveSockets[i].Blocking = false;
                }
            }
        }

        private Socket[] CreateReceiveSockets(int requestedPipelineCount, bool enableReusePort)
        {
            var sockets = new List<Socket>(requestedPipelineCount);
            try
            {
                for (var i = 0; i < requestedPipelineCount; i++)
                {
                    sockets.Add(CreateBoundSocket(enableReusePort));
                }

                return sockets.ToArray();
            }
            catch (Exception ex) when (enableReusePort)
            {
                CloseSockets(sockets);
                _logger.LogWarning(ex, "UDP SO_REUSEPORT setup failed; falling back to one receive pipeline.");
                return new[] { CreateBoundSocket(enableReusePort: false) };
            }
            catch
            {
                CloseSockets(sockets);
                throw;
            }
        }

        private Socket CreateBoundSocket(bool enableReusePort)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                if (enableReusePort)
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)LinuxReusePortOption, 1);
                }

                // Tune OS socket buffers to reduce kernel drops under high bitrate/bursty traffic.
                // These are best-effort; OS may clamp to system limits.
                if (_receiveSocketBufferBytes is int rb && rb > 0)
                {
                    socket.ReceiveBufferSize = rb;
                }

                if (_sendSocketBufferBytes is int sb && sb > 0)
                {
                    socket.SendBufferSize = sb;
                }

                socket.Bind(new IPEndPoint(IPAddress.Any, _port));
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private void CloseReceiveSockets()
        {
            CloseSockets(_receiveSockets);
        }

        private static void CloseSockets(IEnumerable<Socket> sockets)
        {
            foreach (var socket in sockets)
            {
                try
                {
                    socket.Close();
                }
                catch
                {
                    // Socket close is best-effort during shutdown/fallback.
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "UDP Media Server started on port {Port}. receiveDatagramBufferBytes={ReceiveDatagramBufferBytes}, receivePipelines={ReceivePipelines}, reusePort={ReusePort}, nonBlockingSend={NonBlockingSend}",
                _port,
                _receiveDatagramBufferBytes,
                _receivePipelineCount,
                _reusePortEnabled,
                _nonBlockingSend);

            using var closeRegistration = stoppingToken.Register(static state =>
            {
                CloseSockets((Socket[])state!);
            }, _receiveSockets);

            var receiveTasks = new Task[_receiveSockets.Length];
            for (var i = 0; i < _receiveSockets.Length; i++)
            {
                var socket = _receiveSockets[i];
                var pipelineIndex = i;
                receiveTasks[i] = Task.Factory.StartNew(
                    () => ReceiveLoop(socket, pipelineIndex, stoppingToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            _ = Task.Factory.StartNew(
                () => SystemMonitorPushLoop(stoppingToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                await Task.WhenAll(receiveTasks).ConfigureAwait(false);
            }
            finally
            {
                CloseReceiveSockets();
            }
        }

        private void SystemMonitorPushLoop(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = Environment.TickCount64;
                    var targets = _memory.SystemMonitorTargets;
                    var linkTargets = _memory.LinkMonitorTargets;
                    object? udpSnapshot = null;
                    object? signalingSnapshot = null;
                    object? onlineSnapshot = null;
                    object? runtimeTablesSnapshot = null;
                    Dictionary<SystemMonitorTopicMask, string[]>? topicNamesCache = null;

                    for (var i = 0; i < targets.Length; i++)
                    {
                        var target = targets[i];
                        if (!target.TryMarkDue(now)) continue;

                        if ((target.Topics & SystemMonitorTopicMask.Standard) != 0)
                        {
                            var payload = BuildSystemMonitorPayload(
                                target,
                                ref udpSnapshot,
                                ref signalingSnapshot,
                                ref onlineSnapshot,
                                ref runtimeTablesSnapshot,
                                ref topicNamesCache);
                            target.Link.RecordForwardPlanned(payload.Length);
                            SendDirect(_udpSocket, target.Endpoint, payload, payload.Length, SystemMonitorPrefix, target.Link);
                        }
                    }

                    for (var i = 0; i < linkTargets.Length; i++)
                    {
                        var target = linkTargets[i];
                        if (!target.TryMarkDue(now)) continue;

                        var payload = BuildTopologyMonitorPayload(target);
                        target.Link.RecordForwardPlanned(payload.Length);
                        SendDirect(_udpSocket, target.Endpoint, payload, payload.Length, TopologyMonitorPrefix, target.Link);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UDP system monitor push failed");
                }

                stoppingToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            }
        }

        private byte[] BuildSystemMonitorPayload(
            UdpSystemMonitorTarget target,
            ref object? udpSnapshot,
            ref object? signalingSnapshot,
            ref object? onlineSnapshot,
            ref object? runtimeTablesSnapshot,
            ref Dictionary<SystemMonitorTopicMask, string[]>? topicNamesCache)
        {
            var payloadTopics = target.Topics & SystemMonitorTopicMask.Standard;
            topicNamesCache ??= new Dictionary<SystemMonitorTopicMask, string[]>();
            if (!topicNamesCache.TryGetValue(payloadTopics, out var topics))
            {
                topics = SessionSubscription.ToTopicNames(payloadTopics);
                topicNamesCache[payloadTopics] = topics;
            }

            if ((payloadTopics & SystemMonitorTopicMask.UdpGlobal) != 0 && udpSnapshot == null)
            {
                udpSnapshot = _metrics.Snapshot();
            }

            if ((payloadTopics & SystemMonitorTopicMask.SignalingRates) != 0 && signalingSnapshot == null)
            {
                signalingSnapshot = _signalingMetrics.Snapshot();
            }

            if ((payloadTopics & SystemMonitorTopicMask.OnlineSummary) != 0 && onlineSnapshot == null)
            {
                onlineSnapshot = _presence.GetOnlineRoleSnapshot(_livenessOptions.Timeout);
            }

            if ((payloadTopics & SystemMonitorTopicMask.RuntimeTables) != 0 && runtimeTablesSnapshot == null)
            {
                runtimeTablesSnapshot = _memory.Snapshot();
            }

            var envelope = new
            {
                sequence = target.NextSequence(),
                serverTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                publisherSessionId = SystemPublishers.MonitorPublisherSessionId,
                targetSessionId = target.SessionId,
                prefix = "0x07",
                topics,
                udp = (payloadTopics & SystemMonitorTopicMask.UdpGlobal) != 0 ? udpSnapshot : null,
                signaling = (payloadTopics & SystemMonitorTopicMask.SignalingRates) != 0 ? signalingSnapshot : null,
                online = (payloadTopics & SystemMonitorTopicMask.OnlineSummary) != 0 ? onlineSnapshot : null,
                runtimeTables = (payloadTopics & SystemMonitorTopicMask.RuntimeTables) != 0 ? runtimeTablesSnapshot : null
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var payload = new byte[jsonBytes.Length + 1];
            payload[0] = SystemMonitorPrefix;
            Buffer.BlockCopy(jsonBytes, 0, payload, 1, jsonBytes.Length);
            return payload;
        }

        private byte[] BuildTopologyMonitorPayload(UdpTopologyMonitorTarget target)
        {
            var linkSnapshot = _linkMetrics.SnapshotByTopologyId(target.TopologyId);

            var envelope = new
            {
                sequence = target.NextSequence(),
                serverTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                publisherSessionId = SystemPublishers.LinkMonitorPublisherSessionId,
                targetSessionId = target.SessionId,
                prefix = "0x08",
                topics = new[] { "udp_link_metrics" },
                linksUpdatedUtc = _linkMetrics.LastTickUtc,
                subscribedTopologyId = target.TopologyId,
                links = linkSnapshot
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var payload = new byte[jsonBytes.Length + 1];
            payload[0] = TopologyMonitorPrefix;
            Buffer.BlockCopy(jsonBytes, 0, payload, 1, jsonBytes.Length);
            return payload;
        }

        private void ReceiveLoop(Socket socket, int pipelineIndex, CancellationToken stoppingToken)
        {
            var buffer = new byte[_receiveDatagramBufferBytes];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_nonBlockingSend && !socket.Poll(100_000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        remote = new IPEndPoint(IPAddress.Any, 0);
                        int length;
                        try
                        {
                            length = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remote);
                        }
                        catch (SocketException ex) when (_nonBlockingSend && IsWouldBlock(ex.SocketErrorCode))
                        {
                            break;
                        }

                        ProcessDatagram(socket, buffer, length, (IPEndPoint)remote);

                        if (!_nonBlockingSend)
                        {
                            break;
                        }
                    }
                }
                catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex) when (stoppingToken.IsCancellationRequested || ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UDP Receive Error on pipeline {PipelineIndex}", pipelineIndex);
                }
            }
        }

        private void ProcessDatagram(Socket socket, byte[] buffer, int length, IPEndPoint remoteEp)
        {
            if (length == 0) return;

            byte prefix = buffer[0];
            _metrics.RecordRxPacket(length, prefix);

            switch (prefix)
            {
                case (byte)'H':
                case (byte)'P':
                    HandleControlPacket(socket, buffer.AsSpan(0, length), remoteEp);
                    return;

                case 0x03:
                    if (_memory.TryGetFeedbackForward(remoteEp, out var feedbackTarget))
                    {
                        feedbackTarget.IngressLink.RecordReceived(length);
                        feedbackTarget.IngressLink.RecordRouteMatched(length);
                        feedbackTarget.EgressLink.RecordForwardPlanned(length);
                        feedbackTarget.Counter.RecordFeedback(length);
                        SendDirect(socket, feedbackTarget.RobotEndpoint, buffer, length, prefix, feedbackTarget.EgressLink);
                    }
                    else
                    {
                        RecordIngressRouteMiss(remoteEp, UdpLinkMediaKind.Feedback, length, UdpLinkFailureKind.NoRoute);
                    }
                    return;
            }

            if (!TryMapMediaKind(prefix, out var mediaKind))
            {
                return;
            }

            if (!_memory.TryGetSourceRoute(remoteEp, out var route) || route == null)
            {
                RecordIngressRouteMiss(remoteEp, mediaKind, length, UdpLinkFailureKind.NoRoute);
                return;
            }

            RecordDataActivityAndMaybeDack(route, remoteEp, socket);

            if (!route.TryGetRoute(prefix, out var mediaRoute))
            {
                RecordIngressRouteMiss(remoteEp, mediaKind, length, UdpLinkFailureKind.NoRoute);
                return;
            }

            mediaRoute.IngressLink.RecordReceived(length);

            var targets = mediaRoute.Targets;
            if (targets.Length == 0)
            {
                mediaRoute.IngressLink.RecordRouteMiss(length, UdpLinkFailureKind.NoTarget);
                return;
            }

            mediaRoute.IngressLink.RecordRouteMatched(length);
            ForwardMediaTargets(socket, mediaKind, targets, buffer, length, prefix);
        }

        private void ForwardMediaTargets(Socket socket, UdpLinkMediaKind mediaKind, ImmutableArray<UdpForwardTarget> targets, byte[] buffer, int length, byte prefix)
        {
            for (var i = 0; i < targets.Length; i++)
            {
                var dest = targets[i];
                RecordForwardCounter(dest.Counter, mediaKind, length);
                dest.Link.RecordForwardPlanned(length);
                SendDirect(socket, dest.Endpoint, buffer, length, prefix, dest.Link);
            }
        }

        private static void RecordForwardCounter(ForwardEdgeCounter counter, UdpLinkMediaKind mediaKind, int length)
        {
            switch (mediaKind)
            {
                case UdpLinkMediaKind.Video:
                    counter.RecordVideo(length);
                    break;
                case UdpLinkMediaKind.Pose:
                    counter.RecordPose(length);
                    break;
                case UdpLinkMediaKind.Audio:
                    counter.RecordAudio(length);
                    break;
                case UdpLinkMediaKind.TelemetryLowRate:
                case UdpLinkMediaKind.TelemetryHighRate:
                    counter.RecordTelemetry(length);
                    break;
            }
        }

        private void SendDirect(Socket socket, EndPoint endpoint, byte[] buffer, int length, byte prefix, UdpRuntimeLink? link)
        {
            try
            {
                _metrics.RecordTxAttempt(length, prefix);
                link?.RecordSendAttempt(length);
                socket.SendTo(buffer, 0, length, SocketFlags.None, endpoint);
                _metrics.RecordTxSuccess(length, prefix);
                link?.RecordSendSuccess(length);
            }
            catch (SocketException ex)
            {
                _metrics.RecordTxFailure(length, prefix, ex.SocketErrorCode);
                link?.RecordSendFailure(length, ex.SocketErrorCode);
            }
            catch (Exception)
            {
                _metrics.RecordTxFailure(length, prefix, SocketError.SocketError);
                link?.RecordSendFailure(length, SocketError.SocketError);
            }
        }

        private void RecordDataActivityAndMaybeDack(UdpSourceRoute route, IPEndPoint remoteEp, Socket socket)
        {
            var now = Environment.TickCount64;
            if (route.TryMarkDataActivityDue(now, DataActivityUpdateIntervalMs))
            {
                _udpBindings.UpdateDataActivity(route.SourceSessionId);
            }

            if (route.TryMarkDackDue(now, DackIntervalMs))
            {
                SendControl(socket, remoteEp, DackBytes);
            }
        }

        private void RecordIngressRouteMiss(IPEndPoint remoteEp, UdpLinkMediaKind mediaKind, int length, UdpLinkFailureKind reason)
        {
            var sessionId = _memory.GetSessionByEndpoint(remoteEp);
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            if (!_linkMetrics.TryGetIngressLink(sessionId, mediaKind, out var link) || link == null)
            {
                return;
            }

            link.RecordReceived(length);
            link.RecordRouteMiss(length, reason);
        }

        private static bool TryMapMediaKind(byte prefix, out UdpLinkMediaKind mediaKind)
        {
            switch (prefix)
            {
                case 0x01:
                    mediaKind = UdpLinkMediaKind.Video;
                    return true;
                case 0x02:
                    mediaKind = UdpLinkMediaKind.Pose;
                    return true;
                case 0x03:
                    mediaKind = UdpLinkMediaKind.Feedback;
                    return true;
                case 0x04:
                    mediaKind = UdpLinkMediaKind.Audio;
                    return true;
                case 0x05:
                    mediaKind = UdpLinkMediaKind.TelemetryLowRate;
                    return true;
                case 0x06:
                    mediaKind = UdpLinkMediaKind.TelemetryHighRate;
                    return true;
                default:
                    mediaKind = default;
                    return false;
            }
        }

        private static bool IsWouldBlock(SocketError error)
        {
            return error == SocketError.WouldBlock;
        }

        private void SendControl(Socket socket, IPEndPoint remoteEp, byte[] responseBytes)
        {
            SendDirect(socket, remoteEp, responseBytes, responseBytes.Length, responseBytes[0], link: null);
        }

        private void HandleControlPacket(Socket socket, ReadOnlySpan<byte> buffer, IPEndPoint remoteEp)
        {
            try
            {
                if (!UdpProtocolParser.TryParseControlPacket(buffer, out var packet))
                {
                    return;
                }

                static string Redact(string value, int keep = 8)
                {
                    if (string.IsNullOrEmpty(value)) return "<empty>";
                    if (value.Length <= keep) return value;
                    return value.Substring(0, keep) + "...";
                }

                // 打印收到的控制包（脱敏：SessionId/Signature 都是敏感信息）
                // HELLO 通常是低频关键事件：Info；PING 较高频：Debug
                if (packet.Type == UdpControlPacketType.Hello)
                {
                    _logger.LogInformation($"[UDP:{packet.RawType}] from={remoteEp} session={Redact(packet.SessionId)} ts={packet.TimestampText} sig={Redact(packet.Signature)}");
                }
                else
                {
                    _logger.LogDebug($"[UDP:{packet.RawType}] from={remoteEp} session={Redact(packet.SessionId)} ts={packet.TimestampText} sig={Redact(packet.Signature)}");
                }

                // 1. Find Session
                var session = _memory.GetSession(packet.SessionId);
                if (session == null)
                {
                    return;
                }

                // 2. Validate Signature
                // Data = Type + SessionId + Timestamp
                string dataToSign = packet.RawType + packet.SessionId + packet.TimestampText;
                if (!ValidateSignature(dataToSign, packet.SessionId, packet.Signature))
                {
                    return;
                }

                // 3. Validate Timestamp (Optional: prevent replay > 30s)
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - packet.TimestampSeconds) > 30)
                {
                    return;
                }

                // 4. Update Connection Info
                _udpBindings.RegisterEndpoint(packet.SessionId, remoteEp);

                // 5. Send Response (ACK or PONG)
                var responseBytes = packet.Type == UdpControlPacketType.Hello ? AckBytes : PongBytes;

                SendControl(socket, remoteEp, responseBytes);

                if (packet.Type == UdpControlPacketType.Hello)
                {
                    _logger.LogInformation($"UDP Handshake Success: {packet.SessionId} at {remoteEp}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling control packet");
            }
        }

        private static bool ValidateSignature(string data, string key, string providedSignature)
        {
            try
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);

                using var hmac = new HMACSHA256(keyBytes);
                byte[] hashBytes = hmac.ComputeHash(dataBytes);
                string computedSignature = Convert.ToHexString(hashBytes).ToLowerInvariant();

                return computedSignature == providedSignature.ToLowerInvariant();
            }
            catch
            {
                return false;
            }
        }

    }
}

