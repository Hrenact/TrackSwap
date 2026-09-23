using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TrackSwap.Services
{
    internal static class OpenVrInterop
    {
        internal static readonly object SyncRoot = new object();

        private static IntPtr _module;
        private static VRGetGenericInterface _getInterface;
        private static VRShutdownInternal _shutdown;
        private static string _libraryPath;
        private static bool _initialized;
        private static bool _sessionEnding;

        private const string SystemInterfaceVersion = "FnTable:IVRSystem_026";
        private const int PollNextEventFunctionIndex = 30;
        private const int VrEventSize = 64;
        private const uint QuitEventType = 700;

        internal static IntPtr GetInterface(string runtimePath, string interfaceVersion)
        {
            lock (SyncRoot)
            {
                if (_sessionEnding)
                {
                    throw new InvalidOperationException("SteamVR 正在退出。");
                }
                EnsureInitialized(runtimePath);
                EVRInitError error = EVRInitError.None;
                IntPtr table = _getInterface(interfaceVersion, ref error);
                if (table == IntPtr.Zero || error != EVRInitError.None)
                {
                    throw new InvalidOperationException(
                        "无法获取 OpenVR 接口 “" + interfaceVersion + "”，错误码：" + (int)error);
                }
                return table;
            }
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                if (_initialized && _shutdown != null)
                {
                    try
                    {
                        _shutdown();
                    }
                    catch
                    {
                        // SteamVR may already be shutting down.
                    }
                }

                _initialized = false;
                _getInterface = null;
                _shutdown = null;
                _libraryPath = null;

                if (_module != IntPtr.Zero)
                {
                    FreeLibrary(_module);
                    _module = IntPtr.Zero;
                }
            }
        }

        internal static bool DetachIfSteamVrIsQuitting()
        {
            lock (SyncRoot)
            {
                if (!_initialized || _getInterface == null || _sessionEnding)
                {
                    return false;
                }

                EVRInitError error = EVRInitError.None;
                IntPtr systemTable = _getInterface(SystemInterfaceVersion, ref error);
                if (systemTable == IntPtr.Zero || error != EVRInitError.None)
                {
                    return false;
                }

                IntPtr functionAddress = Marshal.ReadIntPtr(
                    systemTable,
                    PollNextEventFunctionIndex * IntPtr.Size);
                if (functionAddress == IntPtr.Zero)
                {
                    return false;
                }

                var pollNextEvent = (PollNextEvent)Marshal.GetDelegateForFunctionPointer(
                    functionAddress,
                    typeof(PollNextEvent));
                IntPtr eventBuffer = Marshal.AllocHGlobal(VrEventSize);
                bool quitObserved = false;
                try
                {
                    for (int index = 0; index < 128 && pollNextEvent(eventBuffer, VrEventSize); index++)
                    {
                        uint eventType = unchecked((uint)Marshal.ReadInt32(eventBuffer));
                        if (eventType == QuitEventType)
                        {
                            quitObserved = true;
                            break;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(eventBuffer);
                }

                if (!quitObserved)
                {
                    return false;
                }

                Reset();
                _sessionEnding = true;
                return true;
            }
        }

        internal static void NotifySteamVrStopped()
        {
            lock (SyncRoot)
            {
                if (!_sessionEnding)
                {
                    return;
                }

                Reset();
                _sessionEnding = false;
            }
        }

        private static void EnsureInitialized(string runtimePath)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
            {
                throw new InvalidOperationException("未找到 SteamVR Runtime 路径。");
            }

            string libraryPath = Path.GetFullPath(
                Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll"));
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException("未找到 OpenVR 运行库。", libraryPath);
            }

            if (_initialized &&
                string.Equals(_libraryPath, libraryPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_module != IntPtr.Zero)
            {
                Reset();
            }

            _module = LoadLibrary(libraryPath);
            if (_module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法载入 OpenVR 运行库。");
            }

            try
            {
                VRInitInternal init = GetExport<VRInitInternal>(_module, "VR_InitInternal2");
                _getInterface = GetExport<VRGetGenericInterface>(_module, "VR_GetGenericInterface");
                _shutdown = GetExport<VRShutdownInternal>(_module, "VR_ShutdownInternal");

                EVRInitError error = EVRInitError.None;
                init(ref error, EVRApplicationType.Utility, null);
                if (error != EVRInitError.None)
                {
                    throw new InvalidOperationException(
                        "OpenVR 初始化失败，错误码：" + (int)error);
                }

                _libraryPath = libraryPath;
                _initialized = true;
            }
            catch
            {
                Reset();
                throw;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate uint VRInitInternal(
            ref EVRInitError error,
            EVRApplicationType applicationType,
            string startupInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate IntPtr VRGetGenericInterface(
            string interfaceVersion,
            ref EVRInitError error);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VRShutdownInternal();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool PollNextEvent(IntPtr eventData, uint eventDataSize);

        private enum EVRApplicationType
        {
            Utility = 4
        }

        private enum EVRInitError
        {
            None = 0
        }
    }
}
