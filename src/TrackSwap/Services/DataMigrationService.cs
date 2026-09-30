using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class DataMigrationService
    {
        private readonly string _legacyDirectory;
        private readonly string _preferredDirectory;
        private readonly Func<string> _activeDirectoryProvider;
        private readonly Func<bool> _preferredWritableProvider;

        public DataMigrationService()
            : this(
                TrackSwapDataPaths.LegacyDataDirectory,
                TrackSwapDataPaths.PreferredDataDirectory,
                () => TrackSwapDataPaths.ActiveDataDirectory,
                () => TrackSwapDataPaths.CanUsePreferredDataDirectory)
        {
        }

        internal DataMigrationService(
            string legacyDirectory,
            string preferredDirectory,
            Func<string> activeDirectoryProvider,
            Func<bool> preferredWritableProvider)
        {
            _legacyDirectory = Path.GetFullPath(legacyDirectory);
            _preferredDirectory = Path.GetFullPath(preferredDirectory);
            _activeDirectoryProvider = activeDirectoryProvider ??
                throw new ArgumentNullException(nameof(activeDirectoryProvider));
            _preferredWritableProvider = preferredWritableProvider ??
                throw new ArgumentNullException(nameof(preferredWritableProvider));
        }

        public string LegacyDirectory => _legacyDirectory;

        public string PreferredDirectory => _preferredDirectory;

        public string ActiveDirectory => Path.GetFullPath(_activeDirectoryProvider());

        public bool CanMigrate =>
            TrackSwapDataPaths.PathsEqual(ActiveDirectory, LegacyDirectory) &&
            TrackSwapDataPaths.ContainsRecognizedData(LegacyDirectory) &&
            _preferredWritableProvider() &&
            !TrackSwapDataPaths.PathsEqual(LegacyDirectory, PreferredDirectory);

        public bool HasLegacyData => TrackSwapDataPaths.ContainsRecognizedData(LegacyDirectory);

        public MigrationResult Migrate(bool replaceExisting)
        {
            if (!TrackSwapDataPaths.ContainsRecognizedData(LegacyDirectory))
            {
                throw new InvalidOperationException(Tr.Get("service.data_migration.migrate.migration_trackswap"));
            }
            if (TrackSwapDataPaths.PathsEqual(LegacyDirectory, PreferredDirectory))
            {
                throw new InvalidOperationException(Tr.Get("service.data_migration.migrate.current_install_directory"));
            }
            if (!_preferredWritableProvider())
            {
                throw new UnauthorizedAccessException(Tr.Get("service.data_migration.migrate.trackswap_install_directory_cannot_migration"));
            }

            ValidateRuntimeConfigurationIfPresent(LegacyDirectory);
            string target = PreferredDirectory;
            bool targetHasData = TrackSwapDataPaths.ContainsRecognizedData(target);
            if (targetHasData && !replaceExisting)
            {
                throw new InvalidOperationException(Tr.Get("service.data_migration.migrate.install_directory_trackswap"));
            }

            string parent = Path.GetDirectoryName(target)
                ?? throw new InvalidDataException(Tr.Get("service.data_migration.migrate.target_data_directory_up_directory"));
            Directory.CreateDirectory(parent);
            string staging = target + ".migration-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            string preserved = Path.Combine(
                staging,
                "Backups",
                "PreMigrationFiles-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            bool targetMovedIntoStaging = false;
            try
            {
                foreach (string sourcePath in EnumerateOwnedFiles(LegacyDirectory))
                {
                    File.Copy(sourcePath, Path.Combine(staging, Path.GetFileName(sourcePath)), true);
                }
                foreach (string directoryName in new[] { "Backups", "Logs", "i18n" })
                {
                    string sourceDirectory = Path.Combine(LegacyDirectory, directoryName);
                    if (Directory.Exists(sourceDirectory))
                    {
                        CopyDirectory(sourceDirectory, Path.Combine(staging, directoryName));
                    }
                }
                ValidateRuntimeConfigurationIfPresent(staging);

                if (Directory.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(preserved));
                    Directory.Move(target, preserved);
                    targetMovedIntoStaging = true;
                }
                Directory.Move(staging, target);
                return new MigrationResult(
                    target,
                    Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Count());
            }
            catch
            {
                if (!Directory.Exists(target) && targetMovedIntoStaging && Directory.Exists(preserved))
                {
                    Directory.Move(preserved, target);
                }
                throw;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        public LegacyCleanupResult CleanLegacyData()
        {
            if (TrackSwapDataPaths.PathsEqual(ActiveDirectory, LegacyDirectory))
            {
                throw new InvalidOperationException(Tr.Get("service.data_migration.clean_legacy_data.trackswap_current_data_directory_cannot_cleanup"));
            }

            int removed = 0;
            foreach (string path in EnumerateOwnedFiles(LegacyDirectory).ToList())
            {
                File.Delete(path);
                removed++;
            }
            foreach (string path in EnumerateOwnedNestedFiles(LegacyDirectory).ToList())
            {
                File.Delete(path);
                removed++;
            }
            RemoveEmptyDirectories(Path.Combine(LegacyDirectory, "Backups"));
            RemoveEmptyDirectories(Path.Combine(LegacyDirectory, "Logs"));
            RemoveEmptyDirectories(Path.Combine(LegacyDirectory, "i18n"));

            bool directoryRemoved = false;
            if (Directory.Exists(LegacyDirectory) &&
                !Directory.EnumerateFileSystemEntries(LegacyDirectory).Any())
            {
                Directory.Delete(LegacyDirectory);
                directoryRemoved = true;
            }
            return new LegacyCleanupResult(removed, directoryRemoved);
        }

        private static IEnumerable<string> EnumerateOwnedFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            }

            var names = new HashSet<string>(
                TrackSwapDataPaths.GetRecognizedDataFiles(),
                StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    names.Contains(Path.GetFileName(path)) ||
                    Path.GetFileName(path).StartsWith(
                        "runtime-config.unexpected-empty-",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static IEnumerable<string> EnumerateOwnedNestedFiles(string directory)
        {
            string steamVrBackups = Path.Combine(directory, "Backups", "SteamVR");
            string automaticBackups = Path.Combine(directory, "Backups", "Automatic");
            string languagePacks = Path.Combine(directory, "i18n");
            return EnumerateFilesIfPresent(steamVrBackups, "steamvr.vrsettings.trackswap-*.backup")
                .Concat(EnumerateFilesIfPresent(steamVrBackups, "appconfig.json.trackswap-*.backup"))
                .Concat(EnumerateFilesIfPresent(automaticBackups, "TrackSwap-*.trackswap-backup"))
                .Concat(EnumerateFilesIfPresent(languagePacks, "*.json"))
                .ToList();
        }

        private static IEnumerable<string> EnumerateFilesIfPresent(string directory, string pattern)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                : Enumerable.Empty<string>();
        }

        private static void RemoveEmptyDirectories(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }
            foreach (string child in Directory.EnumerateDirectories(root).ToList())
            {
                RemoveEmptyDirectories(child);
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }

        private static void ValidateRuntimeConfigurationIfPresent(string directory)
        {
            string path = Path.Combine(directory, "runtime-config.json");
            if (!File.Exists(path))
            {
                return;
            }

            RuntimeConfiguration configuration;
            try
            {
                configuration = JsonConvert.DeserializeObject<RuntimeConfiguration>(File.ReadAllText(path));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(Tr.Get("service.data_migration.validate_runtime_configuration_if_present.runtime_config_cannot_read"), exception);
            }
            if (configuration == null)
            {
                throw new InvalidDataException(Tr.Get("service.data_migration.validate_runtime_configuration_if_present.runtime_config"));
            }

            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                File.Copy(
                    sourceFile,
                    Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)),
                    true);
            }
            foreach (string sourceChild in Directory.EnumerateDirectories(sourceDirectory))
            {
                CopyDirectory(
                    sourceChild,
                    Path.Combine(destinationDirectory, Path.GetFileName(sourceChild)));
            }
        }
    }

    internal sealed class MigrationResult
    {
        public MigrationResult(string targetDirectory, int fileCount)
        {
            TargetDirectory = targetDirectory;
            FileCount = fileCount;
        }

        public string TargetDirectory { get; }

        public int FileCount { get; }
    }

    internal sealed class LegacyCleanupResult
    {
        public LegacyCleanupResult(int removedFileCount, bool directoryRemoved)
        {
            RemovedFileCount = removedFileCount;
            DirectoryRemoved = directoryRemoved;
        }

        public int RemovedFileCount { get; }

        public bool DirectoryRemoved { get; }
    }
}
