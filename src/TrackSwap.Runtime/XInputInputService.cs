using System.Diagnostics;
using System.Runtime.InteropServices;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class XInputInputService : IDisposable
{
    private const int PollIntervalMilliseconds = 8;
    private const int IdleIntervalMilliseconds = 200;
    private const int HapticPollIntervalMilliseconds = 16;
    private const float SinglePulseDurationSeconds = 0.02f;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private const uint ErrorSuccess = 0;
    private const short LeftStickDeadzone = 7849;
    private const short RightStickDeadzone = 8689;

    private readonly object syncRoot = new();
    private readonly SemaphoreSlim sendSignal = new(0, 1);
    private bool leftInputEnabled;
    private bool rightInputEnabled;
    private ControllerInputState leftState = new();
    private ControllerInputState rightState = new();
    private ControllerInputState? pendingLeftState;
    private ControllerInputState? pendingRightState;
    private XInputConfiguration configuration;
    private bool controllerConnected;
    private HapticEnvelope leftHaptic;
    private HapticEnvelope rightHaptic;
    private uint? vibrationControllerIndex;
    private ushort lastLeftMotorSpeed;
    private ushort lastRightMotorSpeed;
    private DateTimeOffset leftLastSend;
    private DateTimeOffset rightLastSend;
    private bool disposed;

    public XInputInputService(
        IEnumerable<RouteConfiguration>? initialRoutes = null,
        XInputConfiguration? initialConfiguration = null)
    {
        configuration = CloneConfiguration(initialConfiguration);
        UpdateActiveInputsLocked(initialRoutes);
    }

    public void Update(XInputConfiguration? nextConfiguration, IEnumerable<RouteConfiguration>? routes)
    {
        lock (syncRoot)
        {
            bool resetLeft = leftInputEnabled || HasXInputRoute(routes, ControllerHand.Left);
            bool resetRight = rightInputEnabled || HasXInputRoute(routes, ControllerHand.Right);
            configuration = CloneConfiguration(nextConfiguration);
            UpdateActiveInputsLocked(routes);
            controllerConnected = false;
            if (resetLeft)
            {
                ResetLocked(ControllerHand.Left, send: true);
                leftHaptic = default;
            }
            if (resetRight)
            {
                ResetLocked(ControllerHand.Right, send: true);
                rightHaptic = default;
            }
        }
    }

    public XInputRuntimeStatus GetStatus()
    {
        bool physicalConnected = TryGetFirstConnectedController(out XInputGamepadSnapshot physicalSnapshot);
        XInputPhysicalState physicalInput = CreatePhysicalState(physicalConnected, physicalSnapshot);
        lock (syncRoot)
        {
            bool enabled = leftInputEnabled || rightInputEnabled;
            return new XInputRuntimeStatus
            {
                Enabled = enabled,
                Connected = enabled && controllerConnected,
                LeftInput = CloneState(leftState),
                RightInput = CloneState(rightState),
                PhysicalInput = physicalInput
            };
        }
    }

    private static XInputPhysicalState CreatePhysicalState(
        bool connected,
        XInputGamepadSnapshot snapshot)
    {
        if (!connected)
        {
            return new XInputPhysicalState();
        }

        (float leftX, float leftY) = XInputControllerMapper.NormalizeStick(
            snapshot.ThumbLX,
            snapshot.ThumbLY,
            LeftStickDeadzone);
        (float rightX, float rightY) = XInputControllerMapper.NormalizeStick(
            snapshot.ThumbRX,
            snapshot.ThumbRY,
            RightStickDeadzone);
        return new XInputPhysicalState
        {
            Connected = true,
            Buttons = snapshot.Buttons,
            LeftTrigger = snapshot.LeftTrigger / 255.0f,
            RightTrigger = snapshot.RightTrigger / 255.0f,
            LeftStickX = leftX,
            LeftStickY = leftY,
            RightStickX = rightX,
            RightStickY = rightY
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Task senderTask = RunSenderAsync(cancellationToken);
        Task hapticTask = RunHapticFeedbackAsync(cancellationToken);
        try
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
        finally
        {
            try
            {
                await Task.WhenAll(senderTask, hapticTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RunHapticFeedbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<HapticFeedbackEvent> events;
                try
                {
                    events = DriverControlClient.GetHapticEvents(TimeSpan.FromMilliseconds(100));
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is TimeoutException ||
                    exception is UnauthorizedAccessException)
                {
                    events = Array.Empty<HapticFeedbackEvent>();
                }

                bool active;
                XInputHapticMode mode;
                float leftAmplitude;
                float rightAmplitude;
                lock (syncRoot)
                {
                    active = leftInputEnabled || rightInputEnabled;
                    ApplyHapticEventsLocked(events);
                    long now = Stopwatch.GetTimestamp();
                    leftAmplitude = leftInputEnabled && leftHaptic.ExpiresAt > now
                        ? leftHaptic.Amplitude
                        : 0.0f;
                    rightAmplitude = rightInputEnabled && rightHaptic.ExpiresAt > now
                        ? rightHaptic.Amplitude
                        : 0.0f;
                    mode = configuration.HapticMode;
                }

                if (active && TryGetFirstConnectedController(out uint controllerIndex, out _))
                {
                    (ushort leftMotor, ushort rightMotor) = ResolveMotorSpeeds(
                        mode,
                        leftAmplitude,
                        rightAmplitude);
                    SetVibration(controllerIndex, leftMotor, rightMotor);
                }
                else
                {
                    StopVibration();
                }

                await Task.Delay(
                    active ? HapticPollIntervalMilliseconds : IdleIntervalMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            StopVibration();
        }
    }

    private void ApplyHapticEventsLocked(IReadOnlyList<HapticFeedbackEvent> events)
    {
        long now = Stopwatch.GetTimestamp();
        foreach (HapticFeedbackEvent feedback in events)
        {
            bool enabled = feedback.Hand == ControllerHand.Left
                ? leftInputEnabled
                : feedback.Hand == ControllerHand.Right && rightInputEnabled;
            if (!enabled)
            {
                continue;
            }
            float amplitude = float.IsFinite(feedback.Amplitude)
                ? Math.Clamp(feedback.Amplitude, 0.0f, 1.0f)
                : 0.0f;
            float duration = float.IsFinite(feedback.DurationSeconds)
                ? Math.Clamp(feedback.DurationSeconds, 0.0f, 10.0f)
                : 0.0f;
            HapticEnvelope envelope = amplitude > 0.0f
                ? new HapticEnvelope(
                    amplitude,
                    now + (long)(Stopwatch.Frequency *
                        (duration > 0.0f ? duration : SinglePulseDurationSeconds)))
                : default;
            if (feedback.Hand == ControllerHand.Left)
            {
                leftHaptic = envelope;
            }
            else
            {
                rightHaptic = envelope;
            }
        }
    }

    internal static (ushort LeftMotor, ushort RightMotor) ResolveMotorSpeeds(
        XInputHapticMode mode,
        float leftHandAmplitude,
        float rightHandAmplitude)
    {
        float left = Math.Clamp(leftHandAmplitude, 0.0f, 1.0f);
        float right = Math.Clamp(rightHandAmplitude, 0.0f, 1.0f);
        if (mode == XInputHapticMode.MirrorHandedness)
        {
            (left, right) = (right, left);
        }
        else if (mode == XInputHapticMode.Unified)
        {
            left = right = Math.Max(left, right);
        }
        return (
            (ushort)Math.Round(left * ushort.MaxValue),
            (ushort)Math.Round(right * ushort.MaxValue));
    }

    private void SetVibration(uint controllerIndex, ushort leftMotorSpeed, ushort rightMotorSpeed)
    {
        if (vibrationControllerIndex.HasValue && vibrationControllerIndex.Value != controllerIndex)
        {
            XInputVibration stopped = default;
            XInputSetState(vibrationControllerIndex.Value, ref stopped);
        }
        if (vibrationControllerIndex == controllerIndex &&
            lastLeftMotorSpeed == leftMotorSpeed &&
            lastRightMotorSpeed == rightMotorSpeed)
        {
            return;
        }
        var vibration = new XInputVibration
        {
            LeftMotorSpeed = leftMotorSpeed,
            RightMotorSpeed = rightMotorSpeed
        };
        if (XInputSetState(controllerIndex, ref vibration) == ErrorSuccess)
        {
            vibrationControllerIndex = controllerIndex;
            lastLeftMotorSpeed = leftMotorSpeed;
            lastRightMotorSpeed = rightMotorSpeed;
        }
    }

    private void StopVibration()
    {
        if (!vibrationControllerIndex.HasValue)
        {
            return;
        }
        XInputVibration vibration = default;
        XInputSetState(vibrationControllerIndex.Value, ref vibration);
        vibrationControllerIndex = null;
        lastLeftMotorSpeed = 0;
        lastRightMotorSpeed = 0;
    }

    private async Task RunSenderAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await sendSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

            ControllerInputState? left;
            ControllerInputState? right;
            lock (syncRoot)
            {
                left = pendingLeftState;
                right = pendingRightState;
                pendingLeftState = null;
                pendingRightState = null;
            }

            if (left != null)
            {
                Send(ControllerHand.Left, left);
            }
            if (right != null)
            {
                Send(ControllerHand.Right, right);
            }
        }
    }

    private void ApplySnapshotLocked(XInputGamepadSnapshot snapshot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HashSet<XInputBindingSource> occupiedSources = XInputControllerMapper.GetOccupiedSources(configuration);
        if (leftInputEnabled)
        {
            ControllerInputState next = XInputControllerMapper.Map(
                snapshot,
                configuration.Left,
                configuration.AnalogPressThreshold,
                leftState,
                occupiedSources);
            if (!controllerConnected || !StatesEqual(leftState, next) || now - leftLastSend >= RefreshInterval)
            {
                leftState = next;
                QueueSendLocked(ControllerHand.Left, next);
                leftLastSend = now;
            }
        }
        if (rightInputEnabled)
        {
            ControllerInputState next = XInputControllerMapper.Map(
                snapshot,
                configuration.Right,
                configuration.AnalogPressThreshold,
                rightState,
                occupiedSources);
            if (!controllerConnected || !StatesEqual(rightState, next) || now - rightLastSend >= RefreshInterval)
            {
                rightState = next;
                QueueSendLocked(ControllerHand.Right, next);
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
        return TryGetFirstConnectedController(out _, out snapshot);
    }

    private static bool TryGetFirstConnectedController(
        out uint controllerIndex,
        out XInputGamepadSnapshot snapshot)
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
                controllerIndex = index;
                return true;
            }
        }
        controllerIndex = 0;
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
            QueueSendLocked(hand, neutral);
        }
    }

    private void QueueSendLocked(ControllerHand hand, ControllerInputState state)
    {
        if (hand == ControllerHand.Left)
        {
            pendingLeftState = CloneState(state);
        }
        else
        {
            pendingRightState = CloneState(state);
        }
        if (sendSignal.CurrentCount == 0)
        {
            sendSignal.Release();
        }
    }

    private static bool StatesEqual(ControllerInputState left, ControllerInputState right)
    {
        return left.JoystickX == right.JoystickX &&
            left.JoystickY == right.JoystickY &&
            left.TriggerValue == right.TriggerValue &&
            left.GripValue == right.GripValue &&
            left.JoystickClick == right.JoystickClick &&
            left.PrimaryButton == right.PrimaryButton &&
            left.SecondaryButton == right.SecondaryButton &&
            left.MenuButton == right.MenuButton &&
            left.HasExplicitTouchState == right.HasExplicitTouchState &&
            left.JoystickTouch == right.JoystickTouch &&
            left.TriggerTouch == right.TriggerTouch &&
            left.GripTouch == right.GripTouch &&
            left.PrimaryTouch == right.PrimaryTouch &&
            left.SecondaryTouch == right.SecondaryTouch &&
            left.MenuTouch == right.MenuTouch &&
            left.ThumbRestTouch == right.ThumbRestTouch;
    }

    private static ControllerInputState CloneState(ControllerInputState value)
    {
        value ??= new ControllerInputState();
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

    private static XInputConfiguration CloneConfiguration(XInputConfiguration? value)
    {
        XInputConfiguration source = value ?? XInputConfiguration.CreateDefault();
        return new XInputConfiguration
        {
            AnalogPressThreshold = source.AnalogPressThreshold,
            HapticMode = source.HapticMode,
            Left = CloneMapping(source.Left ?? XInputConfiguration.CreateDefaultLeftMapping()),
            Right = CloneMapping(source.Right ?? XInputConfiguration.CreateDefaultRightMapping())
        };
    }

    private static XInputHandMapping CloneMapping(XInputHandMapping value)
    {
        return new XInputHandMapping
        {
            PrimaryButton = value.PrimaryButton,
            SecondaryButton = value.SecondaryButton,
            Joystick = value.Joystick,
            JoystickClick = value.JoystickClick,
            Trigger = value.Trigger,
            Grip = value.Grip,
            MenuButton = value.MenuButton,
            ThumbTouch = CloneTouchAssist(value.ThumbTouch),
            IndexTouch = CloneTouchAssist(value.IndexTouch)
        };
    }

    private static XInputTouchAssistMapping CloneTouchAssist(XInputTouchAssistMapping? value)
    {
        value ??= new XInputTouchAssistMapping();
        return new XInputTouchAssistMapping
        {
            ToggleSource = value.ToggleSource,
            DefaultTouched = value.DefaultTouched
        };
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
        bool resetLeft;
        bool resetRight;
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            resetLeft = leftInputEnabled;
            resetRight = rightInputEnabled;
            if (resetLeft)
            {
                ResetLocked(ControllerHand.Left, send: false);
            }
            if (resetRight)
            {
                ResetLocked(ControllerHand.Right, send: false);
            }
        }
        if (resetLeft)
        {
            Send(ControllerHand.Left, new ControllerInputState());
        }
        if (resetRight)
        {
            Send(ControllerHand.Right, new ControllerInputState());
        }
        sendSignal.Dispose();
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint XInputSetState(uint userIndex, ref XInputVibration vibration);

    private readonly record struct HapticEnvelope(float Amplitude, long ExpiresAt);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

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
    public static ControllerInputState MapLeft(XInputGamepadSnapshot gamepad)
    {
        XInputConfiguration configuration = XInputConfiguration.CreateDefault();
        return Map(gamepad, configuration.Left, configuration.AnalogPressThreshold, new ControllerInputState());
    }

    public static ControllerInputState MapRight(XInputGamepadSnapshot gamepad)
    {
        XInputConfiguration configuration = XInputConfiguration.CreateDefault();
        return Map(gamepad, configuration.Right, configuration.AnalogPressThreshold, new ControllerInputState());
    }

    public static ControllerInputState Map(
        XInputGamepadSnapshot gamepad,
        XInputHandMapping mapping,
        float analogPressThreshold,
        ControllerInputState previous,
        ISet<XInputBindingSource>? occupiedSources = null)
    {
        mapping ??= new XInputHandMapping();
        previous ??= new ControllerInputState();
        (float x, float y) = ResolveJoystick(gamepad, mapping.Joystick);
        float triggerValue = ResolveScalar(gamepad, mapping.Trigger);
        float gripValue = ResolveScalar(gamepad, mapping.Grip);
        bool joystickClick = ResolveBoolean(gamepad, mapping.JoystickClick, analogPressThreshold, previous.JoystickClick);
        bool primaryButton = ResolveBoolean(gamepad, mapping.PrimaryButton, analogPressThreshold, previous.PrimaryButton);
        bool secondaryButton = ResolveBoolean(gamepad, mapping.SecondaryButton, analogPressThreshold, previous.SecondaryButton);
        bool menuButton = ResolveBoolean(gamepad, mapping.MenuButton, analogPressThreshold, previous.MenuButton);
        bool thumbAssist = ResolveTouchAssist(
            gamepad,
            mapping.ThumbTouch,
            analogPressThreshold,
            occupiedSources);
        bool indexAssist = ResolveTouchAssist(
            gamepad,
            mapping.IndexTouch,
            analogPressThreshold,
            occupiedSources);
        bool joystickActive = joystickClick || x * x + y * y > 0.0025F;
        bool hasExplicitThumbInput = secondaryButton || primaryButton || joystickActive || menuButton;
        return new ControllerInputState
        {
            JoystickX = x,
            JoystickY = y,
            JoystickClick = joystickClick,
            TriggerValue = triggerValue,
            GripValue = gripValue,
            PrimaryButton = primaryButton,
            SecondaryButton = secondaryButton,
            MenuButton = menuButton,
            HasExplicitTouchState = true,
            JoystickTouch = joystickActive || (!hasExplicitThumbInput && thumbAssist),
            TriggerTouch = triggerValue > 0.0F || indexAssist,
            GripTouch = gripValue > 0.0F,
            PrimaryTouch = primaryButton,
            SecondaryTouch = secondaryButton,
            MenuTouch = menuButton,
            ThumbRestTouch = false
        };
    }

    internal static HashSet<XInputBindingSource> GetOccupiedSources(XInputConfiguration configuration)
    {
        var occupied = new HashSet<XInputBindingSource>();
        AddOccupiedSources(occupied, configuration?.Left);
        AddOccupiedSources(occupied, configuration?.Right);
        occupied.Remove(XInputBindingSource.None);
        return occupied;
    }

    private static void AddOccupiedSources(ISet<XInputBindingSource> occupied, XInputHandMapping? mapping)
    {
        if (mapping == null)
        {
            return;
        }
        occupied.Add(mapping.PrimaryButton);
        occupied.Add(mapping.SecondaryButton);
        occupied.Add(mapping.Joystick);
        occupied.Add(mapping.JoystickClick);
        occupied.Add(mapping.Trigger);
        occupied.Add(mapping.Grip);
        occupied.Add(mapping.MenuButton);
    }

    private static bool ResolveTouchAssist(
        XInputGamepadSnapshot gamepad,
        XInputTouchAssistMapping? mapping,
        float analogPressThreshold,
        ISet<XInputBindingSource>? occupiedSources)
    {
        mapping ??= new XInputTouchAssistMapping();
        bool toggleAvailable = mapping.ToggleSource != XInputBindingSource.None &&
            (occupiedSources == null || !occupiedSources.Contains(mapping.ToggleSource));
        bool togglePressed = toggleAvailable && ResolveBoolean(
            gamepad,
            mapping.ToggleSource,
            analogPressThreshold,
            previous: false);
        return togglePressed ? !mapping.DefaultTouched : mapping.DefaultTouched;
    }

    private static (float X, float Y) ResolveJoystick(
        XInputGamepadSnapshot gamepad,
        XInputBindingSource source)
    {
        if (source == XInputBindingSource.LeftStick)
        {
            return NormalizeStick(
                gamepad.ThumbLX,
                gamepad.ThumbLY,
                XInputInputService.GetLeftStickDeadzone());
        }
        if (source == XInputBindingSource.RightStick)
        {
            return NormalizeStick(
                gamepad.ThumbRX,
                gamepad.ThumbRY,
                XInputInputService.GetRightStickDeadzone());
        }
        return (0.0f, 0.0f);
    }

    private static float ResolveScalar(XInputGamepadSnapshot gamepad, XInputBindingSource source)
    {
        if (source == XInputBindingSource.LeftTrigger)
        {
            return gamepad.LeftTrigger / 255.0f;
        }
        if (source == XInputBindingSource.RightTrigger)
        {
            return gamepad.RightTrigger / 255.0f;
        }
        return ResolveDigitalButton(gamepad, source) ? 1.0f : 0.0f;
    }

    private static bool ResolveBoolean(
        XInputGamepadSnapshot gamepad,
        XInputBindingSource source,
        float pressThreshold,
        bool previous)
    {
        if (XInputConfiguration.IsAnalogSource(source))
        {
            float value = ResolveScalar(gamepad, source);
            float releaseThreshold = Math.Max(
                XInputConfiguration.MinimumAnalogPressThreshold,
                pressThreshold - XInputConfiguration.AnalogReleaseHysteresis);
            return previous ? value >= releaseThreshold : value >= pressThreshold;
        }
        return ResolveDigitalButton(gamepad, source);
    }

    private static bool ResolveDigitalButton(XInputGamepadSnapshot gamepad, XInputBindingSource source)
    {
        ushort mask = source switch
        {
            XInputBindingSource.LeftStickClick => LeftThumb,
            XInputBindingSource.RightStickClick => RightThumb,
            XInputBindingSource.LeftShoulder => LeftShoulder,
            XInputBindingSource.RightShoulder => RightShoulder,
            XInputBindingSource.View => View,
            XInputBindingSource.Menu => Start,
            XInputBindingSource.DPadUp => DPadUp,
            XInputBindingSource.DPadDown => DPadDown,
            XInputBindingSource.DPadLeft => DPadLeft,
            XInputBindingSource.DPadRight => DPadRight,
            XInputBindingSource.A => A,
            XInputBindingSource.B => B,
            XInputBindingSource.X => X,
            XInputBindingSource.Y => Y,
            _ => 0
        };
        return mask != 0 && HasButton(gamepad, mask);
    }

    private static bool HasButton(XInputGamepadSnapshot gamepad, ushort button)
    {
        return (gamepad.Buttons & button) != 0;
    }

    internal static (float X, float Y) NormalizeStick(short rawX, short rawY, short deadzone)
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
