using System;

namespace TrackSwap.Protocol
{
    public sealed class OscRuntimeStatus
    {
        public bool Enabled { get; set; }
        public bool Listening { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public DateTimeOffset? LastMessageAtUtc { get; set; }
        public string LastError { get; set; } = string.Empty;
        public ControllerInputState LeftInput { get; set; } = new ControllerInputState();
        public ControllerInputState RightInput { get; set; } = new ControllerInputState();
    }
}
