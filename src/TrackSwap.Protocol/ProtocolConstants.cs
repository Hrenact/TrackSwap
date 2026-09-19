namespace TrackSwap.Protocol
{
    public static class ProtocolConstants
    {
        public const int CurrentProtocolVersion = 2;
        public const int CurrentConfigurationSchemaVersion = 1;
        public const int MaximumMessageBytes = 64 * 1024;
        public const int MaximumRoutes = 8;
        public const int MaximumRouteIdCharacters = 128;
        public const int MinimumCalibrationSamples = 60;
        public const int MaximumCalibrationSamples = 180;
        public const string PipeName = "TrackSwap.Runtime.v1";
        public const string DriverPipeName = "TrackSwap.Driver.v1";
        public const string VirtualSerialPrefix = "TRKSWAP-PROXY-";
        public const string HeadRolePath = "/user/head";
        public const string LeftHandRolePath = "/user/hand/left";
        public const string RightHandRolePath = "/user/hand/right";

        public static string GetVirtualSerial(int slot)
        {
            return VirtualSerialPrefix + slot.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string GetVirtualDevicePath(int slot)
        {
            return "/devices/trackswap/" + GetVirtualSerial(slot);
        }
    }

    public static class DriverControlProtocol
    {
        public const uint Magic = 0x50575354; // "TSWP" in little-endian byte order.
        public const ushort Version = 2;
        public const ushort SetSourceMessageType = 1;
        public const ushort SetOffsetMessageType = 2;
        public const ushort ApplySnapshotMessageType = 3;
        public const ushort GetTelemetryMessageType = 4;
        public const ushort ResponseFlag = 0x8000;
        public const int HeaderBytes = 20;
        public const int MaximumPayloadBytes = 4096;
        public const int ApplySnapshotFixedBytes = 70;
        public const int MaximumCombinedDevicePathBytes = MaximumPayloadBytes - ApplySnapshotFixedBytes;
        public const int TelemetryPoseBytes = 62;
        public const int TelemetrySnapshotBytes = 202;
        public const int TelemetryBatchBytes = 1 + (ProtocolConstants.MaximumRoutes * TelemetrySnapshotBytes);
    }
}
