using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Communication.Udp.Routing;

namespace GrpcHttp3Demo.Communication.Udp.Sending
{
    internal sealed class UdpSendDispatcher
    {
        private const int DefaultCapacity = 256;

        private readonly Socket _socket;
        private readonly UdpMetricsService _metrics;
        private readonly ILogger _logger;
        private readonly BlockingCollection<OutboundDatagram> _queue;
        private readonly Thread _worker;
        private readonly string _queueFullLogMessage;

        private long _lastQueueFullWarnTickMs;

        public UdpSendDispatcher(Socket socket, UdpMetricsService metrics, ILogger logger, UdpForwardingOptions options, int? capacity = null, string workerName = "UdpSendWorker", string queueFullLogMessage = "UDP send queue full (capacity reached); dropping packet.")
        {
            _socket = socket;
            _metrics = metrics;
            _logger = logger;
            _queueFullLogMessage = queueFullLogMessage;

            // Micro-buffer to decouple RX from TX.
            // Using BlockingCollection with dedicated thread for pure synchronous operation.
            // No async/await overhead, no thread pool scheduling delays.
            _queue = new BlockingCollection<OutboundDatagram>(capacity.GetValueOrDefault(DefaultCapacity));

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = workerName
            };
            _worker.Start();
        }

        public void Enqueue(IPEndPoint destination, byte[] payload, byte prefix, UdpRuntimeLink? link = null)
        {
            // Attempt to hand off to worker.
            // If worker is busy/full, drop immediately. No waiting.
            if (!_queue.TryAdd(new OutboundDatagram(destination, payload, prefix, link)))
            {
                _metrics.RecordForwardQueueDrop(payload.Length, prefix);
                link?.RecordQueueDropped(payload.Length);

                var now = Environment.TickCount64;
                var last = Interlocked.Read(ref _lastQueueFullWarnTickMs);
                if (now - last > 5000 && Interlocked.CompareExchange(ref _lastQueueFullWarnTickMs, now, last) == last)
                {
                    _logger.LogWarning(_queueFullLogMessage);
                }

                return;
            }

            link?.RecordQueueEnqueued(payload.Length);
        }

        public void Complete() => _queue.CompleteAdding();

        public Task WaitAsync() => Task.Run(() => _worker.Join());

        public Task StopAsync()
        {
            Complete();
            return WaitAsync();
        }

        private void WorkerLoop()
        {
            try
            {
                // GetConsumingEnumerable is pure synchronous blocking.
                // No Task, no await, no thread pool scheduling.
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    SendNow(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP send dispatcher crashed");
            }
        }

        private void SendNow(OutboundDatagram item)
        {
            try
            {
                _metrics.RecordTxAttempt(item.Payload.Length, item.Prefix);
                item.Link?.RecordSendAttempt(item.Payload.Length);
                _socket.SendTo(item.Payload, SocketFlags.None, item.Destination);
                _metrics.RecordTxSuccess(item.Payload.Length, item.Prefix);
                item.Link?.RecordSendSuccess(item.Payload.Length);
            }
            catch (SocketException ex)
            {
                _metrics.RecordTxFailure(item.Payload.Length, item.Prefix, ex.SocketErrorCode);
                item.Link?.RecordSendFailure(item.Payload.Length, ex.SocketErrorCode);
            }
            catch (Exception)
            {
                _metrics.RecordTxFailure(item.Payload.Length, item.Prefix, SocketError.SocketError);
                item.Link?.RecordSendFailure(item.Payload.Length, SocketError.SocketError);
            }
        }

        private readonly record struct OutboundDatagram(IPEndPoint Destination, byte[] Payload, byte Prefix, UdpRuntimeLink? Link);
    }
}

