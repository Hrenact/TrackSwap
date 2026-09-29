using System;
using System.IO;
using TrackSwap.Services;

namespace TrackSwap.Tests;

public sealed class TrackSwapDataCleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrackSwap-data-cleanup-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CleanRemovesOwnedDataAcrossLocationsAndPreservesUnknownFiles()
    {
        string preferred = Path.Combine(_root, "install", "UserData");
        string legacy = Path.Combine(_root, "local", "TrackSwap");
        Directory.CreateDirectory(Path.Combine(preferred, "Backups", "Automatic"));
        Directory.CreateDirectory(Path.Combine(legacy, "Logs"));
        File.WriteAllText(Path.Combine(preferred, "runtime-config.json"), "{}");
        File.WriteAllText(
            Path.Combine(preferred, "Backups", "Automatic", "TrackSwap-test.trackswap-backup"),
            "backup");
        File.WriteAllText(Path.Combine(legacy, "ui-preferences.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "Logs", "runtime.log"), "log");
        File.WriteAllText(Path.Combine(legacy, "keep-me.txt"), "unknown");

        var service = new TrackSwapDataCleanupService(preferred, preferred, legacy);
        DataCleanupResult result = service.Clean();

        Assert.Equal(4, result.RemovedFileCount);
        Assert.False(Directory.Exists(preferred));
        Assert.True(File.Exists(Path.Combine(legacy, "keep-me.txt")));
        Assert.False(File.Exists(Path.Combine(legacy, "ui-preferences.json")));
        Assert.False(Directory.Exists(Path.Combine(legacy, "Logs")));
    }

    [Fact]
    public void CleanDeduplicatesActiveAndPreferredDirectory()
    {
        string preferred = Path.Combine(_root, "install", "UserData");
        string legacy = Path.Combine(_root, "local", "TrackSwap");
        Directory.CreateDirectory(preferred);
        File.WriteAllText(Path.Combine(preferred, "device-history.json"), "{}");

        var service = new TrackSwapDataCleanupService(preferred, preferred, legacy);
        DataCleanupResult result = service.Clean();

        Assert.Equal(1, result.RemovedFileCount);
        Assert.False(Directory.Exists(preferred));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
