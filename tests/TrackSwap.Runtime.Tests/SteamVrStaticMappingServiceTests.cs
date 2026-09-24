using Newtonsoft.Json.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class SteamVrStaticMappingServiceTests
{
    [Fact]
    public void ReconcilesDesiredMappingsAndPreservesUnrelatedSettings()
    {
        using var fixture = new SteamVrSettingsFixture();
        fixture.WriteSettings("""
            {
              "steamvr": { "showAdvancedSettings": true },
              "TrackingOverrides": {
                "/devices/trackswap/TRKSWAP-PROXY-00": "/devices/old/target",
                "/devices/unrelated/source": "/devices/unrelated/target"
              }
            }
            """);
        var configuration = new RuntimeConfiguration
        {
            Revision = 7,
            Routes =
            {
                CreateReplacementRoute("route-1", slot: 1, "/devices/new/target")
            }
        };
        var service = new SteamVrStaticMappingService(() => fixture.SettingsPath, () => false);

        StaticMappingReconciliationResult result = service.Reconcile(configuration);

        Assert.True(result.IsCompleted);
        JObject root = JObject.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.True((bool?)root["steamvr"]?["showAdvancedSettings"]);
        Assert.Null(root["TrackingOverrides"]?["/devices/trackswap/TRKSWAP-PROXY-00"]);
        Assert.Equal(
            "/devices/new/target",
            (string?)root["TrackingOverrides"]?["/devices/trackswap/TRKSWAP-PROXY-01"]);
        Assert.Equal(
            "/devices/unrelated/target",
            (string?)root["TrackingOverrides"]?["/devices/unrelated/source"]);
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath, "*.trackswap-*.backup"));
    }

    [Fact]
    public void CompletesPendingDeletionBeforeOtherMappingChanges()
    {
        using var fixture = new SteamVrSettingsFixture();
        fixture.WriteSettings("""
            {
              "TrackingOverrides": {
                "/devices/trackswap/TRKSWAP-PROXY-00": "/devices/old/left",
                "/devices/trackswap/TRKSWAP-PROXY-01": "/devices/old/right"
              }
            }
            """);
        RouteConfiguration deleting = CreateReplacementRoute("delete-me", 0, "/devices/old/left");
        deleting.PendingDeletion = true;
        var configuration = new RuntimeConfiguration
        {
            Revision = 9,
            Routes =
            {
                deleting,
                CreateReplacementRoute("keep-me", 1, "/devices/new/right")
            }
        };
        var service = new SteamVrStaticMappingService(() => fixture.SettingsPath, () => false);

        StaticMappingReconciliationResult result = service.Reconcile(configuration);

        Assert.False(result.IsCompleted);
        Assert.Equal(new[] { "delete-me" }, result.DeletedRouteIds);
        JObject root = JObject.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Null(root["TrackingOverrides"]?["/devices/trackswap/TRKSWAP-PROXY-00"]);
        Assert.Equal(
            "/devices/old/right",
            (string?)root["TrackingOverrides"]?["/devices/trackswap/TRKSWAP-PROXY-01"]);
    }

    [Fact]
    public void DefersEveryWriteWhileSteamVrIsRunning()
    {
        using var fixture = new SteamVrSettingsFixture();
        const string original = "{ \"TrackingOverrides\": {} }";
        fixture.WriteSettings(original);
        var configuration = new RuntimeConfiguration
        {
            Revision = 1,
            Routes = { CreateReplacementRoute("route", 0, "/devices/target") }
        };
        var service = new SteamVrStaticMappingService(() => fixture.SettingsPath, () => true);

        StaticMappingReconciliationResult result = service.Reconcile(configuration);

        Assert.False(result.IsCompleted);
        Assert.Equal(original, File.ReadAllText(fixture.SettingsPath));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.backup"));
    }

    [Fact]
    public void MissingSteamVrSettingsDoesNotCreatePermanentWorkWithoutReplacementRoutes()
    {
        using var fixture = new SteamVrSettingsFixture();
        var service = new SteamVrStaticMappingService(() => fixture.SettingsPath, () => false);

        StaticMappingReconciliationResult result = service.Reconcile(new RuntimeConfiguration());

        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void RuntimeServerRemovesPendingRouteOnlyAfterStaticCleanupSucceeds()
    {
        using var fixture = new SteamVrSettingsFixture();
        fixture.WriteSettings("""
            {
              "TrackingOverrides": {
                "/devices/trackswap/TRKSWAP-PROXY-02": "/devices/target"
              }
            }
            """);
        RouteConfiguration deleting = CreateReplacementRoute("delete-me", 2, "/devices/target");
        deleting.PendingDeletion = true;
        var configuration = new RuntimeConfiguration
        {
            Revision = 12,
            Routes = { deleting }
        };
        var store = new ConfigurationStore(Path.Combine(fixture.DirectoryPath, "runtime-config.json"));
        store.Save(configuration);
        var server = new RuntimePipeServer(
            store,
            new DriverSynchronizer(configuration),
            configuration,
            new CalibrationProfileStore(Path.Combine(fixture.DirectoryPath, "profiles.json")),
            new OpenVrCalibrationService(),
            new TelemetrySampler());
        var service = new SteamVrStaticMappingService(() => fixture.SettingsPath, () => false);

        StaticMappingReconciliationResult result = server.ReconcileStaticMappings(service);

        Assert.False(result.IsCompleted);
        RuntimeConfiguration saved = store.Load();
        Assert.Empty(saved.Routes);
        Assert.True(saved.Revision > configuration.Revision);
        JObject root = JObject.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Null(root["TrackingOverrides"]?["/devices/trackswap/TRKSWAP-PROXY-02"]);
    }

    private static RouteConfiguration CreateReplacementRoute(string id, int slot, string target)
    {
        return new RouteConfiguration
        {
            RouteId = id,
            Name = id,
            Enabled = true,
            Mode = RouteMode.ReplaceTarget,
            VirtualDeviceSlot = slot,
            SourceDevicePath = "/devices/source/" + id,
            TargetDevicePath = target,
            Offset = PoseOffset.Identity()
        };
    }

    private sealed class SteamVrSettingsFixture : IDisposable
    {
        public SteamVrSettingsFixture()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "TrackSwap.Runtime.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            SettingsPath = Path.Combine(DirectoryPath, "steamvr.vrsettings");
        }

        public string DirectoryPath { get; }
        public string SettingsPath { get; }

        public void WriteSettings(string contents)
        {
            File.WriteAllText(SettingsPath, contents);
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
