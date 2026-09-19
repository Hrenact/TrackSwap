using System.IO.Pipes;
using System.Text;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class DriverControlClient
{
    private static readonly object PipeSync = new();
    public static string SwitchSource(string sourceDevicePath, TimeSpan timeout)
    {
        byte[] payload = Encoding.UTF8.GetBytes(sourceDevicePath);
        if (payload.Length == 0 || payload.Length > DriverControlProtocol.MaximumPayloadBytes)
        {
            throw new IOException("The encoded source path exceeds the driver control limit.");
        }

        return SendAccepted(DriverControlProtocol.SetSourceMessageType, payload, timeout);
    }

    public static string SetOffset(PoseOffset offset, TimeSpan timeout)
    {
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(offset.TranslationX);
            writer.Write(offset.TranslationY);
            writer.Write(offset.TranslationZ);
            writer.Write(offset.RotationX);
            writer.Write(offset.RotationY);
            writer.Write(offset.RotationZ);
            writer.Write(offset.RotationW);
        }
        return SendAccepted(DriverControlProtocol.SetOffsetMessageType, payloadStream.ToArray(), timeout);
    }

    public static string ApplySnapshot(int slot, RouteConfiguration? route, ulong revision, TimeSpan timeout)
    {
        bool enabled = route?.Enabled == true;
        byte[] sourcePath = enabled ? Encoding.UTF8.GetBytes(route!.SourceDevicePath) : Array.Empty<byte>();
        byte[] targetPath = enabled ? Encoding.UTF8.GetBytes(route!.TargetDevicePath) : Array.Empty<byte>();
        if (slot < 0 || slot >= ProtocolConstants.MaximumRoutes ||
            (enabled && (sourcePath.Length == 0 || targetPath.Length == 0)) ||
            sourcePath.Length > ushort.MaxValue || targetPath.Length > ushort.MaxValue ||
            sourcePath.Length + targetPath.Length > DriverControlProtocol.MaximumCombinedDevicePathBytes)
        {
            throw new IOException("The encoded source or target path is invalid.");
        }

        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)slot);
            writer.Write(enabled ? (byte)1 : (byte)0);
            writer.Write(revision);
            writer.Write((ushort)sourcePath.Length);
            writer.Write((ushort)targetPath.Length);
            writer.Write(sourcePath);
            writer.Write(targetPath);
            PoseOffset offset = route?.Offset ?? PoseOffset.Identity();
            writer.Write(offset.TranslationX);
            writer.Write(offset.TranslationY);
            writer.Write(offset.TranslationZ);
            writer.Write(offset.RotationX);
            writer.Write(offset.RotationY);
            writer.Write(offset.RotationZ);
            writer.Write(offset.RotationW);
        }
        return SendAccepted(DriverControlProtocol.ApplySnapshotMessageType, payloadStream.ToArray(), timeout);
    }

    public static IReadOnlyList<PoseTelemetrySnapshot> GetTelemetry(TimeSpan timeout)
    {
        byte[] payload = SendBytes(
            DriverControlProtocol.GetTelemetryMessageType,
            new byte[] { 0 },
            timeout);
        return ParseTelemetryBatch(payload);
    }

    internal static IReadOnlyList<PoseTelemetrySnapshot> ParseTelemetryBatch(byte[] payload)
    {
        if (payload == null || payload.Length != DriverControlProtocol.TelemetryBatchBytes)
        {
            throw new IOException("The driver returned an invalid telemetry batch size.");
        }
        int count = payload[0];
        if (count < 0 || count > ProtocolConstants.MaximumRoutes)
        {
            throw new IOException("The driver returned an invalid telemetry route count.");
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
            throw new IOException("The driver returned an invalid telemetry snapshot size.");
        }
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        return new PoseTelemetrySnapshot
        {
            Sequence = reader.ReadUInt64(),
            AppliedRevision = checked((long)reader.ReadUInt64()),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Source = ReadPose(reader),
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
            throw new IOException("The driver control payload size is invalid.");
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
            throw new TimeoutException("Timed out connecting to the TrackSwap driver.", exception);
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
            throw new TimeoutException("Timed out waiting for the TrackSwap driver.", exception);
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
            throw new IOException("The driver returned an invalid control response.");
        }

        byte[] response = new byte[payloadBytes];
        try
        {
            await ReadExactlyAsync(pipe, response, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("Timed out reading the TrackSwap driver response.", exception);
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
                throw new IOException("The driver closed the control pipe before completing its response.");
            }
            total += read;
        }
    }
}
