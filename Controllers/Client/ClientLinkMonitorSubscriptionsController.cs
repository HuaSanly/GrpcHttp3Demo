using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Client
{
    [ApiController]
    [Route("api/client/monitor/topology-subscriptions")]
    [Route("api/client/monitor/link-subscriptions")]
    public sealed class ClientLinkMonitorSubscriptionsController : ControllerBase
    {
        private readonly ClientAuthService _authService;
        private readonly SessionRegistry _sessions;
        private readonly SessionSubscription _subscriptions;

        public ClientLinkMonitorSubscriptionsController(ClientAuthService authService, SessionRegistry sessions, SessionSubscription subscriptions)
        {
            _authService = authService;
            _sessions = sessions;
            _subscriptions = subscriptions;
        }

        [HttpGet]
        public IActionResult List()
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });

            return Ok(new
            {
                updatedUtc = DateTime.UtcNow,
                publisherSessionId = SystemPublishers.LinkMonitorPublisherSessionId,
                items = _subscriptions.ListTopologyMonitorSubscriptions()
            });
        }

        [HttpPost]
        public IActionResult Subscribe([FromBody] TopologyMonitorSubscriptionRequest? request)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (request == null || string.IsNullOrWhiteSpace(request.SubscriberSessionId))
            {
                return BadRequest(new { message = "Missing subscriberSessionId" });
            }

            var session = _sessions.GetSession(request.SubscriberSessionId);
            if (session == null)
            {
                return NotFound(new { message = $"Session not found: {request.SubscriberSessionId}" });
            }

            if (session.UdpEndpoint == null)
            {
                return Conflict(new { message = $"Session has no UDP endpoint: {request.SubscriberSessionId}" });
            }

            var intervalMs = Math.Clamp(request.IntervalMs ?? 1000, 250, 10_000);
            if (!_subscriptions.TryUpdateTopologyMonitorSubscription(request.SubscriberSessionId, isSub: true, intervalMs, request.TopologyId, out var message))
            {
                return BadRequest(new { message });
            }

            var normalizedTopologyId = NormalizeTopologyId(request.TopologyId);

            return Ok(new
            {
                success = true,
                message,
                publisherSessionId = SystemPublishers.LinkMonitorPublisherSessionId,
                subscriberSessionId = request.SubscriberSessionId,
                udpEndpoint = session.UdpEndpoint.ToString(),
                prefix = "0x08",
                topic = "udp_link_metrics",
                topologyId = normalizedTopologyId,
                effectiveIntervalMs = intervalMs
            });
        }

        [HttpDelete("{subscriberSessionId}")]
        public IActionResult Unsubscribe([FromRoute] string subscriberSessionId)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (string.IsNullOrWhiteSpace(subscriberSessionId)) return BadRequest(new { message = "Missing subscriberSessionId" });

            _subscriptions.TryUpdateTopologyMonitorSubscription(subscriberSessionId, isSub: false, 1000, null, out var message);
            return Ok(new
            {
                success = true,
                message,
                publisherSessionId = SystemPublishers.LinkMonitorPublisherSessionId,
                subscriberSessionId
            });
        }

        private bool Authorize()
        {
            return _authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out _);
        }

        private static string? NormalizeTopologyId(string? topologyId)
        {
            var normalized = topologyId?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }

    public sealed class TopologyMonitorSubscriptionRequest
    {
        public string SubscriberSessionId { get; set; } = string.Empty;
        public string? TopologyId { get; set; }
        public int? IntervalMs { get; set; }
    }
}