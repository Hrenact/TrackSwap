using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using TrackSwap.Models;
using TrackSwap;
using TrackSwap.Services;
using Xunit;

namespace TrackSwap.Tests
{
    public sealed class OpenVrSceneServiceTests
    {
        [Fact]
        public void UsesPinnedOpenVrInteropLayout()
        {
            Assert.Equal(12, OpenVrSceneService.GetDeviceToAbsoluteTrackingPoseIndex);
            Assert.Equal(80, OpenVrSceneService.TrackedDevicePoseBytes);
            Assert.Equal("FnTable:IVRSettings_003", OpenVrBackgroundService.SettingsInterfaceVersion);
            Assert.Equal(8, OpenVrBackgroundService.GetStringIndex);
        }

        [Fact]
        public void MotionIntensityIgnoresTrackingNoiseAndScalesWithMovement()
        {
            double idle = DeviceMotionIntensityFilter.Update(
                0.0,
                DeviceMotionIntensityFilter.LinearDeadZoneMetresPerSecond * 0.5,
                DeviceMotionIntensityFilter.AngularDeadZoneRadiansPerSecond * 0.5,
                1.0 / 30.0);
            double gentle = DeviceMotionIntensityFilter.Update(
                0.0,
                0.35,
                0.0,
                1.0 / 30.0);
            double vigorous = DeviceMotionIntensityFilter.Update(
                0.0,
                1.25,
                0.0,
                1.0 / 30.0);
            double rotation = DeviceMotionIntensityFilter.Update(
                0.0,
                0.0,
                6.0,
                1.0 / 30.0);

            Assert.Equal(0.0, idle, 6);
            Assert.InRange(gentle, 0.0, vigorous);
            Assert.InRange(vigorous, 0.4, 1.0);
            Assert.Equal(vigorous, rotation, 6);
        }

        [Fact]
        public void MotionIntensityAttacksQuicklyAndDecaysSmoothly()
        {
            double first = DeviceMotionIntensityFilter.Update(0.0, 1.25, 0.0, 1.0 / 30.0);
            double second = DeviceMotionIntensityFilter.Update(first, 1.25, 0.0, 1.0 / 30.0);
            double released = DeviceMotionIntensityFilter.Update(second, 0.0, 0.0, 1.0 / 30.0);

            Assert.True(second > first);
            Assert.True(released < second);
            Assert.True(released > 0.0);
            Assert.True(second - first > second - released);
        }

        [Fact]
        public void ConvertsOpenVrColumnTransformToWpfMatrix()
        {
            var source = new OpenVrSceneService.HmdMatrix34
            {
                M00 = 0,
                M01 = 0,
                M02 = 1,
                M03 = 1.25f,
                M10 = 0,
                M11 = 1,
                M12 = 0,
                M13 = 2.5f,
                M20 = -1,
                M21 = 0,
                M22 = 0,
                M23 = -3.75f
            };

            Matrix3D converted = OpenVrSceneService.ToMatrix3D(source);
            Point3D transformed = converted.Transform(new Point3D(2, 3, 4));

            Assert.Equal(5.25, transformed.X, 6);
            Assert.Equal(5.5, transformed.Y, 6);
            Assert.Equal(-5.75, transformed.Z, 6);
        }

        [Fact]
        public void CreatesFloorReferenceGridWithoutChangingSceneScale()
        {
            Model3DGroup grid = DeviceViewerModelFactory.CreateFloorReferenceModel();

            Assert.Equal(1.0, DeviceViewerModelFactory.FloorGridRingSpacingMetres);
            Assert.Equal(50.0, DeviceViewerModelFactory.FloorGridRadiusMetres);
            Assert.Equal(0.0, DeviceViewerModelFactory.FloorGridFadeStartMetres);
            Assert.Equal(50.0, DeviceViewerModelFactory.FloorGridFadeEndMetres);
            Assert.Equal(10, DeviceViewerModelFactory.FloorGridSpokeCount);
            Assert.Equal(18.0, DeviceViewerModelFactory.FloorGridAngularOffsetDegrees);
            Assert.Single(grid.Children);
            Assert.InRange(grid.Bounds.X, -50.004, -50.0);
            Assert.InRange(grid.Bounds.Z, -50.004, -50.0);
            Assert.InRange(grid.Bounds.SizeX, 100.0, 100.008);
            Assert.InRange(grid.Bounds.SizeZ, 100.0, 100.008);
            Assert.InRange(grid.Bounds.Y, 0.001, 0.003);

            var geometry = Assert.IsType<GeometryModel3D>(grid.Children[0]);
            var material = Assert.IsType<EmissiveMaterial>(geometry.Material);
            var brush = Assert.IsType<RadialGradientBrush>(material.Brush);
            Assert.Equal(51, brush.GradientStops.Count);
            Assert.Equal(byte.MaxValue, brush.GradientStops[0].Color.A);
            Assert.Equal(0, brush.GradientStops[50].Color.A);
        }

        [Fact]
        public void UsesDarkGrayColorForFloorGrid()
        {
            Color color = (Color)ColorConverter.ConvertFromString(
                DeviceViewerModelFactory.SpatialReferenceColor);

            Assert.InRange(color.R, (byte)32, (byte)64);
            Assert.Equal(color.R, color.G);
            Assert.Equal(color.G, color.B);
        }

        [Fact]
        public void CreatesExpandedBackFaceSelectionOutline()
        {
            Model3DGroup device = DeviceViewerModelFactory.CreateDeviceModel(
                null,
                TrackedDeviceKind.Tracker);
            var sourceGeometry = Assert.IsType<GeometryModel3D>(device.Children[0]);
            var sourceMesh = Assert.IsType<MeshGeometry3D>(sourceGeometry.Geometry);

            Model3DGroup outline = DeviceViewerModelFactory.CreateSelectionOutline(device, 0.004);

            var outlineGeometry = Assert.IsType<GeometryModel3D>(Assert.Single(outline.Children));
            var outlineMesh = Assert.IsType<MeshGeometry3D>(outlineGeometry.Geometry);
            Assert.Null(outlineGeometry.Material);
            var material = Assert.IsType<EmissiveMaterial>(outlineGeometry.BackMaterial);
            var brush = Assert.IsType<SolidColorBrush>(material.Brush);
            Assert.Equal(
                (Color)ColorConverter.ConvertFromString(DeviceViewerModelFactory.SelectionOutlineColor),
                brush.Color);
            Assert.True(outline.Bounds.SizeX > device.Bounds.SizeX);
            Assert.True(outline.Bounds.SizeY > device.Bounds.SizeY);
            Assert.True(outline.Bounds.SizeZ > device.Bounds.SizeZ);
            Assert.Equal(sourceMesh.TriangleIndices[0], outlineMesh.TriangleIndices[0]);
            Assert.Equal(sourceMesh.TriangleIndices[1], outlineMesh.TriangleIndices[1]);
            Assert.Equal(sourceMesh.TriangleIndices[2], outlineMesh.TriangleIndices[2]);
        }

        [Fact]
        public void SelectionOutlineTracksProjectedPixelWidth()
        {
            double near = DeviceViewerWindow.CalculateSelectionOutlineThickness(4, 1100, 46);
            double far = DeviceViewerWindow.CalculateSelectionOutlineThickness(8, 1100, 46);

            Assert.InRange(near, 0.007, 0.009);
            Assert.Equal(near * 2.0, far, 6);
            Assert.Equal(
                0.0015,
                DeviceViewerWindow.CalculateSelectionOutlineThickness(0.05, 1100, 46),
                6);
            Assert.Equal(
                0.04,
                DeviceViewerWindow.CalculateSelectionOutlineThickness(100, 1100, 46),
                6);
        }

        [Fact]
        public void ParsesSteamVrRgbaBackgroundColors()
        {
            Assert.True(OpenVrBackgroundService.TryParseSteamVrColor(
                "#10203080",
                out Color color));
            Assert.Equal(0x10, color.R);
            Assert.Equal(0x20, color.G);
            Assert.Equal(0x30, color.B);
            Assert.Equal(0x80, color.A);
            Assert.False(OpenVrBackgroundService.TryParseSteamVrColor(
                "#102030",
                out _));
        }

        [Fact]
        public void ResolvesEmptyBackgroundToInstalledSteamVrDefault()
        {
            string runtimePath = Path.Combine(
                Path.GetTempPath(),
                "TrackSwap-background-" + Guid.NewGuid().ToString("N"));
            string imagePath = Path.Combine(
                runtimePath,
                "resources",
                "backgrounds",
                "aurorasky.png");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
                File.WriteAllBytes(imagePath, new byte[] { 0 });

                Assert.Equal(
                    imagePath,
                    OpenVrBackgroundService.ResolveBackgroundImagePath(runtimePath, string.Empty));
            }
            finally
            {
                if (Directory.Exists(runtimePath))
                {
                    Directory.Delete(runtimePath, true);
                }
            }
        }

        [Fact]
        public void CreatesCameraCentredSkySphere()
        {
            BitmapSource image = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 0, 0, 0, 255 },
                4);
            image.Freeze();

            Model3DGroup sky = DeviceViewerModelFactory.CreateSkySphereModel(image);

            Assert.Single(sky.Children);
            Assert.InRange(sky.Bounds.SizeX, 199.99, 200.01);
            Assert.InRange(sky.Bounds.SizeY, 199.99, 200.01);
            Assert.InRange(sky.Bounds.SizeZ, 199.99, 200.01);

            var geometry = Assert.IsType<GeometryModel3D>(sky.Children[0]);
            MeshGeometry3D mesh = Assert.IsType<MeshGeometry3D>(geometry.Geometry);
            var material = Assert.IsType<EmissiveMaterial>(geometry.Material);
            var brush = Assert.IsType<ImageBrush>(material.Brush);
            Assert.Equal(
                BitmapScalingMode.HighQuality,
                RenderOptions.GetBitmapScalingMode(brush));
            int centreIndex = -1;
            for (int index = 0; index < mesh.TextureCoordinates.Count; index++)
            {
                if (Math.Abs(mesh.TextureCoordinates[index].X - 0.5) < 1e-9 &&
                    Math.Abs(mesh.TextureCoordinates[index].Y - 0.5) < 1e-9)
                {
                    centreIndex = index;
                    break;
                }
            }
            Assert.True(centreIndex >= 0);
            Assert.InRange(mesh.Positions[centreIndex].X, -0.001, 0.001);
            Assert.InRange(mesh.Positions[centreIndex].Y, -0.001, 0.001);
            Assert.InRange(mesh.Positions[centreIndex].Z, -100.001, -99.999);
        }

        [Fact]
        public void UsesBlackViewportClearBehindSteamVrPanorama()
        {
            Color color = (Color)ColorConverter.ConvertFromString(
                DeviceViewerModelFactory.SkyBackgroundClearColor);

            Assert.Equal(Colors.Black, color);
        }

    }
}
