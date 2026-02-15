using FluentAssertions;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Moq;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Pipeline;

namespace Simetra.Tests.Pipeline;

public class TrapFilterTests
{
    private readonly TrapFilter _sut;

    public TrapFilterTests()
    {
        _sut = new TrapFilter(Mock.Of<ILogger<TrapFilter>>());
    }

    private static PollDefinitionDto MakeDefinition(string metricName, params string[] oids)
    {
        var entries = oids
            .Select(o => new OidEntryDto(o, metricName, OidRole.Metric, null))
            .ToList()
            .AsReadOnly();

        return new PollDefinitionDto(metricName, MetricType.Gauge, entries, 30, MetricPollSource.Configuration);
    }

    private static DeviceInfo MakeDevice(string name, params PollDefinitionDto[] defs)
        => new(name, "10.0.0.1", "router", defs.ToList().AsReadOnly());

    [Fact]
    public void Match_WhenVarbindOidMatchesDefinition_ReturnsDefinition()
    {
        var definition = MakeDefinition("cpu", "1.3.6.1.2.1.1.0");
        var device = MakeDevice("router-1", definition);
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };

        var result = _sut.Match(varbinds, device);

        result.Should().BeSameAs(definition);
    }

    [Fact]
    public void Match_WhenNoOidMatches_ReturnsNull()
    {
        var definition = MakeDefinition("cpu", "1.3.6.1.2.1.1.0");
        var device = MakeDevice("router-1", definition);
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.99.0"), new Integer32(1))
        };

        var result = _sut.Match(varbinds, device);

        result.Should().BeNull();
    }

    [Fact]
    public void Match_WithMultipleDefinitions_ReturnsFirstMatch()
    {
        var def1 = MakeDefinition("cpu", "1.3.6.1.2.1.1.0");
        var def2 = MakeDefinition("memory", "1.3.6.1.2.1.2.0");
        var device = MakeDevice("router-1", def1, def2);
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.2.0"), new Integer32(80)),
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };

        var result = _sut.Match(varbinds, device);

        // First definition (cpu) is checked first and matches via "1.3.6.1.2.1.1.0"
        result.Should().BeSameAs(def1);
    }

    [Fact]
    public void Match_WithMultipleVarbinds_MatchesAny()
    {
        var definition = MakeDefinition("cpu", "1.3.6.1.2.1.1.0");
        var device = MakeDevice("router-1", definition);
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.99.0"), new Integer32(0)),
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };

        var result = _sut.Match(varbinds, device);

        result.Should().BeSameAs(definition);
    }

    [Fact]
    public void Match_EmptyVarbinds_ReturnsNull()
    {
        var definition = MakeDefinition("cpu", "1.3.6.1.2.1.1.0");
        var device = MakeDevice("router-1", definition);
        var varbinds = new List<Variable>();

        var result = _sut.Match(varbinds, device);

        result.Should().BeNull();
    }
}
