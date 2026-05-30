namespace GrpcHttp3Demo.Models.Session
{
    [Flags]
    public enum SystemMonitorTopicMask
    {
        None = 0,
        UdpGlobal = 1,
        SignalingRates = 2,
        OnlineSummary = 4,
        RuntimeTables = 8,
        Standard = UdpGlobal | SignalingRates | OnlineSummary | RuntimeTables,
        All = Standard
    }
}