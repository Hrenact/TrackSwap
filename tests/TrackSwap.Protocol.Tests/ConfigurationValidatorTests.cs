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
        Assert.Equal("TRKSWAP-TRACKER-00", ProtocolConstants.GetTrackerSerial(0));
        Assert.Equal("TRKSWAP-PROXY-00", ProtocolConstants.GetProxySerial(0));
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
    public void SupportsSixteenRoutesButRejectsSeventeen()
    {
        var configuration = CreateConfiguration();
        configuration.Routes.Clear();
        for (int slot = 0; slot < ProtocolConstants.MaximumRoutes; slot++)
        {
            configuration.Routes.Add(new RouteConfiguration
            {
                RouteId = "route-" + slot,
                Name = "Route " + slot,
                Mode = RouteMode.DirectProxy,
                VirtualDeviceSlot = slot,
                SourceDevicePath = "/devices/test/source-" + slot,
                Offset = PoseOffset.Identity()
            });
        }

        Assert.Empty(ConfigurationValidator.Validate(configuration));

        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "route-over-limit",
            Name = "Route over limit",
            Mode = RouteMode.DirectProxy,
            VirtualDeviceSlot = ProtocolConstants.MaximumRoutes,
            SourceDevicePath = "/devices/test/source-over-limit",
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
        Assert.Contains(errors, error => error.Contains("最多支持 16 条路由", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnspecifiedRouteMode()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.Unspecified;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("不受支持的运行模式", StringComparison.Ordinal));
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
            error.Contains("重复使用", StringComparison.Ordinal) &&
            error.Contains(ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal));
    }

    [Fact]
    public void AdvancedOptionAllowsTwoControllerHandsToReuseOnePoseSource()
    {
        var configuration = CreateConfiguration();
        configuration.AllowDuplicatePoseSources = true;
        configuration.Routes.Clear();
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "left-controller",
            Mode = RouteMode.VirtualController,
            VirtualDeviceSlot = 0,
            ControllerHand = ControllerHand.Left,
            ControlInputSource = ControlInputSource.XInput,
            SourceDevicePath = "/devices/test/shared-source",
            Offset = PoseOffset.Identity()
        });
        configuration.Routes.Add(new RouteConfiguration
        {
            RouteId = "right-controller",
            Mode = RouteMode.VirtualController,
            VirtualDeviceSlot = 1,
            ControllerHand = ControllerHand.Right,
            ControlInputSource = ControlInputSource.XInput,
            SourceDevicePath = "/devices/test/shared-source",
            Offset = PoseOffset.Identity()
        });

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Empty(errors);
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
    public void VirtualControllerSupportsXInput()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Mode = RouteMode.VirtualController;
        configuration.Routes[0].TargetDevicePath = string.Empty;
        configuration.Routes[0].ControllerHand = ControllerHand.Right;
        configuration.Routes[0].ControlInputSource = ControlInputSource.XInput;

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

        Assert.Contains(errors, error => error.Contains("虚拟左手控制器", StringComparison.Ordinal));
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

        Assert.Contains(errors, error => error.Contains("槽位 0", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("重复使用", StringComparison.Ordinal) && error.Contains(ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNonFiniteAndZeroLengthRotation()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Offset.RotationW = 0;
        configuration.Routes[0].Offset.TranslationX = double.NaN;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("有限数值", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTranslationBeyondCentimetreSafetyBound()
    {
        var configuration = CreateConfiguration();
        configuration.Routes[0].Offset.TranslationX = 1000.01;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("1000", StringComparison.Ordinal) &&
            error.Contains("厘米", StringComparison.Ordinal));
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

        Assert.Contains(errors, error => error.Contains("路由循环", StringComparison.Ordinal));
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
            error.Contains("跨配置的位姿级联", StringComparison.Ordinal) &&
            error.Contains("Knuckles to right", StringComparison.Ordinal) &&
            error.Contains("Tracker to left", StringComparison.Ordinal));
    }

    [Fact]
    public void DriverControlHeaderContractIsStable()
    {
        Assert.Equal(0x50575354U, DriverControlProtocol.Magic);
        Assert.Equal(20, DriverControlProtocol.HeaderBytes);
        Assert.Equal(6, DriverControlProtocol.Version);
        Assert.Equal(73, DriverControlProtocol.ApplyControllerSnapshotFixedBytes);
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
        Assert.Equal(3233, DriverControlProtocol.TelemetryBatchBytes);
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
        Assert.Contains(errors, error => error.Contains("控制字符", StringComparison.Ordinal) &&
            error.Contains("targetDevicePath", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("驱动通信长度限制", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsXInputThresholdOutsideSupportedRange()
    {
        RuntimeConfiguration configuration = CreateConfiguration();
        configuration.XInput.AnalogPressThreshold = 0.99f;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("XInput", StringComparison.Ordinal) &&
            error.Contains("5% 到 95%", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsStickAsScalarXInputSource()
    {
        RuntimeConfiguration configuration = CreateConfiguration();
        configuration.XInput.Left.Trigger = XInputBindingSource.RightStick;

        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("XInput 左手按键映射", StringComparison.Ordinal));
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
