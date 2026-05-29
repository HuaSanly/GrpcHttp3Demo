using GrpcHttp3Demo.Protos;
using SqlSugar;

namespace GrpcHttp3Demo.Models.Database
{
    public enum DeviceRegistrationStatus
    {
        Pending = 1,
        Active = 2,
        Disabled = 3,
        Expired = 4
    }

    [SugarTable("devices")]
    public sealed class DeviceRecord
    {
        [SugarColumn(IsPrimaryKey = true, Length = 64, IsNullable = false)]
        public string DeviceUid { get; set; } = string.Empty;

        public RegisterRequest.Types.EndpointType Role { get; set; } = RegisterRequest.Types.EndpointType.Unknown;
        public DeviceRegistrationStatus Status { get; set; } = DeviceRegistrationStatus.Pending;

        [SugarColumn(Length = 64, IsNullable = true)]
        public string MacAddress { get; set; } = string.Empty;

        public DateTime RegistrationExpiresUtc { get; set; }
        public DateTime? BoundUtc { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}