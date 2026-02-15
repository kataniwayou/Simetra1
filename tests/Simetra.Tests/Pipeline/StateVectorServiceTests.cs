using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class StateVectorServiceTests
{
    private readonly StateVectorService _sut;

    public StateVectorServiceTests()
    {
        _sut = new StateVectorService(Mock.Of<ILogger<StateVectorService>>());
    }

    private static ExtractionResult MakeResult(string metricName, MetricPollSource source)
    {
        var definition = new PollDefinitionDto(
            metricName,
            MetricType.Gauge,
            Array.Empty<OidEntryDto>().ToList().AsReadOnly(),
            30,
            source);

        return new ExtractionResult { Definition = definition };
    }

    [Fact]
    public void Update_CreatesNewEntry()
    {
        var result = MakeResult("metric1", MetricPollSource.Module);

        _sut.Update("dev1", "metric1", result, "corr-1");

        var entry = _sut.GetEntry("dev1", "metric1");
        entry.Should().NotBeNull();
        entry!.Result.Should().BeSameAs(result);
        entry.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Update_OverwritesExistingEntry()
    {
        var result1 = MakeResult("metric1", MetricPollSource.Module);
        var result2 = MakeResult("metric1", MetricPollSource.Module);

        _sut.Update("dev1", "metric1", result1, "corr-1");
        _sut.Update("dev1", "metric1", result2, "corr-2");

        var entry = _sut.GetEntry("dev1", "metric1");
        entry.Should().NotBeNull();
        entry!.Result.Should().BeSameAs(result2);
        entry.CorrelationId.Should().Be("corr-2");
    }

    [Fact]
    public void GetEntry_MissingKey_ReturnsNull()
    {
        var entry = _sut.GetEntry("nonexistent", "missing");

        entry.Should().BeNull();
    }

    [Fact]
    public void GetAllEntries_ReturnsSnapshot()
    {
        var result1 = MakeResult("metric1", MetricPollSource.Module);
        var result2 = MakeResult("metric2", MetricPollSource.Module);

        _sut.Update("dev1", "metric1", result1, "corr-1");
        _sut.Update("dev2", "metric2", result2, "corr-2");

        var all = _sut.GetAllEntries();

        all.Should().HaveCount(2);
        all.Should().ContainKey("dev1:metric1");
        all.Should().ContainKey("dev2:metric2");
    }

    [Fact]
    public void Update_TimestampIsRecent()
    {
        var result = MakeResult("metric1", MetricPollSource.Module);

        _sut.Update("dev1", "metric1", result, "corr-1");

        var entry = _sut.GetEntry("dev1", "metric1");
        entry.Should().NotBeNull();
        entry!.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}
