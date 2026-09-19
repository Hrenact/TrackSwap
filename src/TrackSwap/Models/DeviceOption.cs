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

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
