using System.Collections.Generic;

namespace TrackSwap.Protocol
{
    public sealed class RuntimeConfiguration
    {
        public int SchemaVersion { get; set; } = ProtocolConstants.CurrentConfigurationSchemaVersion;
        public long Revision { get; set; }
        public bool AllowDuplicatePoseSources { get; set; }
        public int ControllerHandSelectionPriority { get; set; } = ProtocolConstants.DefaultControllerHandSelectionPriority;
        public List<RouteConfiguration> Routes { get; set; } = new List<RouteConfiguration>();
        public OscConfiguration Osc { get; set; } = OscConfiguration.CreateDefault();
    }
}
