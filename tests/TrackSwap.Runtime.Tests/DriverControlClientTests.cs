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

    [Fact]
    public void EncodesExplicitManualHmdPoseWithoutDevicePaths()
    {
        var route = new RouteConfiguration
        {
            Enabled = true,
            Mode = RouteMode.VirtualHmd,
            VirtualDeviceSlot = 4,
            PoseSourceKind = PoseSourceKind.Manual,
            SourceDevicePath = string.Empty,
            ManualPose = new PoseOffset
            {
                TranslationX = 25.0,
                TranslationY = 175.0,
                TranslationZ = -50.0,
                RotationW = 1.0
            }
        };

        byte[] payload = DriverControlClient.BuildHmdSnapshotPayload(route, false, 123);
        using var reader = new BinaryReader(new MemoryStream(payload));

        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal((ulong)123, reader.ReadUInt64());
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(0.25, reader.ReadDouble(), 12);
        Assert.Equal(1.75, reader.ReadDouble(), 12);
        Assert.Equal(-0.5, reader.ReadDouble(), 12);
        Assert.Equal(0.0, reader.ReadDouble(), 12);
        Assert.Equal(0.0, reader.ReadDouble(), 12);
        Assert.Equal(0.0, reader.ReadDouble(), 12);
        Assert.Equal(1.0, reader.ReadDouble(), 12);
        Assert.Equal(DriverControlProtocol.ApplyHmdSnapshotFixedBytes, payload.Length);
    }

    [Fact]
    public void RejectsImplicitSourceFreeHmdSnapshot()
    {
        var route = new RouteConfiguration
        {
            Enabled = true,
            Mode = RouteMode.VirtualHmd,
            PoseSourceKind = PoseSourceKind.Device,
            SourceDevicePath = string.Empty
        };

        Assert.Throws<IOException>(() =>
            DriverControlClient.BuildHmdSnapshotPayload(route, false, 1));
    }
}
