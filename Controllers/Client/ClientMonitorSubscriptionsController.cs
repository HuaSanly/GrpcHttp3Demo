using GrpcHttp3Demo.Authentication;
using GrpcHttp3Demo.Models.Session;
using GrpcHttp3Demo.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GrpcHttp3Demo.Controllers.Client
{
    [ApiController]
    [Route("api/client/monitor/subscriptions")]
    public sealed class ClientMonitorSubscriptionsController : ControllerBase
    {
        private readonly ClientAuthService _authService;
        private readonly SessionRegistry _sessions;
        private readonly SessionSubscription _subscriptions;

        public ClientMonitorSubscriptionsController(ClientAuthService authService, SessionRegistry sessions, SessionSubscription subscriptions)
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
                publisherSessionId = SystemPublishers.MonitorPublisherSessionId,
                items = _subscriptions.ListSystemMonitorSubscriptions()
            });
        }

        [HttpPost]
        public IActionResult Subscribe([FromBody] MonitorSubscriptionRequest? request)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (request == null || string.IsNullOrWhiteSpace(request.SubscriberSessionId))
            {
                return BadRequest(new { message = "Missing subscriberSessionId" });
            }

            if (!TryParseTopics(request.Topics, out var topics, out var topicError))
            {
                return BadRequest(new { message = topicError });
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
            if (!_subscriptions.TryUpdateSystemMonitorSubscription(request.SubscriberSessionId, isSub: true, topics, intervalMs, out var message))
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                success = true,
                message,
                publisherSessionId = SystemPublishers.MonitorPublisherSessionId,
                subscriberSessionId = request.SubscriberSessionId,
                udpEndpoint = session.UdpEndpoint.ToString(),
                prefixes = SessionSubscription.ToPrefixes(topics),
                topics = SessionSubscription.ToTopicNames(topics),
                effectiveIntervalMs = intervalMs
            });
        }

        [HttpDelete("{subscriberSessionId}")]
        public IActionResult Unsubscribe([FromRoute] string subscriberSessionId)
        {
            if (!Authorize()) return Unauthorized(new { message = "Missing or invalid bearer token" });
            if (string.IsNullOrWhiteSpace(subscriberSessionId)) return BadRequest(new { message = "Missing subscriberSessionId" });

            _subscriptions.TryUpdateSystemMonitorSubscription(subscriberSessionId, isSub: false, SystemMonitorTopicMask.None, 1000, out var message);
            return Ok(new
            {
                success = true,
                message,
                publisherSessionId = SystemPublishers.MonitorPublisherSessionId,
                subscriberSessionId
            });
        }

        private bool Authorize()
        {
            return _authService.TryAuthorizeAdmin(Request.Headers.Authorization.ToString(), out _);
        }

        private static bool TryParseTopics(IReadOnlyCollection<string>? values, out SystemMonitorTopicMask topics, out string? error)
        {
            topics = SystemMonitorTopicMask.None;
            error = null;

            if (values == null || values.Count == 0)
            {
                topics = SystemMonitorTopicMask.UdpGlobal;
                return true;
            }

            foreach (var raw in values)
            {
                var value = raw.Trim().ToLowerInvariant();
                switch (value)
                {
                    case "udp":
                    case "udp_global":
                        topics |= SystemMonitorTopicMask.UdpGlobal;
                        break;
                    case "signaling":
                    case "signaling_rates":
                        topics |= SystemMonitorTopicMask.SignalingRates;
                        break;
                    case "online":
                    case "online_summary":
                        topics |= SystemMonitorTopicMask.OnlineSummary;
                        break;
                    case "runtime":
                    case "runtime_tables":
                        topics |= SystemMonitorTopicMask.RuntimeTables;
                        break;
                    default:
                        error = $"Unsupported monitor topic: {raw}";
                        return false;
                }
            }

            if (topics == SystemMonitorTopicMask.None)
            {
                error = "Missing monitor topics";
                return false;
            }

            return true;
        }
    }

    public sealed class MonitorSubscriptionRequest
    {
        public string SubscriberSessionId { get; set; } = string.Empty;
        public string[]? Topics { get; set; }
        public int? IntervalMs { get; set; }
    }
}