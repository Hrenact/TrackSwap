namespace TrackSwap.Protocol
{
    public static class ProtocolConstants
    {
        public const int CurrentProtocolVersion = 2;
        public const int CurrentConfigurationSchemaVersion = 1;
        public const int MaximumMessageBytes = 64 * 1024;
        public const int MaximumRoutes = 16;
        public const int MaximumRouteIdCharacters = 128;
        public const int MinimumCalibrationSamples = 60;
        public const int MaximumCalibrationSamples = 180;
        public const int DefaultControllerHandSelectionPriority = 0;
        public const string PipeName = "TrackSwap.Runtime.v1";
        public const string DriverPipeName = "TrackSwap.Driver.v1";
        public const string TrackerSerialPrefix = "TRKSWAP-TRACKER-";
        public const string ProxySerialPrefix = "TRKSWAP-PROXY-";
        public const string LeftControllerSerial = "TRKSWAP-CONTROLLER-L";
        public const string RightControllerSerial = "TRKSWAP-CONTROLLER-R";
        public const string HeadRolePath = "/user/head";
        public const string LeftHandRolePath = "/user/hand/left";
        public const string RightHandRolePath = "/user/hand/right";

        public static string GetTrackerSerial(int slot)
        {
            return TrackerSerialPrefix + slot.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string GetProxySerial(int slot)
        {
            return ProxySerialPrefix + slot.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string GetOutputSerial(RouteMode mode, int slot)
        {
            return mode == RouteMode.DirectProxy ? GetTrackerSerial(slot) : GetProxySerial(slot);
        }

        public static string GetProxyDevicePath(int slot)
        {
            return "/devices/trackswap/" + GetProxySerial(slot);
        }

        public static bool IsTrackSwapVirtualDevicePath(string devicePath)
        {
            return !string.IsNullOrWhiteSpace(devicePath) &&
                devicePath.StartsWith("/devices/trackswap/", System.StringComparison.OrdinalIgnoreCase) &&
                (devicePath.IndexOf(TrackerSerialPrefix, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 devicePath.IndexOf(ProxySerialPrefix, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 devicePath.IndexOf("TRKSWAP-CONTROLLER-", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string GetControllerSerial(ControllerHand hand)
        {
            return hand == ControllerHand.Left ? LeftControllerSerial :
                hand == ControllerHand.Right ? RightControllerSerial : string.Empty;
        }
    }

    public static class DriverControlProtocol
    {
        public const uint Magic = 0x50575354; // "TSWP" in little-endian byte order.
        public const ushort Version = 5;
        public const ushort SetSourceMessageType = 1;
        public const ushort SetOffsetMessageType = 2;
        public const ushort ApplySnapshotMessageType = 3;
        public const ushort GetTelemetryMessageType = 4;
        public const ushort ApplyControllerSnapshotMessageType = 5;
        public const ushort ApplyControllerInputMessageType = 6;
        public const ushort ResponseFlag = 0x8000;
        public const int HeaderBytes = 20;
        public const int MaximumPayloadBytes = 4096;
        public const int ApplySnapshotFixedBytes = 70;
        public const int ApplyControllerSnapshotFixedBytes = 73;
        public const int MaximumCombinedDevicePathBytes = MaximumPayloadBytes - ApplySnapshotFixedBytes;
        public const int TelemetryPoseBytes = 62;
        public const int TelemetrySnapshotBytes = 202;
        public const int TelemetryBatchBytes = 1 + (ProtocolConstants.MaximumRoutes * TelemetrySnapshotBytes);
        public const int ControllerInputBytes = 23;
    }
}
