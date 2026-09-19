namespace TrackSwap.Protocol
{
    /// <summary>
    /// Header shared by all newline-delimited JSON control messages. Payload shape is
    /// selected by MessageType and is validated before it reaches runtime state.
    /// </summary>
    public sealed class MessageEnvelope
    {
        public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentProtocolVersion;
        public string MessageType { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
    }
}
