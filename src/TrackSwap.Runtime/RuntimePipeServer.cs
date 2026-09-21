using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class RuntimePipeServer
{
    private readonly ConfigurationStore store;
    private readonly DriverSynchronizer synchronizer;
    private readonly CalibrationProfileStore profileStore;
    private readonly OpenVrCalibrationService calibrationService;
    private readonly TelemetrySampler telemetrySampler;
    private readonly string pipeName;
    private readonly Action? requestShutdown;
    private RuntimeConfiguration configuration;

    public RuntimePipeServer(
        ConfigurationStore store,
        DriverSynchronizer synchronizer,
        RuntimeConfiguration initialConfiguration,
        CalibrationProfileStore profileStore,
        OpenVrCalibrationService calibrationService,
        TelemetrySampler telemetrySampler,
        string? pipeName = null,
        Action? requestShutdown = null)
    {
        this.store = store;
        this.synchronizer = synchronizer;
        this.profileStore = profileStore;
        this.calibrationService = calibrationService;
        this.telemetrySampler = telemetrySampler;
        this.pipeName = pipeName ?? ProtocolConstants.PipeName;
        this.requestShutdown = requestShutdown;
        configuration = initialConfiguration;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A connected client did not complete its frame within the bounded read window.
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException || exception is InvalidDataException)
            {
                var error = new MessageEnvelope
                {
                    MessageType = "error",
                    RequestId = string.Empty,
                    PayloadJson = JsonConvert.SerializeObject(new { error = exception.Message }, RuntimeJson.Settings)
                };
                byte[] bytes = Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(error, RuntimeJson.Settings) + "\n");
                try
                {
                    await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var frameCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        frameCancellation.CancelAfter(TimeSpan.FromSeconds(2));
        string line = await ReadBoundedLineAsync(stream, frameCancellation.Token).ConfigureAwait(false);
        MessageEnvelope? request = JsonConvert.DeserializeObject<MessageEnvelope>(line, RuntimeJson.Settings);
        if (request == null || request.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion ||
            string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new InvalidDataException("Invalid runtime control envelope.");
        }

        MessageEnvelope response;
        try
        {
            response = request.MessageType switch
            {
                "applyConfiguration" => ApplyConfiguration(request),
                "getStatus" => CreateStatus(request.RequestId),
                "getTelemetry" => GetTelemetry(request),
                "captureCalibration" => CaptureCalibration(request, cancellationToken),
                "listCalibrationProfiles" => ListCalibrationProfiles(request.RequestId),
                "deleteCalibrationProfile" => DeleteCalibrationProfile(request),
                "shutdown" => CreateShutdownAccepted(request.RequestId),
                _ => throw new InvalidDataException($"Unknown runtime message type '{request.MessageType}'.")
            };
        }
        catch (Exception exception) when (
            exception is JsonException ||
            exception is InvalidDataException ||
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is DllNotFoundException ||
            exception is BadImageFormatException)
        {
            response = new MessageEnvelope
            {
                MessageType = "error",
                RequestId = request.RequestId,
                PayloadJson = JsonConvert.SerializeObject(new { error = exception.Message }, RuntimeJson.Settings)
            };
        }
        string json = JsonConvert.SerializeObject(response, RuntimeJson.Settings) + "\n";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(request.MessageType, "shutdown", StringComparison.Ordinal))
        {
            requestShutdown?.Invoke();
        }
    }

    private MessageEnvelope CaptureCalibration(
        MessageEnvelope request,
        CancellationToken cancellationToken)
    {
        CalibrationCaptureRequest? captureRequest =
            JsonConvert.DeserializeObject<CalibrationCaptureRequest>(request.PayloadJson, RuntimeJson.Settings);
        if (captureRequest == null)
        {
            throw new InvalidDataException("Calibration request is required.");
        }
        CalibrationProfile profile = calibrationService.Capture(captureRequest, cancellationToken);
        profileStore.Add(profile);
        return new MessageEnvelope
        {
            MessageType = "calibrationCaptured",
            RequestId = request.RequestId,
            PayloadJson = JsonConvert.SerializeObject(
                new CalibrationCaptureResponse { Profile = profile }, RuntimeJson.Settings)
        };
    }

    private MessageEnvelope ListCalibrationProfiles(string requestId)
    {
        return new MessageEnvelope
        {
            MessageType = "calibrationProfiles",
            RequestId = requestId,
            PayloadJson = JsonConvert.SerializeObject(profileStore.Load(), RuntimeJson.Settings)
        };
    }

    private MessageEnvelope GetTelemetry(MessageEnvelope request)
    {
        TelemetryRequest? telemetryRequest =
            JsonConvert.DeserializeObject<TelemetryRequest>(request.PayloadJson, RuntimeJson.Settings);
        if (telemetryRequest == null || telemetryRequest.VirtualDeviceSlot < 0 ||
            telemetryRequest.VirtualDeviceSlot >= ProtocolConstants.MaximumRoutes)
        {
            throw new InvalidDataException("A valid virtual device slot is required for telemetry.");
        }
        PoseTelemetrySnapshot snapshot = telemetrySampler.GetLatest(telemetryRequest.VirtualDeviceSlot);
        return new MessageEnvelope
        {
            MessageType = "telemetry",
            RequestId = request.RequestId,
            PayloadJson = JsonConvert.SerializeObject(snapshot, RuntimeJson.Settings)
        };
    }

    private MessageEnvelope DeleteCalibrationProfile(MessageEnvelope request)
    {
        CalibrationProfileRequest? profileRequest =
            JsonConvert.DeserializeObject<CalibrationProfileRequest>(request.PayloadJson, RuntimeJson.Settings);
        if (profileRequest == null || string.IsNullOrWhiteSpace(profileRequest.ProfileId))
        {
            throw new InvalidDataException("Calibration profile id is required.");
        }
        if (!profileStore.Delete(profileRequest.ProfileId))
        {
            throw new InvalidDataException("Calibration profile was not found.");
        }
        return new MessageEnvelope
        {
            MessageType = "calibrationProfileDeleted",
            RequestId = request.RequestId,
            PayloadJson = "{}"
        };
    }

    private MessageEnvelope ApplyConfiguration(MessageEnvelope request)
    {
        RuntimeConfiguration? candidate = JsonConvert.DeserializeObject<RuntimeConfiguration>(
            request.PayloadJson,
            RuntimeJson.Settings);
        IReadOnlyList<string> errors = ConfigurationValidator.Validate(candidate);
        if (candidate == null || errors.Count != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
        if (candidate.Revision <= configuration.Revision)
        {
            throw new InvalidDataException("Configuration revision must increase monotonically.");
        }
        store.Save(candidate);
        configuration = candidate;
        synchronizer.Update(candidate);
        return new MessageEnvelope
        {
            MessageType = "configurationApplied",
            RequestId = request.RequestId,
            PayloadJson = JsonConvert.SerializeObject(new { candidate.Revision }, RuntimeJson.Settings)
        };
    }

    private MessageEnvelope CreateStatus(string requestId)
    {
        DriverSynchronizationStatus driverStatus = synchronizer.GetStatus();
        var status = new RuntimeStatusSnapshot
        {
            ConfigurationRevision = configuration.Revision,
            DriverAppliedRevision = driverStatus.AppliedRevision,
            DriverConnected = driverStatus.IsConnected,
            LastError = driverStatus.LastError,
            Configuration = configuration
        };
        return new MessageEnvelope
        {
            MessageType = "status",
            RequestId = requestId,
            PayloadJson = JsonConvert.SerializeObject(status, RuntimeJson.Settings)
        };
    }

    private static MessageEnvelope CreateShutdownAccepted(string requestId)
    {
        return new MessageEnvelope
        {
            MessageType = "shutdownAccepted",
            RequestId = requestId,
            PayloadJson = "{}"
        };
    }

    private static async Task<string> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < ProtocolConstants.MaximumMessageBytes)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Runtime control connection closed before a complete frame.");
            }
            if (buffer[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
            bytes.Add(buffer[0]);
        }
        throw new InvalidDataException("Runtime control frame exceeds the configured limit.");
    }
}
