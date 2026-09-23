using TrackSwap.Protocol;

namespace TrackSwap.Protocol.Tests;

public sealed class OscControllerAddressesTests
{
    [Fact]
    public void UsesOculusTouchFaceButtonNamesForEachHand()
    {
        OscControllerAddresses left = OscControllerAddresses.ForHand(ControllerHand.Left);
        OscControllerAddresses right = OscControllerAddresses.ForHand(ControllerHand.Right);

        Assert.Equal("/trackswap/left/button/x", left.PrimaryButton);
        Assert.Equal("/trackswap/left/button/y", left.SecondaryButton);
        Assert.Equal("/trackswap/right/button/a", right.PrimaryButton);
        Assert.Equal("/trackswap/right/button/b", right.SecondaryButton);
        Assert.Equal("/trackswap/left/trigger/value", left.TriggerValue);
        Assert.Equal("/trackswap/left/grip/value", left.GripValue);
    }

    [Fact]
    public void ReceiverIsRequiredOnlyByEnabledOscControllerRoutes()
    {
        var route = new RouteConfiguration
        {
            Enabled = true,
            Mode = RouteMode.VirtualController,
            ControlInputSource = ControlInputSource.Osc
        };

        Assert.True(OscConfiguration.IsRequiredForRoutes(new[] { route }));

        route.ControlInputSource = ControlInputSource.XInput;
        Assert.False(OscConfiguration.IsRequiredForRoutes(new[] { route }));

        route.ControlInputSource = ControlInputSource.Osc;
        route.Enabled = false;
        Assert.False(OscConfiguration.IsRequiredForRoutes(new[] { route }));
    }
}
