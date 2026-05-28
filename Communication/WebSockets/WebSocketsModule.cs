using System.Net.WebSockets;
using System.IO;
using Google.Protobuf;
using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Communication.Grpc.Signaling;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Sessions;

namespace GrpcHttp3Demo.Communication.WebSockets
{
    /// <summary>
    /// WebSocket 模块：配置 WS 中间件与映射 WS 路由
    /// </summary>
    public static class WebSocketsModule
    {
        public static WebApplication UseWebSocketsModule(this WebApplication app)
        {
            var webSocketOptions = new WebSocketOptions
            {
                // 协议层 Ping/Pong（由服务器发起），用于尽快发现半开连接
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            app.UseWebSockets(webSocketOptions);
            return app;
        }

        public static WebApplication MapWebSocketsModule(this WebApplication app)
        {
            // VR/Unity 适配通道：client <-> server 双向 WS + Protobuf（二进制）
            // 说明：VR 端不支持 gRPC/HTTP2，则走 /ws/proto 完成控制信令 + 心跳。
            app.Map("/ws/proto", async (HttpContext context, SessionRegistry sessions, SessionPresence presence, PushChannelRegistry pushChannels, SignalingAppService appService, SignalingMetricsService metrics, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("WebSocketProto");

                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                // 支持重连：如果客户端携带 sessionId，则直接绑定该会话的推送通道
                var currentSessionId = context.Request.Query["sessionId"].ToString();
                if (!string.IsNullOrEmpty(currentSessionId))
                {
                    var existing = sessions.GetSession(currentSessionId);
                    if (existing == null)
                    {
                        // sessionId 不存在，视为无效
                        currentSessionId = null;
                    }
                }

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                // 关键：同一条 WS 既要回包也要推事件，所有 SendAsync 必须串行
                var sendLock = new SemaphoreSlim(1, 1);
                var eventSender = new WebSocketProtobufEventStreamSender(webSocket, sendLock, metrics: metrics);

                if (!string.IsNullOrEmpty(currentSessionId))
                {
                    pushChannels.Attach(currentSessionId, eventSender);
                    presence.MarkTransportConnected(currentSessionId);
                    logger.LogInformation($"[WS/Proto] Re-attached sender: {currentSessionId}");
                }
                else
                {
                    logger.LogInformation("[WS/Proto] Connected (no session attached yet)");
                }

                async Task SendEnvelopeAsync(WsEnvelope envelope)
                {
                    var bytes = envelope.ToByteArray();
                    await sendLock.WaitAsync();
                    try
                    {
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Binary,
                            true,
                            CancellationToken.None);

                        // 统计：WS 响应/回包
                        switch (envelope.PayloadCase)
                        {
                            case WsEnvelope.PayloadOneofCase.RegisterResponse:
                                metrics.RecordOutboundRegisterResponse();
                                break;
                            case WsEnvelope.PayloadOneofCase.PingAck:
                                metrics.RecordOutboundPingAck();
                                break;
                            case WsEnvelope.PayloadOneofCase.PairResponse:
                                metrics.RecordOutboundPairResponse();
                                break;
                            case WsEnvelope.PayloadOneofCase.SubscribeResponse:
                                metrics.RecordOutboundSubscribeResponse();
                                break;
                            case WsEnvelope.PayloadOneofCase.ListUnpairedResponse:
                                metrics.RecordOutboundListUnpairedResponse();
                                break;
                            case WsEnvelope.PayloadOneofCase.Error:
                                metrics.RecordOutboundWsError();
                                break;
                        }
                    }
                    finally
                    {
                        sendLock.Release();
                    }
                }

                async Task<byte[]?> ReceiveBinaryMessageAsync()
                {
                    var buffer = new byte[8 * 1024];
                    using var ms = new MemoryStream();

                    while (true)
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return null;
                        }

                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            // 忽略非二进制消息
                            if (result.EndOfMessage)
                            {
                                return Array.Empty<byte>();
                            }
                            continue;
                        }

                        if (result.Count > 0)
                        {
                            ms.Write(buffer, 0, result.Count);
                        }

                        if (result.EndOfMessage)
                        {
                            return ms.ToArray();
                        }
                    }
                }

                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {
                        var payload = await ReceiveBinaryMessageAsync();
                        if (payload == null)
                        {
                            // client closed
                            break;
                        }
                        if (payload.Length == 0)
                        {
                            continue;
                        }

                        WsEnvelope env;
                        try
                        {
                            env = WsEnvelope.Parser.ParseFrom(payload);
                        }
                        catch (Exception ex)
                        {
                            metrics.RecordInboundWsBadProto();
                            await SendEnvelopeAsync(new WsEnvelope
                            {
                                Error = new WsError { Code = "BAD_PROTO", Message = $"Invalid protobuf: {ex.Message}" }
                            });
                            continue;
                        }

                        // 统计：WS 收包
                        switch (env.PayloadCase)
                        {
                            case WsEnvelope.PayloadOneofCase.Register:
                                metrics.RecordInboundRegister();
                                break;
                            case WsEnvelope.PayloadOneofCase.Ping:
                                metrics.RecordInboundPing();
                                break;
                            case WsEnvelope.PayloadOneofCase.Pair:
                                metrics.RecordInboundPair();
                                break;
                            case WsEnvelope.PayloadOneofCase.Subscribe:
                                metrics.RecordInboundSubscribe();
                                break;
                            case WsEnvelope.PayloadOneofCase.ListUnpaired:
                                metrics.RecordInboundListUnpaired();
                                break;
                            // Add detailed logging for debug
                            default:
                                break;
                        }

                        switch (env.PayloadCase)
                        {
                            case WsEnvelope.PayloadOneofCase.Register:
                            {
                                var req = env.Register;

                                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                var port = context.Connection.RemotePort;

                                var resp = appService.Register(req, ip, port);
                                currentSessionId = resp.SessionId;
                                pushChannels.Attach(currentSessionId, eventSender);
                                presence.MarkTransportConnected(currentSessionId);

                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    RegisterResponse = resp
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.Ping:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                var ack = appService.Ping(currentSessionId, env.Ping);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    PingAck = ack
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.Pair:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                if (string.IsNullOrEmpty(env.Pair.PeerSessionId))
                                {
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        PairResponse = new PairResponse { Success = false, Message = "Missing peer_session_id" }
                                    });
                                    break;
                                }

                                var resp = await appService.PairAsync(currentSessionId, env.Pair);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    PairResponse = resp
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.Subscribe:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                if (string.IsNullOrEmpty(env.Subscribe.PublisherSessionId))
                                {
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        SubscribeResponse = new SubscribeResponse { Success = false, Message = "Missing publisher_session_id" }
                                    });
                                    break;
                                }

                                var resp = await appService.SubscribeAsync(currentSessionId, env.Subscribe);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    SubscribeResponse = resp
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.ListUnpaired:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                var resp = appService.ListUnpaired(currentSessionId, env.ListUnpaired);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    ListUnpairedResponse = resp
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.VideoConfig:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                var ack = await appService.PublishVideoConfigAsync(currentSessionId, env.VideoConfig);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    PublishVideoConfigAck = ack
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.AckVideoConfig:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                appService.AckVideoConfig(currentSessionId, env.AckVideoConfig);
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.AudioConfig:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                var ack = await appService.PublishAudioConfigAsync(currentSessionId, env.AudioConfig);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    PublishAudioConfigAck = ack
                                });
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.AckAudioConfig:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                appService.AckAudioConfig(currentSessionId, env.AckAudioConfig);
                                break;
                            }

                            case WsEnvelope.PayloadOneofCase.GetP2PInfo:
                            {
                                if (string.IsNullOrEmpty(currentSessionId))
                                {
                                    metrics.RecordInboundWsNoSession();
                                    await SendEnvelopeAsync(new WsEnvelope
                                    {
                                        RequestId = env.RequestId,
                                        Error = new WsError { Code = "NO_SESSION", Message = "Please Register first (or provide sessionId query)." }
                                    });
                                    break;
                                }

                                var resp = appService.GetP2pInfo(currentSessionId);
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    GetP2PInfoResponse = resp
                                });
                                break;
                            }

                            default:
                                metrics.RecordInboundWsUnsupported();
                                await SendEnvelopeAsync(new WsEnvelope
                                {
                                    RequestId = env.RequestId,
                                    Error = new WsError { Code = "UNSUPPORTED", Message = $"Unsupported payload: {env.PayloadCase}" }
                                });
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"[WS/Proto] Connection aborted: {ex.Message}");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(currentSessionId))
                    {
                        presence.MarkTransportDisconnected(currentSessionId, "websocket_disconnected");
                        pushChannels.Detach(currentSessionId, eventSender);
                        logger.LogInformation($"[WS/Proto] Disconnected: {currentSessionId}. EventSender detached.");
                    }
                    else
                    {
                        logger.LogInformation("[WS/Proto] Disconnected (no session attached)");
                    }
                }
            });

            return app;
        }
    }
}

