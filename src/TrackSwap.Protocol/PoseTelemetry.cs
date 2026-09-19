using System;

namespace TrackSwap.Protocol
{
    public sealed class PoseTelemetry
    {
        public bool Connected { get; set; }
        public bool Valid { get; set; }
        public int TrackingResult { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public double RotationX { get; set; }
        public double RotationY { get; set; }
        public double RotationZ { get; set; }
        public double RotationW { get; set; } = 1;
    }

    public sealed class PoseTelemetrySnapshot
    {
        public int VirtualDeviceSlot { get; set; }
        public ulong Sequence { get; set; }
        public long AppliedRevision { get; set; }
        public DateTimeOffset CapturedAtUtc { get; set; }
        public PoseTelemetry Source { get; set; } = new PoseTelemetry();
        public PoseTelemetry Output { get; set; } = new PoseTelemetry();
        public PoseTelemetry Target { get; set; } = new PoseTelemetry();
    }

    public sealed class TelemetryRequest
    {
        public int VirtualDeviceSlot { get; set; }
    }
}
