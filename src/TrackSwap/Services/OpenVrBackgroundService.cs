using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using TrackSwap.Models;

namespace TrackSwap.Services
{
    internal sealed class OpenVrBackgroundService
    {
        internal const string DefaultBackgroundResource = "backgrounds/aurorasky.png";
        internal const string SettingsInterfaceVersion = "FnTable:IVRSettings_003";
        internal const int GetStringIndex = 8;

        private const string SteamVrSection = "steamvr";
        private const string BackgroundKey = "background";
        internal OpenVrBackgroundState Capture(string runtimePath)
        {
            IntPtr table = OpenVrInterop.GetInterface(runtimePath, SettingsInterfaceVersion);
            GetString getString = GetTableFunction<GetString>(table, GetStringIndex);

            string settingValue = ReadString(getString, SteamVrSection, BackgroundKey);
            var state = new OpenVrBackgroundState
            {
                SettingValue = settingValue
            };

            if (TryParseSteamVrColor(settingValue, out Color color))
            {
                state.SolidColor = color.ToString();
            }
            else
            {
                state.ImagePath = ResolveBackgroundImagePath(runtimePath, settingValue);
            }

            return state;
        }

        internal static string ResolveBackgroundImagePath(
            string runtimePath,
            string settingValue)
        {
            string candidate = string.IsNullOrWhiteSpace(settingValue)
                ? DefaultBackgroundResource
                : settingValue.Trim();
            if (candidate.StartsWith("#", StringComparison.Ordinal))
            {
                return null;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) && uri.IsFile)
            {
                candidate = uri.LocalPath;
            }

            if (Path.IsPathRooted(candidate))
            {
                string absolute = Path.GetFullPath(candidate);
                return File.Exists(absolute) ? absolute : null;
            }

            string resourcesPath = Path.GetFullPath(
                Path.Combine(runtimePath, "resources", candidate.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(resourcesPath) ? resourcesPath : null;
        }

        internal static bool TryParseSteamVrColor(string value, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length != 9 ||
                value[0] != '#')
            {
                return false;
            }

            if (!uint.TryParse(
                value.Substring(1),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out uint rgba))
            {
                return false;
            }

            color = Color.FromArgb(
                (byte)rgba,
                (byte)(rgba >> 24),
                (byte)(rgba >> 16),
                (byte)(rgba >> 8));
            return true;
        }

        private static string ReadString(
            GetString getter,
            string section,
            string key)
        {
            var value = new StringBuilder(4096);
            ESettingsError error = ESettingsError.None;
            getter(section, key, value, (uint)value.Capacity, ref error);
            return error == ESettingsError.None ? value.ToString() : string.Empty;
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException("OpenVR settings function table is incomplete.");
            }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate void GetString(
            string section,
            string key,
            StringBuilder value,
            uint valueCapacity,
            ref ESettingsError error);

        private enum ESettingsError
        {
            None = 0
        }
    }
}
