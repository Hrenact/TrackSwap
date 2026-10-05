using System.Collections.Generic;

namespace TrackSwap.Protocol
{
    public sealed class RuntimeConfiguration
    {
        public int SchemaVersion { get; set; } = ProtocolConstants.CurrentConfigurationSchemaVersion;
        public long Revision { get; set; }
        // Retained for backward-compatible JSON round trips. Both capabilities are
        // unconditional now, so legacy configurations are normalized to true.
        public bool AllowDuplicatePoseSources { get; set; } = true;
        public bool PhysicalSourceHidingEnabled { get; set; } = true;
        public int ControllerHandSelectionPriority { get; set; } = ProtocolConstants.DefaultControllerHandSelectionPriority;
        public List<RouteConfiguration> Routes { get; set; } = new List<RouteConfiguration>();
        public OscConfiguration Osc { get; set; } = OscConfiguration.CreateDefault();
        public XInputConfiguration XInput { get; set; } = XInputConfiguration.CreateDefault();
    }
}
