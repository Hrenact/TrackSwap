using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrackSwap.Protocol
{
    public static class ConfigurationValidator
    {
        private const double MaximumTranslationCentimetres = 1000.0;
        private const double MinimumQuaternionLengthSquared = 1e-12;

        public static IReadOnlyList<string> Validate(RuntimeConfiguration? configuration)
        {
            var errors = new List<string>();
            if (configuration == null)
            {
                errors.Add("配置不能为空。");
                return errors;
            }

            if (configuration.SchemaVersion != ProtocolConstants.CurrentConfigurationSchemaVersion)
            {
                errors.Add($"不支持配置架构版本 {configuration.SchemaVersion}。");
            }

            if (configuration.Revision < 0)
            {
                errors.Add("配置修订号不能为负数。");
            }

            if (configuration.Routes == null)
            {
                errors.Add("路由列表不能为空。");
                return errors;
            }

            if (configuration.Routes.Count > ProtocolConstants.MaximumRoutes)
            {
                errors.Add($"最多支持 {ProtocolConstants.MaximumRoutes} 条路由。");
            }

            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            var routeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var slots = new HashSet<int>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            var controllerHands = new HashSet<ControllerHand>();

            foreach (RouteConfiguration? route in configuration.Routes)
            {
                if (route == null)
                {
                    errors.Add("路由列表不能包含空项。");
                    continue;
                }

                string prefix = string.IsNullOrWhiteSpace(route.RouteId) ? "路由" : $"路由“{route.RouteId}”";
                if (string.IsNullOrWhiteSpace(route.RouteId))
                {
                    errors.Add("每条路由都必须具有稳定的 routeId。");
                }
                else if (route.RouteId.Length > ProtocolConstants.MaximumRouteIdCharacters ||
                    route.RouteId.Any(char.IsControl))
                {
                    errors.Add($"{prefix}的 routeId 过长或包含控制字符。");
                }
                else if (!routeIds.Add(route.RouteId))
                {
                    errors.Add($"routeId“{route.RouteId}”重复。");
                }

                if (!string.IsNullOrWhiteSpace(route.Name))
                {
                    string name = route.Name.Trim();
                    if (name.Length > 64 || name.Any(char.IsControl))
                    {
                        errors.Add($"{prefix}的名称过长或包含控制字符。");
                    }
                    else if (!routeNames.Add(name))
                    {
                        errors.Add($"配置名称“{name}”重复。");
                    }
                }

                if (route.VirtualDeviceSlot < 0 || route.VirtualDeviceSlot >= ProtocolConstants.MaximumRoutes)
                {
                    errors.Add($"{prefix}的 virtualDeviceSlot 无效。");
                }
                else if (!slots.Add(route.VirtualDeviceSlot))
                {
                    errors.Add($"虚拟设备槽位 {route.VirtualDeviceSlot} 被重复使用。");
                }

                ValidateSourcePath(route.SourceDevicePath, prefix, errors);
                if (route.SplitPoseSource)
                {
                    ValidateSourcePath(route.RotationSourceDevicePath, prefix + "的旋转来源", errors);
                    if (!string.IsNullOrWhiteSpace(route.SourceDevicePath) &&
                        string.Equals(route.SourceDevicePath, route.RotationSourceDevicePath, StringComparison.Ordinal))
                    {
                        errors.Add($"{prefix}拆分后的位置来源与旋转来源不能相同。");
                    }
                }
                if (route.HidePhysicalSource && !configuration.PhysicalSourceHidingEnabled)
                {
                    errors.Add($"{prefix}请求隐藏物理位姿来源，但全局设备隐藏功能尚未启用。");
                }
                if (route.HidePhysicalSource && ProtocolConstants.IsTrackSwapVirtualDevicePath(route.SourceDevicePath))
                {
                    errors.Add($"{prefix}不能隐藏 TrackSwap 自己创建的虚拟设备。");
                }
                if (route.HidePhysicalSource && route.SplitPoseSource &&
                    ProtocolConstants.IsTrackSwapVirtualDevicePath(route.RotationSourceDevicePath))
                {
                    errors.Add($"{prefix}不能隐藏 TrackSwap 自己创建的虚拟旋转来源。");
                }
                if (!Enum.IsDefined(typeof(RouteMode), route.Mode) || route.Mode == RouteMode.Unspecified)
                {
                    errors.Add($"{prefix}使用了不受支持的运行模式。");
                }
                if (route.Mode == RouteMode.ReplaceTarget)
                {
                    ValidateTargetPath(route.TargetDevicePath, prefix, errors);
                }
                else if (!string.IsNullOrEmpty(route.TargetDevicePath) && route.TargetDevicePath.Any(char.IsControl))
                {
                    errors.Add($"{prefix}的 targetDevicePath 不能包含控制字符。");
                }
                if (route.Mode == RouteMode.VirtualController)
                {
                    if (route.ControllerHand != ControllerHand.Left && route.ControllerHand != ControllerHand.Right)
                    {
                        errors.Add($"{prefix}必须选择左手或右手控制器。");
                    }
                    else if (!controllerHands.Add(route.ControllerHand))
                    {
                        errors.Add($"只能创建一只虚拟{(route.ControllerHand == ControllerHand.Left ? "左手" : "右手")}控制器。");
                    }
                    if (route.ControlInputSource != ControlInputSource.None &&
                        route.ControlInputSource != ControlInputSource.Osc &&
                        route.ControlInputSource != ControlInputSource.XInput)
                    {
                        errors.Add($"{prefix}使用了不受支持的控制输入来源。");
                    }
                }
                ValidateCombinedPathSize(
                    route.SourceDevicePath,
                    route.SplitPoseSource ? route.RotationSourceDevicePath : string.Empty,
                    route.TargetDevicePath,
                    prefix,
                    errors);

                if (route.Mode == RouteMode.ReplaceTarget &&
                    !string.IsNullOrEmpty(route.SourceDevicePath) &&
                    string.Equals(route.SourceDevicePath, route.TargetDevicePath, StringComparison.Ordinal))
                {
                    errors.Add($"{prefix}不能把设备映射到自身。");
                }
                if (route.Mode == RouteMode.ReplaceTarget && route.SplitPoseSource &&
                    !string.IsNullOrEmpty(route.RotationSourceDevicePath) &&
                    string.Equals(route.RotationSourceDevicePath, route.TargetDevicePath, StringComparison.Ordinal))
                {
                    errors.Add($"{prefix}不能把旋转来源映射到自身。");
                }

                if (route.Mode == RouteMode.ReplaceTarget &&
                    !string.IsNullOrWhiteSpace(route.TargetDevicePath) && !targets.Add(route.TargetDevicePath))
                {
                    errors.Add($"替换目标“{route.TargetDevicePath}”被重复使用。");
                }

                if (!configuration.AllowDuplicatePoseSources &&
                    !string.IsNullOrWhiteSpace(route.SourceDevicePath) &&
                    !sources.Add(route.SourceDevicePath))
                {
                    errors.Add($"位姿来源“{route.SourceDevicePath}”被重复使用。");
                }
                if (!configuration.AllowDuplicatePoseSources && route.SplitPoseSource &&
                    !string.IsNullOrWhiteSpace(route.RotationSourceDevicePath) &&
                    !sources.Add(route.RotationSourceDevicePath))
                {
                    errors.Add($"位姿来源“{route.RotationSourceDevicePath}”被重复使用。");
                }

                ValidateOffset(route.Offset, prefix, errors);
            }

            ValidateCycles(configuration.Routes, errors);
            ValidateOsc(configuration.Osc, errors);
            ValidateXInput(configuration.XInput, errors);

            return errors;
        }

        private static void ValidateOsc(OscConfiguration? configuration, ICollection<string> errors)
        {
            if (configuration == null)
            {
                errors.Add("OSC 配置不能为空。");
                return;
            }
            if (string.IsNullOrWhiteSpace(configuration.ListenAddress) || configuration.ListenAddress.Length > 255 ||
                configuration.ListenAddress.Any(char.IsControl))
            {
                errors.Add("OSC 地址无效。");
            }
            if (configuration.Port < 1 || configuration.Port > 65535)
            {
                errors.Add("OSC 接收端口必须在 1 到 65535 之间。");
            }
            if (configuration.SendPort < 1 || configuration.SendPort > 65535)
            {
                errors.Add("OSC 发送端口必须在 1 到 65535 之间。");
            }
            if (configuration.LeftTouchAssist == null)
            {
                errors.Add("OSC 左手触摸辅助配置不能为空。");
            }
            if (configuration.RightTouchAssist == null)
            {
                errors.Add("OSC 右手触摸辅助配置不能为空。");
            }
        }

        private static void ValidateXInput(XInputConfiguration? configuration, ICollection<string> errors)
        {
            if (configuration == null)
            {
                errors.Add("XInput 配置不能为空。");
                return;
            }
            if (float.IsNaN(configuration.AnalogPressThreshold) ||
                float.IsInfinity(configuration.AnalogPressThreshold) ||
                configuration.AnalogPressThreshold < XInputConfiguration.MinimumAnalogPressThreshold ||
                configuration.AnalogPressThreshold > XInputConfiguration.MaximumAnalogPressThreshold)
            {
                errors.Add("XInput 模拟输入触发阈值必须在 5% 到 95% 之间。");
            }
            if (!Enum.IsDefined(typeof(XInputHapticMode), configuration.HapticMode))
            {
                errors.Add("XInput 震动映射无效。");
            }
            ValidateXInputMapping(configuration.Left, "左手", errors);
            ValidateXInputMapping(configuration.Right, "右手", errors);
        }

        private static void ValidateXInputMapping(
            XInputHandMapping? mapping,
            string handName,
            ICollection<string> errors)
        {
            if (mapping == null)
            {
                errors.Add($"XInput {handName}映射不能为空。");
                return;
            }
            if (!XInputConfiguration.IsJoystickSource(mapping.Joystick))
            {
                errors.Add($"XInput {handName}摇杆必须映射到左摇杆、右摇杆或无。");
            }
            XInputBindingSource[] buttonSources =
            {
                mapping.PrimaryButton,
                mapping.SecondaryButton,
                mapping.JoystickClick,
                mapping.Trigger,
                mapping.Grip,
                mapping.MenuButton
            };
            if (buttonSources.Any(source => !XInputConfiguration.IsButtonOrScalarSource(source)))
            {
                errors.Add($"XInput {handName}按键映射包含不受支持的输入来源。");
            }
            ValidateXInputTouchAssist(mapping.ThumbTouch, handName + "大拇指", errors);
            ValidateXInputTouchAssist(mapping.IndexTouch, handName + "食指", errors);
        }

        private static void ValidateXInputTouchAssist(
            XInputTouchAssistMapping? mapping,
            string fingerName,
            ICollection<string> errors)
        {
            if (mapping == null)
            {
                errors.Add($"XInput {fingerName}触摸辅助不能为空。");
                return;
            }
            if (!XInputConfiguration.IsButtonOrScalarSource(mapping.ToggleSource))
            {
                errors.Add($"XInput {fingerName}触摸辅助包含不受支持的输入来源。");
            }
        }

        public static IReadOnlyList<string> ValidateSourceRoleDependencies(
            RuntimeConfiguration? configuration,
            IReadOnlyDictionary<string, string> sourceRoleTargets)
        {
            var errors = new List<string>();
            if (configuration?.Routes == null || sourceRoleTargets == null)
            {
                return errors;
            }

            List<RouteConfiguration> enabledRoutes = configuration.Routes
                .Where(route => route != null && route.Enabled && !route.PendingDeletion)
                .ToList();
            foreach (RouteConfiguration sourceRoute in enabledRoutes)
            {
                IEnumerable<string> sourcePaths = sourceRoute.SplitPoseSource
                    ? new[] { sourceRoute.SourceDevicePath, sourceRoute.RotationSourceDevicePath }
                    : new[] { sourceRoute.SourceDevicePath };
                foreach (string sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    if (!sourceRoleTargets.TryGetValue(sourcePath, out string? sourceRoleTarget) ||
                        string.IsNullOrWhiteSpace(sourceRoleTarget))
                    {
                        continue;
                    }
                    RouteConfiguration? overridingRoute = enabledRoutes.FirstOrDefault(route =>
                        !ReferenceEquals(route, sourceRoute) &&
                        route.Mode == RouteMode.ReplaceTarget &&
                        string.Equals(route.TargetDevicePath, sourceRoleTarget, StringComparison.Ordinal));
                    if (overridingRoute != null)
                    {
                        string sourceName = string.IsNullOrWhiteSpace(sourceRoute.Name)
                            ? sourceRoute.RouteId
                            : sourceRoute.Name;
                        string overridingName = string.IsNullOrWhiteSpace(overridingRoute.Name)
                            ? overridingRoute.RouteId
                            : overridingRoute.Name;
                        errors.Add(
                            $"配置“{sourceName}”使用了分配给“{sourceRoleTarget}”的设备，但配置“{overridingName}”正在替换该角色。" +
                            "这会形成跨配置的位姿级联。");
                    }
                }
            }

            return errors;
        }

        private static void ValidateCycles(IEnumerable<RouteConfiguration> routes, ICollection<string> errors)
        {
            var edges = routes
                .Where(route => route != null && route.Enabled &&
                    route.Mode == RouteMode.ReplaceTarget &&
                    !string.IsNullOrWhiteSpace(route.SourceDevicePath) &&
                    !string.IsNullOrWhiteSpace(route.TargetDevicePath))
                .SelectMany(route => route.SplitPoseSource
                    ? new[] { route.SourceDevicePath, route.RotationSourceDevicePath }
                    : new[] { route.SourceDevicePath },
                    (route, sourcePath) => new { SourcePath = sourcePath, route.TargetDevicePath })
                .Where(edge => !string.IsNullOrWhiteSpace(edge.SourcePath))
                .GroupBy(edge => edge.SourcePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().TargetDevicePath, StringComparer.Ordinal);

            foreach (string start in edges.Keys)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                string current = start;
                while (edges.TryGetValue(current, out string? next))
                {
                    if (!visited.Add(current))
                    {
                        errors.Add($"检测到以“{current}”为节点的路由循环。");
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
                errors.Add($"{prefix}的 sourceDevicePath 必须是 OpenVR 返回的精确 /devices/ 路径。");
            }
            else if (path.Any(char.IsControl))
            {
                errors.Add($"{prefix}的 sourceDevicePath 不能包含控制字符。");
            }
        }

        private static void ValidateTargetPath(string? path, string prefix, ICollection<string> errors)
        {
            bool supportedRole = string.Equals(path, ProtocolConstants.HeadRolePath, StringComparison.Ordinal) ||
                string.Equals(path, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal) ||
                string.Equals(path, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal);
            if (path == null || (!path.StartsWith("/devices/", StringComparison.Ordinal) && !supportedRole))
            {
                errors.Add($"{prefix}的 targetDevicePath 必须是精确的 /devices/ 路径或受支持的头显/手柄角色路径。");
            }
            else if (path.Any(char.IsControl))
            {
                errors.Add($"{prefix}的 targetDevicePath 不能包含控制字符。");
            }
        }

        private static void ValidateCombinedPathSize(
            string? sourcePath,
            string? rotationSourcePath,
            string? targetPath,
            string prefix,
            ICollection<string> errors)
        {
            if (sourcePath == null || rotationSourcePath == null || targetPath == null)
            {
                return;
            }

            int encodedBytes = Encoding.UTF8.GetByteCount(sourcePath) +
                Encoding.UTF8.GetByteCount(rotationSourcePath) +
                Encoding.UTF8.GetByteCount(targetPath);
            if (encodedBytes > DriverControlProtocol.MaximumCombinedDevicePathBytes)
            {
                errors.Add($"{prefix}编码后的来源和目标路径超过驱动通信长度限制。");
            }
        }

        private static void ValidateOffset(PoseOffset? offset, string prefix, ICollection<string> errors)
        {
            if (offset == null)
            {
                errors.Add($"{prefix}缺少位姿偏移。");
                return;
            }

            double[] values =
            {
                offset.TranslationX, offset.TranslationY, offset.TranslationZ,
                offset.RotationX, offset.RotationY, offset.RotationZ, offset.RotationW
            };
            if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
            {
                errors.Add($"{prefix}的位姿偏移只能包含有限数值。");
                return;
            }

            if (Math.Abs(offset.TranslationX) > MaximumTranslationCentimetres ||
                Math.Abs(offset.TranslationY) > MaximumTranslationCentimetres ||
                Math.Abs(offset.TranslationZ) > MaximumTranslationCentimetres)
            {
                errors.Add($"{prefix}的位置偏移超过每轴 {MaximumTranslationCentimetres} 厘米的安全范围。");
            }

            double lengthSquared =
                (offset.RotationX * offset.RotationX) +
                (offset.RotationY * offset.RotationY) +
                (offset.RotationZ * offset.RotationZ) +
                (offset.RotationW * offset.RotationW);
            if (lengthSquared < MinimumQuaternionLengthSquared)
            {
                errors.Add($"{prefix}的旋转四元数不能为零。");
            }
        }
    }
}
