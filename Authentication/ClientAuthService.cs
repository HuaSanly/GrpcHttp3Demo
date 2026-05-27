using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GrpcHttp3Demo.Models.Authentication;

namespace GrpcHttp3Demo.Authentication
{
    public sealed class ClientAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<string, ClientTokenSession> _tokens = new(StringComparer.Ordinal);

        public ClientAuthService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ClientLoginResult? Login(string username, string password)
        {
            CleanupExpiredTokens();

            var configuredUsername = _configuration["ClientApiAuth:AdminUsername"] ?? "admin";
            var configuredPassword = _configuration["ClientApiAuth:AdminPassword"] ?? "Admin!20260523";

            if (!SecureEquals(username, configuredUsername) || !SecureEquals(password, configuredPassword))
            {
                return null;
            }

            var ttlMinutes = Math.Max(1, _configuration.GetValue<int?>("ClientApiAuth:TokenTtlMinutes") ?? 480);
            var expiresUtc = DateTime.UtcNow.AddMinutes(ttlMinutes);
            var token = CreateToken();

            _tokens[token] = new ClientTokenSession
            {
                Username = configuredUsername,
                Role = "admin",
                ExpiresUtc = expiresUtc
            };

            return new ClientLoginResult
            {
                AccessToken = token,
                ExpiresUtc = expiresUtc,
                Username = configuredUsername,
                Role = "admin"
            };
        }

        public bool TryAuthorizeAdmin(string? authorizationHeader, out ClientTokenSession session)
        {
            CleanupExpiredTokens();

            session = new ClientTokenSession();

            var token = ExtractBearerToken(authorizationHeader);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (!_tokens.TryGetValue(token, out var existingSession))
            {
                return false;
            }

            session = existingSession;

            if (session.ExpiresUtc <= DateTime.UtcNow)
            {
                _tokens.TryRemove(token, out _);
                return false;
            }

            return string.Equals(session.Role, "admin", StringComparison.OrdinalIgnoreCase);
        }

        private void CleanupExpiredTokens()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _tokens)
            {
                if (kv.Value.ExpiresUtc <= now)
                {
                    _tokens.TryRemove(kv.Key, out _);
                }
            }
        }

        private static string CreateToken()
        {
            Span<byte> buffer = stackalloc byte[32];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToHexString(buffer).ToLowerInvariant();
        }

        private static string? ExtractBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            return authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? authorizationHeader[prefix.Length..].Trim()
                : null;
        }

        private static bool SecureEquals(string a, string b)
        {
            var left = Encoding.UTF8.GetBytes(a ?? string.Empty);
            var right = Encoding.UTF8.GetBytes(b ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
    }

}
