using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class SteamVrStaticMappingService
{
    private readonly Func<string?> settingsPathProvider;
    private readonly Func<bool> steamVrRunning;

    public SteamVrStaticMappingService(
        Func<string?>? settingsPathProvider = null,
        Func<bool>? steamVrRunning = null)
    {
        this.settingsPathProvider = settingsPathProvider ?? FindSettingsPath;
        this.steamVrRunning = steamVrRunning ?? RuntimeLifecycleMonitor.IsSteamVrRunning;
    }

    public StaticMappingReconciliationResult Reconcile(RuntimeConfiguration configuration)
    {
        if (steamVrRunning())
        {
            return StaticMappingReconciliationResult.Deferred;
        }

        string? settingsPath = settingsPathProvider();
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            bool requiresSettingsFile = configuration.Routes.Any(route =>
                route.PendingDeletion || (route.Enabled && route.Mode == RouteMode.ReplaceTarget));
            if (!requiresSettingsFile)
            {
                return StaticMappingReconciliationResult.Completed;
            }
            throw new FileNotFoundException("找不到 steamvr.vrsettings，无法整理 TrackSwap 静态映射。", settingsPath);
        }

        JObject root = JObject.Parse(File.ReadAllText(settingsPath));
        JObject original = (JObject)root.DeepClone();
        JObject? overrides = root["TrackingOverrides"] as JObject;
        List<RouteConfiguration> pendingDeletions = configuration.Routes
            .Where(route => route.PendingDeletion)
            .ToList();

        if (pendingDeletions.Count != 0)
        {
            if (overrides != null)
            {
                var deletingSources = new HashSet<string>(
                    pendingDeletions.Select(route => ProtocolConstants.GetProxyDevicePath(route.VirtualDeviceSlot)),
                    StringComparer.Ordinal);
                foreach (JProperty property in overrides.Properties()
                    .Where(property => deletingSources.Contains(property.Name))
                    .ToList())
                {
                    property.Remove();
                }
            }

            WriteIfChanged(settingsPath, original, root);
            return new StaticMappingReconciliationResult(
                completed: false,
                pendingDeletions.Select(route => route.RouteId).ToArray());
        }

        var managedSources = new HashSet<string>(
            Enumerable.Range(0, ProtocolConstants.MaximumRoutes)
                .Select(ProtocolConstants.GetProxyDevicePath),
            StringComparer.Ordinal);
        if (overrides != null)
        {
            foreach (JProperty property in overrides.Properties()
                .Where(property => managedSources.Contains(property.Name))
                .ToList())
            {
                property.Remove();
            }
        }

        IReadOnlyDictionary<string, string> desiredMappings = configuration.Routes
            .Where(route => route.Enabled && route.Mode == RouteMode.ReplaceTarget)
            .ToDictionary(
                route => ProtocolConstants.GetProxyDevicePath(route.VirtualDeviceSlot),
                route => route.TargetDevicePath,
                StringComparer.Ordinal);
        if (desiredMappings.Count != 0 && overrides == null)
        {
            overrides = new JObject();
            root["TrackingOverrides"] = overrides;
        }

        foreach (KeyValuePair<string, string> desired in desiredMappings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string? validationError = ValidateOverride(root, desired.Key, desired.Value);
            if (validationError != null)
            {
                throw new InvalidOperationException(validationError);
            }

            foreach (JProperty property in overrides!.Properties().ToList())
            {
                bool sameSource = string.Equals(property.Name, desired.Key, StringComparison.Ordinal);
                bool sameTarget = string.Equals((string?)property.Value, desired.Value, StringComparison.Ordinal);
                if (sameSource || sameTarget)
                {
                    property.Remove();
                }
            }
            overrides[desired.Key] = desired.Value;
        }

        WriteIfChanged(settingsPath, original, root);
        return StaticMappingReconciliationResult.Completed;
    }

    private static string? FindSettingsPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string registryPath = Path.Combine(localAppData, "openvr", "openvrpaths.vrpath");
        if (!File.Exists(registryPath))
        {
            return null;
        }

        JObject root = JObject.Parse(File.ReadAllText(registryPath));
        string? configDirectory = (root["config"] as JArray)?
            .Values<string>()
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        return string.IsNullOrWhiteSpace(configDirectory)
            ? null
            : Path.GetFullPath(Path.Combine(
                configDirectory.Replace('/', Path.DirectorySeparatorChar),
                "steamvr.vrsettings"));
    }

    private static string? ValidateOverride(JObject root, string sourcePath, string targetPath)
    {
        if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
        {
            return "追踪来源和替换目标不能是同一个设备。";
        }

        JObject? overrides = root["TrackingOverrides"] as JObject;
        var edges = overrides == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : overrides.Properties().ToDictionary(
                property => property.Name,
                property => (string?)property.Value ?? string.Empty,
                StringComparer.Ordinal);
        foreach (string key in edges
            .Where(pair => string.Equals(pair.Key, sourcePath, StringComparison.Ordinal) ||
                string.Equals(pair.Value, targetPath, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList())
        {
            edges.Remove(key);
        }

        edges[sourcePath] = targetPath;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = sourcePath;
        while (edges.TryGetValue(current, out string? next))
        {
            if (!visited.Add(current))
            {
                return "该映射会形成循环覆盖，请选择其他来源或目标。";
            }
            current = next;
        }
        return null;
    }

    private static void WriteIfChanged(string settingsPath, JObject original, JObject updated)
    {
        if (JToken.DeepEquals(original, updated))
        {
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        string backupPath = settingsPath + ".trackswap-" + timestamp + ".backup";
        string temporaryPath = settingsPath + ".trackswap.tmp";
        int duplicateIndex = 1;
        while (File.Exists(backupPath))
        {
            backupPath = settingsPath + ".trackswap-" + timestamp + "-" + duplicateIndex + ".backup";
            duplicateIndex++;
        }

        File.Copy(settingsPath, backupPath, overwrite: false);
        try
        {
            File.WriteAllText(
                temporaryPath,
                updated.ToString(Formatting.Indented) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Replace(temporaryPath, settingsPath, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed class StaticMappingReconciliationResult
{
    public static StaticMappingReconciliationResult Completed { get; } =
        new(completed: true, Array.Empty<string>());
    public static StaticMappingReconciliationResult Deferred { get; } =
        new(completed: false, Array.Empty<string>());

    public StaticMappingReconciliationResult(bool completed, IReadOnlyList<string> deletedRouteIds)
    {
        IsCompleted = completed;
        DeletedRouteIds = deletedRouteIds;
    }

    public bool IsCompleted { get; }
    public IReadOnlyList<string> DeletedRouteIds { get; }
}
