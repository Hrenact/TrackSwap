namespace TrackSwap.Protocol
{
    public sealed class HapticFeedbackEvent
    {
        public ulong Sequence { get; set; }
        public ControllerHand Hand { get; set; }
        public float DurationSeconds { get; set; }
        public float Frequency { get; set; }
        public float Amplitude { get; set; }
    }
}
