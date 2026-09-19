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
        public RuntimeConfiguration Configuration { get; set; } = new RuntimeConfiguration();
    }
}
