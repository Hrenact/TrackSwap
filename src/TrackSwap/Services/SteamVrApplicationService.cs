using System;
using TrackSwap.Localization;
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
                throw new InvalidOperationException(Tr.Get("steamvr.error.runtime_path_not_found"));
            }
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                throw new FileNotFoundException(Tr.Get("service.steam_vr_application.set_auto_launch.not_found_trackswap_steamvr_app_manifest"), manifestPath);
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
                        throw new InvalidOperationException(Tr.Format("service.steam_vr_application.set_auto_launch.steamvr_auto_start_settings_failed_error_code", (int)disableError));
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
                        throw new InvalidOperationException(Tr.Format("service.steam_vr_application.set_auto_launch.steamvr_app_manifest_unregister_failed_error_code", (int)removeError));
                    }
                    return disableError == EVRApplicationError.None;
                }

                EVRApplicationError addError = addManifest(fullManifestPath, false);
                if (addError != EVRApplicationError.None &&
                    addError != EVRApplicationError.AppKeyAlreadyExists)
                {
                    throw new InvalidOperationException(Tr.Format("service.steam_vr_application.set_auto_launch.steamvr_app_manifest_register_failed_error_code", (int)addError));
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
                    throw new InvalidOperationException(Tr.Format("service.steam_vr_application.set_auto_launch.steamvr_auto_start_settings_failed_error_code", (int)launchError));
                }
                return true;
            }
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException(Tr.Get("service.steam_vr_application.set_auto_launch.openvr_applications_complete"));
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
