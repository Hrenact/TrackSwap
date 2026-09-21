using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimePreferenceReader
{
    public static RuntimePreferences Read()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrackSwap",
            "ui-preferences.json");
        if (!File.Exists(path))
        {
            return new RuntimePreferences();
        }

        try
        {
            return JsonConvert.DeserializeObject<RuntimePreferences>(
                File.ReadAllText(path)) ?? new RuntimePreferences();
        }
        catch (JsonException)
        {
            return new RuntimePreferences();
        }
        catch (IOException)
        {
            return new RuntimePreferences();
        }
    }

    internal sealed class RuntimePreferences
    {
        public RuntimeLifecycleMode RuntimeLifecycleMode { get; set; } = RuntimeLifecycleMode.FollowTrackSwap;

        public bool FollowSteamVrWithTrackSwap { get; set; }
    }
}
