using GrpcHttp3Demo.Utils;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.System
{
    [ApiController]
    [Route("api/system")]
    public class SystemConfigController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public SystemConfigController(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            // 只返回“安全字段”，避免泄漏证书/密码等敏感项。
            var udpPort = _configuration.GetValue<int>("MediaServer:UdpPort", 7778);
            var udpControlTimeoutSeconds = _configuration.GetValue<int>("MediaServer:UdpControlTimeoutSeconds", 15);
            var udpRescueCooldownSeconds = _configuration.GetValue<int>("MediaServer:UdpRescueCooldownSeconds", 10);
            var udpMaxRescues = _configuration.GetValue<int>("MediaServer:UdpMaxRescues", 3);

            var sessionTimeoutSeconds = 30;

            // 配置值（仅用于展示；实际生效值由 AppConfig 做环境约束）
            bool? broadcastConfigured = null;
            if (_environment.IsDevelopment())
            {
                broadcastConfigured = _configuration.GetValue<bool>("DevSettings:BroadcastToAll", false);
            }

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                environment = new
                {
                    name = _environment.EnvironmentName,
                    isDevelopment = _environment.IsDevelopment(),
                    isProduction = _environment.IsProduction()
                },
                mode = new
                {
                    broadcastToAllEffective = AppConfig.IsBroadcastToAll,
                    broadcastToAllConfigured = broadcastConfigured
                },
                session = new
                {
                    timeoutSeconds = Math.Max(1, sessionTimeoutSeconds)
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
    }
}

