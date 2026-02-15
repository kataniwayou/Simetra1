using FluentAssertions;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Moq;
using Simetra.Configuration;
using Simetra.Models;
using Simetra.Services;

namespace Simetra.Tests.Extraction;

public class SnmpExtractorTests
{
    private readonly SnmpExtractorService _sut;

    public SnmpExtractorTests()
    {
        var logger = Mock.Of<ILogger<SnmpExtractorService>>();
        _sut = new SnmpExtractorService(logger);
    }

    private static PollDefinitionDto MakeDefinition(params OidEntryDto[] oids)
        => new("test_metric", MetricType.Gauge, oids.ToList().AsReadOnly(), 30, MetricPollSource.Module);

    // -------------------------------------------------------
    // Metric role: all numeric SNMP types
    // -------------------------------------------------------

    [Fact]
    public void Extract_Integer32_MetricRole_ProducesLongMetricValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["value"].Should().Be(42L);
    }

    [Fact]
    public void Extract_Counter32_MetricRole_ProducesLongMetricValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.2.0"), new Counter32(3000000000))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.2.0", "bytes_in", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["bytes_in"].Should().Be(3000000000L);
    }

    [Fact]
    public void Extract_Counter64_MetricRole_ProducesLongMetricValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.3.0"), new Counter64(9876543210))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.3.0", "bytes_total", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["bytes_total"].Should().Be(9876543210L);
    }

    [Fact]
    public void Extract_Gauge32_MetricRole_ProducesLongMetricValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.4.0"), new Gauge32(85))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.4.0", "cpu", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["cpu"].Should().Be(85L);
    }

    [Fact]
    public void Extract_TimeTicks_MetricRole_ProducesLongMetricValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.5.0"), new TimeTicks(123456789))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.5.0", "uptime", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["uptime"].Should().Be(123456789L);
    }

    // -------------------------------------------------------
    // Label role: string SNMP types
    // -------------------------------------------------------

    [Fact]
    public void Extract_OctetString_LabelRole_ProducesStringLabelValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.6.0"), new OctetString("ge0/1"))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.6.0", "interface_name", OidRole.Label, null));

        var result = _sut.Extract(varbinds, definition);

        result.Labels["interface_name"].Should().Be("ge0/1");
    }

    [Fact]
    public void Extract_IP_LabelRole_ProducesStringLabelValue()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.7.0"), new IP("10.0.1.1"))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.7.0", "device_ip", OidRole.Label, null));

        var result = _sut.Extract(varbinds, definition);

        result.Labels["device_ip"].Should().Be("10.0.1.1");
    }

    // -------------------------------------------------------
    // EnumMap semantics
    // -------------------------------------------------------

    [Fact]
    public void Extract_LabelWithEnumMap_MapsIntegerToString()
    {
        var enumMap = new Dictionary<int, string> { { 1, "up" }, { 2, "down" } }.AsReadOnly();
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.8.0"), new Integer32(1))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.8.0", "status", OidRole.Label, enumMap));

        var result = _sut.Extract(varbinds, definition);

        result.Labels["status"].Should().Be("up");
    }

    [Fact]
    public void Extract_LabelWithEnumMap_UnknownValue_FallsBackToIntString()
    {
        var enumMap = new Dictionary<int, string> { { 1, "up" }, { 2, "down" } }.AsReadOnly();
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.8.0"), new Integer32(99))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.8.0", "status", OidRole.Label, enumMap));

        var result = _sut.Extract(varbinds, definition);

        result.Labels["status"].Should().Be("99");
    }

    [Fact]
    public void Extract_MetricWithEnumMap_PreservesRawInteger_StoresEnumAsMetadata()
    {
        var enumMap = new Dictionary<int, string> { { 1, "active" }, { 2, "standby" } }.AsReadOnly();
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.9.0"), new Integer32(2))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.9.0", "state", OidRole.Metric, enumMap));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics["state"].Should().Be(2L);
        result.EnumMapMetadata.Should().ContainKey("state");
        result.EnumMapMetadata["state"].Should().ContainKey(1).And.ContainKey(2);
        result.EnumMapMetadata["state"][1].Should().Be("active");
        result.EnumMapMetadata["state"][2].Should().Be("standby");
    }

    // -------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------

    [Fact]
    public void Extract_UnmatchedVarbind_SilentlySkipped()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.99.0"), new Integer32(1))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics.Should().BeEmpty();
        result.Labels.Should().BeEmpty();
    }

    [Fact]
    public void Extract_NonNumericDataForMetricRole_SkippedWithoutException()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.10.0"), new OctetString("not_a_number"))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.10.0", "value", OidRole.Metric, null));

        var act = () => _sut.Extract(varbinds, definition);

        act.Should().NotThrow();
        var result = act();
        result.Metrics.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MultipleVarbinds_ProducesMetricsAndLabels()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42)),
            new(new ObjectIdentifier("1.3.6.1.2.1.2.0"), new OctetString("ge0/1"))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null),
            new OidEntryDto("1.3.6.1.2.1.2.0", "interface_name", OidRole.Label, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics.Should().HaveCount(1);
        result.Metrics["value"].Should().Be(42L);
        result.Labels.Should().HaveCount(1);
        result.Labels["interface_name"].Should().Be("ge0/1");
    }

    [Fact]
    public void Extract_SetsDefinitionOnResult()
    {
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier("1.3.6.1.2.1.1.0"), new Integer32(42))
        };
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Definition.Should().BeSameAs(definition);
    }

    [Fact]
    public void Extract_EmptyVarbindList_ReturnsEmptyResult()
    {
        var varbinds = new List<Variable>();
        var definition = MakeDefinition(
            new OidEntryDto("1.3.6.1.2.1.1.0", "value", OidRole.Metric, null));

        var result = _sut.Extract(varbinds, definition);

        result.Metrics.Should().BeEmpty();
        result.Labels.Should().BeEmpty();
        result.EnumMapMetadata.Should().BeEmpty();
    }
}
