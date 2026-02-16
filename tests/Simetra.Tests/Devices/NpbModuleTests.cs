using FluentAssertions;
using Simetra.Configuration;
using Simetra.Devices;

namespace Simetra.Tests.Devices;

public class NpbModuleTests
{
    private readonly NpbModule _sut = new();

    private const string Npb2ePrefix = "1.3.6.1.4.1.47477.100.4";

    // --- Identity Tests ---

    [Fact]
    public void DeviceType_IsNpb()
    {
        _sut.DeviceType.Should().Be("NPB");
    }

    // --- TrapDefinitions Tests ---

    [Fact]
    public void TrapDefinitions_HasTwoDefinitions()
    {
        _sut.TrapDefinitions.Should().HaveCount(2);
    }

    [Fact]
    public void TrapDefinitions_ContainsPortLinkUp()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "port_link_up");
    }

    [Fact]
    public void TrapDefinitions_ContainsPortLinkDown()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "port_link_down");
    }

    [Fact]
    public void TrapDefinitions_PortLinkUp_Has6Varbinds()
    {
        var def = _sut.TrapDefinitions.First(d => d.MetricName == "port_link_up");
        def.Oids.Should().HaveCount(6);
    }

    [Fact]
    public void TrapDefinitions_PortLinkDown_Has5Varbinds()
    {
        var def = _sut.TrapDefinitions.First(d => d.MetricName == "port_link_down");
        def.Oids.Should().HaveCount(5);
    }

    [Fact]
    public void TrapDefinitions_AllHaveSourceModule()
    {
        _sut.TrapDefinitions.Should().AllSatisfy(d =>
            d.Source.Should().Be(MetricPollSource.Module));
    }

    [Fact]
    public void TrapDefinitions_AllHaveMetricTypeGauge()
    {
        _sut.TrapDefinitions.Should().AllSatisfy(d =>
            d.MetricType.Should().Be(MetricType.Gauge));
    }

    [Fact]
    public void TrapDefinitions_AllHaveIntervalSecondsZero()
    {
        _sut.TrapDefinitions.Should().AllSatisfy(d =>
            d.IntervalSeconds.Should().Be(0));
    }

    // --- StatePollDefinitions Tests ---

    [Fact]
    public void StatePollDefinitions_HasThreeDefinitions()
    {
        _sut.StatePollDefinitions.Should().HaveCount(3);
    }

    [Fact]
    public void StatePollDefinitions_ContainsRxPackets()
    {
        _sut.StatePollDefinitions
            .Should().Contain(d => d.MetricName == "port_rx_packets");
    }

    [Fact]
    public void StatePollDefinitions_ContainsTxPackets()
    {
        _sut.StatePollDefinitions
            .Should().Contain(d => d.MetricName == "port_tx_packets");
    }

    [Fact]
    public void StatePollDefinitions_ContainsLinkStatus()
    {
        _sut.StatePollDefinitions
            .Should().Contain(d => d.MetricName == "port_link_status");
    }

    [Fact]
    public void StatePollDefinitions_AllHaveSourceModule()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.Source.Should().Be(MetricPollSource.Module));
    }

    [Fact]
    public void StatePollDefinitions_RxPacketsAndTxPackets_AreCounterType()
    {
        var counterPolls = _sut.StatePollDefinitions
            .Where(d => d.MetricName is "port_rx_packets" or "port_tx_packets");

        counterPolls.Should().AllSatisfy(d =>
            d.MetricType.Should().Be(MetricType.Counter));
    }

    [Fact]
    public void StatePollDefinitions_LinkStatus_IsGaugeType()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "port_link_status");
        def.MetricType.Should().Be(MetricType.Gauge);
    }

    [Fact]
    public void StatePollDefinitions_AllHaveIntervalSeconds30()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.IntervalSeconds.Should().Be(30));
    }

    // --- EnumMap Tests ---

    [Fact]
    public void StatePollDefinitions_LinkStatus_HasEnumMap()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "port_link_status");
        def.Oids.First(o => o.Role == OidRole.Metric).EnumMap.Should().NotBeNull();
    }

    [Fact]
    public void StatePollDefinitions_LinkStatus_EnumMap_HasFiveEntries()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "port_link_status");
        def.Oids.First(o => o.Role == OidRole.Metric).EnumMap.Should().HaveCount(5);
    }

    [Fact]
    public void StatePollDefinitions_LinkStatus_EnumMap_MapsCorrectValues()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "port_link_status");
        var enumMap = def.Oids.First(o => o.Role == OidRole.Metric).EnumMap!;

        enumMap.Should().ContainKey(-1).WhoseValue.Should().Be("unknown");
        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("down");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("up");
        enumMap.Should().ContainKey(2).WhoseValue.Should().Be("receiveDown");
        enumMap.Should().ContainKey(3).WhoseValue.Should().Be("forcedDown");
    }

    // --- OID Correctness Tests ---

    [Fact]
    public void TrapDefinitions_PortLinkUp_VarbindOids_AllStartWithNpb2ePrefix()
    {
        var def = _sut.TrapDefinitions.First(d => d.MetricName == "port_link_up");
        def.Oids.Should().AllSatisfy(oid =>
            oid.Oid.Should().StartWith(Npb2ePrefix));
    }

    [Fact]
    public void TrapDefinitions_PortLinkDown_VarbindOids_AllStartWithNpb2ePrefix()
    {
        var def = _sut.TrapDefinitions.First(d => d.MetricName == "port_link_down");
        def.Oids.Should().AllSatisfy(oid =>
            oid.Oid.Should().StartWith(Npb2ePrefix));
    }

    [Fact]
    public void StatePollDefinitions_AllOids_StartWithNpb2ePrefix()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(Npb2ePrefix)));
    }

    // --- Trap Notification OID Constants Tests ---

    [Fact]
    public void PortLinkUpTrapOid_IsCorrect()
    {
        NpbModule.PortLinkUpTrapOid.Should().Be("1.3.6.1.4.1.47477.100.4.10.2.101");
    }

    [Fact]
    public void PortLinkDownTrapOid_IsCorrect()
    {
        NpbModule.PortLinkDownTrapOid.Should().Be("1.3.6.1.4.1.47477.100.4.10.2.102");
    }

    // --- Non-EnumMap OIDs have null EnumMap ---

    [Fact]
    public void TrapDefinitions_AllOids_HaveNullEnumMap()
    {
        _sut.TrapDefinitions.SelectMany(d => d.Oids)
            .Should().AllSatisfy(oid => oid.EnumMap.Should().BeNull());
    }

    [Fact]
    public void StatePollDefinitions_NonLinkStatusOids_HaveNullEnumMap()
    {
        var nonLinkStatusPolls = _sut.StatePollDefinitions
            .Where(d => d.MetricName != "port_link_status");

        nonLinkStatusPolls.SelectMany(d => d.Oids)
            .Should().AllSatisfy(oid => oid.EnumMap.Should().BeNull());
    }
}
