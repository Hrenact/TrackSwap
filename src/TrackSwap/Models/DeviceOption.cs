namespace TrackSwap.Models
{
    public sealed class DeviceOption
    {
        public DeviceOption(
            string displayName,
            string devicePath,
            bool isOnline = false,
            uint? deviceIndex = null,
            string serialNumber = null,
            string roleTargetPath = null)
        {
            DisplayName = displayName;
            DevicePath = devicePath;
            IsOnline = isOnline;
            DeviceIndex = deviceIndex;
            SerialNumber = serialNumber;
            RoleTargetPath = roleTargetPath;
        }

        public string DisplayName { get; }

        public string DevicePath { get; }

        public bool IsOnline { get; }

        public uint? DeviceIndex { get; }

        public string SerialNumber { get; }

        public string RoleTargetPath { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
