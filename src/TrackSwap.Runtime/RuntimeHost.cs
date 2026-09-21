using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeHost
{
    public static async Task RunAsync(
        string configurationPath,
        RuntimeLifecycleMode? lifecycleMode = null,
        int? ownerProcessId = null,
        bool ensureTrackSwapUi = false)
    {
        var store = new ConfigurationStore(configurationPath);
        RuntimeConfiguration configuration = store.Load();
        var synchronizer = new DriverSynchronizer(configuration);
        string configurationDirectory = Path.GetDirectoryName(store.Path)
            ?? throw new InvalidDataException("Runtime configuration path has no parent directory.");
        var profileStore = new CalibrationProfileStore(
            Path.Combine(configurationDirectory, "calibration-profiles.json"));
        var telemetrySampler = new TelemetrySampler();
        using var cancellation = new CancellationTokenSource();
        var server = new RuntimePipeServer(
            store,
            synchronizer,
            configuration,
            profileStore,
            new OpenVrCalibrationService(),
            telemetrySampler,
            requestShutdown: cancellation.Cancel);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine($"TrackSwap Runtime listening on {ProtocolConstants.PipeName}");
        Console.WriteLine($"Configuration: {store.Path}");
        try
        {
            var tasks = new List<Task>
            {
                synchronizer.RunAsync(cancellation.Token),
                telemetrySampler.RunAsync(cancellation.Token),
                server.RunAsync(cancellation.Token)
            };
            if (lifecycleMode.HasValue)
            {
                tasks.Add(RuntimeLifecycleMonitor.RunAsync(
                    lifecycleMode.Value,
                    ownerProcessId,
                    cancellation.Cancel,
                    cancellation.Token));
            }
            if (ensureTrackSwapUi)
            {
                tasks.Add(TrackSwapUiLauncher.RunUntilObservedAsync(cancellation.Token));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
