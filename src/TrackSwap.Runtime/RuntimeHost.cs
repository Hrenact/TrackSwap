using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeHost
{
    public static async Task RunAsync(string configurationPath)
    {
        var store = new ConfigurationStore(configurationPath);
        RuntimeConfiguration configuration = store.Load();
        var synchronizer = new DriverSynchronizer(configuration);
        string configurationDirectory = Path.GetDirectoryName(store.Path)
            ?? throw new InvalidDataException("Runtime configuration path has no parent directory.");
        var profileStore = new CalibrationProfileStore(
            Path.Combine(configurationDirectory, "calibration-profiles.json"));
        var telemetrySampler = new TelemetrySampler();
        var server = new RuntimePipeServer(
            store,
            synchronizer,
            configuration,
            profileStore,
            new OpenVrCalibrationService(),
            telemetrySampler);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine($"TrackSwap Runtime listening on {ProtocolConstants.PipeName}");
        Console.WriteLine($"Configuration: {store.Path}");
        try
        {
            await Task.WhenAll(
                synchronizer.RunAsync(cancellation.Token),
                telemetrySampler.RunAsync(cancellation.Token),
                server.RunAsync(cancellation.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
