using TrackSwap.Runtime;

namespace TrackSwap.Runtime.Tests;

public sealed class DriverControlClientTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.01)]
    [InlineData(-33.5, -0.335)]
    [InlineData(1000.0, 10.0)]
    public void ConvertsConfigurationCentimetresToOpenVrMetres(
        double centimetres,
        double expectedMetres)
    {
        Assert.Equal(expectedMetres, DriverControlClient.ToOpenVrMetres(centimetres), 12);
    }
}
