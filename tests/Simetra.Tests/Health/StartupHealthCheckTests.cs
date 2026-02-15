using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simetra.HealthChecks;
using Simetra.Pipeline;

namespace Simetra.Tests.Health;

public class StartupHealthCheckTests
{
    private readonly Mock<ICorrelationService> _mockCorrelation = new();

    private StartupHealthCheck CreateSut() => new(_mockCorrelation.Object);

    [Fact]
    public async Task CheckHealth_CorrelationIdSet_ReturnsHealthy()
    {
        _mockCorrelation.Setup(c => c.CurrentCorrelationId).Returns("abc-123");
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_CorrelationIdEmpty_ReturnsUnhealthy()
    {
        _mockCorrelation.Setup(c => c.CurrentCorrelationId).Returns(string.Empty);
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_CorrelationIdNull_ReturnsUnhealthy()
    {
        _mockCorrelation.Setup(c => c.CurrentCorrelationId).Returns((string)null!);
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
