using System.Globalization;
using System.Text;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeEventLog
{
    private const long MaximumBytes = 1024 * 1024;
    private static readonly object SyncRoot = new();

    public static void Write(string eventName, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                string directory = TrackSwapDataPaths.ActiveDataDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "runtime-events.log");
                RotateIfNeeded(path);
                string line = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                    " [" + NormalizeEventName(eventName) + "] " +
                    (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ') +
                    Environment.NewLine;
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is System.Security.SecurityException ||
            exception is NotSupportedException)
        {
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumBytes)
        {
            return;
        }
        string previous = Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            "runtime-events.previous.log");
        if (File.Exists(previous))
        {
            File.Delete(previous);
        }
        File.Move(path, previous);
    }

    private static string NormalizeEventName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Runtime";
        }
        return new string(value.Where(character =>
            char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_')
            .Take(64)
            .ToArray());
    }
}
