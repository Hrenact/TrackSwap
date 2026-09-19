namespace TrackSwap.Protocol
{
    public sealed class RouteConfiguration
    {
        public string RouteId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool PendingDeletion { get; set; }
        public int VirtualDeviceSlot { get; set; }

        /// <summary>Exact registered device path returned by OpenVR.</summary>
        public string SourceDevicePath { get; set; } = string.Empty;

        /// <summary>Exact registered device path or supported role path used by the legacy bootstrap mapping.</summary>
        public string TargetDevicePath { get; set; } = string.Empty;

        public PoseOffset Offset { get; set; } = PoseOffset.Identity();
    }
}
