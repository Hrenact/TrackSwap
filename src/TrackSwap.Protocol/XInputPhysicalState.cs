namespace TrackSwap.Protocol
{
    public sealed class XInputPhysicalState
    {
        public bool Connected { get; set; }
        public ushort Buttons { get; set; }
        public float LeftTrigger { get; set; }
        public float RightTrigger { get; set; }
        public float LeftStickX { get; set; }
        public float LeftStickY { get; set; }
        public float RightStickX { get; set; }
        public float RightStickY { get; set; }
    }
}
