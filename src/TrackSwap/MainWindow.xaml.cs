using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TrackSwap.Models;
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
        private readonly DispatcherTimer _statusTimer;

        private string _settingsPath;
        private bool _isLoading;

        public MainWindow()
        {
            InitializeComponent();

            TargetComboBox.ItemsSource = BuildTargets(Array.Empty<DeviceOption>(), Array.Empty<TargetOption>());
            TargetComboBox.SelectedIndex = 0;

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += (_, __) => UpdateRuntimeStatus();

            Loaded += (_, __) =>
            {
                RefreshAll();
                _statusTimer.Start();
            };
            Closed += (_, __) => _statusTimer.Stop();
        }

        private void RefreshAll()
        {
            _isLoading = true;
            try
            {
                string previousTargetPath = (TargetComboBox.SelectedItem as TargetOption)?.TargetPath;
                _settingsPath = _pathService.FindSettingsPath();
                SettingsPathText.Text = _settingsPath ?? "未找到 steamvr.vrsettings";
                ViewRawButton.IsEnabled = !string.IsNullOrWhiteSpace(_settingsPath) && File.Exists(_settingsPath);
                RestoreBackupButton.IsEnabled = ViewRawButton.IsEnabled;

                if (!ViewRawButton.IsEnabled)
                {
                    SourceComboBox.ItemsSource = Array.Empty<DeviceOption>();
                    return;
                }

                IReadOnlyList<DeviceOption> savedSources = _settingsService.ReadKnownSources(_settingsPath);
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

                IReadOnlyList<TargetOption> savedDeviceTargets = _settingsService.ReadKnownDeviceTargets(_settingsPath);
                IReadOnlyList<TargetOption> targets = BuildTargets(onlineSources, savedDeviceTargets);
                TargetComboBox.ItemsSource = targets;
                TargetOption selectedTarget = targets.FirstOrDefault(target =>
                    string.Equals(target.TargetPath, previousTargetPath, StringComparison.Ordinal))
                    ?? targets.FirstOrDefault();
                TargetComboBox.SelectedItem = selectedTarget;

                string currentSource = selectedTarget == null
                    ? null
                    : _settingsService.ReadSourceForTarget(_settingsPath, selectedTarget.TargetPath);

                DeviceOption selectedSource = sources.FirstOrDefault(source =>
                    string.Equals(source.DevicePath, currentSource, StringComparison.Ordinal));
                SourceComboBox.SelectedItem = selectedSource ?? sources.FirstOrDefault();
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
                UpdateRuntimeStatus();
            }
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

        private void UpdateRuntimeStatus()
        {
            bool running = _statusService.IsRunning();
            RuntimeStatusText.Text = running ? "SteamVR 正在运行" : "SteamVR 已退出";
            RuntimeStatusText.Foreground = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            RuntimeDot.Fill = FindBrush(running ? "SuccessBrush" : "MutedTextBrush");
            RuntimeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#123225" : "#202A34"));

            ApplyButton.IsEnabled = !running
                && SourceComboBox.SelectedItem is DeviceOption
                && TargetComboBox.SelectedItem is TargetOption
                && !string.IsNullOrWhiteSpace(_settingsPath);
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
            UpdateRuntimeStatus();
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
                UpdateRuntimeStatus();
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
