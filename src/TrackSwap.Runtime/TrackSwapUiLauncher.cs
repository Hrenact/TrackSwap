using System.Diagnostics;

namespace TrackSwap.Runtime;

internal static class TrackSwapUiLauncher
{
    public static async Task RunUntilObservedAsync(CancellationToken cancellationToken)
    {
        string? executable = FindExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            WriteDiagnostic("TrackSwap.exe was not found from " + GetRuntimeDirectory());
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsRunning(executable))
            {
                WriteDiagnostic("TrackSwap UI observed: " + executable);
                return;
            }

            bool started = Start(executable);
            WriteDiagnostic((started ? "Requested" : "Failed to request") +
                " TrackSwap UI: " + executable);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool EnsureStarted()
    {
        string? executable = FindExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        if (IsRunning(executable))
        {
            return true;
        }

        return Start(executable);
    }

    private static bool Start(string executable)
    {
        Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--steamvr-session",
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false
        });
        return process != null;
    }

    private static string? FindExecutable()
    {
        string runtimeDirectory = GetRuntimeDirectory();
        DirectoryInfo? directory = new DirectoryInfo(runtimeDirectory);
        for (DirectoryInfo? ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
        {
            string packaged = Path.Combine(ancestor.FullName, "TrackSwap.exe");
            if (File.Exists(packaged))
            {
                return Path.GetFullPath(packaged);
            }

            string development = Path.Combine(
                ancestor.FullName,
                "src",
                "TrackSwap",
                "bin",
                "Release",
                "net48",
                "TrackSwap.exe");
            if (File.Exists(development))
            {
                return Path.GetFullPath(development);
            }
        }
        return null;
    }

    private static string GetRuntimeDirectory()
    {
        string? executable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(executable)
            ? Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
    }

    private static bool IsRunning(string expectedExecutable)
    {
        foreach (Process process in Process.GetProcessesByName("TrackSwap"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(process.MainModule?.FileName ?? string.Empty),
                        expectedExecutable,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException ||
                    exception is System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return false;
    }

    internal static void WriteDiagnostic(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrackSwap");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "runtime-ui-launch.log"),
                DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
        }
    }
}
