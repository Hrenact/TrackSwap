using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class XInputControllerMapperTests
{
    [Theory]
    [InlineData(XInputHapticMode.PreserveHandedness, 0.25f, 0.75f, 16384, 49151)]
    [InlineData(XInputHapticMode.MirrorHandedness, 0.25f, 0.75f, 49151, 16384)]
    [InlineData(XInputHapticMode.Unified, 0.25f, 0.75f, 49151, 49151)]
    public void MapsVirtualHandHapticsToXInputMotors(
        XInputHapticMode mode,
        float leftAmplitude,
        float rightAmplitude,
        ushort expectedLeftMotor,
        ushort expectedRightMotor)
    {
        (ushort leftMotor, ushort rightMotor) = XInputInputService.ResolveMotorSpeeds(
            mode,
            leftAmplitude,
            rightAmplitude);

        Assert.Equal(expectedLeftMotor, leftMotor);
        Assert.Equal(expectedRightMotor, rightMotor);
    }

    [Fact]
    public void FaceButtonsAreSplitAcrossHands()
    {
        var leftButtons = new XInputGamepadSnapshot(
            (ushort)(XInputControllerMapper.X | XInputControllerMapper.Y),
            0, 0, 0, 0, 0, 0);
        var rightButtons = new XInputGamepadSnapshot(
            (ushort)(XInputControllerMapper.A | XInputControllerMapper.B),
            0, 0, 0, 0, 0, 0);

        ControllerInputState left = XInputControllerMapper.MapLeft(leftButtons);
        ControllerInputState rightFromLeftButtons = XInputControllerMapper.MapRight(leftButtons);
        ControllerInputState leftFromRightButtons = XInputControllerMapper.MapLeft(rightButtons);
        ControllerInputState right = XInputControllerMapper.MapRight(rightButtons);

        Assert.True(left.PrimaryButton);
        Assert.True(left.SecondaryButton);
        Assert.False(rightFromLeftButtons.PrimaryButton);
        Assert.False(rightFromLeftButtons.SecondaryButton);
        Assert.False(leftFromRightButtons.PrimaryButton);
        Assert.False(leftFromRightButtons.SecondaryButton);
        Assert.True(right.PrimaryButton);
        Assert.True(right.SecondaryButton);
    }

    [Fact]
    public void ShouldersTriggersAndMenuButtonsMapToTheirHands()
    {
        var gamepad = new XInputGamepadSnapshot(
            (ushort)(XInputControllerMapper.LeftShoulder |
                XInputControllerMapper.RightShoulder |
                XInputControllerMapper.View |
                XInputControllerMapper.Start),
            128, 255, 0, 0, 0, 0);

        ControllerInputState left = XInputControllerMapper.MapLeft(gamepad);
        ControllerInputState right = XInputControllerMapper.MapRight(gamepad);

        Assert.Equal(1.0f, left.TriggerValue);
        Assert.InRange(left.GripValue, 0.50f, 0.51f);
        Assert.Equal(1.0f, right.TriggerValue);
        Assert.Equal(1.0f, right.GripValue);
        Assert.True(left.MenuButton);
        Assert.True(right.MenuButton);
    }

    [Fact]
    public void ViewAndMenuButtonsDoNotCrossHands()
    {
        var viewOnly = new XInputGamepadSnapshot(XInputControllerMapper.View, 0, 0, 0, 0, 0, 0);
        var menuOnly = new XInputGamepadSnapshot(XInputControllerMapper.Start, 0, 0, 0, 0, 0, 0);

        Assert.True(XInputControllerMapper.MapLeft(viewOnly).MenuButton);
        Assert.False(XInputControllerMapper.MapRight(viewOnly).MenuButton);
        Assert.False(XInputControllerMapper.MapLeft(menuOnly).MenuButton);
        Assert.True(XInputControllerMapper.MapRight(menuOnly).MenuButton);
    }

    [Fact]
    public void SticksAndClicksRemainHandSpecific()
    {
        var gamepad = new XInputGamepadSnapshot(
            XInputControllerMapper.LeftThumb,
            0, 0, short.MaxValue, 0, 0, short.MaxValue);

        ControllerInputState left = XInputControllerMapper.MapLeft(gamepad);
        ControllerInputState right = XInputControllerMapper.MapRight(gamepad);

        Assert.InRange(left.JoystickX, 0.99f, 1.0f);
        Assert.Equal(0.0f, left.JoystickY);
        Assert.True(left.JoystickClick);
        Assert.Equal(0.0f, right.JoystickX);
        Assert.InRange(right.JoystickY, 0.99f, 1.0f);
        Assert.False(right.JoystickClick);
    }

    [Fact]
    public void StickDeadzoneProducesNeutralInput()
    {
        var gamepad = new XInputGamepadSnapshot(0, 0, 0, 1000, -1000, -1000, 1000);

        ControllerInputState left = XInputControllerMapper.MapLeft(gamepad);
        ControllerInputState right = XInputControllerMapper.MapRight(gamepad);

        Assert.Equal(0.0f, left.JoystickX);
        Assert.Equal(0.0f, left.JoystickY);
        Assert.Equal(0.0f, right.JoystickX);
        Assert.Equal(0.0f, right.JoystickY);
    }

    [Fact]
    public void AnalogTriggerUsesConfiguredThresholdWithoutHysteresis()
    {
        XInputHandMapping mapping = XInputConfiguration.CreateDefaultLeftMapping();
        mapping.PrimaryButton = XInputBindingSource.LeftTrigger;

        ControllerInputState below = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(0, 127, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            new ControllerInputState());
        ControllerInputState pressed = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(0, 128, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            below);
        ControllerInputState releasedBelowThreshold = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(0, 117, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            pressed);

        Assert.False(below.PrimaryButton);
        Assert.True(pressed.PrimaryButton);
        Assert.False(releasedBelowThreshold.PrimaryButton);
    }

    [Fact]
    public void DigitalButtonMappedToScalarProducesZeroOrOne()
    {
        XInputHandMapping mapping = XInputConfiguration.CreateDefaultLeftMapping();
        mapping.Trigger = XInputBindingSource.A;

        ControllerInputState released = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(0, 0, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            new ControllerInputState());
        ControllerInputState pressed = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(XInputControllerMapper.A, 0, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            released);

        Assert.Equal(0.0f, released.TriggerValue);
        Assert.Equal(1.0f, pressed.TriggerValue);
    }

    [Fact]
    public void CustomMappingCanMoveInputsAcrossHands()
    {
        var mapping = new XInputHandMapping
        {
            PrimaryButton = XInputBindingSource.B,
            SecondaryButton = XInputBindingSource.DPadUp,
            Joystick = XInputBindingSource.RightStick,
            JoystickClick = XInputBindingSource.RightStickClick,
            Trigger = XInputBindingSource.RightTrigger,
            Grip = XInputBindingSource.LeftShoulder,
            MenuButton = XInputBindingSource.Menu
        };
        var gamepad = new XInputGamepadSnapshot(
            (ushort)(XInputControllerMapper.B |
                XInputControllerMapper.DPadUp |
                XInputControllerMapper.RightThumb |
                XInputControllerMapper.LeftShoulder |
                XInputControllerMapper.Start),
            0,
            255,
            0,
            0,
            short.MaxValue,
            0);

        ControllerInputState state = XInputControllerMapper.Map(
            gamepad,
            mapping,
            0.5f,
            new ControllerInputState());

        Assert.True(state.PrimaryButton);
        Assert.True(state.SecondaryButton);
        Assert.InRange(state.JoystickX, 0.99f, 1.0f);
        Assert.True(state.JoystickClick);
        Assert.Equal(1.0f, state.TriggerValue);
        Assert.Equal(1.0f, state.GripValue);
        Assert.True(state.MenuButton);
    }

    [Fact]
    public void TouchAssistInvertsTheConfiguredDefaultWhileHeld()
    {
        XInputHandMapping mapping = XInputConfiguration.CreateDefaultLeftMapping();
        mapping.ThumbTouch = new XInputTouchAssistMapping
        {
            ToggleSource = XInputBindingSource.DPadUp,
            DefaultTouched = true
        };
        mapping.IndexTouch = new XInputTouchAssistMapping
        {
            ToggleSource = XInputBindingSource.DPadLeft,
            DefaultTouched = false
        };

        ControllerInputState idle = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(0, 0, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            new ControllerInputState(),
            new HashSet<XInputBindingSource>());
        ControllerInputState held = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(
                (ushort)(XInputControllerMapper.DPadUp | XInputControllerMapper.DPadLeft),
                0, 0, 0, 0, 0, 0),
            mapping,
            0.5f,
            idle,
            new HashSet<XInputBindingSource>());

        Assert.True(idle.HasExplicitTouchState);
        Assert.True(idle.JoystickTouch);
        Assert.False(idle.TriggerTouch);
        Assert.False(held.JoystickTouch);
        Assert.True(held.TriggerTouch);
    }

    [Fact]
    public void ActualBindingMakesTheSamePhysicalInputUnavailableToTouchAssist()
    {
        XInputConfiguration configuration = XInputConfiguration.CreateDefault();
        configuration.Left.ThumbTouch = new XInputTouchAssistMapping
        {
            ToggleSource = XInputBindingSource.A,
            DefaultTouched = true
        };
        HashSet<XInputBindingSource> occupied =
            XInputControllerMapper.GetOccupiedSources(configuration);

        ControllerInputState state = XInputControllerMapper.Map(
            new XInputGamepadSnapshot(XInputControllerMapper.A, 0, 0, 0, 0, 0, 0),
            configuration.Left,
            configuration.AnalogPressThreshold,
            new ControllerInputState(),
            occupied);

        Assert.False(state.PrimaryButton);
        Assert.False(state.PrimaryTouch);
        Assert.True(state.JoystickTouch);
    }
}
