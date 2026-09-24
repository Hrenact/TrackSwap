using System.Net;
using System.Net.Sockets;

namespace TrackSwap.HapticPhoneDiagnostic;

internal sealed class OscHapticListenerService : BackgroundService
{
    private readonly DiagnosticOptions options;
    private readonly HapticWebSocketHub hub;
    private long nextEventId;

    public OscHapticListenerService(DiagnosticOptions options, HapticWebSocketHub hub)
    {
        this.options = options;
        this.hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Any, options.UdpPort));
        Console.WriteLine($"OSC UDP：正在监听 0.0.0.0:{options.UdpPort}");
        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received = await receiver.ReceiveAsync(stoppingToken).ConfigureAwait(false);
            if (!OscHapticPacketParser.TryParse(received.Buffer, out OscHapticPacket packet))
            {
                Console.WriteLine($"忽略来自 {received.RemoteEndPoint} 的非 TrackSwap 触觉 OSC 包（{received.Buffer.Length} 字节）。");
                continue;
            }

            long eventId = Interlocked.Increment(ref nextEventId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Console.WriteLine(
                $"#{eventId} {packet.Hand,-5}  时长 {packet.DurationSeconds:0.###} s  " +
                $"频率 {packet.FrequencyHertz:0.###} Hz  强度 {packet.Amplitude:P0}");

            await hub.BroadcastHapticAsync(new
            {
                type = "haptic",
                eventId,
                receivedAtUtc = now,
                remoteEndpoint = received.RemoteEndPoint.ToString(),
                packet.Address,
                packet.Hand,
                packet.DurationSeconds,
                packet.FrequencyHertz,
                packet.Amplitude,
                byteLength = received.Buffer.Length
            }, stoppingToken).ConfigureAwait(false);
        }
    }
}
