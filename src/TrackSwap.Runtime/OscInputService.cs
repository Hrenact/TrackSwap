using System.Net;
using System.Net.Sockets;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class OscInputService : IDisposable
{
    private readonly object syncRoot = new();
    private OscConfiguration configuration;
    private UdpClient? listener;
    private ControllerInputState leftState = new();
    private ControllerInputState rightState = new();
    private DateTimeOffset? leftLastSignal;
    private DateTimeOffset? rightLastSignal;
    private DateTimeOffset? lastMessage;
    private bool listening;
    private string lastError = string.Empty;
    private int configurationVersion;
    private bool leftInputEnabled;
    private bool rightInputEnabled;
    private static readonly OscControllerAddresses LeftAddresses =
        OscControllerAddresses.ForHand(ControllerHand.Left);
    private static readonly OscControllerAddresses RightAddresses =
        OscControllerAddresses.ForHand(ControllerHand.Right);

    public OscInputService(
        OscConfiguration initialConfiguration,
        IEnumerable<RouteConfiguration>? initialRoutes = null)
    {
        configuration = Clone(initialConfiguration);
        configuration.Enabled = OscConfiguration.IsRequiredForRoutes(initialRoutes);
        UpdateActiveInputsLocked(initialRoutes);
    }

    public void Update(OscConfiguration value, IEnumerable<RouteConfiguration>? routes = null)
    {
        lock (syncRoot)
        {
            bool resetLeft = leftInputEnabled || HasOscInputRoute(routes, ControllerHand.Left);
            bool resetRight = rightInputEnabled || HasOscInputRoute(routes, ControllerHand.Right);
            configuration = Clone(value);
            configuration.Enabled = OscConfiguration.IsRequiredForRoutes(routes);
            UpdateActiveInputsLocked(routes);
            configurationVersion++;
            listener?.Dispose();
            listener = null;
            listening = false;
            if (resetLeft) ResetLocked(ControllerHand.Left, send: true);
            if (resetRight) ResetLocked(ControllerHand.Right, send: true);
        }
    }

    public OscRuntimeStatus GetStatus()
    {
        lock (syncRoot)
        {
            return new OscRuntimeStatus
            {
                Enabled = configuration.Enabled,
                Listening = listening,
                Endpoint = configuration.ListenAddress + ":" + configuration.Port,
                LastMessageAtUtc = lastMessage,
                LastError = lastError,
                LeftInput = Clone(leftState),
                RightInput = Clone(rightState)
            };
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            OscConfiguration snapshot;
            int version;
            lock (syncRoot)
            {
                snapshot = Clone(configuration);
                version = configurationVersion;
            }
            if (!snapshot.Enabled)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                UdpClient activeListener = EnsureListener(snapshot, version);
                using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCancellation.CancelAfter(100);
                try
                {
                    UdpReceiveResult packet = await activeListener.ReceiveAsync(receiveCancellation.Token).ConfigureAwait(false);
                    HandlePacket(OscPacketParser.Parse(packet.Buffer));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                CheckTimeouts(snapshot);
            }
            catch (ObjectDisposedException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException exception)
            {
                lock (syncRoot)
                {
                    listening = false;
                    lastError = exception.Message;
                    listener?.Dispose();
                    listener = null;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private UdpClient EnsureListener(OscConfiguration snapshot, int version)
    {
        lock (syncRoot)
        {
            if (version != configurationVersion)
            {
                throw new ObjectDisposedException(nameof(OscInputService));
            }
            if (listener != null)
            {
                return listener;
            }
            if (!IPAddress.TryParse(snapshot.ListenAddress, out IPAddress? address))
            {
                throw new SocketException((int)SocketError.AddressNotAvailable);
            }
            listener = new UdpClient(new IPEndPoint(address, snapshot.Port));
            listening = true;
            lastError = string.Empty;
            return listener;
        }
    }

    private void HandlePacket(IReadOnlyList<OscValue> values)
    {
        lock (syncRoot)
        {
            foreach (OscValue value in values)
            {
                bool leftChanged = leftInputEnabled && Apply(LeftAddresses, leftState, value);
                bool rightChanged = rightInputEnabled && Apply(RightAddresses, rightState, value);
                if (leftChanged)
                {
                    leftLastSignal = DateTimeOffset.UtcNow;
                    Send(ControllerHand.Left, leftState);
                }
                if (rightChanged)
                {
                    rightLastSignal = DateTimeOffset.UtcNow;
                    Send(ControllerHand.Right, rightState);
                }
                if (leftChanged || rightChanged)
                {
                    lastMessage = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    private void UpdateActiveInputsLocked(IEnumerable<RouteConfiguration>? routes)
    {
        leftInputEnabled = HasOscInputRoute(routes, ControllerHand.Left);
        rightInputEnabled = HasOscInputRoute(routes, ControllerHand.Right);
    }

    private static bool HasOscInputRoute(
        IEnumerable<RouteConfiguration>? routes,
        ControllerHand hand)
    {
        return routes?.Any(route =>
            route.Enabled &&
            route.Mode == RouteMode.VirtualController &&
            route.ControllerHand == hand &&
            route.ControlInputSource == ControlInputSource.Osc) == true;
    }

    private void CheckTimeouts(OscConfiguration snapshot)
    {
        if (snapshot.ResetTimeout == OscResetTimeout.Never)
        {
            return;
        }
        TimeSpan timeout = TimeSpan.FromSeconds((int)snapshot.ResetTimeout);
        lock (syncRoot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (leftLastSignal.HasValue && now - leftLastSignal.Value >= timeout)
            {
                ResetLocked(ControllerHand.Left, send: true);
            }
            if (rightLastSignal.HasValue && now - rightLastSignal.Value >= timeout)
            {
                ResetLocked(ControllerHand.Right, send: true);
            }
        }
    }

    private void ResetLocked(ControllerHand hand, bool send)
    {
        if (hand == ControllerHand.Left)
        {
            leftState = new ControllerInputState();
            leftLastSignal = null;
            if (send) Send(hand, leftState);
        }
        else
        {
            rightState = new ControllerInputState();
            rightLastSignal = null;
            if (send) Send(hand, rightState);
        }
    }

    private void Send(ControllerHand hand, ControllerInputState state)
    {
        try
        {
            DriverControlClient.ApplyControllerInput(hand, state, TimeSpan.FromMilliseconds(250));
        }
        catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            lastError = exception.Message;
        }
    }

    private static bool Apply(OscControllerAddresses addresses, ControllerInputState state, OscValue value)
    {
        float signed = (float)Math.Clamp(value.Value, -1.0, 1.0);
        float unsigned = (float)Math.Clamp(value.Value, 0.0, 1.0);
        bool pressed = value.Value > 0.5;
        if (value.Address == addresses.JoystickX) state.JoystickX = signed;
        else if (value.Address == addresses.JoystickY) state.JoystickY = signed;
        else if (value.Address == addresses.TriggerValue) state.TriggerValue = unsigned;
        else if (value.Address == addresses.GripValue) state.GripValue = unsigned;
        else if (value.Address == addresses.JoystickClick) state.JoystickClick = pressed;
        else if (value.Address == addresses.PrimaryButton) state.PrimaryButton = pressed;
        else if (value.Address == addresses.SecondaryButton) state.SecondaryButton = pressed;
        else if (value.Address == addresses.MenuButton) state.MenuButton = pressed;
        else return false;
        return true;
    }

    private static OscConfiguration Clone(OscConfiguration value)
    {
        return new OscConfiguration
        {
            Enabled = value?.Enabled == true,
            ListenAddress = value?.ListenAddress ?? "127.0.0.1",
            Port = value?.Port ?? 9015,
            ResetTimeout = value?.ResetTimeout ?? OscResetTimeout.FiveSeconds
        };
    }

    private static ControllerInputState Clone(ControllerInputState value)
    {
        return new ControllerInputState
        {
            JoystickX = value.JoystickX,
            JoystickY = value.JoystickY,
            TriggerValue = value.TriggerValue,
            GripValue = value.GripValue,
            JoystickClick = value.JoystickClick,
            PrimaryButton = value.PrimaryButton,
            SecondaryButton = value.SecondaryButton,
            MenuButton = value.MenuButton,
            HasExplicitTouchState = value.HasExplicitTouchState,
            JoystickTouch = value.JoystickTouch,
            TriggerTouch = value.TriggerTouch,
            GripTouch = value.GripTouch,
            PrimaryTouch = value.PrimaryTouch,
            SecondaryTouch = value.SecondaryTouch,
            MenuTouch = value.MenuTouch,
            ThumbRestTouch = value.ThumbRestTouch
        };
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (leftInputEnabled) ResetLocked(ControllerHand.Left, send: true);
            if (rightInputEnabled) ResetLocked(ControllerHand.Right, send: true);
            listener?.Dispose();
            listener = null;
        }
    }
}
