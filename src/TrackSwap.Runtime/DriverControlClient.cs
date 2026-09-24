using System.IO.Pipes;
using System.Text;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class DriverControlClient
{
    private const double CentimetresPerMetre = 100.0;
    private static readonly object PipeSync = new();
    public static string SwitchSource(string sourceDevicePath, TimeSpan timeout)
    {
        byte[] payload = Encoding.UTF8.GetBytes(sourceDevicePath);
        if (payload.Length == 0 || payload.Length > DriverControlProtocol.MaximumPayloadBytes)
        {
            throw new IOException("编码后的来源路径超过驱动通信长度限制。");
        }

        return SendAccepted(DriverControlProtocol.SetSourceMessageType, payload, timeout);
    }

    public static string SetOffset(PoseOffset offset, TimeSpan timeout)
    {
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            WriteOffsetForOpenVr(writer, offset);
        }
        return SendAccepted(DriverControlProtocol.SetOffsetMessageType, payloadStream.ToArray(), timeout);
    }

    public static string ApplySnapshot(
        int slot,
        RouteConfiguration? route,
        bool hidePhysicalSource,
        ulong revision,
        TimeSpan timeout)
    {
        bool enabled = route?.Enabled == true;
        byte[] sourcePath = enabled ? Encoding.UTF8.GetBytes(route!.SourceDevicePath) : Array.Empty<byte>();
        byte[] rotationSourcePath = enabled && route!.SplitPoseSource
            ? Encoding.UTF8.GetBytes(route.RotationSourceDevicePath)
            : Array.Empty<byte>();
        byte[] targetPath = enabled && route!.Mode == RouteMode.ReplaceTarget
            ? Encoding.UTF8.GetBytes(route.TargetDevicePath)
            : Array.Empty<byte>();
        if (slot < 0 || slot >= ProtocolConstants.MaximumRoutes ||
            (enabled && sourcePath.Length == 0) ||
            sourcePath.Length > ushort.MaxValue || rotationSourcePath.Length > ushort.MaxValue ||
            targetPath.Length > ushort.MaxValue ||
            sourcePath.Length + rotationSourcePath.Length + targetPath.Length >
                DriverControlProtocol.MaximumCombinedDevicePathBytes)
        {
            throw new IOException("编码后的来源或目标路径无效。");
        }

        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)slot);
            writer.Write(enabled ? (byte)1 : (byte)0);
            writer.Write(enabled && hidePhysicalSource ? (byte)1 : (byte)0);
            writer.Write(revision);
            writer.Write((ushort)sourcePath.Length);
            writer.Write((ushort)rotationSourcePath.Length);
            writer.Write((ushort)targetPath.Length);
            writer.Write(sourcePath);
            writer.Write(rotationSourcePath);
            writer.Write(targetPath);
            PoseOffset offset = route?.Offset ?? PoseOffset.Identity();
            WriteOffsetForOpenVr(writer, offset);
        }
        return SendAccepted(DriverControlProtocol.ApplySnapshotMessageType, payloadStream.ToArray(), timeout);
    }

    public static string ApplyControllerSnapshot(
        ControllerHand hand,
        RouteConfiguration? route,
        bool hidePhysicalSource,
        int handSelectionPriority,
        ulong revision,
        TimeSpan timeout)
    {
        bool enabled = route?.Enabled == true;
        byte[] sourcePath = enabled ? Encoding.UTF8.GetBytes(route!.SourceDevicePath) : Array.Empty<byte>();
        byte[] rotationSourcePath = enabled && route!.SplitPoseSource
            ? Encoding.UTF8.GetBytes(route.RotationSourceDevicePath)
            : Array.Empty<byte>();
        if ((hand != ControllerHand.Left && hand != ControllerHand.Right) ||
            (enabled && (route!.Mode != RouteMode.VirtualController || route.ControllerHand != hand || sourcePath.Length == 0)) ||
            sourcePath.Length > ushort.MaxValue || rotationSourcePath.Length > ushort.MaxValue ||
            sourcePath.Length + rotationSourcePath.Length > DriverControlProtocol.MaximumPayloadBytes -
                DriverControlProtocol.ApplyControllerSnapshotFixedBytes)
        {
            throw new IOException("虚拟控制器快照无效。");
        }

        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)hand);
            writer.Write(enabled ? (byte)1 : (byte)0);
            writer.Write(enabled ? (byte)route!.VirtualDeviceSlot : byte.MaxValue);
            writer.Write(enabled && hidePhysicalSource ? (byte)1 : (byte)0);
            writer.Write(revision);
            writer.Write(handSelectionPriority);
            writer.Write((ushort)sourcePath.Length);
            writer.Write((ushort)rotationSourcePath.Length);
            writer.Write(sourcePath);
            writer.Write(rotationSourcePath);
            PoseOffset offset = route?.Offset ?? PoseOffset.Identity();
            WriteOffsetForOpenVr(writer, offset);
        }
        return SendAccepted(DriverControlProtocol.ApplyControllerSnapshotMessageType, payloadStream.ToArray(), timeout);
    }

    public static string ApplyControllerInput(
        ControllerHand hand,
        ControllerInputState state,
        TimeSpan timeout)
    {
        if ((hand != ControllerHand.Left && hand != ControllerHand.Right) || state == null)
        {
            throw new IOException("虚拟控制器输入状态无效。");
        }
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)hand);
            writer.Write(state.JoystickX);
            writer.Write(state.JoystickY);
            writer.Write(state.TriggerValue);
            writer.Write(state.GripValue);
            writer.Write(state.JoystickClick ? (byte)1 : (byte)0);
            writer.Write(state.PrimaryButton ? (byte)1 : (byte)0);
            writer.Write(state.SecondaryButton ? (byte)1 : (byte)0);
            writer.Write(state.MenuButton ? (byte)1 : (byte)0);
            writer.Write(state.HasExplicitTouchState ? (byte)1 : (byte)0);
            writer.Write(state.JoystickTouch ? (byte)1 : (byte)0);
            writer.Write(state.TriggerTouch ? (byte)1 : (byte)0);
            writer.Write(state.GripTouch ? (byte)1 : (byte)0);
            writer.Write(state.PrimaryTouch ? (byte)1 : (byte)0);
            writer.Write(state.SecondaryTouch ? (byte)1 : (byte)0);
            writer.Write(state.MenuTouch ? (byte)1 : (byte)0);
            writer.Write(state.ThumbRestTouch ? (byte)1 : (byte)0);
        }
        return SendAccepted(DriverControlProtocol.ApplyControllerInputMessageType, payloadStream.ToArray(), timeout);
    }

    public static IReadOnlyList<PoseTelemetrySnapshot> GetTelemetry(TimeSpan timeout)
    {
        byte[] payload = SendBytes(
            DriverControlProtocol.GetTelemetryMessageType,
            new byte[] { 0 },
            timeout);
        return ParseTelemetryBatch(payload);
    }

    public static IReadOnlyList<HapticFeedbackEvent> GetHapticEvents(TimeSpan timeout)
    {
        byte[] payload = SendBytes(
            DriverControlProtocol.GetHapticEventsMessageType,
            new byte[] { 0 },
            timeout);
        return ParseHapticFeedbackBatch(payload);
    }

    public static PhysicalSourceHidingStatus GetPhysicalSourceHidingStatus(TimeSpan timeout)
    {
        byte[] payload = SendBytes(
            DriverControlProtocol.GetPhysicalSourceHidingStatusMessageType,
            new byte[] { 0 },
            timeout);
        if (payload == null || payload.Length != DriverControlProtocol.PhysicalSourceHidingStatusBytes)
        {
            throw new IOException("驱动返回了无效的设备隐藏状态。");
        }
        string lastError = Encoding.UTF8.GetString(payload, 4, 256).TrimEnd('\0');
        var state = (PhysicalSourceHidingState)payload[0];
        if (!Enum.IsDefined(typeof(PhysicalSourceHidingState), state) || payload[2] > payload[1])
        {
            throw new IOException("驱动返回了无效的设备隐藏状态内容。");
        }
        return new PhysicalSourceHidingStatus
        {
            State = state,
            RequestedDeviceCount = payload[1],
            ActiveDeviceCount = payload[2],
            HookInstalled = payload[3] != 0,
            LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError
        };
    }

    internal static IReadOnlyList<HapticFeedbackEvent> ParseHapticFeedbackBatch(byte[] payload)
    {
        if (payload == null || payload.Length != DriverControlProtocol.HapticFeedbackBatchBytes)
        {
            throw new IOException("驱动返回了无效的震动事件批次大小。");
        }
        int count = payload[0];
        if (count < 0 || count > DriverControlProtocol.MaximumHapticEvents)
        {
            throw new IOException("驱动返回了无效的震动事件数量。");
        }
        var events = new List<HapticFeedbackEvent>(count);
        using var stream = new MemoryStream(payload, 1, payload.Length - 1, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        for (int index = 0; index < DriverControlProtocol.MaximumHapticEvents; index++)
        {
            ulong sequence = reader.ReadUInt64();
            ControllerHand hand = (ControllerHand)reader.ReadByte();
            float durationSeconds = reader.ReadSingle();
            float frequency = reader.ReadSingle();
            float amplitude = reader.ReadSingle();
            if (index >= count)
            {
                continue;
            }
            if (hand != ControllerHand.Left && hand != ControllerHand.Right)
            {
                throw new IOException("驱动返回了无效的震动事件手别。");
            }
            events.Add(new HapticFeedbackEvent
            {
                Sequence = sequence,
                Hand = hand,
                DurationSeconds = durationSeconds,
                Frequency = frequency,
                Amplitude = amplitude
            });
        }
        return events;
    }

    internal static IReadOnlyList<PoseTelemetrySnapshot> ParseTelemetryBatch(byte[] payload)
    {
        if (payload == null || payload.Length != DriverControlProtocol.TelemetryBatchBytes)
        {
            throw new IOException("驱动返回了无效的遥测数据批次大小。");
        }
        int count = payload[0];
        if (count < 0 || count > ProtocolConstants.MaximumRoutes)
        {
            throw new IOException("驱动返回了无效的遥测路由数量。");
        }
        var snapshots = new List<PoseTelemetrySnapshot>(count);
        for (int slot = 0; slot < count; slot++)
        {
            byte[] snapshotBytes = new byte[DriverControlProtocol.TelemetrySnapshotBytes];
            Buffer.BlockCopy(
                payload,
                1 + (slot * DriverControlProtocol.TelemetrySnapshotBytes),
                snapshotBytes,
                0,
                snapshotBytes.Length);
            PoseTelemetrySnapshot snapshot = ParseTelemetry(snapshotBytes);
            snapshot.VirtualDeviceSlot = slot;
            snapshots.Add(snapshot);
        }
        return snapshots;
    }

    internal static PoseTelemetrySnapshot ParseTelemetry(byte[] payload)
    {
        if (payload == null || payload.Length != DriverControlProtocol.TelemetrySnapshotBytes)
        {
            throw new IOException("驱动返回了无效的遥测快照大小。");
        }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        return new PoseTelemetrySnapshot
        {
            Sequence = reader.ReadUInt64(),
            AppliedRevision = checked((long)reader.ReadUInt64()),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Source = ReadPose(reader),
            RotationSource = ReadPose(reader),
            Output = ReadPose(reader),
            Target = ReadPose(reader)
        };
    }

    private static PoseTelemetry ReadPose(BinaryReader reader)
    {
        return new PoseTelemetry
        {
            Connected = reader.ReadByte() != 0,
            Valid = reader.ReadByte() != 0,
            TrackingResult = reader.ReadInt32(),
            PositionX = reader.ReadDouble(),
            PositionY = reader.ReadDouble(),
            PositionZ = reader.ReadDouble(),
            RotationX = reader.ReadDouble(),
            RotationY = reader.ReadDouble(),
            RotationZ = reader.ReadDouble(),
            RotationW = reader.ReadDouble()
        };
    }

    private static void WriteOffsetForOpenVr(BinaryWriter writer, PoseOffset offset)
    {
        writer.Write(ToOpenVrMetres(offset.TranslationX));
        writer.Write(ToOpenVrMetres(offset.TranslationY));
        writer.Write(ToOpenVrMetres(offset.TranslationZ));
        writer.Write(offset.RotationX);
        writer.Write(offset.RotationY);
        writer.Write(offset.RotationZ);
        writer.Write(offset.RotationW);
    }

    internal static double ToOpenVrMetres(double centimetres)
    {
        return centimetres / CentimetresPerMetre;
    }

    private static string SendAccepted(ushort requestType, byte[] payload, TimeSpan timeout)
    {
        string responseText = Encoding.UTF8.GetString(SendBytes(requestType, payload, timeout));
        if (!string.Equals(responseText, "accepted", StringComparison.Ordinal))
        {
            throw new IOException($"The driver rejected control message type {requestType}.");
        }
        return responseText;
    }

    private static byte[] SendBytes(ushort requestType, byte[] payload, TimeSpan timeout)
    {
        lock (PipeSync)
        {
            return SendBytesCore(requestType, payload, timeout);
        }
    }

    private static byte[] SendBytesCore(ushort requestType, byte[] payload, TimeSpan timeout)
    {
        if (payload.Length == 0 || payload.Length > DriverControlProtocol.MaximumPayloadBytes)
        {
            throw new IOException("驱动控制载荷大小无效。");
        }

        return SendBytesCoreAsync(requestType, payload, timeout).GetAwaiter().GetResult();
    }

    private static async Task<byte[]> SendBytesCoreAsync(ushort requestType, byte[] payload, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            ProtocolConstants.DriverPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("连接 TrackSwap 驱动超时。", exception);
        }
        pipe.ReadMode = PipeTransmissionMode.Byte;

        ulong requestId = unchecked((ulong)DateTime.UtcNow.Ticks);
        byte[] request;
        using (var requestStream = new MemoryStream())
        using (var writer = new BinaryWriter(requestStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(DriverControlProtocol.Magic);
            writer.Write(DriverControlProtocol.Version);
            writer.Write(requestType);
            writer.Write((uint)payload.Length);
            writer.Write(requestId);
            writer.Write(payload);
            writer.Flush();
            request = requestStream.ToArray();
        }

        byte[] header = new byte[DriverControlProtocol.HeaderBytes];
        try
        {
            await pipe.WriteAsync(request, cancellation.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cancellation.Token).ConfigureAwait(false);
            await ReadExactlyAsync(pipe, header, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("等待 TrackSwap 驱动响应超时。", exception);
        }

        using var headerStream = new MemoryStream(header, writable: false);
        using var reader = new BinaryReader(headerStream, Encoding.UTF8, leaveOpen: false);
        uint magic = reader.ReadUInt32();
        ushort version = reader.ReadUInt16();
        ushort responseType = reader.ReadUInt16();
        uint payloadBytes = reader.ReadUInt32();
        ulong responseRequestId = reader.ReadUInt64();
        if (magic != DriverControlProtocol.Magic ||
            version != DriverControlProtocol.Version ||
            responseType != (ushort)(requestType | DriverControlProtocol.ResponseFlag) ||
            responseRequestId != requestId ||
            payloadBytes > DriverControlProtocol.MaximumPayloadBytes)
        {
            throw new IOException("驱动返回了无效的控制响应。");
        }

        byte[] response = new byte[payloadBytes];
        try
        {
            await ReadExactlyAsync(pipe, response, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("读取 TrackSwap 驱动响应超时。", exception);
        }

        try
        {
            await pipe.WriteAsync(new byte[] { 1 }, cancellation.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
        {
            // Older drivers may close immediately after the response; acknowledgement is best effort.
        }

        return response;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("驱动在完整响应到达前关闭了控制管道。");
            }
            total += read;
        }
    }
}
