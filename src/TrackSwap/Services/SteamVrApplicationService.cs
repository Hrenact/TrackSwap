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
