using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using Simetra.HealthChecks;
using Simetra.Pipeline;

namespace Simetra.Tests.Health;

public class ReadinessHealthCheckTests
{
    private readonly Mock<IDeviceChannelManager> _mockChannels = new();
    private readonly Mock<ISchedulerFactory> _mockSchedulerFactory = new();
    private readonly Mock<IScheduler> _mockScheduler = new();

    public ReadinessHealthCheckTests()
    {
        _mockSchedulerFactory
            .Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockScheduler.Object);
    }

    private ReadinessHealthCheck CreateSut() => new(_mockChannels.Object, _mockSchedulerFactory.Object);

    private void SetupHealthyScheduler()
    {
        _mockScheduler.Setup(s => s.IsStarted).Returns(true);
        _mockScheduler.Setup(s => s.IsShutdown).Returns(false);
    }

    private void SetupHealthyChannels()
    {
        _mockChannels.Setup(c => c.DeviceNames)
            .Returns(new List<string> { "device-1" }.AsReadOnly());
    }

    [Fact]
    public async Task CheckHealth_ChannelsExistAndSchedulerRunning_ReturnsHealthy()
    {
        SetupHealthyChannels();
        SetupHealthyScheduler();
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_NoChannels_ReturnsUnhealthy()
    {
        _mockChannels.Setup(c => c.DeviceNames)
            .Returns(new List<string>().AsReadOnly());
        SetupHealthyScheduler();
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_SchedulerNotStarted_ReturnsUnhealthy()
    {
        SetupHealthyChannels();
        _mockScheduler.Setup(s => s.IsStarted).Returns(false);
        _mockScheduler.Setup(s => s.IsShutdown).Returns(false);
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_SchedulerShutdown_ReturnsUnhealthy()
    {
        SetupHealthyChannels();
        _mockScheduler.Setup(s => s.IsStarted).Returns(true);
        _mockScheduler.Setup(s => s.IsShutdown).Returns(true);
        var sut = CreateSut();

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
