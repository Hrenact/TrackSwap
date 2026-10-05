using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using TrackSwap.Models;

namespace TrackSwap
{
    internal static class DeviceViewerModelFactory
    {
        internal const string SpatialReferenceColor = "#2F2F2F";
        internal const string SkyBackgroundClearColor = "#000000";
        internal const string SelectionOutlineColor = "#5B9CFF";
        internal const double FloorGridRingSpacingMetres = 1.0;
        internal const double FloorGridRadiusMetres = 50.0;
        internal const double FloorGridFadeStartMetres = 0.0;
        internal const double FloorGridFadeEndMetres = 50.0;
        // SteamVR's grid shader evaluates angle * 5 / PI across a full turn,
        // producing ten radial sectors.
        internal const int FloorGridSpokeCount = 10;
        // The shader measures atan2(X, Z), so its zero-angle ray is +Z.
        // With ten sectors this is an 18-degree phase shift from the usual
        // cos/sin construction that begins on +X.
        internal const double FloorGridAngularOffsetDegrees = 18.0;

        internal static Model3DGroup CreateDeviceModel(
            OpenVrRenderModel renderModel,
            TrackedDeviceKind fallbackKind)
        {
            var group = new Model3DGroup();
            if (renderModel == null || renderModel.Vertices.Length == 0 || renderModel.Indices.Length == 0)
            {
                AddFallbackDeviceGeometry(group, fallbackKind);
            }
            else
            {
                group.Children.Add(CreateOpenVrModel(renderModel));
            }
            return group;
        }

        internal static Model3DGroup CreateSelectionOutline(
            Model3DGroup source,
            double thicknessMetres)
        {
            var outline = new Model3DGroup();
            if (source == null || source.Bounds.IsEmpty || thicknessMetres <= 0)
            {
                return outline;
            }

            Color color = (Color)ColorConverter.ConvertFromString(SelectionOutlineColor);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var material = new EmissiveMaterial(brush);
            material.Freeze();

            foreach (Model3D child in source.Children)
            {
                Model3D expanded = CreateOutlineModel(child, thicknessMetres, material);
                if (expanded != null)
                {
                    outline.Children.Add(expanded);
                }
            }
            return outline;
        }

        private static Model3D CreateOutlineModel(
            Model3D source,
            double thickness,
            Material material)
        {
            if (source is Model3DGroup sourceGroup)
            {
                var outlineGroup = new Model3DGroup { Transform = sourceGroup.Transform };
                foreach (Model3D child in sourceGroup.Children)
                {
                    Model3D expanded = CreateOutlineModel(child, thickness, material);
                    if (expanded != null)
                    {
                        outlineGroup.Children.Add(expanded);
                    }
                }
                return outlineGroup;
            }

            if (!(source is GeometryModel3D geometry) ||
                !(geometry.Geometry is MeshGeometry3D mesh) ||
                mesh.Positions == null ||
                mesh.Positions.Count == 0 ||
                mesh.TriangleIndices == null ||
                mesh.TriangleIndices.Count < 3)
            {
                return null;
            }

            Rect3D bounds = mesh.Bounds;
            var centre = new Point3D(
                bounds.X + (bounds.SizeX / 2.0),
                bounds.Y + (bounds.SizeY / 2.0),
                bounds.Z + (bounds.SizeZ / 2.0));
            bool hasNormals = mesh.Normals != null &&
                mesh.Normals.Count == mesh.Positions.Count;
            var positions = new Point3DCollection(mesh.Positions.Count);
            for (int index = 0; index < mesh.Positions.Count; index++)
            {
                Point3D position = mesh.Positions[index];
                Vector3D direction = hasNormals
                    ? mesh.Normals[index]
                    : position - centre;
                if (direction.LengthSquared < 1e-12)
                {
                    direction = position - centre;
                }
                if (direction.LengthSquared >= 1e-12)
                {
                    direction.Normalize();
                    position += direction * thickness;
                }
                positions.Add(position);
            }

            var outlineIndices = new Int32Collection(mesh.TriangleIndices.Count);
            for (int triangleIndex = 0;
                triangleIndex < mesh.TriangleIndices.Count;
                triangleIndex++)
            {
                outlineIndices.Add(mesh.TriangleIndices[triangleIndex]);
            }

            var outlineMesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = outlineIndices
            };
            return new GeometryModel3D(outlineMesh, null)
            {
                BackMaterial = material,
                Transform = geometry.Transform
            };
        }

        internal static Model3DGroup CreateFloorReferenceModel()
        {
            const int ringSegments = 96;
            const double floorHeight = 0.002;
            const double lineWidth = 0.006;

            var mesh = new MeshGeometry3D();
            for (
                double ringRadius = FloorGridRingSpacingMetres;
                ringRadius <= FloorGridRadiusMetres;
                ringRadius += FloorGridRingSpacingMetres)
            {
                Point3D previous = FloorPoint(ringRadius, 0, floorHeight);
                for (int segment = 1; segment <= ringSegments; segment++)
                {
                    double angle = (Math.PI * 2.0 * segment) / ringSegments;
                    Point3D current = FloorPoint(ringRadius, angle, floorHeight);
                    AppendFloorRibbon(mesh, previous, current, lineWidth);
                    previous = current;
                }
            }

            Point3D center = new Point3D(0, floorHeight, 0);
            for (int spoke = 0; spoke < FloorGridSpokeCount; spoke++)
            {
                double angle =
                    ((Math.PI * 2.0 * spoke) / FloorGridSpokeCount) +
                    (FloorGridAngularOffsetDegrees * Math.PI / 180.0);
                AppendFloorRibbon(
                    mesh,
                    center,
                    FloorPoint(FloorGridRadiusMetres, angle, floorHeight),
                    lineWidth);
            }

            var material = new EmissiveMaterial(CreateFloorGridBrush());
            var group = new Model3DGroup();
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
            return group;
        }

        private static Brush CreateFloorGridBrush()
        {
            Color baseColor = (Color)ColorConverter.ConvertFromString(SpatialReferenceColor);
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                SpreadMethod = GradientSpreadMethod.Pad
            };
            int fadeSteps = (int)Math.Round(
                FloorGridFadeEndMetres - FloorGridFadeStartMetres);
            for (int step = 0; step <= fadeSteps; step++)
            {
                double offset = fadeSteps == 0 ? 1.0 : (double)step / fadeSteps;
                double opacity = Math.Pow(1.0 - offset, 2.0);
                brush.GradientStops.Add(new GradientStop(
                    Color.FromArgb(
                        (byte)Math.Round(byte.MaxValue * opacity),
                        baseColor.R,
                        baseColor.G,
                        baseColor.B),
                    offset));
            }
            brush.Freeze();
            return brush;
        }

        internal static Model3DGroup CreateSkySphereModel(ImageSource image)
        {
            const double radius = 100.0;
            const int longitudeSegments = 256;
            const int latitudeSegments = 128;

            var positions = new Point3DCollection();
            var textureCoordinates = new PointCollection();
            var triangleIndices = new Int32Collection();
            for (int latitudeIndex = 0; latitudeIndex <= latitudeSegments; latitudeIndex++)
            {
                double v = (double)latitudeIndex / latitudeSegments;
                double latitude = (Math.PI / 2.0) - (v * Math.PI);
                double horizontalRadius = Math.Cos(latitude) * radius;
                double y = Math.Sin(latitude) * radius;
                for (int longitudeIndex = 0; longitudeIndex <= longitudeSegments; longitudeIndex++)
                {
                    double u = (double)longitudeIndex / longitudeSegments;
                    double longitude = (u * Math.PI * 2.0) - Math.PI;
                    positions.Add(new Point3D(
                        Math.Sin(longitude) * horizontalRadius,
                        y,
                        -Math.Cos(longitude) * horizontalRadius));
                    textureCoordinates.Add(new Point(u, v));
                }
            }

            int rowWidth = longitudeSegments + 1;
            for (int latitudeIndex = 0; latitudeIndex < latitudeSegments; latitudeIndex++)
            {
                for (int longitudeIndex = 0; longitudeIndex < longitudeSegments; longitudeIndex++)
                {
                    int topLeft = (latitudeIndex * rowWidth) + longitudeIndex;
                    int bottomLeft = topLeft + rowWidth;
                    triangleIndices.Add(topLeft);
                    triangleIndices.Add(bottomLeft);
                    triangleIndices.Add(topLeft + 1);
                    triangleIndices.Add(topLeft + 1);
                    triangleIndices.Add(bottomLeft);
                    triangleIndices.Add(bottomLeft + 1);
                }
            }

            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            var material = new EmissiveMaterial(brush);
            material.Freeze();
            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TextureCoordinates = textureCoordinates,
                TriangleIndices = triangleIndices
            };
            var sphere = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };
            var group = new Model3DGroup();
            group.Children.Add(sphere);
            return group;
        }

        private static Point3D FloorPoint(double radius, double angle, double height)
        {
            return new Point3D(
                Math.Cos(angle) * radius,
                height,
                Math.Sin(angle) * radius);
        }

        private static void AppendFloorRibbon(
            MeshGeometry3D mesh,
            Point3D start,
            Point3D end,
            double width)
        {
            Vector3D direction = end - start;
            direction.Y = 0;
            if (direction.LengthSquared < 1e-12)
            {
                return;
            }
            direction.Normalize();
            Vector3D side = Vector3D.CrossProduct(new Vector3D(0, 1, 0), direction);
            side.Normalize();
            side *= width / 2.0;

            int first = mesh.Positions.Count;
            mesh.Positions.Add(start - side);
            mesh.Positions.Add(start + side);
            mesh.Positions.Add(end + side);
            mesh.Positions.Add(end - side);
            mesh.TextureCoordinates.Add(FloorTextureCoordinate(start - side));
            mesh.TextureCoordinates.Add(FloorTextureCoordinate(start + side));
            mesh.TextureCoordinates.Add(FloorTextureCoordinate(end + side));
            mesh.TextureCoordinates.Add(FloorTextureCoordinate(end - side));
            mesh.TriangleIndices.Add(first);
            mesh.TriangleIndices.Add(first + 1);
            mesh.TriangleIndices.Add(first + 2);
            mesh.TriangleIndices.Add(first);
            mesh.TriangleIndices.Add(first + 2);
            mesh.TriangleIndices.Add(first + 3);
        }

        private static Point FloorTextureCoordinate(Point3D point)
        {
            double diameter = FloorGridRadiusMetres * 2.0;
            return new Point(
                (point.X + FloorGridRadiusMetres) / diameter,
                (point.Z + FloorGridRadiusMetres) / diameter);
        }

        private static GeometryModel3D CreateOpenVrModel(OpenVrRenderModel renderModel)
        {
            var positions = new Point3DCollection(renderModel.Vertices.Length);
            var normals = new Vector3DCollection(renderModel.Vertices.Length);
            var textureCoordinates = new PointCollection(renderModel.Vertices.Length);
            foreach (OpenVrRenderVertex vertex in renderModel.Vertices)
            {
                positions.Add(new Point3D(vertex.PositionX, vertex.PositionY, vertex.PositionZ));
                normals.Add(new Vector3D(vertex.NormalX, vertex.NormalY, vertex.NormalZ));
                textureCoordinates.Add(new Point(
                    ClampTextureCoordinate(vertex.TextureU),
                    ClampTextureCoordinate(vertex.TextureV)));
            }

            var triangleIndices = new Int32Collection(renderModel.Indices.Length);
            foreach (ushort index in renderModel.Indices)
            {
                triangleIndices.Add(index);
            }

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                Normals = normals,
                TextureCoordinates = textureCoordinates,
                TriangleIndices = triangleIndices
            };
            Material material;
            if (renderModel.TextureWidth > 0 && renderModel.TextureHeight > 0 &&
                renderModel.TextureRgba.Length > 0)
            {
                byte[] bgra = new byte[renderModel.TextureRgba.Length];
                for (int index = 0; index < bgra.Length; index += 4)
                {
                    bgra[index] = renderModel.TextureRgba[index + 2];
                    bgra[index + 1] = renderModel.TextureRgba[index + 1];
                    bgra[index + 2] = renderModel.TextureRgba[index];
                    bgra[index + 3] = byte.MaxValue;
                }
                BitmapSource bitmap = BitmapSource.Create(
                    renderModel.TextureWidth,
                    renderModel.TextureHeight,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    bgra,
                    checked(renderModel.TextureWidth * 4));
                bitmap.Freeze();
                var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                brush.Freeze();
                material = new DiffuseMaterial(brush);
            }
            else
            {
                material = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
            }
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static void AddFallbackDeviceGeometry(
            Model3DGroup group,
            TrackedDeviceKind deviceKind)
        {
            Color color = (Color)ColorConverter.ConvertFromString("#929292");
            switch (deviceKind)
            {
                case TrackedDeviceKind.Hmd:
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, 0, -0.015),
                        new Vector3D(0.19, 0.09, 0.08),
                        color));
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, 0.01, 0.04),
                        new Vector3D(0.12, 0.035, 0.05),
                        color));
                    break;
                case TrackedDeviceKind.Controller:
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, -0.055, 0.015),
                        new Vector3D(0.035, 0.13, 0.04),
                        color));
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, 0.025, -0.005),
                        new Vector3D(0.075, 0.045, 0.075),
                        color));
                    break;
                case TrackedDeviceKind.Tracker:
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, 0, 0),
                        new Vector3D(0.085, 0.03, 0.085),
                        color));
                    break;
                default:
                    group.Children.Add(CreateBoxModel(
                        new Point3D(0, 0, 0),
                        new Vector3D(0.075, 0.075, 0.075),
                        color));
                    break;
            }
        }

        private static GeometryModel3D CreateSegmentModel(
            Point3D start,
            Point3D end,
            double thickness,
            Color color)
        {
            Vector3D direction = end - start;
            if (direction.LengthSquared < 1e-12)
            {
                return CreateBoxModel(start, new Vector3D(thickness, thickness, thickness), color);
            }
            direction.Normalize();
            Vector3D helper = Math.Abs(Vector3D.DotProduct(direction, new Vector3D(0, 1, 0))) > 0.95
                ? new Vector3D(1, 0, 0)
                : new Vector3D(0, 1, 0);
            Vector3D side = Vector3D.CrossProduct(direction, helper);
            side.Normalize();
            Vector3D up = Vector3D.CrossProduct(side, direction);
            up.Normalize();
            side *= thickness / 2.0;
            up *= thickness / 2.0;

            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection
                {
                    start - side - up,
                    start + side - up,
                    start + side + up,
                    start - side + up,
                    end - side - up,
                    end + side - up,
                    end + side + up,
                    end - side + up
                },
                TriangleIndices = BoxTriangleIndices()
            };
            var material = new EmissiveMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static GeometryModel3D CreateBoxModel(
            Point3D center,
            Vector3D size,
            Color color)
        {
            double x = size.X / 2;
            double y = size.Y / 2;
            double z = size.Z / 2;
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection
                {
                    new Point3D(center.X - x, center.Y - y, center.Z - z),
                    new Point3D(center.X + x, center.Y - y, center.Z - z),
                    new Point3D(center.X + x, center.Y + y, center.Z - z),
                    new Point3D(center.X - x, center.Y + y, center.Z - z),
                    new Point3D(center.X - x, center.Y - y, center.Z + z),
                    new Point3D(center.X + x, center.Y - y, center.Z + z),
                    new Point3D(center.X + x, center.Y + y, center.Z + z),
                    new Point3D(center.X - x, center.Y + y, center.Z + z)
                },
                TriangleIndices = BoxTriangleIndices()
            };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static Int32Collection BoxTriangleIndices()
        {
            return new Int32Collection
            {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                0,1,5, 0,5,4, 2,3,7, 2,7,6,
                1,2,6, 1,6,5, 3,0,4, 3,4,7
            };
        }

        private static double ClampTextureCoordinate(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
