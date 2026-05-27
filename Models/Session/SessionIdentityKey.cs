using GrpcHttp3Demo.Protos;

namespace GrpcHttp3Demo.Models.Session
{
    public readonly record struct SessionIdentityKey(string DeviceId, RegisterRequest.Types.EndpointType Role)
    {
        public static SessionIdentityKey From(string deviceId, RegisterRequest.Types.EndpointType role)
        {
            deviceId ??= string.Empty;
            var normalized = deviceId.Trim().ToLowerInvariant();
            return new SessionIdentityKey(normalized, role);
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(DeviceId) && Role != RegisterRequest.Types.EndpointType.Unknown;
    }
}
