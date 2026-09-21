using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TrackSwap.Models;

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
            if (string.IsNullOrWhiteSpace(runtimePath))
            {
                throw new InvalidOperationException("未找到 SteamVR Runtime 路径。");
            }

            string libraryPath = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException("未找到 OpenVR 运行库。", libraryPath);
            }

            IntPtr module = LoadLibrary(libraryPath);
            if (module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法载入 OpenVR 运行库。");
            }

            bool initialized = false;
            try
            {
                VRInitInternal init = GetExport<VRInitInternal>(module, "VR_InitInternal2");
                VRGetGenericInterface getInterface = GetExport<VRGetGenericInterface>(module, "VR_GetGenericInterface");
                VRShutdownInternal shutdown = GetExport<VRShutdownInternal>(module, "VR_ShutdownInternal");

                EVRInitError initError = EVRInitError.None;
                init(ref initError, EVRApplicationType.Background, null);
                if (initError != EVRInitError.None)
                {
                    throw new InvalidOperationException("OpenVR 初始化失败，错误码：" + (int)initError);
                }

                initialized = true;
                IntPtr systemTable = getInterface(SystemInterfaceVersion, ref initError);
                if (systemTable == IntPtr.Zero || initError != EVRInitError.None)
                {
                    throw new InvalidOperationException("无法获取 OpenVR System 接口，错误码：" + (int)initError);
                }

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
                    string serial = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.SerialNumber);
                    string renderModel = ReadStringProperty(getStringProperty, index, ETrackedDeviceProperty.RenderModelName);
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
                        GetDeviceKind(deviceClass)));
                }

                return devices
                    .OrderBy(device => device.DeviceIndex)
                    .ToList();
            }
            finally
            {
                if (initialized)
                {
                    try
                    {
                        GetExport<VRShutdownInternal>(module, "VR_ShutdownInternal")();
                    }
                    catch
                    {
                        // The runtime is shutting down; unloading the bridge is still safe here.
                    }
                }

                FreeLibrary(module);
            }
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
                parts.Add("左手");
            }
            else if (role == ETrackedControllerRole.RightHand)
            {
                parts.Add("右手");
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
                    return "头显";
                case ETrackedDeviceClass.Controller:
                    return "控制器";
                case ETrackedDeviceClass.GenericTracker:
                    return "追踪器";
                default:
                    return "OpenVR 设备";
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

        private static T GetExport<T>(IntPtr module, string name) where T : class
        {
            IntPtr address = GetProcAddress(module, name);
            if (address == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException(name);
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException("OpenVR 函数表不完整。");
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate uint VRInitInternal(ref EVRInitError error, EVRApplicationType applicationType, string startupInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate IntPtr VRGetGenericInterface(string interfaceVersion, ref EVRInitError error);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VRShutdownInternal();

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

        private enum EVRApplicationType
        {
            Background = 3
        }

        private enum EVRInitError
        {
            None = 0
        }

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
            ModelNumber = 1001,
            SerialNumber = 1002,
            RenderModelName = 1003,
            RegisteredDeviceType = 1036
        }

        private enum ETrackedPropertyError
        {
            Success = 0
        }
    }
}
