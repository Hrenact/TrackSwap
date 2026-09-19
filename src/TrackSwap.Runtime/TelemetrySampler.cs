using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class TelemetrySampler
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromMilliseconds(500);
    private readonly object syncRoot = new();
    private PoseTelemetrySnapshot? latest;
    private string? lastError;

    public PoseTelemetrySnapshot GetLatest()
    {
        lock (syncRoot)
        {
            if (latest == null)
            {
                throw new InvalidDataException(lastError ?? "Driver telemetry is not available yet.");
            }
            if (DateTimeOffset.UtcNow - latest.CapturedAtUtc > MaximumSnapshotAge)
            {
                throw new InvalidDataException(lastError ?? "Driver telemetry is stale.");
            }
            return latest;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                PoseTelemetrySnapshot snapshot = DriverControlClient.GetTelemetry(TimeSpan.FromMilliseconds(250));
                lock (syncRoot)
                {
                    latest = snapshot;
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
