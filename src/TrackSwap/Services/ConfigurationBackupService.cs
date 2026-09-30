using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class ConfigurationBackupService
    {
        private const int CurrentBackupSchemaVersion = 1;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public void Export(
            string destinationPath,
            RuntimeConfiguration configuration,
            UiPreferences preferences)
        {
            if (configuration == null)
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.export.runtime_read_config"));
            }

            EnsureValid(configuration);
            var backup = new ConfigurationBackupFile
            {
                SchemaVersion = CurrentBackupSchemaVersion,
                ProductVersion = GetProductVersion(),
                CreatedAtUtc = DateTime.UtcNow,
                RuntimeConfiguration = configuration,
                UiPreferences = preferences ?? new UiPreferences()
            };
            WriteAtomic(destinationPath, JsonConvert.SerializeObject(backup, JsonSettings));
        }

        public ConfigurationBackupFile Read(string sourcePath)
        {
            var fileInfo = new FileInfo(sourcePath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(Tr.Get("service.configuration_backup.read.config_backup_file"), sourcePath);
            }
            if (fileInfo.Length <= 0 || fileInfo.Length > 4 * 1024 * 1024)
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.read.config_backup_file_4_mb"));
            }

            ConfigurationBackupFile backup;
            try
            {
                backup = JsonConvert.DeserializeObject<ConfigurationBackupFile>(
                    File.ReadAllText(sourcePath),
                    JsonSettings);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.read.config_backup_file_invalid"), exception);
            }

            if (backup == null || backup.SchemaVersion != CurrentBackupSchemaVersion)
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.read.config_backup_version"));
            }
            if (backup.RuntimeConfiguration == null)
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.read.config_backup_runtime_config"));
            }

            backup.UiPreferences = backup.UiPreferences ?? new UiPreferences();
            EnsureValid(backup.RuntimeConfiguration);
            if (!Enum.IsDefined(
                    typeof(RuntimeLifecycleMode),
                    backup.UiPreferences.RuntimeLifecycleMode))
            {
                throw new InvalidDataException(Tr.Get("service.configuration_backup.read.config_backup_invalid_runtime_lifecycle_mode"));
            }
            return backup;
        }

        public string CreateAutomaticRollback(
            RuntimeConfiguration configuration,
            UiPreferences preferences,
            string reason)
        {
            string directory = Path.Combine(
                TrackSwapDataPaths.ActiveDataDirectory,
                "Backups",
                "Automatic");
            Directory.CreateDirectory(directory);
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "rollback" : reason;
            string path = Path.Combine(
                directory,
                "TrackSwap-" + safeReason + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                ".trackswap-backup");
            Export(path, configuration, preferences);
            return path;
        }

        private static void EnsureValid(RuntimeConfiguration configuration)
        {
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }
        }

        private static void WriteAtomic(string destinationPath, string text)
        {
            string fullPath = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidDataException(Tr.Get("service.configuration_backup.write_atomic.backup_path_up_directory"));
            Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null, true);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string GetProductVersion()
        {
            Assembly assembly = typeof(ConfigurationBackupService).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? Tr.Get("common.status.unknown");
        }
    }

    internal sealed class ConfigurationBackupFile
    {
        public int SchemaVersion { get; set; }

        public string ProductVersion { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public RuntimeConfiguration RuntimeConfiguration { get; set; }

        public UiPreferences UiPreferences { get; set; }
    }
}
