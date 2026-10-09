using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TrackSwap.Models;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    public sealed class OpenVrDeviceService
    {
        private const string SystemInterfaceVersion = "FnTable:IVRSystem_026";
        private const uint MaxTrackedDeviceCount = 64;

        public IReadOnlyList<DeviceOption> EnumerateOnlineDevices(string runtimePath)
        {
            lock (OpenVrInterop.SyncRoot)
            {
                return EnumerateOnlineDevicesCore(runtimePath);
            }
        }

        private IReadOnlyList<DeviceOption> EnumerateOnlineDevicesCore(string runtimePath)
        {
            IntPtr systemTable = OpenVrInterop.GetInterface(runtimePath, SystemInterfaceVersion);

            // These positions must match Valve's IVRSystem_026 function table exactly.
            GetControllerRole getRole = GetTableFunction<GetControllerRole>(systemTable, 19);
            GetTrackedDeviceClass getClass = GetTableFunction<GetTrackedDeviceClass>(systemTable, 20);
            IsTrackedDeviceConnected isConnected = GetTableFunction<IsTrackedDeviceConnected>(systemTable, 21);
            GetStringTrackedDeviceProperty getStringProperty = GetTableFunction<GetStringTrackedDeviceProperty>(systemTable, 28);

            var devices = new List<DeviceOption>();
            for (uint index = 0; index < MaxTrackedDeviceCount; index++)
            {
                if (!isConnected(index))
                {
                    continue;
                }

                ETrackedDeviceClass deviceClass = getClass(index);
                if (deviceClass != ETrackedDeviceClass.Hmd &&
                    deviceClass != ETrackedDeviceClass.Controller &&
                    deviceClass != ETrackedDeviceClass.GenericTracker)
                {
                    continue;
                }

                string registeredType = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.RegisteredDeviceType);
                if (string.IsNullOrWhiteSpace(registeredType))
                {
                    continue;
                }

                string model = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.ModelNumber);
                string manufacturer = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.ManufacturerName);
                string trackingSystem = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.TrackingSystemName);
                string serial = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.SerialNumber);
                string renderModel = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.RenderModelName);
                string controllerType = deviceClass == ETrackedDeviceClass.Controller
                    ? ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.ControllerType)
                    : null;
                string hardwareRevision = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.HardwareRevision);
                string trackingFirmwareVersion = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.TrackingFirmwareVersion);
                string connectedWirelessDongleId = ReadStringProperty(
                    getStringProperty,
                    index,
                    ETrackedDeviceProperty.ConnectedWirelessDongle);
                ETrackedControllerRole role = deviceClass == ETrackedDeviceClass.Controller
                    ? getRole(index)
                    : ETrackedControllerRole.Invalid;

                devices.Add(new DeviceOption(
                    BuildDisplayName(model, serial, deviceClass, role),
                    "/devices/" + registeredType,
                    true,
                    index,
                    serial,
                    GetRoleTargetPath(deviceClass, role),
                    renderModel,
                    GetDeviceKind(deviceClass),
                    PoseSourceKind.Device,
                    connectedWirelessDongleId,
                    manufacturer,
                    model,
                    trackingSystem,
                    controllerType,
                    hardwareRevision,
                    trackingFirmwareVersion));
            }

            return devices
                .OrderBy(device => device.DeviceIndex)
                .ToList();
        }

        private static string BuildDisplayName(
            string model,
            string serial,
            ETrackedDeviceClass deviceClass,
            ETrackedControllerRole role)
        {
            var parts = new List<string>();
            parts.Add(!string.IsNullOrWhiteSpace(model) ? model : DeviceClassName(deviceClass));

            if (role == ETrackedControllerRole.LeftHand)
            {
                parts.Add(Tr.Get("common.hand.left"));
            }
            else if (role == ETrackedControllerRole.RightHand)
            {
                parts.Add(Tr.Get("common.hand.right"));
            }

            if (!string.IsNullOrWhiteSpace(serial))
            {
                parts.Add(serial);
            }

            return string.Join(" · ", parts);
        }

        private static string GetRoleTargetPath(
            ETrackedDeviceClass deviceClass,
            ETrackedControllerRole role)
        {
            if (deviceClass == ETrackedDeviceClass.Hmd)
            {
                return "/user/head";
            }
            if (role == ETrackedControllerRole.LeftHand)
            {
                return "/user/hand/left";
            }
            if (role == ETrackedControllerRole.RightHand)
            {
                return "/user/hand/right";
            }
            return null;
        }

        private static string DeviceClassName(ETrackedDeviceClass deviceClass)
        {
            switch (deviceClass)
            {
                case ETrackedDeviceClass.Hmd:
                    return Tr.Get("common.device.hmd");
                case ETrackedDeviceClass.Controller:
                    return Tr.Get("service.open_vr_device.device_class_name.controller");
                case ETrackedDeviceClass.GenericTracker:
                    return Tr.Get("service.open_vr_device.device_class_name.tracker");
                default:
                    return Tr.Get("service.open_vr_device.device_class_name.openvr_device");
            }
        }

        private static TrackedDeviceKind GetDeviceKind(ETrackedDeviceClass deviceClass)
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

        private static string ReadStringProperty(
            GetStringTrackedDeviceProperty getter,
            uint deviceIndex,
            ETrackedDeviceProperty property)
        {
            ETrackedPropertyError error = ETrackedPropertyError.Success;
            uint requiredLength = getter(deviceIndex, property, null, 0, ref error);
            if (requiredLength == 0)
            {
                return null;
            }

            var value = new StringBuilder((int)requiredLength);
            error = ETrackedPropertyError.Success;
            getter(deviceIndex, property, value, requiredLength, ref error);
            return error == ETrackedPropertyError.Success ? value.ToString() : null;
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException(Tr.Get("service.open_vr_device.read_string_property.openvr_complete"));
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ETrackedControllerRole GetControllerRole(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ETrackedDeviceClass GetTrackedDeviceClass(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool IsTrackedDeviceConnected(uint deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate uint GetStringTrackedDeviceProperty(
            uint deviceIndex,
            ETrackedDeviceProperty property,
            StringBuilder value,
            uint valueCapacity,
            ref ETrackedPropertyError error);

        private enum ETrackedDeviceClass
        {
            Hmd = 1,
            Controller = 2,
            GenericTracker = 3
        }

        private enum ETrackedControllerRole
        {
            Invalid = 0,
            LeftHand = 1,
            RightHand = 2
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
            ConnectedWirelessDongle = 1009,
            RegisteredDeviceType = 1036,
            ControllerType = 7000
        }

        private enum ETrackedPropertyError
        {
            Success = 0
        }
    }
}
