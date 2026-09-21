using System.IO.Pipes;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class RuntimePipeServerTests
{
    [Fact]
    public async Task AcceptsGracefulShutdownRequest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrackSwap.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string pipeName = "TrackSwap.Runtime.Tests." + Guid.NewGuid().ToString("N");
            bool shutdownRequested = false;
            var configuration = new RuntimeConfiguration();
            var server = new RuntimePipeServer(
                new ConfigurationStore(Path.Combine(directory, "runtime-config.json")),
                new DriverSynchronizer(configuration),
                configuration,
                new CalibrationProfileStore(Path.Combine(directory, "profiles.json")),
                new OpenVrCalibrationService(),
                new TelemetrySampler(),
                pipeName,
                () => shutdownRequested = true);
            using var cancellation = new CancellationTokenSource();
            Task serverTask = server.RunAsync(cancellation.Token);

            MessageEnvelope response = RuntimeControlClient.Send(new MessageEnvelope
            {
                MessageType = "shutdown",
                RequestId = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}"
            }, TimeSpan.FromSeconds(2), pipeName);

            Assert.Equal("shutdownAccepted", response.MessageType);
            Assert.True(SpinWait.SpinUntil(() => shutdownRequested, TimeSpan.FromSeconds(1)));
            cancellation.Cancel();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsMultipleEnabledRoutesWithStableSlots()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrackSwap.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string pipeName = "TrackSwap.Runtime.Tests." + Guid.NewGuid().ToString("N");
            var initial = new RuntimeConfiguration();
            var server = new RuntimePipeServer(
                new ConfigurationStore(Path.Combine(directory, "runtime-config.json")),
                new DriverSynchronizer(initial),
                initial,
                new CalibrationProfileStore(Path.Combine(directory, "profiles.json")),
                new OpenVrCalibrationService(),
                new TelemetrySampler(),
                pipeName);
            using var cancellation = new CancellationTokenSource();
            Task serverTask = server.RunAsync(cancellation.Token);
            var configuration = new RuntimeConfiguration
            {
                Revision = 1,
                Routes =
                {
                    new RouteConfiguration
                    {
                        RouteId = "right",
                        Name = "右手定位",
                        VirtualDeviceSlot = 0,
                        SourceDevicePath = "/devices/source/right",
                        TargetDevicePath = ProtocolConstants.RightHandRolePath,
                        Offset = PoseOffset.Identity()
                    },
                    new RouteConfiguration
                    {
                        RouteId = "left",
                        Name = "左手定位",
                        PendingDeletion = true,
                        VirtualDeviceSlot = 1,
                        SourceDevicePath = "/devices/source/left",
                        TargetDevicePath = ProtocolConstants.LeftHandRolePath,
                        Offset = PoseOffset.Identity()
                    }
                }
            };

            MessageEnvelope response = RuntimeControlClient.Send(new MessageEnvelope
            {
                MessageType = "applyConfiguration",
                RequestId = Guid.NewGuid().ToString("N"),
                PayloadJson = JsonConvert.SerializeObject(configuration)
            }, TimeSpan.FromSeconds(2), pipeName);

            Assert.Equal("configurationApplied", response.MessageType);
            MessageEnvelope statusResponse = RuntimeControlClient.Send(new MessageEnvelope
            {
                MessageType = "getStatus",
                RequestId = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}"
            }, TimeSpan.FromSeconds(2), pipeName);
            RuntimeStatusSnapshot? status = JsonConvert.DeserializeObject<RuntimeStatusSnapshot>(statusResponse.PayloadJson);
            Assert.NotNull(status);
            Assert.True(status.Configuration.Routes.Single(route => route.RouteId == "left").PendingDeletion);
            cancellation.Cancel();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IdleClientDoesNotPermanentlyBlockLaterRequests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrackSwap.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string pipeName = "TrackSwap.Runtime.Tests." + Guid.NewGuid().ToString("N");
            var configuration = new RuntimeConfiguration();
            var store = new ConfigurationStore(Path.Combine(directory, "runtime-config.json"));
            var server = new RuntimePipeServer(
                store,
                new DriverSynchronizer(configuration),
                configuration,
                new CalibrationProfileStore(Path.Combine(directory, "profiles.json")),
                new OpenVrCalibrationService(),
                new TelemetrySampler(),
                pipeName);
            using var cancellation = new CancellationTokenSource();
            Task serverTask = server.RunAsync(cancellation.Token);

            using (var idleClient = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                await idleClient.ConnectAsync(2000);
                await Task.Delay(2200);
            }

            MessageEnvelope response = RuntimeControlClient.Send(new MessageEnvelope
            {
                MessageType = "getStatus",
                RequestId = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}"
            }, TimeSpan.FromSeconds(2), pipeName);

            Assert.Equal("status", response.MessageType);
            Assert.NotNull(JsonConvert.DeserializeObject<RuntimeStatusSnapshot>(response.PayloadJson));

            cancellation.Cancel();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
