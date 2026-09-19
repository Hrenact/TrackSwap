using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Threading;
using TrackSwap.Models;
using TrackSwap.Protocol;
using TrackSwap.Services;

namespace TrackSwap
{
    public partial class MainWindow : Window
    {
        private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedVerticalOffset",
                typeof(double),
                typeof(MainWindow),
                new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

        private readonly SteamVrPathService _pathService = new SteamVrPathService();
        private readonly SteamVrStatusService _statusService = new SteamVrStatusService();
        private readonly SteamVrSettingsService _settingsService = new SteamVrSettingsService();
        private readonly OpenVrDeviceService _openVrDeviceService = new OpenVrDeviceService();
        private readonly RuntimeControlService _runtimeControlService = new RuntimeControlService();
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _telemetryTimer;
        private Model3DGroup _sourcePreviewModel;
        private Model3DGroup _outputPreviewModel;
        private Model3DGroup _targetPreviewModel;

        private string _settingsPath;
        private bool _isLoading;
        private bool _isRuntimeStatusUpdatePending;
        private RuntimeStatusSnapshot _runtimeStatus;
        private long _loadedRuntimeRevision = -1;
        private bool _runtimeEditorInitialized;
        private bool _calibrationProfilesLoaded;
        private bool _calibrationBusy;
        private bool _telemetryUpdatePending;
        private IReadOnlyList<DeviceOption> _onlinePhysicalDevices = Array.Empty<DeviceOption>();
        private readonly ObservableCollection<RouteListItem> _routeItems = new ObservableCollection<RouteListItem>();
        private readonly List<RouteConfiguration> _workingRoutes = new List<RouteConfiguration>();
        private RouteConfiguration _selectedRoute;
        private bool _showingSettings;
        private long _displayedDriverAppliedRevision = long.MinValue;
        private bool _displayedDriverConnected;

        public MainWindow()
        {
            InitializeComponent();

            RouteListBox.ItemsSource = _routeItems;

            TargetComboBox.ItemsSource = BuildTargets(Array.Empty<DeviceOption>(), Array.Empty<TargetOption>());
            TargetComboBox.SelectedIndex = 0;
            RuntimeTargetComboBox.ItemsSource = BuildTargets(Array.Empty<DeviceOption>(), Array.Empty<TargetOption>());
            RuntimeTargetComboBox.SelectedIndex = 0;
            CalibrationNameTextBox.Text = "校准 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            InitializePosePreview();
            UpdateContentVisibility();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += async (_, __) => await RefreshStatusAsync();
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _telemetryTimer.Tick += async (_, __) => await RefreshTelemetryAsync();

            Loaded += async (_, __) =>
            {
                RefreshAll();
                _statusTimer.Start();
                _telemetryTimer.Start();
                await RefreshStatusAsync();
            };
            Closed += (_, __) =>
            {
                _statusTimer.Stop();
                _telemetryTimer.Stop();
            };
        }

        private void RefreshAll()
        {
            _isLoading = true;
            try
            {
                string previousTargetPath = (TargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                string previousRuntimeSourcePath = (RuntimeSourceComboBox.SelectedItem as DeviceOption)?.DevicePath;
                string previousRuntimeTargetPath = (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                _settingsPath = _pathService.FindSettingsPath();
                SettingsPathText.Text = _settingsPath ?? "未找到 steamvr.vrsettings";
                ViewRawButton.IsEnabled = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath);
                RestoreBackupButton.IsEnabled = ViewRawButton.IsEnabled;

                IReadOnlyList<DeviceOption> savedSources = ViewRawButton.IsEnabled
                    ? _settingsService.ReadKnownSources(_settingsPath)
                    : Array.Empty<DeviceOption>();
                bool steamVrRunning = _statusService.IsRunning();
                IReadOnlyList<DeviceOption> onlineSources = Array.Empty<DeviceOption>();
                string enumerationWarning = null;

                if (steamVrRunning)
                {
                    try
                    {
                        onlineSources = _openVrDeviceService.EnumerateOnlineDevices(_pathService.FindRuntimePath());
                    }
                    catch (Exception exception)
                    {
                        enumerationWarning = exception.Message;
                    }
                }

                IReadOnlyList<DeviceOption> sources = MergeSources(onlineSources, savedSources);
                SourceComboBox.ItemsSource = sources;

                IReadOnlyList<TargetOption> savedDeviceTargets = ViewRawButton.IsEnabled
                    ? _settingsService.ReadKnownDeviceTargets(_settingsPath)
                    : Array.Empty<TargetOption>();
                IReadOnlyList<TargetOption> targets = BuildTargets(onlineSources, savedDeviceTargets);
                TargetComboBox.ItemsSource = targets;
                TargetOption selectedTarget = targets.FirstOrDefault(target =>
                    string.Equals(target.TargetPath, previousTargetPath, StringComparison.Ordinal))
                    ?? targets.FirstOrDefault();
                TargetComboBox.SelectedItem = selectedTarget;

                string currentSource = selectedTarget == null || !ViewRawButton.IsEnabled
                    ? null
                    : _settingsService.ReadSourceForTarget(_settingsPath, selectedTarget.TargetPath);

                DeviceOption selectedSource = sources.FirstOrDefault(source =>
                    string.Equals(source.DevicePath, currentSource, StringComparison.Ordinal));
                SourceComboBox.SelectedItem = selectedSource ?? sources.FirstOrDefault();
                PopulateRuntimeOptions(
                    onlineSources,
                    targets,
                    previousRuntimeSourcePath,
                    previousRuntimeTargetPath);
                RefreshOverrideList();

                if (!string.IsNullOrWhiteSpace(enumerationWarning))
                {
                    RefreshButton.ToolTip = "在线设备读取失败，已回退到配置记录：" + enumerationWarning;
                }
                else
                {
                    RefreshButton.ToolTip = "重新扫描设备并读取配置";
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "重新载入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                UpdateSelectionDetails();
                UpdateSteamVrStatus();
            }
        }

        private void PopulateRuntimeOptions(
            IReadOnlyList<DeviceOption> onlineDevices,
            IReadOnlyList<TargetOption> targets,
            string selectedSourcePath,
            string selectedTargetPath)
        {
            IReadOnlyList<DeviceOption> physicalSources = onlineDevices
                .Where(device => device.DevicePath.IndexOf(
                    ProtocolConstants.VirtualSerialPrefix,
                    StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();
            _onlinePhysicalDevices = physicalSources;
            RuntimeSourceComboBox.ItemsSource = physicalSources;
            RuntimeSourceComboBox.SelectedItem = physicalSources.FirstOrDefault(device =>
                string.Equals(device.DevicePath, selectedSourcePath, StringComparison.Ordinal));

            RuntimeTargetComboBox.ItemsSource = targets;
            RuntimeTargetComboBox.SelectedItem = targets.FirstOrDefault(target =>
                string.Equals(target.TargetPath, selectedTargetPath, StringComparison.Ordinal))
                ?? targets.FirstOrDefault();
            RefreshCalibrationTargets();
        }

        private static IReadOnlyList<TargetOption> BuildTargets(
            IReadOnlyList<DeviceOption> onlineDevices,
            IReadOnlyList<TargetOption> savedDeviceTargets)
        {
            var targets = new List<TargetOption>
            {
                new TargetOption("常用角色 · 右手", "/user/hand/right"),
                new TargetOption("常用角色 · 左手", "/user/hand/left"),
                new TargetOption("常用角色 · 头显", "/user/head")
            };

            var knownPaths = new HashSet<string>(
                targets.Select(target => target.TargetPath),
                StringComparer.Ordinal);

            foreach (DeviceOption device in onlineDevices)
            {
                if (knownPaths.Add(device.DevicePath))
                {
                    targets.Add(new TargetOption("在线设备 · " + device.DisplayName, device.DevicePath));
                }
            }

            foreach (TargetOption target in savedDeviceTargets)
            {
                if (knownPaths.Add(target.TargetPath))
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private static IReadOnlyList<DeviceOption> MergeSources(
            IReadOnlyList<DeviceOption> onlineSources,
            IReadOnlyList<DeviceOption> savedSources)
        {
            var result = new List<DeviceOption>(onlineSources);
            var knownPaths = new HashSet<string>(
                onlineSources.Select(source => source.DevicePath),
                StringComparer.Ordinal);

            foreach (DeviceOption source in savedSources)
            {
                if (knownPaths.Add(source.DevicePath))
                {
                    result.Add(source);
                }
            }

            return result;
        }

        private void UpdateSteamVrStatus()
        {
            bool running = _statusService.IsRunning();
            SteamVrStatusText.Text = "SteamVR";
            SteamVrStatusText.Foreground = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrDot.Fill = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#123225" : "#202A34"));

            ApplyButton.IsEnabled = !running
                && SourceComboBox.SelectedItem is DeviceOption
                && TargetComboBox.SelectedItem is TargetOption
                && !string.IsNullOrWhiteSpace(_settingsPath);
        }

        private async Task RefreshStatusAsync()
        {
            UpdateSteamVrStatus();
            if (_isRuntimeStatusUpdatePending || _calibrationBusy)
            {
                return;
            }

            _isRuntimeStatusUpdatePending = true;
            try
            {
                RuntimeStatusSnapshot status = await _runtimeControlService.GetStatusAsync();
                _runtimeStatus = status;
                ShowRuntimeOnline(status);
                if (!_calibrationProfilesLoaded && !_calibrationBusy)
                {
                    await RefreshCalibrationProfilesAsync();
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                _runtimeStatus = null;
                ShowRuntimeOffline(exception.Message);
            }
            finally
            {
                _isRuntimeStatusUpdatePending = false;
                UpdateRuntimeSelectionDetails();
            }
        }

        private void ShowRuntimeOnline(RuntimeStatusSnapshot status)
        {
            RuntimeStatusText.Text = "Runtime";
            RuntimeStatusText.Foreground = FindBrush("SuccessBrush");
            RuntimeDot.Fill = FindBrush("SuccessBrush");
            RuntimeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#123225"));
            RuntimeHealthText.Text = "在线";
            RuntimeHealthText.Foreground = FindBrush("SuccessBrush");
            DriverHealthText.Text = status.DriverConnected ? "已连接" : "等待驱动";
            DriverHealthText.Foreground = FindBrush(status.DriverConnected ? "SuccessBrush" : "WarningBrush");
            RuntimeRevisionText.Text = status.ConfigurationRevision.ToString(CultureInfo.InvariantCulture);
            bool applied = status.DriverConnected &&
                status.ConfigurationRevision > 0 &&
                status.DriverAppliedRevision == status.ConfigurationRevision;
            RuntimeAppliedStateText.Text = applied
                ? "已应用"
                : "待应用 · driver " + status.DriverAppliedRevision.ToString(CultureInfo.InvariantCulture);
            RuntimeAppliedStateText.Foreground = FindBrush(applied ? "SuccessBrush" : "WarningBrush");
            RuntimeErrorText.Text = status.LastError ?? string.Empty;
            RuntimeErrorText.Visibility = string.IsNullOrWhiteSpace(status.LastError)
                ? Visibility.Collapsed
                : Visibility.Visible;
            StartRuntimeButton.IsEnabled = false;

            if (status.Configuration != null &&
                (!_runtimeEditorInitialized || status.Configuration.Revision != _loadedRuntimeRevision))
            {
                LoadRuntimeConfiguration(status.Configuration);
                _loadedRuntimeRevision = status.Configuration.Revision;
                _runtimeEditorInitialized = true;
            }
            else if (_displayedDriverAppliedRevision != status.DriverAppliedRevision ||
                _displayedDriverConnected != status.DriverConnected)
            {
                RefreshRouteList(_selectedRoute?.RouteId);
            }
            _displayedDriverAppliedRevision = status.DriverAppliedRevision;
            _displayedDriverConnected = status.DriverConnected;
        }

        private void ShowRuntimeOffline(string error)
        {
            RuntimeStatusText.Text = "Runtime";
            RuntimeStatusText.Foreground = FindBrush("MutedTextBrush");
            RuntimeDot.Fill = FindBrush("MutedTextBrush");
            RuntimeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202A34"));
            RuntimeHealthText.Text = "离线";
            RuntimeHealthText.Foreground = FindBrush("MutedTextBrush");
            DriverHealthText.Text = "未知";
            DriverHealthText.Foreground = FindBrush("MutedTextBrush");
            RuntimeRevisionText.Text = "—";
            RuntimeAppliedStateText.Text = "不可用";
            RuntimeAppliedStateText.Foreground = FindBrush("MutedTextBrush");
            RuntimeErrorText.Text = "无法连接 TrackSwap Runtime：" + error;
            RuntimeErrorText.Visibility = Visibility.Visible;
            StartRuntimeButton.IsEnabled = true;
        }

        private void RefreshOverrideList()
        {
            IReadOnlyList<TrackingOverrideOption> overrides =
                string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath)
                    ? Array.Empty<TrackingOverrideOption>()
                    : _settingsService.ReadOverrides(_settingsPath);

            OverridesItemsControl.ItemsSource = overrides;
            OverrideCountText.Text = overrides.Count + " 条规则";
            EmptyOverridesText.Visibility = overrides.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private Brush FindBrush(string key)
        {
            return (Brush)FindResource(key);
        }

        private void UpdateSelectionDetails()
        {
            DeviceOption source = SourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = TargetComboBox.SelectedItem as TargetOption;
            SourcePathText.Text = source?.DevicePath ?? "未选择来源";
            TargetPathText.Text = target?.TargetPath ?? "未选择目标";
            UpdateSteamVrStatus();
        }

        private void UpdateRuntimeSelectionDetails()
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = RuntimeTargetComboBox.SelectedItem as TargetOption;
            RuntimeSourcePathText.Text = source?.DevicePath ?? "未选择物理来源";
            RuntimeTargetPathText.Text = target?.TargetPath ?? "未选择静态目标";
            ApplyRuntimeButton.IsEnabled = _runtimeStatus != null && _selectedRoute != null && source != null && target != null;

            RouteConfiguration activeRoute = _runtimeStatus?.Configuration?.Routes?.FirstOrDefault(candidate =>
                _selectedRoute != null && string.Equals(candidate.RouteId, _selectedRoute.RouteId, StringComparison.Ordinal));
            bool sourceWillChange = activeRoute != null && source != null &&
                !string.Equals(activeRoute.SourceDevicePath, source.DevicePath, StringComparison.Ordinal);
            RuntimeRouteChangeWarningText.Text = sourceWillChange
                ? "注意：应用后物理来源将从 “" + activeRoute.SourceDevicePath + "” 切换为 “" + source.DevicePath + "”。"
                : string.Empty;
            RuntimeRouteChangeWarningText.Visibility = sourceWillChange
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_selectedRoute != null)
            {
                bool applied = _runtimeStatus != null && _runtimeStatus.DriverConnected &&
                    _runtimeStatus.ConfigurationRevision == _runtimeStatus.DriverAppliedRevision &&
                    activeRoute != null;
                bool mapped = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath) &&
                    _settingsService.ReadOverrides(_settingsPath).Any(mapping =>
                        string.Equals(mapping.SourcePath, ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot), StringComparison.Ordinal) &&
                        string.Equals(mapping.TargetPath, _selectedRoute.TargetDevicePath, StringComparison.Ordinal));
                bool steamVrRunning = _statusService.IsRunning();
                string state = !_selectedRoute.Enabled ? "已停用" : !mapped ? "待初始化" : !steamVrRunning ? "已配置" : applied ? "已应用" : "待应用";
                SelectedRouteStateText.Text = state;
                bool synchronized = mapped && (!steamVrRunning || applied);
                SelectedRouteStateText.Foreground = FindBrush(!_selectedRoute.Enabled ? "MutedTextBrush" : synchronized ? "SuccessBrush" : "WarningBrush");
                RouteSyncText.Text = synchronized ? "配置已同步" : !mapped ? "需要初始化静态映射" : "配置待同步";
                RouteSyncText.Foreground = FindBrush(synchronized ? "SuccessBrush" : "WarningBrush");
                SelectedRouteSummaryText.Text = source == null || target == null
                    ? "选择来源与目标以完成配置。"
                    : source.DisplayName + " 提供定位，" + target.DisplayName + " 保留输入。";
            }
            RefreshCalibrationTargets();
            UpdateCalibrationControls();
        }

        private void RefreshCalibrationTargets()
        {
            string selectedPath = (CalibrationTargetComboBox.SelectedItem as DeviceOption)?.DevicePath;
            string sourcePath = (RuntimeSourceComboBox.SelectedItem as DeviceOption)?.DevicePath;
            IReadOnlyList<DeviceOption> targets = _onlinePhysicalDevices
                .Where(device => !string.Equals(device.DevicePath, sourcePath, StringComparison.Ordinal))
                .ToList();
            bool wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                CalibrationTargetComboBox.ItemsSource = targets;
                CalibrationTargetComboBox.SelectedItem = targets.FirstOrDefault(device =>
                    string.Equals(device.DevicePath, selectedPath, StringComparison.Ordinal));
            }
            finally
            {
                _isLoading = wasLoading;
            }
        }

        private void UpdateCalibrationControls()
        {
            CalibrationProfile profile = CalibrationProfileComboBox.SelectedItem as CalibrationProfile;
            bool runtimeReady = _runtimeStatus != null && !_calibrationBusy;
            CaptureCalibrationButton.IsEnabled = runtimeReady &&
                RuntimeSourceComboBox.SelectedItem is DeviceOption &&
                CalibrationTargetComboBox.SelectedItem is DeviceOption &&
                !string.IsNullOrWhiteSpace(CalibrationNameTextBox.Text);
            ApplyCalibrationProfileButton.IsEnabled = runtimeReady && profile != null;
            DeleteCalibrationProfileButton.IsEnabled = runtimeReady && profile != null;
            CalibrationProfileDetailsText.Text = profile == null
                ? "尚未选择校准档案。"
                : profile.SourceDevicePath + " → " + profile.TargetDevicePath +
                  " · " + profile.SampleCount.ToString(CultureInfo.InvariantCulture) + " 样本" +
                  " · RMS " + profile.TranslationRmsMetres.ToString("F4", CultureInfo.InvariantCulture) + " m / " +
                  profile.RotationRmsDegrees.ToString("F2", CultureInfo.InvariantCulture) + "°";
        }

        private async Task RefreshCalibrationProfilesAsync()
        {
            CalibrationProfilesSnapshot snapshot = await _runtimeControlService.ListCalibrationProfilesAsync();
            string selectedId = (CalibrationProfileComboBox.SelectedItem as CalibrationProfile)?.ProfileId;
            CalibrationProfileComboBox.ItemsSource = snapshot.Profiles;
            CalibrationProfileComboBox.SelectedItem = snapshot.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, selectedId, StringComparison.Ordinal))
                ?? snapshot.Profiles.FirstOrDefault();
            _calibrationProfilesLoaded = true;
            UpdateCalibrationControls();
        }

        private void LoadRuntimeConfiguration(RuntimeConfiguration configuration)
        {
            string selectedRouteId = _selectedRoute?.RouteId;
            _isLoading = true;
            try
            {
                _workingRoutes.Clear();
                int unnamedIndex = 0;
                foreach (RouteConfiguration route in configuration.Routes ?? new List<RouteConfiguration>())
                {
                    RouteConfiguration copy = CloneRoute(route);
                    if (string.IsNullOrWhiteSpace(copy.Name))
                    {
                        copy.Name = unnamedIndex == 0 ? "新配置" : "新配置 (" + unnamedIndex + ")";
                    }
                    unnamedIndex++;
                    _workingRoutes.Add(copy);
                }
                RefreshRouteList(selectedRouteId);
            }
            finally
            {
                _isLoading = false;
                ShowSelectedRoute(_workingRoutes.FirstOrDefault(route =>
                    string.Equals(route.RouteId, selectedRouteId, StringComparison.Ordinal)) ??
                    _workingRoutes.FirstOrDefault());
            }
        }

        private static RouteConfiguration CloneRoute(RouteConfiguration route)
        {
            PoseOffset offset = route.Offset ?? PoseOffset.Identity();
            return new RouteConfiguration
            {
                RouteId = route.RouteId,
                Name = route.Name,
                Enabled = route.Enabled,
                VirtualDeviceSlot = route.VirtualDeviceSlot,
                SourceDevicePath = route.SourceDevicePath,
                TargetDevicePath = route.TargetDevicePath,
                Offset = new PoseOffset
                {
                    TranslationX = offset.TranslationX,
                    TranslationY = offset.TranslationY,
                    TranslationZ = offset.TranslationZ,
                    RotationX = offset.RotationX,
                    RotationY = offset.RotationY,
                    RotationZ = offset.RotationZ,
                    RotationW = offset.RotationW
                }
            };
        }

        private void RefreshRouteList(string selectedRouteId = null)
        {
            _routeItems.Clear();
            bool steamVrRunning = _statusService.IsRunning();
            bool driverApplied = _runtimeStatus != null && _runtimeStatus.DriverConnected &&
                _runtimeStatus.ConfigurationRevision > 0 &&
                _runtimeStatus.DriverAppliedRevision == _runtimeStatus.ConfigurationRevision;
            IReadOnlyList<TrackingOverrideOption> overrides =
                string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath)
                    ? Array.Empty<TrackingOverrideOption>()
                    : _settingsService.ReadOverrides(_settingsPath);
            foreach (RouteConfiguration route in _workingRoutes.OrderBy(candidate => candidate.VirtualDeviceSlot))
            {
                bool mapped = overrides.Any(mapping =>
                    string.Equals(mapping.SourcePath, ProtocolConstants.GetVirtualDevicePath(route.VirtualDeviceSlot), StringComparison.Ordinal) &&
                    string.Equals(mapping.TargetPath, route.TargetDevicePath, StringComparison.Ordinal));
                string state = !route.Enabled ? "已停用" : !mapped ? "待初始化" : !steamVrRunning ? "已配置" : driverApplied ? "已应用" : "等待驱动";
                Brush brush = FindBrush(!route.Enabled ? "MutedTextBrush" : mapped && (!steamVrRunning || driverApplied) ? "SuccessBrush" : "WarningBrush");
                _routeItems.Add(new RouteListItem(route, state, brush));
            }
            RouteListItem selection = _routeItems.FirstOrDefault(item =>
                string.Equals(item.Route.RouteId, selectedRouteId, StringComparison.Ordinal)) ??
                _routeItems.FirstOrDefault();
            RouteListBox.SelectedItem = selection;
            UpdateContentVisibility();
        }

        private void ShowSelectedRoute(RouteConfiguration route)
        {
            _selectedRoute = route;
            if (route == null)
            {
                UpdateContentVisibility();
                return;
            }

            _isLoading = true;
            try
            {
                var sources = ((RuntimeSourceComboBox.ItemsSource as IEnumerable<DeviceOption>) ?? Enumerable.Empty<DeviceOption>()).ToList();
                DeviceOption source = sources.FirstOrDefault(candidate => string.Equals(candidate.DevicePath, route.SourceDevicePath, StringComparison.Ordinal));
                if (source == null && !string.IsNullOrWhiteSpace(route.SourceDevicePath))
                {
                    source = new DeviceOption("已配置 · 当前离线", route.SourceDevicePath);
                    sources.Add(source);
                    RuntimeSourceComboBox.ItemsSource = sources;
                }
                RuntimeSourceComboBox.SelectedItem = source ?? sources.FirstOrDefault();

                var targets = ((RuntimeTargetComboBox.ItemsSource as IEnumerable<TargetOption>) ?? Enumerable.Empty<TargetOption>()).ToList();
                TargetOption target = targets.FirstOrDefault(candidate => string.Equals(candidate.TargetPath, route.TargetDevicePath, StringComparison.Ordinal));
                if (target == null && !string.IsNullOrWhiteSpace(route.TargetDevicePath))
                {
                    target = new TargetOption("已配置目标", route.TargetDevicePath);
                    targets.Add(target);
                    RuntimeTargetComboBox.ItemsSource = targets;
                }
                RuntimeTargetComboBox.SelectedItem = target ?? targets.FirstOrDefault();
                LoadOffsetFields(route.Offset ?? PoseOffset.Identity());
                SelectedProxyText.Text = ProtocolConstants.GetVirtualSerial(route.VirtualDeviceSlot);
                RuntimeProxyText.Text = ProtocolConstants.GetVirtualSerial(route.VirtualDeviceSlot);
                SelectedRouteNameText.Text = route.Name;
                ToggleSelectedRouteButton.Content = route.Enabled ? "停用" : "启用";
            }
            finally
            {
                _isLoading = false;
            }
            UpdateRuntimeSelectionDetails();
            UpdateContentVisibility();
        }

        private void UpdateContentVisibility()
        {
            bool hasRoute = _selectedRoute != null;
            EmptyStateGrid.Visibility = !_showingSettings && !hasRoute ? Visibility.Visible : Visibility.Collapsed;
            RouteContentScrollViewer.Visibility = !_showingSettings && hasRoute ? Visibility.Visible : Visibility.Collapsed;
            SettingsContentScrollViewer.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private void AddRouteButton_Click(object sender, RoutedEventArgs e)
        {
            RouteConfiguration incomplete = _workingRoutes.FirstOrDefault(route => !IsRouteComplete(route));
            if (incomplete != null)
            {
                ShowSelectedRoute(incomplete);
                RouteListBox.SelectedItem = _routeItems.FirstOrDefault(item => item.Route == incomplete);
                return;
            }
            if (_workingRoutes.Count >= ProtocolConstants.MaximumRoutes)
            {
                MessageBox.Show(this, "最多支持 " + ProtocolConstants.MaximumRoutes + " 条路由。", "无法新增", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int slot = Enumerable.Range(0, ProtocolConstants.MaximumRoutes)
                .First(candidate => _workingRoutes.All(route => route.VirtualDeviceSlot != candidate));
            var route = new RouteConfiguration
            {
                RouteId = Guid.NewGuid().ToString("N"),
                Name = GetNextRouteName(),
                Enabled = true,
                VirtualDeviceSlot = slot,
                Offset = PoseOffset.Identity()
            };
            _workingRoutes.Add(route);
            RefreshRouteList(route.RouteId);
            ShowSelectedRoute(route);
        }

        private string GetNextRouteName()
        {
            var names = new HashSet<string>(_workingRoutes.Select(route => route.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("新配置"))
            {
                return "新配置";
            }
            for (int index = 1; ; index++)
            {
                string candidate = "新配置 (" + index.ToString(CultureInfo.InvariantCulture) + ")";
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool IsRouteComplete(RouteConfiguration route)
        {
            return route != null && !string.IsNullOrWhiteSpace(route.SourceDevicePath) &&
                !string.IsNullOrWhiteSpace(route.TargetDevicePath);
        }

        private void RouteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || !(RouteListBox.SelectedItem is RouteListItem item))
            {
                return;
            }
            _showingSettings = false;
            ShowSelectedRoute(item.Route);
        }

        private void RouteListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && !(current is ListBoxItem))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            if (current is ListBoxItem item)
            {
                item.IsSelected = true;
            }
        }

        private void SettingsNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            _showingSettings = true;
            RouteListBox.SelectedItem = null;
            UpdateContentVisibility();
        }

        private void ManageRoutesButton_Click(object sender, RoutedEventArgs e)
        {
            if (RouteListBox.SelectedItem == null)
            {
                MessageBox.Show(this, "请先选择一条配置。", "管理配置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RouteListBox.ContextMenu.PlacementTarget = RouteListBox;
            RouteListBox.ContextMenu.IsOpen = true;
        }

        private void RenameRouteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(RouteListBox.SelectedItem is RouteListItem item))
            {
                return;
            }
            string name = PromptForRouteName(item.Route.Name);
            if (name == null)
            {
                return;
            }
            if (_workingRoutes.Any(route => route != item.Route && string.Equals(route.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "配置名称不能重复。", "无法重命名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            item.Route.Name = name;
            SelectedRouteNameText.Text = name;
            RefreshRouteList(item.Route.RouteId);
            if (IsRouteComplete(item.Route))
            {
                _ = PersistWorkingRoutesAsync();
            }
        }

        private string PromptForRouteName(string currentName)
        {
            var input = new TextBox { Text = currentName, MinWidth = 280, FontFamily = new FontFamily("Microsoft YaHei UI") };
            var ok = new Button { Content = "保存", IsDefault = true, MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 76 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "配置名称", Foreground = FindBrush("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 7) });
            panel.Children.Add(input);
            panel.Children.Add(buttons);
            var dialog = new Window
            {
                Title = "重命名配置",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Background = FindBrush("SurfaceBrush"),
                Content = panel
            };
            ok.Click += (_, __) =>
            {
                string value = input.Text.Trim();
                if (value.Length == 0 || value.Length > 64)
                {
                    MessageBox.Show(dialog, "名称必须包含 1–64 个字符。", "名称无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                dialog.DialogResult = true;
            };
            input.SelectAll();
            input.Focus();
            return dialog.ShowDialog() == true ? input.Text.Trim() : null;
        }

        private async void ToggleRouteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ToggleSelectedRouteAsync();
        }

        private async void ToggleSelectedRouteButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleSelectedRouteAsync();
        }

        private async Task ToggleSelectedRouteAsync()
        {
            if (_selectedRoute == null)
            {
                return;
            }
            bool enabling = !_selectedRoute.Enabled;
            if (!enabling && _statusService.IsRunning() && MessageBox.Show(
                    this,
                    "停用后虚拟代理会立即停止输出，但 SteamVR 静态映射仍然生效，因此对应目标会暂时失去定位。是否继续？",
                    "确认停用",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            _selectedRoute.Enabled = enabling;
            ToggleSelectedRouteButton.Content = _selectedRoute.Enabled ? "停用" : "启用";
            if (!_statusService.IsRunning() && IsRouteComplete(_selectedRoute) && !string.IsNullOrWhiteSpace(_settingsPath))
            {
                string proxyPath = ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot);
                IReadOnlyList<TrackingOverrideOption> overrides = _settingsService.ReadOverrides(_settingsPath);
                bool mapped = overrides.Any(mapping => string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal));
                if (enabling)
                {
                    _settingsService.ApplyOverride(_settingsPath, proxyPath, _selectedRoute.TargetDevicePath);
                }
                else if (mapped)
                {
                    _settingsService.RemoveOverride(_settingsPath, proxyPath);
                }
                RefreshOverrideList();
            }
            if (IsRouteComplete(_selectedRoute))
            {
                await PersistWorkingRoutesAsync();
            }
            else
            {
                RefreshRouteList(_selectedRoute.RouteId);
            }
        }

        private async void DeleteRouteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRoute == null || MessageBox.Show(
                    this,
                    "确定删除配置 “" + _selectedRoute.Name + "”？已保存的校准档案不会删除。",
                    "删除配置",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            RouteConfiguration removed = _selectedRoute;
            _workingRoutes.Remove(removed);
            _selectedRoute = null;
            if (!_statusService.IsRunning() && IsRouteComplete(removed) && !string.IsNullOrWhiteSpace(_settingsPath))
            {
                string proxyPath = ProtocolConstants.GetVirtualDevicePath(removed.VirtualDeviceSlot);
                IReadOnlyList<TrackingOverrideOption> overrides = _settingsService.ReadOverrides(_settingsPath);
                if (overrides.Any(mapping => string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal)))
                {
                    _settingsService.RemoveOverride(_settingsPath, proxyPath);
                    RefreshOverrideList();
                }
            }
            if (IsRouteComplete(removed) && _runtimeStatus != null)
            {
                await PersistWorkingRoutesAsync();
            }
            else
            {
                RefreshRouteList();
                ShowSelectedRoute(_workingRoutes.FirstOrDefault());
            }
        }

        private async Task PersistWorkingRoutesAsync()
        {
            if (_runtimeStatus == null)
            {
                RefreshRouteList(_selectedRoute?.RouteId);
                return;
            }
            var configuration = new RuntimeConfiguration
            {
                Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList()
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "配置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _loadedRuntimeRevision = -1;
                _runtimeEditorInitialized = false;
                await RefreshStatusAsync();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "保存配置失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RuntimeSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading)
            {
                UpdateRuntimeSelectionDetails();
            }
        }

        private void CalibrationSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading)
            {
                UpdateCalibrationControls();
            }
        }

        private async void CaptureCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            DeviceOption target = CalibrationTargetComboBox.SelectedItem as DeviceOption;
            if (source == null || target == null || _runtimeStatus == null)
            {
                return;
            }

            IReadOnlyList<TrackingOverrideOption> overrides =
                string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath)
                    ? Array.Empty<TrackingOverrideOption>()
                    : _settingsService.ReadOverrides(_settingsPath);
            string selectedProxyPath = _selectedRoute == null
                ? string.Empty
                : ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot);
            bool virtualOverrideActive = overrides.Any(mapping =>
                string.Equals(mapping.SourcePath, selectedProxyPath, StringComparison.Ordinal));
            if (virtualOverrideActive)
            {
                MessageBox.Show(
                    this,
                    "校准前必须让目标的原始位姿保持可见。请：\n\n" +
                    "1. 完全退出 SteamVR；\n" +
                    "2. 在“设置 → 旧版静态覆盖 v001”中移除该虚拟代理规则；\n" +
                    "3. 重新启动 SteamVR 后再采集。\n\n" +
                    "校准完成并验证重合后，再恢复静态引导映射。",
                    "需要停用静态覆盖",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "请将来源与目标固定在期望的相对位置，并在约 2 秒采样期间保持两者刚性同步移动。\n\n" +
                source.DevicePath + "\n→ " + target.DevicePath + "\n\n" +
                "此操作只会计算并应用偏移；虚拟代理仍将跟随当前物理来源，不会切换为参考目标。",
                "开始自动校准",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }

            _calibrationBusy = true;
            CalibrationStatusText.Text = "正在采集 90 组同步位姿，请保持两台设备的相对关系不变…";
            UpdateCalibrationControls();
            try
            {
                CalibrationCaptureResponse response = await _runtimeControlService.CaptureCalibrationAsync(
                    new CalibrationCaptureRequest
                    {
                        ProfileName = CalibrationNameTextBox.Text.Trim(),
                        SourceDevicePath = source.DevicePath,
                        TargetDevicePath = target.DevicePath,
                        OriginalTargetPoseVisible = true,
                        RequestedSampleCount = 90
                    });
                LoadOffsetFields(response.Profile.Offset);
                _calibrationProfilesLoaded = false;
                await RefreshCalibrationProfilesAsync();
                CalibrationProfileComboBox.SelectedItem = ((IEnumerable<CalibrationProfile>)CalibrationProfileComboBox.ItemsSource)
                    .FirstOrDefault(profile => string.Equals(
                        profile.ProfileId, response.Profile.ProfileId, StringComparison.Ordinal));
                CalibrationStatusText.Text =
                    "采集通过：RMS " + response.Profile.TranslationRmsMetres.ToString("F4", CultureInfo.InvariantCulture) +
                    " m / " + response.Profile.RotationRmsDegrees.ToString("F2", CultureInfo.InvariantCulture) +
                    "°。正在应用校准偏移…";
                await ApplyRuntimeConfigurationAsync();
                CalibrationStatusText.Text =
                    "校准档案已保存，偏移已应用；物理位姿来源仍为 “" + source.DisplayName + "”。";
                CalibrationNameTextBox.Text = "校准 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            }
            catch (Exception exception)
            {
                CalibrationStatusText.Text = "校准失败：" + exception.Message;
                MessageBox.Show(this, exception.Message, "自动校准失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _calibrationBusy = false;
                UpdateCalibrationControls();
            }
        }

        private async void ApplyCalibrationProfileButton_Click(object sender, RoutedEventArgs e)
        {
            CalibrationProfile profile = CalibrationProfileComboBox.SelectedItem as CalibrationProfile;
            if (profile == null)
            {
                return;
            }
            SelectRuntimeSource(profile.SourceDevicePath);
            LoadOffsetFields(profile.Offset);
            CalibrationStatusText.Text = "正在应用档案 “" + profile.Name + "”…";
            await ApplyRuntimeConfigurationAsync();
            CalibrationStatusText.Text =
                "档案 “" + profile.Name + "” 的来源与偏移已应用；当前物理位姿来源为 “" +
                ((RuntimeSourceComboBox.SelectedItem as DeviceOption)?.DisplayName ?? profile.SourceDevicePath) + "”。";
        }

        private async void DeleteCalibrationProfileButton_Click(object sender, RoutedEventArgs e)
        {
            CalibrationProfile profile = CalibrationProfileComboBox.SelectedItem as CalibrationProfile;
            if (profile == null)
            {
                return;
            }
            if (MessageBox.Show(
                    this,
                    "确定删除校准档案 “" + profile.Name + "”？当前已应用偏移不会改变。",
                    "删除校准档案",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            try
            {
                await _runtimeControlService.DeleteCalibrationProfileAsync(profile.ProfileId);
                await RefreshCalibrationProfilesAsync();
                CalibrationStatusText.Text = "校准档案已删除；当前运行时偏移保持不变。";
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectRuntimeSource(string sourceDevicePath)
        {
            var sources = ((RuntimeSourceComboBox.ItemsSource as IEnumerable<DeviceOption>) ??
                Enumerable.Empty<DeviceOption>()).ToList();
            DeviceOption source = sources.FirstOrDefault(candidate =>
                string.Equals(candidate.DevicePath, sourceDevicePath, StringComparison.Ordinal));
            if (source == null)
            {
                source = new DeviceOption("档案来源 · 当前离线", sourceDevicePath);
                sources.Add(source);
                RuntimeSourceComboBox.ItemsSource = sources;
            }
            RuntimeSourceComboBox.SelectedItem = source;
        }

        private void LoadOffsetFields(PoseOffset offset)
        {
            OffsetTranslationXTextBox.Text = FormatNumber(offset.TranslationX);
            OffsetTranslationYTextBox.Text = FormatNumber(offset.TranslationY);
            OffsetTranslationZTextBox.Text = FormatNumber(offset.TranslationZ);
            OffsetRotationXTextBox.Text = FormatNumber(offset.RotationX);
            OffsetRotationYTextBox.Text = FormatNumber(offset.RotationY);
            OffsetRotationZTextBox.Text = FormatNumber(offset.RotationZ);
            OffsetRotationWTextBox.Text = FormatNumber(offset.RotationW);
        }

        private async void RuntimeRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
            await RefreshStatusAsync();
        }

        private async void StartRuntimeButton_Click(object sender, RoutedEventArgs e)
        {
            StartRuntimeButton.IsEnabled = false;
            if (!_runtimeControlService.TryStartRuntime(out string error))
            {
                MessageBox.Show(this, error, "Runtime 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                StartRuntimeButton.IsEnabled = true;
                return;
            }

            await Task.Delay(500);
            await RefreshStatusAsync();
        }

        private async void ResetRuntimeOffsetButton_Click(object sender, RoutedEventArgs e)
        {
            SetIdentityOffsetFields();
            await ApplyRuntimeConfigurationAsync();
        }

        private async void ApplyRuntimeButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplyRuntimeConfigurationAsync();
        }

        private void SetIdentityOffsetFields()
        {
            OffsetTranslationXTextBox.Text = "0";
            OffsetTranslationYTextBox.Text = "0";
            OffsetTranslationZTextBox.Text = "0";
            OffsetRotationXTextBox.Text = "0";
            OffsetRotationYTextBox.Text = "0";
            OffsetRotationZTextBox.Text = "0";
            OffsetRotationWTextBox.Text = "1";
        }

        private async Task ApplyRuntimeConfigurationAsync()
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = RuntimeTargetComboBox.SelectedItem as TargetOption;
            if (_runtimeStatus == null || _selectedRoute == null || source == null || target == null)
            {
                MessageBox.Show(this, "Runtime 未连接，或尚未选择完整路由。", "无法应用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(source.RoleTargetPath) &&
                string.Equals(source.RoleTargetPath, target.TargetPath, StringComparison.Ordinal))
            {
                MessageBox.Show(
                    this,
                    "不能用目标角色当前绑定的设备替换同一个角色。这样会形成位姿反馈环，使目标停在上一帧。\n\n" +
                    "请选择 Tracker、其他控制器或其他不会被该路由覆盖的位姿来源。",
                    "检测到自引用路由",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!TryReadOffset(out PoseOffset offset, out string parseError))
            {
                MessageBox.Show(this, parseError, "偏移格式无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            long revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1);
            RouteConfiguration activeRoute = _runtimeStatus.Configuration?.Routes?.FirstOrDefault(candidate =>
                string.Equals(candidate.RouteId, _selectedRoute.RouteId, StringComparison.Ordinal));
            if (activeRoute != null &&
                !string.Equals(activeRoute.SourceDevicePath, source.DevicePath, StringComparison.Ordinal))
            {
                MessageBoxResult switchResult = MessageBox.Show(
                    this,
                    "此操作不仅会更新偏移，还会切换物理位姿来源：\n\n" +
                    activeRoute.SourceDevicePath + "\n→ " + source.DevicePath,
                    "确认切换物理来源",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (switchResult != MessageBoxResult.OK)
                {
                    return;
                }
            }

            _selectedRoute.SourceDevicePath = source.DevicePath;
            _selectedRoute.TargetDevicePath = target.TargetPath;
            _selectedRoute.Offset = offset;
            var configuration = new RuntimeConfiguration
            {
                Revision = revision,
                Routes = _workingRoutes.Select(CloneRoute).ToList()
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "运行时配置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyRuntimeButton.IsEnabled = false;
            RuntimeAppliedStateText.Text = "正在提交…";
            RuntimeAppliedStateText.Foreground = FindBrush("WarningBrush");
            try
            {
                if (!_statusService.IsRunning() && !string.IsNullOrWhiteSpace(_settingsPath))
                {
                    _settingsService.ApplyOverride(
                        _settingsPath,
                        ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot),
                        target.TargetPath);
                    RefreshOverrideList();
                }
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _loadedRuntimeRevision = -1;
                _runtimeEditorInitialized = false;
                await RefreshStatusAsync();
                if (_statusService.IsRunning())
                {
                    IReadOnlyList<TrackingOverrideOption> overrides = string.IsNullOrWhiteSpace(_settingsPath)
                        ? Array.Empty<TrackingOverrideOption>()
                        : _settingsService.ReadOverrides(_settingsPath);
                    bool mapped = overrides.Any(mapping =>
                        string.Equals(mapping.SourcePath, ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot), StringComparison.Ordinal) &&
                        string.Equals(mapping.TargetPath, target.TargetPath, StringComparison.Ordinal));
                    if (!mapped)
                    {
                        RuntimeRouteChangeWarningText.Text = "运行时配置已保存。要让该代理替换目标，请退出 SteamVR 后再次点击“应用更改”以写入静态引导映射。";
                        RuntimeRouteChangeWarningText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "运行时路由应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshStatusAsync();
            }
            finally
            {
                UpdateRuntimeSelectionDetails();
            }
        }

        private bool TryReadOffset(out PoseOffset offset, out string error)
        {
            offset = null;
            error = null;
            TextBox[] fields =
            {
                OffsetTranslationXTextBox, OffsetTranslationYTextBox, OffsetTranslationZTextBox,
                OffsetRotationXTextBox, OffsetRotationYTextBox, OffsetRotationZTextBox, OffsetRotationWTextBox
            };
            var values = new double[fields.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!TryParseNumber(fields[index].Text, out values[index]))
                {
                    error = "所有偏移字段都必须是有限数字。";
                    return false;
                }
            }

            double quaternionLength = Math.Sqrt(
                (values[3] * values[3]) + (values[4] * values[4]) +
                (values[5] * values[5]) + (values[6] * values[6]));
            if (quaternionLength < 1e-6)
            {
                error = "旋转四元数不能为零。";
                return false;
            }

            offset = new PoseOffset
            {
                TranslationX = values[0],
                TranslationY = values[1],
                TranslationZ = values[2],
                RotationX = values[3] / quaternionLength,
                RotationY = values[4] / quaternionLength,
                RotationZ = values[5] / quaternionLength,
                RotationW = values[6] / quaternionLength
            };
            return true;
        }

        private static bool TryParseNumber(string text, out double value)
        {
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void Selection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            UpdateSelectionDetails();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_statusService.IsRunning())
            {
                MessageBox.Show(this, "请先完全退出 SteamVR，避免退出时覆盖配置。", "SteamVR 正在运行", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateSteamVrStatus();
                return;
            }

            DeviceOption source = SourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = TargetComboBox.SelectedItem as TargetOption;
            if (source == null || target == null)
            {
                return;
            }

            string validationError = _settingsService.ValidateOverride(_settingsPath, source.DevicePath, target.TargetPath);
            if (validationError != null)
            {
                MessageBox.Show(this, validationError, "无法应用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                this,
                "将使用以下位姿映射：\n\n" + source.DevicePath + "\n→ " + target.TargetPath + "\n\n应用前会自动备份原始配置。",
                "确认应用",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                _settingsService.ApplyOverride(_settingsPath, source.DevicePath, target.TargetPath);
                RefreshOverrideList();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            TrackingOverrideOption mapping = (sender as Button)?.Tag as TrackingOverrideOption;
            if (mapping == null)
            {
                return;
            }

            DeviceOption source = (SourceComboBox.ItemsSource as IEnumerable<DeviceOption>)?
                .FirstOrDefault(item => string.Equals(item.DevicePath, mapping.SourcePath, StringComparison.Ordinal));
            TargetOption target = (TargetComboBox.ItemsSource as IEnumerable<TargetOption>)?
                .FirstOrDefault(item => string.Equals(item.TargetPath, mapping.TargetPath, StringComparison.Ordinal));

            if (source == null || target == null)
            {
                MessageBox.Show(this, "无法在当前设备列表中找到这条规则，请在 SteamVR 运行时重新载入。", "设备不可用", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SourceComboBox.SelectedItem = source;
            TargetComboBox.SelectedItem = target;
            UpdateSelectionDetails();
            ScrollToEditorAndHighlight();
        }

        private static void OnAnimatedVerticalOffsetChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            if (dependencyObject is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)eventArgs.NewValue);
            }
        }

        private void ScrollToEditorAndHighlight()
        {
            double startOffset = MainScrollViewer.VerticalOffset;
            if (startOffset <= 1)
            {
                HighlightEditorCard();
                return;
            }

            double durationMilliseconds = Math.Max(260, Math.Min(540, startOffset * 0.55));
            var animation = new DoubleAnimation
            {
                From = startOffset,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, __) =>
            {
                MainScrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
                MainScrollViewer.ScrollToTop();
                HighlightEditorCard();
            };

            MainScrollViewer.BeginAnimation(
                AnimatedVerticalOffsetProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void HighlightEditorCard()
        {
            var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#31598C"));
            EditorCardBorder.Background = background;

            var animation = new ColorAnimation
            {
                To = (Color)ColorConverter.ConvertFromString("#17212B"),
                Duration = TimeSpan.FromMilliseconds(750),
                BeginTime = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, __) => EditorCardBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#17212B"));
            background.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void RemoveOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            if (_statusService.IsRunning())
            {
                MessageBox.Show(this, "请先完全退出 SteamVR，再移除规则。", "SteamVR 正在运行", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TrackingOverrideOption mapping = (sender as Button)?.Tag as TrackingOverrideOption;
            if (mapping == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                this,
                "确定移除以下位姿映射？\n\n" + mapping.SourcePath + "\n→ " + mapping.TargetPath + "\n\n移除前会自动备份原始配置。",
                "确认移除",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                _settingsService.RemoveOverride(_settingsPath, mapping.SourcePath);
                RefreshOverrideList();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "移除失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializePosePreview()
        {
            PreviewSceneRoot.Children.Add(CreateAxesModel(0.45, 0.006));
            _sourcePreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#F5A623"));
            _outputPreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#4D8DFF"));
            _targetPreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#45D483"));
            PreviewSceneRoot.Children.Add(_sourcePreviewModel);
            PreviewSceneRoot.Children.Add(_outputPreviewModel);
            PreviewSceneRoot.Children.Add(_targetPreviewModel);
            HidePreviewModel(_sourcePreviewModel);
            HidePreviewModel(_outputPreviewModel);
            HidePreviewModel(_targetPreviewModel);
        }

        private async Task RefreshTelemetryAsync()
        {
            if (_telemetryUpdatePending || !IsVisible || _showingSettings || _selectedRoute == null)
            {
                return;
            }

            _telemetryUpdatePending = true;
            try
            {
                PoseTelemetrySnapshot snapshot = await _runtimeControlService.GetTelemetryAsync(
                    _selectedRoute.VirtualDeviceSlot);
                RenderTelemetry(snapshot);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                PreviewStatusText.Text = "遥测不可用";
                PreviewPoseDetailsText.Text = exception.Message;
                HidePreviewModel(_sourcePreviewModel);
                HidePreviewModel(_outputPreviewModel);
                HidePreviewModel(_targetPreviewModel);
            }
            finally
            {
                _telemetryUpdatePending = false;
            }
        }

        private void RenderTelemetry(PoseTelemetrySnapshot snapshot)
        {
            Point3D center = GetPreviewOrigin(snapshot);

            ApplyPreviewPose(_sourcePreviewModel, snapshot.Source, center);
            ApplyPreviewPose(_outputPreviewModel, snapshot.Output, center);
            ApplyPreviewPose(_targetPreviewModel, snapshot.Target, center);

            PreviewStatusText.Text = "遥测 #" + snapshot.Sequence.ToString(CultureInfo.InvariantCulture);
            string healthText =
                "来源 " + PoseHealth(snapshot.Source) + " · 输出 " + PoseHealth(snapshot.Output) +
                " · 目标 " + PoseHealth(snapshot.Target);
            if (IsRenderablePose(snapshot.Output) && IsRenderablePose(snapshot.Target))
            {
                double dx = snapshot.Output.PositionX - snapshot.Target.PositionX;
                double dy = snapshot.Output.PositionY - snapshot.Target.PositionY;
                double dz = snapshot.Output.PositionZ - snapshot.Target.PositionZ;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                string overlapText = AreCoincident(snapshot.Source, snapshot.Output) &&
                    AreCoincident(snapshot.Output, snapshot.Target)
                    ? " · 三者重合"
                    : string.Empty;
                PreviewPoseDetailsText.Text = healthText + overlapText +
                    " · 输出→目标 " + distance.ToString("F4", CultureInfo.InvariantCulture) + " m";
            }
            else
            {
                PreviewPoseDetailsText.Text = healthText;
            }
        }

        private static Point3D GetPreviewOrigin(PoseTelemetrySnapshot snapshot)
        {
            PoseTelemetry anchor = IsRenderablePose(snapshot.Target)
                ? snapshot.Target
                : IsRenderablePose(snapshot.Source)
                    ? snapshot.Source
                    : snapshot.Output;
            return IsRenderablePose(anchor)
                ? new Point3D(anchor.PositionX, anchor.PositionY, anchor.PositionZ)
                : new Point3D();
        }

        private static bool AreCoincident(PoseTelemetry left, PoseTelemetry right)
        {
            if (!IsRenderablePose(left) || !IsRenderablePose(right))
            {
                return false;
            }

            double dx = left.PositionX - right.PositionX;
            double dy = left.PositionY - right.PositionY;
            double dz = left.PositionZ - right.PositionZ;
            if ((dx * dx) + (dy * dy) + (dz * dz) > 0.001 * 0.001)
            {
                return false;
            }

            double leftLength = Math.Sqrt(
                (left.RotationX * left.RotationX) + (left.RotationY * left.RotationY) +
                (left.RotationZ * left.RotationZ) + (left.RotationW * left.RotationW));
            double rightLength = Math.Sqrt(
                (right.RotationX * right.RotationX) + (right.RotationY * right.RotationY) +
                (right.RotationZ * right.RotationZ) + (right.RotationW * right.RotationW));
            if (leftLength < 1e-12 || rightLength < 1e-12)
            {
                return false;
            }

            double orientationDot = Math.Abs(
                (left.RotationX * right.RotationX) + (left.RotationY * right.RotationY) +
                (left.RotationZ * right.RotationZ) + (left.RotationW * right.RotationW)) /
                (leftLength * rightLength);
            return orientationDot >= Math.Cos(0.5 * Math.PI / 360.0);
        }

        private static string PoseHealth(PoseTelemetry pose)
        {
            return pose.Valid ? "有效" : pose.Connected ? "无有效位姿" : "未连接";
        }

        private static bool IsRenderablePose(PoseTelemetry pose)
        {
            return pose != null && pose.Connected && pose.Valid &&
                IsFinite(pose.PositionX) && IsFinite(pose.PositionY) && IsFinite(pose.PositionZ) &&
                IsFinite(pose.RotationX) && IsFinite(pose.RotationY) &&
                IsFinite(pose.RotationZ) && IsFinite(pose.RotationW);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ApplyPreviewPose(Model3DGroup model, PoseTelemetry pose, Point3D center)
        {
            if (!IsRenderablePose(pose))
            {
                HidePreviewModel(model);
                return;
            }

            var rotation = new Quaternion(
                pose.RotationX,
                pose.RotationY,
                pose.RotationZ,
                pose.RotationW);
            double rotationLengthSquared =
                (rotation.X * rotation.X) + (rotation.Y * rotation.Y) +
                (rotation.Z * rotation.Z) + (rotation.W * rotation.W);
            if (rotationLengthSquared < 1e-12)
            {
                HidePreviewModel(model);
                return;
            }
            rotation.Normalize();
            Matrix3D matrix = Matrix3D.Identity;
            matrix.Rotate(rotation);
            matrix.Translate(new Vector3D(
                pose.PositionX - center.X,
                pose.PositionY - center.Y,
                pose.PositionZ - center.Z));
            model.Transform = new MatrixTransform3D(matrix);
        }

        private static void HidePreviewModel(Model3DGroup model)
        {
            model.Transform = new TranslateTransform3D(10000, 10000, 10000);
        }

        private static Model3DGroup CreateDeviceModel(Color bodyColor)
        {
            var group = new Model3DGroup();
            group.Children.Add(CreateBoxModel(new Point3D(0, 0, 0), new Vector3D(0.065, 0.065, 0.065), bodyColor));
            foreach (Model3D axis in CreateAxesModel(0.18, 0.012).Children)
            {
                group.Children.Add(axis);
            }
            return group;
        }

        private static Model3DGroup CreateAxesModel(double length, double thickness)
        {
            var axes = new Model3DGroup();
            axes.Children.Add(CreateBoxModel(
                new Point3D(length / 2, 0, 0), new Vector3D(length, thickness, thickness), Colors.IndianRed));
            axes.Children.Add(CreateBoxModel(
                new Point3D(0, length / 2, 0), new Vector3D(thickness, length, thickness), Colors.LightGreen));
            axes.Children.Add(CreateBoxModel(
                new Point3D(0, 0, length / 2), new Vector3D(thickness, thickness, length), Colors.DodgerBlue));
            return axes;
        }

        private static GeometryModel3D CreateBoxModel(Point3D center, Vector3D size, Color color)
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
                TriangleIndices = new Int32Collection
                {
                    0,2,1, 0,3,2, 4,5,6, 4,6,7,
                    0,1,5, 0,5,4, 2,3,7, 2,7,6,
                    1,2,6, 1,6,5, 3,0,4, 3,4,7
                }
            };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private void LocateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + _settingsPath + "\"",
                UseShellExecute = true
            });
        }

        private void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
            {
                return;
            }

            var window = new BackupRestoreWindow(_settingsPath, _settingsService, _statusService)
            {
                Owner = this
            };

            if (window.ShowDialog() == true)
            {
                RefreshAll();
            }
        }
    }
}
