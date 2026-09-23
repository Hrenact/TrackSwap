using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    public void MissingXInputConfigurationUsesDefaultMapping()
    {
        RuntimeConfiguration configuration = JsonConvert.DeserializeObject<RuntimeConfiguration>("{}")!;

        Assert.Equal(0.5f, configuration.XInput.AnalogPressThreshold);
        Assert.Equal(XInputHapticMode.PreserveHandedness, configuration.XInput.HapticMode);
        Assert.Equal(XInputBindingSource.X, configuration.XInput.Left.PrimaryButton);
        Assert.Equal(XInputBindingSource.A, configuration.XInput.Right.PrimaryButton);
        Assert.Equal(XInputBindingSource.LeftTrigger, configuration.XInput.Left.Grip);
        Assert.Equal(XInputBindingSource.RightTrigger, configuration.XInput.Right.Grip);
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
            JObject persistedRoot = JObject.Parse(persisted);
            JObject persistedOsc = Assert.IsType<JObject>(persistedRoot["osc"]);
            Assert.Null(persistedOsc["left"]);
            Assert.Null(persistedOsc["right"]);
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

    [Fact]
    public void LoadAddsTouchAssistDefaultsToOlderXInputConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "trackswap-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "runtime.json");
        Directory.CreateDirectory(directory);
        try
        {
            JObject root = JObject.FromObject(
                new RuntimeConfiguration(),
                JsonSerializer.Create(RuntimeJson.Settings));
            JObject xInput = Assert.IsType<JObject>(root["xInput"]);
            Assert.True(Assert.IsType<JObject>(xInput["left"]).Remove("thumbTouch"));
            Assert.True(Assert.IsType<JObject>(xInput["left"]).Remove("indexTouch"));
            Assert.True(Assert.IsType<JObject>(xInput["right"]).Remove("thumbTouch"));
            Assert.True(Assert.IsType<JObject>(xInput["right"]).Remove("indexTouch"));
            File.WriteAllText(path, root.ToString(Formatting.None));

            RuntimeConfiguration loaded = new ConfigurationStore(path).Load();

            Assert.Equal(XInputBindingSource.DPadUp, loaded.XInput.Left.ThumbTouch.ToggleSource);
            Assert.Equal(XInputBindingSource.DPadLeft, loaded.XInput.Left.IndexTouch.ToggleSource);
            Assert.Equal(XInputBindingSource.DPadDown, loaded.XInput.Right.ThumbTouch.ToggleSource);
            Assert.Equal(XInputBindingSource.DPadRight, loaded.XInput.Right.IndexTouch.ToggleSource);
            JObject persisted = JObject.Parse(File.ReadAllText(path));
            Assert.NotNull(persisted["xInput"]?["left"]?["thumbTouch"]);
            Assert.NotNull(persisted["xInput"]?["right"]?["indexTouch"]);
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
