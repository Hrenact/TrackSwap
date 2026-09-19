using Newtonsoft.Json;
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

        RuntimeConfiguration? configuration = JsonConvert.DeserializeObject<RuntimeConfiguration>(
            File.ReadAllText(Path),
            RuntimeJson.Settings);
        EnsureValid(configuration);
        return configuration!;
    }

    public void Save(RuntimeConfiguration configuration)
    {
        EnsureValid(configuration);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Runtime configuration path has no parent directory.");
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
}
