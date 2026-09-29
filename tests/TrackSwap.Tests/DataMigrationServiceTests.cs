using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TrackSwap.Protocol;
using TrackSwap.Services;

namespace TrackSwap.Tests;

public sealed class DataMigrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrackSwap.Storage.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MigrationValidatesCopiesAndPreservesExistingDestination()
    {
        string legacy = Path.Combine(_root, "legacy");
        string preferred = Path.Combine(_root, "install", "UserData");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(preferred);
        File.WriteAllText(
            Path.Combine(legacy, "runtime-config.json"),
            JsonConvert.SerializeObject(new RuntimeConfiguration { Revision = 7 }));
        File.WriteAllText(Path.Combine(legacy, "ui-preferences.json"), "{}");
        File.WriteAllText(Path.Combine(preferred, "keep.txt"), "destination data");
        var service = CreateService(legacy, preferred, () => legacy);

        MigrationResult result = service.Migrate(replaceExisting: false);

        Assert.Equal(preferred, result.TargetDirectory);
        Assert.True(File.Exists(Path.Combine(preferred, "runtime-config.json")));
        string preserved = Directory.GetDirectories(
            Path.Combine(preferred, "Backups"),
            "PreMigrationFiles-*",
            SearchOption.TopDirectoryOnly).Single();
        Assert.Equal("destination data", File.ReadAllText(Path.Combine(preserved, "keep.txt")));
    }

    [Fact]
    public void CleanupRemovesOnlyRecognizedFilesAndPreservesUnknownFiles()
    {
        string legacy = Path.Combine(_root, "legacy");
        string preferred = Path.Combine(_root, "install", "UserData");
        string steamVrBackups = Path.Combine(legacy, "Backups", "SteamVR");
        Directory.CreateDirectory(steamVrBackups);
        Directory.CreateDirectory(preferred);
        File.WriteAllText(Path.Combine(legacy, "runtime-config.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "unknown.txt"), "keep");
        File.WriteAllText(
            Path.Combine(steamVrBackups, "steamvr.vrsettings.trackswap-1.backup"),
            "owned");
        File.WriteAllText(Path.Combine(steamVrBackups, "unknown.bin"), "keep");
        var service = CreateService(legacy, preferred, () => preferred);

        LegacyCleanupResult result = service.CleanLegacyData();

        Assert.Equal(2, result.RemovedFileCount);
        Assert.False(result.DirectoryRemoved);
        Assert.False(File.Exists(Path.Combine(legacy, "runtime-config.json")));
        Assert.True(File.Exists(Path.Combine(legacy, "unknown.txt")));
        Assert.True(File.Exists(Path.Combine(steamVrBackups, "unknown.bin")));
    }

    [Fact]
    public void MigrationIsUnavailableWhenPreferredDirectoryIsNotWritable()
    {
        string legacy = Path.Combine(_root, "legacy");
        string preferred = Path.Combine(_root, "install", "UserData");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "runtime-config.json"), "{}");
        var service = new DataMigrationService(legacy, preferred, () => legacy, () => false);

        Assert.False(service.CanMigrate);
    }

    private static DataMigrationService CreateService(
        string legacy,
        string preferred,
        Func<string> activeDirectoryProvider)
    {
        return new DataMigrationService(legacy, preferred, activeDirectoryProvider, () => true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
