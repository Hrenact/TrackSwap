using System.Buffers.Binary;
using System.Text;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class OscPacketWriterTests
{
    [Fact]
    public void WritesHapticMessageWithThreeBigEndianFloats()
    {
        byte[] packet = OscPacketWriter.BuildHapticMessage(
            "/trackswap/right/haptic",
            0.25f,
            120.0f,
            0.75f);

        int offset = 0;
        Assert.Equal("/trackswap/right/haptic", ReadPaddedString(packet, ref offset));
        Assert.Equal(",fff", ReadPaddedString(packet, ref offset));
        Assert.Equal(0.25f, ReadSingle(packet, ref offset));
        Assert.Equal(120.0f, ReadSingle(packet, ref offset));
        Assert.Equal(0.75f, ReadSingle(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    private static string ReadPaddedString(byte[] packet, ref int offset)
    {
        int end = Array.IndexOf(packet, (byte)0, offset);
        Assert.True(end >= offset);
        string value = Encoding.UTF8.GetString(packet, offset, end - offset);
        offset = (end + 4) & ~3;
        return value;
    }

    private static float ReadSingle(byte[] packet, ref int offset)
    {
        int bits = BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(offset, 4));
        offset += 4;
        return BitConverter.Int32BitsToSingle(bits);
    }
}
