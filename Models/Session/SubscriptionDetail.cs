namespace GrpcHttp3Demo.Models.Session
{
    public class SubscriptionDetail
    {
        public string SubscriberId { get; set; } = string.Empty;
        public bool SubVideo { get; set; }
        public bool SubPose { get; set; }
        public bool SubAudio { get; set; }
        public bool SubTelemetryLowRate { get; set; }
        public bool SubTelemetryHighRate { get; set; }
        public SystemMonitorTopicMask SystemMonitorTopics { get; set; }
        public int SystemMonitorIntervalMs { get; set; } = 1000;
        public string? SystemMonitorUdpLinkId { get; set; }
    }
}
