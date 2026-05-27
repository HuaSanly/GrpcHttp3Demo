using System;
using System.Net;
using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Models.Session
{
    public class DeviceContext
    {
        // --- 自身信息 ---
        public string DeviceId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public RegisterRequest.Types.EndpointType Role { get; set; }
        public int RobotGeneration { get; set; }
        public string VrVersion { get; set; } = string.Empty;
        
        // --- 网络信息 ---
        public IPEndPoint? UdpEndpoint { get; set; }
        public string ClientIp { get; set; } = string.Empty;
        public int ClientPort { get; set; }
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        // --- UDP 活跃性 ---
        // 只由 UDP 控制面（HELLO/PING 且验签通过）更新映射；
        // 数据面只记录活跃时间，不参与映射更新。
        public DateTime LastUdpControlUtc { get; set; } = DateTime.MinValue;
        public DateTime LastUdpDataUtc { get; set; } = DateTime.MinValue;

        // UDP 映射救援状态（通过 push 通道提示客户端重发 UDP HELLO）
        public int UdpRescueCount { get; set; } = 0;
        public DateTime LastUdpRescueUtc { get; set; } = DateTime.MinValue;
        
        // --- 状态机 ---
        public int GrpcRescueCount { get; set; } = 0;

        // --- 配对信息 (1:1) ---
        public string? PairedDeviceId { get; set; }
        
        // --- 缓存信息 ---
        public VideoConfig? LastVideoConfig { get; set; }
        public AudioConfig? LastAudioConfig { get; set; }
    }
}

