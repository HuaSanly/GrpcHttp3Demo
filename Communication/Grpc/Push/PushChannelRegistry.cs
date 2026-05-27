using System.Collections.Concurrent;
using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Communication.Grpc.Push
{
    public sealed class PushChannelRegistry
    {
        private readonly ConcurrentDictionary<string, IEventStreamSender> _senders = new();

        public void Attach(string sessionId, IEventStreamSender sender)
        {
            _senders[sessionId] = sender;
        }

        public void Detach(string sessionId)
        {
            _senders.TryRemove(sessionId, out _);
        }

        public void Detach(string sessionId, IEventStreamSender sender)
        {
            if (_senders.TryGetValue(sessionId, out var current) && ReferenceEquals(current, sender))
            {
                _senders.TryRemove(sessionId, out _);
            }
        }

        public bool IsConnected(string sessionId)
        {
            return _senders.ContainsKey(sessionId);
        }

        public Task SendEventAsync(string targetSessionId, EventMessage message)
        {
            if (_senders.TryGetValue(targetSessionId, out var sender))
            {
                _ = SendWithRetryAsync(sender, message, targetSessionId);
            }

            return Task.CompletedTask;
        }

        private async Task SendWithRetryAsync(IEventStreamSender sender, EventMessage message, string targetSessionId)
        {
            int[] delays = { 0, 10, 60 };

            foreach (var delaySeconds in delays)
            {
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }

                try
                {
                    await sender.WriteAsync(message);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Push] Failed to send event to {targetSessionId} (Retry {delaySeconds}s): {ex.Message}");
                }
            }

            Console.WriteLine($"[Push] Give up sending event to {targetSessionId} after retries. Detaching push sender.");
            Detach(targetSessionId, sender);
        }
    }
}