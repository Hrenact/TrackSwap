using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class OscInputService : IDisposable
{
    private readonly object syncRoot = new();
    private readonly Action<ControllerHand, ControllerInputState> controllerInputSender;
    private readonly Action<byte[], string, int>? testHapticPacketSender;
    private OscConfiguration configuration;
    private UdpClient? listener;
    private UdpClient? sender;
    private ControllerInputState leftState = new();
    private ControllerInputState rightState = new();
    private DateTimeOffset? lastMessage;
    private HapticFeedbackEvent leftHaptic = new();
    private HapticFeedbackEvent rightHaptic = new();
    private DateTimeOffset? leftHapticAtUtc;
    private DateTimeOffset? rightHapticAtUtc;
    private readonly List<OscHapticPreviewSample> leftHapticHistory = new();
    private readonly List<OscHapticPreviewSample> rightHapticHistory = new();
    private CancellationTokenSource? leftTestCancellation;
    private CancellationTokenSource? rightTestCancellation;
    private bool listening;
    private string lastError = string.Empty;
    private bool receivePortInUse;
    private string lastSendError = string.Empty;
    private int configurationVersion;
    private bool leftInputEnabled;
    private bool rightInputEnabled;
    private bool leftThumbTouchAssist;
    private bool leftIndexTouchAssist;
    private bool rightThumbTouchAssist;
    private bool rightIndexTouchAssist;
    private DateTimeOffset leftInputSentAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset rightInputSentAtUtc = DateTimeOffset.MinValue;
    private static readonly OscControllerAddresses LeftAddresses =
        OscControllerAddresses.ForHand(ControllerHand.Left);
    private static readonly OscControllerAddresses RightAddresses =
        OscControllerAddresses.ForHand(ControllerHand.Right);
    private static readonly HapticTestStep[] TestPattern =
    {
        new(0.08f, 6.0f, 0.20f, TimeSpan.FromMilliseconds(480)),
        new(0.20f, 18.0f, 0.55f, TimeSpan.FromMilliseconds(600)),
        new(0.36f, 40.0f, 1.00f, TimeSpan.FromMilliseconds(760)),
        new(0.07f, 30.0f, 0.85f, TimeSpan.FromMilliseconds(260)),
        new(0.07f, 30.0f, 0.85f, TimeSpan.Zero)
    };

    public OscInputService(
        OscConfiguration initialConfiguration,
        IEnumerable<RouteConfiguration>? initialRoutes = null,
        Action<ControllerHand, ControllerInputState>? controllerInputSender = null,
        Action<byte[], string, int>? testHapticPacketSender = null)
    {
        this.controllerInputSender = controllerInputSender ?? ((hand, state) =>
            DriverControlClient.ApplyControllerInput(hand, state, TimeSpan.FromMilliseconds(250)));
        this.testHapticPacketSender = testHapticPacketSender;
        configuration = Clone(initialConfiguration);
        configuration.Enabled = OscConfiguration.IsRequiredForRoutes(initialRoutes);
        UpdateActiveInputsLocked(initialRoutes);
        ResetLocked(ControllerHand.Left);
        ResetLocked(ControllerHand.Right);
    }

    public void Update(OscConfiguration value, IEnumerable<RouteConfiguration>? routes = null)
    {
        ControllerInputState? leftReset = null;
        ControllerInputState? rightReset = null;
        lock (syncRoot)
        {
            bool resetLeft = leftInputEnabled || HasOscInputRoute(routes, ControllerHand.Left);
            bool resetRight = rightInputEnabled || HasOscInputRoute(routes, ControllerHand.Right);
            configuration = Clone(value);
            configuration.Enabled = OscConfiguration.IsRequiredForRoutes(routes);
            UpdateActiveInputsLocked(routes);
            CancelTestPatternLocked(ControllerHand.Left);
            CancelTestPatternLocked(ControllerHand.Right);
            configurationVersion++;
            listener?.Dispose();
            listener = null;
            sender?.Dispose();
            sender = null;
            listening = false;
            receivePortInUse = false;
            lastSendError = string.Empty;
            if (resetLeft)
            {
                ResetLocked(ControllerHand.Left);
                leftReset = Clone(leftState);
            }
            if (resetRight)
            {
                ResetLocked(ControllerHand.Right);
                rightReset = Clone(rightState);
            }
        }
        if (leftReset != null) Send(ControllerHand.Left, leftReset);
        if (rightReset != null) Send(ControllerHand.Right, rightReset);
    }

    public OscRuntimeStatus GetStatus()
    {
        lock (syncRoot)
        {
            PruneHapticHistoryLocked(DateTimeOffset.UtcNow);
            return new OscRuntimeStatus
            {
                Enabled = configuration.Enabled,
                Listening = listening,
                Endpoint = configuration.ListenAddress + ":" + configuration.Port,
                SendEndpoint = configuration.ListenAddress + ":" + configuration.SendPort,
                LastMessageAtUtc = lastMessage,
                LastError = lastError,
                ReceivePortInUse = receivePortInUse,
                LeftInput = Clone(leftState),
                RightInput = Clone(rightState),
                LeftThumbTouchAssist = leftThumbTouchAssist,
                LeftIndexTouchAssist = leftIndexTouchAssist,
                RightThumbTouchAssist = rightThumbTouchAssist,
                RightIndexTouchAssist = rightIndexTouchAssist,
                LeftHaptic = Clone(leftHaptic),
                RightHaptic = Clone(rightHaptic),
                LeftHapticAtUtc = leftHapticAtUtc,
                RightHapticAtUtc = rightHapticAtUtc,
                LastSendError = lastSendError,
                LeftHapticHistory = leftHapticHistory.Select(Clone).ToList(),
                RightHapticHistory = rightHapticHistory.Select(Clone).ToList()
            };
        }
    }

    public async Task SendHapticEventsAsync(
        IReadOnlyList<HapticFeedbackEvent> events,
        CancellationToken cancellationToken)
    {
        await SendHapticEventsCoreAsync(events, requireOwnedRoute: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SendTestHapticAsync(
        ControllerHand hand,
        CancellationToken cancellationToken)
    {
        if (hand != ControllerHand.Left && hand != ControllerHand.Right)
        {
            throw new InvalidDataException("测试震动的手别无效。");
        }

        var patternCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (syncRoot)
        {
            CancelTestPatternLocked(hand);
            SetTestCancellationLocked(hand, patternCancellation);
        }
        _ = Task.Factory.StartNew(
            () => RunTestPattern(hand, patternCancellation),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private void RunTestPattern(
        ControllerHand hand,
        CancellationTokenSource patternCancellation)
    {
        try
        {
            CancellationToken cancellationToken = patternCancellation.Token;
            using var testSender = new UdpClient();
            var timeline = Stopwatch.StartNew();
            TimeSpan deadline = TimeSpan.Zero;
            for (int index = 0; index < TestPattern.Length; index++)
            {
                TimeSpan remaining = deadline - timeline.Elapsed;
                if (remaining > TimeSpan.Zero && cancellationToken.WaitHandle.WaitOne(remaining))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                cancellationToken.ThrowIfCancellationRequested();
                SendTestStep(testSender, hand, TestPattern[index], cancellationToken);
                deadline += TestPattern[index].DelayUntilNext;
            }
        }
        catch (OperationCanceledException) when (patternCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            CompleteTestPattern(hand, patternCancellation);
        }
    }

    private void SendTestStep(
        UdpClient testSender,
        ControllerHand hand,
        HapticTestStep step,
        CancellationToken cancellationToken)
    {
        string address;
        string host;
        int port;
        lock (syncRoot)
        {
            address = hand == ControllerHand.Left
                ? LeftAddresses.Haptic
                : RightAddresses.Haptic;
            host = configuration.ListenAddress;
            port = configuration.SendPort;
        }

        var feedback = NormalizeHapticFeedback(new HapticFeedbackEvent
        {
            Hand = hand,
            DurationSeconds = step.DurationSeconds,
            Frequency = step.Frequency,
            Amplitude = step.Amplitude
        });
        byte[] packet = OscPacketWriter.BuildHapticMessage(
            address,
            feedback.DurationSeconds,
            feedback.Frequency,
            feedback.Amplitude);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (testHapticPacketSender is null)
            {
                testSender.Send(packet, packet.Length, host, port);
            }
            else
            {
                testHapticPacketSender(packet, host, port);
            }
            RecordHapticSend(feedback);
        }
        catch (Exception exception) when (
            exception is SocketException ||
            exception is ObjectDisposedException ||
            exception is ArgumentException)
        {
            lock (syncRoot)
            {
                lastSendError = exception.Message;
            }
        }
    }

    private async Task SendHapticEventsCoreAsync(
        IReadOnlyList<HapticFeedbackEvent> events,
        bool requireOwnedRoute,
        CancellationToken cancellationToken)
    {
        foreach (HapticFeedbackEvent feedback in events ?? Array.Empty<HapticFeedbackEvent>())
        {
            string address;
            string host;
            int port;
            UdpClient activeSender;
            lock (syncRoot)
            {
                bool enabled = feedback.Hand == ControllerHand.Left
                    ? leftInputEnabled
                    : feedback.Hand == ControllerHand.Right && rightInputEnabled;
                if (requireOwnedRoute && !enabled)
                {
                    continue;
                }

                address = feedback.Hand == ControllerHand.Left
                    ? LeftAddresses.Haptic
                    : RightAddresses.Haptic;
                host = configuration.ListenAddress;
                port = configuration.SendPort;
                sender ??= new UdpClient();
                activeSender = sender;
            }

            HapticFeedbackEvent normalized = NormalizeHapticFeedback(feedback);
            byte[] packet = OscPacketWriter.BuildHapticMessage(
                address,
                normalized.DurationSeconds,
                normalized.Frequency,
                normalized.Amplitude);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await activeSender.SendAsync(packet, packet.Length, host, port).ConfigureAwait(false);
                RecordHapticSend(normalized);
            }
            catch (Exception exception) when (
                exception is SocketException ||
                exception is ObjectDisposedException ||
                exception is ArgumentException)
            {
                lock (syncRoot)
                {
                    lastSendError = exception.Message;
                }
            }
        }
    }

    private static HapticFeedbackEvent NormalizeHapticFeedback(HapticFeedbackEvent feedback)
    {
        return new HapticFeedbackEvent
        {
            Sequence = feedback.Sequence,
            Hand = feedback.Hand,
            DurationSeconds = float.IsFinite(feedback.DurationSeconds)
                ? Math.Clamp(feedback.DurationSeconds, 0.0f, 10.0f)
                : 0.0f,
            Frequency = float.IsFinite(feedback.Frequency)
                ? Math.Max(0.0f, feedback.Frequency)
                : 0.0f,
            Amplitude = float.IsFinite(feedback.Amplitude)
                ? Math.Clamp(feedback.Amplitude, 0.0f, 1.0f)
                : 0.0f
        };
    }

    private void RecordHapticSend(HapticFeedbackEvent feedback)
    {
        lock (syncRoot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (feedback.Hand == ControllerHand.Left)
            {
                leftHaptic = feedback;
                leftHapticAtUtc = now;
                AppendHapticPreviewSampleLocked(
                    leftHapticHistory,
                    CreatePreviewSample(feedback, now));
            }
            else
            {
                rightHaptic = feedback;
                rightHapticAtUtc = now;
                AppendHapticPreviewSampleLocked(
                    rightHapticHistory,
                    CreatePreviewSample(feedback, now));
            }
            PruneHapticHistoryLocked(now);
            lastSendError = string.Empty;
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
                RefreshControllerInputsIfDue();
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
                    receivePortInUse = exception.SocketErrorCode == SocketError.AddressAlreadyInUse;
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
            receivePortInUse = false;
            return listener;
        }
    }

    private void HandlePacket(IReadOnlyList<OscValue> values)
    {
        ControllerInputState? leftToSend = null;
        ControllerInputState? rightToSend = null;
        lock (syncRoot)
        {
            ControllerInputFingerprint leftBefore = ControllerInputFingerprint.Capture(
                leftState,
                leftThumbTouchAssist,
                leftIndexTouchAssist);
            ControllerInputFingerprint rightBefore = ControllerInputFingerprint.Capture(
                rightState,
                rightThumbTouchAssist,
                rightIndexTouchAssist);
            bool leftRecognized = false;
            bool rightRecognized = false;
            foreach (OscValue value in values)
            {
                leftRecognized |= leftInputEnabled && Apply(
                    LeftAddresses,
                    leftState,
                    configuration.LeftTouchAssist,
                    value,
                    ref leftThumbTouchAssist,
                    ref leftIndexTouchAssist);
                rightRecognized |= rightInputEnabled && Apply(
                    RightAddresses,
                    rightState,
                    configuration.RightTouchAssist,
                    value,
                    ref rightThumbTouchAssist,
                    ref rightIndexTouchAssist);
            }
            if (leftRecognized && leftBefore != ControllerInputFingerprint.Capture(
                leftState,
                leftThumbTouchAssist,
                leftIndexTouchAssist))
            {
                leftToSend = Clone(leftState);
            }
            if (rightRecognized && rightBefore != ControllerInputFingerprint.Capture(
                rightState,
                rightThumbTouchAssist,
                rightIndexTouchAssist))
            {
                rightToSend = Clone(rightState);
            }
            if (leftRecognized || rightRecognized)
            {
                lastMessage = DateTimeOffset.UtcNow;
            }
        }
        if (leftToSend != null) Send(ControllerHand.Left, leftToSend);
        if (rightToSend != null) Send(ControllerHand.Right, rightToSend);
    }

    private void UpdateActiveInputsLocked(IEnumerable<RouteConfiguration>? routes)
    {
        leftInputEnabled = HasOscInputRoute(routes, ControllerHand.Left);
        rightInputEnabled = HasOscInputRoute(routes, ControllerHand.Right);
    }

    private void CancelTestPatternLocked(ControllerHand hand)
    {
        CancellationTokenSource? cancellation = hand == ControllerHand.Left
            ? leftTestCancellation
            : rightTestCancellation;
        if (cancellation == null)
        {
            return;
        }
        if (hand == ControllerHand.Left)
        {
            leftTestCancellation = null;
        }
        else
        {
            rightTestCancellation = null;
        }
        cancellation.Cancel();
    }

    private void SetTestCancellationLocked(
        ControllerHand hand,
        CancellationTokenSource cancellation)
    {
        if (hand == ControllerHand.Left)
        {
            leftTestCancellation = cancellation;
        }
        else
        {
            rightTestCancellation = cancellation;
        }
    }

    private void CompleteTestPattern(
        ControllerHand hand,
        CancellationTokenSource cancellation)
    {
        lock (syncRoot)
        {
            CompleteTestPatternLocked(hand, cancellation);
        }
    }

    private void CompleteTestPatternLocked(
        ControllerHand hand,
        CancellationTokenSource cancellation)
    {
        CancellationTokenSource? active = hand == ControllerHand.Left
            ? leftTestCancellation
            : rightTestCancellation;
        if (ReferenceEquals(active, cancellation))
        {
            if (hand == ControllerHand.Left)
            {
                leftTestCancellation = null;
            }
            else
            {
                rightTestCancellation = null;
            }
        }
        cancellation.Dispose();
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

    private void ResetLocked(ControllerHand hand)
    {
        if (hand == ControllerHand.Left)
        {
            leftState = new ControllerInputState();
            leftThumbTouchAssist = false;
            leftIndexTouchAssist = false;
            if (leftInputEnabled)
            {
                ApplyTouchState(
                    leftState,
                    configuration.LeftTouchAssist,
                    leftThumbTouchAssist,
                    leftIndexTouchAssist);
            }
            leftHaptic = new HapticFeedbackEvent();
            leftHapticAtUtc = null;
            leftHapticHistory.Clear();
        }
        else
        {
            rightState = new ControllerInputState();
            rightThumbTouchAssist = false;
            rightIndexTouchAssist = false;
            if (rightInputEnabled)
            {
                ApplyTouchState(
                    rightState,
                    configuration.RightTouchAssist,
                    rightThumbTouchAssist,
                    rightIndexTouchAssist);
            }
            rightHaptic = new HapticFeedbackEvent();
            rightHapticAtUtc = null;
            rightHapticHistory.Clear();
        }
    }

    private void Send(ControllerHand hand, ControllerInputState state)
    {
        lock (syncRoot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (hand == ControllerHand.Left)
            {
                leftInputSentAtUtc = now;
            }
            else
            {
                rightInputSentAtUtc = now;
            }
        }
        try
        {
            controllerInputSender(hand, state);
        }
        catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            lock (syncRoot)
            {
                lastError = exception.Message;
            }
        }
    }

    private void RefreshControllerInputsIfDue()
    {
        ControllerInputState? leftToSend = null;
        ControllerInputState? rightToSend = null;
        lock (syncRoot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (leftInputEnabled && now - leftInputSentAtUtc >= TimeSpan.FromSeconds(1))
            {
                leftToSend = Clone(leftState);
            }
            if (rightInputEnabled && now - rightInputSentAtUtc >= TimeSpan.FromSeconds(1))
            {
                rightToSend = Clone(rightState);
            }
        }
        if (leftToSend != null) Send(ControllerHand.Left, leftToSend);
        if (rightToSend != null) Send(ControllerHand.Right, rightToSend);
    }

    private static bool Apply(
        OscControllerAddresses addresses,
        ControllerInputState state,
        OscTouchAssistConfiguration touchAssist,
        OscValue value,
        ref bool thumbTouchAssist,
        ref bool indexTouchAssist)
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
        else if (value.Address == addresses.ThumbTouchAssist) thumbTouchAssist = pressed;
        else if (value.Address == addresses.IndexTouchAssist) indexTouchAssist = pressed;
        else return false;
        ApplyTouchState(state, touchAssist, thumbTouchAssist, indexTouchAssist);
        return true;
    }

    private static void ApplyTouchState(
        ControllerInputState state,
        OscTouchAssistConfiguration touchAssist,
        bool thumbTouchAssist,
        bool indexTouchAssist)
    {
        touchAssist ??= new OscTouchAssistConfiguration();
        bool thumbTouched = touchAssist.ThumbDefaultTouched != thumbTouchAssist;
        bool indexTouched = touchAssist.IndexDefaultTouched != indexTouchAssist;
        bool joystickActive = state.JoystickClick ||
            state.JoystickX * state.JoystickX + state.JoystickY * state.JoystickY > 0.0025F;
        bool hasExplicitThumbInput = state.SecondaryButton ||
            state.PrimaryButton ||
            joystickActive ||
            state.MenuButton;
        state.HasExplicitTouchState = true;
        state.JoystickTouch = joystickActive || (!hasExplicitThumbInput && thumbTouched);
        state.TriggerTouch = state.TriggerValue > 0.0F || indexTouched;
        state.GripTouch = state.GripValue > 0.0F;
        state.PrimaryTouch = state.PrimaryButton;
        state.SecondaryTouch = state.SecondaryButton;
        state.MenuTouch = state.MenuButton;
        state.ThumbRestTouch = false;
    }

    private static OscConfiguration Clone(OscConfiguration value)
    {
        return new OscConfiguration
        {
            Enabled = value?.Enabled == true,
            ListenAddress = value?.ListenAddress ?? "127.0.0.1",
            Port = value?.Port ?? 9015,
            SendPort = value?.SendPort ?? 9016,
            LeftTouchAssist = Clone(value?.LeftTouchAssist),
            RightTouchAssist = Clone(value?.RightTouchAssist)
        };
    }

    private static OscTouchAssistConfiguration Clone(OscTouchAssistConfiguration? value)
    {
        return new OscTouchAssistConfiguration
        {
            ThumbDefaultTouched = value?.ThumbDefaultTouched ?? true,
            IndexDefaultTouched = value?.IndexDefaultTouched ?? true
        };
    }

    private static HapticFeedbackEvent Clone(HapticFeedbackEvent value)
    {
        return new HapticFeedbackEvent
        {
            Sequence = value.Sequence,
            Hand = value.Hand,
            DurationSeconds = value.DurationSeconds,
            Frequency = value.Frequency,
            Amplitude = value.Amplitude
        };
    }

    private static OscHapticPreviewSample CreatePreviewSample(
        HapticFeedbackEvent value,
        DateTimeOffset occurredAtUtc)
    {
        return new OscHapticPreviewSample
        {
            OccurredAtUtc = occurredAtUtc,
            DurationSeconds = value.DurationSeconds,
            Frequency = value.Frequency,
            Amplitude = value.Amplitude
        };
    }

    private static OscHapticPreviewSample Clone(OscHapticPreviewSample value)
    {
        return new OscHapticPreviewSample
        {
            OccurredAtUtc = value.OccurredAtUtc,
            DurationSeconds = value.DurationSeconds,
            Frequency = value.Frequency,
            Amplitude = value.Amplitude
        };
    }

    private static void AppendHapticPreviewSampleLocked(
        List<OscHapticPreviewSample> history,
        OscHapticPreviewSample sample)
    {
        if (history.Count != 0)
        {
            OscHapticPreviewSample previous = history[history.Count - 1];
            double elapsedSeconds = (sample.OccurredAtUtc - previous.OccurredAtUtc).TotalSeconds;
            if (elapsedSeconds >= 0.0 && elapsedSeconds < previous.DurationSeconds)
            {
                previous.DurationSeconds = (float)elapsedSeconds;
            }
        }
        history.Add(sample);
    }

    private void PruneHapticHistoryLocked(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TimeSpan.FromSeconds(4);
        leftHapticHistory.RemoveAll(sample => sample.OccurredAtUtc < cutoff);
        rightHapticHistory.RemoveAll(sample => sample.OccurredAtUtc < cutoff);
        const int maximumSamplesPerHand = 256;
        if (leftHapticHistory.Count > maximumSamplesPerHand)
        {
            leftHapticHistory.RemoveRange(0, leftHapticHistory.Count - maximumSamplesPerHand);
        }
        if (rightHapticHistory.Count > maximumSamplesPerHand)
        {
            rightHapticHistory.RemoveRange(0, rightHapticHistory.Count - maximumSamplesPerHand);
        }
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
        ControllerInputState? leftReset = null;
        ControllerInputState? rightReset = null;
        lock (syncRoot)
        {
            CancelTestPatternLocked(ControllerHand.Left);
            CancelTestPatternLocked(ControllerHand.Right);
            if (leftInputEnabled)
            {
                ResetLocked(ControllerHand.Left);
                leftReset = Clone(leftState);
            }
            if (rightInputEnabled)
            {
                ResetLocked(ControllerHand.Right);
                rightReset = Clone(rightState);
            }
            listener?.Dispose();
            listener = null;
            sender?.Dispose();
            sender = null;
        }
        if (leftReset != null) Send(ControllerHand.Left, leftReset);
        if (rightReset != null) Send(ControllerHand.Right, rightReset);
    }

    private readonly record struct HapticTestStep(
        float DurationSeconds,
        float Frequency,
        float Amplitude,
        TimeSpan DelayUntilNext);

    private readonly record struct ControllerInputFingerprint(
        float JoystickX,
        float JoystickY,
        float TriggerValue,
        float GripValue,
        bool JoystickClick,
        bool PrimaryButton,
        bool SecondaryButton,
        bool MenuButton,
        bool HasExplicitTouchState,
        bool JoystickTouch,
        bool TriggerTouch,
        bool GripTouch,
        bool PrimaryTouch,
        bool SecondaryTouch,
        bool MenuTouch,
        bool ThumbRestTouch,
        bool ThumbTouchAssist,
        bool IndexTouchAssist)
    {
        public static ControllerInputFingerprint Capture(
            ControllerInputState state,
            bool thumbTouchAssist,
            bool indexTouchAssist)
        {
            return new ControllerInputFingerprint(
                state.JoystickX,
                state.JoystickY,
                state.TriggerValue,
                state.GripValue,
                state.JoystickClick,
                state.PrimaryButton,
                state.SecondaryButton,
                state.MenuButton,
                state.HasExplicitTouchState,
                state.JoystickTouch,
                state.TriggerTouch,
                state.GripTouch,
                state.PrimaryTouch,
                state.SecondaryTouch,
                state.MenuTouch,
                state.ThumbRestTouch,
                thumbTouchAssist,
                indexTouchAssist);
        }
    }
}
