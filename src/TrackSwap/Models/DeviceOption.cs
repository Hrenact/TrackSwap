namespace TrackSwap.Models
{
    public sealed class DeviceOption
    {
        public DeviceOption(
            string displayName,
            string devicePath,
            bool isOnline = false,
            uint? deviceIndex = null,
            string serialNumber = null)
        {
            DisplayName = displayName;
            DevicePath = devicePath;
            IsOnline = isOnline;
            DeviceIndex = deviceIndex;
            SerialNumber = serialNumber;
        }

        public string DisplayName { get; }

        public string DevicePath { get; }

        public bool IsOnline { get; }

        public uint? DeviceIndex { get; }

        public string SerialNumber { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
