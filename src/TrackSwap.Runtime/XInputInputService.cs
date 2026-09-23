using System.Runtime.InteropServices;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class XInputInputService : IDisposable
{
    private const int PollIntervalMilliseconds = 8;
    private const int IdleIntervalMilliseconds = 200;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const uint ErrorSuccess = 0;
    private const short LeftStickDeadzone = 7849;
    private const short RightStickDeadzone = 8689;

    private readonly object syncRoot = new();
    private bool leftInputEnabled;
    private bool rightInputEnabled;
    private ControllerInputState leftState = new();
    private ControllerInputState rightState = new();
    private bool controllerConnected;
    private DateTimeOffset leftLastSend;
    private DateTimeOffset rightLastSend;
    private bool disposed;

    public XInputInputService(IEnumerable<RouteConfiguration>? initialRoutes = null)
    {
        UpdateActiveInputsLocked(initialRoutes);
    }

    public void Update(IEnumerable<RouteConfiguration>? routes)
    {
        lock (syncRoot)
        {
            bool resetLeft = leftInputEnabled || HasXInputRoute(routes, ControllerHand.Left);
            bool resetRight = rightInputEnabled || HasXInputRoute(routes, ControllerHand.Right);
            UpdateActiveInputsLocked(routes);
            controllerConnected = false;
            if (resetLeft)
            {
                ResetLocked(ControllerHand.Left, send: true);
            }
            if (resetRight)
            {
                ResetLocked(ControllerHand.Right, send: true);
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool active;
            lock (syncRoot)
            {
                active = leftInputEnabled || rightInputEnabled;
            }
            if (!active)
            {
                await Task.Delay(IdleIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool connected = TryGetFirstConnectedController(out XInputGamepadSnapshot snapshot);
            lock (syncRoot)
            {
                if (connected)
                {
                    ApplySnapshotLocked(snapshot);
                }
                else if (controllerConnected)
                {
                    if (leftInputEnabled)
                    {
                        ResetLocked(ControllerHand.Left, send: true);
                    }
                    if (rightInputEnabled)
                    {
                        ResetLocked(ControllerHand.Right, send: true);
                    }
                }
                controllerConnected = connected;
            }
            await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplySnapshotLocked(XInputGamepadSnapshot snapshot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (leftInputEnabled)
        {
            ControllerInputState next = XInputControllerMapper.MapLeft(snapshot);
            if (!controllerConnected || !StatesEqual(leftState, next) || now - leftLastSend >= RefreshInterval)
            {
                leftState = next;
                Send(ControllerHand.Left, next);
                leftLastSend = now;
            }
        }
        if (rightInputEnabled)
        {
            ControllerInputState next = XInputControllerMapper.MapRight(snapshot);
            if (!controllerConnected || !StatesEqual(rightState, next) || now - rightLastSend >= RefreshInterval)
            {
                rightState = next;
                Send(ControllerHand.Right, next);
                rightLastSend = now;
            }
        }
    }

    private void UpdateActiveInputsLocked(IEnumerable<RouteConfiguration>? routes)
    {
        leftInputEnabled = HasXInputRoute(routes, ControllerHand.Left);
        rightInputEnabled = HasXInputRoute(routes, ControllerHand.Right);
    }

    private static bool HasXInputRoute(IEnumerable<RouteConfiguration>? routes, ControllerHand hand)
    {
        return routes?.Any(route =>
            route.Enabled &&
            route.Mode == RouteMode.VirtualController &&
            route.ControllerHand == hand &&
            route.ControlInputSource == ControlInputSource.XInput) == true;
    }

    private static bool TryGetFirstConnectedController(out XInputGamepadSnapshot snapshot)
    {
        for (uint index = 0; index < 4; index++)
        {
            if (XInputGetState(index, out XInputState state) == ErrorSuccess)
            {
                snapshot = new XInputGamepadSnapshot(
                    state.Gamepad.Buttons,
                    state.Gamepad.LeftTrigger,
                    state.Gamepad.RightTrigger,
                    state.Gamepad.ThumbLX,
                    state.Gamepad.ThumbLY,
                    state.Gamepad.ThumbRX,
                    state.Gamepad.ThumbRY);
                return true;
            }
        }
        snapshot = default;
        return false;
    }

    private void ResetLocked(ControllerHand hand, bool send)
    {
        var neutral = new ControllerInputState();
        if (hand == ControllerHand.Left)
        {
            leftState = neutral;
            leftLastSend = DateTimeOffset.UtcNow;
        }
        else
        {
            rightState = neutral;
            rightLastSend = DateTimeOffset.UtcNow;
        }
        if (send)
        {
            Send(hand, neutral);
        }
    }

    private static bool StatesEqual(ControllerInputState left, ControllerInputState right)
    {
        return left.JoystickX == right.JoystickX &&
            left.JoystickY == right.JoystickY &&
            left.TriggerValue == right.TriggerValue &&
            left.GripValue == right.GripValue &&
            left.JoystickClick == right.JoystickClick &&
            left.TriggerClick == right.TriggerClick &&
            left.GripClick == right.GripClick &&
            left.PrimaryButton == right.PrimaryButton &&
            left.SecondaryButton == right.SecondaryButton &&
            left.MenuButton == right.MenuButton;
    }

    private static void Send(ControllerHand hand, ControllerInputState state)
    {
        try
        {
            DriverControlClient.ApplyControllerInput(hand, state, TimeSpan.FromMilliseconds(250));
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is TimeoutException ||
            exception is UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (leftInputEnabled)
            {
                ResetLocked(ControllerHand.Left, send: true);
            }
            if (rightInputEnabled)
            {
                ResetLocked(ControllerHand.Right, send: true);
            }
        }
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    internal static short GetLeftStickDeadzone() => LeftStickDeadzone;
    internal static short GetRightStickDeadzone() => RightStickDeadzone;
}

internal readonly record struct XInputGamepadSnapshot(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short ThumbLX,
    short ThumbLY,
    short ThumbRX,
    short ThumbRY);

internal static class XInputControllerMapper
{
    internal const ushort DPadUp = 0x0001;
    internal const ushort DPadDown = 0x0002;
    internal const ushort DPadLeft = 0x0004;
    internal const ushort DPadRight = 0x0008;
    internal const ushort Start = 0x0010;
    internal const ushort View = 0x0020;
    internal const ushort LeftThumb = 0x0040;
    internal const ushort RightThumb = 0x0080;
    internal const ushort LeftShoulder = 0x0100;
    internal const ushort RightShoulder = 0x0200;
    internal const ushort A = 0x1000;
    internal const ushort B = 0x2000;
    internal const ushort X = 0x4000;
    internal const ushort Y = 0x8000;
    internal const byte TriggerThreshold = 30;

    public static ControllerInputState MapLeft(XInputGamepadSnapshot gamepad)
    {
        (float x, float y) = NormalizeStick(
            gamepad.ThumbLX,
            gamepad.ThumbLY,
            XInputInputService.GetLeftStickDeadzone());
        bool shoulder = HasButton(gamepad, LeftShoulder);
        return new ControllerInputState
        {
            JoystickX = x,
            JoystickY = y,
            JoystickClick = HasButton(gamepad, LeftThumb),
            TriggerValue = shoulder ? 1.0f : 0.0f,
            TriggerClick = shoulder,
            GripValue = gamepad.LeftTrigger / 255.0f,
            GripClick = gamepad.LeftTrigger > TriggerThreshold,
            PrimaryButton = HasButton(gamepad, X),
            SecondaryButton = HasButton(gamepad, Y),
            MenuButton = HasButton(gamepad, View)
        };
    }

    public static ControllerInputState MapRight(XInputGamepadSnapshot gamepad)
    {
        (float x, float y) = NormalizeStick(
            gamepad.ThumbRX,
            gamepad.ThumbRY,
            XInputInputService.GetRightStickDeadzone());
        bool shoulder = HasButton(gamepad, RightShoulder);
        return new ControllerInputState
        {
            JoystickX = x,
            JoystickY = y,
            JoystickClick = HasButton(gamepad, RightThumb),
            TriggerValue = shoulder ? 1.0f : 0.0f,
            TriggerClick = shoulder,
            GripValue = gamepad.RightTrigger / 255.0f,
            GripClick = gamepad.RightTrigger > TriggerThreshold,
            PrimaryButton = HasButton(gamepad, A),
            SecondaryButton = HasButton(gamepad, B),
            MenuButton = HasButton(gamepad, Start)
        };
    }

    private static bool HasButton(XInputGamepadSnapshot gamepad, ushort button)
    {
        return (gamepad.Buttons & button) != 0;
    }

    private static (float X, float Y) NormalizeStick(short rawX, short rawY, short deadzone)
    {
        double x = rawX;
        double y = rawY;
        double magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude <= deadzone)
        {
            return (0.0f, 0.0f);
        }
        double directionX = x / magnitude;
        double directionY = y / magnitude;
        double normalizedMagnitude = Math.Min(1.0, (magnitude - deadzone) / (32767.0 - deadzone));
        return ((float)(directionX * normalizedMagnitude), (float)(directionY * normalizedMagnitude));
    }
}
