using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Storage.Postgres;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.System
{
    [ApiController]
    [Route("api/system/database")]
    public sealed class SystemDatabaseController : ControllerBase
    {
        private readonly ClientAuthService _authService;
        private readonly PostgresConnectionOptions _options;
        private readonly PostgresConnectionTester _tester;

        public SystemDatabaseController(ClientAuthService authService, PostgresConnectionOptions options, PostgresConnectionTester tester)
        {
            _authService = authService;
            _options = options;
            _tester = tester;
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            if (!_authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out var session))
            {
                return Unauthorized(new { message = "Missing or invalid bearer token" });
            }

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                requestedBy = new
                {
                    username = session.Username,
                    role = session.Role
                },
                postgres = new
                {
                    enabled = _options.Enabled,
                    host = _options.Host,
                    port = _options.Port,
                    database = _options.Database,
                    username = _options.Username,
                    sslMode = _options.SslMode,
                    timeoutSeconds = _options.TimeoutSeconds,
                    passwordConfigured = !string.IsNullOrWhiteSpace(_options.Password)
                }
            });
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test(CancellationToken cancellationToken)
        {
            if (!_authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out var session))
            {
                return Unauthorized(new { message = "Missing or invalid bearer token" });
            }

            var result = await _tester.TestAsync(cancellationToken);
            if (!result.Enabled)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    updatedUtc = DateTime.UtcNow,
                    requestedBy = new
                    {
                        username = session.Username,
                        role = session.Role
                    },
                    ok = false,
                    message = result.Message
                });
            }

            if (!result.Success)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    updatedUtc = DateTime.UtcNow,
                    requestedBy = new
                    {
                        username = session.Username,
                        role = session.Role
                    },
                    ok = false,
                    message = result.Message,
                    target = new
                    {
                        host = _options.Host,
                        port = _options.Port,
                        database = _options.Database,
                        username = _options.Username
                    }
                });
            }

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                requestedBy = new
                {
                    username = session.Username,
                    role = session.Role
                },
                ok = true,
                database = result.Database,
                username = result.Username,
                serverAddress = result.ServerAddress,
                serverPort = result.ServerPort,
                serverVersion = result.ServerVersion
            });
        }
    }
}