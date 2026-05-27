namespace GrpcHttp3Demo.Models.Authentication
{
    public sealed class ClientLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
