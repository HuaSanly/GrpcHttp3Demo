using System.Net.WebSockets;
using Google.Protobuf;
using GrpcHttp3Demo.Communication.Grpc.Monitoring;
using GrpcHttp3Demo.Communication.Grpc.Push;
using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Communication.WebSockets
{
    /// <summary>
    /// 基于 WebSocket 的 Protobuf 二进制推送发送器。
    /// 用于 /ws/proto：服务端可在同一条 WS 上回包/推事件。
    /// </summary>
    public class WebSocketProtobufEventStreamSender : IEventStreamSender
    {
        private readonly WebSocket _webSocket;
        private readonly SemaphoreSlim _sendLock;
        private readonly SignalingMetricsService? _metrics;

        public WebSocketProtobufEventStreamSender(WebSocket webSocket, SemaphoreSlim? sendLock = null, SignalingMetricsService? metrics = null)
        {
            _webSocket = webSocket;
            _sendLock = sendLock ?? new SemaphoreSlim(1, 1);
            _metrics = metrics;
        }

        public async Task WriteAsync(EventMessage message)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "WebSocket is not open.");
            }

            var envelope = new WsEnvelope
            {
                Event = message
            };

            var bytes = envelope.ToByteArray();

            await _sendLock.WaitAsync();
            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);

                _metrics?.RecordOutboundEvent(message);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}

