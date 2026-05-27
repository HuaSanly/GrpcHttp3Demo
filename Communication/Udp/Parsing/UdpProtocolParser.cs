using System.Text;

namespace GrpcHttp3Demo.Communication.Udp.Parsing
{
    internal enum UdpDatagramKind : byte
    {
        Unknown = 0,
        Hello = (byte)'H',
        Ping = (byte)'P',
        Video = 0x01,
        Pose = 0x02,
        Feedback = 0x03,
        Audio = 0x04
    }

    internal enum UdpControlPacketType
    {
        Hello,
        Ping
    }

    internal readonly record struct UdpControlPacket(
        UdpControlPacketType Type,
        string RawType,
        string SessionId,
        string TimestampText,
        long TimestampSeconds,
        string Signature);

    internal static class UdpProtocolParser
    {
        public static UdpDatagramKind GetDatagramKind(ReadOnlySpan<byte> packet)
        {
            if (packet.IsEmpty)
            {
                return UdpDatagramKind.Unknown;
            }

            return packet[0] switch
            {
                (byte)'H' => UdpDatagramKind.Hello,
                (byte)'P' => UdpDatagramKind.Ping,
                0x01 => UdpDatagramKind.Video,
                0x02 => UdpDatagramKind.Pose,
                0x03 => UdpDatagramKind.Feedback,
                0x04 => UdpDatagramKind.Audio,
                _ => UdpDatagramKind.Unknown
            };
        }

        public static bool TryParseControlPacket(ReadOnlySpan<byte> packet, out UdpControlPacket controlPacket)
        {
            controlPacket = default;

            if (!TryReadSegment(packet, out var typeBytes, out var remainder) ||
                !TryReadSegment(remainder, out var sessionIdBytes, out remainder) ||
                !TryReadSegment(remainder, out var timestampBytes, out remainder) ||
                !TryReadFinalSegment(remainder, out var signatureBytes))
            {
                return false;
            }

            var rawType = Encoding.UTF8.GetString(typeBytes);
            UdpControlPacketType type;
            if (rawType.Equals("HELLO", StringComparison.OrdinalIgnoreCase))
            {
                type = UdpControlPacketType.Hello;
            }
            else if (rawType.Equals("PING", StringComparison.OrdinalIgnoreCase))
            {
                type = UdpControlPacketType.Ping;
            }
            else
            {
                return false;
            }

            var sessionId = Encoding.UTF8.GetString(sessionIdBytes);
            if (!Guid.TryParse(sessionId, out _))
            {
                return false;
            }

            var timestampText = Encoding.UTF8.GetString(timestampBytes);
            if (!long.TryParse(timestampText, out var timestampSeconds))
            {
                return false;
            }

            if (signatureBytes.Length != 64 || !IsHex(signatureBytes))
            {
                return false;
            }

            var signature = Encoding.UTF8.GetString(signatureBytes);
            controlPacket = new UdpControlPacket(type, rawType, sessionId, timestampText, timestampSeconds, signature);
            return true;
        }

        private static bool TryReadSegment(ReadOnlySpan<byte> source, out ReadOnlySpan<byte> segment, out ReadOnlySpan<byte> remainder)
        {
            var separatorIndex = source.IndexOf((byte)'|');
            if (separatorIndex <= 0)
            {
                segment = default;
                remainder = default;
                return false;
            }

            segment = source[..separatorIndex];
            remainder = source[(separatorIndex + 1)..];
            return true;
        }

        private static bool TryReadFinalSegment(ReadOnlySpan<byte> source, out ReadOnlySpan<byte> segment)
        {
            if (source.IsEmpty || source.IndexOf((byte)'|') >= 0)
            {
                segment = default;
                return false;
            }

            segment = source;
            return true;
        }

        private static bool IsHex(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                var isHex =
                    (value >= (byte)'0' && value <= (byte)'9') ||
                    (value >= (byte)'a' && value <= (byte)'f') ||
                    (value >= (byte)'A' && value <= (byte)'F');

                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
