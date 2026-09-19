using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class TelemetrySampler
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromMilliseconds(500);
    private readonly object syncRoot = new();
    private IReadOnlyDictionary<int, PoseTelemetrySnapshot> latest =
        new Dictionary<int, PoseTelemetrySnapshot>();
    private string? lastError;

    public PoseTelemetrySnapshot GetLatest(int virtualDeviceSlot)
    {
        lock (syncRoot)
        {
            if (!latest.TryGetValue(virtualDeviceSlot, out PoseTelemetrySnapshot? snapshot))
            {
                throw new InvalidDataException(lastError ?? "Driver telemetry is not available yet.");
            }
            if (DateTimeOffset.UtcNow - snapshot.CapturedAtUtc > MaximumSnapshotAge)
            {
                throw new InvalidDataException(lastError ?? "Driver telemetry is stale.");
            }
            return snapshot;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<PoseTelemetrySnapshot> snapshots =
                    DriverControlClient.GetTelemetry(TimeSpan.FromMilliseconds(250));
                lock (syncRoot)
                {
                    latest = snapshots.ToDictionary(snapshot => snapshot.VirtualDeviceSlot);
                    lastError = null;
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException)
            {
                lock (syncRoot)
                {
                    lastError = exception.Message;
                }
            }

            await Task.Delay(33, cancellationToken).ConfigureAwait(false);
        }
    }
}
