namespace TrackSwap.Runtime;

internal sealed class StaticMappingReconciliationWorker
{
    private readonly RuntimePipeServer server;
    private readonly SteamVrStaticMappingService mappingService;
    private readonly object statusGate = new();
    private long reconciledRevision = long.MinValue;
    private bool pending = true;
    private string? lastError;

    public StaticMappingReconciliationWorker(
        RuntimePipeServer server,
        SteamVrStaticMappingService mappingService)
    {
        this.server = server;
        this.mappingService = mappingService;
    }

    public bool HasPendingWork
    {
        get
        {
            lock (statusGate)
            {
                return pending;
            }
        }
    }

    public StaticMappingReconciliationStatus GetStatus()
    {
        lock (statusGate)
        {
            return new StaticMappingReconciliationStatus(pending, lastError);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            long revision = server.GetConfigurationRevision();
            if (revision != reconciledRevision)
            {
                SetStatus(isPending: true, error: null);
                try
                {
                    StaticMappingReconciliationResult result = server.ReconcileStaticMappings(mappingService);
                    if (result.IsCompleted)
                    {
                        reconciledRevision = server.GetConfigurationRevision();
                        SetStatus(isPending: false, error: null);
                    }
                }
                catch (Exception exception)
                {
                    SetStatus(isPending: true, error: exception.Message);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    private void SetStatus(bool isPending, string? error)
    {
        lock (statusGate)
        {
            pending = isPending;
            lastError = error;
        }
    }
}

internal readonly record struct StaticMappingReconciliationStatus(bool IsPending, string? LastError);
