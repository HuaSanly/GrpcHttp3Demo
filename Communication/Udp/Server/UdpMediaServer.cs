using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Communication.Udp.Binding;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Communication.Udp.Parsing;
using GrpcHttp3Demo.Communication.Udp.Routing;
using GrpcHttp3Demo.Communication.Udp.Sending;
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
        private readonly SignalingMetricsService _signalingMetrics;
        private readonly SessionPresence _presence;
        private readonly SessionLivenessOptions _livenessOptions;
        private readonly ILogger<UdpMediaServer> _logger;
        private readonly int _port;
        private readonly int _receiveDatagramBufferBytes;

        private readonly UdpForwardingOptions _forwardingOptions;
        private readonly UdpForwardingSendMode _sendMode;
        private UdpPerTargetSendQueue? _sendQueue;
        private UdpSendDispatcher? _sendDispatcher;

        // Optional data-plane keepalive ack (DACK): per remote endpoint, at most once per 5 seconds.
        private static readonly byte[] DackBytes = "DACK"u8.ToArray();
        private static readonly byte[] AckBytes = "ACK"u8.ToArray();
        private static readonly byte[] PongBytes = "PONG"u8.ToArray();
        private const byte SystemMonitorPrefix = 0x07;
        private const long DataActivityUpdateIntervalMs = 1000;
        private const long DackIntervalMs = 5000;

        public UdpMediaServer(SessionMemoryStore memory, UdpSessionBindingService udpBindings, UdpMetricsService metrics, SignalingMetricsService signalingMetrics, SessionPresence presence, SessionLivenessOptions livenessOptions, ILogger<UdpMediaServer> logger, IConfiguration configuration)
        {
            _memory = memory;
            _udpBindings = udpBindings;
            _metrics = metrics;
            _signalingMetrics = signalingMetrics;
            _presence = presence;
            _livenessOptions = livenessOptions;
            _logger = logger;
            _port = configuration.GetValue<int>("MediaServer:UdpPort", 7778);
            _forwardingOptions = configuration.GetSection("MediaServer:UdpForwarding").Get<UdpForwardingOptions>() ?? new UdpForwardingOptions();
            _sendMode = _forwardingOptions.ResolveSendMode();
            _receiveDatagramBufferBytes = Math.Clamp(
                configuration.GetValue<int?>("MediaServer:UdpSocket:ReceiveDatagramBufferBytes") ?? 65_535,
                2_048,
                65_535);

            _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Tune OS socket buffers to reduce drops under high bitrate/bursty send.
            // These are best-effort; OS may clamp to system limits.
            var recvBuf = configuration.GetValue<int?>("MediaServer:UdpSocket:ReceiveBufferBytes");
            if (recvBuf is int rb && rb > 0)
            {
                _udpSocket.ReceiveBufferSize = rb;
            }

            var sendBuf = configuration.GetValue<int?>("MediaServer:UdpSocket:SendBufferBytes");
            if (sendBuf is int sb && sb > 0)
            {
                _udpSocket.SendBufferSize = sb;
            }

            _udpSocket.Bind(new IPEndPoint(IPAddress.Any, _port));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"UDP Media Server started on port {_port}. sendMode={_sendMode}, receiveDatagramBufferBytes={_receiveDatagramBufferBytes}");

            // Control-plane replies keep the small 256-packet micro-buffer. Media uses the selected send mode.
            _sendDispatcher = new UdpSendDispatcher(_udpSocket, _metrics, _logger, _forwardingOptions);
            _sendQueue = _sendMode == UdpForwardingSendMode.PerTargetQueue
                ? new UdpPerTargetSendQueue(_udpSocket, _metrics, _logger, _forwardingOptions)
                : null;

            using var closeRegistration = stoppingToken.Register(static state =>
            {
                try
                {
                    ((Socket)state!).Close();
                }
                catch
                {
                    // Socket close is best-effort during shutdown.
                }
            }, _udpSocket);

            var receiveTask = Task.Factory.StartNew(
                () => ReceiveLoop(stoppingToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            _ = Task.Factory.StartNew(
                () => SystemMonitorPushLoop(stoppingToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            finally
            {
                if (_sendQueue != null)
                {
                    await _sendQueue.StopAsync();
                }

                if (_sendDispatcher != null)
                {
                    await _sendDispatcher.StopAsync();
                }
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
                    object? udpSnapshot = null;
                    object? signalingSnapshot = null;
                    object? onlineSnapshot = null;
                    object? runtimeTablesSnapshot = null;
                    Dictionary<SystemMonitorTopicMask, string[]>? topicNamesCache = null;

                    for (var i = 0; i < targets.Length; i++)
                    {
                        var target = targets[i];
                        if (!target.TryMarkDue(now)) continue;

                        var payload = BuildSystemMonitorPayload(
                            target,
                            ref udpSnapshot,
                            ref signalingSnapshot,
                            ref onlineSnapshot,
                            ref runtimeTablesSnapshot,
                            ref topicNamesCache);
                        SendSystemMonitor(target.Endpoint, payload);
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
            topicNamesCache ??= new Dictionary<SystemMonitorTopicMask, string[]>();
            if (!topicNamesCache.TryGetValue(target.Topics, out var topics))
            {
                topics = SessionSubscription.ToTopicNames(target.Topics);
                topicNamesCache[target.Topics] = topics;
            }

            if ((target.Topics & SystemMonitorTopicMask.UdpGlobal) != 0 && udpSnapshot == null)
            {
                udpSnapshot = _metrics.Snapshot();
            }

            if ((target.Topics & SystemMonitorTopicMask.SignalingRates) != 0 && signalingSnapshot == null)
            {
                signalingSnapshot = _signalingMetrics.Snapshot();
            }

            if ((target.Topics & SystemMonitorTopicMask.OnlineSummary) != 0 && onlineSnapshot == null)
            {
                onlineSnapshot = _presence.GetOnlineRoleSnapshot(_livenessOptions.Timeout);
            }

            if ((target.Topics & SystemMonitorTopicMask.RuntimeTables) != 0 && runtimeTablesSnapshot == null)
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
                udp = (target.Topics & SystemMonitorTopicMask.UdpGlobal) != 0 ? udpSnapshot : null,
                signaling = (target.Topics & SystemMonitorTopicMask.SignalingRates) != 0 ? signalingSnapshot : null,
                online = (target.Topics & SystemMonitorTopicMask.OnlineSummary) != 0 ? onlineSnapshot : null,
                runtimeTables = (target.Topics & SystemMonitorTopicMask.RuntimeTables) != 0 ? runtimeTablesSnapshot : null
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            var payload = new byte[jsonBytes.Length + 1];
            payload[0] = SystemMonitorPrefix;
            Buffer.BlockCopy(jsonBytes, 0, payload, 1, jsonBytes.Length);
            return payload;
        }

        private void SendSystemMonitor(IPEndPoint endpoint, byte[] payload)
        {
            try
            {
                _metrics.RecordTxAttempt(payload.Length, SystemMonitorPrefix);
                _udpSocket.SendTo(payload, SocketFlags.None, endpoint);
                _metrics.RecordTxSuccess(payload.Length, SystemMonitorPrefix);
            }
            catch (SocketException ex)
            {
                _metrics.RecordTxFailure(payload.Length, SystemMonitorPrefix, ex.SocketErrorCode);
            }
            catch (Exception)
            {
                _metrics.RecordTxFailure(payload.Length, SystemMonitorPrefix, SocketError.SocketError);
            }
        }

        private void ReceiveLoop(CancellationToken stoppingToken)
        {
            var buffer = new byte[_receiveDatagramBufferBytes];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    remote = new IPEndPoint(IPAddress.Any, 0);
                    var length = _udpSocket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remote);
                    var remoteEp = (IPEndPoint)remote;

                    if (length == 0) continue;

                    byte prefix = buffer[0];
                    _metrics.RecordRxPacket(length, prefix);

                    switch (prefix)
                    {
                        case (byte)'H':
                        case (byte)'P':
                            HandleControlPacket(buffer.AsSpan(0, length), remoteEp);
                            continue;

                        case 0x03:
                            if (_memory.TryGetFeedbackForward(remoteEp, out var robotEp, out var counter) && robotEp != null)
                            {
                                counter?.RecordFeedback(length);
                                byte[]? queuedPayload = null;
                                SendMedia(robotEp, buffer, length, prefix, ref queuedPayload);
                            }
                            continue;
                    }

                    if (!_memory.TryGetSourceRoute(remoteEp, out var route) || route == null)
                    {
                        continue;
                    }

                    // 4. Video from Robot (0x01): forward using prebuilt ep -> targets
                    if (prefix == 0x01)
                    {
                        RecordDataActivityAndMaybeDack(route, remoteEp);

                        var targets = route.VideoTargets;
                        byte[]? queuedPayload = null;
                        for (var i = 0; i < targets.Length; i++)
                        {
                            var dest = targets[i];
                            dest.Counter.RecordVideo(length);
                            SendMedia(dest.Endpoint, buffer, length, prefix, ref queuedPayload);
                        }

                        continue;
                    }

                    // 5. Pose from VR (0x02): forward using prebuilt ep -> pose targets
                    if (prefix == 0x02)
                    {
                        RecordDataActivityAndMaybeDack(route, remoteEp);

                        var targets = route.PoseTargets;
                        byte[]? queuedPayload = null;
                        for (var i = 0; i < targets.Length; i++)
                        {
                            var dest = targets[i];
                            dest.Counter.RecordPose(length);
                            SendMedia(dest.Endpoint, buffer, length, prefix, ref queuedPayload);
                        }

                        continue;
                    }

                    // 6. Audio from publisher (0x04): forward using prebuilt ep -> audio targets
                    if (prefix == 0x04)
                    {
                        RecordDataActivityAndMaybeDack(route, remoteEp);

                        var targets = route.AudioTargets;
                        byte[]? queuedPayload = null;
                        for (var i = 0; i < targets.Length; i++)
                        {
                            var dest = targets[i];
                            dest.Counter.RecordAudio(length);
                            SendMedia(dest.Endpoint, buffer, length, prefix, ref queuedPayload);
                        }

                        continue;
                    }

                    // 7. Robot telemetry low rate (0x05): forward using prebuilt ep -> telemetry low-rate targets
                    if (prefix == 0x05)
                    {
                        RecordDataActivityAndMaybeDack(route, remoteEp);

                        var targets = route.TelemetryLowRateTargets;
                        byte[]? queuedPayload = null;
                        for (var i = 0; i < targets.Length; i++)
                        {
                            var dest = targets[i];
                            dest.Counter.RecordTelemetry(length);
                            SendMedia(dest.Endpoint, buffer, length, prefix, ref queuedPayload);
                        }

                        continue;
                    }

                    // 8. Robot telemetry high rate (0x06): forward using prebuilt ep -> telemetry high-rate targets
                    if (prefix == 0x06)
                    {
                        RecordDataActivityAndMaybeDack(route, remoteEp);

                        var targets = route.TelemetryHighRateTargets;
                        byte[]? queuedPayload = null;
                        for (var i = 0; i < targets.Length; i++)
                        {
                            var dest = targets[i];
                            dest.Counter.RecordTelemetry(length);
                            SendMedia(dest.Endpoint, buffer, length, prefix, ref queuedPayload);
                        }

                        continue;
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
                    _logger.LogError(ex, "UDP Receive Error");
                }
            }
        }

        private void SendMedia(IPEndPoint endpoint, byte[] buffer, int length, byte prefix, ref byte[]? queuedPayload)
        {
            if (_sendMode == UdpForwardingSendMode.Direct)
            {
                SendDirect(endpoint, buffer, length, prefix);
                return;
            }

            queuedPayload ??= CopyDatagram(buffer, length);

            if (_sendMode == UdpForwardingSendMode.PerTargetQueue && _sendQueue != null)
            {
                _sendQueue.Enqueue(endpoint, queuedPayload, prefix);
                return;
            }

            _sendDispatcher?.Enqueue(endpoint, queuedPayload, prefix);
        }

        private void SendDirect(IPEndPoint endpoint, byte[] buffer, int length, byte prefix)
        {
            try
            {
                _metrics.RecordTxAttempt(length, prefix);
                _udpSocket.SendTo(buffer, 0, length, SocketFlags.None, endpoint);
                _metrics.RecordTxSuccess(length, prefix);
            }
            catch (SocketException ex)
            {
                _metrics.RecordTxFailure(length, prefix, ex.SocketErrorCode);
            }
            catch (Exception)
            {
                _metrics.RecordTxFailure(length, prefix, SocketError.SocketError);
            }
        }

        private static byte[] CopyDatagram(byte[] buffer, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            return copy;
        }

        private void RecordDataActivityAndMaybeDack(UdpSourceRoute route, IPEndPoint remoteEp)
        {
            var now = Environment.TickCount64;
            if (route.TryMarkDataActivityDue(now, DataActivityUpdateIntervalMs))
            {
                _udpBindings.UpdateDataActivity(route.SourceSessionId);
            }

            if (route.TryMarkDackDue(now, DackIntervalMs))
            {
                _sendDispatcher?.Enqueue(remoteEp, DackBytes, DackBytes[0]);
            }
        }

        private void HandleControlPacket(ReadOnlySpan<byte> buffer, IPEndPoint remoteEp)
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

                _sendDispatcher?.Enqueue(remoteEp, responseBytes, responseBytes[0]);

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

