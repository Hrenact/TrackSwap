using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class DiagnosticsService
    {
        private readonly SteamVrPathService _pathService;
        private readonly SteamVrStatusService _statusService;
        private readonly RuntimeControlService _runtimeControlService;
        private readonly string _dataDirectory;

        public DiagnosticsService(
            SteamVrPathService pathService,
            SteamVrStatusService statusService,
            RuntimeControlService runtimeControlService,
            string dataDirectory = null)
        {
            _pathService = pathService;
            _statusService = statusService;
            _runtimeControlService = runtimeControlService;
            _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? TrackSwapDataPaths.ActiveDataDirectory
                : dataDirectory;
        }

        public DiagnosticsReport Inspect(RuntimeStatusSnapshot runtimeStatus)
        {
            string steamVrPath = TryGet(_pathService.FindRuntimePath);
            string runtimeExecutable = _runtimeControlService.FindRuntimeExecutablePath();
            string driverPath = FindTrackSwapDriverPath();
            IReadOnlyList<string> configurationErrors = runtimeStatus?.Configuration == null
                ? Array.Empty<string>()
                : ConfigurationValidator.Validate(runtimeStatus.Configuration);

            return new DiagnosticsReport
            {
                UiVersion = GetUiVersion(),
                RuntimeProgramFound = File.Exists(runtimeExecutable),
                RuntimeProgram = File.Exists(runtimeExecutable) ? Tr.Get("common.status.found") : Tr.Get("service.diagnostics.inspect.not_found"),
                RuntimeProgramPath = runtimeExecutable,
                RuntimeVersion = GetFileVersion(runtimeExecutable),
                SteamVrInstalled = !string.IsNullOrWhiteSpace(steamVrPath),
                SteamVrRunning = !string.IsNullOrWhiteSpace(steamVrPath) && _statusService.IsRunning(),
                SteamVr = string.IsNullOrWhiteSpace(steamVrPath)
                    ? Tr.Get("service.diagnostics.inspect.path")
                    : _statusService.IsRunning() ? Tr.Get("common.status.running") : Tr.Get("service.diagnostics.inspect.not_running"),
                SteamVrPath = steamVrPath,
                SteamVrVersion = GetSteamVrVersion(steamVrPath),
                DriverRegistered = !string.IsNullOrWhiteSpace(driverPath),
                DriverRegistration = string.IsNullOrWhiteSpace(driverPath) ? Tr.Get("service.diagnostics.inspect.register") : Tr.Get("common.status.registered"),
                DriverPath = driverPath,
                DriverVersion = GetFileVersion(string.IsNullOrWhiteSpace(driverPath)
                    ? null
                    : Path.Combine(driverPath, "bin", "win64", "driver_trackswap.dll")),
                OperatingSystem = GetOperatingSystemDescription(),
                ApplicationManifestPresent = File.Exists(Path.Combine(
                    TrackSwapDataPaths.ApplicationRootDirectory,
                    "TrackSwap.vrmanifest")),
                Configuration = runtimeStatus?.Configuration == null
                    ? Tr.Get("service.diagnostics.inspect.runtime_read")
                    : configurationErrors.Count == 0 ? Tr.Get("common.status.passed") : Tr.Format("common.count.issues", configurationErrors.Count),
                ConfigurationErrors = configurationErrors
            };
        }

        public void Export(
            string destinationPath,
            RuntimeStatusSnapshot runtimeStatus,
            DiagnosticsExportContext context = null)
        {
            DiagnosticsReport report = Inspect(runtimeStatus);
            context = context ?? new DiagnosticsExportContext();
            var anonymizer = new DiagnosticAnonymizer(
                runtimeStatus?.Configuration,
                context.OnlineDevices);
            IReadOnlyList<KeyValuePair<string, string>> pathReplacements = BuildPathReplacements(report);
            JObject diagnostics = DiagnosticsDocumentBuilder.Build(
                report,
                runtimeStatus,
                context,
                anonymizer);
            string sanitizedJson = anonymizer.Sanitize(
                diagnostics.ToString(Newtonsoft.Json.Formatting.Indented),
                pathReplacements);
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "TrackSwap-diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                File.WriteAllText(
                    Path.Combine(temporaryDirectory, "summary.txt"),
                    anonymizer.Sanitize(
                        DiagnosticsDocumentBuilder.BuildSummary(diagnostics),
                        pathReplacements),
                    new UTF8Encoding(false));

                File.WriteAllText(
                    Path.Combine(temporaryDirectory, "diagnostics.json"),
                    sanitizedJson,
                    new UTF8Encoding(false));

                CopySanitizedLog(
                    Path.Combine(
                        _dataDirectory,
                        "runtime-ui-launch.log"),
                    Path.Combine(temporaryDirectory, "runtime-ui-launch.log"),
                    line => true,
                    500,
                    anonymizer,
                    pathReplacements);

                CopySanitizedLog(
                    Path.Combine(_dataDirectory, "ui-lifecycle.log"),
                    Path.Combine(temporaryDirectory, "ui-lifecycle.log"),
                    line => true,
                    500,
                    anonymizer,
                    pathReplacements);

                CopySanitizedLog(
                    Path.Combine(_dataDirectory, "runtime-events.log"),
                    Path.Combine(temporaryDirectory, "runtime-events.log"),
                    line => true,
                    1000,
                    anonymizer,
                    pathReplacements);

                CopySanitizedLog(
                    Path.Combine(_dataDirectory, "runtime-events.previous.log"),
                    Path.Combine(temporaryDirectory, "runtime-events.previous.log"),
                    line => true,
                    1000,
                    anonymizer,
                    pathReplacements);

                CopySanitizedLog(
                    FindSteamVrServerLog(),
                    Path.Combine(temporaryDirectory, "vrserver-trackswap.log"),
                    line => line.IndexOf("trackswap", StringComparison.OrdinalIgnoreCase) >= 0,
                    500,
                    anonymizer,
                    pathReplacements);

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                ZipFile.CreateFromDirectory(temporaryDirectory, destinationPath, CompressionLevel.Optimal, false);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private string FindTrackSwapDriverPath()
        {
            foreach (string path in TryGet(_pathService.FindExternalDriverPaths) ?? Array.Empty<string>())
            {
                try
                {
                    string manifestPath = Path.Combine(path, "driver.vrdrivermanifest");
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }
                    JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    if (string.Equals((string)manifest["name"], "trackswap", StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is Newtonsoft.Json.JsonException)
                {
                }
            }
            return null;
        }

        private static void CopySanitizedLog(
            string sourcePath,
            string destinationPath,
            Func<string, bool> predicate,
            int maximumLines,
            DiagnosticAnonymizer anonymizer,
            IReadOnlyList<KeyValuePair<string, string>> pathReplacements)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }
            try
            {
                var queue = new Queue<string>();
                foreach (string line in File.ReadLines(sourcePath).Where(predicate))
                {
                    if (queue.Count == maximumLines)
                    {
                        queue.Dequeue();
                    }
                    queue.Enqueue(anonymizer.Sanitize(line, pathReplacements));
                }
                if (queue.Count > 0)
                {
                    File.WriteAllLines(destinationPath, queue, new UTF8Encoding(false));
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
            }
        }

        private string FindSteamVrServerLog()
        {
            string logPath = TryGet(_pathService.FindLogPath);
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return null;
            }
            try
            {
                return Path.Combine(logPath, "vrserver.txt");
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                return null;
            }
        }

        private IReadOnlyList<KeyValuePair<string, string>> BuildPathReplacements(DiagnosticsReport report)
        {
            var replacements = new List<KeyValuePair<string, string>>();
            AddPathReplacement(replacements, AppDomain.CurrentDomain.BaseDirectory, "%TRACKSWAP_APP%");
            AddPathReplacement(replacements, _dataDirectory, "%TRACKSWAP_DATA%");
            AddPathReplacement(replacements, Path.GetDirectoryName(report.RuntimeProgramPath), "%TRACKSWAP_RUNTIME%");
            AddPathReplacement(replacements, report.DriverPath, "%TRACKSWAP_DRIVER%");
            AddPathReplacement(replacements, report.SteamVrPath, "%STEAMVR%");
            AddPathReplacement(replacements, TryGet(_pathService.FindLogPath), "%STEAM_LOGS%");
            return replacements
                .OrderByDescending(replacement => replacement.Key.Length)
                .ToArray();
        }

        private static void AddPathReplacement(
            ICollection<KeyValuePair<string, string>> replacements,
            string path,
            string placeholder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            replacements.Add(new KeyValuePair<string, string>(normalized, placeholder));
            string forwardSlashes = normalized.Replace('\\', '/');
            if (!string.Equals(forwardSlashes, normalized, StringComparison.Ordinal))
            {
                replacements.Add(new KeyValuePair<string, string>(forwardSlashes, placeholder));
            }
        }

        private static string GetUiVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? Tr.Get("common.status.unknown");
        }

        private static string GetFileVersion(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                return string.IsNullOrWhiteSpace(version.ProductVersion)
                    ? version.FileVersion
                    : version.ProductVersion;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                return null;
            }
        }

        private static string GetSteamVrVersion(string steamVrPath)
        {
            if (string.IsNullOrWhiteSpace(steamVrPath))
            {
                return null;
            }
            foreach (string relativePath in new[]
            {
                Path.Combine("bin", "win64", "vrserver.exe"),
                Path.Combine("bin", "win64", "vrmonitor.exe")
            })
            {
                string version = GetFileVersion(Path.Combine(steamVrPath, relativePath));
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
            return null;
        }

        private static string GetOperatingSystemDescription()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string productName = key?.GetValue("ProductName") as string;
                    string displayVersion = key?.GetValue("DisplayVersion") as string;
                    string build = key?.GetValue("CurrentBuildNumber") as string;
                    object updateBuildRevision = key?.GetValue("UBR");
                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
                        {
                            productName = productName.Replace("Windows 10", "Windows 11");
                        }
                        string version = string.IsNullOrWhiteSpace(displayVersion)
                            ? productName
                            : productName + " " + displayVersion;
                        string buildText = string.IsNullOrWhiteSpace(build)
                            ? string.Empty
                            : " (build " + build +
                                (updateBuildRevision == null ? string.Empty : "." + updateBuildRevision) + ")";
                        return version + buildText +
                            (Environment.Is64BitOperatingSystem ? " · x64" : " · x86");
                    }
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException ||
                exception is IOException)
            {
            }
            return Environment.OSVersion +
                (Environment.Is64BitOperatingSystem ? " · x64" : " · x86");
        }

        private static T TryGet<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is Newtonsoft.Json.JsonException ||
                exception is ArgumentException)
            {
                return default(T);
            }
        }
    }

    internal sealed class DiagnosticsReport
    {
        public string UiVersion { get; set; }
        public bool RuntimeProgramFound { get; set; }
        public string RuntimeProgram { get; set; }
        public string RuntimeProgramPath { get; set; }
        public string RuntimeVersion { get; set; }
        public bool SteamVrInstalled { get; set; }
        public bool SteamVrRunning { get; set; }
        public string SteamVr { get; set; }
        public string SteamVrPath { get; set; }
        public string SteamVrVersion { get; set; }
        public bool DriverRegistered { get; set; }
        public string DriverRegistration { get; set; }
        public string DriverPath { get; set; }
        public string DriverVersion { get; set; }
        public string OperatingSystem { get; set; }
        public bool ApplicationManifestPresent { get; set; }
        public string Configuration { get; set; }
        public IReadOnlyList<string> ConfigurationErrors { get; set; } = Array.Empty<string>();
    }
}
