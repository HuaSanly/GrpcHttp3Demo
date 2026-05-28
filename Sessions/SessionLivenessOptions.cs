using Microsoft.Extensions.Configuration;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class SessionLivenessOptions
    {
        public int HeartbeatIntervalSeconds { get; init; }
        public int TimeoutSeconds { get; init; }
        public int CleanupCheckIntervalMs { get; init; }

        public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
        public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
        public TimeSpan CleanupCheckInterval => TimeSpan.FromMilliseconds(CleanupCheckIntervalMs);

        public static SessionLivenessOptions FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection("Session");

            return new SessionLivenessOptions
            {
                HeartbeatIntervalSeconds = Math.Max(1, section.GetValue<int>("HeartbeatIntervalSeconds", 3)),
                TimeoutSeconds = Math.Max(1, section.GetValue<int>("TimeoutSeconds", 8)),
                CleanupCheckIntervalMs = Math.Max(250, section.GetValue<int>("CleanupCheckIntervalMs", 1000))
            };
        }
    }
}