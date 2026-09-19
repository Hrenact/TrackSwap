using System.Collections.Generic;

namespace TrackSwap.Protocol
{
    public sealed class RuntimeConfiguration
    {
        public int SchemaVersion { get; set; } = ProtocolConstants.CurrentConfigurationSchemaVersion;
        public long Revision { get; set; }
        public List<RouteConfiguration> Routes { get; set; } = new List<RouteConfiguration>();
    }
}
