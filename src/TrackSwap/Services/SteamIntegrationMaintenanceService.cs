using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class SteamIntegrationMaintenanceService
    {
        public Task CleanupMappingsAsync()
        {
            return Task.Run(() => RunRegistrationScript("CleanupMappings"));
        }

        public Task RemoveAsync()
        {
            return Task.Run(() =>
            {
                var failures = new List<string>();
                TryRun(() => RunRegistrationScript("Uninstall"), failures);
                TryRun(() => RunScript("Uninstall-Driver.ps1", string.Empty), failures);
                if (failures.Count != 0)
                {
                    throw new InvalidOperationException(
                        Tr.Get("service.steam_integration_maintenance.remove_async.steamvr_cleanup_complete_complete") + string.Join("；", failures));
                }
            });
        }

        private static void TryRun(Action action, ICollection<string> failures)
        {
            try
            {
                action();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidOperationException ||
                exception is Win32Exception)
            {
                failures.Add(exception.Message);
            }
        }

        private static void RunRegistrationScript(string mode)
        {
            string manifestPath = FindRequiredFile(
                "TrackSwap.vrmanifest",
                Path.Combine(TrackSwapDataPaths.ApplicationRootDirectory, "src", "TrackSwap", "TrackSwap.vrmanifest"));
            string jsonLibraryPath = FindRequiredFile("Newtonsoft.Json.dll", null);
            RunScript(
                "Manage-SteamVrRegistration.ps1",
                "-Mode " + mode +
                " -ManifestPath " + Quote(manifestPath) +
                " -JsonLibraryPath " + Quote(jsonLibraryPath));
        }

        private static void RunScript(string scriptName, string arguments)
        {
            string scriptPath = FindScript(scriptName);
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powershell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                powershell = "powershell.exe";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " +
                    Quote(scriptPath) + " " + arguments,
                WorkingDirectory = TrackSwapDataPaths.ApplicationRootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException(Tr.Get("service.steam_integration_maintenance.run_script.cannot_start_steamvr")))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                    throw new InvalidOperationException(
                        Path.GetFileName(scriptPath) + Tr.Get("service.steam_integration_maintenance.run_script.error") + process.ExitCode +
                        (string.IsNullOrWhiteSpace(detail) ? "." : ": " + detail.Trim()));
                }
            }
        }

        private static string FindScript(string scriptName)
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", scriptName),
                Path.Combine(TrackSwapDataPaths.ApplicationRootDirectory, "scripts", scriptName)
            };
            for (DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                directory != null;
                directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, "scripts", scriptName));
                if (File.Exists(Path.Combine(directory.FullName, "TrackSwap.sln")))
                {
                    break;
                }
            }
            return candidates.FirstOrDefault(File.Exists) ??
                throw new FileNotFoundException(Tr.Get("service.steam_integration_maintenance.find_script.not_found_steamvr"), scriptName);
        }

        private static string FindRequiredFile(string fileName, string developmentCandidate)
        {
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(TrackSwapDataPaths.ApplicationRootDirectory, fileName),
                developmentCandidate
            };
            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .FirstOrDefault(File.Exists) ??
                throw new FileNotFoundException(Tr.Get("service.steam_integration_maintenance.find_required_file.not_found_steamvr_file"), fileName);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
