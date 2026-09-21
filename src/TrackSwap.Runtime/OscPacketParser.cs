using System.Buffers.Binary;
using System.Text;

namespace TrackSwap.Runtime;

internal readonly record struct OscValue(string Address, double Value);

internal static class OscPacketParser
{
    public static IReadOnlyList<OscValue> Parse(byte[] packet)
    {
        var values = new List<OscValue>();
        ParsePacket(packet, values, 0);
        return values;
    }

    private static void ParsePacket(ReadOnlySpan<byte> packet, ICollection<OscValue> values, int depth)
    {
        if (depth > 4 || packet.Length < 4)
        {
            return;
        }
        if (packet.Length >= 16 && packet.Slice(0, 8).SequenceEqual("#bundle\0"u8))
        {
            int offset = 16;
            while (offset + 4 <= packet.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(offset, 4));
                offset += 4;
                if (length <= 0 || offset + length > packet.Length)
                {
                    return;
                }
                ParsePacket(packet.Slice(offset, length), values, depth + 1);
                offset += length;
            }
            return;
        }

        int position = 0;
        if (!TryReadString(packet, ref position, out string address) ||
            !TryReadString(packet, ref position, out string tags) ||
            string.IsNullOrWhiteSpace(address) || tags.Length < 2 || tags[0] != ',')
        {
            return;
        }

        double value;
        switch (tags[1])
        {
            case 'f' when position + 4 <= packet.Length:
                value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(packet.Slice(position, 4)));
                break;
            case 'i' when position + 4 <= packet.Length:
                value = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(position, 4));
                break;
            case 'd' when position + 8 <= packet.Length:
                value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(packet.Slice(position, 8)));
                break;
            case 'h' when position + 8 <= packet.Length:
                value = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(position, 8));
                break;
            case 'T':
                value = 1.0;
                break;
            case 'F':
                value = 0.0;
                break;
            default:
                return;
        }
        if (!double.IsNaN(value) && !double.IsInfinity(value))
        {
            values.Add(new OscValue(address, value));
        }
    }

    private static bool TryReadString(ReadOnlySpan<byte> packet, ref int position, out string value)
    {
        value = string.Empty;
        int end = packet.Slice(position).IndexOf((byte)0);
        if (end < 0)
        {
            return false;
        }
        value = Encoding.UTF8.GetString(packet.Slice(position, end));
        position += end + 1;
        position = (position + 3) & ~3;
        return position <= packet.Length;
    }
}
