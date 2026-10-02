namespace TrackSwap.Protocol
{
    public sealed class MotionSmoothingConfiguration
    {
        public const double MinimumStrength = 0.0;
        public const double MaximumStrength = 100.0;
        public const double DefaultStrength = 0.0;

        public bool Enabled { get; set; }
        public bool SmoothPosition { get; set; } = true;
        public bool SmoothRotation { get; set; } = true;
        public bool LinkStrengths { get; set; } = true;
        public double PositionStrength { get; set; } = DefaultStrength;
        public double RotationStrength { get; set; } = DefaultStrength;
    }
}
