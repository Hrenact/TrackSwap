using TrackSwap.Protocol;

namespace TrackSwap.Runtime.Tests;

public sealed class RuntimeLifecycleMonitorTests
{
    [Theory]
    [InlineData(RuntimeLifecycleMode.FollowTrackSwap, true, false, true)]
    [InlineData(RuntimeLifecycleMode.FollowTrackSwap, false, true, false)]
    [InlineData(RuntimeLifecycleMode.FollowTrackSwap, false, false, false)]
    [InlineData(RuntimeLifecycleMode.FollowSteamVr, true, false, true)]
    [InlineData(RuntimeLifecycleMode.FollowSteamVr, false, true, true)]
    [InlineData(RuntimeLifecycleMode.FollowSteamVr, false, false, false)]
    public void AppliesLifecycleOwnershipRules(
        RuntimeLifecycleMode mode,
        bool ownerAlive,
        bool steamVrRunning,
        bool expected)
    {
        Assert.Equal(
            expected,
            RuntimeLifecycleMonitor.ShouldRemainRunning(mode, ownerAlive, steamVrRunning));
    }
}
