using Microsoft.Extensions.Logging;

namespace GrpcHttp3Demo.Communication.Grpc.ConfigSync
{
    /// <summary>
    /// 负责 VideoConfig 的可靠投递（ACK + 重试）
    /// </summary>
    public class VideoConfigDeliveryService : QueuedConfigDeliveryServiceBase
    {
        public VideoConfigDeliveryService(ILogger<VideoConfigDeliveryService> logger)
            : base(logger, "VideoConfig")
        {
        }
    }
}

