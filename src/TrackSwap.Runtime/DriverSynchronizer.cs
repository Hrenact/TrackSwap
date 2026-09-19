using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class DriverSynchronizer
{
    private readonly object syncRoot = new();
    private RuntimeConfiguration configuration;

    public DriverSynchronizer(RuntimeConfiguration initialConfiguration)
    {
        configuration = initialConfiguration;
    }

    private bool isDriverConnected;
    private long lastAppliedRevision;
    private string? lastError;

    public void Update(RuntimeConfiguration value)
    {
        lock (syncRoot)
        {
            configuration = value;
        }
    }

    public DriverSynchronizationStatus GetStatus()
    {
        lock (syncRoot)
        {
            return new DriverSynchronizationStatus(
                isDriverConnected,
                lastAppliedRevision,
                lastError);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RuntimeConfiguration snapshot;
            lock (syncRoot)
            {
                snapshot = configuration;
            }

            if (snapshot.Revision > 0)
            {
                try
                {
                    for (int slot = 0; slot < ProtocolConstants.MaximumRoutes; slot++)
                    {
                        RouteConfiguration? route = snapshot.Routes.SingleOrDefault(
                            candidate => candidate.Enabled && candidate.VirtualDeviceSlot == slot);
                        DriverControlClient.ApplySnapshot(
                            slot,
                            route,
                            (ulong)snapshot.Revision,
                            TimeSpan.FromSeconds(1));
                    }
                    lock (syncRoot)
                    {
                        isDriverConnected = true;
                        lastAppliedRevision = snapshot.Revision;
                        lastError = null;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
                {
                    lock (syncRoot)
                    {
                        isDriverConnected = false;
                        lastError = exception.Message;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class DriverSynchronizationStatus
{
    public DriverSynchronizationStatus(bool isConnected, long appliedRevision, string? lastError)
    {
        IsConnected = isConnected;
        AppliedRevision = appliedRevision;
        LastError = lastError;
    }

    public bool IsConnected { get; }
    public long AppliedRevision { get; }
    public string? LastError { get; }
}
