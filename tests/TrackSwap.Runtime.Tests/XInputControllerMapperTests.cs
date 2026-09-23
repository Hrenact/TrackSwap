using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class XInputControllerMapperTests
{
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
        Assert.True(left.TriggerClick);
        Assert.InRange(left.GripValue, 0.50f, 0.51f);
        Assert.True(left.GripClick);
        Assert.Equal(1.0f, right.TriggerValue);
        Assert.True(right.TriggerClick);
        Assert.Equal(1.0f, right.GripValue);
        Assert.True(right.GripClick);
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
}
