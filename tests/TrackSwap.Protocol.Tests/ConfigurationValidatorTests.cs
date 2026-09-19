using TrackSwap.Protocol;

namespace TrackSwap.Protocol.Tests;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void IdentityRouteWithExactPathsIsValid()
    {
        var configuration = CreateConfiguration();

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
        Assert.Equal("TRKSWAP-PROXY-00", ProtocolConstants.GetVirtualSerial(0));
    }

    [Fact]
    public void RejectsDuplicateSlotsAndTargets()
    {
        var configuration = CreateConfiguration();
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "waist-backup",
            VirtualDeviceSlot = 0,
            SourceDevicePath = "/devices/lighthouse/LHR-BBBBBBBB",
            TargetDevicePath = ProtocolConstants.LeftHandRolePath,
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("slot 0", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("assigned more than once", StringComparison.Ordinal) && error.Contains(ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNonFiniteAndZeroLengthRotation()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Offset.RotationW = 0;
        configuration.Routes[0].Offset.TranslationX = double.NaN;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("finite", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRouteCycles()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].SourceDevicePath = "/devices/a";
        configuration.Routes[0].TargetDevicePath = "/devices/b";
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "return-edge",
            VirtualDeviceSlot = 1,
            SourceDevicePath = "/devices/b",
            TargetDevicePath = "/devices/a",
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void DriverControlHeaderContractIsStable()
    {
        Assert.Equal(0x50575354U, DriverControlProtocol.Magic);
        Assert.Equal(20, DriverControlProtocol.HeaderBytes);
        Assert.Equal(
            0x8001,
            DriverControlProtocol.SetSourceMessageType | DriverControlProtocol.ResponseFlag);
        Assert.Equal(
            0x8002,
            DriverControlProtocol.SetOffsetMessageType | DriverControlProtocol.ResponseFlag);
        Assert.Equal(
            0x8003,
            DriverControlProtocol.ApplySnapshotMessageType | DriverControlProtocol.ResponseFlag);
        Assert.Equal(
            0x8004,
            DriverControlProtocol.GetTelemetryMessageType | DriverControlProtocol.ResponseFlag);
        Assert.Equal(202, DriverControlProtocol.TelemetrySnapshotBytes);
        Assert.Equal(443, DriverControlProtocol.MaximumCombinedDevicePathBytes);
    }

    [Fact]
    public void RejectsControlCharactersAndOversizedEncodedPaths()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].RouteId = "bad\nroute";
        configuration.Routes[0].SourceDevicePath = "/devices/" + new string('\u4F4D', 220);
        configuration.Routes[0].TargetDevicePath = "/devices/target\r";

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("routeId", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("control characters", StringComparison.Ordinal) &&
            error.Contains("targetDevicePath", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("driver control limit", StringComparison.Ordinal));
    }

    private static RuntimeConfiguration CreateConfiguration()
    {
        return new RuntimeConfiguration
        {
            Revision = 1,
            Routes =
            {
                new RouteConfiguration
                {
                    RouteId = "waist",
                    VirtualDeviceSlot = 0,
                    SourceDevicePath = "/devices/lighthouse/LHR-AAAAAAAA",
                    TargetDevicePath = ProtocolConstants.LeftHandRolePath,
                    Offset = PoseOffset.Identity()
                }
            }
        };
    }
}
