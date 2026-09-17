using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrackSwap.Models;

namespace TrackSwap.Services
{
    public sealed class SteamVrSettingsService
    {
        public IReadOnlyList<DeviceOption> ReadKnownSources(string settingsPath)
        {
            JObject root = ReadRoot(settingsPath);
            var paths = new HashSet<string>(StringComparer.Ordinal);

            JObject trackers = root["trackers"] as JObject;
            if (trackers != null)
            {
                foreach (JProperty property in trackers.Properties())
                {
                    paths.Add(property.Name);
                }
            }

            JObject overrides = root["TrackingOverrides"] as JObject;
            if (overrides != null)
            {
                foreach (JProperty property in overrides.Properties())
                {
                    paths.Add(property.Name);
                }
            }

            return paths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new DeviceOption(BuildFriendlyName(path), path))
                .ToList();
        }

        public string ReadSourceForTarget(string settingsPath, string targetPath)
        {
            JObject root = ReadRoot(settingsPath);
            JObject overrides = root["TrackingOverrides"] as JObject;
            JProperty match = overrides?
                .Properties()
                .FirstOrDefault(property => string.Equals((string)property.Value, targetPath, StringComparison.Ordinal));
            return match?.Name;
        }

        public IReadOnlyList<TargetOption> ReadKnownDeviceTargets(string settingsPath)
        {
            JObject root = ReadRoot(settingsPath);
            JObject overrides = root["TrackingOverrides"] as JObject;
            if (overrides == null)
            {
                return Array.Empty<TargetOption>();
            }

            return overrides.Properties()
                .Select(property => (string)property.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path) && path.StartsWith("/devices/", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new TargetOption("已保存设备 · " + BuildFriendlyName(path), path))
                .ToList();
        }

        public IReadOnlyList<TrackingOverrideOption> ReadOverrides(string settingsPath)
        {
            JObject root = ReadRoot(settingsPath);
            JObject overrides = root["TrackingOverrides"] as JObject;
            if (overrides == null)
            {
                return Array.Empty<TrackingOverrideOption>();
            }

            return overrides.Properties()
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property =>
                {
                    string targetPath = (string)property.Value;
                    return new TrackingOverrideOption(
                        BuildFriendlyName(property.Name),
                        property.Name,
                        BuildTargetFriendlyName(targetPath),
                        targetPath);
                })
                .ToList();
        }

        public IReadOnlyList<BackupOption> ReadBackups(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                return Array.Empty<BackupOption>();
            }

            string fullSettingsPath = Path.GetFullPath(settingsPath);
            string directory = Path.GetDirectoryName(fullSettingsPath);
            string pattern = Path.GetFileName(fullSettingsPath) + ".trackswap-*.backup";
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return Array.Empty<BackupOption>();
            }

            var backups = new List<BackupOption>();
            foreach (string backupPath in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                var file = new FileInfo(backupPath);
                try
                {
                    JObject root = JObject.Parse(File.ReadAllText(backupPath));
                    JObject overrides = root["TrackingOverrides"] as JObject;
                    List<string> summaries = overrides == null
                        ? new List<string>()
                        : overrides.Properties()
                            .Take(3)
                            .Select(property => BuildFriendlyName(property.Name)
                                + "  →  " + BuildTargetFriendlyName((string)property.Value))
                            .ToList();
                    int count = overrides?.Properties().Count() ?? 0;
                    string summary = summaries.Count == 0
                        ? "备份中没有位姿覆盖规则"
                        : string.Join(Environment.NewLine, summaries)
                            + (count > summaries.Count ? Environment.NewLine + "另有 " + (count - summaries.Count) + " 条…" : string.Empty);

                    backups.Add(new BackupOption(
                        backupPath,
                        file.LastWriteTime,
                        file.Length,
                        count,
                        summary,
                        true,
                        null));
                }
                catch (Exception exception)
                {
                    backups.Add(new BackupOption(
                        backupPath,
                        file.LastWriteTime,
                        file.Length,
                        0,
                        "备份文件损坏或不是有效的 SteamVR 配置",
                        false,
                        exception.Message));
                }
            }

            return backups
                .OrderByDescending(backup => backup.BackupTime)
                .ToList();
        }

        public string RestoreTrackingOverrides(string settingsPath, string backupPath)
        {
            string fullSettingsPath = Path.GetFullPath(settingsPath);
            string fullBackupPath = Path.GetFullPath(backupPath);
            string settingsDirectory = Path.GetDirectoryName(fullSettingsPath);
            string backupDirectory = Path.GetDirectoryName(fullBackupPath);
            string expectedPrefix = Path.GetFileName(fullSettingsPath) + ".trackswap-";
            string backupFileName = Path.GetFileName(fullBackupPath);

            if (!string.Equals(settingsDirectory, backupDirectory, StringComparison.OrdinalIgnoreCase)
                || !backupFileName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                || !backupFileName.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("只能恢复当前配置目录中由 TrackSwap 创建的备份。");
            }

            JObject currentRoot = ReadRoot(fullSettingsPath);
            JObject backupRoot = ReadRoot(fullBackupPath);
            JToken backupOverrides = backupRoot["TrackingOverrides"];

            if (backupOverrides == null)
            {
                currentRoot.Remove("TrackingOverrides");
            }
            else if (backupOverrides.Type == JTokenType.Object)
            {
                currentRoot["TrackingOverrides"] = backupOverrides.DeepClone();
            }
            else
            {
                throw new InvalidOperationException("备份中的 TrackingOverrides 格式无效。");
            }

            return WriteWithBackup(fullSettingsPath, currentRoot);
        }

        public string ValidateOverride(string settingsPath, string sourcePath, string targetPath)
        {
            return ValidateOverride(ReadRoot(settingsPath), sourcePath, targetPath);
        }

        public string ApplyOverride(string settingsPath, string sourcePath, string targetPath)
        {
            JObject root = ReadRoot(settingsPath);
            string validationError = ValidateOverride(root, sourcePath, targetPath);
            if (validationError != null)
            {
                throw new InvalidOperationException(validationError);
            }

            JObject overrides = root["TrackingOverrides"] as JObject;
            if (overrides == null)
            {
                overrides = new JObject();
                root["TrackingOverrides"] = overrides;
            }

            foreach (JProperty property in overrides.Properties().ToList())
            {
                bool sameSource = string.Equals(property.Name, sourcePath, StringComparison.Ordinal);
                bool sameTarget = string.Equals((string)property.Value, targetPath, StringComparison.Ordinal);
                if (sameSource || sameTarget)
                {
                    property.Remove();
                }
            }

            overrides[sourcePath] = targetPath;

            return WriteWithBackup(settingsPath, root);
        }

        public string RemoveOverride(string settingsPath, string sourcePath)
        {
            JObject root = ReadRoot(settingsPath);
            JObject overrides = root["TrackingOverrides"] as JObject;
            JProperty match = overrides?
                .Properties()
                .FirstOrDefault(property => string.Equals(property.Name, sourcePath, StringComparison.Ordinal));

            if (match == null)
            {
                throw new InvalidOperationException("这条映射已不存在，请重新载入配置。");
            }

            match.Remove();
            return WriteWithBackup(settingsPath, root);
        }

        private static string ValidateOverride(JObject root, string sourcePath, string targetPath)
        {
            if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            {
                return "追踪来源和替换目标不能是同一个设备。";
            }

            JObject overrides = root["TrackingOverrides"] as JObject;
            var edges = overrides == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : overrides.Properties().ToDictionary(
                    property => property.Name,
                    property => (string)property.Value,
                    StringComparer.Ordinal);

            foreach (string key in edges
                .Where(pair => string.Equals(pair.Key, sourcePath, StringComparison.Ordinal)
                    || string.Equals(pair.Value, targetPath, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList())
            {
                edges.Remove(key);
            }

            edges[sourcePath] = targetPath;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = sourcePath;
            while (edges.TryGetValue(current, out string next))
            {
                if (!visited.Add(current))
                {
                    return "该映射会形成循环覆盖，请选择其他来源或目标。";
                }

                current = next;
            }

            return null;
        }

        private static string WriteWithBackup(string settingsPath, JObject root)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backupPath = settingsPath + ".trackswap-" + timestamp + ".backup";
            string temporaryPath = settingsPath + ".trackswap.tmp";

            int duplicateIndex = 1;
            while (File.Exists(backupPath))
            {
                backupPath = settingsPath + ".trackswap-" + timestamp + "-" + duplicateIndex + ".backup";
                duplicateIndex++;
            }

            File.Copy(settingsPath, backupPath, false);

            try
            {
                string output = root.ToString(Formatting.Indented) + Environment.NewLine;
                File.WriteAllText(temporaryPath, output, new UTF8Encoding(false));
                File.Replace(temporaryPath, settingsPath, null);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return backupPath;
        }

        private static JObject ReadRoot(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                throw new FileNotFoundException("找不到 steamvr.vrsettings。", settingsPath);
            }

            return JObject.Parse(File.ReadAllText(settingsPath));
        }

        private static string BuildFriendlyName(string path)
        {
            const string trackerMarker = "vive_tracker";
            int trackerIndex = path.IndexOf(trackerMarker, StringComparison.OrdinalIgnoreCase);
            if (trackerIndex >= 0)
            {
                string serial = path.Substring(trackerIndex + trackerMarker.Length);
                return "VIVE Tracker · " + serial;
            }

            int slash = path.LastIndexOf('/');
            return slash >= 0 && slash < path.Length - 1
                ? path.Substring(slash + 1)
                : path;
        }

        private static string BuildTargetFriendlyName(string path)
        {
            switch (path)
            {
                case "/user/hand/right":
                    return "右手";
                case "/user/hand/left":
                    return "左手";
                case "/user/head":
                    return "头显";
                default:
                    return BuildFriendlyName(path);
            }
        }
    }
}
