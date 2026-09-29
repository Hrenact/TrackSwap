namespace TrackSwap.Protocol
{
    /// <summary>
    /// A rigid transform whose translation is expressed in centimetres and whose
    /// quaternion component order is (x, y, z, w). Route Offset values are local
    /// to a device source; ManualPose values are absolute in standing space.
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

        public static PoseOffset DefaultManualPose()
        {
            return Identity();
        }
    }
}
