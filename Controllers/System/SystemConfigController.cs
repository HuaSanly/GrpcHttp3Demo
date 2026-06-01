using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.System
{
    [ApiController]
    [Route("api/system")]
    public class SystemConfigController : ControllerBase
    {
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly SessionLivenessOptions _livenessOptions;
        private readonly PairingAutoSubscribeService _autoSubscribe;

        public SystemConfigController(IConfiguration configuration, IHostEnvironment environment, SessionLivenessOptions livenessOptions, PairingAutoSubscribeService autoSubscribe)
        {
            _configuration = configuration;
            _environment = environment;
            _livenessOptions = livenessOptions;
            _autoSubscribe = autoSubscribe;
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            // 只返回“安全字段”，避免泄漏证书/密码等敏感项。
            var udpPort = _configuration.GetValue<int>("MediaServer:UdpPort", 7778);
            var udpControlTimeoutSeconds = _configuration.GetValue<int>("MediaServer:UdpControlTimeoutSeconds", 15);
            var udpRescueCooldownSeconds = _configuration.GetValue<int>("MediaServer:UdpRescueCooldownSeconds", 10);
            var udpMaxRescues = _configuration.GetValue<int>("MediaServer:UdpMaxRescues", 3);

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                environment = new
                {
                    name = _environment.EnvironmentName,
                    isDevelopment = _environment.IsDevelopment(),
                    isProduction = _environment.IsProduction()
                },
                session = new
                {
                    heartbeatIntervalSeconds = _livenessOptions.HeartbeatIntervalSeconds,
                    timeoutSeconds = _livenessOptions.TimeoutSeconds,
                    cleanupCheckIntervalMs = _livenessOptions.CleanupCheckIntervalMs
                },
                mediaServer = new
                {
                    udpPort,
                    udpControlTimeoutSeconds = Math.Max(1, udpControlTimeoutSeconds),
                    udpRescueCooldownSeconds = Math.Max(1, udpRescueCooldownSeconds),
                    udpMaxRescues = Math.Max(0, udpMaxRescues)
                }
            });
        }

        [HttpGet("pairing/auto-subscribe")]
        public IActionResult GetAutoSubscribeRules()
        {
            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                pairing = new
                {
                    autoSubscribe = new
                    {
                        rules = _autoSubscribe.GetRules()
                    }
                }
            });
        }

        [HttpPut("pairing/auto-subscribe")]
        public IActionResult UpdateAutoSubscribeRules([FromBody] PairingAutoSubscribeUpdateRequest? request)
        {
            if (request?.Rules == null || request.Rules.Length == 0)
            {
                return BadRequest(new { message = "Missing or empty rules array" });
            }

            _autoSubscribe.UpdateRules(request.Rules);
            Console.WriteLine($"[SystemConfig] Pairing auto-subscribe rules updated ({request.Rules.Length} rules)");

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                pairing = new
                {
                    autoSubscribe = new
                    {
                        rules = _autoSubscribe.GetRules()
                    }
                }
            });
        }
    }

    public sealed class PairingAutoSubscribeUpdateRequest
    {
        public PairingAutoSubscribeRule[] Rules { get; set; } = Array.Empty<PairingAutoSubscribeRule>();
    }
}

