using System;
using System.IO;
using System.Windows.Media.Media3D;
using TrackSwap.Services;
using Xunit;

namespace TrackSwap.Tests
{
    public sealed class PoseRotationConverterTests
    {
        [Theory]
        [InlineData(0.0, 0.0, 0.0)]
        [InlineData(90.0, 0.0, 0.0)]
        [InlineData(0.0, 90.0, 0.0)]
        [InlineData(0.0, 0.0, 90.0)]
        [InlineData(28.0, -37.0, 116.0)]
        [InlineData(-145.0, 89.999, 42.0)]
        public void EulerQuaternionRoundTripPreservesRotation(double x, double y, double z)
        {
            Quaternion expected = PoseRotationConverter.FromEulerDegrees(x, y, z);

            Vector3D euler = PoseRotationConverter.ToEulerDegrees(expected);
            Quaternion actual = PoseRotationConverter.FromEulerDegrees(euler.X, euler.Y, euler.Z);

            AssertEquivalent(expected, actual);
            Assert.InRange(Length(actual), 0.999999999, 1.000000001);
        }

        [Theory]
        [InlineData(90.0, 0.0, 0.0, 0.7071067811865475, 0.0, 0.0, 0.7071067811865475)]
        [InlineData(0.0, 90.0, 0.0, 0.0, 0.7071067811865475, 0.0, 0.7071067811865475)]
        [InlineData(0.0, 0.0, 90.0, 0.0, 0.0, 0.7071067811865475, 0.7071067811865475)]
        public void SingleAxisAnglesMapToExpectedQuaternion(
            double x,
            double y,
            double z,
            double expectedX,
            double expectedY,
            double expectedZ,
            double expectedW)
        {
            Quaternion actual = PoseRotationConverter.FromEulerDegrees(x, y, z);

            Assert.Equal(expectedX, actual.X, 10);
            Assert.Equal(expectedY, actual.Y, 10);
            Assert.Equal(expectedZ, actual.Z, 10);
            Assert.Equal(expectedW, actual.W, 10);
        }

        [Theory]
        [InlineData(35.0, 90.0, -20.0)]
        [InlineData(-70.0, -90.0, 125.0)]
        public void GimbalLockUsesStableEquivalentRepresentation(double x, double y, double z)
        {
            Quaternion expected = PoseRotationConverter.FromEulerDegrees(x, y, z);

            Vector3D euler = PoseRotationConverter.ToEulerDegrees(expected);
            Quaternion actual = PoseRotationConverter.FromEulerDegrees(euler.X, euler.Y, euler.Z);

            Assert.Equal(0.0, euler.X, 8);
            AssertEquivalent(expected, actual);
        }

        [Fact]
        public void UiPreferencesDefaultToEulerAndPersistQuaternionChoice()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TrackSwapTests", Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "ui-preferences.json");
            try
            {
                var service = new UiPreferencesService(path);
                Assert.True(service.Load().UseEulerRotationEditor);

                service.Save(new UiPreferences { UseEulerRotationEditor = false });

                Assert.False(service.Load().UseEulerRotationEditor);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static void AssertEquivalent(Quaternion expected, Quaternion actual)
        {
            expected.Normalize();
            actual.Normalize();
            double dot = Math.Abs(
                (expected.X * actual.X) + (expected.Y * actual.Y) +
                (expected.Z * actual.Z) + (expected.W * actual.W));
            Assert.InRange(dot, 0.99999999, 1.00000001);
        }

        private static double Length(Quaternion value)
        {
            return Math.Sqrt(
                (value.X * value.X) + (value.Y * value.Y) +
                (value.Z * value.Z) + (value.W * value.W));
        }
    }
}
