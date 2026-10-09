using System;

namespace TrackSwap.Services
{
    internal static class DeviceMotionIntensityFilter
    {
        internal const double LinearDeadZoneMetresPerSecond = 0.04;
        internal const double LinearFullScaleMetresPerSecond = 1.25;
        internal const double AngularDeadZoneRadiansPerSecond = 0.30;
        internal const double AngularFullScaleRadiansPerSecond = 6.0;
        internal const double AttackSeconds = 0.055;
        internal const double ReleaseSeconds = 0.55;

        public static double Update(
            double previousIntensity,
            double linearSpeedMetresPerSecond,
            double angularSpeedRadiansPerSecond,
            double elapsedSeconds)
        {
            double linear = Normalize(
                linearSpeedMetresPerSecond,
                LinearDeadZoneMetresPerSecond,
                LinearFullScaleMetresPerSecond);
            double angular = Normalize(
                angularSpeedRadiansPerSecond,
                AngularDeadZoneRadiansPerSecond,
                AngularFullScaleRadiansPerSecond);
            double target = Math.Max(linear, angular);
            double previous = Clamp(previousIntensity);
            double elapsed = Math.Max(0.0, Math.Min(0.25, elapsedSeconds));
            double timeConstant = target >= previous ? AttackSeconds : ReleaseSeconds;
            double blend = timeConstant <= 0.0
                ? 1.0
                : 1.0 - Math.Exp(-elapsed / timeConstant);
            return Clamp(previous + ((target - previous) * blend));
        }

        private static double Normalize(double value, double deadZone, double fullScale)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }
            double normalized = (Math.Abs(value) - deadZone) / (fullScale - deadZone);
            normalized = Clamp(normalized);
            // Smooth the lower end so tracking noise does not produce a hard visual edge.
            return normalized * normalized * (3.0 - (2.0 * normalized));
        }

        private static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
