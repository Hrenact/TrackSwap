using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal readonly record struct CalibrationPose(
    double X,
    double Y,
    double Z,
    double Qx,
    double Qy,
    double Qz,
    double Qw);

internal sealed class CalibrationComputation
{
    public required PoseOffset Offset { get; init; }
    public required double TranslationRmsMetres { get; init; }
    public required double RotationRmsDegrees { get; init; }
}

internal static class CalibrationMath
{
    public static CalibrationPose Relative(CalibrationPose source, CalibrationPose target)
    {
        CalibrationPose normalizedSource = Normalize(source);
        CalibrationPose normalizedTarget = Normalize(target);
        var inverseSource = (X: -normalizedSource.Qx, Y: -normalizedSource.Qy,
            Z: -normalizedSource.Qz, W: normalizedSource.Qw);
        (double qx, double qy, double qz, double qw) = Multiply(
            inverseSource,
            (normalizedTarget.Qx, normalizedTarget.Qy, normalizedTarget.Qz, normalizedTarget.Qw));
        (double x, double y, double z) = Rotate(
            inverseSource,
            target.X - source.X,
            target.Y - source.Y,
            target.Z - source.Z);
        return Normalize(new CalibrationPose(x, y, z, qx, qy, qz, qw));
    }

    public static CalibrationComputation Average(IReadOnlyList<CalibrationPose> relativeSamples)
    {
        if (relativeSamples.Count == 0)
        {
            throw new InvalidDataException("校准至少需要一个有效样本。");
        }

        CalibrationPose reference = Normalize(relativeSamples[0]);
        double x = 0;
        double y = 0;
        double z = 0;
        double qx = 0;
        double qy = 0;
        double qz = 0;
        double qw = 0;
        foreach (CalibrationPose rawSample in relativeSamples)
        {
            CalibrationPose sample = Normalize(rawSample);
            double sign = Dot(reference, sample) < 0 ? -1.0 : 1.0;
            x += sample.X;
            y += sample.Y;
            z += sample.Z;
            qx += sign * sample.Qx;
            qy += sign * sample.Qy;
            qz += sign * sample.Qz;
            qw += sign * sample.Qw;
        }

        double count = relativeSamples.Count;
        CalibrationPose mean = Normalize(new CalibrationPose(
            x / count,
            y / count,
            z / count,
            qx / count,
            qy / count,
            qz / count,
            qw / count));
        double translationSquared = 0;
        double rotationSquared = 0;
        foreach (CalibrationPose rawSample in relativeSamples)
        {
            CalibrationPose sample = Normalize(rawSample);
            double dx = sample.X - mean.X;
            double dy = sample.Y - mean.Y;
            double dz = sample.Z - mean.Z;
            translationSquared += (dx * dx) + (dy * dy) + (dz * dz);
            double cosine = Math.Min(1.0, Math.Abs(Dot(mean, sample)));
            double angleDegrees = 2.0 * Math.Acos(cosine) * (180.0 / Math.PI);
            rotationSquared += angleDegrees * angleDegrees;
        }

        return new CalibrationComputation
        {
            Offset = new PoseOffset
            {
                TranslationX = mean.X * 100.0,
                TranslationY = mean.Y * 100.0,
                TranslationZ = mean.Z * 100.0,
                RotationX = mean.Qx,
                RotationY = mean.Qy,
                RotationZ = mean.Qz,
                RotationW = mean.Qw
            },
            TranslationRmsMetres = Math.Sqrt(translationSquared / count),
            RotationRmsDegrees = Math.Sqrt(rotationSquared / count)
        };
    }

    private static CalibrationPose Normalize(CalibrationPose pose)
    {
        double length = Math.Sqrt(
            (pose.Qx * pose.Qx) + (pose.Qy * pose.Qy) +
            (pose.Qz * pose.Qz) + (pose.Qw * pose.Qw));
        if (length < 1e-12 || !double.IsFinite(length))
        {
            throw new InvalidDataException("校准位姿包含无效旋转。");
        }
        return pose with
        {
            Qx = pose.Qx / length,
            Qy = pose.Qy / length,
            Qz = pose.Qz / length,
            Qw = pose.Qw / length
        };
    }

    private static double Dot(CalibrationPose left, CalibrationPose right)
    {
        return (left.Qx * right.Qx) + (left.Qy * right.Qy) +
            (left.Qz * right.Qz) + (left.Qw * right.Qw);
    }

    private static (double X, double Y, double Z, double W) Multiply(
        (double X, double Y, double Z, double W) left,
        (double X, double Y, double Z, double W) right)
    {
        return (
            (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
            (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
            (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W),
            (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z));
    }

    private static (double X, double Y, double Z) Rotate(
        (double X, double Y, double Z, double W) rotation,
        double x,
        double y,
        double z)
    {
        double tx = 2.0 * ((rotation.Y * z) - (rotation.Z * y));
        double ty = 2.0 * ((rotation.Z * x) - (rotation.X * z));
        double tz = 2.0 * ((rotation.X * y) - (rotation.Y * x));
        return (
            x + (rotation.W * tx) + ((rotation.Y * tz) - (rotation.Z * ty)),
            y + (rotation.W * ty) + ((rotation.Z * tx) - (rotation.X * tz)),
            z + (rotation.W * tz) + ((rotation.X * ty) - (rotation.Y * tx)));
    }
}
