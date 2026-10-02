using TrackSwap.Protocol;
using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class XInputInputServiceTests
{
    [Fact]
    public void DisabledServiceReportsOnlyNeutralMonitorState()
    {
        using var service = new XInputInputService(
            Array.Empty<RouteConfiguration>(),
            new XInputConfiguration());

        XInputRuntimeStatus status = service.GetStatus();

        Assert.False(status.Enabled);
        Assert.False(status.Connected);
        Assert.False(status.PhysicalInput.Connected);
        Assert.Equal((ushort)0, status.PhysicalInput.Buttons);
        Assert.Equal(0.0f, status.PhysicalInput.LeftTrigger);
        Assert.Equal(0.0f, status.PhysicalInput.RightTrigger);
        Assert.False(status.LeftInput.PrimaryButton);
        Assert.False(status.RightInput.PrimaryButton);
    }
}
