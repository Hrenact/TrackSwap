using TrackSwap.Protocol;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class CalibrationProfileStoreTests
{
    [Fact]
    public void SameNameAtomicallyReplacesEarlierProfile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrackSwap.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "calibration-profiles.json");
        try
        {
            var store = new CalibrationProfileStore(path);
            store.Add(CreateProfile("first", "Grip", 0.1));
            store.Add(CreateProfile("second", "grip", 0.2));

            CalibrationProfilesSnapshot snapshot = store.Load();

            CalibrationProfile profile = Assert.Single(snapshot.Profiles);
            Assert.Equal("second", profile.ProfileId);
            Assert.Equal(0.2, profile.Offset.TranslationX, 9);
            Assert.True(File.Exists(path + ".previous"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void OpenVrPoseInteropLayoutMatchesPinnedHeader()
    {
        Assert.Equal("FnTable:IVRSystem_026", OpenVrCalibrationService.SystemInterfaceVersion);
        Assert.Equal(12, OpenVrCalibrationService.GetDeviceToAbsoluteTrackingPoseIndex);
        Assert.Equal(21, OpenVrCalibrationService.IsTrackedDeviceConnectedIndex);
        Assert.Equal(28, OpenVrCalibrationService.GetStringTrackedDevicePropertyIndex);
        Assert.Equal(80, OpenVrCalibrationService.TrackedDevicePoseBytes);
    }

    [Fact]
    public void DriverTelemetryBinaryLayoutRoundTrips()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ulong)42);
            writer.Write((ulong)1234);
            for (int poseIndex = 0; poseIndex < 3; poseIndex++)
            {
                writer.Write((byte)1);
                writer.Write((byte)(poseIndex == 1 ? 0 : 1));
                writer.Write(200 + poseIndex);
                for (int valueIndex = 0; valueIndex < 7; valueIndex++)
                {
                    writer.Write((double)((poseIndex * 10) + valueIndex));
                }
            }
        }

        PoseTelemetrySnapshot snapshot = DriverControlClient.ParseTelemetry(stream.ToArray());

        Assert.Equal((ulong)42, snapshot.Sequence);
        Assert.Equal(1234, snapshot.AppliedRevision);
        Assert.True(snapshot.Source.Valid);
        Assert.False(snapshot.Output.Valid);
        Assert.Equal(20, snapshot.Target.PositionX);
        Assert.Equal(26, snapshot.Target.RotationW);
    }

    private static CalibrationProfile CreateProfile(string id, string name, double translationX)
    {
        return new CalibrationProfile
        {
            ProfileId = id,
            Name = name,
            SourceDevicePath = "/devices/source",
            TargetDevicePath = "/devices/target",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            SampleCount = 90,
            Offset = new PoseOffset { TranslationX = translationX }
        };
    }
}
