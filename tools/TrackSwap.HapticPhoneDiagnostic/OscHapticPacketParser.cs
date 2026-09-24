using System.Buffers.Binary;
using System.Text;

namespace TrackSwap.HapticPhoneDiagnostic;

internal readonly record struct OscHapticPacket(
    string Address,
    string Hand,
    float DurationSeconds,
    float FrequencyHertz,
    float Amplitude);

internal static class OscHapticPacketParser
{
    private const string LeftAddress = "/trackswap/left/haptic";
    private const string RightAddress = "/trackswap/right/haptic";

    public static bool TryParse(ReadOnlySpan<byte> packet, out OscHapticPacket value)
    {
        value = default;
        int position = 0;
        if (!TryReadPaddedString(packet, ref position, out string address) ||
            !TryReadPaddedString(packet, ref position, out string tags) ||
            tags != ",fff" ||
            position + 12 > packet.Length)
        {
            return false;
        }

        string hand;
        if (address == LeftAddress)
        {
            hand = "left";
        }
        else if (address == RightAddress)
        {
            hand = "right";
        }
        else
        {
            return false;
        }

        float duration = ReadSingleBigEndian(packet.Slice(position, 4));
        float frequency = ReadSingleBigEndian(packet.Slice(position + 4, 4));
        float amplitude = ReadSingleBigEndian(packet.Slice(position + 8, 4));
        if (!float.IsFinite(duration) || !float.IsFinite(frequency) || !float.IsFinite(amplitude))
        {
            return false;
        }

        value = new OscHapticPacket(address, hand, duration, frequency, amplitude);
        return true;
    }

    private static float ReadSingleBigEndian(ReadOnlySpan<byte> bytes)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes));
    }

    private static bool TryReadPaddedString(ReadOnlySpan<byte> packet, ref int position, out string value)
    {
        value = string.Empty;
        if (position >= packet.Length)
        {
            return false;
        }

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
