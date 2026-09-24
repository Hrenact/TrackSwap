using System.Text;

namespace TrackSwap.Runtime;

internal static class OscPacketWriter
{
    public static byte[] BuildHapticMessage(
        string address,
        float durationSeconds,
        float frequency,
        float amplitude)
    {
        using var stream = new MemoryStream();
        WritePaddedString(stream, address);
        WritePaddedString(stream, ",fff");
        WriteSingleBigEndian(stream, durationSeconds);
        WriteSingleBigEndian(stream, frequency);
        WriteSingleBigEndian(stream, amplitude);
        return stream.ToArray();
    }

    private static void WritePaddedString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        while ((stream.Length & 3) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteSingleBigEndian(Stream stream, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        stream.Write(bytes, 0, bytes.Length);
    }
}
