using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using TrackSwap.Localization;
using TrackSwap.Models;
using TrackSwap.Services;

namespace TrackSwap
{
    public partial class DeviceViewerWindow : Window
    {
        private const double SelectionOutlineTargetPixels = 2.5;
        private const double SelectionOutlineMinimumMetres = 0.0015;
        private const double SelectionOutlineMaximumMetres = 0.04;
        private const double SelectionOutlineRebuildThreshold = 0.15;
        private static readonly TimeSpan SelectionOutlineRebuildInterval =
            TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan SteamVrStatusRefreshInterval =
            TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SceneRefreshRetryInterval =
            TimeSpan.FromMilliseconds(500);
        private readonly SteamVrPathService pathService = new SteamVrPathService();
        private readonly SteamVrStatusService statusService = new SteamVrStatusService();
        private readonly OpenVrSceneService sceneService = new OpenVrSceneService();
        private readonly OpenVrRenderModelService renderModelService = new OpenVrRenderModelService();
        private readonly DeviceHistoryService deviceHistoryService = new DeviceHistoryService();
        private readonly DispatcherTimer steamVrStatusTimer;
        private readonly DispatcherTimer minimizedRefreshTimer;
        private readonly DispatcherTimer copyFeedbackTimer;
        private readonly Dictionary<uint, DeviceVisual> deviceVisuals =
            new Dictionary<uint, DeviceVisual>();
        private readonly Dictionary<string, Task<OpenVrRenderModel>> modelCache =
            new Dictionary<string, Task<OpenVrRenderModel>>(StringComparer.Ordinal);
        private readonly Dictionary<uint, OpenVrSceneDevice> latestDevices =
            new Dictionary<uint, OpenVrSceneDevice>();
        private Dictionary<string, ManagedDeviceRecord> managedDevices =
            new Dictionary<string, ManagedDeviceRecord>(StringComparer.Ordinal);
        private readonly Model3DGroup floorReferenceModel =
            DeviceViewerModelFactory.CreateFloorReferenceModel();
        private Model3DGroup backgroundModel = new Model3DGroup();
        private string backgroundSignature = string.Empty;
        private int backgroundVersion;
        private bool refreshPending;
        private bool steamVrStatusRefreshPending;
        private bool steamVrStatusInitialized;
        private bool steamVrRunning;
        private bool sceneUnavailable;
        private bool initialFramePending = true;
        private bool closed;
        private bool renderingSubscribed;
        private bool pointerMoved;
        private bool technicalDetailsVisible;
        private long lastRenderRefreshTimestamp;
        private DateTime lastDeviceHistoryRefreshUtc = DateTime.MinValue;
        private DateTime nextSceneRefreshAttemptUtc = DateTime.MinValue;
        private uint? selectedDeviceIndex;
        private Point3D cameraTarget = new Point3D(0, 1, 0);
        private double cameraYaw = 0.67;
        private double cameraPitch = 0.38;
        private double cameraDistance = 4.0;
        private MouseButton? dragButton;
        private Point pointerDown;
        private Point lastPointer;

        public DeviceViewerWindow()
        {
            InitializeComponent();
            SceneRoot.Children.Add(backgroundModel);
            SceneRoot.Children.Add(floorReferenceModel);
            steamVrStatusTimer = new DispatcherTimer
            {
                Interval = SteamVrStatusRefreshInterval
            };
            steamVrStatusTimer.Tick += SteamVrStatusTimer_Tick;
            minimizedRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            minimizedRefreshTimer.Tick += MinimizedRefreshTimer_Tick;
            copyFeedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            copyFeedbackTimer.Tick += (_, __) => ResetCopyFeedback();
            SceneViewport.SizeChanged += (_, __) => UpdateSelectionOutlineThickness();
            Loaded += async (_, __) =>
            {
                UpdateCamera();
                steamVrStatusTimer.Start();
                await RefreshSteamVrStatusAsync();
            };
            StateChanged += (_, __) => UpdateRefreshSchedule();
            Closed += (_, __) =>
            {
                closed = true;
                steamVrStatusTimer.Stop();
                minimizedRefreshTimer.Stop();
                copyFeedbackTimer.Stop();
                UnsubscribeRendering();
            };
        }

        private async void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (closed || WindowState == WindowState.Minimized || !IsVisible)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long minimumInterval = Math.Max(1, Stopwatch.Frequency / 240);
            if (lastRenderRefreshTimestamp != 0 &&
                now - lastRenderRefreshTimestamp < minimumInterval)
            {
                return;
            }

            lastRenderRefreshTimestamp = now;
            await RefreshSceneAsync();
        }

        private async void MinimizedRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (!closed && steamVrRunning && WindowState == WindowState.Minimized)
            {
                await RefreshSceneAsync();
            }
        }

        private async void SteamVrStatusTimer_Tick(object sender, EventArgs e)
        {
            await RefreshSteamVrStatusAsync();
        }

        private async Task RefreshSteamVrStatusAsync()
        {
            if (closed || steamVrStatusRefreshPending)
            {
                return;
            }

            steamVrStatusRefreshPending = true;
            try
            {
                bool running = await Task.Run(() => statusService.IsRunning());
                if (closed)
                {
                    return;
                }

                bool changed = !steamVrStatusInitialized || running != steamVrRunning;
                steamVrStatusInitialized = true;
                if (!changed)
                {
                    return;
                }

                steamVrRunning = running;
                sceneUnavailable = false;
                nextSceneRefreshAttemptUtc = DateTime.MinValue;
                UpdateRefreshSchedule();
                if (!running)
                {
                    sceneService.ResetCache();
                    SetWaitingStatus();
                    ClearVisibleScene();
                    return;
                }

                await RefreshSceneAsync();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine("Device viewer SteamVR status refresh failed: " + exception);
            }
            finally
            {
                steamVrStatusRefreshPending = false;
            }
        }

        private void UpdateRefreshSchedule()
        {
            if (closed)
            {
                return;
            }

            if (!steamVrRunning)
            {
                minimizedRefreshTimer.Stop();
                UnsubscribeRendering();
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                UnsubscribeRendering();
                minimizedRefreshTimer.Start();
                return;
            }

            minimizedRefreshTimer.Stop();
            SubscribeRendering();
        }

        private void SubscribeRendering()
        {
            if (renderingSubscribed)
            {
                return;
            }

            lastRenderRefreshTimestamp = 0;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            renderingSubscribed = true;
        }

        private void UnsubscribeRendering()
        {
            if (!renderingSubscribed)
            {
                return;
            }

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            renderingSubscribed = false;
        }

        private async Task RefreshSceneAsync()
        {
            if (refreshPending || closed || !steamVrRunning ||
                (sceneUnavailable && DateTime.UtcNow < nextSceneRefreshAttemptUtc))
            {
                return;
            }

            refreshPending = true;
            try
            {
                string runtimePath = pathService.FindRuntimePath();
                OpenVrSceneSnapshot snapshot = await Task.Run(() => sceneService.Capture(runtimePath));
                if (closed || !steamVrRunning)
                {
                    return;
                }
                ApplySnapshot(snapshot, runtimePath);
                sceneUnavailable = false;
                nextSceneRefreshAttemptUtc = DateTime.MinValue;
                StatusText.Text = Tr.Format(
                    "device_viewer.status.live",
                    snapshot.Devices.Count(device => device.Connected));
                StatusText.Foreground = FindBrush("SuccessBrush");
                StatusText.ToolTip = null;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidOperationException ||
                exception is UnauthorizedAccessException ||
                exception is System.ComponentModel.Win32Exception ||
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException)
            {
                Debug.WriteLine("Device viewer refresh failed: " + exception);
                nextSceneRefreshAttemptUtc = DateTime.UtcNow + SceneRefreshRetryInterval;
                if (!sceneUnavailable)
                {
                    sceneUnavailable = true;
                    StatusText.Text = Tr.Get("device_viewer.status.unavailable");
                    StatusText.Foreground = FindBrush("WarningBrush");
                    StatusText.ToolTip = exception.Message;
                    ClearVisibleScene();
                }
            }
            finally
            {
                refreshPending = false;
            }
        }

        private void ApplySnapshot(OpenVrSceneSnapshot snapshot, string runtimePath)
        {
            ApplyBackground(snapshot.Background);
            RefreshManagedDevices();
            latestDevices.Clear();
            foreach (OpenVrSceneDevice device in snapshot.Devices)
            {
                latestDevices[device.DeviceIndex] = device;
            }
            var currentIndices = new HashSet<uint>(snapshot.Devices.Select(device => device.DeviceIndex));
            foreach (uint removed in deviceVisuals.Keys.Where(index => !currentIndices.Contains(index)).ToList())
            {
                SceneRoot.Children.Remove(deviceVisuals[removed].Model);
                deviceVisuals.Remove(removed);
            }

            foreach (OpenVrSceneDevice device in snapshot.Devices)
            {
                if (!deviceVisuals.TryGetValue(device.DeviceIndex, out DeviceVisual visual))
                {
                    visual = new DeviceVisual(
                        DeviceViewerModelFactory.CreateDeviceModel(null, device.DeviceKind),
                        device.RenderModelName,
                        device.DeviceKind);
                    deviceVisuals.Add(device.DeviceIndex, visual);
                    SceneRoot.Children.Add(visual.Model);
                    _ = LoadDeviceModelAsync(device.DeviceIndex, device, runtimePath, visual.Version);
                }
                else if (!string.Equals(
                    visual.RenderModelName,
                    device.RenderModelName,
                    StringComparison.Ordinal))
                {
                    visual.RenderModelName = device.RenderModelName;
                    visual.DeviceKind = device.DeviceKind;
                    visual.Version++;
                    ReplaceModelContents(
                        visual.Model,
                        DeviceViewerModelFactory.CreateDeviceModel(null, device.DeviceKind));
                    RefreshSelectionOutline(device.DeviceIndex, visual);
                    _ = LoadDeviceModelAsync(device.DeviceIndex, device, runtimePath, visual.Version);
                }

                visual.Model.Transform = device.Connected && device.Valid
                    ? new MatrixTransform3D(device.Transform)
                    : new TranslateTransform3D(10000, 10000, 10000);
            }

            UpdateSelectionOutlineThickness();

            if (initialFramePending && snapshot.Devices.Any(device => device.Valid))
            {
                initialFramePending = false;
                ResetView();
            }

            if (selectedDeviceIndex.HasValue &&
                latestDevices.TryGetValue(selectedDeviceIndex.Value, out OpenVrSceneDevice selectedDevice))
            {
                UpdateSelectedDevicePanel(selectedDevice);
            }
            else if (selectedDeviceIndex.HasValue)
            {
                ClearDeviceSelection();
            }
        }

        private void RefreshManagedDevices()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - lastDeviceHistoryRefreshUtc < TimeSpan.FromSeconds(1))
            {
                return;
            }

            lastDeviceHistoryRefreshUtc = nowUtc;
            try
            {
                managedDevices = deviceHistoryService.LoadCatalog().Devices
                    .ToDictionary(device => device.DevicePath, StringComparer.Ordinal);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is Newtonsoft.Json.JsonException)
            {
                Debug.WriteLine("Device viewer metadata refresh failed: " + exception);
            }
        }

        private void SelectDeviceAt(Point point)
        {
            HitTestResult result = VisualTreeHelper.HitTest(SceneViewport, point);
            if (!(result is RayHitTestResult rayResult) || rayResult.ModelHit == null)
            {
                ClearDeviceSelection();
                return;
            }

            foreach (KeyValuePair<uint, DeviceVisual> entry in deviceVisuals)
            {
                if (!ContainsModel(entry.Value.Model, rayResult.ModelHit))
                {
                    continue;
                }

                if (selectedDeviceIndex.HasValue &&
                    selectedDeviceIndex.Value != entry.Key &&
                    deviceVisuals.TryGetValue(selectedDeviceIndex.Value, out DeviceVisual previous))
                {
                    RemoveSelectionOutline(previous);
                }
                ResetCopyFeedback();
                selectedDeviceIndex = entry.Key;
                EnsureSelectionOutline(entry.Value);
                if (latestDevices.TryGetValue(entry.Key, out OpenVrSceneDevice device))
                {
                    UpdateSelectedDevicePanel(device);
                }
                return;
            }

            ClearDeviceSelection();
        }

        private static bool ContainsModel(Model3D model, Model3D candidate)
        {
            if (ReferenceEquals(model, candidate))
            {
                return true;
            }
            if (!(model is Model3DGroup group))
            {
                return false;
            }
            return group.Children.Any(child => ContainsModel(child, candidate));
        }

        private void UpdateSelectedDevicePanel(OpenVrSceneDevice device)
        {
            managedDevices.TryGetValue(device.Identity ?? string.Empty, out ManagedDeviceRecord record);
            string heading = !string.IsNullOrWhiteSpace(record?.CustomDisplayName)
                ? record.CustomDisplayName
                : device.DisplayName;
            SelectionHeadingText.Text = string.IsNullOrWhiteSpace(heading)
                ? Tr.Get("device_viewer.device.unnamed")
                : heading;
            SelectionDescriptionText.Text = Tr.Format(
                "device_viewer.selection.summary",
                DeviceClassText(device.DeviceClass),
                TrackingStatusText(device));
            SelectionDescriptionText.Foreground = device.Connected && device.Valid
                ? FindBrush("SuccessBrush")
                : FindBrush("WarningBrush");
            SelectedDevicePanel.Visibility = Visibility.Visible;

            SetPropertyRow(DeviceNoteRow, DeviceNoteText, record?.Note);
            SetPropertyRow(DeviceManufacturerRow, DeviceManufacturerText, device.ManufacturerName);
            SetPropertyRow(DeviceModelRow, DeviceModelText, device.ModelNumber);
            SetPropertyRow(DeviceSerialRow, DeviceSerialText, device.SerialNumber);
            SetPropertyRow(DeviceRoleRow, DeviceRoleText, ControllerRoleText(device.ControllerRole));
            SetPropertyRow(
                DeviceConnectionRow,
                DeviceConnectionText,
                device.IsWireless.HasValue
                    ? Tr.Get(device.IsWireless.Value
                        ? "device_viewer.connection.wireless"
                        : "device_viewer.connection.wired")
                    : null);

            string battery = null;
            if (device.BatteryPercentage.HasValue)
            {
                int percent = (int)Math.Round(
                    Math.Max(0, Math.Min(1, device.BatteryPercentage.Value)) * 100,
                    MidpointRounding.AwayFromZero);
                battery = device.IsCharging == true
                    ? Tr.Format("device_viewer.battery.charging", percent)
                    : Tr.Format("device_viewer.battery.percent", percent);
            }
            else if (device.IsCharging == true)
            {
                battery = Tr.Get("device_viewer.battery.charging_only");
            }
            SetPropertyRow(DeviceBatteryRow, DeviceBatteryText, battery);

            SetPropertyRow(
                DeviceRefreshRateRow,
                DeviceRefreshRateText,
                device.DisplayFrequencyHz.HasValue && device.DisplayFrequencyHz.Value > 0
                    ? Tr.Format("device_viewer.frequency", device.DisplayFrequencyHz.Value)
                    : null);
            SetPropertyRow(DeviceModeRow, DeviceModeText, device.TrackingReferenceMode);
            SetPropertyRow(
                DeviceTrackingRangeRow,
                DeviceTrackingRangeText,
                TrackingRangeText(device));

            SetPropertyRow(DevicePathRow, DevicePathText, device.Identity);
            DeviceIndexText.Text = device.DeviceIndex.ToString(CultureInfo.InvariantCulture);
            SetPropertyRow(
                DeviceTrackingSystemRow,
                DeviceTrackingSystemText,
                device.TrackingSystemName);
            SetPropertyRow(DeviceRenderModelRow, DeviceRenderModelText, device.RenderModelName);
            SetPropertyRow(DeviceFirmwareRow, DeviceFirmwareText, device.TrackingFirmwareVersion);
            SetPropertyRow(DeviceHardwareRow, DeviceHardwareText, device.HardwareRevision);
            SetPropertyRow(DeviceControllerTypeRow, DeviceControllerTypeText, device.ControllerType);
        }

        private static void AddCopyField(
            ICollection<KeyValuePair<string, string>> fields,
            string labelKey,
            string value)
        {
            string normalized = NormalizeCopyValue(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                fields.Add(
                    new KeyValuePair<string, string>(Tr.Get(labelKey), normalized));
            }
        }

        private static string NormalizeCopyValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void SetPropertyRow(Grid row, TextBlock valueText, string value)
        {
            bool visible = !string.IsNullOrWhiteSpace(value);
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            valueText.Text = visible ? value : string.Empty;
        }

        private static string TrackingRangeText(OpenVrSceneDevice device)
        {
            if (device.TrackingRangeMinimumMetres.HasValue &&
                device.TrackingRangeMaximumMetres.HasValue)
            {
                return Tr.Format(
                    "device_viewer.tracking_range.both",
                    device.TrackingRangeMinimumMetres.Value,
                    device.TrackingRangeMaximumMetres.Value);
            }
            if (device.TrackingRangeMaximumMetres.HasValue)
            {
                return Tr.Format(
                    "device_viewer.tracking_range.maximum",
                    device.TrackingRangeMaximumMetres.Value);
            }
            if (device.TrackingRangeMinimumMetres.HasValue)
            {
                return Tr.Format(
                    "device_viewer.tracking_range.minimum",
                    device.TrackingRangeMinimumMetres.Value);
            }
            return null;
        }

        private static string DeviceClassText(OpenVrSceneDeviceClass deviceClass)
        {
            switch (deviceClass)
            {
                case OpenVrSceneDeviceClass.Hmd:
                    return Tr.Get("device_viewer.class.hmd");
                case OpenVrSceneDeviceClass.Controller:
                    return Tr.Get("device_viewer.class.controller");
                case OpenVrSceneDeviceClass.GenericTracker:
                    return Tr.Get("device_viewer.class.tracker");
                case OpenVrSceneDeviceClass.TrackingReference:
                    return Tr.Get("device_viewer.class.tracking_reference");
                case OpenVrSceneDeviceClass.DisplayRedirect:
                    return Tr.Get("device_viewer.class.display_redirect");
                default:
                    return Tr.Get("common.status.unknown");
            }
        }

        private static string ControllerRoleText(OpenVrControllerRole role)
        {
            switch (role)
            {
                case OpenVrControllerRole.LeftHand:
                    return Tr.Get("common.hand.left");
                case OpenVrControllerRole.RightHand:
                    return Tr.Get("common.hand.right");
                case OpenVrControllerRole.Treadmill:
                    return Tr.Get("device_viewer.role.treadmill");
                case OpenVrControllerRole.Stylus:
                    return Tr.Get("device_viewer.role.stylus");
                default:
                    return null;
            }
        }

        private static string TrackingStatusText(OpenVrSceneDevice device)
        {
            if (!device.Connected)
            {
                return Tr.Get("device_viewer.tracking.disconnected");
            }
            if (device.TrackingResult == OpenVrTrackingResult.CalibratingInProgress)
            {
                return Tr.Get("device_viewer.tracking.calibrating");
            }
            if (device.TrackingResult == OpenVrTrackingResult.CalibratingOutOfRange ||
                device.TrackingResult == OpenVrTrackingResult.RunningOutOfRange)
            {
                return Tr.Get("device_viewer.tracking.out_of_range");
            }
            if (device.TrackingResult == OpenVrTrackingResult.FallbackRotationOnly)
            {
                return Tr.Get("device_viewer.tracking.rotation_only");
            }
            return device.Valid
                ? Tr.Get("device_viewer.tracking.ok")
                : Tr.Get("device_viewer.tracking.no_pose");
        }

        private void ClearDeviceSelection()
        {
            if (selectedDeviceIndex.HasValue &&
                deviceVisuals.TryGetValue(selectedDeviceIndex.Value, out DeviceVisual visual))
            {
                RemoveSelectionOutline(visual);
            }
            selectedDeviceIndex = null;
            technicalDetailsVisible = false;
            ResetCopyFeedback();
            SelectionHeadingText.Text = Tr.Get("device_viewer.heading");
            SelectionDescriptionText.Text = Tr.Get("device_viewer.description");
            SelectionDescriptionText.Foreground = FindBrush("MutedTextBrush");
            SelectedDevicePanel.Visibility = Visibility.Collapsed;
            TechnicalDetailsPanel.Visibility = Visibility.Collapsed;
            TechnicalDetailsButton.Content = Tr.Get("device_viewer.technical.show");
        }

        private void TechnicalDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            technicalDetailsVisible = !technicalDetailsVisible;
            TechnicalDetailsPanel.Visibility = technicalDetailsVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TechnicalDetailsButton.Content = Tr.Get(technicalDetailsVisible
                ? "device_viewer.technical.hide"
                : "device_viewer.technical.show");
        }

        private void CopyAvailableInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!selectedDeviceIndex.HasValue ||
                !latestDevices.TryGetValue(selectedDeviceIndex.Value, out OpenVrSceneDevice device))
            {
                return;
            }

            var fields = new List<KeyValuePair<string, string>>();
            AddCopyField(fields, "device_viewer.property.name", SelectionHeadingText.Text);
            AddCopyField(fields, "device_viewer.property.device_type", DeviceClassText(device.DeviceClass));
            AddCopyField(fields, "device_viewer.property.tracking_status", TrackingStatusText(device));
            AddCopyField(fields, "device_viewer.property.note", DeviceNoteText.Text);
            AddCopyField(fields, "device_viewer.property.battery", DeviceBatteryText.Text);
            AddCopyField(fields, "device_viewer.property.connection", DeviceConnectionText.Text);
            AddCopyField(fields, "device_viewer.property.role", DeviceRoleText.Text);
            AddCopyField(fields, "device_viewer.property.manufacturer", DeviceManufacturerText.Text);
            AddCopyField(fields, "device_viewer.property.model", DeviceModelText.Text);
            AddCopyField(fields, "device_viewer.property.serial", DeviceSerialText.Text);
            AddCopyField(fields, "device_viewer.property.refresh_rate", DeviceRefreshRateText.Text);
            AddCopyField(fields, "device_viewer.property.mode", DeviceModeText.Text);
            AddCopyField(fields, "device_viewer.property.tracking_range", DeviceTrackingRangeText.Text);
            AddCopyField(fields, "device_viewer.property.path", DevicePathText.Text);
            AddCopyField(fields, "device_viewer.property.index", DeviceIndexText.Text);
            AddCopyField(fields, "device_viewer.property.tracking_system", DeviceTrackingSystemText.Text);
            AddCopyField(fields, "device_viewer.property.render_model", DeviceRenderModelText.Text);
            AddCopyField(fields, "device_viewer.property.firmware", DeviceFirmwareText.Text);
            AddCopyField(fields, "device_viewer.property.hardware", DeviceHardwareText.Text);
            AddCopyField(fields, "device_viewer.property.controller_type", DeviceControllerTypeText.Text);

            var text = new StringBuilder();
            text.AppendLine(Tr.Get("device_viewer.copy.header"));
            foreach (KeyValuePair<string, string> field in fields)
            {
                text.AppendLine(Tr.Format("device_viewer.copy.line", field.Key, field.Value));
            }

            try
            {
                Clipboard.SetText(text.ToString().TrimEnd());
                CopyAvailableInfoButton.Content = Tr.Get("device_viewer.copy.completed");
                copyFeedbackTimer.Stop();
                copyFeedbackTimer.Start();
            }
            catch (ExternalException exception)
            {
                Debug.WriteLine("Device viewer clipboard copy failed: " + exception);
            }
        }

        private void ResetCopyFeedback()
        {
            copyFeedbackTimer.Stop();
            CopyAvailableInfoButton.Content = Tr.Get("device_viewer.copy.available");
        }

        private void ApplyBackground(OpenVrBackgroundState state)
        {
            string nextSignature = BuildBackgroundSignature(state);
            if (string.Equals(backgroundSignature, nextSignature, StringComparison.Ordinal))
            {
                return;
            }

            backgroundSignature = nextSignature;
            int version = ++backgroundVersion;
            RemoveBackgroundModel();
            if (state != null &&
                !string.IsNullOrWhiteSpace(state.SolidColor) &&
                ColorConverter.ConvertFromString(state.SolidColor) is Color color)
            {
                SceneInteractionSurface.Background = new SolidColorBrush(color);
                return;
            }

            // WPF composes an EmissiveMaterial additively over the viewport
            // clear color. SteamVR's final panorama pass returns the source
            // sRGB value after its linear round-trip, so any non-black clear
            // color would brighten every channel (the old #0F0F0F added 15).
            SceneInteractionSurface.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(
                    DeviceViewerModelFactory.SkyBackgroundClearColor));
            if (state == null || string.IsNullOrWhiteSpace(state.ImagePath))
            {
                return;
            }

            _ = LoadBackgroundAsync(state.ImagePath, version);
        }

        private async Task LoadBackgroundAsync(string path, int version)
        {
            BitmapSource image;
            try
            {
                image = await Task.Run(() => LoadFrozenBitmap(path));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                Debug.WriteLine("Device viewer background load failed: " + exception);
                return;
            }

            if (closed || version != backgroundVersion)
            {
                return;
            }

            backgroundModel = DeviceViewerModelFactory.CreateSkySphereModel(image);
            backgroundModel.Transform = new TranslateTransform3D(
                SceneCamera.Position.X,
                SceneCamera.Position.Y,
                SceneCamera.Position.Z);
            SceneRoot.Children.Insert(0, backgroundModel);
        }

        private static BitmapSource LoadFrozenBitmap(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void RemoveBackgroundModel()
        {
            SceneRoot.Children.Remove(backgroundModel);
            backgroundModel = new Model3DGroup();
        }

        private static string BuildBackgroundSignature(OpenVrBackgroundState state)
        {
            if (state == null)
            {
                return string.Empty;
            }
            return string.Join(
                "|",
                state.SettingValue ?? string.Empty,
                state.ImagePath ?? string.Empty,
                state.SolidColor ?? string.Empty);
        }

        private async Task LoadDeviceModelAsync(
            uint deviceIndex,
            OpenVrSceneDevice device,
            string runtimePath,
            int version)
        {
            if (string.IsNullOrWhiteSpace(device.RenderModelName))
            {
                return;
            }

            if (!modelCache.TryGetValue(device.RenderModelName, out Task<OpenVrRenderModel> task))
            {
                task = Task.Run(() =>
                {
                    try
                    {
                        return renderModelService.Load(runtimePath, device.RenderModelName);
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine("Device viewer model load failed: " + exception);
                        return null;
                    }
                });
                modelCache[device.RenderModelName] = task;
            }

            OpenVrRenderModel renderModel = await task;
            if (closed || !deviceVisuals.TryGetValue(deviceIndex, out DeviceVisual visual) ||
                visual.Version != version ||
                !string.Equals(visual.RenderModelName, device.RenderModelName, StringComparison.Ordinal))
            {
                return;
            }
            if (renderModel == null)
            {
                modelCache.Remove(device.RenderModelName);
                return;
            }

            ReplaceModelContents(
                visual.Model,
                DeviceViewerModelFactory.CreateDeviceModel(renderModel, device.DeviceKind));
            RefreshSelectionOutline(deviceIndex, visual);
        }

        private void RefreshSelectionOutline(uint deviceIndex, DeviceVisual visual)
        {
            RemoveSelectionOutline(visual);
            if (selectedDeviceIndex == deviceIndex)
            {
                RebuildSelectionOutline(visual, CalculateSelectionOutlineThicknessFor(visual));
            }
        }

        private void EnsureSelectionOutline(DeviceVisual visual)
        {
            if (visual.SelectionOutline != null)
            {
                return;
            }

            RebuildSelectionOutline(visual, CalculateSelectionOutlineThicknessFor(visual));
        }

        private void UpdateSelectionOutlineThickness()
        {
            if (!selectedDeviceIndex.HasValue ||
                !deviceVisuals.TryGetValue(selectedDeviceIndex.Value, out DeviceVisual visual))
            {
                return;
            }

            double thickness = CalculateSelectionOutlineThicknessFor(visual);
            if (visual.SelectionOutline != null && visual.SelectionOutlineThicknessMetres > 0)
            {
                double relativeChange = Math.Abs(
                    thickness - visual.SelectionOutlineThicknessMetres) /
                    visual.SelectionOutlineThicknessMetres;
                if (relativeChange < SelectionOutlineRebuildThreshold ||
                    DateTime.UtcNow - visual.SelectionOutlineUpdatedUtc <
                        SelectionOutlineRebuildInterval)
                {
                    return;
                }
            }

            RebuildSelectionOutline(visual, thickness);
        }

        private double CalculateSelectionOutlineThicknessFor(DeviceVisual visual)
        {
            Rect3D bounds = visual.ContentBounds.IsEmpty
                ? visual.Model.Bounds
                : visual.ContentBounds;
            Point3D localCentre = bounds.IsEmpty
                ? new Point3D()
                : new Point3D(
                    bounds.X + (bounds.SizeX / 2.0),
                    bounds.Y + (bounds.SizeY / 2.0),
                    bounds.Z + (bounds.SizeZ / 2.0));
            Transform3D transform = visual.Model.Transform;
            Point3D worldCentre = transform == null
                ? localCentre
                : transform.Transform(localCentre);
            Vector3D look = SceneCamera.LookDirection;
            if (look.LengthSquared < 1e-12)
            {
                look = new Vector3D(0, 0, -1);
            }
            look.Normalize();
            Vector3D centreOffset = worldCentre - SceneCamera.Position;
            double depth = Vector3D.DotProduct(centreOffset, look);
            if (depth <= 0.05)
            {
                depth = Math.Max(0.05, centreOffset.Length);
            }

            return CalculateSelectionOutlineThickness(
                depth,
                SceneViewport.ActualWidth,
                SceneCamera.FieldOfView);
        }

        internal static double CalculateSelectionOutlineThickness(
            double depthMetres,
            double viewportWidth,
            double fieldOfViewDegrees)
        {
            double safeWidth = Math.Max(1.0, viewportWidth);
            double fieldOfViewRadians = fieldOfViewDegrees * Math.PI / 180.0;
            double metresPerPixel =
                (2.0 * Math.Max(0.05, depthMetres) *
                    Math.Tan(fieldOfViewRadians / 2.0)) /
                safeWidth;
            return Math.Max(
                SelectionOutlineMinimumMetres,
                Math.Min(
                    SelectionOutlineMaximumMetres,
                    metresPerPixel * SelectionOutlineTargetPixels));
        }

        private static void RebuildSelectionOutline(
            DeviceVisual visual,
            double thicknessMetres)
        {
            RemoveSelectionOutline(visual);
            visual.ContentBounds = visual.Model.Bounds;

            Model3DGroup outline = DeviceViewerModelFactory.CreateSelectionOutline(
                visual.Model,
                thicknessMetres);
            if (outline.Children.Count == 0)
            {
                return;
            }
            visual.SelectionOutline = outline;
            visual.SelectionOutlineThicknessMetres = thicknessMetres;
            visual.SelectionOutlineUpdatedUtc = DateTime.UtcNow;
            visual.Model.Children.Add(outline);
        }

        private static void RemoveSelectionOutline(DeviceVisual visual)
        {
            if (visual.SelectionOutline == null)
            {
                return;
            }
            visual.Model.Children.Remove(visual.SelectionOutline);
            visual.SelectionOutline = null;
            visual.SelectionOutlineThicknessMetres = 0;
        }

        private static void ReplaceModelContents(Model3DGroup target, Model3DGroup source)
        {
            Transform3D transform = target.Transform;
            target.Children.Clear();
            foreach (Model3D child in source.Children)
            {
                target.Children.Add(child);
            }
            target.Transform = transform;
        }

        private void ClearVisibleScene()
        {
            backgroundSignature = string.Empty;
            backgroundVersion++;
            RemoveBackgroundModel();
            SceneInteractionSurface.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#0F0F0F"));
            foreach (DeviceVisual visual in deviceVisuals.Values)
            {
                visual.Model.Transform = new TranslateTransform3D(10000, 10000, 10000);
            }
            latestDevices.Clear();
            ClearDeviceSelection();
            initialFramePending = true;
        }

        private void SetWaitingStatus()
        {
            StatusText.Text = Tr.Get("device_viewer.status.waiting");
            StatusText.Foreground = FindBrush("WarningBrush");
            StatusText.ToolTip = null;
        }

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void ResetView()
        {
            Rect3D bounds = Rect3D.Empty;
            foreach (DeviceVisual visual in deviceVisuals.Values)
            {
                if (visual.Model.Transform is TranslateTransform3D hidden && hidden.OffsetX > 1000)
                {
                    continue;
                }
                AddTransformedBounds(ref bounds, visual.Model);
            }
            cameraYaw = 0.67;
            cameraPitch = 0.38;
            if (bounds.IsEmpty)
            {
                cameraTarget = new Point3D(0, 1, 0);
                cameraDistance = 4.0;
            }
            else
            {
                cameraTarget = new Point3D(
                    bounds.X + (bounds.SizeX / 2.0),
                    bounds.Y + (bounds.SizeY / 2.0),
                    bounds.Z + (bounds.SizeZ / 2.0));
                double largest = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
                cameraDistance = Math.Max(1.2, largest * 1.8);
            }
            UpdateCamera();
        }

        private static void AddTransformedBounds(ref Rect3D combined, Model3DGroup model)
        {
            Rect3D local = model.Bounds;
            if (local.IsEmpty)
            {
                return;
            }
            Transform3D transform = model.Transform;
            if (transform == null || transform.Value.IsIdentity)
            {
                combined.Union(local);
                return;
            }
            Matrix3D matrix = transform.Value;
            double[] xs = { local.X, local.X + local.SizeX };
            double[] ys = { local.Y, local.Y + local.SizeY };
            double[] zs = { local.Z, local.Z + local.SizeZ };
            foreach (double x in xs)
            foreach (double y in ys)
            foreach (double z in zs)
            {
                combined.Union(matrix.Transform(new Point3D(x, y, z)));
            }
        }

        private void SceneViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
            {
                return;
            }
            dragButton = e.ChangedButton;
            pointerDown = e.GetPosition(SceneInteractionSurface);
            lastPointer = pointerDown;
            pointerMoved = false;
            SceneInteractionSurface.CaptureMouse();
            e.Handled = true;
        }

        private void SceneViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragButton == null || !SceneInteractionSurface.IsMouseCaptured)
            {
                return;
            }
            Point current = e.GetPosition(SceneInteractionSurface);
            if ((current - pointerDown).Length > 4.0)
            {
                pointerMoved = true;
            }
            Vector delta = current - lastPointer;
            lastPointer = current;
            if (dragButton == MouseButton.Right)
            {
                cameraYaw -= delta.X * 0.008;
                cameraPitch = Math.Max(-1.35, Math.Min(1.35, cameraPitch + (delta.Y * 0.008)));
            }
            else
            {
                Vector3D look = cameraTarget - SceneCamera.Position;
                look.Normalize();
                Vector3D right = Vector3D.CrossProduct(look, SceneCamera.UpDirection);
                right.Normalize();
                Vector3D up = Vector3D.CrossProduct(right, look);
                up.Normalize();
                double scale = cameraDistance * 0.0017;
                cameraTarget += (-right * delta.X * scale) + (up * delta.Y * scale);
            }
            UpdateCamera();
        }

        private void SceneViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (dragButton == e.ChangedButton)
            {
                bool select = dragButton == MouseButton.Left && !pointerMoved;
                Point selectionPoint = e.GetPosition(SceneViewport);
                EndDrag();
                if (select)
                {
                    SelectDeviceAt(selectionPoint);
                }
                e.Handled = true;
            }
        }

        private void SceneViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            dragButton = null;
            pointerMoved = false;
        }

        private void SceneViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            cameraDistance = Math.Max(0.25, Math.Min(30.0, cameraDistance * Math.Exp(-e.Delta / 1200.0)));
            UpdateCamera();
            e.Handled = true;
        }

        private void EndDrag()
        {
            dragButton = null;
            if (SceneInteractionSurface.IsMouseCaptured)
            {
                SceneInteractionSurface.ReleaseMouseCapture();
            }
        }

        private void UpdateCamera()
        {
            double horizontal = cameraDistance * Math.Cos(cameraPitch);
            var offset = new Vector3D(
                horizontal * Math.Sin(cameraYaw),
                cameraDistance * Math.Sin(cameraPitch),
                horizontal * Math.Cos(cameraYaw));
            SceneCamera.Position = cameraTarget + offset;
            SceneCamera.LookDirection = cameraTarget - SceneCamera.Position;
            SceneCamera.UpDirection = new Vector3D(0, 1, 0);
            backgroundModel.Transform = new TranslateTransform3D(
                SceneCamera.Position.X,
                SceneCamera.Position.Y,
                SceneCamera.Position.Z);
            UpdateSelectionOutlineThickness();
        }

        private System.Windows.Media.Brush FindBrush(string key)
        {
            return (System.Windows.Media.Brush)FindResource(key);
        }

        private sealed class DeviceVisual
        {
            public DeviceVisual(
                Model3DGroup model,
                string renderModelName,
                TrackedDeviceKind deviceKind)
            {
                Model = model;
                ContentBounds = model.Bounds;
                RenderModelName = renderModelName;
                DeviceKind = deviceKind;
            }

            public Model3DGroup Model { get; }
            public string RenderModelName { get; set; }
            public TrackedDeviceKind DeviceKind { get; set; }
            public int Version { get; set; }
            public Model3DGroup SelectionOutline { get; set; }
            public Rect3D ContentBounds { get; set; }
            public double SelectionOutlineThicknessMetres { get; set; }
            public DateTime SelectionOutlineUpdatedUtc { get; set; }
        }
    }
}
