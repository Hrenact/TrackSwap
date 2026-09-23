using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public void MissingControllerPriorityUsesNeutralDefault()
    {
        RuntimeConfiguration configuration = JsonConvert.DeserializeObject<RuntimeConfiguration>("{}")!;

        Assert.Equal(
            ProtocolConstants.DefaultControllerHandSelectionPriority,
            configuration.ControllerHandSelectionPriority);
    }

    [Fact]
    public void LoadIgnoresLegacyOscAddressMappings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "trackswap-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "runtime.json");
        Directory.CreateDirectory(directory);
        try
        {
            RuntimeConfiguration configuration = new();
            string json = JsonConvert.SerializeObject(configuration, RuntimeJson.Settings);
            json = json.Replace(
                "\"resetTimeout\":5",
                "\"resetTimeout\":5,\"left\":{\"primaryButton\":\"/custom/left/a\"},\"right\":{\"primaryButton\":\"/custom/right/a\"}",
                StringComparison.Ordinal);
            Assert.Contains("/custom/", json, StringComparison.Ordinal);
            File.WriteAllText(path, json);

            RuntimeConfiguration loaded = new ConfigurationStore(path).Load();

            Assert.Equal("127.0.0.1", loaded.Osc.ListenAddress);
            new ConfigurationStore(path).Save(loaded);
            string persisted = File.ReadAllText(path);
            Assert.DoesNotContain("primaryButton", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("/custom/", persisted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
