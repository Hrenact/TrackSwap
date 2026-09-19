using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrackSwap.Protocol
{
    public static class ConfigurationValidator
    {
        private const double MaximumTranslationMetres = 10.0;
        private const double MinimumQuaternionLengthSquared = 1e-12;

        public static IReadOnlyList<string> Validate(RuntimeConfiguration? configuration)
        {
            var errors = new List<string>();
            if (configuration == null)
            {
                errors.Add("Configuration is required.");
                return errors;
            }

            if (configuration.SchemaVersion != ProtocolConstants.CurrentConfigurationSchemaVersion)
            {
                errors.Add($"Unsupported schema version {configuration.SchemaVersion}.");
            }

            if (configuration.Revision < 0)
            {
                errors.Add("Revision cannot be negative.");
            }

            if (configuration.Routes == null)
            {
                errors.Add("Routes are required.");
                return errors;
            }

            if (configuration.Routes.Count > ProtocolConstants.MaximumRoutes)
            {
                errors.Add($"At most {ProtocolConstants.MaximumRoutes} routes are supported.");
            }

            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<int>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);

            foreach (RouteConfiguration? route in configuration.Routes)
            {
                if (route == null)
                {
                    errors.Add("Routes cannot contain null entries.");
                    continue;
                }

                string prefix = string.IsNullOrWhiteSpace(route.RouteId) ? "Route" : $"Route '{route.RouteId}'";
                if (string.IsNullOrWhiteSpace(route.RouteId))
                {
                    errors.Add("Every route must have a stable routeId.");
                }
                else if (route.RouteId.Length > ProtocolConstants.MaximumRouteIdCharacters ||
                    route.RouteId.Any(char.IsControl))
                {
                    errors.Add($"{prefix} routeId is too long or contains control characters.");
                }
                else if (!routeIds.Add(route.RouteId))
                {
                    errors.Add($"Duplicate routeId '{route.RouteId}'.");
                }

                if (route.VirtualDeviceSlot < 0 || route.VirtualDeviceSlot >= ProtocolConstants.MaximumRoutes)
                {
                    errors.Add($"{prefix} has an invalid virtualDeviceSlot.");
                }
                else if (!slots.Add(route.VirtualDeviceSlot))
                {
                    errors.Add($"Virtual device slot {route.VirtualDeviceSlot} is assigned more than once.");
                }

                ValidateSourcePath(route.SourceDevicePath, prefix, errors);
                ValidateTargetPath(route.TargetDevicePath, prefix, errors);
                ValidateCombinedPathSize(route.SourceDevicePath, route.TargetDevicePath, prefix, errors);

                if (!string.IsNullOrEmpty(route.SourceDevicePath) &&
                    string.Equals(route.SourceDevicePath, route.TargetDevicePath, StringComparison.Ordinal))
                {
                    errors.Add($"{prefix} cannot map a device to itself.");
                }

                if (!string.IsNullOrWhiteSpace(route.TargetDevicePath) && !targets.Add(route.TargetDevicePath))
                {
                    errors.Add($"Target '{route.TargetDevicePath}' is assigned more than once.");
                }

                if (!string.IsNullOrWhiteSpace(route.SourceDevicePath) && !sources.Add(route.SourceDevicePath))
                {
                    errors.Add($"Source '{route.SourceDevicePath}' is assigned more than once.");
                }

                ValidateOffset(route.Offset, prefix, errors);
            }

            ValidateCycles(configuration.Routes, errors);

            return errors;
        }

        private static void ValidateCycles(IEnumerable<RouteConfiguration> routes, ICollection<string> errors)
        {
            var edges = routes
                .Where(route => route != null && route.Enabled &&
                    !string.IsNullOrWhiteSpace(route.SourceDevicePath) &&
                    !string.IsNullOrWhiteSpace(route.TargetDevicePath))
                .GroupBy(route => route.SourceDevicePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().TargetDevicePath, StringComparer.Ordinal);

            foreach (string start in edges.Keys)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                string current = start;
                while (edges.TryGetValue(current, out string? next))
                {
                    if (!visited.Add(current))
                    {
                        errors.Add($"Route cycle detected at '{current}'.");
                        return;
                    }

                    current = next;
                }
            }
        }

        private static void ValidateSourcePath(string? path, string prefix, ICollection<string> errors)
        {
            if (path == null || string.IsNullOrWhiteSpace(path) || !path.StartsWith("/devices/", StringComparison.Ordinal))
            {
                errors.Add($"{prefix} sourceDevicePath must be an exact /devices/ path returned by OpenVR.");
            }
            else if (path.Any(char.IsControl))
            {
                errors.Add($"{prefix} sourceDevicePath cannot contain control characters.");
            }
        }

        private static void ValidateTargetPath(string? path, string prefix, ICollection<string> errors)
        {
            bool supportedRole = string.Equals(path, ProtocolConstants.HeadRolePath, StringComparison.Ordinal) ||
                string.Equals(path, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal) ||
                string.Equals(path, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal);
            if (path == null || (!path.StartsWith("/devices/", StringComparison.Ordinal) && !supportedRole))
            {
                errors.Add($"{prefix} targetDevicePath must be an exact /devices/ path or a supported head/hand role path.");
            }
            else if (path.Any(char.IsControl))
            {
                errors.Add($"{prefix} targetDevicePath cannot contain control characters.");
            }
        }

        private static void ValidateCombinedPathSize(
            string? sourcePath,
            string? targetPath,
            string prefix,
            ICollection<string> errors)
        {
            if (sourcePath == null || targetPath == null)
            {
                return;
            }

            int encodedBytes = Encoding.UTF8.GetByteCount(sourcePath) + Encoding.UTF8.GetByteCount(targetPath);
            if (encodedBytes > DriverControlProtocol.MaximumCombinedDevicePathBytes)
            {
                errors.Add($"{prefix} encoded source and target paths exceed the driver control limit.");
            }
        }

        private static void ValidateOffset(PoseOffset? offset, string prefix, ICollection<string> errors)
        {
            if (offset == null)
            {
                errors.Add($"{prefix} offset is required.");
                return;
            }

            double[] values =
            {
                offset.TranslationX, offset.TranslationY, offset.TranslationZ,
                offset.RotationX, offset.RotationY, offset.RotationZ, offset.RotationW
            };
            if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
            {
                errors.Add($"{prefix} offset must contain only finite values.");
                return;
            }

            if (Math.Abs(offset.TranslationX) > MaximumTranslationMetres ||
                Math.Abs(offset.TranslationY) > MaximumTranslationMetres ||
                Math.Abs(offset.TranslationZ) > MaximumTranslationMetres)
            {
                errors.Add($"{prefix} translation exceeds the {MaximumTranslationMetres} metre safety bound.");
            }

            double lengthSquared =
                (offset.RotationX * offset.RotationX) +
                (offset.RotationY * offset.RotationY) +
                (offset.RotationZ * offset.RotationZ) +
                (offset.RotationW * offset.RotationW);
            if (lengthSquared < MinimumQuaternionLengthSquared)
            {
                errors.Add($"{prefix} rotation quaternion cannot be zero length.");
            }
        }
    }
}
