using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrackSwap.Models;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal static class DiagnosticsDocumentBuilder
    {
        public const int DocumentSchemaVersion = 1;

        public static JObject Build(
            DiagnosticsReport report,
            RuntimeStatusSnapshot status,
            DiagnosticsExportContext context,
            DiagnosticAnonymizer anonymizer)
        {
            context = context ?? new DiagnosticsExportContext();
            RuntimeConfiguration configuration = status?.Configuration;
            var root = new JObject
            {
                ["schemaVersion"] = DocumentSchemaVersion,
                ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["application"] = new JObject
                {
                    ["uiVersion"] = report.UiVersion,
                    ["runtimePresent"] = report.RuntimeProgramFound,
                    ["runtimeVersion"] = Value(report.RuntimeVersion),
                    ["driverRegistered"] = report.DriverRegistered,
                    ["driverVersion"] = Value(report.DriverVersion),
                    ["protocolVersion"] = ProtocolConstants.CurrentProtocolVersion,
                    ["configurationSchemaVersion"] = ProtocolConstants.CurrentConfigurationSchemaVersion,
                    ["operatingSystem"] = report.OperatingSystem,
                    ["processArchitecture"] = Environment.Is64BitProcess ? "x64" : "x86",
                    ["dataStorageMode"] = GetDataStorageMode(),
                    ["steamVrApplicationManifestPresent"] = report.ApplicationManifestPresent
                },
                ["steamVr"] = new JObject
                {
                    ["installed"] = report.SteamVrInstalled,
                    ["running"] = report.SteamVrRunning,
                    ["version"] = Value(report.SteamVrVersion)
                },
                ["ui"] = new JObject
                {
                    ["runtimeLifecycleMode"] = context.RuntimeLifecycleMode.ToString(),
                    ["followSteamVrWithTrackSwap"] = context.FollowSteamVrWithTrackSwap
                },
                ["runtime"] = BuildRuntime(status, configuration),
                ["configuration"] = BuildConfiguration(report, configuration, anonymizer),
                ["devices"] = BuildDevices(context.OnlineDevices, anonymizer)
            };
            return root;
        }

        public static string BuildSummary(JObject document)
        {
            JObject application = (JObject)document["application"];
            JObject steamVr = (JObject)document["steamVr"];
            JObject runtime = (JObject)document["runtime"];
            JObject configuration = (JObject)document["configuration"];
            JObject osc = (JObject)runtime["osc"];
            JObject xinput = (JObject)runtime["xinput"];
            JObject hiding = (JObject)runtime["physicalSourceHiding"];

            var builder = new StringBuilder();
            builder.AppendLine("TrackSwap VR diagnostic summary");
            builder.AppendLine("Generated at (UTC): " + document["generatedAtUtc"]);
            builder.AppendLine("UI version: " + application["uiVersion"]);
            builder.AppendLine("Runtime: " + Present(application["runtimePresent"]) +
                " · version " + Display(application["runtimeVersion"]));
            builder.AppendLine("Driver: " + Present(application["driverRegistered"]) +
                " · version " + Display(application["driverVersion"]));
            builder.AppendLine("SteamVR: " +
                (steamVr.Value<bool>("running") ? "running" : steamVr.Value<bool>("installed") ? "installed, not running" : "not found") +
                " · version " + Display(steamVr["version"]));
            builder.AppendLine("OS: " + application["operatingSystem"]);
            builder.AppendLine("Data storage: " + application["dataStorageMode"]);
            builder.AppendLine("Runtime IPC: " + (runtime.Value<bool>("connected") ? "online" : "disconnected"));
            builder.AppendLine("Driver IPC: " + (runtime.Value<bool>("driverConnected") ? "connected" : "disconnected"));
            builder.AppendLine("Configuration revision: " + Display(runtime["configurationRevision"]));
            builder.AppendLine("Driver-applied revision: " + Display(runtime["driverAppliedRevision"]));
            builder.AppendLine("Configuration validation: " +
                (configuration.Value<bool>("available")
                    ? configuration.Value<bool>("valid") ? "passed" : "failed"
                    : "unavailable"));
            builder.AppendLine("Routes: " + configuration.Value<int>("routeCount"));
            builder.AppendLine("OSC: enabled=" + osc["enabled"] +
                ", listening=" + osc["listening"] +
                ", receivePortInUse=" + osc["receivePortInUse"]);
            builder.AppendLine("XInput: enabled=" + xinput["enabled"] +
                ", connected=" + xinput["connected"]);
            builder.AppendLine("Physical-source hiding: state=" + hiding["state"] +
                ", hookInstalled=" + hiding["hookInstalled"] +
                ", active=" + hiding["activeDeviceCount"] + "/" + hiding["requestedDeviceCount"]);
            builder.AppendLine();
            builder.AppendLine("See diagnostics.json for the anonymized topology and detailed status.");
            return builder.ToString();
        }

        private static JObject BuildRuntime(
            RuntimeStatusSnapshot status,
            RuntimeConfiguration configuration)
        {
            OscRuntimeStatus osc = status?.Osc ?? new OscRuntimeStatus();
            XInputRuntimeStatus xinput = status?.XInput ?? new XInputRuntimeStatus();
            PhysicalSourceHidingStatus hiding = status?.PhysicalSourceHiding ?? new PhysicalSourceHidingStatus();
            return new JObject
            {
                ["connected"] = status != null,
                ["configurationRevision"] = status == null ? JValue.CreateNull() : new JValue(status.ConfigurationRevision),
                ["driverAppliedRevision"] = status == null ? JValue.CreateNull() : new JValue(status.DriverAppliedRevision),
                ["driverConnected"] = status?.DriverConnected == true,
                ["lastError"] = Value(status?.LastError),
                ["staticMappingPending"] = status?.StaticMappingPending == true,
                ["staticMappingLastError"] = Value(status?.StaticMappingLastError),
                ["osc"] = new JObject
                {
                    ["enabled"] = osc.Enabled,
                    ["listening"] = osc.Listening,
                    ["networkScope"] = GetNetworkScope(configuration?.Osc?.ListenAddress),
                    ["receivePort"] = configuration?.Osc?.Port ?? 0,
                    ["sendPort"] = configuration?.Osc?.SendPort ?? 0,
                    ["receivePortInUse"] = osc.ReceivePortInUse,
                    ["lastMessageAtUtc"] = osc.LastMessageAtUtc.HasValue
                        ? new JValue(osc.LastMessageAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                        : JValue.CreateNull(),
                    ["lastReceiveError"] = Value(osc.LastError),
                    ["lastSendError"] = Value(osc.LastSendError)
                },
                ["xinput"] = new JObject
                {
                    ["enabled"] = xinput.Enabled,
                    ["connected"] = xinput.Connected
                },
                ["physicalSourceHiding"] = new JObject
                {
                    ["state"] = hiding.State.ToString(),
                    ["requestedDeviceCount"] = hiding.RequestedDeviceCount,
                    ["activeDeviceCount"] = hiding.ActiveDeviceCount,
                    ["hookInstalled"] = hiding.HookInstalled,
                    ["lastError"] = Value(hiding.LastError)
                }
            };
        }

        private static JObject BuildConfiguration(
            DiagnosticsReport report,
            RuntimeConfiguration configuration,
            DiagnosticAnonymizer anonymizer)
        {
            var routes = new JArray();
            int index = 0;
            foreach (RouteConfiguration route in configuration?.Routes ?? new List<RouteConfiguration>())
            {
                if (route == null)
                {
                    continue;
                }
                routes.Add(BuildRoute(route, ++index, anonymizer));
            }

            var errors = new JArray(report.ConfigurationErrors
                .Select(error => anonymizer.Sanitize(error)));
            return new JObject
            {
                ["available"] = configuration != null,
                ["schemaVersion"] = configuration == null ? JValue.CreateNull() : new JValue(configuration.SchemaVersion),
                ["valid"] = configuration != null && report.ConfigurationErrors.Count == 0,
                ["validationErrors"] = errors,
                ["routeCount"] = routes.Count,
                ["routes"] = routes,
                ["oscSettings"] = BuildOscSettings(configuration?.Osc),
                ["xinputSettings"] = BuildXInputSettings(configuration?.XInput)
            };
        }

        private static JObject BuildRoute(
            RouteConfiguration route,
            int routeNumber,
            DiagnosticAnonymizer anonymizer)
        {
            MotionSmoothingConfiguration smoothing = route.MotionSmoothing ?? new MotionSmoothingConfiguration();
            return new JObject
            {
                ["id"] = "ROUTE_" + routeNumber.ToString("00"),
                ["enabled"] = route.Enabled,
                ["pendingDeletion"] = route.PendingDeletion,
                ["mode"] = route.Mode.ToString(),
                ["virtualDeviceSlot"] = route.VirtualDeviceSlot,
                ["controllerHand"] = route.Mode == RouteMode.VirtualController
                    ? new JValue(route.ControllerHand.ToString())
                    : JValue.CreateNull(),
                ["controlInputSource"] = route.Mode == RouteMode.VirtualController
                    ? new JValue(route.ControlInputSource.ToString())
                    : JValue.CreateNull(),
                ["poseSourceKind"] = route.PoseSourceKind.ToString(),
                ["source"] = Value(anonymizer.AliasDevicePath(route.SourceDevicePath)),
                ["rotationSource"] = route.SplitPoseSource
                    ? Value(anonymizer.AliasDevicePath(route.RotationSourceDevicePath))
                    : JValue.CreateNull(),
                ["target"] = route.Mode == RouteMode.ReplaceTarget
                    ? Value(anonymizer.AliasDevicePath(route.TargetDevicePath))
                    : JValue.CreateNull(),
                ["splitPoseSource"] = route.SplitPoseSource,
                ["hidePhysicalSource"] = route.HidePhysicalSource,
                ["hasNonIdentityOffset"] = !IsIdentity(route.Offset),
                ["manualPoseCustomized"] = route.PoseSourceKind == PoseSourceKind.Manual &&
                    !IsDefaultManualPose(route.ManualPose),
                ["motionSmoothing"] = new JObject
                {
                    ["enabled"] = smoothing.Enabled,
                    ["smoothPosition"] = smoothing.SmoothPosition,
                    ["positionStrength"] = smoothing.PositionStrength,
                    ["smoothRotation"] = smoothing.SmoothRotation,
                    ["rotationStrength"] = smoothing.RotationStrength
                }
            };
        }

        private static JArray BuildDevices(
            IEnumerable<DeviceOption> devices,
            DiagnosticAnonymizer anonymizer)
        {
            var result = new JArray();
            foreach (DeviceOption device in (devices ?? Array.Empty<DeviceOption>())
                .Where(device => device != null && device.IsOnline)
                .OrderBy(device => device.DeviceIndex ?? uint.MaxValue))
            {
                result.Add(new JObject
                {
                    ["id"] = Value(anonymizer.AliasDevicePath(device.DevicePath)),
                    ["kind"] = device.DeviceKind.ToString(),
                    ["manufacturer"] = Value(anonymizer.Sanitize(device.ManufacturerName)),
                    ["model"] = Value(anonymizer.Sanitize(device.ModelNumber)),
                    ["renderModel"] = Value(anonymizer.Sanitize(device.RenderModelName)),
                    ["trackingSystem"] = Value(anonymizer.Sanitize(device.TrackingSystemName)),
                    ["controllerType"] = Value(anonymizer.Sanitize(device.ControllerType)),
                    ["hardwareRevision"] = Value(anonymizer.Sanitize(device.HardwareRevision)),
                    ["trackingFirmwareVersion"] = Value(anonymizer.Sanitize(device.TrackingFirmwareVersion)),
                    ["role"] = Value(anonymizer.AliasDevicePath(device.RoleTargetPath))
                });
            }
            return result;
        }

        private static JObject BuildOscSettings(OscConfiguration configuration)
        {
            configuration = configuration ?? OscConfiguration.CreateDefault();
            return new JObject
            {
                ["networkScope"] = GetNetworkScope(configuration.ListenAddress),
                ["receivePort"] = configuration.Port,
                ["sendPort"] = configuration.SendPort,
                ["leftTouchAssist"] = new JObject
                {
                    ["thumbDefaultTouched"] = configuration.LeftTouchAssist?.ThumbDefaultTouched,
                    ["indexDefaultTouched"] = configuration.LeftTouchAssist?.IndexDefaultTouched
                },
                ["rightTouchAssist"] = new JObject
                {
                    ["thumbDefaultTouched"] = configuration.RightTouchAssist?.ThumbDefaultTouched,
                    ["indexDefaultTouched"] = configuration.RightTouchAssist?.IndexDefaultTouched
                }
            };
        }

        private static JObject BuildXInputSettings(XInputConfiguration configuration)
        {
            configuration = configuration ?? XInputConfiguration.CreateDefault();
            return new JObject
            {
                ["analogPressThreshold"] = configuration.AnalogPressThreshold,
                ["hapticMode"] = configuration.HapticMode.ToString(),
                ["left"] = BuildXInputMapping(configuration.Left),
                ["right"] = BuildXInputMapping(configuration.Right)
            };
        }

        private static JObject BuildXInputMapping(XInputHandMapping mapping)
        {
            mapping = mapping ?? new XInputHandMapping();
            return new JObject
            {
                ["primaryButton"] = mapping.PrimaryButton.ToString(),
                ["secondaryButton"] = mapping.SecondaryButton.ToString(),
                ["joystick"] = mapping.Joystick.ToString(),
                ["joystickClick"] = mapping.JoystickClick.ToString(),
                ["trigger"] = mapping.Trigger.ToString(),
                ["grip"] = mapping.Grip.ToString(),
                ["menuButton"] = mapping.MenuButton.ToString(),
                ["thumbTouch"] = BuildTouchAssist(mapping.ThumbTouch),
                ["indexTouch"] = BuildTouchAssist(mapping.IndexTouch)
            };
        }

        private static JObject BuildTouchAssist(XInputTouchAssistMapping mapping)
        {
            mapping = mapping ?? new XInputTouchAssistMapping();
            return new JObject
            {
                ["toggleSource"] = mapping.ToggleSource.ToString(),
                ["defaultTouched"] = mapping.DefaultTouched
            };
        }

        private static string GetNetworkScope(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return "Unspecified";
            }
            if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return "Loopback";
            }
            if (!IPAddress.TryParse(address, out IPAddress parsed))
            {
                return "Hostname";
            }
            if (IPAddress.IsLoopback(parsed))
            {
                return "Loopback";
            }
            byte[] bytes = parsed.GetAddressBytes();
            bool privateIpv4 = bytes.Length == 4 &&
                (bytes[0] == 10 ||
                 bytes[0] == 127 ||
                 bytes[0] == 192 && bytes[1] == 168 ||
                 bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                 bytes[0] == 169 && bytes[1] == 254);
            bool privateIpv6 = bytes.Length == 16 &&
                ((bytes[0] & 0xFE) == 0xFC || parsed.IsIPv6LinkLocal);
            return privateIpv4 || privateIpv6 ? "PrivateNetwork" : "PublicNetwork";
        }

        private static string GetDataStorageMode()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRACKSWAP_DATA_DIRECTORY")))
            {
                return "EnvironmentOverride";
            }
            return TrackSwapDataPaths.IsUsingLegacyData ? "LegacyLocalAppData" : "PackageUserData";
        }

        private static bool IsIdentity(PoseOffset offset)
        {
            return offset != null &&
                offset.TranslationX == 0 && offset.TranslationY == 0 && offset.TranslationZ == 0 &&
                offset.RotationX == 0 && offset.RotationY == 0 && offset.RotationZ == 0 &&
                offset.RotationW == 1;
        }

        private static bool IsDefaultManualPose(PoseOffset offset)
        {
            PoseOffset expected = PoseOffset.DefaultManualPose();
            if (offset == null)
            {
                return false;
            }
            return offset.TranslationX == expected.TranslationX &&
                offset.TranslationY == expected.TranslationY &&
                offset.TranslationZ == expected.TranslationZ &&
                offset.RotationX == expected.RotationX &&
                offset.RotationY == expected.RotationY &&
                offset.RotationZ == expected.RotationZ &&
                offset.RotationW == expected.RotationW;
        }

        private static JToken Value(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? JValue.CreateNull() : new JValue(value);
        }

        private static string Display(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? "unknown" : token.ToString();
        }

        private static string Present(JToken token)
        {
            return token?.Value<bool>() == true ? "present" : "not found";
        }
    }
}
