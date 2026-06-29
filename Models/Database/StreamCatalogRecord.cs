using GrpcHttp3Demo.Protos;
using SqlSugar;

namespace GrpcHttp3Demo.Models.Database
{
    [SugarTable("stream_catalog")]
    public sealed class StreamCatalogRecord
    {
        /// <summary>
        /// UDP 数据包首字节类型值，例如 0x02、0x09；也是本表主键。
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, ColumnDescription = "UDP 数据包首字节类型值，例如 0x02、0x09；也是本表主键")]
        public byte Prefix { get; set; }

        /// <summary>
        /// 程序内部使用的稳定流代码，例如 pose_controller、body24raw；不要使用展示名做逻辑判断。
        /// </summary>
        [SugarColumn(Length = 64, IsNullable = false, ColumnDescription = "程序内部使用的稳定流代码，例如 pose_controller、body24raw")]
        public string StreamCode { get; set; } = string.Empty;

        /// <summary>
        /// 管理后台或监控界面展示用名称。
        /// </summary>
        [SugarColumn(Length = 128, IsNullable = false, ColumnDescription = "管理后台或监控界面展示用名称")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 简短说明；只放公网后端可公开保存的信息，不关联内部 Outline 文档。
        /// </summary>
        [SugarColumn(Length = 512, IsNullable = true, ColumnDescription = "简短说明；只放公网后端可公开保存的信息，不关联内部 Outline 文档")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 协议版本号，对应 UDP 包内 Version 字段；没有版本字段的协议可保持 0。
        /// </summary>
        [SugarColumn(ColumnDescription = "协议版本号，对应 UDP 包内 Version 字段；没有版本字段的协议可保持 0")]
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// 固定包长，单位字节；固定长度协议填写该值，变长协议留空。
        /// </summary>
        [SugarColumn(IsNullable = true, ColumnDescription = "固定包长，单位字节；固定长度协议填写该值，变长协议留空")]
        public int? FixedDatagramBytes { get; set; }

        /// <summary>
        /// 最小合法包长，单位字节；用于接入层快速校验和监控告警。
        /// </summary>
        [SugarColumn(IsNullable = true, ColumnDescription = "最小合法包长，单位字节；用于接入层快速校验和监控告警")]
        public int? MinDatagramBytes { get; set; }

        /// <summary>
        /// 最大合法包长，单位字节；用于接入层快速校验和监控告警。
        /// </summary>
        [SugarColumn(IsNullable = true, ColumnDescription = "最大合法包长，单位字节；用于接入层快速校验和监控告警")]
        public int? MaxDatagramBytes { get; set; }

        /// <summary>
        /// UDP 逻辑 socket 组编号；相同编号表示这些流走同一个 socket 组，端口仍由 MediaServer 统一配置。
        /// </summary>
        [SugarColumn(ColumnDescription = "UDP 逻辑 socket 组编号；相同编号表示这些流走同一个 socket 组，端口仍由 MediaServer 统一配置")]
        public int UdpSocketGroupId { get; set; }

        /// <summary>
        /// 是否启用该流；关闭后运行时不应接受、订阅或转发该流。
        /// </summary>
        [SugarColumn(ColumnDescription = "是否启用该流；关闭后运行时不应接受、订阅或转发该流")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否允许该流进入 UDP 转发链；可用于只统计不转发或灰度停转。
        /// </summary>
        [SugarColumn(ColumnDescription = "是否允许该流进入 UDP 转发链；可用于只统计不转发或灰度停转")]
        public bool ForwardingEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用该流的运行时指标统计。
        /// </summary>
        [SugarColumn(ColumnDescription = "是否启用该流的运行时指标统计")]
        public bool MetricsEnabled { get; set; } = true;

        /// <summary>
        /// 允许发布该流的端角色，例如 VR、Robot、Client。
        /// </summary>
        [SugarColumn(ColumnDescription = "允许发布该流的端角色，例如 VR、Robot、Client")]
        public RegisterRequest.Types.EndpointType PublisherRole { get; set; } = RegisterRequest.Types.EndpointType.Unknown;

        /// <summary>
        /// 允许订阅该流的端角色位掩码；具体位定义由后端统一解释。
        /// </summary>
        [SugarColumn(ColumnDescription = "允许订阅该流的端角色位掩码；具体位定义由后端统一解释")]
        public int SubscriberRoleMask { get; set; }

        /// <summary>
        /// 展示排序值；数值越小越靠前。
        /// </summary>
        [SugarColumn(ColumnDescription = "展示排序值；数值越小越靠前")]
        public int SortOrder { get; set; }

        /// <summary>
        /// 记录创建时间，UTC。
        /// </summary>
        [SugarColumn(ColumnDescription = "记录创建时间，UTC")]
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 记录最后更新时间，UTC。
        /// </summary>
        [SugarColumn(ColumnDescription = "记录最后更新时间，UTC")]
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
