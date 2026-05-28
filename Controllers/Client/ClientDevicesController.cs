using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Protos;
using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Client
{
    [ApiController]
    [Route("api/client/devices")]
    public sealed class ClientDevicesController : ControllerBase
    {
        private readonly SessionQueries _sessions;
        private readonly ClientAuthService _authService;
        private readonly SessionLivenessOptions _livenessOptions;

        public ClientDevicesController(SessionQueries sessions, ClientAuthService authService, SessionLivenessOptions livenessOptions)
        {
            _sessions = sessions;
            _authService = authService;
            _livenessOptions = livenessOptions;
        }

        [HttpGet("robots")]
        public IActionResult GetRobots([FromQuery] bool onlineOnly = true)
        {
            return BuildRoleList(RegisterRequest.Types.EndpointType.Robot, onlineOnly);
        }

        [HttpGet("vrs")]
        public IActionResult GetVrs([FromQuery] bool onlineOnly = true)
        {
            return BuildRoleList(RegisterRequest.Types.EndpointType.Vr, onlineOnly);
        }

        [HttpGet("clients")]
        public IActionResult GetClients([FromQuery] bool onlineOnly = true)
        {
            return BuildRoleList(RegisterRequest.Types.EndpointType.Client, onlineOnly);
        }

        private IActionResult BuildRoleList(RegisterRequest.Types.EndpointType role, bool onlineOnly)
        {
            if (!_authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out var session))
            {
                return Unauthorized(new { message = "Missing or invalid bearer token" });
            }

            var onlineTimeout = _livenessOptions.Timeout;
            var items = _sessions.ListSessions(onlineTimeout, role, onlineOnly);

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                requestedBy = new
                {
                    username = session.Username,
                    role = session.Role
                },
                timeoutSeconds = (int)onlineTimeout.TotalSeconds,
                role = role.ToString(),
                onlineOnly,
                items
            });
        }
    }
}
