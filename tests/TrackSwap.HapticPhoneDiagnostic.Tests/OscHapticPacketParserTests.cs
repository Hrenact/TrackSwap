using System.Buffers.Binary;
using System.Text;
using TrackSwap.HapticPhoneDiagnostic;

namespace TrackSwap.HapticPhoneDiagnostic.Tests;

public sealed class OscHapticPacketParserTests
{
    [Theory]
    [InlineData("/trackswap/left/haptic", "left")]
    [InlineData("/trackswap/right/haptic", "right")]
    public void ParsesTrackSwapHapticMessage(string address, string expectedHand)
    {
        byte[] packet = BuildPacket(address, 0.5f, 120.0f, 0.75f);

        bool parsed = OscHapticPacketParser.TryParse(packet, out OscHapticPacket value);

        Assert.True(parsed);
        Assert.Equal(address, value.Address);
        Assert.Equal(expectedHand, value.Hand);
        Assert.Equal(0.5f, value.DurationSeconds);
        Assert.Equal(120.0f, value.FrequencyHertz);
        Assert.Equal(0.75f, value.Amplitude);
    }

    [Fact]
    public void RejectsOtherAddress()
    {
        byte[] packet = BuildPacket("/other/haptic", 0.5f, 120.0f, 0.75f);

        Assert.False(OscHapticPacketParser.TryParse(packet, out _));
    }

    [Fact]
    public void RejectsWrongTypeTag()
    {
        byte[] packet = BuildPacket("/trackswap/left/haptic", 0.5f, 120.0f, 0.75f);
        int tagOffset = PaddedLength("/trackswap/left/haptic");
        packet[tagOffset + 1] = (byte)'i';

        Assert.False(OscHapticPacketParser.TryParse(packet, out _));
    }

    [Fact]
    public void RejectsTruncatedPacket()
    {
        byte[] packet = BuildPacket("/trackswap/right/haptic", 0.5f, 120.0f, 0.75f);

        Assert.False(OscHapticPacketParser.TryParse(packet.AsSpan(0, packet.Length - 1), out _));
    }

    private static byte[] BuildPacket(string address, float duration, float frequency, float amplitude)
    {
        using var stream = new MemoryStream();
        WriteString(stream, address);
        WriteString(stream, ",fff");
        WriteSingle(stream, duration);
        WriteSingle(stream, frequency);
        WriteSingle(stream, amplitude);
        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.WriteByte(0);
        while ((stream.Length & 3) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, BitConverter.SingleToInt32Bits(value));
        stream.Write(buffer);
    }

    private static int PaddedLength(string value)
    {
        return (Encoding.UTF8.GetByteCount(value) + 4) & ~3;
    }
}
