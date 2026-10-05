using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Media3D;
using TrackSwap.Models;

namespace TrackSwap.Services
{
    internal sealed class OpenVrSceneService
    {
        internal const int GetDeviceToAbsoluteTrackingPoseIndex = 12;
        internal const uint MaximumTrackedDeviceCount = 64;

        private const string SystemInterfaceVersion = "FnTable:IVRSystem_026";
        private static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromSeconds(1);

        private readonly Dictionary<uint, DeviceMetadata> catalog =
            new Dictionary<uint, DeviceMetadata>();
        private readonly OpenVrBackgroundService backgroundService =
            new OpenVrBackgroundService();
        private OpenVrBackgroundState background = new OpenVrBackgroundState();
        private DateTime lastCatalogRefreshUtc = DateTime.MinValue;

        internal static int TrackedDevicePoseBytes => Marshal.SizeOf<TrackedDevicePose>();

        public OpenVrSceneSnapshot Capture(string runtimePath)
        {
            lock (OpenVrInterop.SyncRoot)
            {
                IntPtr systemTable = OpenVrInterop.GetInterface(runtimePath, SystemInterfaceVersion);
                GetDeviceToAbsoluteTrackingPose getPoses =
                    GetTableFunction<GetDeviceToAbsoluteTrackingPose>(
                        systemTable,
                        GetDeviceToAbsoluteTrackingPoseIndex);
                IsTrackedDeviceConnected isConnected =
                    GetTableFunction<IsTrackedDeviceConnected>(systemTable, 21);
                GetTrackedDeviceClass getClass =
                    GetTableFunction<GetTrackedDeviceClass>(systemTable, 20);
                GetStringTrackedDeviceProperty getStringProperty =
                    GetTableFunction<GetStringTrackedDeviceProperty>(systemTable, 28);
                GetControllerRole getControllerRole =
                    GetTableFunction<GetControllerRole>(systemTable, 19);
                GetBoolTrackedDeviceProperty getBoolProperty =
                    GetTableFunction<GetBoolTrackedDeviceProperty>(systemTable, 22);
                GetFloatTrackedDeviceProperty getFloatProperty =
                    GetTableFunction<GetFloatTrackedDeviceProperty>(systemTable, 23);

                int poseBytes = TrackedDevicePoseBytes;
                IntPtr poseBuffer = Marshal.AllocHGlobal(checked(poseBytes * (int)MaximumTrackedDeviceCount));
                try
                {
                    getPoses(
                        ETrackingUniverseOrigin.Standing,
                        0,
                        poseBuffer,
                        MaximumTrackedDeviceCount);

                    DateTime nowUtc = DateTime.UtcNow;
                    if (nowUtc - lastCatalogRefreshUtc >= CatalogRefreshInterval)
                    {
                        RefreshCatalog(
                            isConnected,
                            getClass,
                            getControllerRole,
                            getStringProperty,
                            getBoolProperty,
                            getFloatProperty);
                        background = backgroundService.Capture(runtimePath);
                        lastCatalogRefreshUtc = nowUtc;
                    }

                    var devices = new List<OpenVrSceneDevice>(catalog.Count);
                    foreach (KeyValuePair<uint, DeviceMetadata> entry in catalog.OrderBy(pair => pair.Key))
                    {
                        TrackedDevicePose pose = ReadPose(poseBuffer, poseBytes, entry.Key);
                        devices.Add(new OpenVrSceneDevice
                        {
                            DeviceIndex = entry.Key,
                            Identity = entry.Value.Identity,
                            DisplayName = entry.Value.DisplayName,
                            ModelNumber = entry.Value.ModelNumber,
                            SerialNumber = entry.Value.SerialNumber,
                            ManufacturerName = entry.Value.ManufacturerName,
                            TrackingSystemName = entry.Value.TrackingSystemName,
                            RenderModelName = entry.Value.RenderModelName,
                            TrackingFirmwareVersion = entry.Value.TrackingFirmwareVersion,
                            HardwareRevision = entry.Value.HardwareRevision,
                            ControllerType = entry.Value.ControllerType,
                            TrackingReferenceMode = entry.Value.TrackingReferenceMode,
                            DeviceKind = entry.Value.DeviceKind,
                            DeviceClass = (OpenVrSceneDeviceClass)entry.Value.DeviceClass,
                            ControllerRole = (OpenVrControllerRole)entry.Value.ControllerRole,
                            IsWireless = entry.Value.IsWireless,
                            IsCharging = entry.Value.IsCharging,
                            BatteryPercentage = entry.Value.BatteryPercentage,
                            DisplayFrequencyHz = entry.Value.DisplayFrequencyHz,
                            TrackingRangeMinimumMetres = entry.Value.TrackingRangeMinimumMetres,
                            TrackingRangeMaximumMetres = entry.Value.TrackingRangeMaximumMetres,
                            Connected = pose.DeviceIsConnected,
                            Valid = pose.PoseIsValid && IsFinite(pose.DeviceToAbsoluteTracking),
                            TrackingResult = (OpenVrTrackingResult)pose.TrackingResult,
                            Transform = ToMatrix3D(pose.DeviceToAbsoluteTracking)
                        });
                    }

                    return new OpenVrSceneSnapshot
                    {
                        Devices = devices,
                        Background = background
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(poseBuffer);
                }
            }
        }

        internal void ResetCache()
        {
            lock (OpenVrInterop.SyncRoot)
            {
                catalog.Clear();
                background = new OpenVrBackgroundState();
                lastCatalogRefreshUtc = DateTime.MinValue;
            }
        }

        private void RefreshCatalog(
            IsTrackedDeviceConnected isConnected,
            GetTrackedDeviceClass getClass,
            GetControllerRole getControllerRole,
            GetStringTrackedDeviceProperty getStringProperty,
            GetBoolTrackedDeviceProperty getBoolProperty,
            GetFloatTrackedDeviceProperty getFloatProperty)
        {
            var refreshed = new Dictionary<uint, DeviceMetadata>();
            for (uint index = 0; index < MaximumTrackedDeviceCount; index++)
            {
                if (!isConnected(index))
                {
                    continue;
                }

                ETrackedDeviceClass deviceClass = getClass(index);
                if (deviceClass == ETrackedDeviceClass.Invalid)
                {
                    continue;
                }

                string registeredType = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.RegisteredDeviceType);
                string serial = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.SerialNumber);
                string model = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.ModelNumber);
                string renderModel = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.RenderModelName);
                string manufacturer = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.ManufacturerName);
                string trackingSystem = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.TrackingSystemName);
                string identity = !string.IsNullOrWhiteSpace(registeredType)
                    ? "/devices/" + registeredType
                    : !string.IsNullOrWhiteSpace(serial)
                        ? "serial:" + serial
                        : "index:" + index;
                string displayName = !string.IsNullOrWhiteSpace(model)
                    ? model
                    : !string.IsNullOrWhiteSpace(serial)
                        ? serial
                        : deviceClass.ToString();
                refreshed[index] = new DeviceMetadata
                {
                    Identity = identity,
                    DisplayName = displayName,
                    ModelNumber = model,
                    SerialNumber = serial,
                    ManufacturerName = manufacturer,
                    TrackingSystemName = trackingSystem,
                    RenderModelName = renderModel,
                    TrackingFirmwareVersion = ReadStringProperty(
                        getStringProperty,
                        index,
                        ETrackedDeviceProperty.TrackingFirmwareVersion),
                    HardwareRevision = ReadStringProperty(
                        getStringProperty,
                        index,
                        ETrackedDeviceProperty.HardwareRevision),
                    ControllerType = deviceClass == ETrackedDeviceClass.Controller
                        ? ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.ControllerType)
                        : null,
                    TrackingReferenceMode = deviceClass == ETrackedDeviceClass.TrackingReference
                        ? ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.ModeLabel)
                        : null,
                    DeviceKind = ToDeviceKind(deviceClass),
                    DeviceClass = deviceClass,
                    ControllerRole = deviceClass == ETrackedDeviceClass.Controller
                        ? getControllerRole(index)
                        : ETrackedControllerRole.Invalid,
                    IsWireless = ReadBoolProperty(
                        getBoolProperty,
                        index,
                        ETrackedDeviceProperty.DeviceIsWireless),
                    IsCharging = ReadBoolProperty(
                        getBoolProperty,
                        index,
                        ETrackedDeviceProperty.DeviceIsCharging),
                    BatteryPercentage = ReadFloatProperty(
                        getFloatProperty,
                        index,
                        ETrackedDeviceProperty.DeviceBatteryPercentage),
                    DisplayFrequencyHz = deviceClass == ETrackedDeviceClass.Hmd
                        ? ReadFloatProperty(getFloatProperty, index, ETrackedDeviceProperty.DisplayFrequency)
                        : null,
                    TrackingRangeMinimumMetres = deviceClass == ETrackedDeviceClass.TrackingReference
                        ? ReadFloatProperty(getFloatProperty, index, ETrackedDeviceProperty.TrackingRangeMinimum)
                        : null,
                    TrackingRangeMaximumMetres = deviceClass == ETrackedDeviceClass.TrackingReference
                        ? ReadFloatProperty(getFloatProperty, index, ETrackedDeviceProperty.TrackingRangeMaximum)
                        : null
                };
            }

            catalog.Clear();
            foreach (KeyValuePair<uint, DeviceMetadata> entry in refreshed)
            {
                catalog.Add(entry.Key, entry.Value);
            }
        }

        internal static Matrix3D ToMatrix3D(HmdMatrix34 value)
        {
            return new Matrix3D(
                value.M00, value.M10, value.M20, 0,
                value.M01, value.M11, value.M21, 0,
                value.M02, value.M12, value.M22, 0,
                value.M03, value.M13, value.M23, 1);
        }

        private static bool IsFinite(HmdMatrix34 value)
        {
            return IsFinite(value.M00) && IsFinite(value.M01) && IsFinite(value.M02) && IsFinite(value.M03) &&
                IsFinite(value.M10) && IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) &&
                IsFinite(value.M20) && IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static TrackedDevicePose ReadPose(IntPtr buffer, int poseBytes, uint deviceIndex)
        {
            return Marshal.PtrToStructure<TrackedDevicePose>(
                IntPtr.Add(buffer, checked((int)deviceIndex * poseBytes)));
        }

        private static string ReadStringProperty(
            GetStringTrackedDeviceProperty getter,
            uint deviceIndex,
            ETrackedDeviceProperty property)
        {
            ETrackedPropertyError error = ETrackedPropertyError.Success;
            uint requiredLength = getter(deviceIndex, property, null, 0, ref error);
            if (requiredLength == 0 || requiredLength > 32768)
            {
                return null;
            }

            var value = new StringBuilder((int)requiredLength);
            error = ETrackedPropertyError.Success;
            getter(deviceIndex, property, value, requiredLength, ref error);
            return error == ETrackedPropertyError.Success ? value.ToString() : null;
        }

        private static bool? ReadBoolProperty(
            GetBoolTrackedDeviceProperty getter,
            uint deviceIndex,
            ETrackedDeviceProperty property)
        {
            ETrackedPropertyError error = ETrackedPropertyError.Success;
            bool value = getter(deviceIndex, property, ref error);
            return error == ETrackedPropertyError.Success ? value : (bool?)null;
        }

        private static double? ReadFloatProperty(
            GetFloatTrackedDeviceProperty getter,
            uint deviceIndex,
            ETrackedDeviceProperty property)
        {
            ETrackedPropertyError error = ETrackedPropertyError.Success;
            float value = getter(deviceIndex, property, ref error);
            return error == ETrackedPropertyError.Success && !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : (double?)null;
        }

        private static TrackedDeviceKind ToDeviceKind(ETrackedDeviceClass deviceClass)
        {
            switch (deviceClass)
            {
                case ETrackedDeviceClass.Hmd:
                    return TrackedDeviceKind.Hmd;
                case ETrackedDeviceClass.Controller:
                    return TrackedDeviceKind.Controller;
                case ETrackedDeviceClass.GenericTracker:
                    return TrackedDeviceKind.Tracker;
                default:
                    return TrackedDeviceKind.Unknown;
            }
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException("OpenVR function table is incomplete.");
            }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private sealed class DeviceMetadata
        {
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
            public ETrackedDeviceClass DeviceClass { get; set; }
            public ETrackedControllerRole ControllerRole { get; set; }
            public bool? IsWireless { get; set; }
            public bool? IsCharging { get; set; }
            public double? BatteryPercentage { get; set; }
            public double? DisplayFrequencyHz { get; set; }
            public double? TrackingRangeMinimumMetres { get; set; }
            public double? TrackingRangeMaximumMetres { get; set; }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin origin,
            float predictedSeconds,
            IntPtr poses,
            uint poseCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool IsTrackedDeviceConnected(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ETrackedDeviceClass GetTrackedDeviceClass(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ETrackedControllerRole GetControllerRole(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool GetBoolTrackedDeviceProperty(
            uint deviceIndex,
            ETrackedDeviceProperty property,
            ref ETrackedPropertyError error);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate float GetFloatTrackedDeviceProperty(
            uint deviceIndex,
            ETrackedDeviceProperty property,
            ref ETrackedPropertyError error);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate uint GetStringTrackedDeviceProperty(
            uint deviceIndex,
            ETrackedDeviceProperty property,
            StringBuilder value,
            uint valueCapacity,
            ref ETrackedPropertyError error);

        private enum ETrackingUniverseOrigin
        {
            Standing = 1
        }

        private enum ETrackedDeviceClass
        {
            Invalid = 0,
            Hmd = 1,
            Controller = 2,
            GenericTracker = 3,
            TrackingReference = 4,
            DisplayRedirect = 5
        }

        private enum ETrackedControllerRole
        {
            Invalid = 0,
            LeftHand = 1,
            RightHand = 2,
            OptOut = 3,
            Treadmill = 4,
            Stylus = 5
        }

        private enum ETrackedDeviceProperty
        {
            TrackingSystemName = 1000,
            ModelNumber = 1001,
            SerialNumber = 1002,
            RenderModelName = 1003,
            ManufacturerName = 1005,
            TrackingFirmwareVersion = 1006,
            HardwareRevision = 1007,
            DeviceIsWireless = 1010,
            DeviceIsCharging = 1011,
            DeviceBatteryPercentage = 1012,
            RegisteredDeviceType = 1036,
            DisplayFrequency = 2002,
            TrackingRangeMinimum = 4004,
            TrackingRangeMaximum = 4005,
            ModeLabel = 4006,
            ControllerType = 7000
        }

        private enum ETrackedPropertyError
        {
            Success = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HmdMatrix34
        {
            public float M00, M01, M02, M03;
            public float M10, M11, M12, M13;
            public float M20, M21, M22, M23;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HmdVector3
        {
            public float X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrackedDevicePose
        {
            public HmdMatrix34 DeviceToAbsoluteTracking;
            public HmdVector3 Velocity;
            public HmdVector3 AngularVelocity;
            public int TrackingResult;
            public byte PoseIsValidRaw;
            public byte DeviceIsConnectedRaw;

            public bool PoseIsValid => PoseIsValidRaw != 0;
            public bool DeviceIsConnected => DeviceIsConnectedRaw != 0;
        }
    }
}
