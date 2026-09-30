using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json;
using TrackSwap.Localization;
using TrackSwap.Models;
using TrackSwap.Protocol;
using TrackSwap.Services;

namespace TrackSwap
{
    public partial class MainWindow : Window
    {
        private const string ProxyRenderModelName = "{trackswap}trackswap_proxy_tracker";
        private const string GenericHmdRenderModelName = "generic_hmd";
        private const float XInputCaptureActivationThreshold = 0.65f;
        private const ushort XInputDPadUp = 0x0001;
        private const ushort XInputDPadDown = 0x0002;
        private const ushort XInputDPadLeft = 0x0004;
        private const ushort XInputDPadRight = 0x0008;
        private const ushort XInputMenu = 0x0010;
        private const ushort XInputView = 0x0020;
        private const ushort XInputLeftThumb = 0x0040;
        private const ushort XInputRightThumb = 0x0080;
        private const ushort XInputLeftShoulder = 0x0100;
        private const ushort XInputRightShoulder = 0x0200;
        private const ushort XInputA = 0x1000;
        private const ushort XInputB = 0x2000;
        private const ushort XInputX = 0x4000;
        private const ushort XInputY = 0x8000;

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
        private readonly LocalizationService _localizationService = LocalizationManager.Current;
        private readonly RuntimeControlService _runtimeControlService = new RuntimeControlService();
        private readonly ConfigurationBackupService _configurationBackupService = new ConfigurationBackupService();
        private readonly DataMigrationService _dataMigrationService = new DataMigrationService();
        private readonly TrackSwapDataCleanupService _dataCleanupService = new TrackSwapDataCleanupService();
        private readonly SteamIntegrationMaintenanceService _steamIntegrationMaintenanceService =
            new SteamIntegrationMaintenanceService();
        private readonly DiagnosticsService _diagnosticsService;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _deviceRefreshTimer;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly DispatcherTimer _oscMonitorTimer;
        private readonly DispatcherTimer _oscApplyTimer;
        private readonly DispatcherTimer _xInputApplyTimer;
        private readonly DispatcherTimer _xInputHoverPreviewTimer;
        private readonly DispatcherTimer _routeAutoApplyTimer;
        private Model3DGroup _sourcePreviewModel;
        private Model3DGroup _rotationSourcePreviewModel;
        private Model3DGroup _proxyPreviewModel;
        private Model3DGroup _targetPreviewModel;
        private Model3DGroup _gizmoPreviewModel;
        private readonly Dictionary<GeometryModel3D, GizmoAxis> _gizmoHitModels =
            new Dictionary<GeometryModel3D, GizmoAxis>();
        private GizmoMode _gizmoMode = GizmoMode.None;
        private GizmoAxis _gizmoDragAxis = GizmoAxis.None;
        private PoseOffset _gizmoDragStartOffset;
        private Point _gizmoDragStartPointer;
        private Vector _gizmoDragScreenDirection;
        private double _gizmoDragUnitsPerPixel;
        private bool _gizmoPreviewOverrideActive;
        private bool _gizmoVisible;
        private Matrix3D _gizmoWorldTransform = Matrix3D.Identity;
        private Quaternion _splitBasePreviewRotation = Quaternion.Identity;
        private Vector3D _previewCenterOffset;
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
        private DiagnosticsReport _diagnosticsReport;
        private long _loadedRuntimeRevision = -1;
        private bool _runtimeEditorInitialized;
        private bool _deviceRefreshPending;
        private bool _telemetryUpdatePending;
        private bool _oscMonitorUpdatePending;
        private bool _hapticTimelineRenderingAttached;
        private bool _loadingOscFields;
        private bool _oscAutoApplyBusy;
        private bool _oscAutoApplyQueued;
        private bool _xInputMonitorUpdatePending;
        private bool _loadingXInputFields;
        private bool _xInputAutoApplyBusy;
        private bool _xInputAutoApplyQueued;
        private bool _routeAutoApplyBusy;
        private bool _routeAutoApplyQueued;
        private string _routeAutoApplyIssueText;
        private string _routeAutoApplyIssueDetail;
        private int _runtimeStatusFailureCount;
        private int _telemetryFailureCount;
        private string _previewModelDescription;
        private IReadOnlyList<DeviceOption> _onlinePhysicalDevices = Array.Empty<DeviceOption>();
        private IReadOnlyList<DeviceOption> _knownPhysicalDevices = Array.Empty<DeviceOption>();
        private readonly Dictionary<string, string> _knownSourceRoleTargets =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly ObservableCollection<RouteListItem> _routeItems = new ObservableCollection<RouteListItem>();
        private readonly List<RouteConfiguration> _workingRoutes = new List<RouteConfiguration>();
        private readonly Dictionary<ComboBox, Image> _xInputPreviewImages =
            new Dictionary<ComboBox, Image>();
        private readonly Dictionary<string, ImageSource> _xInputIconCache =
            new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        private ComboBox _xInputCaptureComboBox;
        private XInputBindingOption _xInputCapturePreviousOption;
        private XInputBindingOption _xInputCaptureCommandOption;
        private HashSet<XInputBindingSource> _xInputCaptureSuppressedSources;
        private bool _xInputCaptureBaselineReady;
        private DateTime _xInputCaptureDeadlineUtc;
        private bool _xInputAnalogThresholdPreviewConnected;
        private double _xInputAnalogThresholdPreviewValue;
        private bool _xInputAnalogThresholdDragging;
        private double _xInputAnalogThresholdDragOffset;
        private OscConfiguration _workingOsc = OscConfiguration.CreateDefault();
        private XInputConfiguration _workingXInput = XInputConfiguration.CreateDefault();
        private RouteConfiguration _selectedRoute;
        private bool _showingSettings;
        private bool _loadingLanguageSelection;
        private string _languageLocale = LocalizationService.OfficialLocale;
        private SettingsSection _settingsSection = SettingsSection.Runtime;
        private bool _showSteamVrRoleTargets;
        private bool _allowDuplicatePoseSources;
        private bool _physicalSourceHidingEnabled;
        private int _controllerHandSelectionPriority;
        private bool _hideSourceInPreview;
        private bool _hideTargetInPreview;
        private bool _showProxyInPreview;
        private bool _followSteamVrWithTrackSwap;
        private bool _steamVrObservedForUiLifecycle;
        private bool _steamVrUiCloseScheduled;
        private RuntimeLifecycleMode _runtimeLifecycleMode;
        private bool _runtimeLifecycleSelectionReady;
        private bool _runtimeStartPending;
        private bool _runtimeLifecycleRestarting;
        private bool _isClosing;
        private bool _destructiveCleanupInProgress;
        private bool _lastDeviceRefreshSteamVrRunning;
        private long _displayedDriverAppliedRevision = long.MinValue;
        private bool _displayedDriverConnected;

        public MainWindow()
        {
            InitializeComponent();
            _diagnosticsService = new DiagnosticsService(_pathService, _statusService, _runtimeControlService);

            UiPreferences preferences = _uiPreferencesService.Load();
            _languageLocale = _localizationService.CurrentLocale;
            _showSteamVrRoleTargets = preferences.ShowSteamVrRoleTargets;
            _allowDuplicatePoseSources = preferences.AllowDuplicatePoseSources;
            _controllerHandSelectionPriority = preferences.ControllerHandSelectionPriority;
            _hideSourceInPreview = preferences.HideSourceInPreview;
            _hideTargetInPreview = preferences.HideTargetInPreview;
            _showProxyInPreview = preferences.ShowProxyInPreview;
            _runtimeLifecycleMode = preferences.RuntimeLifecycleMode;
            _followSteamVrWithTrackSwap = preferences.FollowSteamVrWithTrackSwap;
            ShowSteamVrRoleTargetsCheckBox.IsChecked = _showSteamVrRoleTargets;
            AllowDuplicatePoseSourcesCheckBox.IsChecked = _allowDuplicatePoseSources;
            ControllerHandSelectionPriorityTextBox.Text =
                _controllerHandSelectionPriority.ToString(CultureInfo.InvariantCulture);
            HideSourceInPreviewCheckBox.IsChecked = _hideSourceInPreview;
            HideTargetInPreviewCheckBox.IsChecked = _hideTargetInPreview;
            ShowProxyInPreviewCheckBox.IsChecked = _showProxyInPreview;
            FollowSteamVrWithTrackSwapCheckBox.IsChecked = _followSteamVrWithTrackSwap;
            RefreshLanguageSettingsView();
            InitializeLocalizedFixedOptions();
            _xInputHoverPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _xInputHoverPreviewTimer.Tick += (_, __) => UpdateHoveredXInputPreview();
            InitializeXInputMappingOptions();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            _oscApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _oscApplyTimer.Tick += async (_, __) =>
            {
                _oscApplyTimer.Stop();
                await ApplyOscSettingsAsync();
            };
            _xInputApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _xInputApplyTimer.Tick += async (_, __) =>
            {
                _xInputApplyTimer.Stop();
                await ApplyXInputSettingsAsync();
            };
            _routeAutoApplyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _routeAutoApplyTimer.Tick += async (_, __) =>
            {
                _routeAutoApplyTimer.Stop();
                await ApplyRouteEditorAutomaticallyAsync();
            };
            OffsetTranslationXTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetTranslationYTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetTranslationZTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetRotationXTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetRotationYTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetRotationZTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            OffsetRotationWTextBox.TextChanged += RuntimeOffsetTextBox_TextChanged;
            foreach (TextBox field in GetOffsetTextBoxes())
            {
                field.PreviewTextInput += RuntimeOffsetTextBox_PreviewTextInput;
                DataObject.AddPastingHandler(field, RuntimeOffsetTextBox_Pasting);
            }
            LoadOscFields(_workingOsc);
            LoadXInputFields(_workingXInput);
            OscListenAddressTextBox.TextChanged += (_, __) => ScheduleOscSettingsApply(immediate: false);
            OscPortTextBox.TextChanged += (_, __) => ScheduleOscSettingsApply(immediate: false);
            OscSendPortTextBox.TextChanged += (_, __) => ScheduleOscSettingsApply(immediate: false);
            InitializeLocalizedLifecycleOptions();
            _runtimeLifecycleSelectionReady = true;

            _localizationService.LanguageChanged += LocalizationService_LanguageChanged;

            RouteListBox.ItemsSource = _routeItems;

            TargetComboBox.ItemsSource = BuildTargets(
                Array.Empty<DeviceOption>(),
                Array.Empty<TargetOption>(),
                includeRoleTargets: false,
                includeConcreteDevices: true);
            RuntimeTargetComboBox.ItemsSource = BuildTargets(
                Array.Empty<DeviceOption>(),
                Array.Empty<TargetOption>(),
                includeRoleTargets: _showSteamVrRoleTargets,
                includeConcreteDevices: true);
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
            _telemetryTimer.Tick += async (_, __) =>
            {
                if (OpenVrInterop.DetachIfSteamVrIsQuitting())
                {
                    return;
                }
                await RefreshTelemetryAsync();
            };
            _oscMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _oscMonitorTimer.Tick += async (_, __) =>
            {
                if (_settingsSection == SettingsSection.XInput)
                {
                    await RefreshXInputMonitorAsync();
                }
                else
                {
                    await RefreshOscMonitorAsync();
                }
            };

            Loaded += async (_, __) =>
            {
                RefreshAll();
                UpdateHapticTimelineRendering();
                await EnsureRuntimeStartedAsync(showError: false);
                _statusTimer.Start();
                _deviceRefreshTimer.Start();
                _telemetryTimer.Start();
                _oscMonitorTimer.Start();
                await RefreshStatusAsync();
                if (_statusService.IsRunning())
                {
                    await SyncSteamVrAutoLaunchAsync(
                        enabled: _followSteamVrWithTrackSwap,
                        showWarning: false);
                }
            };
            Closing += MainWindow_Closing;
            Closed += (_, __) =>
            {
                _localizationService.LanguageChanged -= LocalizationService_LanguageChanged;
                _isClosing = true;
                _previewModelRequestVersion++;
                _statusTimer.Stop();
                _deviceRefreshTimer.Stop();
                _telemetryTimer.Stop();
                _oscMonitorTimer.Stop();
                _oscApplyTimer.Stop();
                _xInputApplyTimer.Stop();
                _xInputHoverPreviewTimer.Stop();
                _routeAutoApplyTimer.Stop();
                DetachHapticTimelineRendering();
                OpenVrInterop.Reset();
            };
        }

        private void InitializeLocalizedFixedOptions()
        {
            RuntimeModeComboBox.ItemsSource = new[]
            {
                new RouteModeOption(RouteMode.DirectProxy, Tr.Get("app.initialize_localized_fixed_options.output_virtual_tracker")),
                new RouteModeOption(RouteMode.VirtualController, Tr.Get("app.initialize_localized_fixed_options.output_virtual_controller")),
                new RouteModeOption(RouteMode.VirtualHmd, Tr.Get("app.initialize_localized_fixed_options.output_virtual_hmd")),
                new RouteModeOption(RouteMode.ReplaceTarget, Tr.Get("app.initialize_localized_fixed_options.replace_device_pose"))
            };
            RuntimeControllerHandComboBox.ItemsSource = new[]
            {
                new ControllerHandOption(ControllerHand.Left, Tr.Get("common.hand.left")),
                new ControllerHandOption(ControllerHand.Right, Tr.Get("common.hand.right"))
            };
            RuntimeControlInputComboBox.ItemsSource = new[]
            {
                new ControlInputOption(ControlInputSource.None, Tr.Get("common.value.none")),
                new ControlInputOption(ControlInputSource.Osc, "OSC"),
                new ControlInputOption(ControlInputSource.XInput, "XInput")
            };
        }

        private void InitializeLocalizedLifecycleOptions()
        {
            RuntimeLifecycleComboBox.ItemsSource = new[]
            {
                new RuntimeLifecycleOption(RuntimeLifecycleMode.FollowTrackSwap, Tr.Get("settings.advanced.initialize_localized_lifecycle_options.trackswap")),
                new RuntimeLifecycleOption(RuntimeLifecycleMode.FollowSteamVr, Tr.Get("settings.advanced.initialize_localized_lifecycle_options.steamvr"))
            };
            RuntimeLifecycleComboBox.SelectedItem =
                ((IEnumerable<RuntimeLifecycleOption>)RuntimeLifecycleComboBox.ItemsSource)
                    .First(option => option.Mode == _runtimeLifecycleMode);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing)
                {
                    return;
                }
                bool previousLoading = _isLoading;
                _isLoading = true;
                _runtimeLifecycleSelectionReady = false;
                try
                {
                    InitializeLocalizedFixedOptions();
                    InitializeLocalizedLifecycleOptions();
                    InitializeXInputMappingOptions(registerPreviews: false);
                    LoadXInputFields(_workingXInput);
                    LoadOscFields(_workingOsc);
                    RefreshAll();
                    RefreshLanguageSettingsView();
                }
                finally
                {
                    _runtimeLifecycleSelectionReady = true;
                    _isLoading = previousLoading;
                }
            }), DispatcherPriority.DataBind);
        }

        private async Task RefreshOscMonitorAsync()
        {
            if (_oscMonitorUpdatePending || !_showingSettings || _settingsSection != SettingsSection.Osc)
            {
                return;
            }

            _oscMonitorUpdatePending = true;
            try
            {
                OscRuntimeStatus status = await _runtimeControlService.GetOscStatusAsync();
                UpdateOscIndicators(status);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                UpdateOscIndicators(null);
            }
            finally
            {
                _oscMonitorUpdatePending = false;
            }
        }

        private void UpdateOscIndicators(OscRuntimeStatus status)
        {
            ControllerInputState left = status?.LeftInput;
            ControllerInputState right = status?.RightInput;
            left = left ?? new ControllerInputState();
            right = right ?? new ControllerInputState();

            OscLeftPrimaryIndicator.Value = left.PrimaryButton ? 1.0 : 0.0;
            OscLeftSecondaryIndicator.Value = left.SecondaryButton ? 1.0 : 0.0;
            OscLeftJoystickIndicator.X = left.JoystickX;
            OscLeftJoystickIndicator.Y = left.JoystickY;
            OscLeftJoystickClickIndicator.Value = left.JoystickClick ? 1.0 : 0.0;
            OscLeftTriggerValueIndicator.Value = left.TriggerValue;
            OscLeftGripValueIndicator.Value = left.GripValue;
            OscLeftMenuIndicator.Value = left.MenuButton ? 1.0 : 0.0;
            OscLeftThumbTouchIndicator.Value = status?.LeftThumbTouchAssist == true ? 1.0 : 0.0;
            OscLeftIndexTouchIndicator.Value = status?.LeftIndexTouchAssist == true ? 1.0 : 0.0;

            OscRightPrimaryIndicator.Value = right.PrimaryButton ? 1.0 : 0.0;
            OscRightSecondaryIndicator.Value = right.SecondaryButton ? 1.0 : 0.0;
            OscRightJoystickIndicator.X = right.JoystickX;
            OscRightJoystickIndicator.Y = right.JoystickY;
            OscRightJoystickClickIndicator.Value = right.JoystickClick ? 1.0 : 0.0;
            OscRightTriggerValueIndicator.Value = right.TriggerValue;
            OscRightGripValueIndicator.Value = right.GripValue;
            OscRightMenuIndicator.Value = right.MenuButton ? 1.0 : 0.0;
            OscRightThumbTouchIndicator.Value = status?.RightThumbTouchAssist == true ? 1.0 : 0.0;
            OscRightIndexTouchIndicator.Value = status?.RightIndexTouchAssist == true ? 1.0 : 0.0;
            OscLeftHapticIndicator.SetSamples(status?.LeftHapticHistory);
            OscRightHapticIndicator.SetSamples(status?.RightHapticHistory);
        }

        private async void TestLeftOscHapticButton_Click(object sender, RoutedEventArgs e)
        {
            await TestOscHapticAsync(ControllerHand.Left);
        }

        private async void TestRightOscHapticButton_Click(object sender, RoutedEventArgs e)
        {
            await TestOscHapticAsync(ControllerHand.Right);
        }

        private async Task TestOscHapticAsync(ControllerHand hand)
        {
            try
            {
                await _runtimeControlService.TestOscHapticAsync(hand);
                await RefreshOscMonitorAsync();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("settings.osc.test_osc_haptic_async.cannot_haptic"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task RefreshXInputMonitorAsync()
        {
            if (_xInputMonitorUpdatePending || !_showingSettings || _settingsSection != SettingsSection.XInput)
            {
                return;
            }

            _xInputMonitorUpdatePending = true;
            try
            {
                XInputRuntimeStatus status = await _runtimeControlService.GetXInputStatusAsync();
                UpdateXInputIndicators(status.LeftInput, status.RightInput);
                UpdateXInputTouchAssistIndicators(status.PhysicalInput);
                UpdateXInputAnalogThresholdPreview(status.PhysicalInput);
                UpdateXInputCapture(status.PhysicalInput);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                UpdateXInputIndicators(null, null);
                UpdateXInputTouchAssistIndicators(null);
                UpdateXInputAnalogThresholdPreview(null);
                CheckXInputCaptureTimeout();
            }
            finally
            {
                _xInputMonitorUpdatePending = false;
            }
        }

        private void UpdateXInputIndicators(ControllerInputState left, ControllerInputState right)
        {
            left = left ?? new ControllerInputState();
            right = right ?? new ControllerInputState();

            XInputLeftPrimaryIndicator.Value = left.PrimaryButton ? 1.0 : 0.0;
            XInputLeftSecondaryIndicator.Value = left.SecondaryButton ? 1.0 : 0.0;
            UpdateXInputJoystickTransform(XInputLeftJoystickTransform, left.JoystickX, left.JoystickY);
            XInputLeftJoystickClickIndicator.Value = left.JoystickClick ? 1.0 : 0.0;
            XInputLeftTriggerIndicator.Value = left.TriggerValue;
            XInputLeftGripIndicator.Value = left.GripValue;
            XInputLeftMenuIndicator.Value = left.MenuButton ? 1.0 : 0.0;

            XInputRightPrimaryIndicator.Value = right.PrimaryButton ? 1.0 : 0.0;
            XInputRightSecondaryIndicator.Value = right.SecondaryButton ? 1.0 : 0.0;
            UpdateXInputJoystickTransform(XInputRightJoystickTransform, right.JoystickX, right.JoystickY);
            XInputRightJoystickClickIndicator.Value = right.JoystickClick ? 1.0 : 0.0;
            XInputRightTriggerIndicator.Value = right.TriggerValue;
            XInputRightGripIndicator.Value = right.GripValue;
            XInputRightMenuIndicator.Value = right.MenuButton ? 1.0 : 0.0;
        }

        private void UpdateXInputTouchAssistIndicators(XInputPhysicalState physicalInput)
        {
            XInputLeftThumbTouchIndicator.Value = IsXInputBindingActive(
                physicalInput,
                GetXInputSource(XInputLeftThumbTouchComboBox)) ? 1.0 : 0.0;
            XInputLeftIndexTouchIndicator.Value = IsXInputBindingActive(
                physicalInput,
                GetXInputSource(XInputLeftIndexTouchComboBox)) ? 1.0 : 0.0;
            XInputRightThumbTouchIndicator.Value = IsXInputBindingActive(
                physicalInput,
                GetXInputSource(XInputRightThumbTouchComboBox)) ? 1.0 : 0.0;
            XInputRightIndexTouchIndicator.Value = IsXInputBindingActive(
                physicalInput,
                GetXInputSource(XInputRightIndexTouchComboBox)) ? 1.0 : 0.0;
        }

        private bool IsXInputBindingActive(XInputPhysicalState state, XInputBindingSource source)
        {
            if (state == null || !state.Connected || source == XInputBindingSource.None)
            {
                return false;
            }
            double threshold = XInputAnalogThresholdSlider.Value / 100.0;
            if (source == XInputBindingSource.LeftTrigger)
            {
                return state.LeftTrigger >= threshold;
            }
            if (source == XInputBindingSource.RightTrigger)
            {
                return state.RightTrigger >= threshold;
            }
            return GetActiveXInputSources(state, joystickOnly: false).Contains(source);
        }

        private static void UpdateXInputJoystickTransform(
            TranslateTransform transform,
            double x,
            double y)
        {
            const double maximumOffset = 23.0;
            double normalizedX = double.IsNaN(x) || double.IsInfinity(x)
                ? 0.0
                : Math.Max(-1.0, Math.Min(1.0, x));
            double normalizedY = double.IsNaN(y) || double.IsInfinity(y)
                ? 0.0
                : Math.Max(-1.0, Math.Min(1.0, y));
            transform.X = normalizedX * maximumOffset;
            transform.Y = -normalizedY * maximumOffset;
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
                string previousRuntimeRotationSourcePath =
                    (RuntimeRotationSourceComboBox.SelectedItem as DeviceOption)?.DevicePath;
                string previousRuntimeTargetPath = (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                _settingsPath = _pathService.FindSettingsPath();
                SettingsPathText.Text = _settingsPath ?? Tr.Get("app.refresh_all.not_found_steamvr_vrsettings");
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
                DeviceHistoryCountText.Text = historyItems.Count + Tr.Get("app.refresh_all.device");
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
                    previousRuntimeRotationSourcePath,
                    previousRuntimeTargetPath,
                    savedDeviceTargets);
                RefreshOverrideList();

                if (!string.IsNullOrWhiteSpace(enumerationWarning))
                {
                    RefreshButton.ToolTip = Tr.Get("app.refresh_all.online_device_read_failed_config") + enumerationWarning;
                }
                else
                {
                    RefreshButton.ToolTip = Tr.Get("app.refresh_all.scan_device_read_config");
                }
                _lastDeviceRefreshSteamVrRunning = steamVrRunning;
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("app.refresh_all.load_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (_deviceRefreshPending || _isLoading ||
                SourceComboBox.IsDropDownOpen || TargetComboBox.IsDropDownOpen ||
                RuntimeSourceComboBox.IsDropDownOpen ||
                RuntimeRotationSourceComboBox.IsDropDownOpen ||
                RuntimeTargetComboBox.IsDropDownOpen)
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
                            Tr.Get("app.refresh_devices_async.device_scan_failed_preserve_up") + exception.Message;
                        return;
                    }
                }
                else if (_lastDeviceRefreshSteamVrRunning)
                {
                    OpenVrInterop.Reset();
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
            string selectedRotationSourcePath,
            string selectedTargetPath,
            IReadOnlyList<TargetOption> savedDeviceTargets)
        {
            bool includeManual = _selectedRoute != null;
            PoseSourceKind selectedSourceKind = _selectedRoute?.PoseSourceKind ?? PoseSourceKind.Device;
            IReadOnlyList<DeviceOption> sources = BuildDeviceChoices(
                _knownPhysicalDevices,
                selectedSourcePath,
                selectedSourceKind,
                includeManual);
            RuntimeSourceComboBox.ItemsSource = sources;
            RuntimeSourceComboBox.SelectedItem = sources.FirstOrDefault(device =>
                device.PoseSourceKind == selectedSourceKind &&
                (selectedSourceKind == PoseSourceKind.Manual ||
                 string.Equals(device.DevicePath, selectedSourcePath, StringComparison.Ordinal)));

            IReadOnlyList<DeviceOption> rotationSources = BuildDeviceChoices(
                _knownPhysicalDevices,
                selectedRotationSourcePath);
            RuntimeRotationSourceComboBox.ItemsSource = rotationSources;
            RuntimeRotationSourceComboBox.SelectedItem = rotationSources.FirstOrDefault(device =>
                string.Equals(device.DevicePath, selectedRotationSourcePath, StringComparison.Ordinal));

            IReadOnlyList<TargetOption> targets = BuildTargets(
                _knownPhysicalDevices,
                savedDeviceTargets,
                includeRoleTargets: _showSteamVrRoleTargets,
                includeConcreteDevices: true,
                currentTargetPath: selectedTargetPath);
            RuntimeTargetComboBox.ItemsSource = targets;
            RuntimeTargetComboBox.SelectedItem = targets.FirstOrDefault(target =>
                string.Equals(target.TargetPath, selectedTargetPath, StringComparison.Ordinal));
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
                targets.Add(new TargetOption(Tr.Get("route.build_targets.steamvr_role_right"), "/user/hand/right"));
                targets.Add(new TargetOption(Tr.Get("route.build_targets.steamvr_role_left"), "/user/hand/left"));
                targets.Add(new TargetOption(Tr.Get("route.build_targets.steamvr_role_hmd"), "/user/head"));
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
                    : currentDevice?.IsOnline == true ? Tr.Get("device.status.online_prefix") : Tr.Get("device.status.offline_prefix");
                targets.Add(new TargetOption(
                    currentPrefix + currentName,
                    currentTargetPath,
                    currentTargetPath.StartsWith("/user/", StringComparison.Ordinal)
                        ? (bool?)null
                        : currentDevice?.IsOnline == true));
            }

            foreach (DeviceOption device in knownDevices.Where(device => device.IsOnline))
            {
                if (knownPaths.Add(device.DevicePath))
                {
                    targets.Add(new TargetOption(
                        Tr.Get("device.status.online_prefix") + DeviceOption.BaseDisplayName(device.DisplayName),
                        device.DevicePath,
                        true));
                }
            }

            foreach (DeviceOption device in knownDevices.Where(device => !device.IsOnline))
            {
                if (knownPaths.Add(device.DevicePath))
                {
                    targets.Add(new TargetOption(
                        Tr.Get("device.status.offline_prefix") + DeviceOption.BaseDisplayName(device.DisplayName),
                        device.DevicePath,
                        false));
                }
            }

            foreach (TargetOption target in savedDeviceTargets.Where(target => knownPaths.Add(target.TargetPath)))
            {
                targets.Add(new TargetOption(
                    target.TargetPath.StartsWith("/devices/", StringComparison.Ordinal)
                        ? Tr.Get("device.status.offline_prefix") + DeviceOption.BaseDisplayName(target.DisplayName)
                        : target.DisplayName,
                    target.TargetPath,
                    target.TargetPath.StartsWith("/devices/", StringComparison.Ordinal)
                        ? (bool?)false
                        : null));
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
            string currentDevicePath,
            PoseSourceKind currentSourceKind = PoseSourceKind.Device,
            bool includeManual = false)
        {
            var result = new List<DeviceOption>();
            if (includeManual)
            {
                result.Add(new DeviceOption(
                    Tr.Get("route.source.manual_pose"),
                    string.Empty,
                    isOnline: true,
                    deviceKind: TrackedDeviceKind.Unknown,
                    poseSourceKind: PoseSourceKind.Manual));
            }
            DeviceOption current = knownDevices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal));
            if (currentSourceKind == PoseSourceKind.Device && !string.IsNullOrWhiteSpace(currentDevicePath))
            {
                result.Add(CloneDeviceOption(
                    current,
                    (current?.IsOnline == true ? Tr.Get("device.status.online_prefix") : Tr.Get("device.status.offline_prefix")) + (current != null
                        ? DeviceOption.BaseDisplayName(current.DisplayName)
                        : DeviceNameFromPath(currentDevicePath)),
                    currentDevicePath));
            }

            foreach (DeviceOption device in knownDevices.Where(device =>
                !string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal) && device.IsOnline))
            {
                result.Add(CloneDeviceOption(
                    device,
                    Tr.Get("device.status.online_prefix") + DeviceOption.BaseDisplayName(device.DisplayName),
                    device.DevicePath));
            }
            foreach (DeviceOption device in knownDevices.Where(device =>
                !string.Equals(device.DevicePath, currentDevicePath, StringComparison.Ordinal) && !device.IsOnline))
            {
                result.Add(CloneDeviceOption(
                    device,
                    Tr.Get("device.status.offline_prefix") + DeviceOption.BaseDisplayName(device.DisplayName),
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
                device?.DeviceKind ?? TrackedDeviceKind.Unknown,
                PoseSourceKind.Device);
        }

        private static bool IsPhysicalDevice(DeviceOption device)
        {
            return device != null &&
                !string.IsNullOrWhiteSpace(device.DevicePath) &&
                device.DevicePath.StartsWith("/devices/", StringComparison.Ordinal) &&
                !ProtocolConstants.IsTrackSwapVirtualDevicePath(device.DevicePath);
        }

        private static string DeviceNameFromPath(string devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return Tr.Get("app.device_name_from_path.device");
            }
            int separator = devicePath.LastIndexOf('/');
            return separator >= 0 && separator + 1 < devicePath.Length
                ? devicePath.Substring(separator + 1)
                : devicePath;
        }

        private static string DescribePoseSource(PoseSourceKind sourceKind, string devicePath)
        {
            return sourceKind == PoseSourceKind.Manual
                ? Tr.Get("route.source.manual_pose")
                : string.IsNullOrWhiteSpace(devicePath) ? Tr.Get("route.preview.describe_pose_source.select") : devicePath;
        }

        private void UpdateSteamVrStatus()
        {
            bool running = _statusService.IsRunning();
            if (!running)
            {
                OpenVrInterop.NotifySteamVrStopped();
            }
            if (running && _followSteamVrWithTrackSwap)
            {
                _steamVrObservedForUiLifecycle = true;
                _steamVrUiCloseScheduled = false;
            }
            else if (!running &&
                _followSteamVrWithTrackSwap &&
                _steamVrObservedForUiLifecycle &&
                !_steamVrUiCloseScheduled &&
                !_destructiveCleanupInProgress &&
                !_isClosing)
            {
                _steamVrUiCloseScheduled = true;
            }
            SteamVrStatusText.Text = "SteamVR";
            SteamVrStatusText.Foreground = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrDot.Fill = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            SteamVrBadge.Background = Brushes.Transparent;
            SteamVrBadge.ToolTip = running ? Tr.Get("steamvr.status.running") : Tr.Get("steamvr.status.not_running");

            ApplyButton.IsEnabled = !running
                && SourceComboBox.SelectedItem is DeviceOption
                && IsConcreteDeviceTarget(TargetComboBox.SelectedItem as TargetOption)
                && !string.IsNullOrWhiteSpace(_settingsPath);
        }

        private async Task RefreshStatusAsync()
        {
            if (_destructiveCleanupInProgress)
            {
                return;
            }

            UpdateSteamVrStatus();
            if (_isRuntimeStatusUpdatePending)
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
                stoppedStateWorkCompleted = !_statusService.IsRunning() && !status.StaticMappingPending;
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
                if (_routeAutoApplyQueued && !_routeAutoApplyBusy && _runtimeStatus != null)
                {
                    _routeAutoApplyQueued = false;
                    ScheduleRouteAutoApply(immediate: true);
                }
                if (_steamVrUiCloseScheduled &&
                    stoppedStateWorkCompleted &&
                    !_destructiveCleanupInProgress &&
                    !_isClosing)
                {
                    Close();
                }
            }
        }

        private void ShowRuntimeOnline(RuntimeStatusSnapshot status)
        {
            RuntimeLoadingStateText.Text = Tr.Get("app.show_runtime_online.in_progress_read_runtime_save_config");
            RuntimeStatusText.Text = "Runtime";
            RuntimeStatusText.Foreground = FindBrush("SuccessBrush");
            RuntimeDot.Fill = FindBrush("SuccessBrush");
            RuntimeBadge.Background = Brushes.Transparent;
            RuntimeBadge.ToolTip = Tr.Get("app.show_runtime_online.runtime_connect");
            UpdateInputStatusIndicators(status);
            RuntimeHealthText.Text = Tr.Get("common.status.online");
            RuntimeHealthText.Foreground = FindBrush("SuccessBrush");
            DriverHealthText.Text = status.DriverConnected ? Tr.Get("app.show_runtime_online.connect") : Tr.Get("runtime.status.waiting_for_driver");
            DriverHealthText.Foreground = FindBrush(status.DriverConnected ? "SuccessBrush" : "WarningBrush");
            RuntimeRevisionText.Text = status.ConfigurationRevision.ToString(CultureInfo.InvariantCulture);
            bool applied = status.DriverConnected &&
                status.ConfigurationRevision > 0 &&
                status.DriverAppliedRevision == status.ConfigurationRevision;
            RuntimeAppliedStateText.Text = applied
                ? Tr.Get("route.status.applied")
                : Tr.Get("app.show_runtime_online.apply_driver") + status.DriverAppliedRevision.ToString(CultureInfo.InvariantCulture);
            RuntimeAppliedStateText.Foreground = FindBrush(applied ? "SuccessBrush" : "WarningBrush");
            string runtimeError = string.Join(
                Environment.NewLine,
                new[] { status.LastError, status.StaticMappingLastError }
                    .Where(message => !string.IsNullOrWhiteSpace(message)));
            RuntimeErrorText.Text = runtimeError;
            RuntimeErrorText.Visibility = string.IsNullOrWhiteSpace(runtimeError)
                ? Visibility.Collapsed
                : Visibility.Visible;
            StartRuntimeButton.IsEnabled = false;
            if (status.Configuration != null &&
                (!_runtimeEditorInitialized || status.Configuration.Revision != _loadedRuntimeRevision))
            {
                LoadRuntimeConfiguration(status.Configuration);
                _loadedRuntimeRevision = status.Configuration.Revision;
                _runtimeEditorInitialized = true;
                UpdateContentVisibility();
            }
            else if (_displayedDriverAppliedRevision != status.DriverAppliedRevision ||
                _displayedDriverConnected != status.DriverConnected)
            {
                RefreshRouteList(_selectedRoute?.RouteId);
            }
            _displayedDriverAppliedRevision = status.DriverAppliedRevision;
            _displayedDriverConnected = status.DriverConnected;
            UpdateDiagnosticConfigurationState(status);
        }

        private void ShowRuntimeOffline(string error)
        {
            RuntimeLoadingStateText.Text = Tr.Get("app.show_runtime_offline.cannot_connect_runtime_in_progress_config_save");
            RuntimeStatusText.Text = "Runtime";
            RuntimeStatusText.Foreground = FindBrush("MutedTextBrush");
            RuntimeDot.Fill = FindBrush("MutedTextBrush");
            RuntimeBadge.Background = Brushes.Transparent;
            RuntimeBadge.ToolTip = Tr.Get("app.show_runtime_offline.runtime_connect");
            SetStatusIndicator(OscStatusDot, OscStatusText, OscStatusBadge, "MutedTextBrush", Tr.Get("app.show_runtime_offline.runtime_connect_cannot_read_osc_status"));
            SetStatusIndicator(XInputStatusDot, XInputStatusText, XInputStatusBadge, "MutedTextBrush", Tr.Get("app.show_runtime_offline.runtime_connect_cannot_read_xinput_status"));
            SetStatusIndicator(
                PhysicalSourceHidingStatusDot,
                PhysicalSourceHidingStatusText,
                PhysicalSourceHidingStatusBadge,
                _physicalSourceHidingEnabled && _workingRoutes.Any(route => route.Enabled && route.HidePhysicalSource)
                    ? "WarningBrush"
                    : "MutedTextBrush",
                _physicalSourceHidingEnabled && _workingRoutes.Any(route => route.Enabled && route.HidePhysicalSource)
                    ? Tr.Get("app.show_runtime_offline.device_hide_waiting_runtime_driver")
                    : Tr.Get("settings.advanced.hiding.disabled"));
            RuntimeHealthText.Text = Tr.Get("common.status.offline");
            RuntimeHealthText.Foreground = FindBrush("MutedTextBrush");
            DriverHealthText.Text = Tr.Get("common.status.unknown");
            DriverHealthText.Foreground = FindBrush("MutedTextBrush");
            RuntimeRevisionText.Text = "—";
            RuntimeAppliedStateText.Text = Tr.Get("app.show_runtime_offline.available");
            RuntimeAppliedStateText.Foreground = FindBrush("MutedTextBrush");
            RuntimeErrorText.Text = Tr.Get("app.show_runtime_offline.cannot_connect_trackswap_runtime") + error;
            RuntimeErrorText.Visibility = Visibility.Visible;
            StartRuntimeButton.IsEnabled = true;
            UpdateDiagnosticConfigurationState(null);
        }

        private void UpdateInputStatusIndicators(RuntimeStatusSnapshot status)
        {
            UpdateOscStatusIndicator(status?.Osc);
            UpdateXInputStatusIndicator(status?.XInput);
            UpdatePhysicalSourceHidingStatusIndicator(status?.PhysicalSourceHiding);
        }

        private void UpdateOscStatusIndicator(OscRuntimeStatus status)
        {
            if (status == null || !status.Enabled)
            {
                SetStatusIndicator(OscStatusDot, OscStatusText, OscStatusBadge, "MutedTextBrush", Tr.Get("settings.osc.status.inactive"));
                return;
            }

            string endpoint = string.IsNullOrWhiteSpace(status.Endpoint) ? Tr.Get("settings.osc.update_osc_status_indicator.current") : status.Endpoint;
            bool hasReceiveError = !string.IsNullOrWhiteSpace(status.LastError);
            bool hasSendError = !string.IsNullOrWhiteSpace(status.LastSendError);
            if (hasReceiveError || hasSendError)
            {
                var issues = new List<string>();
                if (hasReceiveError)
                {
                    issues.Add(status.ReceivePortInUse
                        ? Tr.Get("settings.osc.update_osc_status_indicator.osc_receive_port") + endpoint + Tr.Get("settings.osc.update_osc_status_indicator.close_receive_port")
                        : Tr.Get("settings.osc.receive.failed_prefix") + endpoint + "：" + status.LastError);
                }
                if (hasSendError)
                {
                    string sendEndpoint = string.IsNullOrWhiteSpace(status.SendEndpoint)
                        ? Tr.Get("settings.osc.update_osc_status_indicator.current_target")
                        : status.SendEndpoint;
                    issues.Add(Tr.Get("settings.osc.update_osc_status_indicator.osc_cannot") + sendEndpoint + Tr.Get("settings.osc.update_osc_status_indicator.haptic_feedback") + status.LastSendError);
                }
                SetStatusIndicator(
                    OscStatusDot,
                    OscStatusText,
                    OscStatusBadge,
                    "DestructiveBrush",
                    string.Join(Environment.NewLine, issues));
                return;
            }
            if (!status.Listening)
            {
                SetStatusIndicator(OscStatusDot, OscStatusText, OscStatusBadge, "WarningBrush", Tr.Get("settings.osc.receive.preparing_prefix") + endpoint);
                return;
            }
            bool hasRecentSignal = status.LastMessageAtUtc.HasValue;
            if (hasRecentSignal)
            {
                hasRecentSignal = DateTimeOffset.UtcNow - status.LastMessageAtUtc.Value <=
                    TimeSpan.FromSeconds(5);
            }

            SetStatusIndicator(
                OscStatusDot,
                OscStatusText,
                OscStatusBadge,
                hasRecentSignal ? "SuccessBrush" : "WarningBrush",
                hasRecentSignal
                    ? Tr.Get("settings.osc.update_osc_status_indicator.osc_in_progress") + endpoint + Tr.Get("settings.osc.signal_suffix")
                    : Tr.Get("settings.osc.receive.listening_prefix") + endpoint + Tr.Get("settings.osc.update_osc_status_indicator.waiting"));
        }

        private void UpdateXInputStatusIndicator(XInputRuntimeStatus status)
        {
            if (status == null || !status.Enabled)
            {
                SetStatusIndicator(XInputStatusDot, XInputStatusText, XInputStatusBadge, "MutedTextBrush", Tr.Get("settings.xinput.status.inactive"));
                return;
            }

            SetStatusIndicator(
                XInputStatusDot,
                XInputStatusText,
                XInputStatusBadge,
                status.Connected ? "SuccessBrush" : "WarningBrush",
                status.Connected ? Tr.Get("settings.xinput.update_xinput_status_indicator.xinput_controller_connect") : Tr.Get("settings.xinput.update_xinput_status_indicator.waiting_xinput_controller"));
        }

        private void UpdatePhysicalSourceHidingStatusIndicator(PhysicalSourceHidingStatus status)
        {
            if (status == null || status.State == PhysicalSourceHidingState.Disabled)
            {
                SetStatusIndicator(PhysicalSourceHidingStatusDot, PhysicalSourceHidingStatusText,
                    PhysicalSourceHidingStatusBadge, "MutedTextBrush", Tr.Get("settings.advanced.update_physical_source_hiding_status_indicator.config_hide_pose_source_device"));
                return;
            }
            string count = status.ActiveDeviceCount.ToString(CultureInfo.InvariantCulture) + " / " +
                status.RequestedDeviceCount.ToString(CultureInfo.InvariantCulture);
            if (status.State == PhysicalSourceHidingState.Failed)
            {
                SetStatusIndicator(PhysicalSourceHidingStatusDot, PhysicalSourceHidingStatusText,
                    PhysicalSourceHidingStatusBadge, "DestructiveBrush",
                    Tr.Get("settings.advanced.physical_source_hiding.failed") +
                    (string.IsNullOrWhiteSpace(status.LastError) ? string.Empty : "\n" + status.LastError));
                return;
            }
            if (status.State == PhysicalSourceHidingState.Waiting)
            {
                SetStatusIndicator(PhysicalSourceHidingStatusDot, PhysicalSourceHidingStatusText,
                    PhysicalSourceHidingStatusBadge, "WarningBrush", Tr.Get("settings.advanced.physical_source_hiding.waiting_prefix") + count + "）");
                return;
            }
            SetStatusIndicator(PhysicalSourceHidingStatusDot, PhysicalSourceHidingStatusText,
                PhysicalSourceHidingStatusBadge, "SuccessBrush", Tr.Get("settings.advanced.update_physical_source_hiding_status_indicator.device_hide") + count + "）");
        }

        private void SetStatusIndicator(System.Windows.Shapes.Ellipse dot, TextBlock text, Border badge, string brushKey, string toolTip)
        {
            Brush brush = FindBrush(brushKey);
            dot.Fill = brush;
            text.Foreground = brush;
            badge.Background = Brushes.Transparent;
            badge.ToolTip = toolTip;
        }

        private void RefreshOverrideList()
        {
            IReadOnlyList<TrackingOverrideOption> overrides =
                string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath)
                    ? Array.Empty<TrackingOverrideOption>()
                    : _settingsService.ReadOverrides(_settingsPath);

            OverridesItemsControl.ItemsSource = overrides;
            OverrideCountText.Text = overrides.Count + Tr.Get("settings.steamvr.refresh_override_list.item");
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
            SourcePathText.Text = source?.DevicePath ?? Tr.Get("app.update_selection_details.select_source");
            TargetPathText.Text = target?.TargetPath ?? Tr.Get("app.update_selection_details.select_target");
            bool legacyRoleTarget = target != null && !IsConcreteDeviceTarget(target);
            LegacyStaticTargetWarningText.Visibility = legacyRoleTarget
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateSteamVrStatus();
        }

        private void UpdateRuntimeSelectionDetails()
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            DeviceOption rotationSource = RuntimeRotationSourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = RuntimeTargetComboBox.SelectedItem as TargetOption;
            bool modeSelected = RuntimeModeComboBox.SelectedItem is RouteModeOption;
            bool replacesTarget = _selectedRoute?.Mode == RouteMode.ReplaceTarget;
            bool virtualController = _selectedRoute?.Mode == RouteMode.VirtualController;
            bool manualPose = _selectedRoute?.PoseSourceKind == PoseSourceKind.Manual;
            bool controllerReady = RuntimeControllerHandComboBox.SelectedItem is ControllerHandOption &&
                RuntimeControlInputComboBox.SelectedItem is ControlInputOption;
            RuntimeSourcePathText.Text = manualPose ? Tr.Get("app.update_runtime_selection_details.fixed_steamvr") :
                source?.DevicePath ?? Tr.Get("app.update_runtime_selection_details.select_position_source");
            RuntimeRotationSourcePathText.Text = _selectedRoute?.SplitPoseSource == true
                ? rotationSource?.DevicePath ?? Tr.Get("app.update_runtime_selection_details.select_rotation_source")
                : Tr.Get("route.rotation_source.same_as_position");
            HidePhysicalSourceCheckBox.IsEnabled = _selectedRoute != null &&
                !_selectedRoute.PendingDeletion &&
                !manualPose &&
                source != null &&
                (!_selectedRoute.SplitPoseSource || rotationSource != null);
            SplitPoseSourceCheckBox.IsEnabled = _selectedRoute != null &&
                !_selectedRoute.PendingDeletion && !manualPose;
            PoseEditorTitleText.Text = manualPose ? Tr.Get("route.source.manual_pose") : Tr.Get("route.pose_offset.title");
            RuntimeTargetPathText.Text = !modeSelected
                ? Tr.Get("app.update_runtime_selection_details.select_mode")
                : replacesTarget
                ? target?.TargetPath ?? Tr.Get("app.update_runtime_selection_details.select_replacement_target")
                : virtualController ? Tr.Get("app.update_runtime_selection_details.virtual_controller_replacement_target") : Tr.Get("app.update_runtime_selection_details.output_replacement_target");
            RouteConfiguration activeRoute = _runtimeStatus?.Configuration?.Routes?.FirstOrDefault(candidate =>
                _selectedRoute != null && string.Equals(candidate.RouteId, _selectedRoute.RouteId, StringComparison.Ordinal));
            bool sourceWillChange = activeRoute != null &&
                (activeRoute.PoseSourceKind != _selectedRoute.PoseSourceKind ||
                 !string.Equals(activeRoute.SourceDevicePath, source?.DevicePath ?? string.Empty, StringComparison.Ordinal));
            bool rotationSourceWillChange = activeRoute != null && _selectedRoute?.SplitPoseSource == true &&
                rotationSource != null &&
                !string.Equals(activeRoute.RotationSourceDevicePath, rotationSource.DevicePath, StringComparison.Ordinal);
            bool modeWillChange = activeRoute != null && activeRoute.Mode != _selectedRoute?.Mode;
            bool hmdRegistrationWillChange = activeRoute != null &&
                (activeRoute.Mode == RouteMode.VirtualHmd) != (_selectedRoute?.Mode == RouteMode.VirtualHmd);
            bool blockedRoleTarget = replacesTarget && target != null && IsSteamVrRoleTargetPath(target.TargetPath) &&
                !_showSteamVrRoleTargets;
            RuntimeRouteChangeWarningText.Text = blockedRoleTarget
                ? Tr.Get("route.validation.legacy_role_target")
                : hmdRegistrationWillChange
                ? Tr.Get("route.status.virtual_hmd_restart_required")
                : modeWillChange
                ? Tr.Get("route.status.mode_change_pending")
                : sourceWillChange
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Tr.Get("route.warning.source_change"),
                    DescribePoseSource(activeRoute.PoseSourceKind, activeRoute.SourceDevicePath),
                    DescribePoseSource(_selectedRoute.PoseSourceKind, source?.DevicePath))
                : rotationSourceWillChange
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Tr.Get("route.warning.rotation_source_change"),
                    rotationSource.DevicePath)
                : string.Empty;
            RuntimeRouteChangeWarningText.Visibility = blockedRoleTarget || modeWillChange ||
                sourceWillChange || rotationSourceWillChange
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_selectedRoute != null)
            {
                bool applied = _runtimeStatus != null && _runtimeStatus.DriverConnected &&
                    _runtimeStatus.ConfigurationRevision == _runtimeStatus.DriverAppliedRevision &&
                    activeRoute != null && RouteConfigurationMatches(activeRoute, _selectedRoute);
                bool routeSaved = activeRoute != null && RouteConfigurationMatches(activeRoute, _selectedRoute);
                string proxyPath = ProtocolConstants.GetProxyDevicePath(_selectedRoute.VirtualDeviceSlot);
                TrackingOverrideOption proxyMapping = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath)
                    ? _settingsService.ReadOverrides(_settingsPath).FirstOrDefault(mapping =>
                        string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal))
                    : null;
                bool exactMapping = replacesTarget && proxyMapping != null &&
                    string.Equals(proxyMapping.TargetPath, _selectedRoute.TargetDevicePath, StringComparison.Ordinal);
                bool desiredVirtualHmd = _workingRoutes.Any(route =>
                    route.Enabled && route.Mode == RouteMode.VirtualHmd && IsRouteComplete(route));
                bool hmdBootReady = _settingsService.ReadVirtualHmdEnabled(_settingsPath) == desiredVirtualHmd;
                bool staticStateReady = (replacesTarget ? exactMapping : proxyMapping == null) && hmdBootReady;
                bool steamVrRunning = _statusService.IsRunning();
                bool synchronized = routeSaved && staticStateReady && (!steamVrRunning || applied);
                SelectedRouteSyncText.Text = _selectedRoute.PendingDeletion
                    ? Tr.Get("route.status.pending_delete")
                    : synchronized ? Tr.Get("route.status.synchronized") : !routeSaved
                    ? Tr.Get("route.status.pending_apply")
                    : !staticStateReady
                    ? !hmdBootReady
                        ? Tr.Get("app.update_runtime_selection_details.waiting_restart_steamvr")
                        : replacesTarget
                        ? Tr.Get("app.update_runtime_selection_details.write_mapping")
                        : Tr.Get("app.update_runtime_selection_details.remove_mapping")
                    : Tr.Get("app.update_runtime_selection_details.config");
                SelectedRouteSyncText.Foreground = FindBrush(
                    _selectedRoute.PendingDeletion || !synchronized ? "WarningBrush" : "SuccessBrush");
                if (!string.IsNullOrWhiteSpace(_routeAutoApplyIssueText))
                {
                    SelectedRouteSyncText.Text = _routeAutoApplyIssueText;
                    SelectedRouteSyncText.Foreground = FindBrush("WarningBrush");
                    SelectedRouteSyncText.ToolTip = _routeAutoApplyIssueDetail;
                }
                else
                {
                    SelectedRouteSyncText.ToolTip = null;
                }
            }
        }

        private void LoadRuntimeConfiguration(RuntimeConfiguration configuration)
        {
            string selectedRouteId = _selectedRoute?.RouteId;
            _isLoading = true;
            try
            {
                _physicalSourceHidingEnabled = configuration.PhysicalSourceHidingEnabled;
                PhysicalSourceHidingEnabledCheckBox.IsChecked = _physicalSourceHidingEnabled;
                _workingOsc = CloneOscConfiguration(configuration.Osc ?? OscConfiguration.CreateDefault());
                LoadOscFields(_workingOsc);
                _workingXInput = CloneXInputConfiguration(configuration.XInput ?? XInputConfiguration.CreateDefault());
                LoadXInputFields(_workingXInput);
                _workingRoutes.Clear();
                int unnamedIndex = 0;
                foreach (RouteConfiguration route in configuration.Routes ?? new List<RouteConfiguration>())
                {
                    RouteConfiguration copy = CloneRoute(route);
                    if (string.IsNullOrWhiteSpace(copy.Name))
                    {
                        copy.Name = unnamedIndex == 0 ? Tr.Get("route.default_name") : Tr.Get("route.default_name_numbered_prefix") + unnamedIndex + ")";
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
                HidePhysicalSource = route.HidePhysicalSource,
                SplitPoseSource = route.SplitPoseSource,
                VirtualDeviceSlot = route.VirtualDeviceSlot,
                Mode = route.Mode,
                ControllerHand = route.ControllerHand,
                ControlInputSource = route.ControlInputSource,
                PoseSourceKind = route.PoseSourceKind,
                SourceDevicePath = route.SourceDevicePath,
                RotationSourceDevicePath = route.RotationSourceDevicePath,
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
                },
                ManualPose = ClonePoseOffset(route.ManualPose ?? PoseOffset.DefaultManualPose())
            };
        }

        private static PoseOffset ClonePoseOffset(PoseOffset value)
        {
            return new PoseOffset
            {
                TranslationX = value.TranslationX,
                TranslationY = value.TranslationY,
                TranslationZ = value.TranslationZ,
                RotationX = value.RotationX,
                RotationY = value.RotationY,
                RotationZ = value.RotationZ,
                RotationW = value.RotationW
            };
        }

        private void RefreshRouteList(string selectedRouteId = null)
        {
            bool preserveSettingsPage = _showingSettings;
            _routeItems.Clear();
            RouteCountText.Text = _workingRoutes.Count.ToString("00", CultureInfo.InvariantCulture) +
                " / " + ProtocolConstants.MaximumRoutes.ToString("00", CultureInfo.InvariantCulture);
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
                RouteConfiguration activeRoute = _runtimeStatus?.Configuration?.Routes?.FirstOrDefault(candidate =>
                    string.Equals(candidate.RouteId, route.RouteId, StringComparison.Ordinal));
                bool routeSaved = activeRoute != null && RouteConfigurationMatches(activeRoute, route);
                TrackingOverrideOption proxyMapping = overrides.FirstOrDefault(mapping =>
                    string.Equals(mapping.SourcePath, ProtocolConstants.GetProxyDevicePath(route.VirtualDeviceSlot), StringComparison.Ordinal));
                bool replacesTarget = route.Mode == RouteMode.ReplaceTarget;
                bool exactMapping = replacesTarget && proxyMapping != null &&
                    string.Equals(proxyMapping.TargetPath, route.TargetDevicePath, StringComparison.Ordinal);
                bool desiredVirtualHmd = _workingRoutes.Any(candidate =>
                    candidate.Enabled && candidate.Mode == RouteMode.VirtualHmd && IsRouteComplete(candidate));
                bool hmdBootReady = _settingsService.ReadVirtualHmdEnabled(_settingsPath) == desiredVirtualHmd;
                bool staticStateReady = (replacesTarget ? exactMapping : proxyMapping == null) && hmdBootReady;
                string state = route.PendingDeletion ? Tr.Get("route.status.pending_delete") : !route.Enabled ? Tr.Get("route.refresh_route_list.disable") : !routeSaved
                    ? Tr.Get("route.status.pending_apply")
                    : !staticStateReady
                    ? !hmdBootReady ? Tr.Get("route.refresh_route_list.restart_steamvr") : replacesTarget ? Tr.Get("route.status.pending_mapping") : Tr.Get("route.status.pending_unmapping")
                    : !steamVrRunning ? Tr.Get("route.refresh_route_list.config") : driverApplied ? Tr.Get("route.status.applied") : Tr.Get("runtime.status.waiting_for_driver");
                Brush brush = FindBrush(route.PendingDeletion ? "WarningBrush" : !route.Enabled ? "MutedTextBrush" : routeSaved && staticStateReady && (!steamVrRunning || driverApplied) ? "SuccessBrush" : "WarningBrush");
                _routeItems.Add(new RouteListItem(route, state, brush));
            }
            RouteListItem selection = _routeItems.FirstOrDefault(item =>
                string.Equals(item.Route.RouteId, selectedRouteId, StringComparison.Ordinal)) ??
                _routeItems.FirstOrDefault();
            RouteListBox.SelectedItem = preserveSettingsPage ? null : selection;
            UpdateContentVisibility();
        }

        private void ShowSelectedRoute(RouteConfiguration route)
        {
            ClearRouteAutoApplyIssue();
            _selectedRoute = route;
            _splitBasePreviewRotation = Quaternion.Identity;
            if (route == null)
            {
                _gizmoVisible = false;
                HidePreviewModel(_gizmoPreviewModel);
                UpdateContentVisibility();
                return;
            }

            _isLoading = true;
            try
            {
                IReadOnlyList<DeviceOption> sources = BuildDeviceChoices(
                    _knownPhysicalDevices,
                    route.SourceDevicePath,
                    route.PoseSourceKind,
                    includeManual: true);
                RuntimeSourceComboBox.ItemsSource = sources;
                DeviceOption source = sources.FirstOrDefault(candidate =>
                    candidate.PoseSourceKind == route.PoseSourceKind &&
                    (route.PoseSourceKind == PoseSourceKind.Manual ||
                     string.Equals(candidate.DevicePath, route.SourceDevicePath, StringComparison.Ordinal)));
                RuntimeSourceComboBox.SelectedItem = source;
                IReadOnlyList<DeviceOption> rotationSources = BuildDeviceChoices(
                    _knownPhysicalDevices,
                    route.RotationSourceDevicePath);
                RuntimeRotationSourceComboBox.ItemsSource = rotationSources;
                RuntimeRotationSourceComboBox.SelectedItem = rotationSources.FirstOrDefault(candidate =>
                    string.Equals(candidate.DevicePath, route.RotationSourceDevicePath, StringComparison.Ordinal));

                RuntimeModeComboBox.SelectedItem = RuntimeModeComboBox.Items
                    .Cast<RouteModeOption>()
                    .FirstOrDefault(option => option.Mode == route.Mode);
                RuntimeControllerHandComboBox.SelectedItem = RuntimeControllerHandComboBox.Items
                    .Cast<ControllerHandOption>()
                    .FirstOrDefault(option => option.Hand == route.ControllerHand);
                RuntimeControlInputComboBox.SelectedItem = RuntimeControlInputComboBox.Items
                    .Cast<ControlInputOption>()
                    .FirstOrDefault(option => option.Source == route.ControlInputSource);

                IReadOnlyList<TargetOption> targets = BuildTargets(
                    _knownPhysicalDevices,
                    Array.Empty<TargetOption>(),
                    includeRoleTargets: _showSteamVrRoleTargets,
                    includeConcreteDevices: true,
                    currentTargetPath: route.TargetDevicePath);
                RuntimeTargetComboBox.ItemsSource = targets;
                TargetOption target = targets.FirstOrDefault(candidate => string.Equals(candidate.TargetPath, route.TargetDevicePath, StringComparison.Ordinal));
                RuntimeTargetComboBox.SelectedItem = target;
                LoadOffsetFields(route.PoseSourceKind == PoseSourceKind.Manual
                    ? route.ManualPose ?? PoseOffset.DefaultManualPose()
                    : route.Offset ?? PoseOffset.Identity());
                SelectedProxyText.Text = route.Mode == RouteMode.Unspecified
                    ? Tr.Get("route.show_selected_route.select_mode")
                    : route.Mode == RouteMode.VirtualController
                        ? ProtocolConstants.GetControllerSerial(route.ControllerHand)
                        : ProtocolConstants.GetOutputSerial(route.Mode, route.VirtualDeviceSlot);
                RuntimeProxyText.Text = ProtocolConstants.GetOutputSerial(route.Mode, route.VirtualDeviceSlot);
                RuntimeControllerOutputText.Text = string.IsNullOrWhiteSpace(ProtocolConstants.GetControllerSerial(route.ControllerHand))
                    ? Tr.Get("route.controller.select_hand")
                    : ProtocolConstants.GetControllerSerial(route.ControllerHand);
                SelectedRouteNameText.Text = route.Name;
                ToggleSelectedRouteButton.Content = route.Enabled ? Tr.Get("common.action.disable") : Tr.Get("common.action.enable");
                ToggleSelectedRouteButton.IsEnabled = !route.PendingDeletion;
                RuntimeSourceComboBox.IsEnabled = !route.PendingDeletion;
                RuntimeRotationSourceComboBox.IsEnabled = !route.PendingDeletion && route.SplitPoseSource;
                RuntimeRotationSourceComboBox.Tag = route.SplitPoseSource ? Tr.Get("common.action.select") : Tr.Get("route.rotation_source.same_as_position");
                RuntimeModeComboBox.IsEnabled = !route.PendingDeletion;
                RuntimeTargetComboBox.IsEnabled = !route.PendingDeletion;
                HidePhysicalSourceCheckBox.Visibility = _physicalSourceHidingEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                HidePhysicalSourceCheckBox.IsChecked = route.HidePhysicalSource;
                HidePhysicalSourceCheckBox.IsEnabled = !route.PendingDeletion &&
                    route.PoseSourceKind == PoseSourceKind.Device &&
                    !string.IsNullOrWhiteSpace(route.SourceDevicePath);
                SplitPoseSourceCheckBox.IsChecked = route.SplitPoseSource;
                SplitPoseSourceCheckBox.IsEnabled = !route.PendingDeletion &&
                    route.PoseSourceKind == PoseSourceKind.Device;
                UpdateRuntimeModePresentation();
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
            bool waitingForRuntimeConfiguration = !_runtimeEditorInitialized;
            RuntimeLoadingStateGrid.Visibility = !_showingSettings && waitingForRuntimeConfiguration
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyStateGrid.Visibility = !_showingSettings && !waitingForRuntimeConfiguration && !hasRoute
                ? Visibility.Visible
                : Visibility.Collapsed;
            RouteContentScrollViewer.Visibility = !_showingSettings && !waitingForRuntimeConfiguration && hasRoute
                ? Visibility.Visible
                : Visibility.Collapsed;
            SettingsContentScrollViewer.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
            AddRouteButton.IsEnabled = _runtimeEditorInitialized &&
                _workingRoutes.Count < ProtocolConstants.MaximumRoutes;
            UpdateHapticTimelineRendering();
        }

        private void UpdateHapticTimelineRendering()
        {
            bool shouldRender = IsLoaded &&
                !_isClosing &&
                _showingSettings &&
                _settingsSection == SettingsSection.Osc;
            if (shouldRender && !_hapticTimelineRenderingAttached)
            {
                CompositionTarget.Rendering += HapticTimelineRendering;
                _hapticTimelineRenderingAttached = true;
            }
            else if (!shouldRender)
            {
                DetachHapticTimelineRendering();
            }
        }

        private void HapticTimelineRendering(object sender, EventArgs e)
        {
            OscLeftHapticIndicator.InvalidateVisual();
            OscRightHapticIndicator.InvalidateVisual();
        }

        private void DetachHapticTimelineRendering()
        {
            if (!_hapticTimelineRenderingAttached)
            {
                return;
            }
            CompositionTarget.Rendering -= HapticTimelineRendering;
            _hapticTimelineRenderingAttached = false;
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
                return Tr.Get("route.build_configured_runtime_target_name.role_target_right_physical_device");
            }
            if (string.Equals(targetPath, ProtocolConstants.LeftHandRolePath, StringComparison.Ordinal))
            {
                return Tr.Get("route.build_configured_runtime_target_name.role_target_left_physical_device");
            }
            if (string.Equals(targetPath, ProtocolConstants.HeadRolePath, StringComparison.Ordinal))
            {
                return Tr.Get("route.build_configured_runtime_target_name.role_target_hmd_physical_device");
            }
            return Tr.Get("route.build_configured_runtime_target_name.config_target");
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
                AppDialog.Show(this, Tr.Get("route.limit.prefix") + ProtocolConstants.MaximumRoutes + Tr.Get("route.add_route_button_click.item"), Tr.Get("route.add_route_button_click.cannot"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                Mode = RouteMode.Unspecified,
                Offset = PoseOffset.Identity()
            };
            _workingRoutes.Add(route);
            RefreshRouteList(route.RouteId);
            ShowSelectedRoute(route);
        }

        private string GetNextRouteName()
        {
            var names = new HashSet<string>(_workingRoutes.Select(route => route.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(Tr.Get("route.default_name")))
            {
                return Tr.Get("route.default_name");
            }
            for (int index = 1; ; index++)
            {
                string candidate = Tr.Get("route.default_name_numbered_prefix") + index.ToString(CultureInfo.InvariantCulture) + ")";
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool IsRouteComplete(RouteConfiguration route)
        {
            return route != null &&
                (route.PoseSourceKind == PoseSourceKind.Manual
                    ? !route.SplitPoseSource
                    : !string.IsNullOrWhiteSpace(route.SourceDevicePath)) &&
                (!route.SplitPoseSource || !string.IsNullOrWhiteSpace(route.RotationSourceDevicePath)) &&
                (route.Mode == RouteMode.DirectProxy ||
                 route.Mode == RouteMode.VirtualHmd ||
                 route.Mode == RouteMode.ReplaceTarget && !string.IsNullOrWhiteSpace(route.TargetDevicePath) ||
                 route.Mode == RouteMode.VirtualController &&
                    (route.ControllerHand == ControllerHand.Left || route.ControllerHand == ControllerHand.Right) &&
                    (route.ControlInputSource == ControlInputSource.None ||
                     route.ControlInputSource == ControlInputSource.Osc ||
                     route.ControlInputSource == ControlInputSource.XInput));
        }

        private static bool RouteConfigurationMatches(RouteConfiguration left, RouteConfiguration right)
        {
            return left != null && right != null &&
                left.Enabled == right.Enabled &&
                left.PendingDeletion == right.PendingDeletion &&
                left.HidePhysicalSource == right.HidePhysicalSource &&
                left.SplitPoseSource == right.SplitPoseSource &&
                left.Mode == right.Mode &&
                left.PoseSourceKind == right.PoseSourceKind &&
                string.Equals(left.SourceDevicePath, right.SourceDevicePath, StringComparison.Ordinal) &&
                string.Equals(left.RotationSourceDevicePath, right.RotationSourceDevicePath, StringComparison.Ordinal) &&
                (left.Mode == RouteMode.DirectProxy ||
                 left.Mode == RouteMode.VirtualHmd ||
                 left.Mode == RouteMode.VirtualController &&
                    left.ControllerHand == right.ControllerHand &&
                    left.ControlInputSource == right.ControlInputSource ||
                  left.Mode == RouteMode.ReplaceTarget &&
                    string.Equals(left.TargetDevicePath, right.TargetDevicePath, StringComparison.Ordinal)) &&
                PoseOffsetsMatch(left.Offset, right.Offset) &&
                PoseOffsetsMatch(left.ManualPose, right.ManualPose);
        }

        private static bool PoseOffsetsMatch(PoseOffset left, PoseOffset right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            const double tolerance = 1e-9;
            return Math.Abs(left.TranslationX - right.TranslationX) <= tolerance &&
                Math.Abs(left.TranslationY - right.TranslationY) <= tolerance &&
                Math.Abs(left.TranslationZ - right.TranslationZ) <= tolerance &&
                Math.Abs(left.RotationX - right.RotationX) <= tolerance &&
                Math.Abs(left.RotationY - right.RotationY) <= tolerance &&
                Math.Abs(left.RotationZ - right.RotationZ) <= tolerance &&
                Math.Abs(left.RotationW - right.RotationW) <= tolerance;
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
            else if (sender == SettingsFilesCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Files);
            }
            else if (sender == SettingsLanguageCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Language);
            }
            else if (sender == SettingsOscCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Osc);
            }
            else if (sender == SettingsXInputCategoryButton)
            {
                ShowSettingsSection(SettingsSection.XInput);
            }
            else if (sender == SettingsAdvancedCategoryButton)
            {
                ShowSettingsSection(SettingsSection.Advanced);
            }
        }

        private void ShowSettingsSection(SettingsSection section)
        {
            if (section != SettingsSection.XInput)
            {
                CancelXInputCapture();
            }
            _settingsSection = section;
            RuntimeSettingsPanel.Visibility = section == SettingsSection.Runtime ? Visibility.Visible : Visibility.Collapsed;
            SteamVrSettingsPanel.Visibility = section == SettingsSection.SteamVr ? Visibility.Visible : Visibility.Collapsed;
            DeviceSettingsPanel.Visibility = section == SettingsSection.Devices ? Visibility.Visible : Visibility.Collapsed;
            FilesSettingsPanel.Visibility = section == SettingsSection.Files ? Visibility.Visible : Visibility.Collapsed;
            LanguageSettingsPanel.Visibility = section == SettingsSection.Language ? Visibility.Visible : Visibility.Collapsed;
            OscSettingsPanel.Visibility = section == SettingsSection.Osc ? Visibility.Visible : Visibility.Collapsed;
            XInputSettingsPanel.Visibility = section == SettingsSection.XInput ? Visibility.Visible : Visibility.Collapsed;
            AdvancedSettingsPanel.Visibility = section == SettingsSection.Advanced ? Visibility.Visible : Visibility.Collapsed;

            UpdateSettingsCategoryButton(SettingsRuntimeCategoryButton, section == SettingsSection.Runtime);
            UpdateSettingsCategoryButton(SettingsSteamVrCategoryButton, section == SettingsSection.SteamVr);
            UpdateSettingsCategoryButton(SettingsDevicesCategoryButton, section == SettingsSection.Devices);
            UpdateSettingsCategoryButton(SettingsFilesCategoryButton, section == SettingsSection.Files);
            UpdateSettingsCategoryButton(SettingsLanguageCategoryButton, section == SettingsSection.Language);
            UpdateSettingsCategoryButton(SettingsOscCategoryButton, section == SettingsSection.Osc);
            UpdateSettingsCategoryButton(SettingsXInputCategoryButton, section == SettingsSection.XInput);
            UpdateSettingsCategoryButton(SettingsAdvancedCategoryButton, section == SettingsSection.Advanced);
            if (section == SettingsSection.Runtime)
            {
                RefreshDiagnosticsView();
            }
            else if (section == SettingsSection.Files)
            {
                RefreshFilesAndBackupsView();
            }
            else if (section == SettingsSection.Language)
            {
                RefreshLanguageSettingsView();
            }
            UpdateHapticTimelineRendering();
        }

        private void RefreshLanguageSettingsView()
        {
            _loadingLanguageSelection = true;
            try
            {
                LanguageDirectoryText.Text = _localizationService.LanguageDirectory;
                LanguageComboBox.ItemsSource = _localizationService.Languages.ToList();
                LanguageComboBox.SelectedItem = _localizationService.Languages.FirstOrDefault(option =>
                    string.Equals(option.Locale, _localizationService.CurrentLocale, StringComparison.OrdinalIgnoreCase));

                var issueItems = _localizationService.Issues
                    .Select(issue => new LanguageIssueListItem(
                        issue.Key,
                        GetLocalizationIssueLabel(issue.Kind),
                        GetLocalizationIssueDetail(issue),
                        GetLocalizationIssueBrush(issue.Kind),
                        _localizationService.Translate("language.copy_hint")))
                    .ToList();
                LanguageIssuesItemsControl.ItemsSource = issueItems;
                LanguageIssuesEmptyText.Visibility = issueItems.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (_localizationService.IsOfficialLanguage)
                {
                    LanguagePackStatusText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        _localizationService.Translate("language.status.official"),
                        _localizationService.CatalogCount);
                }
                else
                {
                    int missing = _localizationService.Issues.Count(issue => issue.Kind == LocalizationIssueKind.Missing);
                    int invalid = _localizationService.Issues.Count(issue => issue.Kind == LocalizationIssueKind.Invalid);
                    int stale = _localizationService.Issues.Count(issue => issue.Kind == LocalizationIssueKind.Stale);
                    LanguagePackStatusText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        _localizationService.Translate("language.status.external"),
                        _localizationService.CurrentLocale,
                        missing,
                        invalid,
                        stale);
                }
            }
            finally
            {
                _loadingLanguageSelection = false;
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingLanguageSelection || !(LanguageComboBox.SelectedItem is LanguageOption option))
            {
                return;
            }
            _localizationService.SetLanguage(option.Locale);
            _languageLocale = _localizationService.CurrentLocale;
            SaveUiPreferences();
            RefreshLanguageSettingsView();
        }

        private void RefreshLanguagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _localizationService.Reload(_languageLocale);
                _languageLocale = _localizationService.CurrentLocale;
                SaveUiPreferences();
                LanguageCopyStatusText.Visibility = Visibility.Collapsed;
                RefreshLanguageSettingsView();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException ||
                exception is JsonException)
            {
                AppDialog.Show(
                    this,
                    exception.Message,
                    _localizationService.Translate("language.refresh.failure_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenLanguageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(_localizationService.LanguageDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + _localizationService.LanguageDirectory + "\"",
                UseShellExecute = true
            });
        }

        private void ExportLanguageTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = _localizationService.Translate("language.template.dialog_title"),
                Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                FileName = "TrackSwap-i18n-template.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            try
            {
                _localizationService.ExportTemplate(dialog.FileName);
                AppDialog.Show(
                    this,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _localizationService.Translate("language.template.success"),
                        dialog.FileName),
                    _localizationService.Translate("language.template.success_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                AppDialog.Show(
                    this,
                    exception.Message,
                    _localizationService.Translate("language.template.failure_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LanguageIssueButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string key) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }
            Clipboard.SetText(key);
            LanguageCopyStatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.Translate("language.copied"),
                key);
            LanguageCopyStatusText.Visibility = Visibility.Visible;
        }

        private string GetLocalizationIssueLabel(LocalizationIssueKind kind)
        {
            switch (kind)
            {
                case LocalizationIssueKind.Missing:
                    return _localizationService.Translate("language.issue.missing");
                case LocalizationIssueKind.Invalid:
                    return _localizationService.Translate("language.issue.invalid");
                default:
                    return _localizationService.Translate("language.issue.stale");
            }
        }

        private Brush GetLocalizationIssueBrush(LocalizationIssueKind kind)
        {
            return FindBrush(kind == LocalizationIssueKind.Invalid ? "DestructiveBrush" : "WarningBrush");
        }

        private string GetLocalizationIssueDetail(LocalizationIssue issue)
        {
            if (issue.Key.StartsWith("file:", StringComparison.Ordinal))
            {
                return _localizationService.Translate("language.issue.detail.file");
            }
            switch (issue.Kind)
            {
                case LocalizationIssueKind.Missing:
                    return _localizationService.Translate("language.issue.detail.missing");
                case LocalizationIssueKind.Invalid:
                    return _localizationService.Translate("language.issue.detail.invalid");
                default:
                    return _localizationService.Translate("language.issue.detail.stale");
            }
        }

        private void RefreshFilesAndBackupsView()
        {
            string applicationRoot = TrackSwapDataPaths.ApplicationRootDirectory;
            string activeData = TrackSwapDataPaths.ActiveDataDirectory;
            CurrentDataLocationText.Text = activeData;
            bool insideInstallation = IsPathInside(activeData, applicationRoot);
            bool preferredWritable = TrackSwapDataPaths.CanUsePreferredDataDirectory;
            CurrentDataLocationStateText.Text = insideInstallation
                ? Tr.Get("settings.files.data_root.install_directory_notice")
                : preferredWritable
                    ? Tr.Get("settings.files.migration.legacy_root_notice")
                    : Tr.Get("settings.files.data_root.fallback_notice");
            CurrentDataLocationStateText.Foreground = FindBrush(
                insideInstallation ? "SuccessBrush" : "WarningBrush");

            bool hasLegacyData = _dataMigrationService.HasLegacyData;
            LegacyMigrationCard.Visibility = hasLegacyData ? Visibility.Visible : Visibility.Collapsed;
            LegacyDataLocationText.Text = _dataMigrationService.LegacyDirectory;
            MigrateLegacyDataButton.Visibility = _dataMigrationService.CanMigrate
                ? Visibility.Visible
                : Visibility.Collapsed;
            CleanupLegacyDataButton.Visibility = hasLegacyData && !_dataMigrationService.CanMigrate &&
                !TrackSwapDataPaths.IsUsingLegacyData
                ? Visibility.Visible
                : Visibility.Collapsed;
            LegacyMigrationDescriptionText.Text = _dataMigrationService.CanMigrate
                ? Tr.Get("settings.files.migration.available_notice")
                : !preferredWritable && TrackSwapDataPaths.IsUsingLegacyData
                    ? Tr.Get("settings.files.migration.destination_not_writable")
                    : Tr.Get("settings.files.migration.cleanup_available");

            string openVrRegistry = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "openvr",
                "openvrpaths.vrpath");
            string steamConfigDirectory = string.IsNullOrWhiteSpace(_settingsPath)
                ? null
                : Path.GetDirectoryName(_settingsPath);
            string steamLogDirectory = _pathService.FindLogPath();
            var items = new List<FileLocationListItem>
            {
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.trackswap_install_directory"), applicationRoot,
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.steam_file"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.data_directory"), activeData,
                    Tr.Get("settings.files.location.user_data_description"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.runtime_config"), Path.Combine(activeData, "runtime-config.json"),
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.osc_xinput_runtime_advanced"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.ui_settings"), _uiPreferencesService.FilePath,
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.ui_show_lifecycle"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.language_pack"), _localizationService.LanguageDirectory,
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.json_file"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.category.devices"), _deviceHistoryService.FilePath,
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.device_name_path"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.calibration"), Path.Combine(activeData, "calibration-profiles.json"),
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.calibration_create"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.logs"), Path.Combine(activeData, "runtime-ui-launch.log"),
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.runtime_start_trackswap_ui"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.automatic_backup"), Path.Combine(activeData, "Backups", "Automatic"),
                    Tr.Get("settings.files.backup.rollback_description"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_config_backup"), Path.Combine(activeData, "Backups", "SteamVR"),
                    Tr.Get("settings.steamvr.backup.description"), applicationRoot),
                CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.openvr_path"), openVrRegistry,
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_file_trackswap_driver_path"), applicationRoot)
            };
            if (!string.IsNullOrWhiteSpace(steamConfigDirectory))
            {
                items.Add(CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_pose_settings"), _settingsPath,
                    Tr.Get("settings.files.location.steamvr_settings_description"), applicationRoot));
                items.Add(CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_apply"), Path.Combine(steamConfigDirectory, "appconfig.json"),
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_file_trackswap_manifest"), applicationRoot));
                items.Add(CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_auto_start_settings"), Path.Combine(
                        steamConfigDirectory,
                        "vrappconfig",
                        "com.hrenact.trackswap.vrappconfig"),
                    Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_trackswap_save_auto_start_status"), applicationRoot));
            }
            if (!string.IsNullOrWhiteSpace(steamLogDirectory))
            {
                items.Add(CreateFileLocation(Tr.Get("settings.files.backup.refresh_files_and_backups_view.steamvr_logs_directory"), steamLogDirectory,
                    Tr.Get("settings.files.location.steamvr_log_description"), applicationRoot));
            }
            FileLocationsItemsControl.ItemsSource = items;
        }

        private FileLocationListItem CreateFileLocation(
            string name,
            string path,
            string description,
            string applicationRoot)
        {
            bool exists = !string.IsNullOrWhiteSpace(path) &&
                (File.Exists(path) || Directory.Exists(path));
            bool inside = !string.IsNullOrWhiteSpace(path) && IsPathInside(path, applicationRoot);
            return new FileLocationListItem(
                name,
                path ?? Tr.Get("settings.files.create_file_location.not_found"),
                description,
                inside ? Tr.Get("settings.files.create_file_location.install_directory") : Tr.Get("settings.files.outside_install_directory"),
                inside ? FindBrush("SuccessBrush") : FindBrush("WarningBrush"),
                exists ? Tr.Get("common.status.exists") : Tr.Get("settings.files.create_file_location.create"));
        }

        private static bool IsPathInside(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            string candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string path = (sender as FrameworkElement)?.Tag as string;
            OpenPathInExplorer(path, selectFile: false);
        }

        private void LocatePathButton_Click(object sender, RoutedEventArgs e)
        {
            string path = (sender as FrameworkElement)?.Tag as string;
            OpenPathInExplorer(path, selectFile: File.Exists(path));
        }

        private static void OpenPathInExplorer(string path, bool selectFile)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string existing = path;
            while (!File.Exists(existing) && !Directory.Exists(existing))
            {
                existing = Path.GetDirectoryName(existing);
                if (string.IsNullOrWhiteSpace(existing))
                {
                    return;
                }
            }

            string arguments = selectFile && File.Exists(path)
                ? "/select,\"" + path + "\""
                : "\"" + (Directory.Exists(existing) ? existing : Path.GetDirectoryName(existing)) + "\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }

        private async void MigrateLegacyDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_statusService.IsRunning())
            {
                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.migration.stop_runtime_first"),
                    Tr.Get("steamvr.status.running"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool targetHasData = TrackSwapDataPaths.ContainsRecognizedData(
                TrackSwapDataPaths.PreferredDataDirectory);
            if (targetHasData && AppDialog.Show(
                    this,
                    Tr.Get("settings.files.migration.replace_existing_confirmation"),
                    Tr.Get("settings.files.migrate_legacy_data_button_click.confirm_migration"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            MigrateLegacyDataButton.IsEnabled = false;
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
                await WaitForRuntimeExitAsync();

                int steamVrBackupCount = _settingsService.MigrateLegacyBackups(_settingsPath);
                MigrationResult result = _dataMigrationService.Migrate(targetHasData);
                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.migrate_legacy_data_button_click.migration") + result.FileCount + Tr.Get("settings.files.migrate_legacy_data_button_click.count_file") + steamVrBackupCount +
                    Tr.Get("settings.files.migrate_legacy_data_button_click.count_steamvr_config_backup") + result.TargetDirectory +
                    Tr.Get("settings.files.migration.restart_notice"),
                    Tr.Get("settings.files.migrate_legacy_data_button_click.migration_complete"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RestartApplicationAfterDataChange();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException ||
                exception is InvalidOperationException)
            {
                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.migrate_legacy_data_button_click.cannot_migration") + exception.Message,
                    Tr.Get("settings.files.migrate_legacy_data_button_click.migration_failed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                MigrateLegacyDataButton.IsEnabled = true;
                RefreshFilesAndBackupsView();
            }
        }

        private static async Task WaitForRuntimeExitAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Process[] processes = Process.GetProcessesByName("TrackSwap.Runtime");
                try
                {
                    if (processes.Length == 0)
                    {
                        return;
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException(Tr.Get("app.wait_for_runtime_exit_async.runtime_5_exit_migration_cancel_close_runtime"));
        }

        private void CleanupLegacyDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (AppDialog.Show(
                    this,
                    Tr.Get("settings.files.legacy_cleanup.confirmation"),
                    Tr.Get("settings.files.cleanup_legacy_data_button_click.confirm_cleanup_file"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                LegacyCleanupResult result = _dataMigrationService.CleanLegacyData();
                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.cleanup_legacy_data_button_click.delete") + result.RemovedFileCount + Tr.Get("settings.files.cleanup_legacy_data_button_click.count_file") +
                    (result.DirectoryRemoved ? Tr.Get("settings.files.cleanup_legacy_data_button_click.directory_clear_remove") : Tr.Get("settings.files.cleanup_legacy_data_button_click.directory_file_preserve")),
                    Tr.Get("settings.files.cleanup_legacy_data_button_click.cleanup_complete"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshFilesAndBackupsView();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("common.error.cleanup_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResetTrackSwapDataButton_Click(object sender, RoutedEventArgs e)
        {
            await ResetTrackSwapAsync(removeSteamIntegration: false);
        }

        private async void RemoveTrackSwapDataAndIntegrationButton_Click(object sender, RoutedEventArgs e)
        {
            await ResetTrackSwapAsync(removeSteamIntegration: true);
        }

        private async Task ResetTrackSwapAsync(bool removeSteamIntegration)
        {
            if (_destructiveCleanupInProgress)
            {
                return;
            }

            if (_statusService.IsRunning())
            {
                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.cleanup.stop_steamvr_first"),
                    Tr.Get("steamvr.status.running"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string actionDescription = removeSteamIntegration
                ? Tr.Get("settings.files.pre_uninstall.confirmation")
                : Tr.Get("settings.files.reset.confirmation");
            if (AppDialog.Show(
                    this,
                    actionDescription + Tr.Get("app.reset_track_swap_async.cannot_ok"),
                    removeSteamIntegration ? Tr.Get("app.reset_track_swap_async.confirm_pre_uninstall") : Tr.Get("app.reset_track_swap_async.confirm_reset_trackswap"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            SetDestructiveCleanupOverlay(visible: true, removeSteamIntegration: removeSteamIntegration);
            await Dispatcher.Yield(DispatcherPriority.Render);
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
                await WaitForRuntimeExitAsync();

                if (removeSteamIntegration)
                {
                    await _steamIntegrationMaintenanceService.RemoveAsync();
                }
                else
                {
                    await _steamIntegrationMaintenanceService.CleanupMappingsAsync();
                }

                _dataCleanupService.Clean();
                _isClosing = true;
                Application.Current.Shutdown(0);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException ||
                exception is InvalidOperationException ||
                exception is System.ComponentModel.Win32Exception)
            {
                SetDestructiveCleanupOverlay(visible: false, removeSteamIntegration: removeSteamIntegration);
                AppDialog.Show(
                    this,
                    Tr.Get("app.reset_track_swap_async.cleanup_complete_complete_stop_down_file_location") + exception.Message,
                    Tr.Get("common.error.cleanup_failed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (!_isClosing)
                {
                    SetDestructiveCleanupOverlay(visible: false, removeSteamIntegration: removeSteamIntegration);
                    RefreshFilesAndBackupsView();
                }
            }
        }

        private void SetDestructiveCleanupOverlay(bool visible, bool removeSteamIntegration)
        {
            _destructiveCleanupInProgress = visible;
            DestructiveCleanupOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }

            DestructiveCleanupTitleText.Text = removeSteamIntegration
                ? Tr.Get("settings.files.set_destructive_cleanup_overlay.in_progress")
                : Tr.Get("settings.files.set_destructive_cleanup_overlay.in_progress_reset");
            DestructiveCleanupDescriptionText.Text = removeSteamIntegration
                ? Tr.Get("settings.files.pre_uninstall.progress")
                : Tr.Get("settings.files.reset.progress");
            DestructiveCleanupOverlay.Focus();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_destructiveCleanupInProgress && !_isClosing)
            {
                e.Cancel = true;
            }
        }

        private void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_runtimeStatus?.Configuration == null)
            {
                AppDialog.Show(this, Tr.Get("service.configuration_backup.export.runtime_read_config"), Tr.Get("settings.files.backup.export_configuration_button_click.cannot_export"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = Tr.Get("settings.files.backup.export_configuration_button_click.export_trackswap_config"),
                Filter = Tr.Get("settings.files.backup.save_file_filter"),
                DefaultExt = ".trackswap-backup",
                AddExtension = true,
                FileName = "TrackSwap-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".trackswap-backup"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                _configurationBackupService.Export(
                    dialog.FileName,
                    _runtimeStatus.Configuration,
                    CreateUiPreferencesSnapshot());
                AppDialog.Show(this, Tr.Get("settings.files.backup.export_configuration_button_click.config_export") + dialog.FileName, Tr.Get("common.status.export_complete"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException ||
                exception is NotSupportedException)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("common.error.export_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Tr.Get("settings.files.backup.import_configuration_button_click.import_trackswap_config"),
                Filter = Tr.Get("settings.files.backup.open_file_filter"),
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            ImportConfigurationButton.IsEnabled = false;
            try
            {
                ConfigurationBackupFile backup = _configurationBackupService.Read(dialog.FileName);
                int routeCount = backup.RuntimeConfiguration.Routes?.Count ?? 0;
                string riskText = backup.RuntimeConfiguration.PhysicalSourceHidingEnabled
                    ? Tr.Get("settings.files.backup.import_hiding_notice")
                    : string.Empty;
                if (AppDialog.Show(
                        this,
                        Tr.Get("settings.files.backup.import_configuration_button_click.backup") + routeCount + Tr.Get("settings.files.backup.import_configuration_button_click.item_config_create") +
                        backup.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                        Tr.Get("settings.files.backup.import_overwrite_suffix") +
                        riskText + Tr.Get("settings.files.backup.import_configuration_button_click.ok_import"),
                        Tr.Get("settings.files.backup.import_configuration_button_click.confirm_import_config"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                RuntimeConfiguration previousConfiguration = _runtimeStatus?.Configuration;
                if (previousConfiguration == null)
                {
                    throw new InvalidDataException(Tr.Get("settings.files.backup.import_configuration_button_click.runtime_read_current_config"));
                }
                UiPreferences previousPreferences = CreateUiPreferencesSnapshot();
                _configurationBackupService.CreateAutomaticRollback(
                    previousConfiguration,
                    previousPreferences,
                    "before-import");

                RuntimeConfiguration imported = backup.RuntimeConfiguration;
                imported.Revision = Math.Max(DateTime.UtcNow.Ticks, previousConfiguration.Revision + 1);
                backup.UiPreferences.AllowDuplicatePoseSources = imported.AllowDuplicatePoseSources;
                backup.UiPreferences.ControllerHandSelectionPriority = imported.ControllerHandSelectionPriority;
                bool runtimeChanged = false;
                try
                {
                    await _runtimeControlService.ApplyConfigurationAsync(imported);
                    runtimeChanged = true;
                    _uiPreferencesService.Save(backup.UiPreferences);
                }
                catch
                {
                    if (runtimeChanged)
                    {
                        previousConfiguration.Revision = Math.Max(
                            DateTime.UtcNow.Ticks,
                            imported.Revision + 1);
                        await _runtimeControlService.ApplyConfigurationAsync(previousConfiguration);
                    }
                    _uiPreferencesService.Save(previousPreferences);
                    throw;
                }

                AppDialog.Show(
                    this,
                    Tr.Get("settings.files.backup.import_restart_notice"),
                    Tr.Get("settings.files.backup.import_configuration_button_click.import_complete"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                RestartApplicationAfterDataChange();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is TimeoutException ||
                exception is InvalidDataException ||
                exception is NotSupportedException ||
                exception is JsonException)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("settings.files.backup.import_configuration_button_click.import_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportConfigurationButton.IsEnabled = true;
            }
        }

        private UiPreferences CreateUiPreferencesSnapshot()
        {
            return new UiPreferences
            {
                LanguageLocale = _languageLocale,
                ShowSteamVrRoleTargets = _showSteamVrRoleTargets,
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                HideSourceInPreview = _hideSourceInPreview,
                HideTargetInPreview = _hideTargetInPreview,
                ShowProxyInPreview = _showProxyInPreview,
                RuntimeLifecycleMode = _runtimeLifecycleMode,
                FollowSteamVrWithTrackSwap = _followSteamVrWithTrackSwap
            };
        }

        private void RestartApplicationAfterDataChange()
        {
            string executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                Application.Current.Shutdown();
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--wait-for-pid " + Process.GetCurrentProcess().Id,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false
            });
            Application.Current.Shutdown();
        }

        private void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDiagnosticsView();
        }

        private void RefreshDiagnosticsView()
        {
            DiagnosticsReport report = _diagnosticsService.Inspect(_runtimeStatus);
            _diagnosticsReport = report;
            DiagnosticUiVersionText.Text = report.UiVersion;
            DiagnosticUiVersionText.Foreground = FindBrush("TextBrush");

            DiagnosticRuntimeProgramText.Text = report.RuntimeProgram;
            DiagnosticRuntimeProgramText.ToolTip = report.RuntimeProgramPath ?? Tr.Get("settings.runtime.refresh_diagnostics_view.not_found_trackswap_runtime_exe");
            DiagnosticRuntimeProgramText.Foreground = FindBrush(
                string.Equals(report.RuntimeProgram, Tr.Get("common.status.found"), StringComparison.Ordinal)
                    ? "SuccessBrush"
                    : "WarningBrush");

            DiagnosticSteamVrText.Text = report.SteamVr;
            DiagnosticSteamVrText.ToolTip = report.SteamVrPath ?? Tr.Get("settings.runtime.refresh_diagnostics_view.openvr_path_steamvr");
            DiagnosticSteamVrText.Foreground = FindBrush(
                string.IsNullOrWhiteSpace(report.SteamVrPath) ? "WarningBrush" : "TextBrush");

            DiagnosticDriverAndConfigText.Text = report.DriverRegistration + Tr.Get("settings.runtime.refresh_diagnostics_view.config") + report.Configuration;
            DiagnosticDriverAndConfigText.ToolTip = report.DriverPath ?? Tr.Get("settings.runtime.refresh_diagnostics_view.openvr_driver_trackswap");
            DiagnosticDriverAndConfigText.Foreground = FindBrush(
                string.Equals(report.DriverRegistration, Tr.Get("common.status.registered"), StringComparison.Ordinal) &&
                string.Equals(report.Configuration, Tr.Get("common.status.passed"), StringComparison.Ordinal)
                    ? "SuccessBrush"
                    : "WarningBrush");
        }

        private void UpdateDiagnosticConfigurationState(RuntimeStatusSnapshot status)
        {
            if (_diagnosticsReport == null)
            {
                return;
            }
            IReadOnlyList<string> errors = status?.Configuration == null
                ? Array.Empty<string>()
                : ConfigurationValidator.Validate(status.Configuration);
            _diagnosticsReport.ConfigurationErrors = errors;
            _diagnosticsReport.Configuration = status?.Configuration == null
                ? Tr.Get("service.diagnostics.inspect.runtime_read")
                : errors.Count == 0 ? Tr.Get("common.status.passed") : errors.Count + Tr.Get("common.count.issue_suffix");
            DiagnosticDriverAndConfigText.Text =
                _diagnosticsReport.DriverRegistration + Tr.Get("settings.runtime.refresh_diagnostics_view.config") + _diagnosticsReport.Configuration;
            DiagnosticDriverAndConfigText.Foreground = FindBrush(
                string.Equals(_diagnosticsReport.DriverRegistration, Tr.Get("common.status.registered"), StringComparison.Ordinal) &&
                string.Equals(_diagnosticsReport.Configuration, Tr.Get("common.status.passed"), StringComparison.Ordinal)
                    ? "SuccessBrush"
                    : "WarningBrush");
        }

        private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = Tr.Get("settings.runtime.export_diagnostics_button_click.export_trackswap_diagnostics_bundle"),
                Filter = Tr.Get("settings.runtime.export_diagnostics_button_click.zip_zip_zip"),
                DefaultExt = ".zip",
                AddExtension = true,
                FileName = "TrackSwap-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            ExportDiagnosticsButton.IsEnabled = false;
            try
            {
                _diagnosticsService.Export(dialog.FileName, _runtimeStatus);
                AppDialog.Show(
                    this,
                    Tr.Get("settings.runtime.export_diagnostics_button_click.diagnostics_bundle_export") + dialog.FileName,
                    Tr.Get("common.status.export_complete"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidDataException ||
                exception is NotSupportedException)
            {
                AppDialog.Show(
                    this,
                    Tr.Get("settings.runtime.export_diagnostics_button_click.cannot_export_diagnostics_bundle") + exception.Message,
                    Tr.Get("common.error.export_failed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ExportDiagnosticsButton.IsEnabled = true;
            }
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
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("route.show_steam_vr_role_targets_check_box_click.cannot_save_advanced"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PreviewModelVisibilityCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool previousHideSource = _hideSourceInPreview;
            bool previousHideTarget = _hideTargetInPreview;
            bool previousShowProxy = _showProxyInPreview;
            try
            {
                _hideSourceInPreview = HideSourceInPreviewCheckBox.IsChecked == true;
                _hideTargetInPreview = HideTargetInPreviewCheckBox.IsChecked == true;
                _showProxyInPreview = ShowProxyInPreviewCheckBox.IsChecked == true;
                SaveUiPreferences();
                if (_hideSourceInPreview && _sourcePreviewModel != null)
                {
                    HidePreviewModel(_sourcePreviewModel);
                    HidePreviewModel(_rotationSourcePreviewModel);
                }
                if (_hideTargetInPreview && _targetPreviewModel != null)
                {
                    HidePreviewModel(_targetPreviewModel);
                }
                if (!ShouldShowProxyInPreview() && _proxyPreviewModel != null)
                {
                    HidePreviewModel(_proxyPreviewModel);
                }
                _ = RefreshPreviewDeviceModelsAsync();
            }
            catch (Exception exception)
            {
                _hideSourceInPreview = previousHideSource;
                _hideTargetInPreview = previousHideTarget;
                _showProxyInPreview = previousShowProxy;
                HideSourceInPreviewCheckBox.IsChecked = previousHideSource;
                HideTargetInPreviewCheckBox.IsChecked = previousHideTarget;
                ShowProxyInPreviewCheckBox.IsChecked = previousShowProxy;
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("route.show_steam_vr_role_targets_check_box_click.cannot_save_advanced"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AllowDuplicatePoseSourcesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool previousValue = _allowDuplicatePoseSources;
            try
            {
                _allowDuplicatePoseSources = AllowDuplicatePoseSourcesCheckBox.IsChecked == true;
                SaveUiPreferences();
            }
            catch (Exception exception)
            {
                _allowDuplicatePoseSources = previousValue;
                AllowDuplicatePoseSourcesCheckBox.IsChecked = previousValue;
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("route.show_steam_vr_role_targets_check_box_click.cannot_save_advanced"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void PhysicalSourceHidingEnabledCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (_runtimeStatus == null)
            {
                PhysicalSourceHidingEnabledCheckBox.IsChecked = _physicalSourceHidingEnabled;
                AppDialog.Show(this, Tr.Get("settings.advanced.physical_source_hiding.runtime_offline"), Tr.Get("common.error.save_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool nextValue = PhysicalSourceHidingEnabledCheckBox.IsChecked == true;
            if (nextValue && AppDialog.Show(
                    this,
                    Tr.Get("settings.advanced.physical_source_hiding.confirmation"),
                    Tr.Get("settings.advanced.physical_source_hiding_enabled_check_box_click.enable_device_hide"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                PhysicalSourceHidingEnabledCheckBox.IsChecked = false;
                return;
            }

            bool previousValue = _physicalSourceHidingEnabled;
            var previousRouteValues = _workingRoutes.ToDictionary(route => route.RouteId, route => route.HidePhysicalSource);
            try
            {
                _physicalSourceHidingEnabled = nextValue;
                if (!nextValue)
                {
                    foreach (RouteConfiguration route in _workingRoutes) route.HidePhysicalSource = false;
                }
                if (_selectedRoute != null)
                {
                    HidePhysicalSourceCheckBox.Visibility = nextValue ? Visibility.Visible : Visibility.Collapsed;
                    HidePhysicalSourceCheckBox.IsChecked = _selectedRoute.HidePhysicalSource;
                }
                if (!await PersistWorkingRoutesAsync())
                {
                    _physicalSourceHidingEnabled = previousValue;
                    foreach (RouteConfiguration route in _workingRoutes)
                    {
                        if (previousRouteValues.TryGetValue(route.RouteId, out bool hidden)) route.HidePhysicalSource = hidden;
                    }
                    PhysicalSourceHidingEnabledCheckBox.IsChecked = previousValue;
                    if (_selectedRoute != null)
                    {
                        HidePhysicalSourceCheckBox.Visibility = previousValue ? Visibility.Visible : Visibility.Collapsed;
                        HidePhysicalSourceCheckBox.IsChecked = _selectedRoute.HidePhysicalSource;
                    }
                    return;
                }
            }
            catch (Exception exception)
            {
                _physicalSourceHidingEnabled = previousValue;
                foreach (RouteConfiguration route in _workingRoutes)
                {
                    if (previousRouteValues.TryGetValue(route.RouteId, out bool hidden)) route.HidePhysicalSource = hidden;
                }
                PhysicalSourceHidingEnabledCheckBox.IsChecked = previousValue;
                AppDialog.Show(this, exception.Message, Tr.Get("settings.advanced.physical_source_hiding.save_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HidePhysicalSourceCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _selectedRoute == null) return;
            if (_runtimeStatus == null)
            {
                HidePhysicalSourceCheckBox.IsChecked = _selectedRoute.HidePhysicalSource;
                AppDialog.Show(this, Tr.Get("settings.advanced.physical_source_hiding.runtime_offline"), Tr.Get("common.error.save_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool nextValue = HidePhysicalSourceCheckBox.IsChecked == true;
            DeviceOption selectedSource = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            DeviceOption selectedRotationSource = RuntimeRotationSourceComboBox.SelectedItem as DeviceOption;
            bool isHeadset = IsHeadsetSource(selectedSource, _selectedRoute.SourceDevicePath) ||
                (_selectedRoute.SplitPoseSource &&
                 IsHeadsetSource(selectedRotationSource, _selectedRoute.RotationSourceDevicePath));
            if (nextValue && isHeadset &&
                AppDialog.Show(
                    this,
                    Tr.Get("route.physical_source_hiding.hmd_confirmation"),
                    Tr.Get("route.hide_physical_source_check_box_click.hide_hmd_pose"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                HidePhysicalSourceCheckBox.IsChecked = false;
                return;
            }
            bool previousValue = _selectedRoute.HidePhysicalSource;
            _selectedRoute.HidePhysicalSource = nextValue;
            if (!await PersistWorkingRoutesAsync())
            {
                _selectedRoute.HidePhysicalSource = previousValue;
                HidePhysicalSourceCheckBox.IsChecked = previousValue;
            }
        }

        private bool IsHeadsetSource(DeviceOption source, string devicePath)
        {
            return source?.DeviceKind == TrackedDeviceKind.Hmd ||
                (_knownSourceRoleTargets.TryGetValue(devicePath ?? string.Empty, out string role) &&
                 string.Equals(role, ProtocolConstants.HeadRolePath, StringComparison.Ordinal));
        }

        private void SplitPoseSourceCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _selectedRoute == null)
            {
                return;
            }

            if (_selectedRoute.PoseSourceKind == PoseSourceKind.Manual)
            {
                SplitPoseSourceCheckBox.IsChecked = false;
                return;
            }

            bool split = SplitPoseSourceCheckBox.IsChecked == true;
            _splitBasePreviewRotation = Quaternion.Identity;
            _selectedRoute.SplitPoseSource = split;
            _selectedRoute.RotationSourceDevicePath = string.Empty;

            _isLoading = true;
            try
            {
                RuntimeRotationSourceComboBox.SelectedItem = null;
                RuntimeRotationSourceComboBox.IsEnabled = split && !_selectedRoute.PendingDeletion;
                RuntimeRotationSourceComboBox.Tag = split ? Tr.Get("common.action.select") : Tr.Get("route.rotation_source.same_as_position");
            }
            finally
            {
                _isLoading = false;
            }

            UpdateRuntimeSelectionDetails();
            _ = RefreshPreviewDeviceModelsAsync();
            ScheduleRouteAutoApply(immediate: true);
        }

        private async void ControllerHandSelectionPriorityTextBox_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            await ApplyControllerHandSelectionPriorityFromTextAsync();
        }

        private async void ControllerHandSelectionPriorityTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            await ApplyControllerHandSelectionPriorityFromTextAsync();
            Keyboard.ClearFocus();
        }

        private async void ResetControllerHandSelectionPriorityButton_Click(object sender, RoutedEventArgs e)
        {
            ControllerHandSelectionPriorityTextBox.Text =
                ProtocolConstants.DefaultControllerHandSelectionPriority.ToString(CultureInfo.InvariantCulture);
            await SetControllerHandSelectionPriorityAsync(
                ProtocolConstants.DefaultControllerHandSelectionPriority);
        }

        private async Task ApplyControllerHandSelectionPriorityFromTextAsync()
        {
            if (!int.TryParse(
                    ControllerHandSelectionPriorityTextBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int priority))
            {
                ControllerHandSelectionPriorityTextBox.Text =
                    _controllerHandSelectionPriority.ToString(CultureInfo.InvariantCulture);
                AppDialog.Show(
                    this,
                    Tr.Get("settings.advanced.controller_priority.range_error"),
                    Tr.Get("settings.advanced.apply_controller_hand_selection_priority_from_text_async.priority_invalid"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            await SetControllerHandSelectionPriorityAsync(priority);
        }

        private async Task SetControllerHandSelectionPriorityAsync(int priority)
        {
            if (priority == _controllerHandSelectionPriority)
            {
                ControllerHandSelectionPriorityTextBox.Text = priority.ToString(CultureInfo.InvariantCulture);
                return;
            }

            int previousValue = _controllerHandSelectionPriority;
            try
            {
                _controllerHandSelectionPriority = priority;
                ControllerHandSelectionPriorityTextBox.Text = priority.ToString(CultureInfo.InvariantCulture);
                SaveUiPreferences();

                if (_runtimeStatus != null)
                {
                    RuntimeConfiguration active = _runtimeStatus.Configuration ?? new RuntimeConfiguration();
                    var configuration = new RuntimeConfiguration
                    {
                        Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                        AllowDuplicatePoseSources = active.AllowDuplicatePoseSources,
                        PhysicalSourceHidingEnabled = active.PhysicalSourceHidingEnabled,
                        ControllerHandSelectionPriority = priority,
                        Routes = (active.Routes ?? new List<RouteConfiguration>())
                            .Select(CloneRoute)
                            .ToList(),
                        Osc = CloneOscConfiguration(active.Osc ?? OscConfiguration.CreateDefault()),
                        XInput = CloneXInputConfiguration(active.XInput ?? XInputConfiguration.CreateDefault())
                    };
                    await _runtimeControlService.ApplyConfigurationAsync(configuration);
                    _loadedRuntimeRevision = -1;
                    await RefreshStatusAsync();
                }
            }
            catch (Exception exception)
            {
                _controllerHandSelectionPriority = previousValue;
                ControllerHandSelectionPriorityTextBox.Text = previousValue.ToString(CultureInfo.InvariantCulture);
                SaveUiPreferences();
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("settings.advanced.set_controller_hand_selection_priority_async.cannot_save_controller_priority"),
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
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("route.show_steam_vr_role_targets_check_box_click.cannot_save_advanced"),
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
                    AppDialog.Show(
                        this,
                        Tr.Get("app.sync_steam_vr_auto_launch_async.trackswap_steamvr_start_apply") + exception.Message,
                        Tr.Get("app.sync_steam_vr_auto_launch_async.steamvr_start_settings"),
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
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("settings.advanced.lifecycle.change_failed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveUiPreferences()
        {
            _uiPreferencesService.Save(new UiPreferences
            {
                LanguageLocale = _languageLocale,
                ShowSteamVrRoleTargets = _showSteamVrRoleTargets,
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                HideSourceInPreview = _hideSourceInPreview,
                HideTargetInPreview = _hideTargetInPreview,
                ShowProxyInPreview = _showProxyInPreview,
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
                if (_destructiveCleanupInProgress || _isClosing)
                {
                    return;
                }
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
            if (_runtimeStartPending ||
                _runtimeLifecycleRestarting ||
                _destructiveCleanupInProgress ||
                _isClosing)
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
                        AppDialog.Show(this, error, Tr.Get("app.ensure_runtime_started_async.runtime_start_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            DeleteRouteMenuItem.Header = pendingDeletion ? Tr.Get("route.update_route_context_menu.cancel_delete") : Tr.Get("route.action.delete_ellipsis");
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
                AppDialog.Show(this, Tr.Get("route.rename_route_menu_item_click.config_name_cannot"), Tr.Get("route.rename_route_menu_item_click.cannot"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var ok = new Button { Content = Tr.Get("route.prompt_for_route_name.save"), IsDefault = true, MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = Tr.Get("dialog.cancel"), IsCancel = true, MinWidth = 76 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = Tr.Get("route.prompt_for_route_name.config_name"), Foreground = FindBrush("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 7) });
            panel.Children.Add(input);
            panel.Children.Add(buttons);
            var dialog = new Window
            {
                Title = Tr.Get("route.prompt_for_route_name.config"),
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
                    AppDialog.Show(dialog, Tr.Get("route.prompt_for_route_name.name_required_1_64_count"), Tr.Get("route.prompt_for_route_name.name_invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                AppDialog.Show(this, Tr.Get("route.toggle_selected_route_async.runtime_offline_cannot_delete_status_start_runtime"), Tr.Get("route.toggle_selected_route_async.cannot_delete"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool enabling = !_selectedRoute.Enabled;
            if (enabling)
            {
                if (_selectedRoute.Mode == RouteMode.ReplaceTarget &&
                    !IsAllowedRuntimeTargetPath(_selectedRoute.TargetDevicePath))
                {
                    AppDialog.Show(
                        this,
                        Tr.Get("route.validation.legacy_role_target_enable_error"),
                        Tr.Get("route.toggle_selected_route_async.required_migration_target"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                _selectedRoute.Enabled = true;
                var candidate = new RuntimeConfiguration
                {
                    Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                    AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                    PhysicalSourceHidingEnabled = _physicalSourceHidingEnabled,
                    ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                    Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList(),
                    Osc = CloneOscConfiguration(_workingOsc),
                    XInput = CloneXInputConfiguration(_workingXInput)
                };
                IReadOnlyList<string> dependencyErrors =
                    ConfigurationValidator.ValidateSourceRoleDependencies(
                        candidate,
                        _knownSourceRoleTargets);
                _selectedRoute.Enabled = false;
                if (dependencyErrors.Count != 0)
                {
                    AppDialog.Show(
                        this,
                        Tr.Get("route.toggle_selected_route_async.cannot_enable_config_item_read_item_device_role") +
                        string.Join(Environment.NewLine, dependencyErrors),
                        Tr.Get("route.toggle_selected_route_async.pose"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            if (!enabling && _selectedRoute.Mode == RouteMode.ReplaceTarget &&
                _statusService.IsRunning() && AppDialog.Show(
                    this,
                    Tr.Get("route.toggle_selected_route_async.disable_stop_output_steamvr_static_mapping_target"),
                    Tr.Get("route.toggle_selected_route_async.confirm_disable"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            _selectedRoute.Enabled = enabling;
            ToggleSelectedRouteButton.Content = _selectedRoute.Enabled ? Tr.Get("common.action.disable") : Tr.Get("common.action.enable");
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
                _selectedRoute.PendingDeletion = false;
                await PersistWorkingRoutesAsync();
                return;
            }

            bool steamVrRunning = _statusService.IsRunning();
            string selectedProxyPath = ProtocolConstants.GetProxyDevicePath(_selectedRoute.VirtualDeviceSlot);
            bool hasStaticMapping = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath) &&
                _settingsService.ReadOverrides(_settingsPath).Any(mapping =>
                    string.Equals(mapping.SourcePath, selectedProxyPath, StringComparison.Ordinal));
            if (!hasStaticMapping)
            {
                string outputName = _selectedRoute.Mode == RouteMode.VirtualController
                    ? Tr.Get("route.delete_route_menu_item_click.virtual_controller")
                    : _selectedRoute.Mode == RouteMode.VirtualHmd
                        ? Tr.Get("route.delete_route_menu_item_click.virtual_hmd")
                    : _selectedRoute.Mode == RouteMode.DirectProxy
                        ? Tr.Get("route.delete_route_menu_item_click.virtual_tracker")
                        : Tr.Get("route.delete_route_menu_item_click.tracker");
                if (AppDialog.Show(
                        this,
                        Tr.Get("route.delete_route_menu_item_click.ok_delete_config") + _selectedRoute.Name + Tr.Get("route.delete.target_mapping_fragment") + outputName + Tr.Get("route.delete_route_menu_item_click.stop_output"),
                        Tr.Get("route.action.delete"),
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning) != MessageBoxResult.OK)
                {
                    return;
                }

                RouteConfiguration removedRoute = _selectedRoute;
                _workingRoutes.Remove(removedRoute);
                _selectedRoute = _workingRoutes.OrderBy(route => route.VirtualDeviceSlot).FirstOrDefault();
                await PersistWorkingRoutesAsync();
                return;
            }

            string message = steamVrRunning
                ? Tr.Get("route.delete.pending_cleanup_notice")
                : Tr.Get("route.delete_route_menu_item_click.ok_delete_config") + _selectedRoute.Name + Tr.Get("route.delete_route_menu_item_click.trackswap_cleanup");
            if (AppDialog.Show(
                    this,
                    message,
                    steamVrRunning ? Tr.Get("route.delete_route_menu_item_click.delete") : Tr.Get("route.action.delete"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            _selectedRoute.PendingDeletion = true;
            await PersistWorkingRoutesAsync();
        }

        private async Task<bool> PersistWorkingRoutesAsync()
        {
            if (_runtimeStatus == null)
            {
                RefreshRouteList(_selectedRoute?.RouteId);
                return false;
            }
            var configuration = new RuntimeConfiguration
            {
                Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                PhysicalSourceHidingEnabled = _physicalSourceHidingEnabled,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList(),
                Osc = CloneOscConfiguration(_workingOsc),
                XInput = CloneXInputConfiguration(_workingXInput)
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                AppDialog.Show(this, string.Join(Environment.NewLine, errors), Tr.Get("route.validation.invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _loadedRuntimeRevision = -1;
                _runtimeEditorInitialized = false;
                await RefreshStatusAsync();
                return true;
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("route.persist_working_routes_async.save_config_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void RuntimeSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoading)
            {
                if (_selectedRoute != null)
                {
                    PoseSourceKind previousSourceKind = _selectedRoute.PoseSourceKind;
                    DeviceOption selectedSource = RuntimeSourceComboBox.SelectedItem as DeviceOption;
                    PoseSourceKind nextSourceKind = selectedSource?.PoseSourceKind ?? PoseSourceKind.Device;
                    if (previousSourceKind != nextSourceKind &&
                        TryReadOffset(out PoseOffset visiblePose, out _))
                    {
                        if (previousSourceKind == PoseSourceKind.Manual)
                        {
                            _selectedRoute.ManualPose = visiblePose;
                        }
                        else
                        {
                            _selectedRoute.Offset = visiblePose;
                        }
                    }
                    string nextSourcePath =
                        nextSourceKind == PoseSourceKind.Device ? selectedSource?.DevicePath : string.Empty;
                    if (nextSourceKind == PoseSourceKind.Manual)
                    {
                        _selectedRoute.SplitPoseSource = false;
                        _selectedRoute.HidePhysicalSource = false;
                        SplitPoseSourceCheckBox.IsChecked = false;
                        HidePhysicalSourceCheckBox.IsChecked = false;
                    }
                    string nextRotationSourcePath = _selectedRoute.SplitPoseSource &&
                        nextSourceKind == PoseSourceKind.Device
                        ? (RuntimeRotationSourceComboBox.SelectedItem as DeviceOption)?.DevicePath
                        : string.Empty;
                    if (_selectedRoute.PoseSourceKind != nextSourceKind ||
                        !string.Equals(_selectedRoute.SourceDevicePath, nextSourcePath, StringComparison.Ordinal) ||
                        !string.Equals(
                            _selectedRoute.RotationSourceDevicePath ?? string.Empty,
                            nextRotationSourcePath ?? string.Empty,
                            StringComparison.Ordinal))
                    {
                        _selectedRoute.HidePhysicalSource = false;
                        HidePhysicalSourceCheckBox.IsChecked = false;
                    }
                    _selectedRoute.PoseSourceKind = nextSourceKind;
                    _selectedRoute.SourceDevicePath = nextSourcePath;
                    _selectedRoute.RotationSourceDevicePath = nextRotationSourcePath;
                    if (previousSourceKind != nextSourceKind)
                    {
                        _isLoading = true;
                        try
                        {
                            LoadOffsetFields(nextSourceKind == PoseSourceKind.Manual
                                ? _selectedRoute.ManualPose ?? PoseOffset.DefaultManualPose()
                                : _selectedRoute.Offset ?? PoseOffset.Identity());
                        }
                        finally
                        {
                            _isLoading = false;
                        }
                    }
                    if (_selectedRoute.Mode == RouteMode.ReplaceTarget)
                    {
                        _selectedRoute.TargetDevicePath =
                            (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                    }
                    if (_selectedRoute.Mode == RouteMode.VirtualController)
                    {
                        _selectedRoute.ControllerHand =
                            (RuntimeControllerHandComboBox.SelectedItem as ControllerHandOption)?.Hand ?? ControllerHand.None;
                        _selectedRoute.ControlInputSource =
                            (RuntimeControlInputComboBox.SelectedItem as ControlInputOption)?.Source ?? ControlInputSource.None;
                        RuntimeControllerOutputText.Text = string.IsNullOrWhiteSpace(
                            ProtocolConstants.GetControllerSerial(_selectedRoute.ControllerHand))
                            ? Tr.Get("route.controller.select_hand")
                            : ProtocolConstants.GetControllerSerial(_selectedRoute.ControllerHand);
                        SelectedProxyText.Text = string.IsNullOrWhiteSpace(
                            ProtocolConstants.GetControllerSerial(_selectedRoute.ControllerHand))
                            ? ProtocolConstants.GetOutputSerial(_selectedRoute.Mode, _selectedRoute.VirtualDeviceSlot)
                            : ProtocolConstants.GetControllerSerial(_selectedRoute.ControllerHand);
                    }
                }
                UpdateRuntimeSelectionDetails();
                _ = RefreshPreviewDeviceModelsAsync();
                ScheduleRouteAutoApply(immediate: true);
            }
        }

        private void RuntimeOffsetTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _isClosing || _selectedRoute == null)
            {
                return;
            }

            if (!TryReadOffset(out PoseOffset offset, out string error))
            {
                _routeAutoApplyTimer.Stop();
                SetRouteAutoApplyIssue(Tr.Get("route.pose_offset.invalid_format"), error);
                return;
            }

            if (_selectedRoute.PoseSourceKind == PoseSourceKind.Manual)
            {
                _selectedRoute.ManualPose = offset;
            }
            else
            {
                _selectedRoute.Offset = offset;
            }
            ClearRouteAutoApplyIssue();
            UpdateRuntimeSelectionDetails();
            if (_gizmoPreviewOverrideActive)
            {
                return;
            }
            ScheduleRouteAutoApply(immediate: false);
        }

        private void RuntimeOffsetTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox field)
            {
                e.Handled = !IsPermittedOffsetText(BuildProspectiveText(field, e.Text));
            }
        }

        private void RuntimeOffsetTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!(sender is TextBox field) ||
                !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            string pastedText = e.SourceDataObject.GetData(DataFormats.UnicodeText, true) as string;
            if (!IsPermittedOffsetText(BuildProspectiveText(field, pastedText ?? string.Empty)))
            {
                e.CancelCommand();
            }
        }

        private TextBox[] GetOffsetTextBoxes()
        {
            return new[]
            {
                OffsetTranslationXTextBox, OffsetTranslationYTextBox, OffsetTranslationZTextBox,
                OffsetRotationXTextBox, OffsetRotationYTextBox, OffsetRotationZTextBox,
                OffsetRotationWTextBox
            };
        }

        private static string BuildProspectiveText(TextBox field, string insertedText)
        {
            string current = field.Text ?? string.Empty;
            int selectionStart = Math.Max(0, Math.Min(field.SelectionStart, current.Length));
            int selectionLength = Math.Max(
                0,
                Math.Min(field.SelectionLength, current.Length - selectionStart));
            return current.Remove(selectionStart, selectionLength)
                .Insert(selectionStart, insertedText ?? string.Empty);
        }

        private static bool IsPermittedOffsetText(string text)
        {
            bool decimalPointSeen = false;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character >= '0' && character <= '9')
                {
                    continue;
                }
                if (character == '.' && !decimalPointSeen)
                {
                    decimalPointSeen = true;
                    continue;
                }
                if (character == '-' && index == 0)
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private void ScheduleRouteAutoApply(bool immediate)
        {
            if (_isLoading || _isClosing || _selectedRoute == null || _selectedRoute.PendingDeletion)
            {
                return;
            }
            ClearRouteAutoApplyIssue();
            _routeAutoApplyTimer.Stop();
            _routeAutoApplyTimer.Interval = immediate
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromMilliseconds(450);
            _routeAutoApplyTimer.Start();
        }

        private async Task ApplyRouteEditorAutomaticallyAsync()
        {
            if (_routeAutoApplyBusy)
            {
                _routeAutoApplyQueued = true;
                return;
            }
            if (_runtimeStatus == null)
            {
                _routeAutoApplyQueued = true;
                return;
            }
            if (!IsRouteEditorComplete())
            {
                return;
            }
            if (!TryReadOffset(out PoseOffset offset, out string error))
            {
                SetRouteAutoApplyIssue(Tr.Get("route.pose_offset.invalid_format"), error);
                return;
            }

            _selectedRoute.Offset = offset;
            _routeAutoApplyBusy = true;
            _routeAutoApplyQueued = false;
            try
            {
                SelectedRouteSyncText.Text = Tr.Get("route.apply_route_editor_automatically_async.in_progress_apply");
                SelectedRouteSyncText.Foreground = FindBrush("WarningBrush");
                SelectedRouteSyncText.ToolTip = null;
                await ApplyRuntimeConfigurationAsync(showErrors: false, confirmSourceSwitch: false);
            }
            finally
            {
                _routeAutoApplyBusy = false;
                if (_routeAutoApplyQueued)
                {
                    _routeAutoApplyQueued = false;
                    ScheduleRouteAutoApply(immediate: true);
                }
            }
        }

        private bool IsRouteEditorComplete()
        {
            if (_selectedRoute == null || _selectedRoute.PendingDeletion ||
                !(RuntimeModeComboBox.SelectedItem is RouteModeOption) ||
                !(RuntimeSourceComboBox.SelectedItem is DeviceOption source) ||
                (source.PoseSourceKind == PoseSourceKind.Manual && _selectedRoute.SplitPoseSource))
            {
                return false;
            }
            if (_selectedRoute.SplitPoseSource &&
                !(RuntimeRotationSourceComboBox.SelectedItem is DeviceOption))
            {
                return false;
            }
            if (_selectedRoute.Mode == RouteMode.ReplaceTarget)
            {
                return RuntimeTargetComboBox.SelectedItem is TargetOption target &&
                    IsAllowedRuntimeTarget(target);
            }
            if (_selectedRoute.Mode == RouteMode.VirtualController)
            {
                return RuntimeControllerHandComboBox.SelectedItem is ControllerHandOption &&
                    RuntimeControlInputComboBox.SelectedItem is ControlInputOption;
            }
            return true;
        }

        private void SetRouteAutoApplyIssue(string text, string detail)
        {
            _routeAutoApplyIssueText = text;
            _routeAutoApplyIssueDetail = detail;
            SelectedRouteSyncText.Text = text;
            SelectedRouteSyncText.Foreground = FindBrush("WarningBrush");
            SelectedRouteSyncText.ToolTip = detail;
        }

        private void ClearRouteAutoApplyIssue()
        {
            _routeAutoApplyIssueText = null;
            _routeAutoApplyIssueDetail = null;
        }

        private void RuntimeModeSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || _selectedRoute == null ||
                !(RuntimeModeComboBox.SelectedItem is RouteModeOption option))
            {
                return;
            }

            if (_selectedRoute.Mode != option.Mode)
            {
                if (TryReadOffset(out PoseOffset visiblePose, out _))
                {
                    if (_selectedRoute.PoseSourceKind == PoseSourceKind.Manual)
                    {
                        _selectedRoute.ManualPose = visiblePose;
                    }
                    else
                    {
                        _selectedRoute.Offset = visiblePose;
                    }
                }
                _selectedRoute.Mode = option.Mode;
                _selectedRoute.TargetDevicePath = null;
                _selectedRoute.ControllerHand = ControllerHand.None;
                _selectedRoute.ControlInputSource = ControlInputSource.None;
                RuntimeTargetComboBox.SelectedItem = null;
                RuntimeControllerHandComboBox.SelectedItem = null;
                RuntimeControlInputComboBox.SelectedItem = null;
                SelectedProxyText.Text = option.Mode == RouteMode.VirtualController
                    ? Tr.Get("route.controller.select_hand")
                    : ProtocolConstants.GetOutputSerial(option.Mode, _selectedRoute.VirtualDeviceSlot);
                RuntimeProxyText.Text = ProtocolConstants.GetOutputSerial(option.Mode, _selectedRoute.VirtualDeviceSlot);
                RuntimeControllerOutputText.Text = Tr.Get("route.controller.select_hand");

                _isLoading = true;
                try
                {
                    IReadOnlyList<DeviceOption> sourceChoices = BuildDeviceChoices(
                        _knownPhysicalDevices,
                        _selectedRoute.SourceDevicePath,
                        _selectedRoute.PoseSourceKind,
                        includeManual: true);
                    RuntimeSourceComboBox.ItemsSource = sourceChoices;
                    RuntimeSourceComboBox.SelectedItem = sourceChoices.FirstOrDefault(candidate =>
                        candidate.PoseSourceKind == _selectedRoute.PoseSourceKind &&
                        (_selectedRoute.PoseSourceKind == PoseSourceKind.Manual ||
                         string.Equals(candidate.DevicePath, _selectedRoute.SourceDevicePath, StringComparison.Ordinal)));
                }
                finally
                {
                    _isLoading = false;
                }
            }
            UpdateRuntimeModePresentation();
            UpdateRuntimeSelectionDetails();
            _ = RefreshPreviewDeviceModelsAsync();
            ScheduleRouteAutoApply(immediate: true);
        }

        private void UpdateRuntimeModePresentation()
        {
            bool modeSelected = _selectedRoute?.Mode == RouteMode.DirectProxy ||
                _selectedRoute?.Mode == RouteMode.ReplaceTarget ||
                _selectedRoute?.Mode == RouteMode.VirtualController ||
                _selectedRoute?.Mode == RouteMode.VirtualHmd;
            bool replacesTarget = _selectedRoute?.Mode == RouteMode.ReplaceTarget;
            bool virtualController = _selectedRoute?.Mode == RouteMode.VirtualController;
            Visibility virtualHmdActionVisibility = _selectedRoute?.Mode == RouteMode.VirtualHmd
                ? Visibility.Visible
                : Visibility.Collapsed;
            OpenSteamVrRoomSetupButton.Visibility = virtualHmdActionVisibility;
            OpenVrViewButton.Visibility = virtualHmdActionVisibility;
            RuntimeOutputPanel.Visibility = modeSelected && !virtualController
                ? Visibility.Visible
                : Visibility.Collapsed;
            RuntimeTargetPanel.Visibility = replacesTarget ? Visibility.Visible : Visibility.Collapsed;
            RuntimeTargetArrow.Visibility = replacesTarget ? Visibility.Visible : Visibility.Collapsed;
            RuntimeTargetColumn.Width = replacesTarget ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumnSpan(RuntimeProxyPanel, replacesTarget ? 1 : 3);
            RuntimeControllerPanel.Visibility = virtualController ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OpenVrViewButton_Click(object sender, RoutedEventArgs e)
        {
            string viewerPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "driver",
                "trackswap",
                "bin",
                "win64",
                "TrackSwap.Viewer.exe");
            if (!File.Exists(viewerPath))
            {
                AppDialog.Show(
                    this,
                    Tr.Get("app.open_vr_view_button_click.not_found_trackswap_vr_view_complete_version"),
                    Tr.Get("viewer.open_failed_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = viewerPath,
                    Arguments = BuildVrViewerLocalizationArguments(),
                    WorkingDirectory = Path.GetDirectoryName(viewerPath),
                    UseShellExecute = false
                });
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("viewer.open_failed_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildVrViewerLocalizationArguments()
        {
            var entries = new[]
            {
                new[] { "title", Tr.Get("viewer.title") },
                new[] { "both", Tr.Get("viewer.eye.both") },
                new[] { "left", Tr.Get("viewer.eye.left") },
                new[] { "right", Tr.Get("viewer.eye.right") },
                new[] { "topmost", Tr.Get("viewer.topmost") },
                new[] { "screenshot", Tr.Get("viewer.screenshot") },
                new[] { "fullscreen", Tr.Get("viewer.fullscreen") },
                new[] { "waiting-display", Tr.Get("viewer.waiting_display") },
                new[] { "waiting-frame", Tr.Get("viewer.waiting_frame") },
                new[] { "status", Tr.Get("viewer.status") },
                new[] { "size-hd", Tr.Get("viewer.size.hd") },
                new[] { "size-full-hd", Tr.Get("viewer.size.full_hd") },
                new[] { "size-quad-hd", Tr.Get("viewer.size.quad_hd") },
                new[] { "size-source", Tr.Get("viewer.size.source") },
                new[] { "size-follow", Tr.Get("viewer.size.follow") },
                new[] { "png-filter", Tr.Get("viewer.png_filter") }
            };
            return string.Join(" ", entries.Select(entry =>
                "--loc-" + entry[0] + " " +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(entry[1]))));
        }

        private void OpenSteamVrRoomSetupButton_Click(object sender, RoutedEventArgs e)
        {
            string runtimePath = _pathService.FindRuntimePath();
            string roomSetupPath = string.IsNullOrWhiteSpace(runtimePath)
                ? null
                : Path.Combine(
                    runtimePath,
                    "tools",
                    "steamvr_room_setup",
                    "win64",
                    "steamvr_room_setup.exe");
            if (string.IsNullOrWhiteSpace(roomSetupPath) || !File.Exists(roomSetupPath))
            {
                AppDialog.Show(
                    this,
                    Tr.Get("settings.steamvr.room_setup.not_found"),
                    Tr.Get("steamvr.room_setup.open_failed_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = roomSetupPath,
                    WorkingDirectory = Path.GetDirectoryName(roomSetupPath),
                    UseShellExecute = false
                });
            }
            catch (Exception exception)
            {
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("steamvr.room_setup.open_failed_title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool ShouldShowProxyInPreview()
        {
            return _showProxyInPreview || _selectedRoute?.Mode == RouteMode.DirectProxy ||
                _selectedRoute?.Mode == RouteMode.VirtualController ||
                _selectedRoute?.Mode == RouteMode.VirtualHmd;
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

        private async void StartRuntimeButton_Click(object sender, RoutedEventArgs e)
        {
            StartRuntimeButton.IsEnabled = false;
            await EnsureRuntimeStartedAsync(showError: true);
            await RefreshStatusAsync();
        }

        private void ResetRuntimeOffsetButton_Click(object sender, RoutedEventArgs e)
        {
            LoadOffsetFields(_selectedRoute?.PoseSourceKind == PoseSourceKind.Manual
                ? PoseOffset.DefaultManualPose()
                : PoseOffset.Identity());
            ScheduleRouteAutoApply(immediate: true);
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

        private async Task ApplyRuntimeConfigurationAsync(
            bool showErrors = true,
            bool confirmSourceSwitch = true)
        {
            DeviceOption source = RuntimeSourceComboBox.SelectedItem as DeviceOption;
            DeviceOption rotationSource = RuntimeRotationSourceComboBox.SelectedItem as DeviceOption;
            TargetOption target = RuntimeTargetComboBox.SelectedItem as TargetOption;
            bool modeSelected = RuntimeModeComboBox.SelectedItem is RouteModeOption;
            bool replacesTarget = _selectedRoute?.Mode == RouteMode.ReplaceTarget;
            bool virtualController = _selectedRoute?.Mode == RouteMode.VirtualController;
            bool manualPose = source?.PoseSourceKind == PoseSourceKind.Manual;
            ControllerHandOption hand = RuntimeControllerHandComboBox.SelectedItem as ControllerHandOption;
            ControlInputOption input = RuntimeControlInputComboBox.SelectedItem as ControlInputOption;
            if (_runtimeStatus == null || _selectedRoute == null || !modeSelected ||
                source == null ||
                (manualPose && _selectedRoute.SplitPoseSource) ||
                (_selectedRoute.SplitPoseSource && rotationSource == null) ||
                (replacesTarget && target == null) ||
                (virtualController && (hand == null || input == null)))
            {
                if (showErrors)
                {
                    AppDialog.Show(this, Tr.Get("route.apply_runtime_configuration_async.runtime_connect_select_complete"), Tr.Get("common.error.apply_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (_runtimeStatus == null)
                {
                    _routeAutoApplyQueued = true;
                }
                return;
            }

            if (replacesTarget && !IsAllowedRuntimeTarget(target))
            {
                if (showErrors)
                {
                    AppDialog.Show(
                        this,
                        Tr.Get("route.validation.hidden_steamvr_role_target"),
                        Tr.Get("route.toggle_selected_route_async.required_migration_target"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    string message = Tr.Get("route.validation.physical_device_required");
                    SetRouteAutoApplyIssue(Tr.Get("route.toggle_selected_route_async.required_migration_target"), message);
                    AppDialog.Show(this, message, Tr.Get("route.validation.invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (replacesTarget &&
                ((!string.IsNullOrWhiteSpace(source.RoleTargetPath) &&
                  string.Equals(source.RoleTargetPath, target.TargetPath, StringComparison.Ordinal)) ||
                 (_selectedRoute.SplitPoseSource &&
                  !string.IsNullOrWhiteSpace(rotationSource?.RoleTargetPath) &&
                  string.Equals(rotationSource.RoleTargetPath, target.TargetPath, StringComparison.Ordinal))))
            {
                if (showErrors)
                {
                    AppDialog.Show(
                        this,
                        Tr.Get("route.validation.role_feedback_loop") +
                        Tr.Get("route.apply_runtime_configuration_async.select_tracker_controller_pose_source"),
                        Tr.Get("route.validation.self_reference_detected"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    string message = Tr.Get("route.validation.source_role_conflict");
                    SetRouteAutoApplyIssue(Tr.Get("route.validation.invalid"), message);
                    AppDialog.Show(this, message, Tr.Get("route.validation.invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (!TryReadOffset(out PoseOffset offset, out string parseError))
            {
                if (showErrors)
                {
                    AppDialog.Show(this, parseError, Tr.Get("route.pose_offset.invalid_format"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    SetRouteAutoApplyIssue(Tr.Get("route.pose_offset.invalid_format"), parseError);
                }
                return;
            }

            long revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1);
            RouteConfiguration activeRoute = _runtimeStatus.Configuration?.Routes?.FirstOrDefault(candidate =>
                string.Equals(candidate.RouteId, _selectedRoute.RouteId, StringComparison.Ordinal));
            bool positionSourceChanged = activeRoute != null &&
                (activeRoute.PoseSourceKind != source.PoseSourceKind ||
                 !string.Equals(activeRoute.SourceDevicePath, source.DevicePath, StringComparison.Ordinal));
            bool rotationSourceChanged = activeRoute != null && _selectedRoute.SplitPoseSource &&
                !string.Equals(
                    activeRoute.RotationSourceDevicePath,
                    rotationSource.DevicePath,
                    StringComparison.Ordinal);
            if (confirmSourceSwitch && activeRoute != null &&
                (positionSourceChanged || rotationSourceChanged))
            {
                MessageBoxResult switchResult = AppDialog.Show(
                    this,
                    Tr.Get("route.apply_runtime_configuration_async.pose_switch_pose_source") +
                    (positionSourceChanged
                        ? Tr.Get("route.apply_runtime_configuration_async.position") + DescribePoseSource(activeRoute.PoseSourceKind, activeRoute.SourceDevicePath) +
                            "\n→ " + DescribePoseSource(source.PoseSourceKind, source.DevicePath) + "\n\n"
                        : string.Empty) +
                    (rotationSourceChanged
                        ? Tr.Get("route.apply_runtime_configuration_async.rotation") + activeRoute.RotationSourceDevicePath + "\n→ " + rotationSource.DevicePath
                        : string.Empty),
                    Tr.Get("route.apply_runtime_configuration_async.confirm_switch_pose_source"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (switchResult != MessageBoxResult.OK)
                {
                    return;
                }
            }

            _selectedRoute.PoseSourceKind = source.PoseSourceKind;
            _selectedRoute.SourceDevicePath = manualPose ? string.Empty : source.DevicePath;
            _selectedRoute.RotationSourceDevicePath = _selectedRoute.SplitPoseSource
                ? rotationSource.DevicePath
                : string.Empty;
            if (replacesTarget)
            {
                _selectedRoute.TargetDevicePath = target.TargetPath;
            }
            if (virtualController)
            {
                _selectedRoute.ControllerHand = hand.Hand;
                _selectedRoute.ControlInputSource = input.Source;
            }
            if (manualPose)
            {
                _selectedRoute.ManualPose = offset;
                _selectedRoute.HidePhysicalSource = false;
            }
            else
            {
                _selectedRoute.Offset = offset;
            }
            var configuration = new RuntimeConfiguration
            {
                Revision = revision,
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                PhysicalSourceHidingEnabled = _physicalSourceHidingEnabled,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                Routes = _workingRoutes.Select(CloneRoute).ToList(),
                Osc = CloneOscConfiguration(_workingOsc),
                XInput = CloneXInputConfiguration(_workingXInput)
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            errors = errors
                .Concat(ConfigurationValidator.ValidateSourceRoleDependencies(
                    configuration,
                    _knownSourceRoleTargets))
                .ToList();
            if (errors.Count != 0)
            {
                if (showErrors)
                {
                    AppDialog.Show(
                        this,
                        errors.Any(error => error.IndexOf(Tr.Get("route.apply_runtime_configuration_async.config_pose"), StringComparison.Ordinal) >= 0)
                            ? Tr.Get("route.apply_runtime_configuration_async.cannot_apply_config_item_read_item_device_role") +
                                string.Join(Environment.NewLine, errors)
                            : string.Join(Environment.NewLine, errors),
                        Tr.Get("route.apply_runtime_configuration_async.config_invalid"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    string message = string.Join(Environment.NewLine, errors);
                    SetRouteAutoApplyIssue(Tr.Get("route.validation.invalid"), message);
                    AppDialog.Show(this, message, Tr.Get("route.validation.invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            RuntimeAppliedStateText.Text = Tr.Get("route.apply_runtime_configuration_async.in_progress_submit");
            RuntimeAppliedStateText.Foreground = FindBrush("WarningBrush");
            try
            {
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _loadedRuntimeRevision = -1;
                _runtimeEditorInitialized = false;
                await RefreshStatusAsync();
                if (_statusService.IsRunning())
                {
                    IReadOnlyList<TrackingOverrideOption> overrides = string.IsNullOrWhiteSpace(_settingsPath)
                        ? Array.Empty<TrackingOverrideOption>()
                        : _settingsService.ReadOverrides(_settingsPath);
                    string proxyPath = ProtocolConstants.GetProxyDevicePath(_selectedRoute.VirtualDeviceSlot);
                    TrackingOverrideOption proxyMapping = overrides.FirstOrDefault(mapping =>
                        string.Equals(mapping.SourcePath, proxyPath, StringComparison.Ordinal));
                    bool staticStateReady = replacesTarget
                        ? proxyMapping != null && string.Equals(proxyMapping.TargetPath, target.TargetPath, StringComparison.Ordinal)
                        : proxyMapping == null;
                    if (_selectedRoute.Mode == RouteMode.VirtualHmd)
                    {
                        staticStateReady = staticStateReady &&
                            _settingsService.ReadVirtualHmdEnabled(_settingsPath) == _selectedRoute.Enabled;
                    }
                    if (!staticStateReady)
                    {
                        RuntimeRouteChangeWarningText.Text = _selectedRoute.Mode == RouteMode.VirtualHmd
                            ? Tr.Get("route.apply_runtime_configuration_async.virtual_hmd_config_save_exit_start_steamvr")
                            : replacesTarget
                            ? Tr.Get("route.apply_runtime_configuration_async.config_save_exit_steamvr_automatic_write_static_mapping")
                            : Tr.Get("route.status.static_mapping_removal_pending");
                        RuntimeRouteChangeWarningText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception exception)
            {
                if (showErrors)
                {
                    AppDialog.Show(this, exception.Message, Tr.Get("route.apply_runtime_configuration_async.apply_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else if (_selectedRoute != null)
                {
                    SetRouteAutoApplyIssue(Tr.Get("route.apply_runtime_configuration_async.automatic_apply_failed"), exception.Message);
                }
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
            TextBox[] fields = GetOffsetTextBoxes();
            var values = new double[fields.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!TryParseNumber(fields[index].Text, out values[index]))
                {
                    error = Tr.Get("route.preview.try_read_offset.offset_required");
                    return false;
                }
            }

            double quaternionLength = Math.Sqrt(
                (values[3] * values[3]) + (values[4] * values[4]) +
                (values[5] * values[5]) + (values[6] * values[6]));
            if (quaternionLength < 1e-6)
            {
                error = Tr.Get("route.preview.try_read_offset.rotation_quaternion_cannot");
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
                AppDialog.Show(this, Tr.Get("app.apply_button_click.exit_steamvr_exit_config"), Tr.Get("steamvr.status.running"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                AppDialog.Show(
                    this,
                    Tr.Get("app.apply_button_click.steamvr_role_target_select_count_online_physical_device_target"),
                    Tr.Get("app.apply_button_click.required_device_target"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string validationError = _settingsService.ValidateOverride(_settingsPath, source.DevicePath, target.TargetPath);
            if (validationError != null)
            {
                AppDialog.Show(this, validationError, Tr.Get("common.error.apply_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult result = AppDialog.Show(
                this,
                Tr.Get("app.apply_button_click.down_pose_mapping") + source.DevicePath + "\n→ " + target.TargetPath + Tr.Get("app.apply_button_click.apply_automatic_backup_raw_config"),
                Tr.Get("app.apply_button_click.confirm_apply"),
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
                AppDialog.Show(this, exception.Message, Tr.Get("app.apply_button_click.apply_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                AppDialog.Show(this, Tr.Get("settings.steamvr.edit_override_button_click.cannot_current_device_item_steamvr_load"), Tr.Get("settings.steamvr.edit_override_button_click.device_available"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                AppDialog.Show(this, Tr.Get("settings.steamvr.remove_override_button_click.exit_steamvr_remove"), Tr.Get("steamvr.status.running"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TrackingOverrideOption mapping = (sender as Button)?.Tag as TrackingOverrideOption;
            if (mapping == null)
            {
                return;
            }

            MessageBoxResult result = AppDialog.Show(
                this,
                Tr.Get("settings.steamvr.remove_override_button_click.ok_remove_down_pose_mapping") + mapping.SourcePath + "\n→ " + mapping.TargetPath + Tr.Get("settings.steamvr.remove_override_button_click.remove_automatic_backup_raw_config"),
                Tr.Get("settings.steamvr.remove_override_button_click.confirm_remove"),
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
                AppDialog.Show(this, exception.Message, Tr.Get("settings.steamvr.remove_override_button_click.remove_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializePosePreview()
        {
            _sourcePreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#F5A623"), TrackedDeviceKind.Unknown);
            _rotationSourcePreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#C084FC"), TrackedDeviceKind.Unknown);
            _proxyPreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#5A9BFF"), TrackedDeviceKind.Tracker);
            _targetPreviewModel = CreateDeviceModel((Color)ColorConverter.ConvertFromString("#45D483"), TrackedDeviceKind.Unknown);
            _gizmoPreviewModel = new Model3DGroup();
            PreviewSceneRoot.Children.Add(_sourcePreviewModel);
            PreviewSceneRoot.Children.Add(_rotationSourcePreviewModel);
            PreviewSceneRoot.Children.Add(_proxyPreviewModel);
            PreviewSceneRoot.Children.Add(_targetPreviewModel);
            PreviewSceneRoot.Children.Add(_gizmoPreviewModel);
            HidePreviewModel(_sourcePreviewModel);
            HidePreviewModel(_rotationSourcePreviewModel);
            HidePreviewModel(_proxyPreviewModel);
            HidePreviewModel(_targetPreviewModel);
            RebuildGizmoModel();
            HidePreviewModel(_gizmoPreviewModel);
            UpdateGizmoModeButtons();
            UpdatePreviewCamera();
        }

        private void GizmoHiddenButton_Click(object sender, RoutedEventArgs e)
        {
            SetGizmoMode(GizmoMode.None);
        }

        private void GizmoTranslateButton_Click(object sender, RoutedEventArgs e)
        {
            SetGizmoMode(GizmoMode.Translate);
        }

        private void GizmoRotateButton_Click(object sender, RoutedEventArgs e)
        {
            SetGizmoMode(GizmoMode.Rotate);
        }

        private void SetGizmoMode(GizmoMode mode)
        {
            if (_gizmoMode == mode)
            {
                return;
            }

            EndPreviewDrag();
            _gizmoMode = mode;
            RebuildGizmoModel();
            if (mode != GizmoMode.None &&
                _selectedRoute != null &&
                TryReadOffset(out PoseOffset offset, out _))
            {
                Matrix3D matrix = CreateConfiguredOutputMatrix(offset);
                matrix.Translate(_previewCenterOffset);
                _gizmoWorldTransform = matrix;
                _gizmoVisible = true;
                _gizmoPreviewModel.Transform = new MatrixTransform3D(matrix);
            }
            else
            {
                _gizmoVisible = false;
                HidePreviewModel(_gizmoPreviewModel);
            }
            UpdateGizmoModeButtons();
        }

        private void UpdateGizmoModeButtons()
        {
            GizmoHiddenButton.Background = FindBrush(
                _gizmoMode == GizmoMode.None ? "AccentSoftBrush" : "SurfaceRaisedBrush");
            GizmoTranslateButton.Background = FindBrush(
                _gizmoMode == GizmoMode.Translate ? "AccentSoftBrush" : "SurfaceRaisedBrush");
            GizmoRotateButton.Background = FindBrush(
                _gizmoMode == GizmoMode.Rotate ? "AccentSoftBrush" : "SurfaceRaisedBrush");
        }

        private void PosePreviewViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
            {
                return;
            }

            Point pointer = e.GetPosition(PosePreviewViewport);
            if (e.ChangedButton == MouseButton.Left && TryBeginGizmoDrag(pointer))
            {
                PreviewInteractionSurface.CaptureMouse();
                PreviewInteractionSurface.Cursor = Cursors.Hand;
                e.Handled = true;
                return;
            }

            _previewDragButton = e.ChangedButton;
            _previewLastPointer = pointer;
            PreviewInteractionSurface.CaptureMouse();
            PreviewInteractionSurface.Cursor = e.ChangedButton == MouseButton.Left
                ? Cursors.SizeAll
                : Cursors.ScrollAll;
            e.Handled = true;
        }

        private void PosePreviewViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!PreviewInteractionSurface.IsMouseCaptured)
            {
                return;
            }

            Point current = e.GetPosition(PosePreviewViewport);
            if (_gizmoDragAxis != GizmoAxis.None)
            {
                UpdateGizmoDrag(current, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                return;
            }

            if (_previewDragButton == null)
            {
                return;
            }

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

        private async void PosePreviewViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _gizmoDragAxis != GizmoAxis.None)
            {
                EndPreviewDrag();
                try
                {
                    await ApplyRouteEditorAutomaticallyAsync();
                }
                finally
                {
                    _gizmoPreviewOverrideActive = false;
                }
                e.Handled = true;
                return;
            }

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

        private async void PosePreviewViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            bool interruptedGizmoDrag = _gizmoDragAxis != GizmoAxis.None;
            _previewDragButton = null;
            _gizmoDragAxis = GizmoAxis.None;
            PreviewInteractionSurface.Cursor = Cursors.Arrow;
            if (interruptedGizmoDrag)
            {
                try
                {
                    await ApplyRouteEditorAutomaticallyAsync();
                }
                finally
                {
                    _gizmoPreviewOverrideActive = false;
                }
            }
        }

        private void EndPreviewDrag()
        {
            _previewDragButton = null;
            _gizmoDragAxis = GizmoAxis.None;
            PreviewInteractionSurface.Cursor = Cursors.Arrow;
            if (PreviewInteractionSurface.IsMouseCaptured)
            {
                PreviewInteractionSurface.ReleaseMouseCapture();
            }
        }

        private bool TryBeginGizmoDrag(Point pointer)
        {
            if (_selectedRoute == null || !TryReadOffset(out PoseOffset offset, out _))
            {
                return false;
            }

            GizmoAxis axis = GizmoAxis.None;
            Point3D hitPoint = new Point3D();
            VisualTreeHelper.HitTest(
                PosePreviewViewport,
                null,
                result =>
                {
                    if (result is RayMeshGeometry3DHitTestResult ray &&
                        ray.ModelHit is GeometryModel3D hitModel &&
                        _gizmoHitModels.TryGetValue(hitModel, out GizmoAxis hitAxis))
                    {
                        axis = hitAxis;
                        hitPoint = ray.PointHit;
                        return HitTestResultBehavior.Stop;
                    }
                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(pointer));
            if (axis == GizmoAxis.None)
            {
                return false;
            }

            Point3D origin = _gizmoWorldTransform.Transform(new Point3D());
            Vector3D worldAxis = _gizmoWorldTransform.Transform(GetGizmoAxisVector(axis));
            if (worldAxis.LengthSquared < 1e-12)
            {
                return false;
            }
            worldAxis.Normalize();

            Point3D directionOrigin = _gizmoMode == GizmoMode.Translate ? origin : hitPoint;
            Vector3D direction = worldAxis;
            if (_gizmoMode == GizmoMode.Rotate)
            {
                Vector3D radial = hitPoint - origin;
                radial -= worldAxis * Vector3D.DotProduct(radial, worldAxis);
                direction = Vector3D.CrossProduct(worldAxis, radial);
                if (direction.LengthSquared < 1e-12)
                {
                    return false;
                }
                direction.Normalize();
            }

            if (!TryProjectPreviewPoint(directionOrigin, out Point screenOrigin) ||
                !TryProjectPreviewPoint(directionOrigin + (direction * 0.1), out Point screenEnd))
            {
                return false;
            }

            Vector screenDirection = screenEnd - screenOrigin;
            if (screenDirection.LengthSquared < 1e-6)
            {
                return false;
            }
            screenDirection.Normalize();

            _gizmoDragAxis = axis;
            _gizmoDragStartOffset = offset;
            _gizmoDragStartPointer = pointer;
            _gizmoDragScreenDirection = screenDirection;
            _gizmoDragUnitsPerPixel = GetPreviewWorldUnitsPerPixel(directionOrigin);
            _gizmoPreviewOverrideActive = true;
            return true;
        }

        private void UpdateGizmoDrag(Point pointer, bool fineAdjustment)
        {
            if (_gizmoDragAxis == GizmoAxis.None || _gizmoDragStartOffset == null)
            {
                return;
            }

            Vector pointerDelta = pointer - _gizmoDragStartPointer;
            double signedPixels = Vector.Multiply(pointerDelta, _gizmoDragScreenDirection);
            double fineScale = fineAdjustment ? 0.2 : 1.0;
            var next = new PoseOffset
            {
                TranslationX = _gizmoDragStartOffset.TranslationX,
                TranslationY = _gizmoDragStartOffset.TranslationY,
                TranslationZ = _gizmoDragStartOffset.TranslationZ,
                RotationX = _gizmoDragStartOffset.RotationX,
                RotationY = _gizmoDragStartOffset.RotationY,
                RotationZ = _gizmoDragStartOffset.RotationZ,
                RotationW = _gizmoDragStartOffset.RotationW
            };

            Vector3D localAxis = GetGizmoAxisVector(_gizmoDragAxis);
            var rotation = new Quaternion(
                next.RotationX,
                next.RotationY,
                next.RotationZ,
                next.RotationW);
            if (QuaternionLengthSquared(rotation) < 1e-12)
            {
                rotation = Quaternion.Identity;
            }
            else
            {
                rotation.Normalize();
            }

            if (_gizmoMode == GizmoMode.Translate)
            {
                double distance = signedPixels * _gizmoDragUnitsPerPixel * fineScale;
                Vector3D sourceLocalDelta = RotateVector(rotation, localAxis * distance);
                next.TranslationX += sourceLocalDelta.X * 100.0;
                next.TranslationY += sourceLocalDelta.Y * 100.0;
                next.TranslationZ += sourceLocalDelta.Z * 100.0;
            }
            else
            {
                double angleDegrees = signedPixels * 0.6 * fineScale;
                Quaternion delta = new Quaternion(localAxis, angleDegrees);
                Quaternion combined = MultiplyQuaternion(rotation, delta);
                combined.Normalize();
                next.RotationX = combined.X;
                next.RotationY = combined.Y;
                next.RotationZ = combined.Z;
                next.RotationW = combined.W;
            }

            LoadOffsetFields(next);
            ApplyOffsetPreviewOverride(next);
        }

        private bool TryProjectPreviewPoint(Point3D point, out Point screen)
        {
            screen = new Point();
            double width = PosePreviewViewport.ActualWidth;
            double height = PosePreviewViewport.ActualHeight;
            if (width <= 1 || height <= 1)
            {
                return false;
            }

            Vector3D forward = PosePreviewCamera.LookDirection;
            Vector3D up = PosePreviewCamera.UpDirection;
            if (forward.LengthSquared < 1e-12 || up.LengthSquared < 1e-12)
            {
                return false;
            }
            forward.Normalize();
            up.Normalize();
            Vector3D right = Vector3D.CrossProduct(forward, up);
            if (right.LengthSquared < 1e-12)
            {
                return false;
            }
            right.Normalize();
            up = Vector3D.CrossProduct(right, forward);
            up.Normalize();

            Vector3D fromCamera = point - PosePreviewCamera.Position;
            double depth = Vector3D.DotProduct(fromCamera, forward);
            if (depth <= 1e-6)
            {
                return false;
            }
            double halfHeight = depth * Math.Tan(PosePreviewCamera.FieldOfView * Math.PI / 360.0);
            double halfWidth = halfHeight * width / height;
            screen = new Point(
                (0.5 + (Vector3D.DotProduct(fromCamera, right) / (2.0 * halfWidth))) * width,
                (0.5 - (Vector3D.DotProduct(fromCamera, up) / (2.0 * halfHeight))) * height);
            return true;
        }

        private double GetPreviewWorldUnitsPerPixel(Point3D point)
        {
            Vector3D forward = PosePreviewCamera.LookDirection;
            if (forward.LengthSquared < 1e-12 || PosePreviewViewport.ActualHeight <= 1)
            {
                return 0.001;
            }
            forward.Normalize();
            double depth = Math.Max(0.01, Vector3D.DotProduct(point - PosePreviewCamera.Position, forward));
            return 2.0 * depth * Math.Tan(PosePreviewCamera.FieldOfView * Math.PI / 360.0) /
                PosePreviewViewport.ActualHeight;
        }

        private static Vector3D GetGizmoAxisVector(GizmoAxis axis)
        {
            return axis == GizmoAxis.X ? new Vector3D(1, 0, 0) :
                axis == GizmoAxis.Y ? new Vector3D(0, 1, 0) :
                new Vector3D(0, 0, 1);
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
            bool manualPose = _selectedRoute.PoseSourceKind == PoseSourceKind.Manual;
            DeviceOption rotationSource = _selectedRoute.SplitPoseSource
                ? RuntimeRotationSourceComboBox.SelectedItem as DeviceOption ??
                    _onlinePhysicalDevices.FirstOrDefault(device => string.Equals(
                        device.DevicePath,
                        _selectedRoute.RotationSourceDevicePath,
                        StringComparison.Ordinal))
                : null;
            bool replacesTarget = _selectedRoute.Mode == RouteMode.ReplaceTarget;
            string targetPath = replacesTarget
                ? (RuntimeTargetComboBox.SelectedItem as TargetOption)?.TargetPath ?? _selectedRoute.TargetDevicePath
                : string.Empty;
            DeviceOption target = _knownPhysicalDevices.FirstOrDefault(device =>
                string.Equals(device.DevicePath, targetPath, StringComparison.Ordinal)) ??
                _knownPhysicalDevices.FirstOrDefault(device =>
                    string.Equals(device.RoleTargetPath, targetPath, StringComparison.Ordinal));

            Task<OpenVrRenderModel> sourceTask = manualPose
                ? Task.FromResult<OpenVrRenderModel>(null)
                : GetPreviewRenderModelAsync(source?.RenderModelName);
            Task<OpenVrRenderModel> rotationSourceTask = _selectedRoute.SplitPoseSource
                ? GetPreviewRenderModelAsync(rotationSource?.RenderModelName)
                : Task.FromResult<OpenVrRenderModel>(null);
            Task<OpenVrRenderModel> targetTask = GetPreviewRenderModelAsync(target?.RenderModelName);
            bool showProxy = ShouldShowProxyInPreview();
            string outputRenderModel = _selectedRoute.Mode == RouteMode.VirtualController
                ? _selectedRoute.ControllerHand == ControllerHand.Left
                    ? "oculus_quest2_controller_left"
                    : _selectedRoute.ControllerHand == ControllerHand.Right
                        ? "oculus_quest2_controller_right"
                        : null
                : _selectedRoute.Mode == RouteMode.VirtualHmd
                    ? GenericHmdRenderModelName
                    : ProxyRenderModelName;
            Task<OpenVrRenderModel> proxyTask = showProxy
                ? GetPreviewRenderModelAsync(outputRenderModel)
                : Task.FromResult<OpenVrRenderModel>(null);
            OpenVrRenderModel[] models = await Task.WhenAll(
                sourceTask,
                rotationSourceTask,
                targetTask,
                proxyTask);
            if (models[0] == null && !string.IsNullOrWhiteSpace(source?.RenderModelName))
            {
                _previewModelCache.Remove(source.RenderModelName);
            }
            if (models[1] == null && !string.IsNullOrWhiteSpace(rotationSource?.RenderModelName))
            {
                _previewModelCache.Remove(rotationSource.RenderModelName);
            }
            if (models[2] == null && !string.IsNullOrWhiteSpace(target?.RenderModelName))
            {
                _previewModelCache.Remove(target.RenderModelName);
            }
            if (requestVersion != _previewModelRequestVersion || !IsVisible)
            {
                return;
            }

            TrackedDeviceKind sourceKind = source?.DeviceKind ?? InferDeviceKind(_selectedRoute.SourceDevicePath);
            TrackedDeviceKind rotationSourceKind = rotationSource?.DeviceKind ??
                InferDeviceKind(_selectedRoute.RotationSourceDevicePath);
            TrackedDeviceKind targetKind = target?.DeviceKind ?? InferTargetKind(targetPath);
            Color targetColor = (Color)ColorConverter.ConvertFromString("#45D483");
            SetPreviewDeviceModel(
                _sourcePreviewModel,
                models[0],
                sourceKind,
                (Color)ColorConverter.ConvertFromString("#F5A623"));
            SetPreviewDeviceModel(
                _rotationSourcePreviewModel,
                models[1],
                rotationSourceKind,
                (Color)ColorConverter.ConvertFromString("#C084FC"));
            SetPreviewDeviceModel(_targetPreviewModel, models[2], targetKind, targetColor);
            SetPreviewDeviceModel(
                _proxyPreviewModel,
                models[3],
                _selectedRoute.Mode == RouteMode.VirtualHmd ? TrackedDeviceKind.Hmd : TrackedDeviceKind.Tracker,
                (Color)ColorConverter.ConvertFromString("#5A9BFF"));

            string sourceMode = manualPose
                ? Tr.Get("route.preview.refresh_preview_device_models_async.position_source_model_manual_pose")
                : _hideSourceInPreview
                ? Tr.Get("route.preview.refresh_preview_device_models_async.position_source_model_hide")
                : models[0] == null ? Tr.Get("route.preview.refresh_preview_device_models_async.position_source_model") : Tr.Get("route.preview.refresh_preview_device_models_async.position_source_model_steamvr") + models[0].Name;
            string rotationSourceMode = !_selectedRoute.SplitPoseSource
                ? Tr.Get("route.preview.refresh_preview_device_models_async.rotation_source_model_position_source")
                : _hideSourceInPreview
                    ? Tr.Get("route.preview.refresh_preview_device_models_async.rotation_source_model_hide")
                    : models[1] == null
                        ? Tr.Get("route.preview.refresh_preview_device_models_async.rotation_source_model")
                        : Tr.Get("route.preview.refresh_preview_device_models_async.rotation_source_model_steamvr") + models[1].Name;
            string targetMode = !replacesTarget
                ? Tr.Get("route.preview.refresh_preview_device_models_async.target_model_output")
                : _hideTargetInPreview
                ? Tr.Get("route.preview.refresh_preview_device_models_async.target_model_hide")
                : models[2] == null ? Tr.Get("route.preview.refresh_preview_device_models_async.target_model") : Tr.Get("route.preview.refresh_preview_device_models_async.target_model_steamvr") + models[2].Name;
            string proxyMode = !showProxy
                ? Tr.Get("route.preview.refresh_preview_device_models_async.model_hide")
                : models[3] == null
                    ? Tr.Get("route.preview.refresh_preview_device_models_async.model")
                    : Tr.Get("route.preview.refresh_preview_device_models_async.model_steamvr") + models[3].Name;
            _previewModelDescription = sourceMode + "\n" + rotationSourceMode + "\n" + targetMode + "\n" + proxyMode;
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
                    ? Tr.Get("preview.telemetry.retrying")
                    : Tr.Get("preview.telemetry.paused");
                PreviewStatusText.Foreground = FindBrush("WarningBrush");
                PreviewStatusText.ToolTip = Tr.Get("route.preview.refresh_telemetry_async.preserve") + exception.Message;
            }
            finally
            {
                _telemetryUpdatePending = false;
            }
        }

        private void RenderTelemetry(PoseTelemetrySnapshot snapshot)
        {
            ApplySourceAndTargetPreview(snapshot);

            PreviewStatusText.Text = Tr.Get("preview.status.live");
            PreviewStatusText.Foreground = FindBrush("SuccessBrush");
            string healthText = Tr.Get("route.preview.render_telemetry.output") + PoseHealth(snapshot.Output) +
                (_selectedRoute?.PoseSourceKind == PoseSourceKind.Manual
                    ? Tr.Get("route.preview.render_telemetry.manual_pose")
                    : Tr.Get("route.preview.render_telemetry.position_source") + PoseHealth(snapshot.Source)) +
                (_selectedRoute?.SplitPoseSource == true
                    ? Tr.Get("route.preview.render_telemetry.rotation_source") + PoseHealth(snapshot.RotationSource)
                    : string.Empty) +
                Tr.Get("route.preview.render_telemetry.target") + PoseHealth(snapshot.Target) + Tr.Get("route.preview.render_telemetry.source");
            string diagnosticText = healthText;
            if (IsRenderablePose(snapshot.Source) && IsRenderablePose(snapshot.Output) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.Output, out Matrix3D actualOutputMatrix))
            {
                healthText += Tr.Get("route.preview.render_telemetry.offset") +
                    actualOutputMatrix.OffsetX.ToString("F3", CultureInfo.InvariantCulture) + ", " +
                    actualOutputMatrix.OffsetY.ToString("F3", CultureInfo.InvariantCulture) + ", " +
                    actualOutputMatrix.OffsetZ.ToString("F3", CultureInfo.InvariantCulture) + ") m";
                string targetDistanceText = string.Empty;
                if (IsRenderablePose(snapshot.Target))
                {
                    double dx = snapshot.Output.PositionX - snapshot.Target.PositionX;
                    double dy = snapshot.Output.PositionY - snapshot.Target.PositionY;
                    double dz = snapshot.Output.PositionZ - snapshot.Target.PositionZ;
                    targetDistanceText = Tr.Get("route.preview.render_telemetry.output_target") +
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
            bool splitSource = _selectedRoute?.SplitPoseSource == true;
            bool manualPose = _selectedRoute?.PoseSourceKind == PoseSourceKind.Manual;
            Matrix3D targetMatrix = Matrix3D.Identity;
            bool targetVisible = !_hideTargetInPreview &&
                IsRenderablePose(snapshot.Source) &&
                IsRenderablePose(snapshot.Target) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.Target, out targetMatrix);
            bool sourceVisible = !manualPose && !_hideSourceInPreview && IsRenderablePose(snapshot.Source);
            Matrix3D sourceMatrix = Matrix3D.Identity;
            Matrix3D rotationSourceMatrix = Matrix3D.Identity;
            bool rotationSourceVisible = splitSource && !_hideSourceInPreview &&
                IsRenderablePose(snapshot.Source) &&
                IsRenderablePose(snapshot.RotationSource) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.RotationSource, out rotationSourceMatrix);
            _splitBasePreviewRotation = splitSource &&
                TryGetRelativePoseRotation(snapshot.Source, snapshot.RotationSource, out Quaternion baseRotation)
                    ? baseRotation
                    : Quaternion.Identity;
            Matrix3D proxyMatrix = Matrix3D.Identity;
            bool hasConfiguredOffset = TryReadOffset(out PoseOffset configuredOffset, out _);
            bool hasOutputTransform = IsRenderablePose(snapshot.Source) &&
                IsRenderablePose(snapshot.Output) &&
                TryGetRelativePoseMatrix(snapshot.Source, snapshot.Output, out proxyMatrix);
            if ((_gizmoPreviewOverrideActive || !hasOutputTransform) && hasConfiguredOffset)
            {
                proxyMatrix = CreateConfiguredOutputMatrix(configuredOffset);
                hasOutputTransform = true;
            }
            bool proxyVisible = ShouldShowProxyInPreview() && hasOutputTransform;
            bool gizmoVisible = _gizmoMode != GizmoMode.None &&
                _selectedRoute != null && hasConfiguredOffset;
            _gizmoVisible = gizmoVisible;
            Matrix3D gizmoMatrix = hasConfiguredOffset
                ? CreateConfiguredOutputMatrix(configuredOffset)
                : proxyMatrix;
            Rect3D combined = Rect3D.Empty;
            if (sourceVisible)
            {
                AddTransformedBounds(ref combined, _sourcePreviewModel, sourceMatrix);
            }
            if (rotationSourceVisible)
            {
                AddTransformedBounds(ref combined, _rotationSourcePreviewModel, rotationSourceMatrix);
            }
            if (targetVisible)
            {
                AddTransformedBounds(ref combined, _targetPreviewModel, targetMatrix);
            }
            if (proxyVisible)
            {
                AddTransformedBounds(ref combined, _proxyPreviewModel, proxyMatrix);
            }
            if (gizmoVisible)
            {
                AddTransformedBounds(ref combined, _gizmoPreviewModel, gizmoMatrix);
            }

            Vector3D centerOffset = _gizmoPreviewOverrideActive
                ? _previewCenterOffset
                : new Vector3D();
            if (!_gizmoPreviewOverrideActive && !combined.IsEmpty)
            {
                centerOffset = new Vector3D(
                    -(combined.X + (combined.SizeX / 2.0)),
                    -(combined.Y + (combined.SizeY / 2.0)),
                    -(combined.Z + (combined.SizeZ / 2.0)));
            }
            _previewCenterOffset = centerOffset;

            if (sourceVisible)
            {
                sourceMatrix.Translate(centerOffset);
                _sourcePreviewModel.Transform = new MatrixTransform3D(sourceMatrix);
            }
            else
            {
                HidePreviewModel(_sourcePreviewModel);
            }
            if (rotationSourceVisible)
            {
                rotationSourceMatrix.Translate(centerOffset);
                _rotationSourcePreviewModel.Transform = new MatrixTransform3D(rotationSourceMatrix);
            }
            else
            {
                HidePreviewModel(_rotationSourcePreviewModel);
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
            if (proxyVisible)
            {
                proxyMatrix.Translate(centerOffset);
                _proxyPreviewModel.Transform = new MatrixTransform3D(proxyMatrix);
            }
            else
            {
                HidePreviewModel(_proxyPreviewModel);
            }
            if (gizmoVisible)
            {
                gizmoMatrix.Translate(centerOffset);
                _gizmoWorldTransform = gizmoMatrix;
                _gizmoPreviewModel.Transform = new MatrixTransform3D(gizmoMatrix);
            }
            else
            {
                HidePreviewModel(_gizmoPreviewModel);
            }
        }

        private void ApplyOffsetPreviewOverride(PoseOffset offset)
        {
            Matrix3D matrix = CreateConfiguredOutputMatrix(offset);
            matrix.Translate(_previewCenterOffset);
            _gizmoWorldTransform = matrix;
            _gizmoVisible = _gizmoMode != GizmoMode.None;
            if (_gizmoVisible)
            {
                _gizmoPreviewModel.Transform = new MatrixTransform3D(matrix);
            }
            else
            {
                HidePreviewModel(_gizmoPreviewModel);
            }
            if (ShouldShowProxyInPreview())
            {
                _proxyPreviewModel.Transform = new MatrixTransform3D(matrix);
            }
        }

        private Matrix3D CreateConfiguredOutputMatrix(PoseOffset offset)
        {
            Quaternion offsetRotation = NormalizeQuaternion(new Quaternion(
                offset.RotationX,
                offset.RotationY,
                offset.RotationZ,
                offset.RotationW));
            Quaternion baseRotation = _selectedRoute?.SplitPoseSource == true
                ? NormalizeQuaternion(_splitBasePreviewRotation)
                : Quaternion.Identity;
            Quaternion outputRotation = MultiplyQuaternion(baseRotation, offsetRotation);
            outputRotation.Normalize();
            Vector3D translatedOffset = RotateVector(baseRotation, new Vector3D(
                offset.TranslationX / 100.0,
                offset.TranslationY / 100.0,
                offset.TranslationZ / 100.0));

            Matrix3D matrix = Matrix3D.Identity;
            matrix.Rotate(outputRotation);
            matrix.Translate(translatedOffset);
            return matrix;
        }

        private static bool TryGetRelativePoseRotation(
            PoseTelemetry source,
            PoseTelemetry target,
            out Quaternion rotation)
        {
            rotation = Quaternion.Identity;
            if (!IsRenderablePose(source) || !IsRenderablePose(target))
            {
                return false;
            }

            Quaternion sourceRotation = NormalizeQuaternion(new Quaternion(
                source.RotationX,
                source.RotationY,
                source.RotationZ,
                source.RotationW));
            Quaternion targetRotation = NormalizeQuaternion(new Quaternion(
                target.RotationX,
                target.RotationY,
                target.RotationZ,
                target.RotationW));
            Quaternion inverseSource = sourceRotation;
            inverseSource.Conjugate();
            rotation = MultiplyQuaternion(inverseSource, targetRotation);
            rotation.Normalize();
            return true;
        }

        private static Quaternion NormalizeQuaternion(Quaternion rotation)
        {
            if (QuaternionLengthSquared(rotation) < 1e-12)
            {
                return Quaternion.Identity;
            }
            rotation.Normalize();
            return rotation;
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

        private static double QuaternionLengthSquared(Quaternion value)
        {
            return (value.X * value.X) + (value.Y * value.Y) +
                (value.Z * value.Z) + (value.W * value.W);
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
            return pose.Valid ? Tr.Get("common.status.valid") : pose.Connected ? Tr.Get("route.preview.pose_health.pose") : Tr.Get("common.status.disconnected");
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
                    // OpenVR render-model textures may carry an auxiliary alpha channel.
                    // The preview renders solid device shells, so do not let that channel
                    // turn an emissive (unlit) material translucent.
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

        private void RebuildGizmoModel()
        {
            Transform3D transform = _gizmoPreviewModel.Transform;
            _gizmoPreviewModel.Children.Clear();
            _gizmoHitModels.Clear();
            if (_gizmoMode == GizmoMode.Translate)
            {
                AddTranslationGizmoAxis(GizmoAxis.X, Colors.IndianRed);
                AddTranslationGizmoAxis(GizmoAxis.Y, Colors.LimeGreen);
                AddTranslationGizmoAxis(GizmoAxis.Z, Colors.DodgerBlue);
            }
            else if (_gizmoMode == GizmoMode.Rotate)
            {
                AddRotationGizmoAxis(GizmoAxis.X, Colors.IndianRed);
                AddRotationGizmoAxis(GizmoAxis.Y, Colors.LimeGreen);
                AddRotationGizmoAxis(GizmoAxis.Z, Colors.DodgerBlue);
            }
            _gizmoPreviewModel.Transform = transform;
        }

        private void AddTranslationGizmoAxis(GizmoAxis axis, Color color)
        {
            const double shaftLength = 0.13;
            const double tipLength = 0.055;
            const double tipRadius = 0.022;
            const double thickness = 0.009;
            Vector3D size = axis == GizmoAxis.X
                ? new Vector3D(shaftLength, thickness, thickness)
                : axis == GizmoAxis.Y
                    ? new Vector3D(thickness, shaftLength, thickness)
                    : new Vector3D(thickness, thickness, shaftLength);
            Point3D center = axis == GizmoAxis.X
                ? new Point3D(shaftLength / 2.0, 0, 0)
                : axis == GizmoAxis.Y
                    ? new Point3D(0, shaftLength / 2.0, 0)
                    : new Point3D(0, 0, shaftLength / 2.0);
            AddGizmoGeometry(CreateBoxModel(center, size, color), axis);
            AddGizmoGeometry(CreateConeModel(
                axis,
                shaftLength,
                shaftLength + tipLength,
                tipRadius,
                color), axis);
        }

        private void AddRotationGizmoAxis(GizmoAxis axis, Color color)
        {
            AddGizmoGeometry(CreateRingModel(axis, 0.135, 0.012, color), axis);
        }

        private void AddGizmoGeometry(GeometryModel3D model, GizmoAxis axis)
        {
            _gizmoPreviewModel.Children.Add(model);
            _gizmoHitModels[model] = axis;
        }

        private static GeometryModel3D CreateRingModel(
            GizmoAxis axis,
            double radius,
            double thickness,
            Color color)
        {
            const int segments = 72;
            double innerRadius = radius - (thickness / 2.0);
            double outerRadius = radius + (thickness / 2.0);
            var positions = new Point3DCollection((segments + 1) * 2);
            for (int index = 0; index <= segments; index++)
            {
                double angle = index * Math.PI * 2.0 / segments;
                positions.Add(RingPoint(axis, innerRadius, angle));
                positions.Add(RingPoint(axis, outerRadius, angle));
            }

            var indices = new Int32Collection(segments * 6);
            for (int index = 0; index < segments; index++)
            {
                int first = index * 2;
                indices.Add(first);
                indices.Add(first + 1);
                indices.Add(first + 3);
                indices.Add(first);
                indices.Add(first + 3);
                indices.Add(first + 2);
            }
            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static Point3D RingPoint(GizmoAxis axis, double radius, double angle)
        {
            double first = Math.Cos(angle) * radius;
            double second = Math.Sin(angle) * radius;
            return axis == GizmoAxis.X
                ? new Point3D(0, first, second)
                : axis == GizmoAxis.Y
                    ? new Point3D(first, 0, second)
                    : new Point3D(first, second, 0);
        }

        private static GeometryModel3D CreateConeModel(
            GizmoAxis axis,
            double baseDistance,
            double tipDistance,
            double radius,
            Color color)
        {
            const int segments = 24;
            var positions = new Point3DCollection(segments + 2);
            for (int index = 0; index < segments; index++)
            {
                double angle = index * Math.PI * 2.0 / segments;
                double first = Math.Cos(angle) * radius;
                double second = Math.Sin(angle) * radius;
                positions.Add(axis == GizmoAxis.X
                    ? new Point3D(baseDistance, first, second)
                    : axis == GizmoAxis.Y
                        ? new Point3D(first, baseDistance, second)
                        : new Point3D(first, second, baseDistance));
            }
            positions.Add(axis == GizmoAxis.X
                ? new Point3D(tipDistance, 0, 0)
                : axis == GizmoAxis.Y
                    ? new Point3D(0, tipDistance, 0)
                    : new Point3D(0, 0, tipDistance));
            positions.Add(axis == GizmoAxis.X
                ? new Point3D(baseDistance, 0, 0)
                : axis == GizmoAxis.Y
                    ? new Point3D(0, baseDistance, 0)
                    : new Point3D(0, 0, baseDistance));

            int tipIndex = segments;
            int baseCenterIndex = segments + 1;
            var indices = new Int32Collection(segments * 6);
            for (int index = 0; index < segments; index++)
            {
                int next = (index + 1) % segments;
                indices.Add(index);
                indices.Add(next);
                indices.Add(tipIndex);
                indices.Add(baseCenterIndex);
                indices.Add(next);
                indices.Add(index);
            }

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
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
            MessageBoxResult result = AppDialog.Show(
                this,
                Tr.Get("settings.devices.clear_history.confirmation"),
                Tr.Get("settings.devices.clear_history"),
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
                AppDialog.Show(
                    this,
                    exception.Message,
                    Tr.Get("settings.devices.clear_device_history_button_click.cannot_clear_device_history"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadOscFields(OscConfiguration configuration)
        {
            _loadingOscFields = true;
            try
            {
                OscListenAddressTextBox.Text = configuration.ListenAddress;
                OscPortTextBox.Text = configuration.Port.ToString(CultureInfo.InvariantCulture);
                OscSendPortTextBox.Text = configuration.SendPort.ToString(CultureInfo.InvariantCulture);
                LoadOscMapping(OscControllerAddresses.ForHand(ControllerHand.Left), true);
                LoadOscMapping(OscControllerAddresses.ForHand(ControllerHand.Right), false);
                OscTouchAssistConfiguration leftTouch = configuration.LeftTouchAssist ?? new OscTouchAssistConfiguration();
                OscTouchAssistConfiguration rightTouch = configuration.RightTouchAssist ?? new OscTouchAssistConfiguration();
                SelectXInputTouchDefault(OscLeftThumbDefaultComboBox, leftTouch.ThumbDefaultTouched);
                SelectXInputTouchDefault(OscLeftIndexDefaultComboBox, leftTouch.IndexDefaultTouched);
                SelectXInputTouchDefault(OscRightThumbDefaultComboBox, rightTouch.ThumbDefaultTouched);
                SelectXInputTouchDefault(OscRightIndexDefaultComboBox, rightTouch.IndexDefaultTouched);
            }
            finally
            {
                _loadingOscFields = false;
            }
        }

        private void LoadOscMapping(OscControllerAddresses addresses, bool left)
        {
            TextBox primary = left ? OscLeftPrimaryTextBox : OscRightPrimaryTextBox;
            TextBox secondary = left ? OscLeftSecondaryTextBox : OscRightSecondaryTextBox;
            TextBox joystickX = left ? OscLeftJoystickXTextBox : OscRightJoystickXTextBox;
            TextBox joystickY = left ? OscLeftJoystickYTextBox : OscRightJoystickYTextBox;
            TextBox joystickClick = left ? OscLeftJoystickClickTextBox : OscRightJoystickClickTextBox;
            TextBox triggerValue = left ? OscLeftTriggerValueTextBox : OscRightTriggerValueTextBox;
            TextBox gripValue = left ? OscLeftGripValueTextBox : OscRightGripValueTextBox;
            TextBox menu = left ? OscLeftMenuTextBox : OscRightMenuTextBox;
            TextBox thumbTouch = left ? OscLeftThumbTouchTextBox : OscRightThumbTouchTextBox;
            TextBox indexTouch = left ? OscLeftIndexTouchTextBox : OscRightIndexTouchTextBox;
            TextBox haptic = left ? OscLeftHapticTextBox : OscRightHapticTextBox;
            primary.Text = addresses.PrimaryButton;
            secondary.Text = addresses.SecondaryButton;
            joystickX.Text = addresses.JoystickX;
            joystickY.Text = addresses.JoystickY;
            joystickClick.Text = addresses.JoystickClick;
            triggerValue.Text = addresses.TriggerValue;
            gripValue.Text = addresses.GripValue;
            menu.Text = addresses.MenuButton;
            thumbTouch.Text = addresses.ThumbTouchAssist;
            indexTouch.Text = addresses.IndexTouchAssist;
            haptic.Text = addresses.Haptic;
        }

        private void OscTouchDefaultComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScheduleOscSettingsApply(immediate: true);
        }

        private void ScheduleOscSettingsApply(bool immediate)
        {
            if (_loadingOscFields || _isClosing)
            {
                return;
            }
            _oscApplyTimer.Stop();
            _oscApplyTimer.Interval = immediate
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromMilliseconds(400);
            _oscApplyTimer.Start();
        }

        private async Task ApplyOscSettingsAsync()
        {
            if (_oscAutoApplyBusy)
            {
                _oscAutoApplyQueued = true;
                return;
            }
            if (_runtimeStatus == null)
            {
                return;
            }
            if (!int.TryParse(OscPortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ||
                !int.TryParse(OscSendPortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sendPort))
            {
                return;
            }
            var osc = new OscConfiguration
            {
                Enabled = OscConfiguration.IsRequiredForRoutes(_workingRoutes),
                ListenAddress = OscListenAddressTextBox.Text.Trim(),
                Port = port,
                SendPort = sendPort,
                LeftTouchAssist = new OscTouchAssistConfiguration
                {
                    ThumbDefaultTouched = GetXInputTouchDefault(OscLeftThumbDefaultComboBox),
                    IndexDefaultTouched = GetXInputTouchDefault(OscLeftIndexDefaultComboBox)
                },
                RightTouchAssist = new OscTouchAssistConfiguration
                {
                    ThumbDefaultTouched = GetXInputTouchDefault(OscRightThumbDefaultComboBox),
                    IndexDefaultTouched = GetXInputTouchDefault(OscRightIndexDefaultComboBox)
                }
            };
            var configuration = new RuntimeConfiguration
            {
                Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                PhysicalSourceHidingEnabled = _physicalSourceHidingEnabled,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList(),
                Osc = osc,
                XInput = CloneXInputConfiguration(_workingXInput)
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                return;
            }
            _oscAutoApplyBusy = true;
            try
            {
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _workingOsc = CloneOscConfiguration(osc);
                _loadedRuntimeRevision = -1;
                await RefreshStatusAsync();
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("settings.osc.apply_osc_settings_async.save_osc_settings_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _oscAutoApplyBusy = false;
                if (_oscAutoApplyQueued)
                {
                    _oscAutoApplyQueued = false;
                    ScheduleOscSettingsApply(immediate: true);
                }
            }
        }

        private static OscConfiguration CloneOscConfiguration(OscConfiguration configuration)
        {
            OscConfiguration source = configuration ?? OscConfiguration.CreateDefault();
            return new OscConfiguration
            {
                Enabled = source.Enabled,
                ListenAddress = source.ListenAddress,
                Port = source.Port,
                SendPort = source.SendPort,
                LeftTouchAssist = CloneOscTouchAssistConfiguration(source.LeftTouchAssist),
                RightTouchAssist = CloneOscTouchAssistConfiguration(source.RightTouchAssist)
            };
        }

        private static OscTouchAssistConfiguration CloneOscTouchAssistConfiguration(
            OscTouchAssistConfiguration configuration)
        {
            return new OscTouchAssistConfiguration
            {
                ThumbDefaultTouched = configuration?.ThumbDefaultTouched ?? true,
                IndexDefaultTouched = configuration?.IndexDefaultTouched ?? true
            };
        }

        private void InitializeXInputMappingOptions(bool registerPreviews = true)
        {
            _loadingXInputFields = true;
            try
            {
                var joystickOptions = new[]
                {
                    new XInputBindingOption(XInputBindingSource.None, Tr.Get("common.value.none"), null),
                    new XInputBindingOption(XInputBindingSource.None, Tr.Get("settings.xinput.capture_prompt"), null, isCaptureCommand: true),
                    new XInputBindingOption(XInputBindingSource.LeftStick, Tr.Get("settings.xinput.initialize_xinput_mapping_options.left_joystick"), "XboxSeriesX_Left_Stick.png"),
                    new XInputBindingOption(XInputBindingSource.RightStick, Tr.Get("settings.xinput.initialize_xinput_mapping_options.right_joystick"), "XboxSeriesX_Right_Stick.png")
                };
                var inputOptions = new[]
                {
                    new XInputBindingOption(XInputBindingSource.None, Tr.Get("common.value.none"), null),
                    new XInputBindingOption(XInputBindingSource.None, Tr.Get("settings.xinput.capture_prompt"), null, isCaptureCommand: true),
                    new XInputBindingOption(XInputBindingSource.A, Tr.Get("settings.xinput.initialize_xinput_mapping_options.a"), "XboxSeriesX_A.png"),
                    new XInputBindingOption(XInputBindingSource.B, Tr.Get("settings.xinput.initialize_xinput_mapping_options.b"), "XboxSeriesX_B.png"),
                    new XInputBindingOption(XInputBindingSource.X, Tr.Get("settings.xinput.initialize_xinput_mapping_options.x"), "XboxSeriesX_X.png"),
                    new XInputBindingOption(XInputBindingSource.Y, Tr.Get("settings.xinput.initialize_xinput_mapping_options.y"), "XboxSeriesX_Y.png"),
                    new XInputBindingOption(XInputBindingSource.DPadUp, Tr.Get("settings.xinput.initialize_xinput_mapping_options.up"), "XboxSeriesX_Dpad_Up.png"),
                    new XInputBindingOption(XInputBindingSource.DPadDown, Tr.Get("settings.xinput.initialize_xinput_mapping_options.down"), "XboxSeriesX_Dpad_Down.png"),
                    new XInputBindingOption(XInputBindingSource.DPadLeft, Tr.Get("settings.xinput.initialize_xinput_mapping_options.left"), "XboxSeriesX_Dpad_Left.png"),
                    new XInputBindingOption(XInputBindingSource.DPadRight, Tr.Get("settings.xinput.initialize_xinput_mapping_options.right"), "XboxSeriesX_Dpad_Right.png"),
                    new XInputBindingOption(XInputBindingSource.LeftStickClick, Tr.Get("settings.xinput.initialize_xinput_mapping_options.left_joystick_down"), "XboxSeriesX_Left_Stick_Click.png"),
                    new XInputBindingOption(XInputBindingSource.RightStickClick, Tr.Get("settings.xinput.initialize_xinput_mapping_options.right_joystick_down"), "XboxSeriesX_Right_Stick_Click.png"),
                    new XInputBindingOption(XInputBindingSource.LeftShoulder, Tr.Get("settings.xinput.initialize_xinput_mapping_options.left_lb"), "XboxSeriesX_LB.png"),
                    new XInputBindingOption(XInputBindingSource.RightShoulder, Tr.Get("settings.xinput.initialize_xinput_mapping_options.right_rb"), "XboxSeriesX_RB.png"),
                    new XInputBindingOption(XInputBindingSource.LeftTrigger, Tr.Get("settings.xinput.initialize_xinput_mapping_options.left_trigger_lt"), "XboxSeriesX_LT.png"),
                    new XInputBindingOption(XInputBindingSource.RightTrigger, Tr.Get("settings.xinput.initialize_xinput_mapping_options.right_trigger_rt"), "XboxSeriesX_RT.png"),
                    new XInputBindingOption(XInputBindingSource.View, Tr.Get("settings.xinput.initialize_xinput_mapping_options.view"), "XboxSeriesX_View.png"),
                    new XInputBindingOption(XInputBindingSource.Menu, Tr.Get("settings.input.menu_button"), "XboxSeriesX_Menu.png")
                };

                DataTemplate itemTemplate = (DataTemplate)FindResource("XInputBindingOptionTemplate");
                XInputLeftJoystickComboBox.ItemsSource = joystickOptions;
                XInputLeftJoystickComboBox.ItemTemplate = itemTemplate;
                XInputRightJoystickComboBox.ItemsSource = joystickOptions;
                XInputRightJoystickComboBox.ItemTemplate = itemTemplate;
                foreach (ComboBox comboBox in GetXInputButtonComboBoxes())
                {
                    comboBox.ItemsSource = inputOptions;
                    comboBox.ItemTemplate = itemTemplate;
                    comboBox.VerticalAlignment = VerticalAlignment.Center;
                }

                var touchDefaultOptions = new[]
                {
                    new XInputTouchDefaultOption(true, Tr.Get("settings.xinput.default_touch")),
                    new XInputTouchDefaultOption(false, Tr.Get("settings.xinput.initialize_xinput_mapping_options.default"))
                };
                XInputLeftThumbDefaultComboBox.ItemsSource = touchDefaultOptions;
                XInputLeftIndexDefaultComboBox.ItemsSource = touchDefaultOptions;
                XInputRightThumbDefaultComboBox.ItemsSource = touchDefaultOptions;
                XInputRightIndexDefaultComboBox.ItemsSource = touchDefaultOptions;
                _loadingOscFields = true;
                try
                {
                    OscLeftThumbDefaultComboBox.ItemsSource = touchDefaultOptions;
                    OscLeftIndexDefaultComboBox.ItemsSource = touchDefaultOptions;
                    OscRightThumbDefaultComboBox.ItemsSource = touchDefaultOptions;
                    OscRightIndexDefaultComboBox.ItemsSource = touchDefaultOptions;
                }
                finally
                {
                    _loadingOscFields = false;
                }

                XInputHapticModeComboBox.ItemsSource = new[]
                {
                    new XInputHapticModeOption(XInputHapticMode.PreserveHandedness, Tr.Get("settings.xinput.initialize_xinput_mapping_options.preserve_hand")),
                    new XInputHapticModeOption(XInputHapticMode.MirrorHandedness, Tr.Get("settings.xinput.preserve_hand_mirrored")),
                    new XInputHapticModeOption(XInputHapticMode.Unified, Tr.Get("settings.xinput.initialize_xinput_mapping_options.haptic"))
                };

                if (!registerPreviews)
                {
                    return;
                }
                RegisterXInputPreview(XInputLeftPrimaryComboBox, XInputLeftPrimaryPreviewImage);
                RegisterXInputPreview(XInputLeftSecondaryComboBox, XInputLeftSecondaryPreviewImage);
                RegisterXInputPreview(XInputLeftJoystickComboBox, XInputLeftJoystickPreviewImage);
                RegisterXInputPreview(XInputLeftJoystickClickComboBox, XInputLeftJoystickClickPreviewImage);
                RegisterXInputPreview(XInputLeftTriggerComboBox, XInputLeftTriggerPreviewImage);
                RegisterXInputPreview(XInputLeftGripComboBox, XInputLeftGripPreviewImage);
                RegisterXInputPreview(XInputLeftMenuComboBox, XInputLeftMenuPreviewImage);
                RegisterXInputPreview(XInputLeftThumbTouchComboBox, XInputLeftThumbTouchPreviewImage);
                RegisterXInputPreview(XInputLeftIndexTouchComboBox, XInputLeftIndexTouchPreviewImage);
                RegisterXInputPreview(XInputRightPrimaryComboBox, XInputRightPrimaryPreviewImage);
                RegisterXInputPreview(XInputRightSecondaryComboBox, XInputRightSecondaryPreviewImage);
                RegisterXInputPreview(XInputRightJoystickComboBox, XInputRightJoystickPreviewImage);
                RegisterXInputPreview(XInputRightJoystickClickComboBox, XInputRightJoystickClickPreviewImage);
                RegisterXInputPreview(XInputRightTriggerComboBox, XInputRightTriggerPreviewImage);
                RegisterXInputPreview(XInputRightGripComboBox, XInputRightGripPreviewImage);
                RegisterXInputPreview(XInputRightMenuComboBox, XInputRightMenuPreviewImage);
                RegisterXInputPreview(XInputRightThumbTouchComboBox, XInputRightThumbTouchPreviewImage);
                RegisterXInputPreview(XInputRightIndexTouchComboBox, XInputRightIndexTouchPreviewImage);
            }
            finally
            {
                _loadingXInputFields = false;
            }
        }

        private IEnumerable<ComboBox> GetXInputButtonComboBoxes()
        {
            yield return XInputLeftPrimaryComboBox;
            yield return XInputLeftSecondaryComboBox;
            yield return XInputLeftJoystickClickComboBox;
            yield return XInputLeftTriggerComboBox;
            yield return XInputLeftGripComboBox;
            yield return XInputLeftMenuComboBox;
            yield return XInputLeftThumbTouchComboBox;
            yield return XInputLeftIndexTouchComboBox;
            yield return XInputRightPrimaryComboBox;
            yield return XInputRightSecondaryComboBox;
            yield return XInputRightJoystickClickComboBox;
            yield return XInputRightTriggerComboBox;
            yield return XInputRightGripComboBox;
            yield return XInputRightMenuComboBox;
            yield return XInputRightThumbTouchComboBox;
            yield return XInputRightIndexTouchComboBox;
        }

        private void LoadXInputFields(XInputConfiguration configuration)
        {
            XInputConfiguration source = CloneXInputConfiguration(configuration);
            _loadingXInputFields = true;
            try
            {
                XInputAnalogThresholdSlider.Value = source.AnalogPressThreshold * 100.0;
                XInputAnalogThresholdText.Text = Math.Round(source.AnalogPressThreshold * 100.0)
                    .ToString("0", CultureInfo.InvariantCulture) + "%";
                XInputHapticModeComboBox.SelectedItem = XInputHapticModeComboBox.Items
                    .Cast<XInputHapticModeOption>()
                    .FirstOrDefault(option => option.Mode == source.HapticMode);
                LoadXInputMapping(source.Left, true);
                LoadXInputMapping(source.Right, false);
            }
            finally
            {
                _loadingXInputFields = false;
            }
        }

        private void LoadXInputMapping(XInputHandMapping mapping, bool left)
        {
            XInputHandMapping defaults = left
                ? XInputConfiguration.CreateDefaultLeftMapping()
                : XInputConfiguration.CreateDefaultRightMapping();
            XInputTouchAssistMapping thumbTouch = mapping.ThumbTouch ?? defaults.ThumbTouch;
            XInputTouchAssistMapping indexTouch = mapping.IndexTouch ?? defaults.IndexTouch;
            SelectXInputOption(left ? XInputLeftPrimaryComboBox : XInputRightPrimaryComboBox, mapping.PrimaryButton);
            SelectXInputOption(left ? XInputLeftSecondaryComboBox : XInputRightSecondaryComboBox, mapping.SecondaryButton);
            SelectXInputOption(left ? XInputLeftJoystickComboBox : XInputRightJoystickComboBox, mapping.Joystick);
            SelectXInputOption(left ? XInputLeftJoystickClickComboBox : XInputRightJoystickClickComboBox, mapping.JoystickClick);
            SelectXInputOption(left ? XInputLeftTriggerComboBox : XInputRightTriggerComboBox, mapping.Trigger);
            SelectXInputOption(left ? XInputLeftGripComboBox : XInputRightGripComboBox, mapping.Grip);
            SelectXInputOption(left ? XInputLeftMenuComboBox : XInputRightMenuComboBox, mapping.MenuButton);
            SelectXInputOption(left ? XInputLeftThumbTouchComboBox : XInputRightThumbTouchComboBox, thumbTouch.ToggleSource);
            SelectXInputTouchDefault(left ? XInputLeftThumbDefaultComboBox : XInputRightThumbDefaultComboBox, thumbTouch.DefaultTouched);
            SelectXInputOption(left ? XInputLeftIndexTouchComboBox : XInputRightIndexTouchComboBox, indexTouch.ToggleSource);
            SelectXInputTouchDefault(left ? XInputLeftIndexDefaultComboBox : XInputRightIndexDefaultComboBox, indexTouch.DefaultTouched);
        }

        private static void SelectXInputOption(ComboBox comboBox, XInputBindingSource source)
        {
            comboBox.SelectedItem = comboBox.Items.Cast<XInputBindingOption>()
                .FirstOrDefault(option => option.Source == source);
        }

        private static void SelectXInputTouchDefault(ComboBox comboBox, bool touched)
        {
            comboBox.SelectedItem = comboBox.Items.Cast<XInputTouchDefaultOption>()
                .FirstOrDefault(option => option.Touched == touched);
        }

        private void XInputAnalogThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            const double minimumSelectableThreshold = 5.0;
            const double maximumSelectableThreshold = 95.0;
            double constrainedValue = Math.Max(
                minimumSelectableThreshold,
                Math.Min(maximumSelectableThreshold, e.NewValue));
            if (Math.Abs(constrainedValue - e.NewValue) > 0.0001)
            {
                XInputAnalogThresholdSlider.Value = constrainedValue;
                return;
            }

            if (XInputAnalogThresholdText != null)
            {
                XInputAnalogThresholdText.Text = Math.Round(e.NewValue)
                    .ToString("0", CultureInfo.InvariantCulture) + "%";
            }
            UpdateXInputAnalogThresholdPreviewState();
            UpdateXInputAnalogThresholdAdornment();
            ScheduleXInputSettingsApply(immediate: false);
        }

        private void UpdateXInputAnalogThresholdPreview(XInputPhysicalState physicalInput)
        {
            _xInputAnalogThresholdPreviewConnected = physicalInput?.Connected == true;
            _xInputAnalogThresholdPreviewValue = _xInputAnalogThresholdPreviewConnected
                ? Math.Max(
                    Math.Max(0.0, Math.Min(1.0, physicalInput.LeftTrigger)),
                    Math.Max(0.0, Math.Min(1.0, physicalInput.RightTrigger)))
                : 0.0;
            UpdateXInputAnalogThresholdPreviewState();
        }

        private void UpdateXInputAnalogThresholdPreviewState()
        {
            if (XInputAnalogThresholdSlider == null)
            {
                return;
            }

            double pressThreshold = XInputAnalogThresholdSlider.Value / 100.0;
            bool pressed = _xInputAnalogThresholdPreviewConnected &&
                _xInputAnalogThresholdPreviewValue >= pressThreshold;

            Color zeroColor = ReadApplicationColor("SurfaceRaisedBrush", Color.FromRgb(31, 31, 31));
            Color signalColor = ReadApplicationColor("MutedTextBrush", Color.FromRgb(146, 146, 146));
            Color backgroundColor = ReadApplicationColor("WindowBrush", Color.FromRgb(18, 18, 18));
            const double activeSignalOpacity = 96.0 / 255.0;
            Color oneColor = BlendColors(backgroundColor, signalColor, activeSignalOpacity);
            Color fillColor = pressed ? oneColor : zeroColor;
            double amount = _xInputAnalogThresholdPreviewValue;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.0, 0.5),
                EndPoint = new Point(1.0, 0.5),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(fillColor, 0.0));
            brush.GradientStops.Add(new GradientStop(fillColor, amount));
            brush.GradientStops.Add(new GradientStop(backgroundColor, amount));
            brush.GradientStops.Add(new GradientStop(backgroundColor, 1.0));
            brush.Freeze();
            XInputAnalogThresholdSlider.Background = brush;
        }

        private static Color ReadApplicationColor(string resourceKey, Color fallback)
        {
            return Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush
                ? brush.Color
                : fallback;
        }

        private static Color BlendColors(Color from, Color to, double amount)
        {
            amount = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromRgb(
                (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
                (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
                (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
        }

        private void XInputAnalogThresholdSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateXInputAnalogThresholdAdornment();
        }

        private void UpdateXInputAnalogThresholdAdornment()
        {
            if (XInputAnalogThresholdSlider == null ||
                XInputAnalogThresholdOverlay == null ||
                XInputAnalogDefaultThresholdMarker == null ||
                XInputAnalogThresholdMarker == null ||
                XInputAnalogThresholdText == null ||
                XInputAnalogThresholdOverlay.ActualWidth <= 0.0)
            {
                return;
            }

            const double thumbWidth = 1.5;
            const double trackHeight = 24.0;
            const double textGap = 2.25;
            const double markerOpticalGap = 5.25;
            double range = XInputAnalogThresholdSlider.Maximum - XInputAnalogThresholdSlider.Minimum;
            double usableWidth = Math.Max(0.0, XInputAnalogThresholdOverlay.ActualWidth - thumbWidth);
            double pressNormalized = range > 0.0
                ? (XInputAnalogThresholdSlider.Value - XInputAnalogThresholdSlider.Minimum) / range
                : 0.0;
            double pressCenter = (thumbWidth / 2.0) + (pressNormalized * usableWidth);
            double defaultNormalized = range > 0.0
                ? ((XInputConfiguration.DefaultAnalogPressThreshold * 100.0) -
                    XInputAnalogThresholdSlider.Minimum) / range
                : 0.0;
            double defaultCenter = (thumbWidth / 2.0) + (defaultNormalized * usableWidth);
            double trackTop = Math.Max(
                0.0,
                (XInputAnalogThresholdOverlay.ActualHeight - trackHeight) / 2.0);
            Canvas.SetLeft(
                XInputAnalogDefaultThresholdMarker,
                defaultCenter - (XInputAnalogDefaultThresholdMarker.Width / 2.0));
            Canvas.SetTop(
                XInputAnalogDefaultThresholdMarker,
                trackTop + trackHeight + markerOpticalGap);
            Canvas.SetLeft(
                XInputAnalogThresholdMarker,
                pressCenter - (XInputAnalogThresholdMarker.Width / 2.0));
            Canvas.SetTop(
                XInputAnalogThresholdMarker,
                trackTop + trackHeight + markerOpticalGap);
            Canvas.SetLeft(
                XInputAnalogThresholdText,
                Math.Max(
                    0.0,
                    Math.Min(
                        XInputAnalogThresholdOverlay.ActualWidth - XInputAnalogThresholdText.Width,
                        pressCenter - (XInputAnalogThresholdText.Width / 2.0))));
            Canvas.SetTop(
                XInputAnalogThresholdText,
                Math.Max(
                    0.0,
                    trackTop - textGap - XInputAnalogThresholdText.Height));
        }

        private void XInputAnalogThresholdMarker_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (XInputAnalogThresholdOverlay == null || XInputAnalogThresholdMarker == null)
            {
                return;
            }

            Point pointer = e.GetPosition(XInputAnalogThresholdOverlay);
            double markerCenter = Canvas.GetLeft(XInputAnalogThresholdMarker) +
                (XInputAnalogThresholdMarker.Width / 2.0);
            _xInputAnalogThresholdDragOffset = pointer.X - markerCenter;
            _xInputAnalogThresholdDragging = XInputAnalogThresholdMarker.CaptureMouse();
            e.Handled = true;
        }

        private void XInputAnalogThresholdMarker_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_xInputAnalogThresholdDragging)
            {
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndXInputAnalogThresholdDrag();
                return;
            }

            UpdateXInputAnalogThresholdFromPointer(e.GetPosition(XInputAnalogThresholdOverlay).X);
            e.Handled = true;
        }

        private void XInputAnalogThresholdMarker_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_xInputAnalogThresholdDragging)
            {
                return;
            }

            UpdateXInputAnalogThresholdFromPointer(e.GetPosition(XInputAnalogThresholdOverlay).X);
            EndXInputAnalogThresholdDrag();
            e.Handled = true;
        }

        private void XInputAnalogThresholdMarker_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _xInputAnalogThresholdDragging = false;
        }

        private void UpdateXInputAnalogThresholdFromPointer(double pointerX)
        {
            if (XInputAnalogThresholdSlider == null ||
                XInputAnalogThresholdOverlay == null ||
                XInputAnalogThresholdOverlay.ActualWidth <= 0.0)
            {
                return;
            }

            const double thumbWidth = 1.5;
            double usableWidth = Math.Max(0.0, XInputAnalogThresholdOverlay.ActualWidth - thumbWidth);
            if (usableWidth <= 0.0)
            {
                return;
            }

            double center = pointerX - _xInputAnalogThresholdDragOffset;
            double normalized = Math.Max(
                0.0,
                Math.Min(1.0, (center - (thumbWidth / 2.0)) / usableWidth));
            double value = XInputAnalogThresholdSlider.Minimum +
                (normalized * (XInputAnalogThresholdSlider.Maximum -
                    XInputAnalogThresholdSlider.Minimum));
            XInputAnalogThresholdSlider.Value = Math.Round(value);
        }

        private void EndXInputAnalogThresholdDrag()
        {
            _xInputAnalogThresholdDragging = false;
            if (XInputAnalogThresholdMarker?.IsMouseCaptured == true)
            {
                XInputAnalogThresholdMarker.ReleaseMouseCapture();
            }
        }

        private void XInputHapticModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScheduleXInputSettingsApply(immediate: true);
        }

        private void XInputMappingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                if (!_loadingXInputFields &&
                    comboBox.SelectedItem is XInputBindingOption selected &&
                    selected.IsCaptureCommand)
                {
                    XInputBindingOption previous = e.RemovedItems
                        .OfType<XInputBindingOption>()
                        .FirstOrDefault(option => !option.IsCaptureCommand);
                    BeginXInputCapture(comboBox, previous, selected);
                    return;
                }
                if (!_loadingXInputFields && comboBox == _xInputCaptureComboBox)
                {
                    ClearXInputCaptureState();
                }
                UpdateXInputPreview(comboBox);
            }
            ScheduleXInputSettingsApply(immediate: true);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_destructiveCleanupInProgress)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _xInputCaptureComboBox != null)
            {
                CancelXInputCapture();
                e.Handled = true;
            }
        }

        private void BeginXInputCapture(
            ComboBox comboBox,
            XInputBindingOption previous,
            XInputBindingOption command)
        {
            CancelXInputCapture();
            _xInputCaptureComboBox = comboBox;
            _xInputCapturePreviousOption = previous ?? comboBox.Items
                .Cast<XInputBindingOption>()
                .First(option => option.Source == XInputBindingSource.None && !option.IsCaptureCommand);
            _xInputCaptureCommandOption = command;
            _xInputCaptureSuppressedSources = new HashSet<XInputBindingSource>();
            _xInputCaptureBaselineReady = false;
            _xInputCaptureDeadlineUtc = DateTime.UtcNow.AddSeconds(5);
            command.SetDisplayName(Tr.Get("settings.xinput.begin_xinput_capture.waiting_input"));
            comboBox.ToolTip = IsXInputJoystickComboBox(comboBox)
                ? Tr.Get("settings.xinput.begin_xinput_capture.left_joystick_right_joystick_esc_cancel")
                : Tr.Get("settings.xinput.begin_xinput_capture.down_trigger_esc_cancel");
            UpdateXInputPreview(comboBox);
        }

        private void UpdateXInputCapture(XInputPhysicalState physicalInput)
        {
            if (_xInputCaptureComboBox == null)
            {
                return;
            }
            if (CheckXInputCaptureTimeout())
            {
                return;
            }
            if (physicalInput == null || !physicalInput.Connected)
            {
                _xInputCaptureBaselineReady = false;
                _xInputCaptureSuppressedSources.Clear();
                return;
            }

            HashSet<XInputBindingSource> active = GetActiveXInputSources(
                physicalInput,
                IsXInputJoystickComboBox(_xInputCaptureComboBox));
            if (!_xInputCaptureBaselineReady)
            {
                _xInputCaptureSuppressedSources = active;
                _xInputCaptureBaselineReady = true;
                return;
            }

            _xInputCaptureSuppressedSources.RemoveWhere(source => !active.Contains(source));
            XInputBindingSource? captured = active
                .Where(source => !_xInputCaptureSuppressedSources.Contains(source))
                .Cast<XInputBindingSource?>()
                .FirstOrDefault();
            if (captured.HasValue)
            {
                CompleteXInputCapture(captured.Value);
            }
        }

        private bool CheckXInputCaptureTimeout()
        {
            if (_xInputCaptureComboBox != null && DateTime.UtcNow >= _xInputCaptureDeadlineUtc)
            {
                CancelXInputCapture();
                return true;
            }
            return false;
        }

        private void CompleteXInputCapture(XInputBindingSource source)
        {
            ComboBox comboBox = _xInputCaptureComboBox;
            XInputBindingOption option = comboBox?.Items
                .Cast<XInputBindingOption>()
                .FirstOrDefault(candidate => !candidate.IsCaptureCommand && candidate.Source == source);
            if (comboBox == null || option == null)
            {
                CancelXInputCapture();
                return;
            }

            ClearXInputCaptureState();
            _loadingXInputFields = true;
            try
            {
                comboBox.SelectedItem = option;
            }
            finally
            {
                _loadingXInputFields = false;
            }
            UpdateXInputPreview(comboBox);
            ScheduleXInputSettingsApply(immediate: true);
        }

        private void CancelXInputCapture()
        {
            ComboBox comboBox = _xInputCaptureComboBox;
            XInputBindingOption previous = _xInputCapturePreviousOption;
            ClearXInputCaptureState();
            if (comboBox == null || previous == null)
            {
                return;
            }

            _loadingXInputFields = true;
            try
            {
                comboBox.SelectedItem = previous;
            }
            finally
            {
                _loadingXInputFields = false;
            }
            UpdateXInputPreview(comboBox);
        }

        private void ClearXInputCaptureState()
        {
            ComboBox comboBox = _xInputCaptureComboBox;
            if (_xInputCaptureCommandOption != null)
            {
                _xInputCaptureCommandOption.SetDisplayName(Tr.Get("settings.xinput.capture_prompt"));
            }
            if (comboBox != null)
            {
                comboBox.ToolTip = null;
            }
            _xInputCaptureComboBox = null;
            _xInputCapturePreviousOption = null;
            _xInputCaptureCommandOption = null;
            _xInputCaptureSuppressedSources = null;
            _xInputCaptureBaselineReady = false;
        }

        private bool IsXInputJoystickComboBox(ComboBox comboBox)
        {
            return comboBox == XInputLeftJoystickComboBox ||
                comboBox == XInputRightJoystickComboBox;
        }

        private static HashSet<XInputBindingSource> GetActiveXInputSources(
            XInputPhysicalState state,
            bool joystickOnly)
        {
            var active = new HashSet<XInputBindingSource>();
            if (joystickOnly)
            {
                if (GetVectorMagnitude(state.LeftStickX, state.LeftStickY) >= XInputCaptureActivationThreshold)
                {
                    active.Add(XInputBindingSource.LeftStick);
                }
                if (GetVectorMagnitude(state.RightStickX, state.RightStickY) >= XInputCaptureActivationThreshold)
                {
                    active.Add(XInputBindingSource.RightStick);
                }
                return active;
            }

            AddPressedXInputSource(active, state.Buttons, XInputA, XInputBindingSource.A);
            AddPressedXInputSource(active, state.Buttons, XInputB, XInputBindingSource.B);
            AddPressedXInputSource(active, state.Buttons, XInputX, XInputBindingSource.X);
            AddPressedXInputSource(active, state.Buttons, XInputY, XInputBindingSource.Y);
            AddPressedXInputSource(active, state.Buttons, XInputDPadUp, XInputBindingSource.DPadUp);
            AddPressedXInputSource(active, state.Buttons, XInputDPadDown, XInputBindingSource.DPadDown);
            AddPressedXInputSource(active, state.Buttons, XInputDPadLeft, XInputBindingSource.DPadLeft);
            AddPressedXInputSource(active, state.Buttons, XInputDPadRight, XInputBindingSource.DPadRight);
            AddPressedXInputSource(active, state.Buttons, XInputLeftThumb, XInputBindingSource.LeftStickClick);
            AddPressedXInputSource(active, state.Buttons, XInputRightThumb, XInputBindingSource.RightStickClick);
            AddPressedXInputSource(active, state.Buttons, XInputLeftShoulder, XInputBindingSource.LeftShoulder);
            AddPressedXInputSource(active, state.Buttons, XInputRightShoulder, XInputBindingSource.RightShoulder);
            AddPressedXInputSource(active, state.Buttons, XInputView, XInputBindingSource.View);
            AddPressedXInputSource(active, state.Buttons, XInputMenu, XInputBindingSource.Menu);
            if (state.LeftTrigger >= XInputCaptureActivationThreshold)
            {
                active.Add(XInputBindingSource.LeftTrigger);
            }
            if (state.RightTrigger >= XInputCaptureActivationThreshold)
            {
                active.Add(XInputBindingSource.RightTrigger);
            }
            return active;
        }

        private static void AddPressedXInputSource(
            ISet<XInputBindingSource> active,
            ushort buttons,
            ushort mask,
            XInputBindingSource source)
        {
            if ((buttons & mask) != 0)
            {
                active.Add(source);
            }
        }

        private static double GetVectorMagnitude(float x, float y)
        {
            return Math.Sqrt(x * x + y * y);
        }

        private void RegisterXInputPreview(ComboBox comboBox, Image previewImage)
        {
            _xInputPreviewImages[comboBox] = previewImage;
            comboBox.DropDownOpened += (_, __) =>
            {
                UpdateXInputPreview(comboBox);
                _xInputHoverPreviewTimer.Start();
            };
            comboBox.DropDownClosed += (_, __) =>
            {
                UpdateXInputPreview(comboBox);
                if (!_xInputPreviewImages.Keys.Any(candidate => candidate.IsDropDownOpen))
                {
                    _xInputHoverPreviewTimer.Stop();
                }
            };
        }

        private void UpdateHoveredXInputPreview()
        {
            ComboBox comboBox = _xInputPreviewImages.Keys.FirstOrDefault(
                candidate => candidate.IsDropDownOpen);
            if (comboBox == null)
            {
                _xInputHoverPreviewTimer.Stop();
                return;
            }

            foreach (object option in comboBox.Items)
            {
                if (option is XInputBindingOption bindingOption &&
                    comboBox.ItemContainerGenerator.ContainerFromItem(option) is ComboBoxItem item &&
                    item.IsMouseOver)
                {
                    SetXInputPreview(comboBox, bindingOption);
                    return;
                }
            }
        }

        private void UpdateXInputPreview(ComboBox comboBox)
        {
            SetXInputPreview(comboBox, comboBox.SelectedItem as XInputBindingOption);
        }

        private void SetXInputPreview(ComboBox comboBox, XInputBindingOption option)
        {
            if (!_xInputPreviewImages.TryGetValue(comboBox, out Image image))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(option?.IconUri))
            {
                image.Source = null;
                return;
            }
            if (!_xInputIconCache.TryGetValue(option.IconUri, out ImageSource source))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(option.IconUri, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
                _xInputIconCache[option.IconUri] = source;
            }
            image.Source = source;
        }

        private void ResetLeftXInputBindingsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetXInputHandBindings(
                left: true,
                Tr.Get("settings.xinput.reset_left_confirmation"),
                Tr.Get("settings.xinput.reset_left_xinput_bindings_button_click.reset_left"));
        }

        private void ResetRightXInputBindingsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetXInputHandBindings(
                left: false,
                Tr.Get("settings.xinput.reset_right_confirmation"),
                Tr.Get("settings.xinput.reset_right_xinput_bindings_button_click.reset_right"));
        }

        private void ResetXInputHandBindings(bool left, string message, string title)
        {
            MessageBoxResult result = AppDialog.Show(
                this,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            CancelXInputCapture();
            _loadingXInputFields = true;
            try
            {
                LoadXInputMapping(
                    left
                        ? XInputConfiguration.CreateDefaultLeftMapping()
                        : XInputConfiguration.CreateDefaultRightMapping(),
                    left);
            }
            finally
            {
                _loadingXInputFields = false;
            }
            ScheduleXInputSettingsApply(immediate: true);
        }

        private void ScheduleXInputSettingsApply(bool immediate)
        {
            if (_loadingXInputFields || _isClosing || _xInputApplyTimer == null)
            {
                return;
            }
            _xInputApplyTimer.Stop();
            _xInputApplyTimer.Interval = immediate
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromMilliseconds(250);
            _xInputApplyTimer.Start();
        }

        private async Task ApplyXInputSettingsAsync()
        {
            if (_xInputAutoApplyBusy)
            {
                _xInputAutoApplyQueued = true;
                return;
            }
            if (_runtimeStatus == null)
            {
                return;
            }

            XInputConfiguration xInput = BuildXInputConfigurationFromFields();
            var configuration = new RuntimeConfiguration
            {
                Revision = Math.Max(DateTime.UtcNow.Ticks, _runtimeStatus.ConfigurationRevision + 1),
                AllowDuplicatePoseSources = _allowDuplicatePoseSources,
                PhysicalSourceHidingEnabled = _physicalSourceHidingEnabled,
                ControllerHandSelectionPriority = _controllerHandSelectionPriority,
                Routes = _workingRoutes.Where(IsRouteComplete).Select(CloneRoute).ToList(),
                Osc = CloneOscConfiguration(_workingOsc),
                XInput = xInput
            };
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count != 0)
            {
                AppDialog.Show(this, string.Join(Environment.NewLine, errors), Tr.Get("settings.xinput.apply_xinput_settings_async.xinput_settings_invalid"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _xInputAutoApplyBusy = true;
            try
            {
                await _runtimeControlService.ApplyConfigurationAsync(configuration);
                _workingXInput = CloneXInputConfiguration(xInput);
                _loadedRuntimeRevision = -1;
                await RefreshStatusAsync();
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("settings.xinput.apply_xinput_settings_async.save_xinput_settings_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _xInputAutoApplyBusy = false;
                if (_xInputAutoApplyQueued)
                {
                    _xInputAutoApplyQueued = false;
                    ScheduleXInputSettingsApply(immediate: true);
                }
            }
        }

        private XInputConfiguration BuildXInputConfigurationFromFields()
        {
            return new XInputConfiguration
            {
                AnalogPressThreshold = (float)(XInputAnalogThresholdSlider.Value / 100.0),
                HapticMode = (XInputHapticModeComboBox.SelectedItem as XInputHapticModeOption)?.Mode
                    ?? XInputHapticMode.PreserveHandedness,
                Left = BuildXInputMapping(true),
                Right = BuildXInputMapping(false)
            };
        }

        private XInputHandMapping BuildXInputMapping(bool left)
        {
            return new XInputHandMapping
            {
                PrimaryButton = GetXInputSource(left ? XInputLeftPrimaryComboBox : XInputRightPrimaryComboBox),
                SecondaryButton = GetXInputSource(left ? XInputLeftSecondaryComboBox : XInputRightSecondaryComboBox),
                Joystick = GetXInputSource(left ? XInputLeftJoystickComboBox : XInputRightJoystickComboBox),
                JoystickClick = GetXInputSource(left ? XInputLeftJoystickClickComboBox : XInputRightJoystickClickComboBox),
                Trigger = GetXInputSource(left ? XInputLeftTriggerComboBox : XInputRightTriggerComboBox),
                Grip = GetXInputSource(left ? XInputLeftGripComboBox : XInputRightGripComboBox),
                MenuButton = GetXInputSource(left ? XInputLeftMenuComboBox : XInputRightMenuComboBox),
                ThumbTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = GetXInputSource(left ? XInputLeftThumbTouchComboBox : XInputRightThumbTouchComboBox),
                    DefaultTouched = GetXInputTouchDefault(left ? XInputLeftThumbDefaultComboBox : XInputRightThumbDefaultComboBox)
                },
                IndexTouch = new XInputTouchAssistMapping
                {
                    ToggleSource = GetXInputSource(left ? XInputLeftIndexTouchComboBox : XInputRightIndexTouchComboBox),
                    DefaultTouched = GetXInputTouchDefault(left ? XInputLeftIndexDefaultComboBox : XInputRightIndexDefaultComboBox)
                }
            };
        }

        private static bool GetXInputTouchDefault(ComboBox comboBox)
        {
            XInputTouchDefaultOption option = comboBox.SelectedItem as XInputTouchDefaultOption;
            return option == null || option.Touched;
        }

        private XInputBindingSource GetXInputSource(ComboBox comboBox)
        {
            if (comboBox == _xInputCaptureComboBox && _xInputCapturePreviousOption != null)
            {
                return _xInputCapturePreviousOption.Source;
            }
            return (comboBox.SelectedItem as XInputBindingOption)?.Source ?? XInputBindingSource.None;
        }

        private static XInputConfiguration CloneXInputConfiguration(XInputConfiguration configuration)
        {
            XInputConfiguration source = configuration ?? XInputConfiguration.CreateDefault();
            return new XInputConfiguration
            {
                AnalogPressThreshold = source.AnalogPressThreshold,
                HapticMode = source.HapticMode,
                Left = CloneXInputMapping(source.Left ?? XInputConfiguration.CreateDefaultLeftMapping()),
                Right = CloneXInputMapping(source.Right ?? XInputConfiguration.CreateDefaultRightMapping())
            };
        }

        private static XInputHandMapping CloneXInputMapping(XInputHandMapping mapping)
        {
            return new XInputHandMapping
            {
                PrimaryButton = mapping.PrimaryButton,
                SecondaryButton = mapping.SecondaryButton,
                Joystick = mapping.Joystick,
                JoystickClick = mapping.JoystickClick,
                Trigger = mapping.Trigger,
                Grip = mapping.Grip,
                MenuButton = mapping.MenuButton,
                ThumbTouch = CloneXInputTouchAssist(mapping.ThumbTouch),
                IndexTouch = CloneXInputTouchAssist(mapping.IndexTouch)
            };
        }

        private static XInputTouchAssistMapping CloneXInputTouchAssist(XInputTouchAssistMapping mapping)
        {
            return mapping == null
                ? new XInputTouchAssistMapping()
                : new XInputTouchAssistMapping
                {
                    ToggleSource = mapping.ToggleSource,
                    DefaultTouched = mapping.DefaultTouched
                };
        }

        private enum GizmoMode
        {
            None,
            Translate,
            Rotate
        }

        private enum GizmoAxis
        {
            None,
            X,
            Y,
            Z
        }

        private enum SettingsSection
        {
            Runtime,
            SteamVr,
            Devices,
            Files,
            Language,
            Osc,
            XInput,
            Advanced
        }

        private sealed class LanguageIssueListItem
        {
            public LanguageIssueListItem(string key, string kindLabel, string detail, Brush kindBrush, string copyHint)
            {
                Key = key;
                KindLabel = kindLabel;
                Detail = detail;
                KindBrush = kindBrush;
                CopyHint = copyHint;
            }

            public string Key { get; }
            public string KindLabel { get; }
            public string Detail { get; }
            public Brush KindBrush { get; }
            public string CopyHint { get; }
        }

        private sealed class FileLocationListItem
        {
            public FileLocationListItem(
                string name,
                string path,
                string description,
                string locationLabel,
                Brush locationBrush,
                string existenceLabel)
            {
                Name = name;
                Path = path;
                Description = description;
                LocationLabel = locationLabel;
                LocationBrush = locationBrush;
                ExistenceLabel = existenceLabel;
            }

            public string Name { get; }

            public string Path { get; }

            public string Description { get; }

            public string LocationLabel { get; }

            public Brush LocationBrush { get; }

            public string ExistenceLabel { get; }
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

            public string StateText => IsOnline ? Tr.Get("common.status.online") : Tr.Get("common.status.offline");
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

        private sealed class RouteModeOption
        {
            public RouteModeOption(RouteMode mode, string displayName)
            {
                Mode = mode;
                DisplayName = displayName;
            }

            public RouteMode Mode { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private sealed class ControllerHandOption
        {
            public ControllerHandOption(ControllerHand hand, string displayName)
            {
                Hand = hand;
                DisplayName = displayName;
            }
            public ControllerHand Hand { get; }
            public string DisplayName { get; }
            public override string ToString() { return DisplayName; }
        }

        private sealed class ControlInputOption
        {
            public ControlInputOption(ControlInputSource source, string displayName)
            {
                Source = source;
                DisplayName = displayName;
            }
            public ControlInputSource Source { get; }
            public string DisplayName { get; }
            public override string ToString() { return DisplayName; }
        }

        private sealed class XInputBindingOption : System.ComponentModel.INotifyPropertyChanged
        {
            private const string IconBaseUri =
                "pack://application:,,,/TrackSwap;component/Assets/ThirdParty/Xelu/Xbox%20Series/";
            private string _displayName;

            public XInputBindingOption(
                XInputBindingSource source,
                string displayName,
                string iconFileName,
                bool isCaptureCommand = false)
            {
                Source = source;
                _displayName = displayName;
                IconUri = string.IsNullOrWhiteSpace(iconFileName)
                    ? null
                    : IconBaseUri + iconFileName;
                IsCaptureCommand = isCaptureCommand;
            }

            public XInputBindingSource Source { get; }
            public string DisplayName => _displayName;
            public string IconUri { get; }
            public bool IsCaptureCommand { get; }
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            public void SetDisplayName(string value)
            {
                if (string.Equals(_displayName, value, StringComparison.Ordinal))
                {
                    return;
                }
                _displayName = value;
                PropertyChanged?.Invoke(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayName)));
            }

            public override string ToString() { return DisplayName; }
        }

        private sealed class XInputTouchDefaultOption
        {
            public XInputTouchDefaultOption(bool touched, string displayName)
            {
                Touched = touched;
                DisplayName = displayName;
            }

            public bool Touched { get; }
            public string DisplayName { get; }
            public override string ToString() { return DisplayName; }
        }

        private sealed class XInputHapticModeOption
        {
            public XInputHapticModeOption(XInputHapticMode mode, string displayName)
            {
                Mode = mode;
                DisplayName = displayName;
            }

            public XInputHapticMode Mode { get; }
            public string DisplayName { get; }
            public override string ToString() { return DisplayName; }
        }
    }
}
