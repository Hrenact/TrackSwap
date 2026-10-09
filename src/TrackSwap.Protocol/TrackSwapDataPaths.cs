using System;
using System.IO;
using System.Linq;

namespace TrackSwap.Protocol
{
    public static class TrackSwapDataPaths
    {
        public const string DataDirectoryName = "UserData";

        private static readonly string[] RecognizedDataFiles =
        {
            "runtime-config.json",
            "runtime-config.json.previous",
            "calibration-profiles.json",
            "calibration-profiles.json.previous",
            "device-history.json",
            "ui-preferences.json",
            "runtime-ui-launch.log",
            "runtime-events.log",
            "runtime-events.previous.log",
            "ui-lifecycle.log"
        };

        private static readonly string[] AuthoritativeDataFiles =
        {
            "runtime-config.json",
            "runtime-config.json.previous",
            "calibration-profiles.json",
            "calibration-profiles.json.previous",
            "ui-preferences.json"
        };

        private static readonly object ActiveDataDirectoryLock = new object();
        private static string? resolvedAutomaticDataDirectory;

        public static string ApplicationRootDirectory => ResolveApplicationRootDirectory();

        public static string PreferredDataDirectory => Path.Combine(
            ApplicationRootDirectory,
            DataDirectoryName);

        public static string LegacyDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrackSwap");

        public static string ActiveDataDirectory
        {
            get
            {
                string overridePath = Environment.GetEnvironmentVariable("TRACKSWAP_DATA_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(overridePath))
                {
                    return Path.GetFullPath(overridePath);
                }
                lock (ActiveDataDirectoryLock)
                {
                    return resolvedAutomaticDataDirectory ??=
                        ResolveAutomaticDataDirectory(PreferredDataDirectory, LegacyDataDirectory);
                }
            }
        }

        public static bool IsUsingLegacyData => PathsEqual(
            ActiveDataDirectory,
            LegacyDataDirectory);

        public static bool CanUsePreferredDataDirectory => CanWriteDirectory(
            PreferredDataDirectory);

        public static bool ContainsRecognizedData(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            return RecognizedDataFiles.Any(fileName =>
                File.Exists(Path.Combine(directory, fileName)));
        }

        public static string[] GetRecognizedDataFiles()
        {
            return (string[])RecognizedDataFiles.Clone();
        }

        internal static string ResolveAutomaticDataDirectory(string preferred, string legacy)
        {
            bool legacyHasData = !PathsEqual(preferred, legacy) && ContainsRecognizedData(legacy);
            // Logs and device history can be created before migration is complete. They must
            // not make a later process abandon an authoritative legacy configuration.
            if (legacyHasData && !ContainsAny(preferred, AuthoritativeDataFiles))
            {
                return legacy;
            }
            if (ContainsRecognizedData(preferred))
            {
                return preferred;
            }
            if (legacyHasData)
            {
                return legacy;
            }
            return CanWriteDirectory(preferred) ? preferred : legacy;
        }

        private static bool ContainsAny(string directory, string[] fileNames)
        {
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                fileNames.Any(fileName => File.Exists(Path.Combine(directory, fileName)));
        }

        public static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveApplicationRootDirectory()
        {
            string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            DirectoryInfo current = new DirectoryInfo(baseDirectory);

            if (string.Equals(current.Name, "runtime", StringComparison.OrdinalIgnoreCase) &&
                current.Parent != null &&
                File.Exists(Path.Combine(current.Parent.FullName, "TrackSwap.exe")))
            {
                return current.Parent.FullName;
            }

            if (File.Exists(Path.Combine(baseDirectory, "TrackSwap.exe")) ||
                File.Exists(Path.Combine(baseDirectory, "TrackSwap.vrmanifest")))
            {
                return baseDirectory;
            }

            for (DirectoryInfo? candidate = current; candidate != null; candidate = candidate.Parent)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "TrackSwap.sln")))
                {
                    return candidate.FullName;
                }
            }

            return string.Equals(current.Name, "runtime", StringComparison.OrdinalIgnoreCase) &&
                current.Parent != null
                ? current.Parent.FullName
                : baseDirectory;
        }

        private static bool CanWriteDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".trackswap-write-test-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe))
                {
                }
                File.Delete(probe);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException ||
                exception is NotSupportedException)
            {
                return false;
            }
        }
    }
}
