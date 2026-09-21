using System.Diagnostics;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeLifecycleMonitor
{
    private static readonly string[] SteamVrProcessNames =
    {
        "vrserver",
        "vrmonitor",
        "vrcompositor"
    };

    public static async Task RunAsync(
        RuntimeLifecycleMode mode,
        int? ownerProcessId,
        Action requestShutdown,
        CancellationToken cancellationToken)
    {
        DateTime? idleSince = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            bool ownerAlive = IsProcessAlive(ownerProcessId);
            bool steamVrRunning = IsSteamVrRunning();
            bool shouldRemainRunning = ShouldRemainRunning(mode, ownerAlive, steamVrRunning);
            if (shouldRemainRunning)
            {
                idleSince = null;
            }
            else
            {
                idleSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - idleSince.Value >= TimeSpan.FromSeconds(5))
                {
                    requestShutdown();
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool ShouldRemainRunning(
        RuntimeLifecycleMode mode,
        bool ownerAlive,
        bool steamVrRunning)
    {
        return mode == RuntimeLifecycleMode.FollowTrackSwap
            ? ownerAlive
            : ownerAlive || steamVrRunning;
    }

    private static bool IsProcessAlive(int? processId)
    {
        if (!processId.HasValue || processId.Value <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId.Value);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsSteamVrRunning()
    {
        return SteamVrProcessNames.Any(name => Process.GetProcessesByName(name).Length != 0);
    }
}
