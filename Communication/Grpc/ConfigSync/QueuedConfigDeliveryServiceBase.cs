using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GrpcHttp3Demo.Communication.Grpc.ConfigSync
{
    public abstract class QueuedConfigDeliveryServiceBase
    {
        private readonly Channel<DeliveryRequest> _channel;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingAcks = new();
        private readonly ILogger _logger;
        private readonly string _logPrefix;

        protected QueuedConfigDeliveryServiceBase(ILogger logger, string logPrefix)
        {
            _logger = logger;
            _logPrefix = logPrefix;
            _channel = Channel.CreateUnbounded<DeliveryRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _ = Task.Run(ProcessQueueAsync);
        }

        public void CompleteAck(string configId, bool success)
        {
            if (_pendingAcks.TryGetValue(configId, out var tcs))
            {
                tcs.TrySetResult(success);
            }
        }

        public async Task SendReliablyAsync(
            string configId,
            string targetSessionId,
            Func<Task> sendAction,
            Func<Task> disconnectAction)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _channel.Writer.WriteAsync(new DeliveryRequest(configId, targetSessionId, sendAction, disconnectAction, completion));
            await completion.Task;
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var request in _channel.Reader.ReadAllAsync())
            {
                await ProcessRequestAsync(request);
            }
        }

        private async Task ProcessRequestAsync(DeliveryRequest request)
        {
            const int maxRetries = 5;
            var timeout = TimeSpan.FromSeconds(3);

            try
            {
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    var ackCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingAcks[request.ConfigId] = ackCompletion;

                    try
                    {
                        _logger.LogInformation($"[{_logPrefix}] Sending to {request.TargetSessionId}, Attempt {attempt + 1}/{maxRetries + 1}, ConfigId={request.ConfigId}");

                        await request.SendAction();

                        var completedTask = await Task.WhenAny(ackCompletion.Task, Task.Delay(timeout));
                        if (completedTask == ackCompletion.Task)
                        {
                            _pendingAcks.TryRemove(request.ConfigId, out _);

                            if (ackCompletion.Task.Result)
                            {
                                _logger.LogInformation($"[{_logPrefix}] ACK received from {request.TargetSessionId} for {request.ConfigId}");
                                request.Completion.TrySetResult();
                                return;
                            }

                            _logger.LogWarning($"[{_logPrefix}] NACK received from {request.TargetSessionId} for {request.ConfigId}. Treating as retry-able failure.");
                        }
                        else
                        {
                            _logger.LogWarning($"[{_logPrefix}] Timeout waiting for ACK from {request.TargetSessionId} (Attempt {attempt + 1})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[{_logPrefix}] Exception delivering to {request.TargetSessionId}");
                    }
                }

                _logger.LogError($"[{_logPrefix}] Failed to deliver to {request.TargetSessionId} after {maxRetries + 1} attempts. Disconnecting session.");
                _pendingAcks.TryRemove(request.ConfigId, out _);

                try
                {
                    await request.DisconnectAction();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing disconnect action");
                }

                request.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                request.Completion.TrySetException(ex);
            }
        }

        private readonly record struct DeliveryRequest(
            string ConfigId,
            string TargetSessionId,
            Func<Task> SendAction,
            Func<Task> DisconnectAction,
            TaskCompletionSource Completion);
    }
}
