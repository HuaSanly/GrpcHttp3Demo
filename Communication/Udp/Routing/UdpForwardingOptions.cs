using System;

namespace GrpcHttp3Demo.Communication.Udp.Routing
{
    internal enum UdpForwardingSendMode
    {
        Direct,
        GlobalQueue,
        PerTargetQueue
    }

    internal sealed class UdpForwardingOptions
    {
        public bool Enabled { get; init; } = false;
        public string? SendMode { get; init; }
        public int QueueCapacityPerTarget { get; init; } = 2048;
        public int MaxPpsPerTarget { get; init; } = 0;
        public int MaxBpsPerTarget { get; init; } = 0;
        public bool RetryOnNoBuffer { get; init; } = true;
        public int MaxRetries { get; init; } = 1;
        public int RetryDelayMs { get; init; } = 1;

        public UdpForwardingSendMode ResolveSendMode()
        {
            if (!string.IsNullOrWhiteSpace(SendMode))
            {
                return SendMode.Trim().ToLowerInvariant() switch
                {
                    "direct" => UdpForwardingSendMode.Direct,
                    "globalqueue" => UdpForwardingSendMode.GlobalQueue,
                    "global_queue" => UdpForwardingSendMode.GlobalQueue,
                    "global-queue" => UdpForwardingSendMode.GlobalQueue,
                    "pertargetqueue" => UdpForwardingSendMode.PerTargetQueue,
                    "per_target_queue" => UdpForwardingSendMode.PerTargetQueue,
                    "per-target-queue" => UdpForwardingSendMode.PerTargetQueue,
                    _ => Enabled ? UdpForwardingSendMode.PerTargetQueue : UdpForwardingSendMode.Direct
                };
            }

            return Enabled ? UdpForwardingSendMode.PerTargetQueue : UdpForwardingSendMode.Direct;
        }
    }
}

