using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        public DiagnosticsService(
            SteamVrPathService pathService,
            SteamVrStatusService statusService,
            RuntimeControlService runtimeControlService)
        {
            _pathService = pathService;
            _statusService = statusService;
            _runtimeControlService = runtimeControlService;
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
                RuntimeProgram = File.Exists(runtimeExecutable) ? "已找到" : "未找到",
                RuntimeProgramPath = runtimeExecutable,
                SteamVr = string.IsNullOrWhiteSpace(steamVrPath)
                    ? "未检测到安装路径"
                    : _statusService.IsRunning() ? "运行中" : "已安装 · 未运行",
                SteamVrPath = steamVrPath,
                DriverRegistration = string.IsNullOrWhiteSpace(driverPath) ? "未注册" : "已注册",
                DriverPath = driverPath,
                Configuration = runtimeStatus?.Configuration == null
                    ? "尚未从 Runtime 读取"
                    : configurationErrors.Count == 0 ? "通过" : configurationErrors.Count + " 个问题",
                ConfigurationErrors = configurationErrors
            };
        }

        public void Export(string destinationPath, RuntimeStatusSnapshot runtimeStatus)
        {
            DiagnosticsReport report = Inspect(runtimeStatus);
            IReadOnlyList<string> sensitiveDeviceValues = GetSensitiveDeviceValues(runtimeStatus?.Configuration);
            IReadOnlyList<KeyValuePair<string, string>> pathReplacements = BuildPathReplacements(report);
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "TrackSwap-diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                File.WriteAllText(
                    Path.Combine(temporaryDirectory, "summary.txt"),
                    Sanitize(BuildSummary(report, runtimeStatus), sensitiveDeviceValues, pathReplacements),
                    new UTF8Encoding(false));

                CopySanitizedLog(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TrackSwap",
                        "runtime-ui-launch.log"),
                    Path.Combine(temporaryDirectory, "runtime-ui-launch.log"),
                    line => true,
                    500,
                    sensitiveDeviceValues,
                    pathReplacements);

                CopySanitizedLog(
                    FindSteamVrServerLog(),
                    Path.Combine(temporaryDirectory, "vrserver-trackswap.log"),
                    line => line.IndexOf("trackswap", StringComparison.OrdinalIgnoreCase) >= 0,
                    500,
                    sensitiveDeviceValues,
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

        private static string BuildSummary(DiagnosticsReport report, RuntimeStatusSnapshot status)
        {
            RuntimeConfiguration configuration = status?.Configuration;
            IReadOnlyList<RouteConfiguration> routes = configuration?.Routes ?? new List<RouteConfiguration>();
            var builder = new StringBuilder();
            builder.AppendLine("TrackSwap 诊断摘要");
            builder.AppendLine("生成时间: " + DateTimeOffset.Now.ToString("O"));
            builder.AppendLine("UI 版本: " + report.UiVersion);
            builder.AppendLine("操作系统: " + GetOperatingSystemDescription());
            builder.AppendLine("Runtime: " + (status == null ? "未连接" : "在线"));
            builder.AppendLine("Runtime 程序: " + report.RuntimeProgram + " · " + (report.RuntimeProgramPath ?? "—"));
            builder.AppendLine("SteamVR: " + report.SteamVr + " · " + (report.SteamVrPath ?? "—"));
            builder.AppendLine("TrackSwap 驱动: " + report.DriverRegistration + " · " + (report.DriverPath ?? "—"));
            builder.AppendLine("驱动连接: " + (status?.DriverConnected == true ? "是" : "否"));
            builder.AppendLine("配置 revision: " + (status == null ? "—" : status.ConfigurationRevision.ToString()));
            builder.AppendLine("驱动 applied revision: " + (status == null ? "—" : status.DriverAppliedRevision.ToString()));
            builder.AppendLine("静态映射整理: " + (status == null
                ? "未知"
                : status.StaticMappingPending ? "待处理" : "已完成"));
            builder.AppendLine("设备隐藏: " + (status?.PhysicalSourceHiding == null
                ? "未知"
                : status.PhysicalSourceHiding.State + " · " +
                  status.PhysicalSourceHiding.ActiveDeviceCount + " / " +
                  status.PhysicalSourceHiding.RequestedDeviceCount));
            builder.AppendLine("配置校验: " + report.Configuration);
            builder.AppendLine();
            builder.AppendLine("路由统计（不含名称、设备路径与序列号）");
            builder.AppendLine("总数: " + routes.Count);
            builder.AppendLine("启用: " + routes.Count(route => route.Enabled));
            builder.AppendLine("待删除: " + routes.Count(route => route.PendingDeletion));
            builder.AppendLine("虚拟追踪器: " + routes.Count(route => route.Mode == RouteMode.DirectProxy));
            builder.AppendLine("虚拟控制器: " + routes.Count(route => route.Mode == RouteMode.VirtualController));
            builder.AppendLine("替换设备位姿: " + routes.Count(route => route.Mode == RouteMode.ReplaceTarget));
            builder.AppendLine("OSC 输入: " + routes.Count(route => route.Mode == RouteMode.VirtualController && route.ControlInputSource == ControlInputSource.Osc));
            builder.AppendLine("XInput 输入: " + routes.Count(route => route.Mode == RouteMode.VirtualController && route.ControlInputSource == ControlInputSource.XInput));
            if (report.ConfigurationErrors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("配置问题");
                foreach (string error in report.ConfigurationErrors)
                {
                    builder.AppendLine("- " + error);
                }
            }
            if (!string.IsNullOrWhiteSpace(status?.LastError))
            {
                builder.AppendLine();
                builder.AppendLine("Runtime 最近错误");
                builder.AppendLine(status.LastError);
            }
            if (!string.IsNullOrWhiteSpace(status?.StaticMappingLastError))
            {
                builder.AppendLine();
                builder.AppendLine("静态映射最近错误");
                builder.AppendLine(status.StaticMappingLastError);
            }
            return builder.ToString();
        }

        private static void CopySanitizedLog(
            string sourcePath,
            string destinationPath,
            Func<string, bool> predicate,
            int maximumLines,
            IReadOnlyList<string> sensitiveDeviceValues,
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
                    queue.Enqueue(Sanitize(line, sensitiveDeviceValues, pathReplacements));
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

        private static IReadOnlyList<string> GetSensitiveDeviceValues(RuntimeConfiguration configuration)
        {
            if (configuration?.Routes == null)
            {
                return Array.Empty<string>();
            }
            return configuration.Routes
                .Where(route => route != null)
                .SelectMany(route => new[] { route.SourceDevicePath, route.TargetDevicePath })
                .Where(path => !string.IsNullOrWhiteSpace(path) && path.StartsWith("/devices/", StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => new[] { path, path.Split('/').LastOrDefault() })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .ToArray();
        }

        private IReadOnlyList<KeyValuePair<string, string>> BuildPathReplacements(DiagnosticsReport report)
        {
            var replacements = new List<KeyValuePair<string, string>>();
            AddPathReplacement(replacements, AppDomain.CurrentDomain.BaseDirectory, "%TRACKSWAP_APP%");
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

        private static string Sanitize(
            string text,
            IReadOnlyList<string> sensitiveDeviceValues,
            IReadOnlyList<KeyValuePair<string, string>> pathReplacements)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string result = ReplaceIgnoreCase(text, localAppData, "%LOCALAPPDATA%");
            result = ReplaceIgnoreCase(result, userProfile, "%USERPROFILE%");
            foreach (KeyValuePair<string, string> replacement in pathReplacements ?? Array.Empty<KeyValuePair<string, string>>())
            {
                result = ReplaceIgnoreCase(result, replacement.Key, replacement.Value);
            }
            foreach (string sensitiveValue in sensitiveDeviceValues ?? Array.Empty<string>())
            {
                result = ReplaceIgnoreCase(result, sensitiveValue, "<DEVICE_REDACTED>");
            }
            result = Regex.Replace(result, "(?i)/devices/[^\\s\\\"']+", "/devices/<REDACTED>");
            result = Regex.Replace(result, "(?i)\\bLHR-[0-9A-F]+\\b", "<DEVICE_SERIAL>");
            result = Regex.Replace(
                result,
                "(?i)\\b(serial(?:_number| number)?)[ \\t]*[:=][ \\t]*[\\\"']?[^,;\\s\\\"']+",
                "$1=<DEVICE_SERIAL>");
            result = Regex.Replace(
                result,
                "(?i)file:///[A-Z]:/[^\\s\\\"']+",
                match => "file:///<LOCAL_PATH>/" + GetPortableFileName(match.Value));
            result = Regex.Replace(
                result,
                "(?i)\\b[A-Z]:\\\\[^\\s\\\"']+",
                match => "<LOCAL_PATH>/" + GetPortableFileName(match.Value));
            return result;
        }

        private static string GetPortableFileName(string path)
        {
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int separator = normalized.LastIndexOf('/');
            return separator >= 0 && separator + 1 < normalized.Length
                ? normalized.Substring(separator + 1)
                : "file";
        }

        private static string ReplaceIgnoreCase(string value, string oldValue, string newValue)
        {
            if (string.IsNullOrWhiteSpace(oldValue))
            {
                return value;
            }
            return Regex.Replace(value, Regex.Escape(oldValue), _ => newValue, RegexOptions.IgnoreCase);
        }

        private static string GetUiVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "未知";
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
        public string RuntimeProgram { get; set; }
        public string RuntimeProgramPath { get; set; }
        public string SteamVr { get; set; }
        public string SteamVrPath { get; set; }
        public string DriverRegistration { get; set; }
        public string DriverPath { get; set; }
        public string Configuration { get; set; }
        public IReadOnlyList<string> ConfigurationErrors { get; set; } = Array.Empty<string>();
    }
}
