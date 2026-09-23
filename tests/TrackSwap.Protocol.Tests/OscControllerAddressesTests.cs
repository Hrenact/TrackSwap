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
    }
}
