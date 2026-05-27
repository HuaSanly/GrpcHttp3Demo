using GrpcHttp3Demo.Communication.Udp.Metrics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace GrpcHttp3Demo.Controllers.Monitoring
{
    [ApiController]
    public sealed class UdpMonitorController : ControllerBase
    {
        private readonly UdpMetricsService _metrics;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public UdpMonitorController(UdpMetricsService metrics, IHostEnvironment env, IConfiguration configuration)
        {
            _metrics = metrics;
            _env = env;
            _configuration = configuration;
        }

        [HttpGet("/api/monitor/udp/stats")]
        public IActionResult GetStats()
        {
            var monitoringEnabled = _env.IsDevelopment() || _configuration.GetValue<bool>("Monitoring:Enabled", false);
            if (!monitoringEnabled) return NotFound();
            return Ok(_metrics.Snapshot());
        }
    }
}

