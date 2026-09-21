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
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using TrackSwap.Models;
using TrackSwap.Protocol;
using TrackSwap.Services;
using MessageBox = TrackSwap.AppDialog;

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
        private readonly OpenVrRenderModelService _openVrRenderModelService = new OpenVrRenderModelService();
        private readonly SteamVrApplicationService _steamVrApplicationService = new SteamVrApplicationService();
        private readonly DeviceHistoryService _deviceHistoryService = new DeviceHistoryService();
        private readonly UiPreferencesService _uiPreferencesService = new UiPreferencesService();
        private readonly RuntimeControlService _runtimeControlService = new RuntimeControlService();
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _deviceRefreshTimer;
        private readonly DispatcherTimer _telemetryTimer;
        private Model3DGroup _sourcePreviewModel;
        private Model3DGroup _targetPreviewModel;
        private Point3D _previewCameraTarget = new Point3D(0, 0, 0);
        private double _previewCameraYaw = 0.694;
        private double _previewCameraPitch = 0.397;
        private double _previewCameraDistance = 0.68;
        private MouseButton? _previewDragButton;
        private Point _previewLastPointer;
        private readonly Dictionary<string, Task<OpenVrRenderModel>> _previewModelCache =
            new Dictionary<string, Task<OpenVrRenderModel>>(StringComparer.Ordinal);
        private int _previewModelRequestVersion;

        private string _settingsPath;
        private bool _isLoading;
        private bool _isRuntimeStatusUpdatePending;
        private RuntimeStatusSnapshot _runtimeStatus;
        private long _loadedRuntimeRevision = -1;
        private bool _runtimeEditorInitialized;
        private bool _calibrationProfilesLoaded;
        private bool _calibrationBusy;
        private bool _deviceRefreshPending;
        private bool _telemetryUpdatePending;
        private int _runtimeStatusFailureCount;
        private int _telemetryFailureCount;
        private string _previewModelDescription;
        private IReadOnlyList<DeviceOption> _onlinePhysicalDevices = Array.Empty<DeviceOption>();
        private IReadOnlyList<DeviceOption> _knownPhysicalDevices = Array.Empty<DeviceOption>();
        private readonly Dictionary<string, string> _knownSourceRoleTargets =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly ObservableCollection<RouteListItem> _routeItems = new ObservableCollection<RouteListItem>();
        private readonly List<RouteConfiguration> _workingRoutes = new List<RouteConfiguration>();
        private RouteConfiguration _selectedRoute;
        private bool _showingSettings;
        private SettingsSection _settingsSection = SettingsSection.Runtime;
        private bool _showSteamVrRoleTargets;
        private bool _followSteamVrWithTrackSwap;
        private bool _steamVrObservedForUiLifecycle;
        private bool _steamVrUiCloseScheduled;
        private readonly bool _startedForSteamVrSession;
        private RuntimeLifecycleMode _runtimeLifecycleMode;
        private bool _runtimeLifecycleSelectionReady;
        private bool _runtimeStartPending;
        private bool _runtimeLifecycleRestarting;
        private bool _isClosing;
        private bool _pendingDeletionBusy;
        private bool _pendingDeletionAutoRetrySuppressed;
        private bool _pendingStaticMappingBusy;
        private bool _pendingStaticMappingAutoRetrySuppressed;
        private bool _lastDeviceRefreshSteamVrRunning;
        private long _displayedDriverAppliedRevision = long.MinValue;
        private bool _displayedDriverConnected;

        public MainWindow()
        {
            InitializeComponent();

            UiPreferences preferences = _uiPreferencesService.Load();
            _showSteamVrRoleTargets = preferences.ShowSteamVrRoleTargets;
            _runtimeLifecycleMode = preferences.RuntimeLifecycleMode;
            _startedForSteamVrSession = Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--steamvr-session", StringComparison.OrdinalIgnoreCase));
            _followSteamVrWithTrackSwap =
                preferences.FollowSteamVrWithTrackSwap || _startedForSteamVrSession;
            ShowSteamVrRoleTargetsCheckBox.IsChecked = _showSteamVrRoleTargets;
            FollowSteamVrWithTrackSwapCheckBox.IsChecked = _followSteamVrWithTrackSwap;
            RuntimeLifecycleComboBox.ItemsSource = new[]
            {
                new RuntimeLifecycleOption(RuntimeLifecycleMode.FollowTrackSwap, "跟随 TrackSwap"),
                new RuntimeLifecycleOption(RuntimeLifecycleMode.FollowSteamVr, "跟随 SteamVR")
            };
            RuntimeLifecycleComboBox.SelectedItem =
                ((IEnumerable<RuntimeLifecycleOption>)RuntimeLifecycleComboBox.ItemsSource)
                    .First(option => option.Mode == _runtimeLifecycleMode);
            _runtimeLifecycleSelectionReady = true;

            RouteListBox.ItemsSource = _routeItems;

            TargetComboBox.ItemsSource = BuildTargets(
                Array.Empty<DeviceOption>(),
                Array.Empty<TargetOption>(),
                includeRoleTargets: false,
                includeConcreteDevices: true);
            TargetComboBox.SelectedIndex = 0;
            RuntimeTargetComboBox.ItemsSource = BuildTargets(
                Array.Empty<DeviceOption>(),
                Array.Empty<TargetOption>(),
                includeRoleTargets: _showSteamVrRoleTargets,
                includeConcreteDevices: true);
            RuntimeTargetComboBox.SelectedIndex = 0;
            CalibrationNameTextBox.Text = "校准 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            InitializePosePreview();
            ShowSettingsSection(_settingsSection);
            UpdateContentVisibility();

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += async (_, __) => await RefreshStatusAsync();
            _deviceRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _deviceRefreshTimer.Tick += async (_, __) => await RefreshDevicesAsync();
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _telemetryTimer.Tick += async (_, __) => await RefreshTelemetryAsync();

            Loaded += async (_, __) =>
            {
                RefreshAll();
                await EnsureRuntimeStartedAsync(showError: false);
                _statusTimer.Start();
                _deviceRefreshTimer.Start();
                _telemetryTimer.Start();
                await RefreshStatusAsync();
                if (_statusService.IsRunning() && _followSteamVrWithTrackSwap)
                {
                    await SyncSteamVrAutoLaunchAsync(enabled: true, showWarning: false);
                }
            };
            Closed += (_, __) =>
            {
                _isClosing = true;
                _previewModelRequestVersion++;
                _statusTimer.Stop();
                _deviceRefreshTimer.Stop();
                _telemetryTimer.Stop();
            };
        }

        private void RefreshAll(
            IReadOnlyList<DeviceOption> suppliedOnlineSources = null,
            bool useSuppliedOnlineSources = false)
        {
            _isLoading = true;
            try
            {
                string previousTargetPath = (TargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                string previousRuntimeSourcePath = (RuntimeSourceComboBox.SelectedItem as DeviceOption)?.DevicePath;
                string previousRuntimeTargetPath = (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                _settingsPath = _pathService.FindSettingsPath();
                SettingsPathText.Text = _settingsPath ?? "未找到 steamvr.vrsettings";
                DeviceHistoryPathText.Text = _deviceHistoryService.FilePath;
                ViewRawButton.IsEnabled = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath);
                RestoreBackupButton.IsEnabled = ViewRawButton.IsEnabled;

                IReadOnlyList<DeviceOption> savedSources = ViewRawButton.IsEnabled
                    ? _settingsService.ReadKnownSources(_settingsPath)
                    : Array.Empty<DeviceOption>();
                bool steamVrRunning = _statusService.IsRunning();
                IReadOnlyList<DeviceOption> onlineSources = useSuppliedOnlineSources
                    ? suppliedOnlineSources ?? Array.Empty<DeviceOption>()
                    : Array.Empty<DeviceOption>();
                string enumerationWarning = null;

                if (steamVrRunning && !useSuppliedOnlineSources)
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

                IReadOnlyList<DeviceOption> onlinePhysicalDevices = onlineSources
                    .Where(IsPhysicalDevice)
                    .ToList();
                _deviceHistoryService.Remember(onlinePhysicalDevices);
                _onlinePhysicalDevices = onlinePhysicalDevices;
                IReadOnlyList<DeviceOption> rememberedDevices = _deviceHistoryService.Load();
                var onlineDevicePaths = new HashSet<string>(
                    onlinePhysicalDevices.Select(device => device.DevicePath),
                    StringComparer.Ordinal);
                IReadOnlyList<DeviceHistoryListItem> historyItems = rememberedDevices
                    .Select(device => new DeviceHistoryListItem(
                        DeviceOption.BaseDisplayName(device.DisplayName),
                        device.DevicePath,
                        onlineDevicePaths.Contains(device.DevicePath)))
                    .OrderByDescending(device => device.IsOnline)
                    .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                DeviceHistoryCountText.Text = historyItems.Count + " 台设备";
                DeviceHistoryItemsControl.ItemsSource = historyItems;
                EmptyDeviceHistoryText.Visibility = historyItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                _knownPhysicalDevices = MergeDeviceCatalog(
                    onlinePhysicalDevices,
                    rememberedDevices);
                foreach (DeviceOption device in onlinePhysicalDevices.Where(device =>
                    !string.IsNullOrWhiteSpace(device.DevicePath) &&
                    !string.IsNullOrWhiteSpace(device.RoleTargetPath)))
                {
                    _knownSourceRoleTargets[device.DevicePath] = device.RoleTargetPath;
                }

                IReadOnlyList<TargetOption> savedDeviceTargets = ViewRawButton.IsEnabled
                    ? _settingsService.ReadKnownDeviceTargets(_settingsPath)
                    : Array.Empty<TargetOption>();
                IReadOnlyList<TargetOption> savedRoleTargets = ViewRawButton.IsEnabled
                    ? _settingsService.ReadKnownRoleTargets(_settingsPath)
                    : Array.Empty<TargetOption>();
                IReadOnlyList<TargetOption> targets = BuildTargets(
                    _knownPhysicalDevices,
                    savedDeviceTargets.Concat(savedRoleTargets).ToList(),
                    includeRoleTargets: false,
                    includeConcreteDevices: true,
                    currentTargetPath: previousTargetPath);
                TargetComboBox.ItemsSource = targets;
                TargetOption selectedTarget = targets.FirstOrDefault(target =>
                    string.Equals(target.TargetPath, previousTargetPath, StringComparison.Ordinal))
                    ?? targets.FirstOrDefault();
                TargetComboBox.SelectedItem = selectedTarget;

                string currentSource = selectedTarget == null || !ViewRawButton.IsEnabled
                    ? null
                    : _settingsService.ReadSourceForTarget(_settingsPath, selectedTarget.TargetPath);

                IReadOnlyList<DeviceOption> legacySourceCatalog = MergeDeviceCatalog(
                    _knownPhysicalDevices,
                    savedSources.Where(IsPhysicalDevice));
                IReadOnlyList<DeviceOption> sources = BuildDeviceChoices(legacySourceCatalog, currentSource);
                SourceComboBox.ItemsSource = sources;
                DeviceOption selectedSource = sources.FirstOrDefault(source =>
                    string.Equals(source.DevicePath, currentSource, StringComparison.Ordinal));
                SourceComboBox.SelectedItem = selectedSource ?? sources.FirstOrDefault();
                PopulateRuntimeOptions(
                    previousRuntimeSourcePath,
                    previousRuntimeTargetPath,
                    savedDeviceTargets);
                RefreshOverrideList();

                if (!string.IsNullOrWhiteSpace(enumerationWarning))
                {
                    RefreshButton.ToolTip = "在线设备读取失败，已回退到配置记录：" + enumerationWarning;
                }
                else
                {
                    RefreshButton.ToolTip = "重新扫描设备并读取配置";
                }
                _lastDeviceRefreshSteamVrRunning = steamVrRunning;
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
                _ = RefreshPreviewDeviceModelsAsync();
            }
        }

        private async Task RefreshDevicesAsync()
        {
            if (_deviceRefreshPending || _isLoading || _calibrationBusy ||
                SourceComboBox.IsDropDownOpen || TargetComboBox.IsDropDownOpen ||
                RuntimeSourceComboBox.IsDropDownOpen || RuntimeTargetComboBox.IsDropDownOpen ||
                CalibrationTargetComboBox.IsDropDownOpen)
            {
                return;
            }

            _deviceRefreshPending = true;
            try
            {
                bool steamVrRunning = _statusService.IsRunning();
                IReadOnlyList<DeviceOption> onlineSources = Array.Empty<DeviceOption>();
                if (steamVrRunning)
                {
                    try
                    {
                        string runtimePath = _pathService.FindRuntimePath();
                        onlineSources = await Task.Run(() =>
                            _openVrDeviceService.EnumerateOnlineDevices(runtimePath));
                    }
                    catch (Exception exception)
                    {
                        RefreshButton.ToolTip =
                            "实时设备扫描失败，已保留上次结果：" + exception.Message;
                        return;
                    }
                }

                IReadOnlyList<DeviceOption> physicalDevices = onlineSources
                    .Where(IsPhysicalDevice)
                    .ToList();
                if (steamVrRunning == _lastDeviceRefreshSteamVrRunning &&
                    DeviceCatalogsEqual(_onlinePhysicalDevices, physicalDevices))
                {
                    return;
                }

                RefreshAll(onlineSources, useSuppliedOnlineSources: true);
            }
            finally
            {
                _deviceRefreshPending = false;
            }
        }

        private static bool DeviceCatalogsEqual(
            IReadOnlyList<DeviceOption> left,
            IReadOnlyList<DeviceOption> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            var rightByPath = right.ToDictionary(device => device.DevicePath, StringComparer.Ordinal);
            foreach (DeviceOption device in left)
            {
                if (!rightByPath.TryGetValue(device.DevicePath, out DeviceOption match) ||
                    !string.Equals(device.DisplayName, match.DisplayName, StringComparison.Ordinal) ||
                    !string.Equals(device.RoleTargetPath, match.RoleTargetPath, StringComparison.Ordinal) ||
                    !string.Equals(device.RenderModelName, match.RenderModelName, StringComparison.Ordinal) ||
                    device.DeviceKind != match.DeviceKind)
                {
                    return false;
                }
            }
            return true;
        }

        private void PopulateRuntimeOptions(
            string selectedSourcePath,
            string selectedTargetPath,
            IReadOnlyList<TargetOption> savedDeviceTargets)
        {
            IReadOnlyList<DeviceOption> sources = BuildDeviceChoices(_knownPhysicalDevices, selectedSourcePath);
            RuntimeSourceComboBox.ItemsSource = sources;
            RuntimeSourceComboBox.SelectedItem = sources.FirstOrDefault(device =>
                string.Equals(device.DevicePath, selectedSourcePath, StringComparison.Ordinal));

            IReadOnlyList<TargetOption> targets = BuildTargets(
                _knownPhysicalDevices,
                savedDeviceTargets,
                includeRoleTargets: _showSteamVrRoleTargets,
                includeConcreteDevices: true,
                currentTargetPath: selectedTargetPath);
            RuntimeTargetComboBox.ItemsSource = targets;
            RuntimeTargetComboBox.SelectedItem = targets.FirstOrDefault(target =>
                string.Equals(target.TargetPath, selectedTargetPath, StringComparison.Ordinal))
                ?? targets.FirstOrDefault();
            RefreshCalibrationTargets();
        }

        private static IReadOnlyList<TargetOption> BuildTargets(
            IReadOnlyList<DeviceOption> knownDevices,
            IReadOnlyList<TargetOption> savedDeviceTargets,
            bool includeRoleTargets,
            bool includeConcreteDevices,
            string currentTargetPath = null)
        {
            var targets = new List<TargetOption>();
            if (includeRoleTargets)
            {
                targets.Add(new TargetOption("SteamVR 角色 · 右手", "/user/hand/right"));
                targets.Add(new TargetOption("SteamVR 角色 · 左手", "/user/hand/left"));
                targets.Add(new TargetOption("SteamVR 角色 · 头显", "/user/head"));
            }

            if (!includeConcreteDevices)
            {
                return targets;
            }

            var knownPaths = new HashSet<string>(targets.Select(target => target.TargetPath), StringComparer.Ordinal);
            DeviceOption currentDevice = knownDevices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, currentTargetPath, StringComparison.Ordinal));
            TargetOption currentSavedTarget = savedDeviceTargets.FirstOrDefault(target =>
                string.Equals(target.TargetPath, currentTargetPath, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(currentTargetPath) && knownPaths.Add(currentTargetPath))
            {
                string currentName = currentDevice != null
                    ? DeviceOption.BaseDisplayName(currentDevice.DisplayName)
                    : currentSavedTarget != null
                        ? DeviceOption.BaseDisplayName(currentSavedTarget.DisplayName)
                        : currentTargetPath.StartsWith("/user/", StringComparison.Ordinal)
                            ? BuildConfiguredRuntimeTargetName(currentTargetPath)
                            : DeviceNameFromPath(currentTargetPath);
                string currentPrefix = currentTargetPath.StartsWith("/user/", StringComparison.Ordinal)
                    ? string.Empty
                    : currentDevice?.IsOnline == true ? "在线 · " : "离线 · ";
                targets.Add(new TargetOption(currentPrefix + currentName, currentTargetPath));
            }

            foreach (DeviceOption device in knownDevices.Where(device => device.IsOnline))
            {
                if (knownPaths.Add(device.DevicePath))
                {
                    targets.Add(new TargetOption(
                        "在线 · " + DeviceOption.BaseDisplayName(device.DisplayName),
                        device.DevicePath));
                }
            }

            foreach (DeviceOption device in knownDevices.Where(device => !device.IsOnline))
            {
                if (knownPaths.Add(device.DevicePath))
                {
                    targets.Add(new TargetOption(
                        "离线 · " + DeviceOption.BaseDisplayName(device.DisplayName),
                        device.DevicePath));
                }
            }

            foreach (TargetOption target in savedDeviceTargets.Where(target => knownPaths.Add(target.TargetPath)))
            {
                targets.Add(new TargetOption(
                    target.TargetPath.StartsWith("/devices/", StringComparison.Ordinal)
                        ? "离线 · " + DeviceOption.BaseDisplayName(target.DisplayName)
                        : target.DisplayName,
                    target.TargetPath));
            }

            return targets;
        }

        private static IReadOnlyList<DeviceOption> MergeDeviceCatalog(
            params IEnumerable<DeviceOption>[] groups)
        {
            var result = new List<DeviceOption>();
            var knownPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (IEnumerable<DeviceOption> group in groups)
            {
                foreach (DeviceOption device in group ?? Enumerable.Empty<DeviceOption>())
                {
                    if (IsPhysicalDevice(device) && knownPaths.Add(device.DevicePath))
                    {
                        result.Add(device);
                    }
                }
            }
            return result;
        }

        private static IReadOnlyList<DeviceOption> BuildDeviceChoices(
            IReadOnlyList<DeviceOption> knownDevices,
            string currentDevicePath)
        {
            var result = new List<DeviceOption>();
            DeviceOption current = knownDevices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(currentDevicePath))
            {
                result.Add(CloneDeviceOption(
                    current,
                    (current?.IsOnline == true ? "在线 · " : "离线 · ") + (current != null
                        ? DeviceOption.BaseDisplayName(current.DisplayName)
                        : DeviceNameFromPath(currentDevicePath)),
                    currentDevicePath));
            }

            foreach (DeviceOption device in knownDevices.Where(device =>
                !string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal) && device.IsOnline))
            {
                result.Add(CloneDeviceOption(
                    device,
                    "在线 · " + DeviceOption.BaseDisplayName(device.DisplayName),
                    device.DevicePath));
            }
            foreach (DeviceOption device in knownDevices.Where(device =>
                !string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal) && !device.IsOnline))
            {
                result.Add(CloneDeviceOption(
                    device,
                    "离线 · " + DeviceOption.BaseDisplayName(device.DisplayName),
                    device.DevicePath));
            }
            return result;
        }

        private static DeviceOption CloneDeviceOption(
            DeviceOption device,
            string displayName,
            string devicePath)
        {
            return new DeviceOption(
                displayName,
                devicePath,
                device?.IsOnline == true,
                device?.DeviceIndex,
                device?.SerialNumber,
                device?.RoleTargetPath,
                device?.RenderModelName,
                device?.DeviceKind ?? TrackedDeviceKind.Unknown);
        }

        private static bool IsPhysicalDevice(DeviceOption device)
        {
            return device != null &&
                !string.IsNullOrWhiteSpace(device.DevicePath) &&
                device.DevicePath.StartsWith("/devices/", StringComparison.Ordinal) &&
                device.DevicePath.IndexOf(ProtocolConstants.VirtualSerialPrefix, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string DeviceNameFromPath(string devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return "未知设备";
            }
            int separator = devicePath.LastIndexOf('/');
            return separator >= 0 && separator + 1 < devicePath.Length
                ? devicePath.Substring(separator + 1)
                : devicePath;
        }

        private void UpdateSteamVrStatus()
        {
            bool running = _statusService.IsRunning();
            if (running && _followSteamVrWithTrackSwap)
            {
                _steamVrObservedForUiLifecycle = true;
                _steamVrUiCloseScheduled = false;
            }
            else if (!running &&
                _followSteamVrWithTrackSwap &&
                _steamVrObservedForUiLifecycle &&
                !_steamVrUiCloseScheduled &&
                !_isClosing)
            {
                _steamVrUiCloseScheduled = true;
            }
            SteamVrStatusText.Text = "SteamVR";
            SteamVrStatusText.Foreground = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrDot.Fill = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrBadge.Background = Brushes.Transparent;

            ApplyButton.IsEnabled = !running
                && SourceComboBox.SelectedItem is DeviceOption
                && IsConcreteDeviceTarget(TargetComboBox.SelectedItem as TargetOption)
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
            bool stoppedStateWorkCompleted = false;
            try
            {
                RuntimeStatusSnapshot status = await _runtimeControlService.GetStatusAsync();
                _runtimeStatusFailureCount = 0;
                _runtimeStatus = status;
                ShowRuntimeOnline(status);
                if (!_calibrationProfilesLoaded && !_calibrationBusy)
                {
                    await RefreshCalibrationProfilesAsync();
                }
                if (!_statusService.IsRunning())
                {
                    bool deletionsCompleted = true;
                    if (!_pendingDeletionAutoRetrySuppressed &&
                        status.Configuration?.Routes?.Any(route => route.PendingDeletion) == true)
                    {
                        deletionsCompleted = await FinalizePendingDeletionsAsync();
                    }
                    bool mappingsCompleted = false;
                    if (!_pendingStaticMappingAutoRetrySuppressed &&
                        deletionsCompleted &&
                        !_workingRoutes.Any(route => route.PendingDeletion))
                    {
                        mappingsCompleted = await ReconcilePendingStaticMappingsAsync();
                    }
                    stoppedStateWorkCompleted = deletionsCompleted && mappingsCompleted;
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                _runtimeStatusFailureCount++;
                if (_runtimeStatusFailureCount >= 3)
                {
                    _runtimeStatus = null;
                    ShowRuntimeOffline(exception.Message);
                    await EnsureRuntimeStartedAsync(showError: false);
                }
            }
            finally
            {
                _isRuntimeStatusUpdatePending = false;
                UpdateRuntimeSelectionDetails();
                if (_steamVrUiCloseScheduled && stoppedStateWorkCompleted && !_isClosing)
                {
                    Close();
                }
            }
        }

        private void ShowRuntimeOnline(RuntimeStatusSnapshot status)
        {
            RuntimeStatusText.Text = "Runtime";
            RuntimeStatusText.Foreground = FindBrush("SuccessBrush");
            RuntimeDot.Fill = FindBrush("SuccessBrush");
            RuntimeBadge.Background = Brushes.Transparent;
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
            RuntimeBadge.Background = Brushes.Transparent;
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
            bool legacyRoleTarget = target != null && !IsConcreteDeviceTarget(target);
            LegacyStaticTargetWarningText.Visibility = legacyRoleTarget
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateSteamVrStatus();
        }

        private void UpdateRuntimeSelectionDetails()
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = RuntimeTargetComboBox.SelectedItem as TargetOption;
            RuntimeSourcePathText.Text = source?.DevicePath ?? "未选择物理来源";
            RuntimeTargetPathText.Text = target?.TargetPath ?? "未选择静态目标";
            ApplyRuntimeButton.IsEnabled = _runtimeStatus != null && _selectedRoute != null &&
                !_selectedRoute.PendingDeletion && source != null && IsAllowedRuntimeTarget(target);

            RouteConfiguration activeRoute = _runtimeStatus?.Configuration?.Routes?.FirstOrDefault(candidate =>
                _selectedRoute != null && string.Equals(candidate.RouteId, _selectedRoute.RouteId, StringComparison.Ordinal));
            bool sourceWillChange = activeRoute != null && source != null &&
                !string.Equals(activeRoute.SourceDevicePath, source.DevicePath, StringComparison.Ordinal);
            bool blockedRoleTarget = target != null && IsSteamVrRoleTargetPath(target.TargetPath) &&
                !_showSteamVrRoleTargets;
            RuntimeRouteChangeWarningText.Text = blockedRoleTarget
                ? "当前目标来自旧角色配置。请选择一个明确的在线实体设备后再应用。"
                : sourceWillChange
                ? "注意：应用后物理来源将从 “" + activeRoute.SourceDevicePath + "” 切换为 “" + source.DevicePath + "”。"
                : string.Empty;
            RuntimeRouteChangeWarningText.Visibility = blockedRoleTarget || sourceWillChange
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
                string state = _selectedRoute.PendingDeletion ? "待删除" : !_selectedRoute.Enabled ? "已停用" : !mapped ? "待映射" : !steamVrRunning ? "已配置" : applied ? "已应用" : "待应用";
                SelectedRouteStateText.Text = state;
                bool synchronized = mapped && (!steamVrRunning || applied);
                SelectedRouteStateText.Foreground = FindBrush(_selectedRoute.PendingDeletion ? "WarningBrush" : !_selectedRoute.Enabled ? "MutedTextBrush" : synchronized ? "SuccessBrush" : "WarningBrush");
                RouteSyncText.Text = _selectedRoute.PendingDeletion
                    ? "退出 SteamVR 后将自动清理绑定并删除"
                    : synchronized ? "配置已同步" : !mapped
                    ? steamVrRunning ? "退出 SteamVR 后将自动写入静态映射" : "正在等待自动写入静态映射"
                    : "配置待同步";
                RouteSyncText.Foreground = FindBrush(_selectedRoute.PendingDeletion || !synchronized ? "WarningBrush" : "SuccessBrush");
                SelectedRouteSummaryText.Text = _selectedRoute.PendingDeletion
                    ? "该配置仍保持输出；可通过右键菜单取消删除。"
                    : blockedRoleTarget
                    ? "旧配置使用 SteamVR 角色目标；请选择明确的在线实体设备完成迁移。"
                    : source == null || target == null
                    ? "选择来源与目标以完成配置。"
                    : DeviceOption.BaseDisplayName(source.DisplayName) + " 提供定位，" +
                        DeviceOption.BaseDisplayName(target.DisplayName) + " 保留输入。";
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
            bool runtimeReady = _runtimeStatus != null && !_calibrationBusy &&
                _selectedRoute?.PendingDeletion != true;
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
                PendingDeletion = route.PendingDeletion,
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
                string state = route.PendingDeletion ? "待删除" : !route.Enabled ? "已停用" : !mapped ? "待映射" : !steamVrRunning ? "已配置" : driverApplied ? "已应用" : "等待驱动";
                Brush brush = FindBrush(route.PendingDeletion ? "WarningBrush" : !route.Enabled ? "MutedTextBrush" : mapped && (!steamVrRunning || driverApplied) ? "SuccessBrush" : "WarningBrush");
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
                IReadOnlyList<DeviceOption> sources = BuildDeviceChoices(
                    _knownPhysicalDevices,
                    route.SourceDevicePath);
                RuntimeSourceComboBox.ItemsSource = sources;
                DeviceOption source = sources.FirstOrDefault(candidate => string.Equals(candidate.DevicePath, route.SourceDevicePath, StringComparison.Ordinal));
                RuntimeSourceComboBox.SelectedItem = source ?? sources.FirstOrDefault();

                IReadOnlyList<TargetOption> targets = BuildTargets(
                    _knownPhysicalDevices,
                    Array.Empty<TargetOption>(),
                    includeRoleTargets: _showSteamVrRoleTargets,
                    includeConcreteDevices: true,
                    currentTargetPath: route.TargetDevicePath);
                RuntimeTargetComboBox.ItemsSource = targets;
                TargetOption target = targets.FirstOrDefault(candidate => string.Equals(candidate.TargetPath, route.TargetDevicePath, StringComparison.Ordinal));
                RuntimeTargetComboBox.SelectedItem = target ?? targets.FirstOrDefault();
                LoadOffsetFields(route.Offset ?? PoseOffset.Identity());
                SelectedProxyText.Text = ProtocolConstants.GetVirtualSerial(route.VirtualDeviceSlot);
                RuntimeProxyText.Text = ProtocolConstants.GetVirtualSerial(route.VirtualDeviceSlot);
                SelectedRouteNameText.Text = route.Name;
                ToggleSelectedRouteButton.Content = route.Enabled ? "停用" : "启用";
                ToggleSelectedRouteButton.IsEnabled = !route.PendingDeletion;
                RuntimeSourceComboBox.IsEnabled = !route.PendingDeletion;
                RuntimeTargetComboBox.IsEnabled = !route.PendingDeletion;
            }
            finally
            {
                _isLoading = false;
            }
            UpdateRuntimeSelectionDetails();
            UpdateContentVisibility();
            _ = RefreshPreviewDeviceModelsAsync();
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

        private static bool IsConcreteDeviceTarget(TargetOption target)
        {
            return target != null &&
                !string.IsNullOrWhiteSpace(target.TargetPath) &&
                target.TargetPath.StartsWith("/devices/", StringComparison.Ordinal);
        }

        private static bool IsSteamVrRoleTargetPath(string targetPath)
        {
            return string.Equals(targetPath, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal) ||
                string.Equals(targetPath, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal) ||
                string.Equals(targetPath, ProtocolConstants.HeadRolePath, StringComparison.Ordinal);
        }

        private bool IsAllowedRuntimeTarget(TargetOption target)
        {
            return IsConcreteDeviceTarget(target) ||
                (_showSteamVrRoleTargets && target != null && IsSteamVrRoleTargetPath(target.TargetPath));
        }

        private bool IsAllowedRuntimeTargetPath(string targetPath)
        {
            return !string.IsNullOrWhiteSpace(targetPath) &&
                (targetPath.StartsWith("/devices/", StringComparison.Ordinal) ||
                    (_showSteamVrRoleTargets && IsSteamVrRoleTargetPath(targetPath)));
        }

        private static string BuildConfiguredRuntimeTargetName(string targetPath)
        {
            if (string.Equals(targetPath, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal))
            {
                return "旧角色目标 · 右手（请改选实体设备）";
            }
            if (string.Equals(targetPath, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal))
            {
                return "旧角色目标 · 左手（请改选实体设备）";
            }
            if (string.Equals(targetPath, ProtocolConstants.HeadRolePath, StringComparison.Ordinal))
            {
                return "旧角色目标 · 头显（请改选实体设备）";
            }
            return "已配置目标";
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
            UpdateRouteContextMenu();
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
                UpdateRouteContextMenu();
            }
        }

        private void SettingsNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            _showingSettings = true;
            RouteListBox.SelectedItem = null;
            UpdateContentVisibility();
        }

        private void SettingsCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == SettingsRuntimeCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Runtime);
            }
            else if (sender == SettingsSteamVrCategoryButton)
            {
                ShowSettingsSection(SettingsSection.SteamVr);
            }
            else if (sender == SettingsDevicesCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Devices);
            }
            else if (sender == SettingsAdvancedCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Advanced);
            }
        }

        private void ShowSettingsSection(SettingsSection section)
        {
            _settingsSection = section;
            RuntimeSettingsPanel.Visibility = section == SettingsSection.Runtime ? Visibility.Visible : Visibility.Collapsed;
            SteamVrSettingsPanel.Visibility = section == SettingsSection.SteamVr ? Visibility.Visible : Visibility.Collapsed;
            DeviceSettingsPanel.Visibility = section == SettingsSection.Devices ? Visibility.Visible : Visibility.Collapsed;
            AdvancedSettingsPanel.Visibility = section == SettingsSection.Advanced ? Visibility.Visible : Visibility.Collapsed;

            UpdateSettingsCategoryButton(SettingsRuntimeCategoryButton, section == SettingsSection.Runtime);
            UpdateSettingsCategoryButton(SettingsSteamVrCategoryButton, section == SettingsSection.SteamVr);
            UpdateSettingsCategoryButton(SettingsDevicesCategoryButton, section == SettingsSection.Devices);
            UpdateSettingsCategoryButton(SettingsAdvancedCategoryButton, section == SettingsSection.Advanced);
        }

        private void ShowSteamVrRoleTargetsCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool previousValue = _showSteamVrRoleTargets;
            bool nextValue = ShowSteamVrRoleTargetsCheckBox.IsChecked == true;
            try
            {
                _showSteamVrRoleTargets = nextValue;
                SaveUiPreferences();
                RefreshAll();
                if (_selectedRoute != null)
                {
                    ShowSelectedRoute(_selectedRoute);
                }
            }
            catch (Exception exception)
            {
                _showSteamVrRoleTargets = previousValue;
                ShowSteamVrRoleTargetsCheckBox.IsChecked = previousValue;
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法保存高级选项",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void FollowSteamVrWithTrackSwapCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool previousValue = _followSteamVrWithTrackSwap;
            bool nextValue = FollowSteamVrWithTrackSwapCheckBox.IsChecked == true;
            try
            {
                _followSteamVrWithTrackSwap = nextValue;
                if (!nextValue)
                {
                    _steamVrObservedForUiLifecycle = false;
                    _steamVrUiCloseScheduled = false;
                }
                else if (_statusService.IsRunning())
                {
                    _steamVrObservedForUiLifecycle = true;
                }
                SaveUiPreferences();

                if (_statusService.IsRunning())
                {
                    await SyncSteamVrAutoLaunchAsync(nextValue, showWarning: true);
                }
            }
            catch (Exception exception)
            {
                _followSteamVrWithTrackSwap = previousValue;
                FollowSteamVrWithTrackSwapCheckBox.IsChecked = previousValue;
                SaveUiPreferences();
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法保存高级选项",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task SyncSteamVrAutoLaunchAsync(bool enabled, bool showWarning)
        {
            try
            {
                string runtimePath = _pathService.FindRuntimePath();
                string manifestPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "TrackSwap.vrmanifest");
                await Task.Run(() => _steamVrApplicationService.SetAutoLaunch(
                    runtimePath,
                    manifestPath,
                    enabled));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is System.ComponentModel.Win32Exception)
            {
                if (showWarning)
                {
                    MessageBox.Show(
                        this,
                        "TrackSwap 自身的跟随机制仍会生效，但未能同步 SteamVR 的启动应用列表。\n\n" + exception.Message,
                        "SteamVR 启动设置未同步",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private async void RuntimeLifecycleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeLifecycleSelectionReady ||
                !(RuntimeLifecycleComboBox.SelectedItem is RuntimeLifecycleOption selected) ||
                selected.Mode == _runtimeLifecycleMode)
            {
                return;
            }

            RuntimeLifecycleMode previousMode = _runtimeLifecycleMode;
            try
            {
                _runtimeLifecycleMode = selected.Mode;
                SaveUiPreferences();
                await RestartRuntimeForLifecycleChangeAsync();
            }
            catch (Exception exception)
            {
                _runtimeLifecycleMode = previousMode;
                SaveUiPreferences();
                _runtimeLifecycleSelectionReady = false;
                RuntimeLifecycleComboBox.SelectedItem =
                    ((IEnumerable<RuntimeLifecycleOption>)RuntimeLifecycleComboBox.ItemsSource)
                        .First(option => option.Mode == previousMode);
                _runtimeLifecycleSelectionReady = true;
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法更改 Runtime 启停行为",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveUiPreferences()
        {
            _uiPreferencesService.Save(new UiPreferences
            {
                ShowSteamVrRoleTargets = _showSteamVrRoleTargets,
                RuntimeLifecycleMode = _runtimeLifecycleMode,
                FollowSteamVrWithTrackSwap = _followSteamVrWithTrackSwap
            });
        }

        private async Task RestartRuntimeForLifecycleChangeAsync()
        {
            _runtimeLifecycleRestarting = true;
            try
            {
                try
                {
                    await _runtimeControlService.ShutdownAsync();
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is TimeoutException ||
                    exception is UnauthorizedAccessException ||
                    exception is InvalidDataException)
                {
                }

                await Task.Delay(600);
                if (!_runtimeControlService.TryStartRuntime(
                    _runtimeLifecycleMode,
                    Process.GetCurrentProcess().Id,
                    out string error))
                {
                    throw new InvalidOperationException(error);
                }
                _runtimeStatusFailureCount = 0;
                await Task.Delay(600);
                await RefreshStatusAsync();
            }
            finally
            {
                _runtimeLifecycleRestarting = false;
            }
        }

        private async Task EnsureRuntimeStartedAsync(bool showError)
        {
            if (_runtimeStartPending || _runtimeLifecycleRestarting || _isClosing)
            {
                return;
            }

            _runtimeStartPending = true;
            try
            {
                if (!_runtimeControlService.TryStartRuntime(
                    _runtimeLifecycleMode,
                    Process.GetCurrentProcess().Id,
                    out string error))
                {
                    if (showError)
                    {
                        MessageBox.Show(this, error, "Runtime 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }
                await Task.Delay(500);
            }
            finally
            {
                _runtimeStartPending = false;
            }
        }

        private static void UpdateSettingsCategoryButton(Button button, bool selected)
        {
            button.Background = (Brush)Application.Current.FindResource(selected ? "AccentSoftBrush" : "SurfaceBrush");
            button.BorderBrush = (Brush)Application.Current.FindResource(selected ? "AccentBorderMutedBrush" : "BorderBrush");
            button.Foreground = (Brush)Application.Current.FindResource(selected ? "TextBrush" : "MutedTextBrush");
        }

        private void UpdateRouteContextMenu()
        {
            bool pendingDeletion = _selectedRoute?.PendingDeletion == true;
            RenameRouteMenuItem.IsEnabled = _selectedRoute != null && !pendingDeletion;
            ToggleRouteMenuItem.IsEnabled = _selectedRoute != null && !pendingDeletion;
            DeleteRouteMenuItem.Header = pendingDeletion ? "取消删除" : "删除配置…";
            DeleteRouteMenuItem.Foreground = pendingDeletion
                ? FindBrush("TextBrush")
                : FindBrush("DestructiveBrush");
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
            if (_runtimeStatus == null)
            {
                MessageBox.Show(this, "Runtime 离线时无法安全记录待删除状态。请先启动 Runtime。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool enabling = !_selectedRoute.Enabled;
            if (enabling)
            {
                if (!IsAllowedRuntimeTargetPath(_selectedRoute.TargetDevicePath))
                {
                    MessageBox.Show(
                        this,
                        "该配置仍使用旧的 SteamVR 角色目标。请先选择一个明确的在线实体设备并应用，再启用配置。",
                        "需要迁移目标",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                _selectedRoute.Enabled = true;
                var candidate = new RuntimeConfiguration
                {
                    Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                    Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList()
                };
                IReadOnlyList<string> dependencyErrors =
                    ConfigurationValidator.ValidateSourceRoleDependencies(
                        candidate,
                        _knownSourceRoleTargets);
                _selectedRoute.Enabled = false;
                if (dependencyErrors.Count != 0)
                {
                    MessageBox.Show(
                        this,
                        "无法启用：该配置会让一条路由读取另一条路由已经覆盖的设备角色，形成级联移动。\n\n" +
                        string.Join(Environment.NewLine, dependencyErrors),
                        "检测到跨路由位姿级联",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
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
            if (_selectedRoute == null)
            {
                return;
            }

            if (_selectedRoute.PendingDeletion)
            {
                if (!_statusService.IsRunning() && !string.IsNullOrWhiteSpace(_settingsPath))
                {
                    string proxyPath = ProtocolConstants.GetVirtualDevicePath(_selectedRoute.VirtualDeviceSlot);
                    bool mapped = _settingsService.ReadOverrides(_settingsPath).Any(mapping =>
                        string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal));
                    if (!mapped && IsRouteComplete(_selectedRoute))
                    {
                        _settingsService.ApplyOverride(_settingsPath, proxyPath, _selectedRoute.TargetDevicePath);
                        RefreshOverrideList();
                    }
                }
                _selectedRoute.PendingDeletion = false;
                _pendingDeletionAutoRetrySuppressed = false;
                await PersistWorkingRoutesAsync();
                return;
            }

            bool steamVrRunning = _statusService.IsRunning();
            string message = steamVrRunning
                ? "SteamVR 正在运行。配置会先标记为“待删除”并继续输出，避免目标立即失去定位。完全退出 SteamVR 后，TrackSwap 将自动清理静态绑定并完成删除。\n\n已保存的校准档案不会删除。"
                : "确定删除配置 “" + _selectedRoute.Name + "”？TrackSwap 将清理对应的静态绑定；已保存的校准档案不会删除。";
            if (MessageBox.Show(
                    this,
                    message,
                    steamVrRunning ? "标记为待删除" : "删除配置",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            _selectedRoute.PendingDeletion = true;
            _pendingDeletionAutoRetrySuppressed = false;
            await PersistWorkingRoutesAsync();
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

        private async Task<bool> FinalizePendingDeletionsAsync()
        {
            if (_pendingDeletionBusy || _statusService.IsRunning() || _runtimeStatus == null)
            {
                return false;
            }

            List<RouteConfiguration> pendingRoutes = _workingRoutes
                .Where(route => route.PendingDeletion)
                .ToList();
            if (pendingRoutes.Count == 0)
            {
                return true;
            }

            _pendingDeletionBusy = true;
            try
            {
                if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
                {
                    throw new InvalidOperationException("未找到 steamvr.vrsettings，无法清理待删除配置的静态绑定。");
                }

                IReadOnlyList<TrackingOverrideOption> overrides = _settingsService.ReadOverrides(_settingsPath);
                foreach (RouteConfiguration route in pendingRoutes)
                {
                    string proxyPath = ProtocolConstants.GetVirtualDevicePath(route.VirtualDeviceSlot);
                    if (overrides.Any(mapping => string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal)))
                    {
                        _settingsService.RemoveOverride(_settingsPath, proxyPath);
                    }
                }
                RefreshOverrideList();

                var configuration = new RuntimeConfiguration
                {
                    Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                    Routes = _workingRoutes
                        .Where(route => !route.PendingDeletion && IsRouteComplete(route))
                        .Select(CloneRoute)
                        .ToList()
                };
                IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
                if (errors.Count != 0)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
                }

                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _workingRoutes.RemoveAll(route => route.PendingDeletion);
                if (_selectedRoute?.PendingDeletion == true)
                {
                    _selectedRoute = null;
                }
                _loadedRuntimeRevision = -1;
                _runtimeEditorInitialized = false;
                RuntimeStatusSnapshot refreshed = await _runtimeControlService.GetStatusAsync();
                _runtimeStatus = refreshed;
                ShowRuntimeOnline(refreshed);
                UpdateRouteContextMenu();
                return true;
            }
            catch (Exception exception)
            {
                _pendingDeletionAutoRetrySuppressed = true;
                MessageBox.Show(
                    this,
                    "待删除配置尚未完成，状态已保留，可在修复问题后重新启动 TrackSwap 重试。\n\n" + exception.Message,
                    "删除尚未完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                _pendingDeletionBusy = false;
            }
        }

        private Task<bool> ReconcilePendingStaticMappingsAsync()
        {
            if (_pendingStaticMappingBusy || _statusService.IsRunning() || _runtimeStatus == null)
            {
                return Task.FromResult(false);
            }
            if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
            {
                return Task.FromResult(false);
            }
            if (_workingRoutes.Any(route => route.PendingDeletion))
            {
                return Task.FromResult(false);
            }

            IReadOnlyDictionary<string, string> desiredMappings = _workingRoutes
                .Where(route => route.Enabled && IsRouteComplete(route))
                .ToDictionary(
                    route => ProtocolConstants.GetVirtualDevicePath(route.VirtualDeviceSlot),
                    route => route.TargetDevicePath,
                    StringComparer.Ordinal);

            _pendingStaticMappingBusy = true;
            try
            {
                bool changed = _settingsService.ReconcileTrackSwapOverrides(
                    _settingsPath,
                    desiredMappings);
                if (changed)
                {
                    RefreshOverrideList();
                    RefreshRouteList(_selectedRoute?.RouteId);
                    UpdateRuntimeSelectionDetails();
                }

                _pendingStaticMappingAutoRetrySuppressed = false;
                return Task.FromResult(true);
            }
            catch (Exception exception)
            {
                _pendingStaticMappingAutoRetrySuppressed = true;
                MessageBox.Show(
                    this,
                    "静态映射尚未自动写入，已保留待映射状态；修复问题后重新启动 TrackSwap 即可重试。\n\n" + exception.Message,
                    "映射尚未完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return Task.FromResult(false);
            }
            finally
            {
                _pendingStaticMappingBusy = false;
            }
        }

        private void RuntimeSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading)
            {
                if (_selectedRoute != null)
                {
                    _selectedRoute.SourceDevicePath =
                        (RuntimeSourceComboBox.SelectedItem as DeviceOption)?.DevicePath;
                    _selectedRoute.TargetDevicePath =
                        (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                }
                UpdateRuntimeSelectionDetails();
                _ = RefreshPreviewDeviceModelsAsync();
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
                source = new DeviceOption("离线 · " + DeviceNameFromPath(sourceDevicePath), sourceDevicePath);
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
            if (_selectedRoute != null)
            {
                ShowSelectedRoute(_selectedRoute);
            }
            await RefreshStatusAsync();
        }

        private async void StartRuntimeButton_Click(object sender, RoutedEventArgs e)
        {
            StartRuntimeButton.IsEnabled = false;
            await EnsureRuntimeStartedAsync(showError: true);
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

            if (!IsAllowedRuntimeTarget(target))
            {
                MessageBox.Show(
                    this,
                    "该配置仍使用隐藏的 SteamVR 角色目标。请先选择明确的实体设备，或在“设置 → 高级选项”中启用 SteamVR 角色目标。",
                    "需要迁移目标",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            errors = errors
                .Concat(ConfigurationValidator.ValidateSourceRoleDependencies(
                    configuration,
                    _knownSourceRoleTargets))
                .ToList();
            if (errors.Count != 0)
            {
                MessageBox.Show(
                    this,
                    errors.Any(error => error.IndexOf("cross-route pose cascade", StringComparison.Ordinal) >= 0)
                        ? "无法应用：该配置会让一条路由读取另一条路由已经覆盖的设备角色，形成级联移动。\n\n" +
                            string.Join(Environment.NewLine, errors)
                        : string.Join(Environment.NewLine, errors),
                    "运行时配置无效",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                _pendingStaticMappingAutoRetrySuppressed = false;
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
                        RuntimeRouteChangeWarningText.Text = "运行时配置已保存。退出 SteamVR 后将自动写入静态引导映射，无需再次点击“应用更改”。";
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

            if (!IsConcreteDeviceTarget(target))
            {
                MessageBox.Show(
                    this,
                    "旧版 SteamVR 角色目标仅用于查看和维护已有规则。请选择一个明确的在线实体设备作为新目标。",
                    "需要明确设备目标",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
            var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#24466F"));
            EditorCardBorder.Background = background;

            var animation = new ColorAnimation
            {
                To = (Color)ColorConverter.ConvertFromString("#1A1A1A"),
                Duration = TimeSpan.FromMilliseconds(750),
                BeginTime = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, __) => EditorCardBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#1A1A1A"));
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
            _sourcePreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#F5A623"), TrackedDeviceKind.Unknown);
            _targetPreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#45D483"), TrackedDeviceKind.Unknown);
            PreviewSceneRoot.Children.Add(_sourcePreviewModel);
            PreviewSceneRoot.Children.Add(_targetPreviewModel);
            HidePreviewModel(_sourcePreviewModel);
            HidePreviewModel(_targetPreviewModel);
            UpdatePreviewCamera();
        }

        private void PosePreviewViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
            {
                return;
            }
            _previewDragButton = e.ChangedButton;
            _previewLastPointer = e.GetPosition(PreviewInteractionSurface);
            PreviewInteractionSurface.CaptureMouse();
            PreviewInteractionSurface.Cursor = e.ChangedButton == MouseButton.Left
                ? Cursors.SizeAll
                : Cursors.ScrollAll;
            e.Handled = true;
        }

        private void PosePreviewViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_previewDragButton == null || !PreviewInteractionSurface.IsMouseCaptured)
            {
                return;
            }

            Point current = e.GetPosition(PreviewInteractionSurface);
            Vector delta = current - _previewLastPointer;
            _previewLastPointer = current;
            if (_previewDragButton == MouseButton.Right)
            {
                _previewCameraYaw -= delta.X * 0.01;
                _previewCameraPitch = Math.Max(
                    -Math.PI * 0.47,
                    Math.Min(Math.PI * 0.47, _previewCameraPitch + (delta.Y * 0.01)));
            }
            else
            {
                Vector3D look = _previewCameraTarget - PosePreviewCamera.Position;
                if (look.LengthSquared > 1e-12)
                {
                    look.Normalize();
                    Vector3D right = Vector3D.CrossProduct(look, PosePreviewCamera.UpDirection);
                    if (right.LengthSquared > 1e-12)
                    {
                        right.Normalize();
                        Vector3D up = Vector3D.CrossProduct(right, look);
                        up.Normalize();
                        double scale = _previewCameraDistance * 0.0017;
                        _previewCameraTarget -= right * (delta.X * scale);
                        _previewCameraTarget += up * (delta.Y * scale);
                    }
                }
            }
            UpdatePreviewCamera();
            e.Handled = true;
        }

        private void PosePreviewViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_previewDragButton != e.ChangedButton)
            {
                return;
            }
            EndPreviewDrag();
            e.Handled = true;
        }

        private void PosePreviewViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _previewCameraDistance *= Math.Pow(0.85, e.Delta / 120.0);
            _previewCameraDistance = Math.Max(0.12, Math.Min(6.0, _previewCameraDistance));
            UpdatePreviewCamera();
            e.Handled = true;
        }

        private void PosePreviewViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _previewDragButton = null;
            PreviewInteractionSurface.Cursor = Cursors.Arrow;
        }

        private void EndPreviewDrag()
        {
            _previewDragButton = null;
            PreviewInteractionSurface.Cursor = Cursors.Arrow;
            if (PreviewInteractionSurface.IsMouseCaptured)
            {
                PreviewInteractionSurface.ReleaseMouseCapture();
            }
        }

        private void UpdatePreviewCamera()
        {
            double horizontalDistance = _previewCameraDistance * Math.Cos(_previewCameraPitch);
            var offset = new Vector3D(
                horizontalDistance * Math.Sin(_previewCameraYaw),
                _previewCameraDistance * Math.Sin(_previewCameraPitch),
                horizontalDistance * Math.Cos(_previewCameraYaw));
            PosePreviewCamera.Position = _previewCameraTarget + offset;
            PosePreviewCamera.LookDirection = _previewCameraTarget - PosePreviewCamera.Position;
            PosePreviewCamera.UpDirection = new Vector3D(0, 1, 0);
        }

        private void ResetPreviewViewButton_Click(object sender, RoutedEventArgs e)
        {
            EndPreviewDrag();
            _previewCameraTarget = new Point3D(0, 0, 0);
            _previewCameraYaw = 0.694;
            _previewCameraPitch = 0.397;
            _previewCameraDistance = 0.68;
            UpdatePreviewCamera();
        }

        private async Task RefreshPreviewDeviceModelsAsync()
        {
            int requestVersion = ++_previewModelRequestVersion;
            if (_selectedRoute == null)
            {
                return;
            }

            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption ??
                _onlinePhysicalDevices.FirstOrDefault(device => string.Equals(
                    device.DevicePath,
                    _selectedRoute.SourceDevicePath,
                    StringComparison.Ordinal));
            string targetPath = (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath ??
                _selectedRoute.TargetDevicePath;
            DeviceOption target = _knownPhysicalDevices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, targetPath, StringComparison.Ordinal)) ??
                _knownPhysicalDevices.FirstOrDefault(device =>
                    string.Equals(device.RoleTargetPath, targetPath, StringComparison.Ordinal));

            Task<OpenVrRenderModel> sourceTask = GetPreviewRenderModelAsync(source?.RenderModelName);
            Task<OpenVrRenderModel> targetTask = GetPreviewRenderModelAsync(target?.RenderModelName);
            OpenVrRenderModel[] models = await Task.WhenAll(sourceTask, targetTask);
            if (models[0] == null && !string.IsNullOrWhiteSpace(source?.RenderModelName))
            {
                _previewModelCache.Remove(source.RenderModelName);
            }
            if (models[1] == null && !string.IsNullOrWhiteSpace(target?.RenderModelName))
            {
                _previewModelCache.Remove(target.RenderModelName);
            }
            if (requestVersion != _previewModelRequestVersion || !IsVisible)
            {
                return;
            }

            TrackedDeviceKind sourceKind = source?.DeviceKind ?? InferDeviceKind(_selectedRoute.SourceDevicePath);
            TrackedDeviceKind targetKind = target?.DeviceKind ?? InferTargetKind(targetPath);
            Color targetColor = (Color)ColorConverter.ConvertFromString("#45D483");
            SetPreviewDeviceModel(
                _sourcePreviewModel,
                models[0],
                sourceKind,
                (Color)ColorConverter.ConvertFromString("#F5A623"));
            SetPreviewDeviceModel(_targetPreviewModel, models[1], targetKind, targetColor);

            string sourceMode = models[0] == null ? "来源模型：内置回退" : "来源模型：SteamVR " + models[0].Name;
            string targetMode = models[1] == null ? "目标模型：内置回退" : "目标模型：SteamVR " + models[1].Name;
            _previewModelDescription = sourceMode + "\n" + targetMode + "\n虚拟输出仅作为路由代理，不参与渲染。";
            PreviewStatusText.ToolTip = _previewModelDescription;
        }

        private Task<OpenVrRenderModel> GetPreviewRenderModelAsync(string renderModelName)
        {
            if (string.IsNullOrWhiteSpace(renderModelName) || !_statusService.IsRunning())
            {
                return Task.FromResult<OpenVrRenderModel>(null);
            }

            if (_previewModelCache.TryGetValue(renderModelName, out Task<OpenVrRenderModel> cached))
            {
                return cached;
            }

            string runtimePath = _pathService.FindRuntimePath();
            Task<OpenVrRenderModel> loadTask = Task.Run(() =>
            {
                try
                {
                    return _openVrRenderModelService.Load(runtimePath, renderModelName);
                }
                catch
                {
                    return null;
                }
            });
            _previewModelCache[renderModelName] = loadTask;
            return loadTask;
        }

        private static TrackedDeviceKind InferDeviceKind(string devicePath)
        {
            string value = devicePath ?? string.Empty;
            if (value.IndexOf("hmd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TrackedDeviceKind.Hmd;
            }
            if (value.IndexOf("controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TrackedDeviceKind.Controller;
            }
            if (value.IndexOf("tracker", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TrackedDeviceKind.Tracker;
            }
            return TrackedDeviceKind.Unknown;
        }

        private static TrackedDeviceKind InferTargetKind(string targetPath)
        {
            if (string.Equals(targetPath, ProtocolConstants.HeadRolePath, StringComparison.Ordinal))
            {
                return TrackedDeviceKind.Hmd;
            }
            if (string.Equals(targetPath, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal) ||
                string.Equals(targetPath, ProtocolConstants.RightHandRolePath, StringComparison.Ordinal))
            {
                return TrackedDeviceKind.Controller;
            }
            return InferDeviceKind(targetPath);
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
                _telemetryFailureCount = 0;
                RenderTelemetry(snapshot);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                _telemetryFailureCount++;
                PreviewStatusText.Text = _telemetryFailureCount < 5
                    ? "遥测重试…"
                    : "遥测暂停";
                PreviewStatusText.Foreground = FindBrush("WarningBrush");
                PreviewStatusText.ToolTip = "保留最后有效画面：" + exception.Message;
            }
            finally
            {
                _telemetryUpdatePending = false;
            }
        }

        private void RenderTelemetry(PoseTelemetrySnapshot snapshot)
        {
            ApplySourceAndTargetPreview(snapshot);

            PreviewStatusText.Text = "实时";
            PreviewStatusText.Foreground = FindBrush("SuccessBrush");
            string healthText = "输出 " + PoseHealth(snapshot.Output) +
                " · 目标 " + PoseHealth(snapshot.Target) + " · 来源局部空间";
            string diagnosticText = healthText;
            if (IsRenderablePose(snapshot.Source) && IsRenderablePose(snapshot.Output) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.Output, out Matrix3D actualOutputMatrix))
            {
                healthText += " · 实际偏移 (" +
                    actualOutputMatrix.OffsetX.ToString("F3", CultureInfo.InvariantCulture) + ", " +
                    actualOutputMatrix.OffsetY.ToString("F3", CultureInfo.InvariantCulture) + ", " +
                    actualOutputMatrix.OffsetZ.ToString("F3", CultureInfo.InvariantCulture) + ") m";
                string targetDistanceText = string.Empty;
                if (IsRenderablePose(snapshot.Target))
                {
                    double dx = snapshot.Output.PositionX - snapshot.Target.PositionX;
                    double dy = snapshot.Output.PositionY - snapshot.Target.PositionY;
                    double dz = snapshot.Output.PositionZ - snapshot.Target.PositionZ;
                    targetDistanceText = " · 输出→目标 " +
                        Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz))
                            .ToString("F4", CultureInfo.InvariantCulture) + " m";
                }
                diagnosticText = healthText + targetDistanceText;
            }
            PreviewStatusText.ToolTip = string.IsNullOrWhiteSpace(_previewModelDescription)
                ? diagnosticText
                : diagnosticText + "\n" + _previewModelDescription;
        }

        private void ApplySourceAndTargetPreview(PoseTelemetrySnapshot snapshot)
        {
            Matrix3D targetMatrix = Matrix3D.Identity;
            bool targetVisible = IsRenderablePose(snapshot.Source) &&
                IsRenderablePose(snapshot.Target) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.Target, out targetMatrix);
            bool sourceVisible = IsRenderablePose(snapshot.Source);
            Matrix3D sourceMatrix = Matrix3D.Identity;
            Rect3D combined = Rect3D.Empty;
            if (sourceVisible)
            {
                AddTransformedBounds(ref combined, _sourcePreviewModel, sourceMatrix);
            }
            if (targetVisible)
            {
                AddTransformedBounds(ref combined, _targetPreviewModel, targetMatrix);
            }

            Vector3D centerOffset = new Vector3D();
            if (!combined.IsEmpty)
            {
                centerOffset = new Vector3D(
                    -(combined.X + (combined.SizeX / 2.0)),
                    -(combined.Y + (combined.SizeY / 2.0)),
                    -(combined.Z + (combined.SizeZ / 2.0)));
            }

            if (sourceVisible)
            {
                sourceMatrix.Translate(centerOffset);
                _sourcePreviewModel.Transform = new MatrixTransform3D(sourceMatrix);
            }
            else
            {
                HidePreviewModel(_sourcePreviewModel);
            }
            if (targetVisible)
            {
                targetMatrix.Translate(centerOffset);
                _targetPreviewModel.Transform = new MatrixTransform3D(targetMatrix);
            }
            else
            {
                HidePreviewModel(_targetPreviewModel);
            }
        }

        private static void AddTransformedBounds(
            ref Rect3D combined,
            Model3DGroup model,
            Matrix3D transform)
        {
            Rect3D localBounds = GetLocalModelBounds(model);
            if (!localBounds.IsEmpty)
            {
                combined.Union(TransformBounds(localBounds, transform));
            }
        }

        private static bool TryGetRelativePoseMatrix(
            PoseTelemetry source,
            PoseTelemetry target,
            out Matrix3D matrix)
        {
            matrix = Matrix3D.Identity;
            var sourceRotation = new Quaternion(
                source.RotationX,
                source.RotationY,
                source.RotationZ,
                source.RotationW);
            var targetRotation = new Quaternion(
                target.RotationX,
                target.RotationY,
                target.RotationZ,
                target.RotationW);
            double sourceLengthSquared =
                (sourceRotation.X * sourceRotation.X) + (sourceRotation.Y * sourceRotation.Y) +
                (sourceRotation.Z * sourceRotation.Z) + (sourceRotation.W * sourceRotation.W);
            double targetLengthSquared =
                (targetRotation.X * targetRotation.X) + (targetRotation.Y * targetRotation.Y) +
                (targetRotation.Z * targetRotation.Z) + (targetRotation.W * targetRotation.W);
            if (sourceLengthSquared < 1e-12 || targetLengthSquared < 1e-12)
            {
                return false;
            }
            sourceRotation.Normalize();
            targetRotation.Normalize();
            Quaternion inverseSource = sourceRotation;
            inverseSource.Conjugate();
            Quaternion relativeRotation = MultiplyQuaternion(inverseSource, targetRotation);
            relativeRotation.Normalize();

            var worldDelta = new Vector3D(
                target.PositionX - source.PositionX,
                target.PositionY - source.PositionY,
                target.PositionZ - source.PositionZ);
            Vector3D relativeTranslation = RotateVector(inverseSource, worldDelta);
            matrix.Rotate(relativeRotation);
            matrix.Translate(relativeTranslation);
            return true;
        }

        private static Quaternion MultiplyQuaternion(Quaternion left, Quaternion right)
        {
            return new Quaternion(
                (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
                (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
                (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W),
                (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z));
        }

        private static Vector3D RotateVector(Quaternion rotation, Vector3D value)
        {
            var vector = new Vector3D(rotation.X, rotation.Y, rotation.Z);
            Vector3D twiceCross = 2.0 * Vector3D.CrossProduct(vector, value);
            return value + (rotation.W * twiceCross) + Vector3D.CrossProduct(vector, twiceCross);
        }

        private static Rect3D GetLocalModelBounds(Model3DGroup model)
        {
            Rect3D bounds = Rect3D.Empty;
            foreach (Model3D child in model.Children)
            {
                bounds.Union(child.Bounds);
            }
            return bounds;
        }

        private static Rect3D TransformBounds(Rect3D bounds, Matrix3D transform)
        {
            Rect3D transformed = Rect3D.Empty;
            double[] xValues = { bounds.X, bounds.X + bounds.SizeX };
            double[] yValues = { bounds.Y, bounds.Y + bounds.SizeY };
            double[] zValues = { bounds.Z, bounds.Z + bounds.SizeZ };
            foreach (double x in xValues)
            {
                foreach (double y in yValues)
                {
                    foreach (double z in zValues)
                    {
                        transformed.Union(transform.Transform(new Point3D(x, y, z)));
                    }
                }
            }
            return transformed;
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

        private static void HidePreviewModel(Model3DGroup model)
        {
            model.Transform = new TranslateTransform3D(10000, 10000, 10000);
        }

        private static Model3DGroup CreateDeviceModel(Color bodyColor, TrackedDeviceKind deviceKind)
        {
            var group = new Model3DGroup();
            AddFallbackDeviceGeometry(group, deviceKind, bodyColor);
            foreach (Model3D axis in CreateAxesModel(0.12, 0.003).Children)
            {
                group.Children.Add(axis);
            }
            return group;
        }

        private static void SetPreviewDeviceModel(
            Model3DGroup group,
            OpenVrRenderModel renderModel,
            TrackedDeviceKind fallbackKind,
            Color bodyColor)
        {
            Transform3D transform = group.Transform;
            group.Children.Clear();
            if (renderModel == null || renderModel.Vertices.Length == 0 || renderModel.Indices.Length == 0)
            {
                AddFallbackDeviceGeometry(group, fallbackKind, bodyColor);
                foreach (Model3D axis in CreateAxesModel(0.12, 0.003).Children)
                {
                    group.Children.Add(axis);
                }
            }
            else
            {
                group.Children.Add(CreateOpenVrModel(renderModel));
            }
            group.Transform = transform;
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
                // Some OpenVR render models deliberately place UVs outside 0..1 to sample
                // an edge colour. WPF does not expose OpenGL's CLAMP_TO_EDGE sampler, so
                // clamp explicitly. The RenderModels API already returns texture data in
                // the orientation expected by its UVs; do not flip the V coordinate here.
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
            var materials = new MaterialGroup();
            if (renderModel.TextureWidth > 0 && renderModel.TextureHeight > 0 && renderModel.TextureRgba.Length > 0)
            {
                byte[] bgra = new byte[renderModel.TextureRgba.Length];
                for (int index = 0; index < bgra.Length; index += 4)
                {
                    bgra[index] = renderModel.TextureRgba[index + 2];
                    bgra[index + 1] = renderModel.TextureRgba[index + 1];
                    bgra[index + 2] = renderModel.TextureRgba[index];
                    bgra[index + 3] = renderModel.TextureRgba[index + 3];
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
                var textureBrush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                textureBrush.Freeze();
                materials.Children.Add(new DiffuseMaterial(textureBrush));
            }
            else
            {
                materials.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.LightGray)));
            }
            return new GeometryModel3D(mesh, materials) { BackMaterial = materials };
        }

        private static double ClampTextureCoordinate(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static void AddFallbackDeviceGeometry(
            Model3DGroup group,
            TrackedDeviceKind deviceKind,
            Color bodyColor)
        {
            switch (deviceKind)
            {
                case TrackedDeviceKind.Hmd:
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0, -0.015), new Vector3D(0.19, 0.09, 0.08), bodyColor));
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0.01, 0.04), new Vector3D(0.12, 0.035, 0.05), bodyColor));
                    break;
                case TrackedDeviceKind.Controller:
                    group.Children.Add(CreateBoxModel(new Point3D(0, -0.055, 0.015), new Vector3D(0.035, 0.13, 0.04), bodyColor));
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0.025, -0.005), new Vector3D(0.075, 0.045, 0.075), bodyColor));
                    break;
                case TrackedDeviceKind.Tracker:
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0, 0), new Vector3D(0.085, 0.03, 0.085), bodyColor));
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0.022, 0), new Vector3D(0.052, 0.014, 0.052), bodyColor));
                    break;
                default:
                    group.Children.Add(CreateBoxModel(new Point3D(0, 0, 0), new Vector3D(0.065, 0.065, 0.065), bodyColor));
                    break;
            }
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

        private void ClearDeviceHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "确定清空设备记录？\n\n在线设备会在下次扫描时重新记录；现有配置仍会保留其引用，并按实际连接状态显示为“在线”或“离线”。",
                "清空设备记录",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                _deviceHistoryService.Clear();
                RefreshAll();
                if (_selectedRoute != null)
                {
                    ShowSelectedRoute(_selectedRoute);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法清空设备记录",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private enum SettingsSection
        {
            Runtime,
            SteamVr,
            Devices,
            Advanced
        }

        private sealed class DeviceHistoryListItem
        {
            public DeviceHistoryListItem(string displayName, string devicePath, bool isOnline)
            {
                DisplayName = displayName;
                DevicePath = devicePath;
                IsOnline = isOnline;
            }

            public string DisplayName { get; }

            public string DevicePath { get; }

            public bool IsOnline { get; }

            public string StateText => IsOnline ? "在线" : "离线";
        }

        private sealed class RuntimeLifecycleOption
        {
            public RuntimeLifecycleOption(RuntimeLifecycleMode mode, string displayName)
            {
                Mode = mode;
                DisplayName = displayName;
            }

            public RuntimeLifecycleMode Mode { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
