using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeStateEventMonitor
{
    public static async Task RunAsync(
        DriverSynchronizer synchronizer,
        OscInputService oscInput,
        XInputInputService xInput,
        StaticMappingReconciliationWorker staticMappingWorker,
        CancellationToken cancellationToken)
    {
        string? previousDriver = null;
        string? previousOsc = null;
        string? previousXInput = null;
        string? previousMapping = null;
        string? previousHiding = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            DriverSynchronizationStatus driver = synchronizer.GetStatus();
            OscRuntimeStatus osc = oscInput.GetStatus();
            XInputRuntimeStatus xinput = xInput.GetStatus();
            StaticMappingReconciliationStatus mapping = staticMappingWorker.GetStatus();

            WriteWhenChanged(
                "Driver",
                "connected=" + driver.IsConnected +
                "; appliedRevision=" + driver.AppliedRevision +
                "; error=" + Safe(driver.LastError),
                ref previousDriver);
            WriteWhenChanged(
                "OSC",
                "enabled=" + osc.Enabled +
                "; listening=" + osc.Listening +
                "; receivePortInUse=" + osc.ReceivePortInUse +
                "; receiveError=" + Safe(osc.LastError) +
                "; sendError=" + Safe(osc.LastSendError),
                ref previousOsc);
            WriteWhenChanged(
                "XInput",
                "enabled=" + xinput.Enabled + "; connected=" + xinput.Connected,
                ref previousXInput);
            WriteWhenChanged(
                "StaticMapping",
                "pending=" + mapping.IsPending + "; error=" + Safe(mapping.LastError),
                ref previousMapping);

            PhysicalSourceHidingStatus hiding = driver.PhysicalSourceHiding ?? new PhysicalSourceHidingStatus();
            WriteWhenChanged(
                "PhysicalSourceHiding",
                "state=" + hiding.State +
                "; hookInstalled=" + hiding.HookInstalled +
                "; active=" + hiding.ActiveDeviceCount +
                "; requested=" + hiding.RequestedDeviceCount +
                "; error=" + Safe(hiding.LastError),
                ref previousHiding);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteWhenChanged(
        string eventName,
        string value,
        ref string? previous)
    {
        if (string.Equals(value, previous, StringComparison.Ordinal))
        {
            return;
        }
        previous = value;
        RuntimeEventLog.Write(eventName, value);
    }

    private static string Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }
}
