using System;

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
            TrackedDeviceKind deviceKind = TrackedDeviceKind.Unknown)
        {
            DisplayName = displayName;
            DevicePath = devicePath;
            IsOnline = isOnline;
            DeviceIndex = deviceIndex;
            SerialNumber = serialNumber;
            RoleTargetPath = roleTargetPath;
            RenderModelName = renderModelName;
            DeviceKind = deviceKind;
        }

        public string DisplayName { get; }

        public string DevicePath { get; }

        public bool IsOnline { get; }

        public uint? DeviceIndex { get; }

        public string SerialNumber { get; }

        public string RoleTargetPath { get; }

        public string RenderModelName { get; }

        public TrackedDeviceKind DeviceKind { get; }

        public static string BaseDisplayName(string displayName)
        {
            string value = displayName ?? string.Empty;
            string[] prefixes = { "当前 · ", "在线 · ", "离线 · ", "历史 · ", "在线设备 · ", "已保存设备 · " };
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    value = value.Substring(prefix.Length);
                    break;
                }
            }
            const string oldOnlineSuffix = " · 在线";
            if (value.EndsWith(oldOnlineSuffix, StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - oldOnlineSuffix.Length);
            }
            return value;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
