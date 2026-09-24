using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Reflection;
using TrackSwap.Protocol;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class OscInputServiceTests
{
    [Fact]
    public async Task ReportsReceivePortConflictSeparately()
    {
        using var blocker = new UdpClient(AddressFamily.InterNetwork);
        blocker.Client.ExclusiveAddressUse = true;
        blocker.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)blocker.Client.LocalEndPoint!).Port;
        var routes = new[]
        {
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Left,
                ControlInputSource = ControlInputSource.Osc
            }
        };
        using var service = new OscInputService(
            new OscConfiguration
            {
                ListenAddress = "127.0.0.1",
                Port = port
            },
            routes,
            (_, _) => { });
        using var cancellation = new CancellationTokenSource();
        Task runTask = service.RunAsync(cancellation.Token);
        try
        {
            OscRuntimeStatus status = service.GetStatus();
            for (int attempt = 0; attempt < 40 && !status.ReceivePortInUse; attempt++)
            {
                await Task.Delay(25);
                status = service.GetStatus();
            }

            Assert.True(status.ReceivePortInUse);
            Assert.False(status.Listening);
            Assert.False(string.IsNullOrWhiteSpace(status.LastError));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [Fact]
    public void CoalescesEachPacketAndIgnoresRepeatedValues()
    {
        int sendCount = 0;
        ControllerHand? lastHand = null;
        var routes = new[]
        {
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Left,
                ControlInputSource = ControlInputSource.Osc
            }
        };
        using var service = new OscInputService(
            new OscConfiguration(),
            routes,
            (hand, _) =>
            {
                sendCount++;
                lastHand = hand;
            });
        var packet = new[]
        {
            new OscValue("/trackswap/left/button/x", 1.0),
            new OscValue("/trackswap/left/button/y", 1.0),
            new OscValue("/trackswap/left/trigger/value", 0.75)
        };

        ApplyValues(service, packet);

        Assert.Equal(1, sendCount);
        Assert.Equal(ControllerHand.Left, lastHand);
        DateTimeOffset? firstMessageAt = service.GetStatus().LastMessageAtUtc;
        Assert.NotNull(firstMessageAt);

        ApplyValues(service, packet);

        Assert.Equal(1, sendCount);
        Assert.True(service.GetStatus().LastMessageAtUtc >= firstMessageAt);

        ApplyValues(service, new OscValue("/trackswap/left/button/x", 0.0));

        Assert.Equal(2, sendCount);
    }

    [Fact]
    public void TouchAssistDefaultsAndActualInputPriorityMatchXInputBehavior()
    {
        var configuration = new OscConfiguration
        {
            LeftTouchAssist = new OscTouchAssistConfiguration
            {
                ThumbDefaultTouched = true,
                IndexDefaultTouched = true
            }
        };
        var routes = new[]
        {
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Left,
                ControlInputSource = ControlInputSource.Osc
            }
        };
        using var service = new OscInputService(configuration, routes);

        OscRuntimeStatus initial = service.GetStatus();
        Assert.True(initial.LeftInput.HasExplicitTouchState);
        Assert.True(initial.LeftInput.JoystickTouch);
        Assert.True(initial.LeftInput.TriggerTouch);

        ApplyValues(
            service,
            new OscValue("/trackswap/left/touch/thumb", 1.0),
            new OscValue("/trackswap/left/touch/index", 1.0));
        OscRuntimeStatus inverted = service.GetStatus();
        Assert.True(inverted.LeftThumbTouchAssist);
        Assert.True(inverted.LeftIndexTouchAssist);
        Assert.False(inverted.LeftInput.JoystickTouch);
        Assert.False(inverted.LeftInput.TriggerTouch);

        ApplyValues(
            service,
            new OscValue("/trackswap/left/button/x", 1.0),
            new OscValue("/trackswap/left/trigger/value", 0.6));
        OscRuntimeStatus actualInput = service.GetStatus();
        Assert.True(actualInput.LeftInput.PrimaryTouch);
        Assert.True(actualInput.LeftInput.TriggerTouch);
        Assert.False(actualInput.LeftInput.JoystickTouch);
    }

    [Fact]
    public void RaisedDefaultCanBeTemporarilyInvertedToContact()
    {
        var configuration = new OscConfiguration
        {
            RightTouchAssist = new OscTouchAssistConfiguration
            {
                ThumbDefaultTouched = false,
                IndexDefaultTouched = false
            }
        };
        var routes = new[]
        {
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Right,
                ControlInputSource = ControlInputSource.Osc
            }
        };
        using var service = new OscInputService(configuration, routes);

        Assert.False(service.GetStatus().RightInput.JoystickTouch);
        Assert.False(service.GetStatus().RightInput.TriggerTouch);

        ApplyValues(
            service,
            new OscValue("/trackswap/right/touch/thumb", 0.51),
            new OscValue("/trackswap/right/touch/index", 1.0));

        Assert.True(service.GetStatus().RightInput.JoystickTouch);
        Assert.True(service.GetStatus().RightInput.TriggerTouch);
    }

    [Fact]
    public async Task TestHapticCanRunWithoutAnOscRoute()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int sendPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var service = new OscInputService(
            new OscConfiguration
            {
                ListenAddress = "127.0.0.1",
                Port = 9015,
                SendPort = sendPort
            });

        await service.SendTestHapticAsync(ControllerHand.Right, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        UdpReceiveResult packet = await receiver.ReceiveAsync(timeout.Token);
        Assert.Equal("/trackswap/right/haptic", ReadAddress(packet.Buffer));
        OscRuntimeStatus status = service.GetStatus();
        OscHapticPreviewSample sample = Assert.Single(status.RightHapticHistory);
        Assert.Equal(0.08f, sample.DurationSeconds);
        Assert.Equal(6.0f, sample.Frequency);
        Assert.Equal(0.20f, sample.Amplitude);
    }

    [Fact]
    public async Task TestHapticSendsRecognizableFiveStepPattern()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int sendPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var service = new OscInputService(
            new OscConfiguration
            {
                ListenAddress = "127.0.0.1",
                Port = 9015,
                SendPort = sendPort
            });

        await service.SendTestHapticAsync(ControllerHand.Left, CancellationToken.None);

        var received = new List<(float Duration, float Frequency, float Amplitude)>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        for (int index = 0; index < 5; index++)
        {
            UdpReceiveResult packet = await receiver.ReceiveAsync(timeout.Token);
            Assert.Equal("/trackswap/left/haptic", ReadAddress(packet.Buffer));
            received.Add(ReadHapticValues(packet.Buffer));
        }

        Assert.Equal(
            new[]
            {
                (0.08f, 6.0f, 0.20f),
                (0.20f, 18.0f, 0.55f),
                (0.36f, 40.0f, 1.00f),
                (0.07f, 30.0f, 0.85f),
                (0.07f, 30.0f, 0.85f)
            },
            received);
    }

    [Fact]
    public async Task NewHapticTruncatesThePreviousPreviewSegment()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int sendPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var service = new OscInputService(
            new OscConfiguration
            {
                ListenAddress = "127.0.0.1",
                Port = 9015,
                SendPort = sendPort
            });

        await service.SendTestHapticAsync(ControllerHand.Left, CancellationToken.None);
        await service.SendTestHapticAsync(ControllerHand.Left, CancellationToken.None);

        IReadOnlyList<OscHapticPreviewSample> history = service.GetStatus().LeftHapticHistory;
        Assert.Equal(2, history.Count);
        Assert.InRange(history[0].DurationSeconds, 0.0f, 0.079999f);
        Assert.Equal(0.08f, history[1].DurationSeconds);
        await Task.Delay(600);
        IReadOnlyList<OscHapticPreviewSample> restartedHistory = service.GetStatus().LeftHapticHistory;
        Assert.Equal(3, restartedHistory.Count);
        Assert.Equal(new[] { 6.0f, 6.0f, 18.0f }, restartedHistory.Select(sample => sample.Frequency));
    }

    [Fact]
    public async Task SendsHapticsOnlyForOscOwnedHands()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int sendPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        var routes = new[]
        {
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Left,
                ControlInputSource = ControlInputSource.Osc
            },
            new RouteConfiguration
            {
                Enabled = true,
                Mode = RouteMode.VirtualController,
                ControllerHand = ControllerHand.Right,
                ControlInputSource = ControlInputSource.XInput
            }
        };
        using var service = new OscInputService(
            new OscConfiguration
            {
                ListenAddress = "127.0.0.1",
                Port = 9015,
                SendPort = sendPort
            },
            routes);

        await service.SendHapticEventsAsync(
            new[]
            {
                new HapticFeedbackEvent
                {
                    Hand = ControllerHand.Left,
                    DurationSeconds = 0.25f,
                    Frequency = 120.0f,
                    Amplitude = 0.75f
                },
                new HapticFeedbackEvent
                {
                    Hand = ControllerHand.Right,
                    DurationSeconds = 1.0f,
                    Frequency = 10.0f,
                    Amplitude = 1.0f
                }
            },
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        UdpReceiveResult packet = await receiver.ReceiveAsync(timeout.Token);
        Assert.Equal("/trackswap/left/haptic", ReadAddress(packet.Buffer));
        await Task.Delay(30);
        Assert.Equal(0, receiver.Available);
        Assert.Equal(0.75f, service.GetStatus().LeftHaptic.Amplitude);
        Assert.Null(service.GetStatus().RightHapticAtUtc);
    }

    private static string ReadAddress(byte[] packet)
    {
        int end = Array.IndexOf(packet, (byte)0);
        Assert.True(end > 0);
        return Encoding.UTF8.GetString(packet, 0, end);
    }

    private static void ApplyValues(OscInputService service, params OscValue[] values)
    {
        MethodInfo method = typeof(OscInputService).GetMethod(
            "HandlePacket",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(service, new object[] { values });
    }

    private static (float Duration, float Frequency, float Amplitude) ReadHapticValues(byte[] packet)
    {
        int position = PaddedStringLength(packet, 0);
        position += PaddedStringLength(packet, position);
        return (
            ReadSingle(packet, position),
            ReadSingle(packet, position + 4),
            ReadSingle(packet, position + 8));
    }

    private static int PaddedStringLength(byte[] packet, int offset)
    {
        int end = Array.IndexOf(packet, (byte)0, offset);
        Assert.True(end >= offset);
        int length = end - offset + 1;
        return (length + 3) & ~3;
    }

    private static float ReadSingle(byte[] packet, int offset)
    {
        return BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(offset, 4)));
    }
}
