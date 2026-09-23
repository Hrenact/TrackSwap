using System;
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

            lock (OpenVrInterop.SyncRoot)
            {
                IntPtr applicationsTable = OpenVrInterop.GetInterface(
                    runtimePath,
                    ApplicationsInterfaceVersion);

                AddApplicationManifest addManifest = GetTableFunction<AddApplicationManifest>(applicationsTable, 0);
                RemoveApplicationManifest removeManifest = GetTableFunction<RemoveApplicationManifest>(applicationsTable, 1);
                IdentifyApplication identifyApplication = GetTableFunction<IdentifyApplication>(applicationsTable, 11);
                SetApplicationAutoLaunch setAutoLaunch = GetTableFunction<SetApplicationAutoLaunch>(applicationsTable, 17);
                string fullManifestPath = Path.GetFullPath(manifestPath);

                if (!enabled)
                {
                    EVRApplicationError disableError = setAutoLaunch(ApplicationKey, false);
                    if (disableError != EVRApplicationError.None &&
                        disableError != EVRApplicationError.UnknownApplication)
                    {
                        throw new InvalidOperationException("SteamVR 自动启动设置失败，错误码：" + (int)disableError);
                    }

                    // SteamVR may terminate any process whose executable belongs to a
                    // registered dashboard-overlay manifest when the session ends,
                    // even if that application's auto-launch flag is disabled. Remove
                    // the manifest entirely so a manually opened TrackSwap process is
                    // outside SteamVR's lifecycle ownership.
                    EVRApplicationError removeError = removeManifest(fullManifestPath);
                    if (removeError != EVRApplicationError.None &&
                        removeError != EVRApplicationError.UnknownApplication)
                    {
                        throw new InvalidOperationException("SteamVR 应用清单注销失败，错误码：" + (int)removeError);
                    }
                    return disableError == EVRApplicationError.None;
                }

                EVRApplicationError addError = addManifest(fullManifestPath, false);
                if (addError != EVRApplicationError.None &&
                    addError != EVRApplicationError.AppKeyAlreadyExists)
                {
                    throw new InvalidOperationException("SteamVR 应用清单注册失败，错误码：" + (int)addError);
                }

                identifyApplication(
                    unchecked((uint)System.Diagnostics.Process.GetCurrentProcess().Id),
                    ApplicationKey);
                EVRApplicationError launchError = setAutoLaunch(ApplicationKey, true);
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError AddApplicationManifest(
            string applicationManifestFullPath,
            [MarshalAs(UnmanagedType.I1)] bool temporary);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError RemoveApplicationManifest(
            string applicationManifestFullPath);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError SetApplicationAutoLaunch(
            string applicationKey,
            [MarshalAs(UnmanagedType.I1)] bool autoLaunch);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRApplicationError IdentifyApplication(uint processId, string applicationKey);

        private enum EVRApplicationError
        {
            None = 0,
            AppKeyAlreadyExists = 100,
            UnknownApplication = 104
        }
    }
}
