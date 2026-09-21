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
    public void DirectProxyRouteDoesNotRequireTarget()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.DirectProxy;
        configuration.Routes[0].TargetDevicePath = string.Empty;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void DirectProxyRouteDoesNotReserveStaleTarget()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.DirectProxy;
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "replacement",
            Mode = RouteMode.ReplaceTarget,
            VirtualDeviceSlot = 1,
            SourceDevicePath = "/devices/lighthouse/LHR-BBBBBBBB",
            TargetDevicePath = ProtocolConstants.LeftHandRolePath,
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.DoesNotContain(errors, error =>
            error.Contains("assigned more than once", StringComparison.Ordinal) &&
            error.Contains(ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal));
    }

    [Fact]
    public void VirtualControllerSupportsOscInput()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.VirtualController;
        configuration.Routes[0].TargetDevicePath = string.Empty;
        configuration.Routes[0].ControllerHand = ControllerHand.Left;
        configuration.Routes[0].ControlInputSource = ControlInputSource.Osc;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
        Assert.Equal("TRKSWAP-CONTROLLER-L", ProtocolConstants.GetControllerSerial(ControllerHand.Left));
    }

    [Fact]
    public void VirtualControllerAllowsNoControlInput()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.VirtualController;
        configuration.Routes[0].TargetDevicePath = string.Empty;
        configuration.Routes[0].ControllerHand = ControllerHand.Left;
        configuration.Routes[0].ControlInputSource = ControlInputSource.None;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsSecondVirtualControllerForSameHand()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.VirtualController;
        configuration.Routes[0].TargetDevicePath = string.Empty;
        configuration.Routes[0].ControllerHand = ControllerHand.Left;
        configuration.Routes[0].ControlInputSource = ControlInputSource.Osc;
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "second-left",
            Mode = RouteMode.VirtualController,
            VirtualDeviceSlot = 1,
            ControllerHand = ControllerHand.Left,
            ControlInputSource = ControlInputSource.Osc,
            SourceDevicePath = "/devices/lighthouse/LHR-BBBBBBBB",
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("Only one virtual left controller", StringComparison.Ordinal));
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
    public void RejectsSourceWhoseRoleIsOverriddenByAnotherRoute()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Name = "Knuckles to right";
        configuration.Routes[0].SourceDevicePath = "/devices/valve/index-left";
        configuration.Routes[0].TargetDevicePath = ProtocolConstants.RightHandRolePath;
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "tracker-to-left",
            Name = "Tracker to left",
            VirtualDeviceSlot = 1,
            SourceDevicePath = "/devices/lighthouse/tracker",
            TargetDevicePath = ProtocolConstants.LeftHandRolePath,
            Offset = PoseOffset.Identity()
        });
        var sourceRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/devices/valve/index-left"] = ProtocolConstants.LeftHandRolePath
        };

        IReadOnlyList<string> errors =
            ConfigurationValidator.ValidateSourceRoleDependencies(configuration, sourceRoles);

        Assert.Contains(errors, error =>
            error.Contains("cross-route pose cascade", StringComparison.Ordinal) &&
            error.Contains("Knuckles to right", StringComparison.Ordinal) &&
            error.Contains("Tracker to left", StringComparison.Ordinal));
    }

    [Fact]
    public void DriverControlHeaderContractIsStable()
    {
        Assert.Equal(0x50575354U, DriverControlProtocol.Magic);
        Assert.Equal(20, DriverControlProtocol.HeaderBytes);
        Assert.Equal(3, DriverControlProtocol.Version);
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
        Assert.Equal(4026, DriverControlProtocol.MaximumCombinedDevicePathBytes);
        Assert.Equal(1617, DriverControlProtocol.TelemetryBatchBytes);
    }

    [Fact]
    public void RejectsControlCharactersAndOversizedEncodedPaths()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].RouteId = "bad\nroute";
        configuration.Routes[0].SourceDevicePath = "/devices/" + new string('\u4F4D', 1400);
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
