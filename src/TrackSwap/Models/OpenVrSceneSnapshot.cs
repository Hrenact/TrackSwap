using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace TrackSwap.Models
{
    public sealed class OpenVrSceneSnapshot
    {
        public IReadOnlyList<OpenVrSceneDevice> Devices { get; set; } =
            new List<OpenVrSceneDevice>();

        public OpenVrBackgroundState Background { get; set; } =
            new OpenVrBackgroundState();
    }

    public sealed class OpenVrSceneDevice
    {
        public uint DeviceIndex { get; set; }

        public string Identity { get; set; }

        public string DisplayName { get; set; }

        public string ModelNumber { get; set; }

        public string SerialNumber { get; set; }

        public string ManufacturerName { get; set; }

        public string TrackingSystemName { get; set; }

        public string RenderModelName { get; set; }

        public string TrackingFirmwareVersion { get; set; }

        public string HardwareRevision { get; set; }

        public string ControllerType { get; set; }

        public string TrackingReferenceMode { get; set; }

        public TrackedDeviceKind DeviceKind { get; set; }

        public OpenVrSceneDeviceClass DeviceClass { get; set; }

        public OpenVrControllerRole ControllerRole { get; set; }

        public bool? IsWireless { get; set; }

        public bool? IsCharging { get; set; }

        public double? BatteryPercentage { get; set; }

        public double? DisplayFrequencyHz { get; set; }

        public double? TrackingRangeMinimumMetres { get; set; }

        public double? TrackingRangeMaximumMetres { get; set; }

        public bool Connected { get; set; }

        public bool Valid { get; set; }

        public OpenVrTrackingResult TrackingResult { get; set; }

        public Matrix3D Transform { get; set; } = Matrix3D.Identity;

        public Vector3D LinearVelocity { get; set; }

        public Vector3D AngularVelocity { get; set; }
    }

    public enum OpenVrSceneDeviceClass
    {
        Invalid = 0,
        Hmd = 1,
        Controller = 2,
        GenericTracker = 3,
        TrackingReference = 4,
        DisplayRedirect = 5
    }

    public enum OpenVrControllerRole
    {
        Invalid = 0,
        LeftHand = 1,
        RightHand = 2,
        OptOut = 3,
        Treadmill = 4,
        Stylus = 5
    }

    public enum OpenVrTrackingResult
    {
        Uninitialized = 1,
        CalibratingInProgress = 100,
        CalibratingOutOfRange = 101,
        RunningOk = 200,
        RunningOutOfRange = 201,
        FallbackRotationOnly = 300
    }

    public sealed class OpenVrBackgroundState
    {
        public string SettingValue { get; set; }

        public string ImagePath { get; set; }

        public string SolidColor { get; set; }
    }
}
