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
    private PhysicalSourceHidingStatus physicalSourceHiding = new();

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
                lastError,
                physicalSourceHiding);
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
                    RouteConfiguration?[] trackerRoutes = Enumerable.Range(0, ProtocolConstants.MaximumRoutes)
                        .Select(slot => snapshot.Routes.SingleOrDefault(candidate =>
                            candidate.Enabled && candidate.Mode != RouteMode.VirtualController &&
                            candidate.Mode != RouteMode.VirtualHmd && candidate.VirtualDeviceSlot == slot))
                        .ToArray();
                    RouteConfiguration? hmdRoute = snapshot.Routes.SingleOrDefault(candidate =>
                        candidate.Enabled && candidate.Mode == RouteMode.VirtualHmd);
                    var controllerRoutes = new[] { ControllerHand.Left, ControllerHand.Right }
                        .ToDictionary(hand => hand, hand => snapshot.Routes.SingleOrDefault(candidate =>
                            candidate.Enabled && candidate.Mode == RouteMode.VirtualController &&
                            candidate.ControllerHand == hand));

                    // Establish every new owner before clearing an old snapshot family. This
                    // preserves physical-source hiding across direct/controller/HMD transitions.
                    for (int slot = 0; slot < ProtocolConstants.MaximumRoutes; slot++)
                    {
                        RouteConfiguration? route = trackerRoutes[slot];
                        if (route == null) continue;
                        DriverControlClient.ApplySnapshot(
                            slot,
                            route,
                            snapshot.PhysicalSourceHidingEnabled && route?.HidePhysicalSource == true,
                            (ulong)snapshot.Revision,
                            TimeSpan.FromSeconds(1));
                    }
                    foreach (ControllerHand hand in new[] { ControllerHand.Left, ControllerHand.Right })
                    {
                        RouteConfiguration? route = controllerRoutes[hand];
                        if (route == null) continue;
                        DriverControlClient.ApplyControllerSnapshot(
                            hand,
                            route,
                            snapshot.PhysicalSourceHidingEnabled && route?.HidePhysicalSource == true,
                            snapshot.ControllerHandSelectionPriority,
                            (ulong)snapshot.Revision,
                            TimeSpan.FromSeconds(1));
                    }
                    if (hmdRoute != null)
                    {
                        DriverControlClient.ApplyHmdSnapshot(
                            hmdRoute,
                            snapshot.PhysicalSourceHidingEnabled && hmdRoute.HidePhysicalSource,
                            (ulong)snapshot.Revision,
                            TimeSpan.FromSeconds(1));
                    }

                    for (int slot = 0; slot < ProtocolConstants.MaximumRoutes; slot++)
                    {
                        if (trackerRoutes[slot] != null) continue;
                        DriverControlClient.ApplySnapshot(slot, null, false, (ulong)snapshot.Revision, TimeSpan.FromSeconds(1));
                    }
                    foreach (ControllerHand hand in new[] { ControllerHand.Left, ControllerHand.Right })
                    {
                        if (controllerRoutes[hand] != null) continue;
                        DriverControlClient.ApplyControllerSnapshot(
                            hand, null, false, snapshot.ControllerHandSelectionPriority,
                            (ulong)snapshot.Revision, TimeSpan.FromSeconds(1));
                    }
                    if (hmdRoute == null)
                    {
                        DriverControlClient.ApplyHmdSnapshot(null, false, (ulong)snapshot.Revision, TimeSpan.FromSeconds(1));
                    }
                    PhysicalSourceHidingStatus hidingStatus =
                        DriverControlClient.GetPhysicalSourceHidingStatus(TimeSpan.FromSeconds(1));
                    lock (syncRoot)
                    {
                        isDriverConnected = true;
                        lastAppliedRevision = snapshot.Revision;
                        lastError = null;
                        physicalSourceHiding = hidingStatus;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is TimeoutException ||
                    exception is UnauthorizedAccessException ||
                    exception is ArgumentException ||
                    exception is InvalidOperationException)
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
    public DriverSynchronizationStatus(
        bool isConnected,
        long appliedRevision,
        string? lastError,
        PhysicalSourceHidingStatus physicalSourceHiding)
    {
        IsConnected = isConnected;
        AppliedRevision = appliedRevision;
        LastError = lastError;
        PhysicalSourceHiding = physicalSourceHiding;
    }

    public bool IsConnected { get; }
    public long AppliedRevision { get; }
    public string? LastError { get; }
    public PhysicalSourceHidingStatus PhysicalSourceHiding { get; }
}
