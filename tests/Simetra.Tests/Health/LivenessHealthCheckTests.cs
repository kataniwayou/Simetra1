using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Simetra.Configuration;
using Simetra.HealthChecks;
using Simetra.Pipeline;

namespace Simetra.Tests.Health;

public class LivenessHealthCheckTests
{
    private static IOptions<LivenessOptions> MakeLivenessOptions(double graceMultiplier = 3.0)
    {
        return Options.Create(new LivenessOptions { GraceMultiplier = graceMultiplier });
    }

    [Fact]
    public async Task CheckHealth_AllFreshStamps_ReturnsHealthy()
    {
        var liveness = new LivenessVectorService();
        liveness.Stamp("heartbeat");

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15);

        var sut = new LivenessHealthCheck(
            liveness,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_StaleStamp_ReturnsUnhealthy()
    {
        var mockLiveness = new Mock<ILivenessVectorService>();
        mockLiveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>
            {
                ["heartbeat"] = DateTimeOffset.UtcNow.AddSeconds(-120)
            }.AsReadOnly());

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15); // threshold = 15 * 3.0 = 45s, age = 120s -> stale

        var sut = new LivenessHealthCheck(
            mockLiveness.Object,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_UnhealthyIncludesStaleData()
    {
        var mockLiveness = new Mock<ILivenessVectorService>();
        mockLiveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>
            {
                ["heartbeat"] = DateTimeOffset.UtcNow.AddSeconds(-120)
            }.AsReadOnly());

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15);

        var sut = new LivenessHealthCheck(
            mockLiveness.Object,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("heartbeat");
    }

    [Fact]
    public async Task CheckHealth_UnknownJobKey_Skipped()
    {
        var mockLiveness = new Mock<ILivenessVectorService>();
        mockLiveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>
            {
                ["unknown-job"] = DateTimeOffset.UtcNow.AddSeconds(-120)
            }.AsReadOnly());

        var intervals = new JobIntervalRegistry();
        // "unknown-job" is NOT registered in intervals

        var sut = new LivenessHealthCheck(
            mockLiveness.Object,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_NoStamps_ReturnsHealthy()
    {
        var mockLiveness = new Mock<ILivenessVectorService>();
        mockLiveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>().AsReadOnly());

        var intervals = new JobIntervalRegistry();

        var sut = new LivenessHealthCheck(
            mockLiveness.Object,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_MixedFreshAndStale_ReturnsUnhealthy()
    {
        var mockLiveness = new Mock<ILivenessVectorService>();
        mockLiveness.Setup(l => l.GetAllStamps()).Returns(
            new Dictionary<string, DateTimeOffset>
            {
                ["heartbeat"] = DateTimeOffset.UtcNow, // fresh
                ["correlation"] = DateTimeOffset.UtcNow.AddSeconds(-120) // stale
            }.AsReadOnly());

        var intervals = new JobIntervalRegistry();
        intervals.Register("heartbeat", 15);   // threshold = 45s, age ~0s -> fresh
        intervals.Register("correlation", 15);  // threshold = 45s, age 120s -> stale

        var sut = new LivenessHealthCheck(
            mockLiveness.Object,
            intervals,
            MakeLivenessOptions(),
            Mock.Of<ILogger<LivenessHealthCheck>>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("correlation");
        result.Data.Should().NotContainKey("heartbeat");
    }
}
