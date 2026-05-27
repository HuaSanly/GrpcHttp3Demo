namespace GrpcHttp3Demo.Models.Authentication
{
    public sealed class ClientTokenSession
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
    }
}
