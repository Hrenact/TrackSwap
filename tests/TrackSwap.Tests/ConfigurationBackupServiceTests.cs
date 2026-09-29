using System;
using System.IO;
using TrackSwap.Protocol;
using TrackSwap.Services;

namespace TrackSwap.Tests;

public sealed class ConfigurationBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "TrackSwap.Backup.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExportAndReadRoundTripRuntimeAndUiSettings()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.trackswap-backup");
        var configuration = new RuntimeConfiguration
        {
            Revision = 42,
            Osc = new OscConfiguration { ListenAddress = "127.0.0.2" }
        };
        var preferences = new UiPreferences
        {
            ShowProxyInPreview = true,
            RuntimeLifecycleMode = RuntimeLifecycleMode.FollowSteamVr
        };
        var service = new ConfigurationBackupService();

        service.Export(path, configuration, preferences);
        ConfigurationBackupFile restored = service.Read(path);

        Assert.Equal(42, restored.RuntimeConfiguration.Revision);
        Assert.Equal("127.0.0.2", restored.RuntimeConfiguration.Osc.ListenAddress);
        Assert.True(restored.UiPreferences.ShowProxyInPreview);
        Assert.Equal(RuntimeLifecycleMode.FollowSteamVr, restored.UiPreferences.RuntimeLifecycleMode);
    }

    [Fact]
    public void ReadRejectsInvalidBackup()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "invalid.trackswap-backup");
        File.WriteAllText(path, "{\"schemaVersion\":99}");

        Assert.Throws<InvalidDataException>(() => new ConfigurationBackupService().Read(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
