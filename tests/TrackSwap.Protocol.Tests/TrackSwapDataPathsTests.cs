using TrackSwap.Protocol;

namespace TrackSwap.Protocol.Tests;

[Collection("TrackSwap data path environment")]
public sealed class TrackSwapDataPathsTests
{
    [Fact]
    public void ActiveDataDirectoryHonorsExplicitOverride()
    {
        string variableName = "TRACKSWAP_DATA_DIRECTORY";
        string? previous = Environment.GetEnvironmentVariable(variableName);
        string expected = Path.Combine(Path.GetTempPath(), "TrackSwap-path-test", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(variableName, expected);

            Assert.Equal(Path.GetFullPath(expected), TrackSwapDataPaths.ActiveDataDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    public void PathsEqualIgnoresCaseAndTrailingSeparator()
    {
        string path = Path.Combine(Path.GetTempPath(), "TrackSwap-Data");

        Assert.True(TrackSwapDataPaths.PathsEqual(path, path.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.False(TrackSwapDataPaths.PathsEqual(path, path + "-other"));
    }

    [Fact]
    public void DeviceHistoryAloneDoesNotDisplaceLegacyConfiguration()
    {
        string root = Path.Combine(Path.GetTempPath(), "TrackSwap-path-test", Guid.NewGuid().ToString("N"));
        string preferred = Path.Combine(root, "package", "UserData");
        string legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(preferred);
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(preferred, "device-history.json"), "[]");
            File.WriteAllText(Path.Combine(legacy, "runtime-config.json"), "{}");

            Assert.Equal(legacy, TrackSwapDataPaths.ResolveAutomaticDataDirectory(preferred, legacy));

            File.WriteAllText(Path.Combine(preferred, "runtime-config.json"), "{}");
            Assert.Equal(preferred, TrackSwapDataPaths.ResolveAutomaticDataDirectory(preferred, legacy));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

[CollectionDefinition("TrackSwap data path environment", DisableParallelization = true)]
public sealed class TrackSwapDataPathEnvironmentCollection
{
}
