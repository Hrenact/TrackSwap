namespace TrackSwap.Protocol
{
    /// <summary>
    /// A rigid transform expressed in the source device's local coordinate frame.
    /// Translation is in metres. Quaternion component order is (x, y, z, w).
    /// The driver applies this as T_output = T_source * T_offset.
    /// </summary>
    public sealed class PoseOffset
    {
        public double TranslationX { get; set; }
        public double TranslationY { get; set; }
        public double TranslationZ { get; set; }
        public double RotationX { get; set; }
        public double RotationY { get; set; }
        public double RotationZ { get; set; }
        public double RotationW { get; set; } = 1.0;

        public static PoseOffset Identity()
        {
            return new PoseOffset();
        }
    }
}
