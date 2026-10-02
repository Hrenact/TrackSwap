using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TrackSwap.Models;
using TrackSwap.Services;
using Xunit;

namespace TrackSwap.Tests
{
    public sealed class DeviceHistoryServiceTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "TrackSwap.Tests",
            Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(_directory, "device-history.json");

        [Fact]
        public void RememberMigratesLegacyArrayAndRecordsReceiver()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(
                FilePath,
                "[{\"DisplayName\":\"Tracker A\",\"DevicePath\":\"/devices/lighthouse/LHR-A\",\"SerialNumber\":\"LHR-A\",\"DeviceKind\":3}]");
            var service = new DeviceHistoryService(FilePath);

            service.Remember(new[] { CreateDevice("Tracker A", "LHR-A", "dongle-1") });

            DeviceManagementCatalog catalog = service.LoadCatalog();
            Assert.Single(catalog.Devices);
            Assert.Equal("dongle-1", catalog.Devices[0].LastConnectedReceiverId);
            Assert.Equal("dongle-1", Assert.Single(catalog.Receivers).ReceiverId);
            Assert.Equal(2, JObject.Parse(File.ReadAllText(FilePath)).Value<int>("SchemaVersion"));
        }

        [Fact]
        public void CustomMetadataDecoratesOnlineDeviceWithoutChangingStableIdentity()
        {
            var service = new DeviceHistoryService(FilePath);
            DeviceOption device = CreateDevice("Tracker A", "LHR-A", "dongle-1");
            service.Remember(new[] { device });

            service.UpdateDeviceMetadata(device.DevicePath, "腰部", "蓝色绑带");
            service.UpdateReceiverMetadata("dongle-1", "接收器 1", "机箱背面");

            ManagedDeviceRecord storedDevice = Assert.Single(service.LoadCatalog().Devices);
            ManagedReceiverRecord storedReceiver = Assert.Single(service.LoadCatalog().Receivers);
            DeviceOption decorated = Assert.Single(service.ApplyCustomNames(new[] { device }));
            Assert.Equal("腰部", storedDevice.DisplayName);
            Assert.Equal("Tracker A · LHR-A", storedDevice.HardwareDisplayName);
            Assert.Equal("蓝色绑带", storedDevice.Note);
            Assert.Equal("接收器 1", storedReceiver.DisplayName);
            Assert.Equal("机箱背面", storedReceiver.Note);
            Assert.Equal(device.DevicePath, decorated.DevicePath);
            Assert.Equal("腰部", decorated.DisplayName);
            Assert.Equal("dongle-1", decorated.ConnectedWirelessDongleId);
        }

        [Fact]
        public void RememberUpdatesLastReceiverAndRetainsBothReceiverRecords()
        {
            var service = new DeviceHistoryService(FilePath);

            service.Remember(new[] { CreateDevice("Tracker A", "LHR-A", "dongle-1") });
            service.Remember(new[] { CreateDevice("Tracker A", "LHR-A", "dongle-2") });

            DeviceManagementCatalog catalog = service.LoadCatalog();
            Assert.Equal("dongle-2", Assert.Single(catalog.Devices).LastConnectedReceiverId);
            Assert.Equal(
                new[] { "dongle-1", "dongle-2" },
                catalog.Receivers.Select(receiver => receiver.ReceiverId).OrderBy(value => value).ToArray());
        }

        private static DeviceOption CreateDevice(string model, string serial, string receiverId)
        {
            return new DeviceOption(
                model + " · " + serial,
                "/devices/lighthouse/" + serial,
                true,
                1,
                serial,
                null,
                "render-model",
                TrackedDeviceKind.Tracker,
                connectedWirelessDongleId: receiverId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
