using System.Collections.ObjectModel;
using FluentAssertions;
using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Tests.Models;

public class PollDefinitionDtoTests
{
    private static MetricPollOptions MakeOptions(
        string metricName = "test_metric",
        MetricType metricType = MetricType.Gauge,
        int intervalSeconds = 30,
        MetricPollSource source = MetricPollSource.Configuration,
        List<OidEntryOptions>? oids = null)
    {
        return new MetricPollOptions
        {
            MetricName = metricName,
            MetricType = metricType,
            IntervalSeconds = intervalSeconds,
            Source = source,
            Oids = oids ?? []
        };
    }

    [Fact]
    public void FromOptions_PreservesMetricNameAndMetricType()
    {
        var options = MakeOptions(metricName: "cpu_usage", metricType: MetricType.Counter);

        var dto = PollDefinitionDto.FromOptions(options);

        dto.MetricName.Should().Be("cpu_usage");
        dto.MetricType.Should().Be(MetricType.Counter);
    }

    [Fact]
    public void FromOptions_ConvertsOidEntriesToDto()
    {
        var oidEntries = new List<OidEntryOptions>
        {
            new()
            {
                Oid = "1.3.6.1.2.1.1.0",
                PropertyName = "cpu",
                Role = OidRole.Metric,
                EnumMap = null
            },
            new()
            {
                Oid = "1.3.6.1.2.1.2.0",
                PropertyName = "interface_name",
                Role = OidRole.Label,
                EnumMap = null
            }
        };
        var options = MakeOptions(oids: oidEntries);

        var dto = PollDefinitionDto.FromOptions(options);

        dto.Oids.Should().HaveCount(2);
        dto.Oids[0].Oid.Should().Be("1.3.6.1.2.1.1.0");
        dto.Oids[0].PropertyName.Should().Be("cpu");
        dto.Oids[0].Role.Should().Be(OidRole.Metric);
        dto.Oids[1].Oid.Should().Be("1.3.6.1.2.1.2.0");
        dto.Oids[1].PropertyName.Should().Be("interface_name");
        dto.Oids[1].Role.Should().Be(OidRole.Label);
    }

    [Fact]
    public void FromOptions_CreatesDefensiveEnumMapCopy()
    {
        var originalMap = new Dictionary<int, string> { { 1, "up" }, { 2, "down" } };
        var oidEntries = new List<OidEntryOptions>
        {
            new()
            {
                Oid = "1.3.6.1.2.1.1.0",
                PropertyName = "status",
                Role = OidRole.Label,
                EnumMap = originalMap
            }
        };
        var options = MakeOptions(oids: oidEntries);

        var dto = PollDefinitionDto.FromOptions(options);

        // Modify original after conversion
        originalMap[3] = "testing";

        dto.Oids[0].EnumMap.Should().HaveCount(2, "defensive copy should not reflect post-conversion changes");
        dto.Oids[0].EnumMap.Should().NotContainKey(3);
    }

    [Fact]
    public void FromOptions_PreservesSourceField()
    {
        var configOptions = MakeOptions(source: MetricPollSource.Configuration);
        var moduleOptions = MakeOptions(source: MetricPollSource.Module);

        var configDto = PollDefinitionDto.FromOptions(configOptions);
        var moduleDto = PollDefinitionDto.FromOptions(moduleOptions);

        configDto.Source.Should().Be(MetricPollSource.Configuration);
        moduleDto.Source.Should().Be(MetricPollSource.Module);
    }

    [Fact]
    public void FromOptions_OidsListIsReadOnly()
    {
        var oidEntries = new List<OidEntryOptions>
        {
            new()
            {
                Oid = "1.3.6.1.2.1.1.0",
                PropertyName = "value",
                Role = OidRole.Metric,
                EnumMap = null
            }
        };
        var options = MakeOptions(oids: oidEntries);

        var dto = PollDefinitionDto.FromOptions(options);

        // The underlying type is ReadOnlyCollection which implements IList but throws on mutation
        dto.Oids.Should().BeAssignableTo<ReadOnlyCollection<OidEntryDto>>();
    }
}
