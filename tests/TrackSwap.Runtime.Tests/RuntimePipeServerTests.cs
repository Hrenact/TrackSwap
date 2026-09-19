using System.IO.Pipes;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class RuntimePipeServerTests
{
    [Fact]
    public async Task IdleClientDoesNotPermanentlyBlockLaterRequests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrackSwap.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new RuntimeConfiguration();
            var store = new ConfigurationStore(Path.Combine(directory, "runtime-config.json"));
            var server = new RuntimePipeServer(
                store,
                new DriverSynchronizer(configuration),
                configuration,
                new CalibrationProfileStore(Path.Combine(directory, "profiles.json")),
                new OpenVrCalibrationService(),
                new TelemetrySampler());
            using var cancellation = new CancellationTokenSource();
            Task serverTask = server.RunAsync(cancellation.Token);

            using (var idleClient = new NamedPipeClientStream(
                ".",
                ProtocolConstants.PipeName,
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
            }, TimeSpan.FromSeconds(2));

            Assert.Equal("status", response.MessageType);
            Assert.NotNull(JsonConvert.DeserializeObject<RuntimeStatusSnapshot>(response.PayloadJson));

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await serverTask);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
