namespace GrpcHttp3Demo.Models.Authentication
{
    public sealed class ClientLoginResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
