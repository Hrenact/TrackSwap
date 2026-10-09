using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TrackSwap.Models;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class DiagnosticsExportContext
    {
        public IReadOnlyList<DeviceOption> OnlineDevices { get; set; } = Array.Empty<DeviceOption>();
        public RuntimeLifecycleMode RuntimeLifecycleMode { get; set; } = RuntimeLifecycleMode.FollowTrackSwap;
        public bool FollowSteamVrWithTrackSwap { get; set; }
    }

    internal sealed class DiagnosticAnonymizer
    {
        private readonly Dictionary<string, string> deviceAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> sensitiveReplacements =
            new List<KeyValuePair<string, string>>();

        public DiagnosticAnonymizer(
            RuntimeConfiguration configuration,
            IEnumerable<DeviceOption> devices)
        {
            foreach (RouteConfiguration route in configuration?.Routes ?? new List<RouteConfiguration>())
            {
                RegisterDevice(route?.SourceDevicePath);
                RegisterDevice(route?.RotationSourceDevicePath);
                RegisterDevice(route?.TargetDevicePath);
            }

            foreach (DeviceOption device in (devices ?? Array.Empty<DeviceOption>())
                .Where(device => device != null)
                .OrderBy(device => device.DeviceIndex ?? uint.MaxValue))
            {
                string alias = RegisterDevice(device.DevicePath);
                RegisterSensitive(device.SerialNumber, alias);
                RegisterSensitive(device.ConnectedWirelessDongleId, alias + "_RECEIVER");
            }

            int routeNumber = 1;
            foreach (RouteConfiguration route in configuration?.Routes ?? new List<RouteConfiguration>())
            {
                if (route == null)
                {
                    continue;
                }
                string alias = "ROUTE_" + routeNumber.ToString("00");
                routeNumber++;
                RegisterIdentifier(route.RouteId, alias);
                RegisterIdentifier(route.Name, alias);
            }

            RegisterSensitive(configuration?.Osc?.ListenAddress, "<OSC_ADDRESS>");
        }

        public string AliasDevicePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            if (string.Equals(path, ProtocolConstants.HeadRolePath, StringComparison.Ordinal))
            {
                return "ROLE_HEAD";
            }
            if (string.Equals(path, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal))
            {
                return "ROLE_LEFT_HAND";
            }
            if (string.Equals(path, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal))
            {
                return "ROLE_RIGHT_HAND";
            }
            if (ProtocolConstants.IsTrackSwapVirtualDevicePath(path))
            {
                string identity = path.Split('/').LastOrDefault();
                return string.IsNullOrWhiteSpace(identity)
                    ? "TRACKSWAP_DEVICE"
                    : "TRACKSWAP:" + identity;
            }
            return path.StartsWith("/devices/", StringComparison.OrdinalIgnoreCase)
                ? RegisterDevice(path)
                : "INVALID_DEVICE_REFERENCE";
        }

        public string Sanitize(
            string text,
            IReadOnlyList<KeyValuePair<string, string>> pathReplacements = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string result = text;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            result = ReplaceIgnoreCase(result, localAppData, "%LOCALAPPDATA%");
            result = ReplaceIgnoreCase(result, userProfile, "%USERPROFILE%");

            foreach (KeyValuePair<string, string> replacement in
                pathReplacements ?? Array.Empty<KeyValuePair<string, string>>())
            {
                result = ReplaceIgnoreCase(result, replacement.Key, replacement.Value);
            }
            foreach (KeyValuePair<string, string> replacement in sensitiveReplacements
                .OrderByDescending(replacement => replacement.Key.Length))
            {
                result = ReplaceIgnoreCase(result, replacement.Key, replacement.Value);
            }

            result = Regex.Replace(
                result,
                "(?i)/devices/[^\\s\\\"']+",
                "<DEVICE_UNKNOWN>");
            result = Regex.Replace(result, "(?i)\\bLHR-[0-9A-F]+\\b", "<DEVICE_UNKNOWN>");
            result = Regex.Replace(
                result,
                "(?i)\\b(serial(?:_number| number)?)[ \\t]*[:=][ \\t]*[\\\"']?(?!DEVICE_[0-9]{2}\\b)[^,;\\s\\\"']+",
                "$1=<DEVICE_UNKNOWN>");
            result = Regex.Replace(
                result,
                "(?<![0-9])(?:[0-9]{1,3}\\.){3}[0-9]{1,3}(?![0-9])",
                "<NETWORK_ADDRESS>");
            result = Regex.Replace(
                result,
                "(?i)(\\bPack=)[^;\\s]+",
                "$1<LANGUAGE_PACK>");
            result = Regex.Replace(
                result,
                "(?i)file:///[A-Z]:/[^\\s\\\"']+",
                match => "file:///<LOCAL_PATH>/" + GetPortableFileName(match.Value));
            result = Regex.Replace(
                result,
                "(?i)\\b[A-Z]:\\\\[^\\s\\\"']+",
                match => "<LOCAL_PATH>/" + GetPortableFileName(match.Value));
            return result;
        }

        private string RegisterDevice(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            if (!path.StartsWith("/devices/", StringComparison.OrdinalIgnoreCase))
            {
                return AliasDevicePath(path);
            }
            if (ProtocolConstants.IsTrackSwapVirtualDevicePath(path))
            {
                string identity = path.Split('/').LastOrDefault();
                return string.IsNullOrWhiteSpace(identity)
                    ? "TRACKSWAP_DEVICE"
                    : "TRACKSWAP:" + identity;
            }
            if (!deviceAliases.TryGetValue(path, out string alias))
            {
                alias = "DEVICE_" + (deviceAliases.Count + 1).ToString("00");
                deviceAliases[path] = alias;
                RegisterSensitive(path, alias);
                RegisterSensitive(path.Split('/').LastOrDefault(), alias);
            }
            return alias;
        }

        private void RegisterIdentifier(string value, string alias)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            RegisterSensitive("“" + value + "”", "“" + alias + "”");
            RegisterSensitive("\"" + value + "\"", "\"" + alias + "\"");
            if (value.Length >= 4)
            {
                RegisterSensitive(value, alias);
            }
        }

        private void RegisterSensitive(string value, string replacement)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(replacement))
            {
                return;
            }
            sensitiveReplacements.Add(new KeyValuePair<string, string>(value, replacement));
        }

        private static string GetPortableFileName(string path)
        {
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int separator = normalized.LastIndexOf('/');
            return separator >= 0 && separator + 1 < normalized.Length
                ? normalized.Substring(separator + 1)
                : "file";
        }

        private static string ReplaceIgnoreCase(string value, string oldValue, string newValue)
        {
            if (string.IsNullOrWhiteSpace(oldValue))
            {
                return value;
            }
            return Regex.Replace(value, Regex.Escape(oldValue), _ => newValue, RegexOptions.IgnoreCase);
        }
    }
}
