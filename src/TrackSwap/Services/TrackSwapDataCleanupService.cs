using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class TrackSwapDataCleanupService
    {
        private readonly string _activeDirectory;
        private readonly string _preferredDirectory;
        private readonly string _legacyDirectory;

        public TrackSwapDataCleanupService()
            : this(
                TrackSwapDataPaths.ActiveDataDirectory,
                TrackSwapDataPaths.PreferredDataDirectory,
                TrackSwapDataPaths.LegacyDataDirectory)
        {
        }

        internal TrackSwapDataCleanupService(
            string activeDirectory,
            string preferredDirectory,
            string legacyDirectory)
        {
            _activeDirectory = Path.GetFullPath(activeDirectory);
            _preferredDirectory = Path.GetFullPath(preferredDirectory);
            _legacyDirectory = Path.GetFullPath(legacyDirectory);
        }

        public DataCleanupResult Clean()
        {
            int removedFiles = 0;
            int removedDirectories = 0;
            foreach (string directory in new[]
            {
                _activeDirectory,
                _preferredDirectory,
                _legacyDirectory
            }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                CleanDirectory(directory, ref removedFiles, ref removedDirectories);
            }
            return new DataCleanupResult(removedFiles, removedDirectories);
        }

        private static void CleanDirectory(
            string directory,
            ref int removedFiles,
            ref int removedDirectories)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var recognizedNames = new HashSet<string>(
                TrackSwapDataPaths.GetRecognizedDataFiles(),
                StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToList())
            {
                string name = Path.GetFileName(file);
                if (!recognizedNames.Contains(name) &&
                    !name.StartsWith("runtime-config.unexpected-empty-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                File.Delete(file);
                removedFiles++;
            }

            foreach (string ownedDirectoryName in new[] { "Backups", "Logs" })
            {
                string ownedDirectory = Path.Combine(directory, ownedDirectoryName);
                if (!Directory.Exists(ownedDirectory))
                {
                    continue;
                }
                removedFiles += Directory.EnumerateFiles(
                    ownedDirectory,
                    "*",
                    SearchOption.AllDirectories).Count();
                Directory.Delete(ownedDirectory, true);
                removedDirectories++;
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                removedDirectories++;
            }
        }
    }

    internal sealed class DataCleanupResult
    {
        public DataCleanupResult(int removedFileCount, int removedDirectoryCount)
        {
            RemovedFileCount = removedFileCount;
            RemovedDirectoryCount = removedDirectoryCount;
        }

        public int RemovedFileCount { get; }

        public int RemovedDirectoryCount { get; }
    }
}
