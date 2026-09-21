using System;
using System.IO;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    public sealed class UiPreferencesService
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly string _filePath;

        public UiPreferencesService(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrackSwap",
                "ui-preferences.json");
        }

        public UiPreferences Load()
        {
            if (!File.Exists(_filePath))
            {
                return new UiPreferences();
            }

            try
            {
                return JsonConvert.DeserializeObject<UiPreferences>(
                    File.ReadAllText(_filePath),
                    JsonSettings) ?? new UiPreferences();
            }
            catch (JsonException)
            {
                return new UiPreferences();
            }
            catch (IOException)
            {
                return new UiPreferences();
            }
        }

        public void Save(UiPreferences preferences)
        {
            string directory = Path.GetDirectoryName(_filePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(preferences, JsonSettings));
            if (File.Exists(_filePath))
            {
                File.Replace(temporaryPath, _filePath, null);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }
    }

    public sealed class UiPreferences
    {
        public bool ShowSteamVrRoleTargets { get; set; }

        public bool HideSourceInPreview { get; set; }

        public bool HideTargetInPreview { get; set; }

        public bool ShowProxyInPreview { get; set; }

        public RuntimeLifecycleMode RuntimeLifecycleMode { get; set; } = RuntimeLifecycleMode.FollowTrackSwap;

        public bool FollowSteamVrWithTrackSwap { get; set; }
    }
}
