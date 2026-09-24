using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TrackSwap.Services
{
    public sealed class SteamVrPathService
    {
        public string FindSettingsPath()
        {
            string configDirectory = FindFirstRegisteredPath("config");

            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                return null;
            }

            configDirectory = configDirectory.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(configDirectory, "steamvr.vrsettings"));
        }

        public string FindRuntimePath()
        {
            string runtimeDirectory = FindFirstRegisteredPath("runtime");
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return null;
            }

            return Path.GetFullPath(runtimeDirectory.Replace('/', Path.DirectorySeparatorChar));
        }

        public string FindLogPath()
        {
            string logDirectory = FindFirstRegisteredPath("log");
            return string.IsNullOrWhiteSpace(logDirectory)
                ? null
                : Path.GetFullPath(logDirectory.Replace('/', Path.DirectorySeparatorChar));
        }

        public string[] FindExternalDriverPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string registryPath = Path.Combine(localAppData, "openvr", "openvrpaths.vrpath");
            if (!File.Exists(registryPath))
            {
                return Array.Empty<string>();
            }

            JObject root = JObject.Parse(File.ReadAllText(registryPath));
            return (root["external_drivers"] as JArray)?
                .Values<string>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray() ?? Array.Empty<string>();
        }

        private static string FindFirstRegisteredPath(string propertyName)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string registryPath = Path.Combine(localAppData, "openvr", "openvrpaths.vrpath");

            if (!File.Exists(registryPath))
            {
                return null;
            }

            JObject root = JObject.Parse(File.ReadAllText(registryPath));
            JArray paths = root[propertyName] as JArray;
            return paths?
                .Values<string>()
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        }
    }
}
