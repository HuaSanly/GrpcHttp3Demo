namespace GrpcHttp3Demo.Models.Udp
{
    // SCReAM 热状态：最小化，只存建议码率与时间戳（session级）
    public readonly record struct ScreamHotState(float BitrateKbps, long UpdatedAtMs);
}
