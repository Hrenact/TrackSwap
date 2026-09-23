using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class ConfigurationStore
{
    public ConfigurationStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public RuntimeConfiguration Load()
    {
        if (!File.Exists(Path))
        {
            return new RuntimeConfiguration();
        }

        JObject root = JObject.Parse(File.ReadAllText(Path));
        bool removedLegacyOscAddresses = false;
        if (root.GetValue("osc", StringComparison.OrdinalIgnoreCase) is JObject osc)
        {
            removedLegacyOscAddresses |= RemoveProperty(osc, "left");
            removedLegacyOscAddresses |= RemoveProperty(osc, "right");
        }
        bool addedXInputTouchAssistDefaults = EnsureXInputTouchAssistDefaults(root);
        RuntimeConfiguration? configuration = root.ToObject<RuntimeConfiguration>(
            JsonSerializer.Create(RuntimeJson.Settings));
        EnsureValid(configuration);
        bool expectedOscEnabled = OscConfiguration.IsRequiredForRoutes(configuration!.Routes);
        bool normalizedOscEnabled = configuration.Osc.Enabled != expectedOscEnabled;
        configuration.Osc.Enabled = expectedOscEnabled;
        if (removedLegacyOscAddresses || addedXInputTouchAssistDefaults || normalizedOscEnabled)
        {
            Save(configuration);
        }
        return configuration;
    }

    public void Save(RuntimeConfiguration configuration)
    {
        EnsureValid(configuration);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Runtime 配置路径没有上级目录。");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string json = JsonConvert.SerializeObject(configuration, RuntimeJson.Settings);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(Path))
            {
                File.Replace(temporaryPath, Path, Path + ".previous", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, Path);
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

    private static void EnsureValid(RuntimeConfiguration? configuration)
    {
        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }

    private static bool RemoveProperty(JObject value, string propertyName)
    {
        JProperty? property = value.Properties().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property == null)
        {
            return false;
        }
        property.Remove();
        return true;
    }

    private static bool EnsureXInputTouchAssistDefaults(JObject root)
    {
        if (root.GetValue("xInput", StringComparison.OrdinalIgnoreCase) is not JObject xInput)
        {
            return false;
        }

        JsonSerializer serializer = JsonSerializer.Create(RuntimeJson.Settings);
        bool changed = false;
        changed |= EnsureXInputHandDefaults(
            xInput,
            "left",
            XInputConfiguration.CreateDefaultLeftMapping(),
            serializer);
        changed |= EnsureXInputHandDefaults(
            xInput,
            "right",
            XInputConfiguration.CreateDefaultRightMapping(),
            serializer);
        return changed;
    }

    private static bool EnsureXInputHandDefaults(
        JObject xInput,
        string propertyName,
        XInputHandMapping defaults,
        JsonSerializer serializer)
    {
        JToken? handToken = xInput.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
        if (handToken == null)
        {
            xInput[propertyName] = JToken.FromObject(defaults, serializer);
            return true;
        }
        if (handToken is not JObject hand)
        {
            return false;
        }

        bool changed = false;
        if (hand.GetValue("thumbTouch", StringComparison.OrdinalIgnoreCase) == null)
        {
            hand["thumbTouch"] = JToken.FromObject(defaults.ThumbTouch, serializer);
            changed = true;
        }
        if (hand.GetValue("indexTouch", StringComparison.OrdinalIgnoreCase) == null)
        {
            hand["indexTouch"] = JToken.FromObject(defaults.IndexTouch, serializer);
            changed = true;
        }
        return changed;
    }

}
