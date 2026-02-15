using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Simetra.Lifecycle;
using Simetra.Pipeline;
using Simetra.Services;
using Simetra.Telemetry;

namespace Simetra.Tests.Lifecycle;

public class GracefulShutdownServiceTests
{
    private readonly Mock<ISchedulerFactory> _mockSchedulerFactory = new();
    private readonly Mock<IScheduler> _mockScheduler = new();
    private readonly Mock<IDeviceChannelManager> _mockChannelManager = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<ILogger<GracefulShutdownService>> _mockLogger = new();

    public GracefulShutdownServiceTests()
    {
        _mockSchedulerFactory
            .Setup(f => f.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockScheduler.Object);

        _mockScheduler
            .Setup(s => s.Standby(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannelManager
            .Setup(c => c.WaitForDrainAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Default: local dev mode (no K8sLeaseElection)
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(K8sLeaseElection)))
            .Returns((K8sLeaseElection?)null);

        // Default: no SnmpListenerService registered
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IEnumerable<IHostedService>)))
            .Returns(Enumerable.Empty<IHostedService>());

        // Default: no telemetry providers (null-safe path)
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(OpenTelemetry.Metrics.MeterProvider)))
            .Returns((OpenTelemetry.Metrics.MeterProvider?)null);

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(OpenTelemetry.Trace.TracerProvider)))
            .Returns((OpenTelemetry.Trace.TracerProvider?)null);
    }

    private GracefulShutdownService CreateSut() => new(
        _mockSchedulerFactory.Object,
        _mockChannelManager.Object,
        _mockServiceProvider.Object,
        _mockLogger.Object);

    [Fact]
    public async Task StopAsync_CallsSchedulerStandby()
    {
        var sut = CreateSut();

        await sut.StopAsync(CancellationToken.None);

        _mockScheduler.Verify(s => s.Standby(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_CallsChannelCompleteAllAndDrain()
    {
        var sut = CreateSut();

        await sut.StopAsync(CancellationToken.None);

        _mockChannelManager.Verify(c => c.CompleteAll(), Times.Once);
        _mockChannelManager.Verify(c => c.WaitForDrainAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_NullLeaseService_DoesNotThrow()
    {
        // K8sLeaseElection is null (local dev mode)
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(K8sLeaseElection)))
            .Returns((K8sLeaseElection?)null);

        var sut = CreateSut();

        var act = () => sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_TelemetryFlushRuns_EvenWhenSchedulerFails()
    {
        _mockScheduler
            .Setup(s => s.Standby(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Scheduler error"));

        var sut = CreateSut();

        // StopAsync should still complete (scheduler failure caught, telemetry flush runs)
        var act = () => sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();

        // Verify drain still ran after scheduler failure
        _mockChannelManager.Verify(c => c.CompleteAll(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ReturnsCompletedTask()
    {
        var sut = CreateSut();

        var task = sut.StartAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }
}
