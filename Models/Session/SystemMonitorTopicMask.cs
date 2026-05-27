namespace GrpcHttp3Demo.Models.Session
{
    [Flags]
    public enum SystemMonitorTopicMask
    {
        None = 0,
        UdpGlobal = 1,
        SignalingRates = 2
    }
}