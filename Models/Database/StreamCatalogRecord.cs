using GrpcHttp3Demo.Protos;
using SqlSugar;

namespace GrpcHttp3Demo.Models.Database
{
    [SugarTable("stream_catalog")]
    public sealed class StreamCatalogRecord
    {
        [SugarColumn(IsPrimaryKey = true)]
        public byte Prefix { get; set; }

        [SugarColumn(Length = 64, IsNullable = false)]
        public string StreamCode { get; set; } = string.Empty;

        [SugarColumn(Length = 128, IsNullable = false)]
        public string DisplayName { get; set; } = string.Empty;

        public RegisterRequest.Types.EndpointType PublisherRole { get; set; } = RegisterRequest.Types.EndpointType.Unknown;
        public int SubscriberRoleMask { get; set; }
    }
}