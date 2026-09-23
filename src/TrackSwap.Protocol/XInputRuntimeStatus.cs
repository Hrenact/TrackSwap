namespace TrackSwap.Protocol
{
    public sealed class XInputRuntimeStatus
    {
        public bool Enabled { get; set; }
        public bool Connected { get; set; }
        public ControllerInputState LeftInput { get; set; } = new ControllerInputState();
        public ControllerInputState RightInput { get; set; } = new ControllerInputState();
        public XInputPhysicalState PhysicalInput { get; set; } = new XInputPhysicalState();
    }
}
