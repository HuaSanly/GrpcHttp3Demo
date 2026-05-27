using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Models.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Client
{
    [ApiController]
    [Route("api/client/auth")]
    public sealed class ClientAuthController : ControllerBase
    {
        private readonly ClientAuthService _authService;

        public ClientAuthController(ClientAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] ClientLoginRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Missing username or password" });
            }

            var result = _authService.Login(request.Username, request.Password);
            if (result == null)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            return Ok(new
            {
                tokenType = "Bearer",
                accessToken = result.AccessToken,
                expiresUtc = result.ExpiresUtc,
                user = new
                {
                    username = result.Username,
                    role = result.Role
                }
            });
        }
    }

}
