using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using TrackSwap.Models;
using TrackSwap.Protocol;
using TrackSwap.Services;

namespace TrackSwap.Tests;

public sealed class DiagnosticsServiceTests
{
    [Fact]
    public void Export_WritesAnonymousReproductionTopologyAndSanitizedLogs()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrackSwap-diagnostics-test-" + Guid.NewGuid().ToString("N"));
        string dataDirectory = Path.Combine(root, "UserData");
        string destination = Path.Combine(root, "diagnostics.zip");
        string extracted = Path.Combine(root, "extracted");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(dataDirectory, "runtime-ui-launch.log"),
                "TrackSwap UI at C:\\Users\\PrivateUser\\TrackSwap.exe for /devices/pico/SERIAL-SECRET");
            File.WriteAllText(
                Path.Combine(dataDirectory, "ui-lifecycle.log"),
                "Language reload completed. Pack=private-pack.json; Address=192.168.50.77");
            File.WriteAllText(
                Path.Combine(dataDirectory, "runtime-events.log"),
                "Driver failed for serial=SERIAL-SECRET and route My Bedroom");

            RuntimeStatusSnapshot status = CreateStatus();
            IReadOnlyList<DeviceOption> devices = new[]
            {
                new DeviceOption(
                    "PICO 4 · SERIAL-SECRET",
                    "/devices/pico/SERIAL-SECRET",
                    true,
                    0,
                    "SERIAL-SECRET",
                    ProtocolConstants.HeadRolePath,
                    "pico_hmd",
                    TrackedDeviceKind.Hmd,
                    PoseSourceKind.Device,
                    "DONGLE-SECRET",
                    "Pico",
                    "PICO 4",
                    "oculus",
                    null,
                    "REV-A",
                    "FW-9.9"),
                new DeviceOption(
                    "Valve Tracker · LHR-ABC123",
                    "/devices/lighthouse/LHR-ABC123",
                    true,
                    1,
                    "LHR-ABC123",
                    null,
                    "{htc}vr_tracker_vive_3_0",
                    TrackedDeviceKind.Tracker,
                    PoseSourceKind.Device,
                    null,
                    "Valve Corporation",
                    "VIVE Tracker",
                    "lighthouse",
                    null,
                    "REV-B",
                    "FW-2.0")
            };

            var service = new DiagnosticsService(
                new SteamVrPathService(),
                new SteamVrStatusService(),
                new RuntimeControlService(),
                dataDirectory);
            service.Export(
                destination,
                status,
                new DiagnosticsExportContext
                {
                    OnlineDevices = devices,
                    RuntimeLifecycleMode = RuntimeLifecycleMode.FollowSteamVr,
                    FollowSteamVrWithTrackSwap = true
                });

            ZipFile.ExtractToDirectory(destination, extracted);
            string[] entries = Directory.GetFiles(extracted)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Contains("summary.txt", entries);
            Assert.Contains("diagnostics.json", entries);
            Assert.Contains("runtime-events.log", entries);
            Assert.Contains("runtime-ui-launch.log", entries);
            Assert.Contains("ui-lifecycle.log", entries);
            Assert.DoesNotContain("runtime-config.json", entries);
            Assert.DoesNotContain("device-history.json", entries);

            string allText = string.Join(
                "\n",
                Directory.GetFiles(extracted).Select(File.ReadAllText));
            Assert.DoesNotContain("SERIAL-SECRET", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LHR-ABC123", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("My Bedroom", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("route-private", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PrivateUser", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("192.168.50.77", allText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-pack.json", allText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DEVICE_01", allText, StringComparison.Ordinal);
            Assert.Contains("PICO 4", allText, StringComparison.Ordinal);
            Assert.Contains("Valve Corporation", allText, StringComparison.Ordinal);
            Assert.Contains("lighthouse", allText, StringComparison.Ordinal);
            Assert.Contains("FW-9.9", allText, StringComparison.Ordinal);

            JObject document = JObject.Parse(File.ReadAllText(Path.Combine(extracted, "diagnostics.json")));
            Assert.Equal("FollowSteamVr", document["ui"]?["runtimeLifecycleMode"]?.Value<string>());
            Assert.True(document["ui"]?["followSteamVrWithTrackSwap"]?.Value<bool>());
            Assert.Equal("DEVICE_01", document["configuration"]?["routes"]?[0]?["source"]?.Value<string>());
            Assert.Equal("DEVICE_01", document["configuration"]?["routes"]?[1]?["source"]?.Value<string>());
            Assert.Equal("DEVICE_02", document["configuration"]?["routes"]?[0]?["rotationSource"]?.Value<string>());
            Assert.Equal("oculus", document["devices"]?[0]?["trackingSystem"]?.Value<string>());
            Assert.Equal("REV-A", document["devices"]?[0]?["hardwareRevision"]?.Value<string>());
            Assert.True(document["configuration"]?["routes"]?[0]?["hasNonIdentityOffset"]?.Value<bool>());
            Assert.Equal("PrivateNetwork", document["runtime"]?["osc"]?["networkScope"]?.Value<string>());
            Assert.True(document["runtime"]?["xinput"]?["connected"]?.Value<bool>());
            Assert.True(document["runtime"]?["physicalSourceHiding"]?["hookInstalled"]?.Value<bool>());
            Assert.False(document["configuration"]?["valid"]?.Value<bool>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Anonymizer_UsesStableAliasesAcrossPathsSerialsAndErrors()
    {
        RuntimeConfiguration configuration = CreateStatus().Configuration;
        var device = new DeviceOption(
            "Device",
            "/devices/pico/SERIAL-SECRET",
            true,
            3,
            "SERIAL-SECRET");
        var anonymizer = new DiagnosticAnonymizer(configuration, new[] { device });
        string alias = anonymizer.AliasDevicePath(device.DevicePath);

        string sanitized = anonymizer.Sanitize(
            "/devices/pico/SERIAL-SECRET serial=SERIAL-SECRET 10.20.30.40 “My Bedroom”");

        Assert.DoesNotContain("SERIAL-SECRET", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.20.30.40", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("My Bedroom", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(sanitized, alias));
        Assert.Contains("<NETWORK_ADDRESS>", sanitized, StringComparison.Ordinal);
        Assert.Contains("ROUTE_01", sanitized, StringComparison.Ordinal);
    }

    private static RuntimeStatusSnapshot CreateStatus()
    {
        var configuration = new RuntimeConfiguration
        {
            Revision = 42,
            Osc = new OscConfiguration
            {
                ListenAddress = "192.168.50.77",
                Port = 9015,
                SendPort = 9016
            },
            XInput = XInputConfiguration.CreateDefault(),
            Routes = new List<RouteConfiguration>
            {
                new RouteConfiguration
                {
                    RouteId = "route-private",
                    Name = "My Bedroom",
                    Enabled = true,
                    Mode = RouteMode.ReplaceTarget,
                    VirtualDeviceSlot = 0,
                    SourceDevicePath = "/devices/pico/SERIAL-SECRET",
                    SplitPoseSource = true,
                    RotationSourceDevicePath = "/devices/lighthouse/LHR-ABC123",
                    TargetDevicePath = "/devices/oculus/QUEST-SECRET",
                    HidePhysicalSource = true,
                    Offset = new PoseOffset
                    {
                        TranslationX = 12.345,
                        RotationW = 1
                    },
                    MotionSmoothing = new MotionSmoothingConfiguration
                    {
                        Enabled = true,
                        SmoothPosition = true,
                        PositionStrength = 35
                    }
                },
                new RouteConfiguration
                {
                    RouteId = "route-controller",
                    Name = "My Bedroom",
                    Enabled = true,
                    Mode = RouteMode.VirtualController,
                    VirtualDeviceSlot = 1,
                    ControllerHand = ControllerHand.Left,
                    ControlInputSource = ControlInputSource.XInput,
                    SourceDevicePath = "/devices/pico/SERIAL-SECRET"
                }
            }
        };
        return new RuntimeStatusSnapshot
        {
            Configuration = configuration,
            ConfigurationRevision = 42,
            DriverAppliedRevision = 41,
            DriverConnected = true,
            StaticMappingPending = true,
            Osc = new OscRuntimeStatus
            {
                Enabled = true,
                Listening = false,
                ReceivePortInUse = true,
                LastError = "Cannot bind 192.168.50.77"
            },
            XInput = new XInputRuntimeStatus
            {
                Enabled = true,
                Connected = true
            },
            PhysicalSourceHiding = new PhysicalSourceHidingStatus
            {
                State = PhysicalSourceHidingState.Active,
                RequestedDeviceCount = 2,
                ActiveDeviceCount = 2,
                HookInstalled = true
            }
        };
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
