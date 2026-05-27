using Microsoft.Extensions.Logging;

namespace GrpcHttp3Demo.Communication.Grpc.ConfigSync
{
    /// <summary>
    /// 负责 AudioConfig 的可靠投递（ACK + 重试）
    /// </summary>
    public class AudioConfigDeliveryService : QueuedConfigDeliveryServiceBase
    {
        public AudioConfigDeliveryService(ILogger<AudioConfigDeliveryService> logger)
            : base(logger, "AudioConfig")
        {
        }
    }
}
