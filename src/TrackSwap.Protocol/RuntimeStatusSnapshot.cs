namespace TrackSwap.Protocol
{
    /// <summary>
    /// A control-plane status snapshot. This is intentionally separate from pose
    /// telemetry so querying it can never participate in the per-frame path.
    /// </summary>
    public sealed class RuntimeStatusSnapshot
    {
        public long ConfigurationRevision { get; set; }
        public long DriverAppliedRevision { get; set; }
        public bool DriverConnected { get; set; }
        public string? LastError { get; set; }
        public bool StaticMappingPending { get; set; }
        public string? StaticMappingLastError { get; set; }
        public RuntimeConfiguration Configuration { get; set; } = new RuntimeConfiguration();
        public OscRuntimeStatus Osc { get; set; } = new OscRuntimeStatus();
        public XInputRuntimeStatus XInput { get; set; } = new XInputRuntimeStatus();
        public PhysicalSourceHidingStatus PhysicalSourceHiding { get; set; } = new PhysicalSourceHidingStatus();
    }

    public enum PhysicalSourceHidingState
    {
        Disabled = 0,
        Waiting = 1,
        Active = 2,
        Failed = 3
    }

    public sealed class PhysicalSourceHidingStatus
    {
        public PhysicalSourceHidingState State { get; set; }
        public int RequestedDeviceCount { get; set; }
        public int ActiveDeviceCount { get; set; }
        public bool HookInstalled { get; set; }
        public string? LastError { get; set; }
    }
}
