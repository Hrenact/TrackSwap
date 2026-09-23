using TrackSwap.Protocol;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class DriverControlClientTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.01)]
    [InlineData(-33.5, -0.335)]
    [InlineData(1000.0, 10.0)]
    public void ConvertsConfigurationCentimetresToOpenVrMetres(
        double centimetres,
        double expectedMetres)
    {
        Assert.Equal(expectedMetres, DriverControlClient.ToOpenVrMetres(centimetres), 12);
    }

    [Fact]
    public void ParsesFixedHapticFeedbackBatch()
    {
        byte[] payload = new byte[DriverControlProtocol.HapticFeedbackBatchBytes];
        using (var stream = new MemoryStream(payload, writable: true))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)1);
            writer.Write((ulong)42);
            writer.Write((byte)ControllerHand.Right);
            writer.Write(0.25f);
            writer.Write(120.0f);
            writer.Write(0.75f);
        }

        HapticFeedbackEvent feedback = Assert.Single(
            DriverControlClient.ParseHapticFeedbackBatch(payload));

        Assert.Equal((ulong)42, feedback.Sequence);
        Assert.Equal(ControllerHand.Right, feedback.Hand);
        Assert.Equal(0.25f, feedback.DurationSeconds);
        Assert.Equal(120.0f, feedback.Frequency);
        Assert.Equal(0.75f, feedback.Amplitude);
    }
}
