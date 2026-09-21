using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TrackSwap.Models;
using TrackSwap.Services;
using MessageBox = TrackSwap.AppDialog;

namespace TrackSwap
{
    public partial class BackupRestoreWindow : Window
    {
        private readonly string _settingsPath;
        private readonly SteamVrSettingsService _settingsService;
        private readonly SteamVrStatusService _statusService;

        public BackupRestoreWindow(
            string settingsPath,
            SteamVrSettingsService settingsService,
            SteamVrStatusService statusService)
        {
            InitializeComponent();
            _settingsPath = settingsPath;
            _settingsService = settingsService;
            _statusService = statusService;

            RefreshBackups();
        }

        private void RefreshBackups()
        {
            IReadOnlyList<BackupOption> backups = _settingsService.ReadBackups(_settingsPath);
            BackupsItemsControl.ItemsSource = backups;
            EmptyBackupsText.Visibility = backups.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RuntimeWarningText.Text = _statusService.IsRunning()
                ? "请先退出 SteamVR 后再恢复"
                : string.Empty;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_statusService.IsRunning())
            {
                MessageBox.Show(this, "请先完全退出 SteamVR，避免退出时覆盖恢复结果。", "SteamVR 正在运行", MessageBoxButton.OK, MessageBoxImage.Warning);
                RuntimeWarningText.Text = "请先退出 SteamVR 后再恢复";
                return;
            }

            BackupOption backup = (sender as Button)?.Tag as BackupOption;
            if (backup == null || !backup.IsValid)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                this,
                "恢复 " + backup.DisplayTime + " 的位姿配置？\n\n"
                    + backup.Summary + "\n\n"
                    + "只会替换 TrackingOverrides；当前配置会先自动备份。",
                "确认恢复",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                string safetyBackup = _settingsService.RestoreTrackingOverrides(_settingsPath, backup.FilePath);
                MessageBox.Show(
                    this,
                    "位姿配置已恢复。\n\n恢复前快照：" + Path.GetFileName(safetyBackup),
                    "恢复完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
