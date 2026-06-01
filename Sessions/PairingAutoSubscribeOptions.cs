using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace GrpcHttp3Demo.Sessions
{
    public sealed class PairingAutoSubscribeRule
    {
        public string PublisherRole { get; set; } = string.Empty;
        public string SubscriberRole { get; set; } = string.Empty;
        public string[] MediaKinds { get; set; } = Array.Empty<string>();
    }

    public sealed class PairingAutoSubscribeOptions
    {
        public const string ConfigSection = "Pairing:AutoSubscribe";
        public const string PersistFileName = "pairing-auto-subscribe.json";

        public PairingAutoSubscribeRule[] Rules { get; set; } = Array.Empty<PairingAutoSubscribeRule>();

        public static PairingAutoSubscribeOptions FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection(ConfigSection);
            var rules = section.GetSection("Rules").Get<PairingAutoSubscribeRule[]>();
            return new PairingAutoSubscribeOptions { Rules = rules ?? Array.Empty<PairingAutoSubscribeRule>() };
        }

        public PairingAutoSubscribeRule? FindRule(string publisherRole, string subscriberRole)
        {
            return Rules.FirstOrDefault(r =>
                string.Equals(r.PublisherRole, publisherRole, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.SubscriberRole, subscriberRole, StringComparison.OrdinalIgnoreCase));
        }

        public PairingAutoSubscribeOptions Clone()
        {
            return new PairingAutoSubscribeOptions
            {
                Rules = Rules.Select(r => new PairingAutoSubscribeRule
                {
                    PublisherRole = r.PublisherRole,
                    SubscriberRole = r.SubscriberRole,
                    MediaKinds = (string[])r.MediaKinds.Clone()
                }).ToArray()
            };
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(new { pairing = new { autoSubscribe = new { rules = Rules } } },
                new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        }

        public static PairingAutoSubscribeOptions? FromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("pairing", out var pairing)) return null;
            if (!pairing.TryGetProperty("autoSubscribe", out var autoSub)) return null;
            if (!autoSub.TryGetProperty("rules", out var rulesElement)) return null;

            var rules = JsonSerializer.Deserialize<PairingAutoSubscribeRule[]>(rulesElement.GetRawText());
            return rules != null ? new PairingAutoSubscribeOptions { Rules = rules } : null;
        }
    }
}
