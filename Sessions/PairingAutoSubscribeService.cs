using System.Text.Json;
using GrpcHttp3Demo.Communication.Udp.Metrics;
using GrpcHttp3Demo.Storage.Memory.Session;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class PairingAutoSubscribeService
    {
        private readonly SessionMemoryStore _memory;
        private readonly SessionSubscription _subscriptions;
        private readonly string _persistDir;
        private PairingAutoSubscribeOptions _options;

        public PairingAutoSubscribeService(
            SessionMemoryStore memory,
            SessionSubscription subscriptions,
            PairingAutoSubscribeOptions options,
            IHostEnvironment environment)
        {
            _memory = memory;
            _subscriptions = subscriptions;
            _options = options;
            _persistDir = Path.Combine(environment.ContentRootPath, "Configs");
            Directory.CreateDirectory(_persistDir);

            LoadPersistedOverrides();
        }

        public IReadOnlyList<PairingAutoSubscribeRule> GetRules() => _options.Rules;

        public void UpdateRules(PairingAutoSubscribeRule[] rules)
        {
            _options = new PairingAutoSubscribeOptions { Rules = rules };
            Persist();
        }

        public void ApplyAutoSubscribe(string sessionA, string sessionB)
        {
            if (!_memory.Sessions.TryGetValue(sessionA, out var ctxA)) return;
            if (!_memory.Sessions.TryGetValue(sessionB, out var ctxB)) return;
            if (string.Equals(sessionA, sessionB, StringComparison.Ordinal)) return;

            var roleA = ctxA.Role.ToString();
            var roleB = ctxB.Role.ToString();

            // A is publisher, B subscribes
            ApplyRule(sessionA, sessionB, roleA, roleB);
            // B is publisher, A subscribes
            ApplyRule(sessionB, sessionA, roleB, roleA);
        }

        public void ApplyAutoUnsubscribe(string sessionA, string sessionB)
        {
            if (!_memory.Sessions.TryGetValue(sessionA, out var ctxA)) return;
            if (!_memory.Sessions.TryGetValue(sessionB, out var ctxB)) return;
            if (string.Equals(sessionA, sessionB, StringComparison.Ordinal)) return;

            var roleA = ctxA.Role.ToString();
            var roleB = ctxB.Role.ToString();

            // Unsubscribe both directions — unsubscribe ALL media kinds
            UnsubscribeAll(sessionA, sessionB);
            UnsubscribeAll(sessionB, sessionA);
        }

        private void ApplyRule(string publisherId, string subscriberId, string publisherRole, string subscriberRole)
        {
            var rule = _options.FindRule(publisherRole, subscriberRole);
            if (rule == null || rule.MediaKinds.Length == 0) return;

            var (subVideo, subPose, subAudio, subTelemetryLowRate, subTelemetryHighRate) = ParseMediaKinds(rule.MediaKinds);

            _subscriptions.UpdateSubscription(
                publisherId,
                subscriberId,
                isSub: true,
                subVideo,
                subPose,
                subAudio,
                subTelemetryLowRate,
                subTelemetryHighRate);
        }

        private void UnsubscribeAll(string publisherId, string subscriberId)
        {
            _subscriptions.UpdateSubscription(
                publisherId,
                subscriberId,
                isSub: false,
                subVideo: false,
                subPose: false,
                subAudio: false,
                subTelemetryLowRate: false,
                subTelemetryHighRate: false);
        }

        private static (bool video, bool pose, bool audio, bool telemetryLow, bool telemetryHigh) ParseMediaKinds(string[] kinds)
        {
            bool video = false, pose = false, audio = false, tlmLow = false, tlmHigh = false;
            foreach (var kind in kinds)
            {
                switch (kind.ToLowerInvariant())
                {
                    case "video": video = true; break;
                    case "pose": pose = true; break;
                    case "audio": audio = true; break;
                    case "telemetrylowrate": tlmLow = true; break;
                    case "telemetryhighrate": tlmHigh = true; break;
                }
            }
            return (video, pose, audio, tlmLow, tlmHigh);
        }

        private void LoadPersistedOverrides()
        {
            var path = Path.Combine(_persistDir, PairingAutoSubscribeOptions.PersistFileName);
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                var overrides = PairingAutoSubscribeOptions.FromJson(json);
                if (overrides != null)
                {
                    _options = overrides;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PairingAutoSubscribe] Failed to load persisted config: {ex.Message}");
            }
        }

        private void Persist()
        {
            var path = Path.Combine(_persistDir, PairingAutoSubscribeOptions.PersistFileName);
            try
            {
                File.WriteAllText(path, _options.ToJson());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PairingAutoSubscribe] Failed to persist config: {ex.Message}");
            }
        }
    }
}
