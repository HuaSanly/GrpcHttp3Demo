using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Models.HttpRequest.System
{
    /// <summary>
    /// UDP 流注册项创建或更新请求。
    /// </summary>
    public sealed class StreamCatalogUpsertRequest
    {
        /// <summary>
        /// UDP 数据包首字节类型值，例如 0x02、0x09；POST 时必填，PUT 时可省略并使用路由值。
        /// </summary>
        public byte? Prefix { get; set; }

        /// <summary>
        /// 程序内部使用的稳定流代码，例如 pose_controller、body24raw。
        /// </summary>
        public string StreamCode { get; set; } = string.Empty;

        /// <summary>
        /// 管理后台或监控界面展示用名称。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 简短说明；只放公网后端可公开保存的信息，不关联内部 Outline 文档。
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 协议版本号，对应 UDP 包内 Version 字段；没有版本字段的协议可保持 0。
        /// </summary>
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// 固定包长，单位字节；固定长度协议填写该值，变长协议留空。
        /// </summary>
        public int? FixedDatagramBytes { get; set; }

        /// <summary>
        /// 最小合法包长，单位字节；用于接入层快速校验和监控告警。
        /// </summary>
        public int? MinDatagramBytes { get; set; }

        /// <summary>
        /// 最大合法包长，单位字节；用于接入层快速校验和监控告警。
        /// </summary>
        public int? MaxDatagramBytes { get; set; }

        /// <summary>
        /// UDP 逻辑 socket 组编号；相同编号表示这些流走同一个 socket 组。
        /// </summary>
        public int UdpSocketGroupId { get; set; }

        /// <summary>
        /// 是否启用该流；关闭后运行时不应接受、订阅或转发该流。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否允许该流进入 UDP 转发链；可用于只统计不转发或灰度停转。
        /// </summary>
        public bool ForwardingEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用该流的运行时指标统计。
        /// </summary>
        public bool MetricsEnabled { get; set; } = true;

        /// <summary>
        /// 允许发布该流的端角色，例如 VR、Robot、Client。
        /// </summary>
        public RegisterRequest.Types.EndpointType PublisherRole { get; set; } = RegisterRequest.Types.EndpointType.Unknown;

        /// <summary>
        /// 允许订阅该流的端角色位掩码；具体位定义由后端统一解释。
        /// </summary>
        public int SubscriberRoleMask { get; set; }

        /// <summary>
        /// 展示排序值；数值越小越靠前。
        /// </summary>
        public int SortOrder { get; set; }
    }
}
