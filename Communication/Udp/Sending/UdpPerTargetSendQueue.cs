using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Communication.Udp.Routing;

namespace GrpcHttp3Demo.Communication.Udp.Sending
{
    internal sealed class UdpPerTargetSendQueue
    {
        private readonly Socket _socket;
        private readonly UdpMetricsService _metrics;
        private readonly ILogger _logger;
        private readonly UdpForwardingOptions _options;

        private readonly ConcurrentDictionary<IPEndPoint, TargetQueue> _queues = new();
        private long _lastQueueFullWarnTickMs;

        public UdpPerTargetSendQueue(Socket socket, UdpMetricsService metrics, ILogger logger, UdpForwardingOptions options)
        {
            _socket = socket;
            _metrics = metrics;
            _logger = logger;
            _options = options;
        }

        public void Enqueue(IPEndPoint destination, byte[] buffer, byte prefix, UdpRuntimeLink link)
        {
            var q = _queues.GetOrAdd(destination, ep => TargetQueue.Create(ep, _socket, _metrics, _logger, _options));
            if (!q.TryEnqueue(buffer, prefix, link))
            {
                _metrics.RecordForwardQueueDrop(buffer.Length, prefix);
                link.RecordQueueDropped(buffer.Length);

                // Rate-limit warning to avoid log storms.
                var now = Environment.TickCount64;
                var last = Interlocked.Read(ref _lastQueueFullWarnTickMs);
                if (now - last > 2000 && Interlocked.CompareExchange(ref _lastQueueFullWarnTickMs, now, last) == last)
                {
                    _logger.LogWarning("UDP forward queue full; dropping packets. Consider increasing MediaServer:UdpForwarding:QueueCapacityPerTarget or enabling pacing.");
                }

                return;
            }

            link.RecordQueueEnqueued(buffer.Length);
        }

        public async Task StopAsync()
        {
            foreach (var q in _queues.Values)
            {
                q.Complete();
            }

            foreach (var q in _queues.Values)
            {
                await q.WaitAsync();
            }
        }

        private sealed class TargetQueue
        {
            private readonly IPEndPoint _destination;
            private readonly Socket _socket;
            private readonly UdpMetricsService _metrics;
            private readonly ILogger _logger;
            private readonly UdpForwardingOptions _options;
            private readonly Channel<SendItem> _channel;
            private readonly Task _worker;

            private long _nextSendAllowedTicks;
            private long _lastSendFailWarnTickMs;

            private TargetQueue(IPEndPoint destination, Socket socket, UdpMetricsService metrics, ILogger logger, UdpForwardingOptions options, Channel<SendItem> channel)
            {
                _destination = destination;
                _socket = socket;
                _metrics = metrics;
                _logger = logger;
                _options = options;
                _channel = channel;
                _worker = Task.Run(WorkerLoopAsync);
            }

            public static TargetQueue Create(IPEndPoint destination, Socket socket, UdpMetricsService metrics, ILogger logger, UdpForwardingOptions options)
            {
                var capacity = options.QueueCapacityPerTarget <= 0 ? 2048 : options.QueueCapacityPerTarget;
                var channel = Channel.CreateBounded<SendItem>(new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });

                return new TargetQueue(destination, socket, metrics, logger, options, channel);
            }

            public bool TryEnqueue(byte[] buffer, byte prefix, UdpRuntimeLink link)
            {
                // Safe to share the received datagram buffer across multiple targets as long as we never mutate it.
                // This avoids per-target allocations under high bitrate.
                return _channel.Writer.TryWrite(new SendItem(buffer, prefix, link));
            }

            public void Complete() => _channel.Writer.TryComplete();

            public Task WaitAsync() => _worker;

            private async Task WorkerLoopAsync()
            {
                try
                {
                    await foreach (var item in _channel.Reader.ReadAllAsync())
                    {
                        await ApplyPacingAsync(item.Payload.Length);
                        await SendWithRetryAsync(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UDP forward worker crashed for {Destination}", _destination);
                }
            }

            private async ValueTask ApplyPacingAsync(int bytes)
            {
                var maxPps = _options.MaxPpsPerTarget;
                var maxBps = _options.MaxBpsPerTarget;
                if (maxPps <= 0 && maxBps <= 0)
                {
                    return;
                }

                var now = Stopwatch.GetTimestamp();
                var next = Interlocked.Read(ref _nextSendAllowedTicks);
                if (next > now)
                {
                    var delaySeconds = (next - now) / (double)Stopwatch.Frequency;
                    if (delaySeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    }
                }

                var afterSendTicks = Stopwatch.GetTimestamp();
                long addTicks = 0;
                if (maxPps > 0)
                {
                    addTicks = Math.Max(addTicks, (long)(Stopwatch.Frequency / (double)maxPps));
                }
                if (maxBps > 0)
                {
                    addTicks = Math.Max(addTicks, (long)(bytes * (Stopwatch.Frequency / (double)maxBps)));
                }

                Interlocked.Exchange(ref _nextSendAllowedTicks, afterSendTicks + addTicks);
            }

            private async Task SendWithRetryAsync(SendItem item)
            {
                var maxRetries = _options.MaxRetries < 0 ? 0 : _options.MaxRetries;
                var retryDelayMs = _options.RetryDelayMs <= 0 ? 1 : _options.RetryDelayMs;

                for (var attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _metrics.RecordTxAttempt(item.Payload.Length, item.Prefix);
                        item.Link.RecordSendAttempt(item.Payload.Length);
                        await _socket.SendToAsync(item.Payload, SocketFlags.None, _destination);
                        _metrics.RecordTxSuccess(item.Payload.Length, item.Prefix);
                        item.Link.RecordSendSuccess(item.Payload.Length);
                        return;
                    }
                    catch (SocketException ex)
                    {
                        var code = ex.SocketErrorCode;
                        _metrics.RecordTxFailure(item.Payload.Length, item.Prefix, code);
                        item.Link.RecordSendFailure(item.Payload.Length, code);

                        var retryable = _options.RetryOnNoBuffer && code == SocketError.NoBufferSpaceAvailable;
                        if (retryable && attempt < maxRetries)
                        {
                            _metrics.RecordTxRetry(item.Payload.Length, item.Prefix, code);
                            item.Link.RecordRetry(item.Payload.Length);
                            await Task.Delay(retryDelayMs);
                            continue;
                        }

                        // Rate-limit warning to avoid log storms.
                        var now = Environment.TickCount64;
                        var last = Interlocked.Read(ref _lastSendFailWarnTickMs);
                        if (now - last > 2000 && Interlocked.CompareExchange(ref _lastSendFailWarnTickMs, now, last) == last)
                        {
                            _logger.LogWarning("UDP send failed to {Destination}, error={Error}. If bursts occur, consider enabling pacing or increasing OS socket buffers.", _destination, code);
                        }

                        return;
                    }
                    catch (Exception ex)
                    {
                        item.Link.RecordSendFailure(item.Payload.Length, SocketError.SocketError);
                        _logger.LogError(ex, "UDP send failed to {Destination}", _destination);
                        return;
                    }
                }
            }

            private readonly record struct SendItem(byte[] Payload, byte Prefix, UdpRuntimeLink Link);
        }
    }
}

