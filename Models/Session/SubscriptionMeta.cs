namespace GrpcHttp3Demo.Models.Session
{
    // 低频、富信息的订阅元数据，供管理/控制面使用
    public class SubscriptionMeta
    {
        public string SubscriberId { get; set; } = string.Empty;
        public bool SubVideo { get; set; }
        public bool SubPose { get; set; }
        public bool SubAudio { get; set; }
        public bool SubTelemetryLowRate { get; set; }
        public bool SubTelemetryHighRate { get; set; }
        public SystemMonitorTopicMask SystemMonitorTopics { get; set; }
        public int SystemMonitorIntervalMs { get; set; } = 1000;
        public string? LinkMonitorTopologyId { get; set; }
        public byte[]? Sps { get; set; }
        public byte[]? Pps { get; set; }
        public float? TargetBitrateKbps { get; set; }
        public float? SubscriberBandwidthKbps { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
