using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class CalibrationMathTests
{
    [Fact]
    public void RelativePoseRecoversLocalTranslationAndRotation()
    {
        double halfSqrtTwo = Math.Sqrt(0.5);
        var source = new CalibrationPose(2, 3, 4, 0, halfSqrtTwo, 0, halfSqrtTwo);
        var target = new CalibrationPose(2, 3, 3, 0, 1, 0, 0);

        CalibrationPose relative = CalibrationMath.Relative(source, target);

        Assert.Equal(1, relative.X, 9);
        Assert.Equal(0, relative.Y, 9);
        Assert.Equal(0, relative.Z, 9);
        Assert.Equal(0, relative.Qx, 9);
        Assert.Equal(halfSqrtTwo, relative.Qy, 9);
        Assert.Equal(0, relative.Qz, 9);
        Assert.Equal(halfSqrtTwo, relative.Qw, 9);
    }

    [Fact]
    public void AverageTreatsQuaternionSignsAsEquivalent()
    {
        var samples = new[]
        {
            new CalibrationPose(0.1, -0.2, 0.3, 0, 0, 0, 1),
            new CalibrationPose(0.1, -0.2, 0.3, 0, 0, 0, -1)
        };

        CalibrationComputation result = CalibrationMath.Average(samples);

        Assert.Equal(10, result.Offset.TranslationX, 9);
        Assert.Equal(-20, result.Offset.TranslationY, 9);
        Assert.Equal(30, result.Offset.TranslationZ, 9);
        Assert.Equal(1, Math.Abs(result.Offset.RotationW), 9);
        Assert.Equal(0, result.TranslationRmsMetres, 9);
        Assert.Equal(0, result.RotationRmsDegrees, 9);
    }

    [Fact]
    public void AverageReportsRelativeInstability()
    {
        var samples = new[]
        {
            new CalibrationPose(0, 0, 0, 0, 0, 0, 1),
            new CalibrationPose(0.02, 0, 0, 0, 0, Math.Sin(Math.PI / 180), Math.Cos(Math.PI / 180))
        };

        CalibrationComputation result = CalibrationMath.Average(samples);

        Assert.Equal(0.01, result.TranslationRmsMetres, 9);
        Assert.Equal(1.0, result.RotationRmsDegrees, 6);
    }
}
