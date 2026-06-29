namespace GrpcHttp3Demo.Models.HttpResponse.System
{
    /// <summary>
    /// UDP 流注册项响应模型。
    /// </summary>
    public sealed class StreamCatalogResponse
    {
        /// <summary>
        /// UDP 数据包首字节类型值，例如 2、9。
        /// </summary>
        public byte Prefix { get; set; }

        /// <summary>
        /// UDP 数据包首字节类型值的十六进制展示，例如 0x02、0x09。
        /// </summary>
        public string PrefixHex { get; set; } = string.Empty;

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
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 协议版本号，对应 UDP 包内 Version 字段；没有版本字段的协议可保持 0。
        /// </summary>
        public int ProtocolVersion { get; set; }

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
        public bool Enabled { get; set; }

        /// <summary>
        /// 是否允许该流进入 UDP 转发链；可用于只统计不转发或灰度停转。
        /// </summary>
        public bool ForwardingEnabled { get; set; }

        /// <summary>
        /// 是否启用该流的运行时指标统计。
        /// </summary>
        public bool MetricsEnabled { get; set; }

        /// <summary>
        /// 允许发布该流的端角色名称，例如 Vr、Robot、Client。
        /// </summary>
        public string PublisherRole { get; set; } = string.Empty;

        /// <summary>
        /// 允许发布该流的端角色枚举值。
        /// </summary>
        public int PublisherRoleValue { get; set; }

        /// <summary>
        /// 允许订阅该流的端角色位掩码；具体位定义由后端统一解释。
        /// </summary>
        public int SubscriberRoleMask { get; set; }

        /// <summary>
        /// 展示排序值；数值越小越靠前。
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 记录创建时间，UTC。
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// 记录最后更新时间，UTC。
        /// </summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
