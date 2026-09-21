using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TrackSwap.Services
{
    public sealed class SteamVrApplicationService
    {
        public const string ApplicationKey = "com.hrenact.trackswap";
        private const string ApplicationsInterfaceVersion = "FnTable:IVRApplications_007";

        public bool SetAutoLaunch(string runtimePath, string manifestPath, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
            {
                throw new InvalidOperationException("未找到 SteamVR Runtime 路径。");
            }
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                throw new FileNotFoundException("未找到 TrackSwap 的 SteamVR 应用清单。", manifestPath);
            }

            string libraryPath = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException("未找到 OpenVR 运行库。", libraryPath);
            }

            lock (OpenVrInterop.SyncRoot)
            {
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
                    init(ref initError, EVRApplicationType.Utility, null);
                    if (initError != EVRInitError.None)
                    {
                        throw new InvalidOperationException("OpenVR 初始化失败，错误码：" + (int)initError);
                    }

                    initialized = true;
                    IntPtr applicationsTable = getInterface(ApplicationsInterfaceVersion, ref initError);
                    if (applicationsTable == IntPtr.Zero || initError != EVRInitError.None)
                    {
                        throw new InvalidOperationException("无法获取 OpenVR Applications 接口，错误码：" + (int)initError);
                    }

                    AddApplicationManifest addManifest = GetTableFunction<AddApplicationManifest>(applicationsTable, 0);
                    IdentifyApplication identifyApplication = GetTableFunction<IdentifyApplication>(applicationsTable, 11);
                    SetApplicationAutoLaunch setAutoLaunch = GetTableFunction<SetApplicationAutoLaunch>(applicationsTable, 17);
                    EVRApplicationError addError = addManifest(Path.GetFullPath(manifestPath), false);
                    if (addError != EVRApplicationError.None &&
                        addError != EVRApplicationError.AppKeyAlreadyExists)
                    {
                        throw new InvalidOperationException("SteamVR 应用清单注册失败，错误码：" + (int)addError);
                    }

                    identifyApplication(
                        unchecked((uint)System.Diagnostics.Process.GetCurrentProcess().Id),
                        ApplicationKey);
                    EVRApplicationError launchError = setAutoLaunch(ApplicationKey, enabled);
                    if (launchError == EVRApplicationError.UnknownApplication)
                    {
                        // SteamVR can defer a newly-added permanent manifest until its next
                        // session. The driver/runtime fallback covers the current session.
                        return false;
                    }
                    if (launchError != EVRApplicationError.None)
                    {
                        throw new InvalidOperationException("SteamVR 自动启动设置失败，错误码：" + (int)launchError);
                    }
                    return true;
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
                        }
                    }
                    FreeLibrary(module);
                }
            }
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
                throw new InvalidOperationException("OpenVR Applications 函数表不完整。");
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError AddApplicationManifest(
            string applicationManifestFullPath,
            [MarshalAs(UnmanagedType.I1)] bool temporary);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError SetApplicationAutoLaunch(
            string applicationKey,
            [MarshalAs(UnmanagedType.I1)] bool autoLaunch);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError IdentifyApplication(uint processId, string applicationKey);

        private enum EVRApplicationType
        {
            Utility = 4
        }

        private enum EVRInitError
        {
            None = 0
        }

        private enum EVRApplicationError
        {
            None = 0,
            AppKeyAlreadyExists = 100,
            UnknownApplication = 104
        }
    }
}
