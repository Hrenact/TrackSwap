using System;
using System.Windows.Media.Media3D;

namespace TrackSwap.Services
{
    internal static class PoseRotationConverter
    {
        private const double DegreesToRadians = Math.PI / 180.0;
        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const double GimbalLockThreshold = 1.0 - 1e-10;

        // Euler angles are composed in X -> Y -> Z order: q = qZ * qY * qX.
        public static Quaternion FromEulerDegrees(double x, double y, double z)
        {
            double halfX = x * DegreesToRadians * 0.5;
            double halfY = y * DegreesToRadians * 0.5;
            double halfZ = z * DegreesToRadians * 0.5;
            double sinX = Math.Sin(halfX);
            double cosX = Math.Cos(halfX);
            double sinY = Math.Sin(halfY);
            double cosY = Math.Cos(halfY);
            double sinZ = Math.Sin(halfZ);
            double cosZ = Math.Cos(halfZ);

            var rotation = new Quaternion(
                (sinX * cosY * cosZ) - (cosX * sinY * sinZ),
                (cosX * sinY * cosZ) + (sinX * cosY * sinZ),
                (cosX * cosY * sinZ) - (sinX * sinY * cosZ),
                (cosX * cosY * cosZ) + (sinX * sinY * sinZ));
            rotation.Normalize();
            return rotation;
        }

        public static Vector3D ToEulerDegrees(Quaternion rotation)
        {
            if (LengthSquared(rotation) < 1e-12)
            {
                rotation = Quaternion.Identity;
            }
            else
            {
                rotation.Normalize();
            }

            double sinY = Math.Max(-1.0, Math.Min(
                1.0,
                2.0 * ((rotation.W * rotation.Y) - (rotation.Z * rotation.X))));
            double x;
            double y = Math.Asin(sinY);
            double z;
            if (Math.Abs(sinY) >= GimbalLockThreshold)
            {
                // At gimbal lock there are infinitely many X/Z pairs. Keep a stable
                // canonical representation by fixing X to zero and preserving rotation.
                x = 0.0;
                double matrix01 = 2.0 * ((rotation.X * rotation.Y) - (rotation.Z * rotation.W));
                double matrix11 = 1.0 - (2.0 * (
                    (rotation.X * rotation.X) + (rotation.Z * rotation.Z)));
                z = Math.Atan2(-matrix01, matrix11);
            }
            else
            {
                x = Math.Atan2(
                    2.0 * ((rotation.W * rotation.X) + (rotation.Y * rotation.Z)),
                    1.0 - (2.0 * ((rotation.X * rotation.X) + (rotation.Y * rotation.Y))));
                z = Math.Atan2(
                    2.0 * ((rotation.W * rotation.Z) + (rotation.X * rotation.Y)),
                    1.0 - (2.0 * ((rotation.Y * rotation.Y) + (rotation.Z * rotation.Z))));
            }

            return new Vector3D(
                NormalizeDegrees(x * RadiansToDegrees),
                NormalizeDegrees(y * RadiansToDegrees),
                NormalizeDegrees(z * RadiansToDegrees));
        }

        private static double NormalizeDegrees(double value)
        {
            double normalized = value % 360.0;
            if (normalized > 180.0)
            {
                normalized -= 360.0;
            }
            else if (normalized <= -180.0)
            {
                normalized += 360.0;
            }
            return Math.Abs(normalized) < 1e-10 ? 0.0 : normalized;
        }

        private static double LengthSquared(Quaternion value)
        {
            return (value.X * value.X) + (value.Y * value.Y) +
                (value.Z * value.Z) + (value.W * value.W);
        }
    }
}
