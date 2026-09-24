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
    private readonly OscInputService? oscInput;
    private readonly XInputInputService? xInput;
    private readonly string pipeName;
    private readonly Action? requestShutdown;
    private readonly object configurationGate = new();
    private RuntimeConfiguration configuration;
    private StaticMappingReconciliationWorker? staticMappingWorker;

    public RuntimePipeServer(
        ConfigurationStore store,
        DriverSynchronizer synchronizer,
        RuntimeConfiguration initialConfiguration,
        CalibrationProfileStore profileStore,
        OpenVrCalibrationService calibrationService,
        TelemetrySampler telemetrySampler,
        string? pipeName = null,
        Action? requestShutdown = null,
        OscInputService? oscInput = null,
        XInputInputService? xInput = null)
    {
        this.store = store;
        this.synchronizer = synchronizer;
        this.profileStore = profileStore;
        this.calibrationService = calibrationService;
        this.telemetrySampler = telemetrySampler;
        this.oscInput = oscInput;
        this.xInput = xInput;
        this.pipeName = pipeName ?? ProtocolConstants.PipeName;
        this.requestShutdown = requestShutdown;
        configuration = initialConfiguration;
    }

    public void AttachStaticMappingWorker(StaticMappingReconciliationWorker worker)
    {
        staticMappingWorker = worker;
    }

    public long GetConfigurationRevision()
    {
        lock (configurationGate)
        {
            return configuration.Revision;
        }
    }

    public StaticMappingReconciliationResult ReconcileStaticMappings(
        SteamVrStaticMappingService mappingService)
    {
        lock (configurationGate)
        {
            StaticMappingReconciliationResult result = mappingService.Reconcile(configuration);
            if (result.DeletedRouteIds.Count == 0)
            {
                return result;
            }

            var deletedRouteIds = new HashSet<string>(result.DeletedRouteIds, StringComparer.Ordinal);
            RuntimeConfiguration candidate = CloneConfiguration(configuration);
            candidate.Routes.RemoveAll(route => deletedRouteIds.Contains(route.RouteId));
            candidate.Revision = Math.Max(DateTime.UtcNow.Ticks, configuration.Revision + 1);
            CommitConfiguration(candidate);
            return result;
        }
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
            throw new InvalidDataException("Runtime 控制消息无效。");
        }

        MessageEnvelope response;
        try
        {
            response = request.MessageType switch
            {
                "applyConfiguration" => ApplyConfiguration(request),
                "getStatus" => CreateStatus(request.RequestId),
                "getOscStatus" => CreateOscStatus(request.RequestId),
                "testOscHaptic" => TestOscHaptic(request, cancellationToken),
                "getXInputStatus" => CreateXInputStatus(request.RequestId),
                "getTelemetry" => GetTelemetry(request),
                "captureCalibration" => CaptureCalibration(request, cancellationToken),
                "listCalibrationProfiles" => ListCalibrationProfiles(request.RequestId),
                "deleteCalibrationProfile" => DeleteCalibrationProfile(request),
                "shutdown" => CreateShutdownAccepted(request.RequestId),
                _ => throw new InvalidDataException($"未知的 Runtime 消息类型：“{request.MessageType}”。")
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
            throw new InvalidDataException("缺少校准请求。");
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
            throw new InvalidDataException("读取遥测数据需要有效的虚拟设备槽位。");
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
            throw new InvalidDataException("缺少校准档案 ID。");
        }
        if (!profileStore.Delete(profileRequest.ProfileId))
        {
            throw new InvalidDataException("未找到校准档案。");
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
        lock (configurationGate)
        {
            if (candidate.Revision <= configuration.Revision)
            {
                throw new InvalidDataException("配置修订号必须递增。");
            }
            CommitConfiguration(candidate);
        }
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
        StaticMappingReconciliationStatus mappingStatus =
            staticMappingWorker?.GetStatus() ?? new StaticMappingReconciliationStatus(false, null);
        RuntimeStatusSnapshot status;
        lock (configurationGate)
        {
            status = new RuntimeStatusSnapshot
            {
                ConfigurationRevision = configuration.Revision,
                DriverAppliedRevision = driverStatus.AppliedRevision,
                DriverConnected = driverStatus.IsConnected,
                LastError = driverStatus.LastError,
                StaticMappingPending = mappingStatus.IsPending,
                StaticMappingLastError = mappingStatus.LastError,
                Configuration = CloneConfiguration(configuration),
                Osc = oscInput?.GetStatus() ?? new OscRuntimeStatus
                {
                    Enabled = configuration.Osc.Enabled,
                    Endpoint = configuration.Osc.ListenAddress + ":" + configuration.Osc.Port,
                    SendEndpoint = configuration.Osc.ListenAddress + ":" + configuration.Osc.SendPort
                },
                XInput = xInput?.GetStatus() ?? new XInputRuntimeStatus(),
                PhysicalSourceHiding = CreatePhysicalSourceHidingStatus(configuration, driverStatus)
            };
        }
        return new MessageEnvelope
        {
            MessageType = "status",
            RequestId = requestId,
            PayloadJson = JsonConvert.SerializeObject(status, RuntimeJson.Settings)
        };
    }

    private static PhysicalSourceHidingStatus CreatePhysicalSourceHidingStatus(
        RuntimeConfiguration configuration,
        DriverSynchronizationStatus driverStatus)
    {
        int requestedCount = configuration.PhysicalSourceHidingEnabled
            ? configuration.Routes
                .Where(route => route.Enabled && !route.PendingDeletion && route.HidePhysicalSource)
                .SelectMany(route => route.SplitPoseSource
                    ? new[] { route.SourceDevicePath, route.RotationSourceDevicePath }
                    : new[] { route.SourceDevicePath })
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .Count()
            : 0;
        if (requestedCount == 0)
        {
            return new PhysicalSourceHidingStatus();
        }
        if (!driverStatus.IsConnected)
        {
            return new PhysicalSourceHidingStatus
            {
                State = PhysicalSourceHidingState.Waiting,
                RequestedDeviceCount = requestedCount,
                LastError = driverStatus.LastError
            };
        }
        return driverStatus.PhysicalSourceHiding;
    }

    private MessageEnvelope CreateOscStatus(string requestId)
    {
        OscRuntimeStatus status = oscInput?.GetStatus() ?? new OscRuntimeStatus
        {
            Enabled = configuration.Osc.Enabled,
            Endpoint = configuration.Osc.ListenAddress + ":" + configuration.Osc.Port,
            SendEndpoint = configuration.Osc.ListenAddress + ":" + configuration.Osc.SendPort
        };
        return new MessageEnvelope
        {
            MessageType = "oscStatus",
            RequestId = requestId,
            PayloadJson = JsonConvert.SerializeObject(status, RuntimeJson.Settings)
        };
    }

    private MessageEnvelope TestOscHaptic(
        MessageEnvelope request,
        CancellationToken cancellationToken)
    {
        OscHapticTestRequest? payload = JsonConvert.DeserializeObject<OscHapticTestRequest>(
            request.PayloadJson,
            RuntimeJson.Settings);
        if (payload == null ||
            (payload.Hand != ControllerHand.Left && payload.Hand != ControllerHand.Right))
        {
            throw new InvalidDataException("测试震动的手别无效。");
        }
        if (oscInput == null)
        {
            throw new InvalidDataException("OSC 服务未启动。");
        }
        oscInput.SendTestHapticAsync(payload.Hand, cancellationToken).GetAwaiter().GetResult();
        return new MessageEnvelope
        {
            MessageType = "oscHapticTestSent",
            RequestId = request.RequestId,
            PayloadJson = "{}"
        };
    }

    private MessageEnvelope CreateXInputStatus(string requestId)
    {
        XInputRuntimeStatus status = xInput?.GetStatus() ?? new XInputRuntimeStatus();
        return new MessageEnvelope
        {
            MessageType = "xInputStatus",
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

    private void CommitConfiguration(RuntimeConfiguration candidate)
    {
        candidate.Osc.Enabled = OscConfiguration.IsRequiredForRoutes(candidate.Routes);
        IReadOnlyList<string> errors = ConfigurationValidator.Validate(candidate);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
        store.Save(candidate);
        configuration = candidate;
        synchronizer.Update(candidate);
        oscInput?.Update(candidate.Osc, candidate.Routes);
        xInput?.Update(candidate.XInput, candidate.Routes);
    }

    private static RuntimeConfiguration CloneConfiguration(RuntimeConfiguration value)
    {
        string json = JsonConvert.SerializeObject(value, RuntimeJson.Settings);
        return JsonConvert.DeserializeObject<RuntimeConfiguration>(json, RuntimeJson.Settings)
            ?? throw new InvalidDataException("无法复制 Runtime 配置。");
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
                throw new EndOfStreamException("Runtime 控制连接在完整消息到达前已关闭。");
            }
            if (buffer[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
            bytes.Add(buffer[0]);
        }
        throw new InvalidDataException("Runtime 控制消息超过允许的大小限制。");
    }
}
