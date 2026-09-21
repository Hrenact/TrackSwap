using System.Buffers.Binary;
using System.Text;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class OscPacketParserTests
{
    [Fact]
    public void ParsesFloatAndBooleanMessages()
    {
        IReadOnlyList<OscValue> axis = OscPacketParser.Parse(BuildMessage("/trackswap/left/joystick/x", ",f", 0.75F));
        IReadOnlyList<OscValue> button = OscPacketParser.Parse(BuildMessage("/trackswap/left/button/x", ",T", null));

        Assert.Single(axis);
        Assert.Equal(0.75, axis[0].Value, 5);
        Assert.Single(button);
        Assert.Equal(1.0, button[0].Value);
    }

    [Fact]
    public void IgnoresMalformedAndNonFiniteValues()
    {
        Assert.Empty(OscPacketParser.Parse(new byte[] { 1, 2, 3, 4 }));
        Assert.Empty(OscPacketParser.Parse(BuildMessage("/value", ",f", float.NaN)));
    }

    private static byte[] BuildMessage(string address, string tags, float? value)
    {
        var bytes = new List<byte>();
        AddString(bytes, address);
        AddString(bytes, tags);
        if (value.HasValue)
        {
            Span<byte> encoded = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(encoded, BitConverter.SingleToInt32Bits(value.Value));
            bytes.AddRange(encoded.ToArray());
        }
        return bytes.ToArray();
    }

    private static void AddString(ICollection<byte> bytes, string value)
    {
        foreach (byte item in Encoding.UTF8.GetBytes(value)) bytes.Add(item);
        bytes.Add(0);
        while (bytes.Count % 4 != 0) bytes.Add(0);
    }
}
