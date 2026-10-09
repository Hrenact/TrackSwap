using System;
using TrackSwap.Localization;
using TrackSwap.Protocol;

namespace TrackSwap.Models
{
    public enum TrackedDeviceKind
    {
        Unknown,
        Hmd,
        Controller,
        Tracker
    }

    public sealed class DeviceOption
    {
        public DeviceOption(
            string displayName,
            string devicePath,
            bool isOnline = false,
            uint? deviceIndex = null,
            string serialNumber = null,
            string roleTargetPath = null,
            string renderModelName = null,
            TrackedDeviceKind deviceKind = TrackedDeviceKind.Unknown,
            PoseSourceKind poseSourceKind = PoseSourceKind.Device,
            string connectedWirelessDongleId = null,
            string manufacturerName = null,
            string modelNumber = null,
            string trackingSystemName = null,
            string controllerType = null,
            string hardwareRevision = null,
            string trackingFirmwareVersion = null)
        {
            DisplayName = displayName;
            DevicePath = devicePath;
            IsOnline = isOnline;
            DeviceIndex = deviceIndex;
            SerialNumber = serialNumber;
            RoleTargetPath = roleTargetPath;
            RenderModelName = renderModelName;
            DeviceKind = deviceKind;
            PoseSourceKind = poseSourceKind;
            ConnectedWirelessDongleId = connectedWirelessDongleId;
            ManufacturerName = manufacturerName;
            ModelNumber = modelNumber;
            TrackingSystemName = trackingSystemName;
            ControllerType = controllerType;
            HardwareRevision = hardwareRevision;
            TrackingFirmwareVersion = trackingFirmwareVersion;
        }

        public string DisplayName { get; }

        public string StatusDisplayName => BaseDisplayName(DisplayName);

        public string DevicePath { get; }

        public bool IsOnline { get; }

        public uint? DeviceIndex { get; }

        public string SerialNumber { get; }

        public string RoleTargetPath { get; }

        public string RenderModelName { get; }

        public TrackedDeviceKind DeviceKind { get; }

        public PoseSourceKind PoseSourceKind { get; }

        public string ConnectedWirelessDongleId { get; }

        public string ManufacturerName { get; }

        public string ModelNumber { get; }

        public string TrackingSystemName { get; }

        public string ControllerType { get; }

        public string HardwareRevision { get; }

        public string TrackingFirmwareVersion { get; }

        public static string BaseDisplayName(string displayName)
        {
            string value = displayName ?? string.Empty;
            foreach (string templateKey in new[]
            {
                "device.status.online",
                "device.status.offline",
                "device.status.saved"
            })
            {
                if (TryStripFormattedValue(value, Tr.Get(templateKey), out string stripped))
                {
                    value = stripped;
                    break;
                }
            }
            foreach (string prefix in new[]
            {
                "当前 · ", "在线 · ", "离线 · ", "历史 · ", "在线设备 · ", "已保存设备 · "
            })
            {
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                value = value.Substring(prefix.Length);
                break;
            }
            const string oldOnlineSuffix = " · 在线";
            if (value.EndsWith(oldOnlineSuffix, StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - oldOnlineSuffix.Length);
            }
            return value;
        }

        private static bool TryStripFormattedValue(string value, string template, out string stripped)
        {
            const string placeholder = "{0}";
            int placeholderIndex = template?.IndexOf(placeholder, StringComparison.Ordinal) ?? -1;
            if (placeholderIndex < 0)
            {
                stripped = value;
                return false;
            }

            string prefix = template.Substring(0, placeholderIndex);
            string suffix = template.Substring(placeholderIndex + placeholder.Length);
            if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
                !value.EndsWith(suffix, StringComparison.Ordinal) ||
                value.Length < prefix.Length + suffix.Length)
            {
                stripped = value;
                return false;
            }

            stripped = value.Substring(prefix.Length, value.Length - prefix.Length - suffix.Length);
            return true;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
