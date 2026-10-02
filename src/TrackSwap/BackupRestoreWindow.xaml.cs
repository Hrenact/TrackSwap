using System;
using TrackSwap.Localization;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TrackSwap.Models;
using TrackSwap.Services;

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
                ? Tr.Get("steamvr.restore.stop_first")
                : string.Empty;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_statusService.IsRunning())
            {
                AppDialog.Show(this, Tr.Get("backup.restore.restore_button_click.exit_steamvr_exit_restore"), Tr.Get("steamvr.status.running"), MessageBoxButton.OK, MessageBoxImage.Warning);
                RuntimeWarningText.Text = Tr.Get("steamvr.restore.stop_first");
                return;
            }

            BackupOption backup = (sender as Button)?.Tag as BackupOption;
            if (backup == null || !backup.IsValid)
            {
                return;
            }

            MessageBoxResult result = AppDialog.Show(
                this,
                Tr.Format("backup.restore.confirmation", backup.DisplayTime, backup.Summary),
                Tr.Get("backup.restore.restore_button_click.confirm_restore"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                string safetyBackup = _settingsService.RestoreTrackingOverrides(_settingsPath, backup.FilePath);
                AppDialog.Show(
                    this,
                    Tr.Format("backup.restore.completed_message", Path.GetFileName(safetyBackup)),
                    Tr.Get("backup.restore.restore_button_click.restore_complete"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception exception)
            {
                AppDialog.Show(this, exception.Message, Tr.Get("backup.restore.restore_button_click.restore_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
