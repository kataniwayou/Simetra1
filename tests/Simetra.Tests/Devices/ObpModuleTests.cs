using FluentAssertions;
using Simetra.Configuration;
using Simetra.Devices;

namespace Simetra.Tests.Devices;

public class ObpModuleTests
{
    private readonly ObpModule _sut = new();

    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    // --- Identity Tests ---

    [Fact]
    public void DeviceType_IsObp()
    {
        _sut.DeviceType.Should().Be("OBP");
    }

    [Fact]
    public void DeviceName_IsObp01()
    {
        _sut.DeviceName.Should().Be("obp-01");
    }

    [Fact]
    public void IpAddress_Is10_0_20_1()
    {
        _sut.IpAddress.Should().Be("10.0.20.1");
    }

    // --- TrapDefinitions Tests ---

    [Fact]
    public void TrapDefinitions_HasFiveDefinitions()
    {
        // 3 per-link traps + 2 NMU traps
        _sut.TrapDefinitions.Should().HaveCount(5);
    }

    [Fact]
    public void TrapDefinitions_AllHaveExactlyOneOid()
    {
        // OBP traps are OBJECT-TYPE, not NOTIFICATION-TYPE -- single OID per trap
        _sut.TrapDefinitions.Should().AllSatisfy(d =>
            d.Oids.Should().HaveCount(1));
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

    [Fact]
    public void TrapDefinitions_ContainsWorkModeChange()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "work_mode_change");
    }

    [Fact]
    public void TrapDefinitions_ContainsStateChange()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "state_change");
    }

    [Fact]
    public void TrapDefinitions_ContainsPowerAlarmBypass2Changed()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "power_alarm_bypass2_changed");
    }

    [Fact]
    public void TrapDefinitions_ContainsSystemStartup()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "system_startup");
    }

    [Fact]
    public void TrapDefinitions_ContainsCardStatusChanged()
    {
        _sut.TrapDefinitions
            .Should().Contain(d => d.MetricName == "card_status_changed");
    }

    // --- StatePollDefinitions Tests ---

    [Fact]
    public void StatePollDefinitions_HasEightDefinitions()
    {
        // 6 per-link + 2 NMU power states
        _sut.StatePollDefinitions.Should().HaveCount(8);
    }

    [Fact]
    public void StatePollDefinitions_AllHaveSourceModule()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.Source.Should().Be(MetricPollSource.Module));
    }

    [Fact]
    public void StatePollDefinitions_AllHaveMetricTypeGauge()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.MetricType.Should().Be(MetricType.Gauge));
    }

    [Fact]
    public void StatePollDefinitions_AllHaveIntervalSeconds30()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.IntervalSeconds.Should().Be(30));
    }

    [Fact]
    public void StatePollDefinitions_AllHaveExactlyOneOid()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.Oids.Should().HaveCount(1));
    }

    [Fact]
    public void StatePollDefinitions_ContainsAllExpectedMetrics()
    {
        var expectedMetrics = new[]
        {
            "link_state", "link_channel", "work_mode",
            "active_heart_status", "passive_heart_status", "power_alarm_status",
            "power1_state", "power2_state"
        };

        var actualMetrics = _sut.StatePollDefinitions.Select(d => d.MetricName);
        actualMetrics.Should().BeEquivalentTo(expectedMetrics);
    }

    // --- EnumMap Tests ---

    [Fact]
    public void StatePollDefinitions_LinkState_EnumMap_HasCorrectValues()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "link_state");
        var enumMap = def.Oids.First().EnumMap!;

        enumMap.Should().HaveCount(2);
        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("off");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("on");
    }

    [Fact]
    public void StatePollDefinitions_LinkChannel_EnumMap_HasCorrectValues()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "link_channel");
        var enumMap = def.Oids.First().EnumMap!;

        enumMap.Should().HaveCount(2);
        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("bypass");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("primary");
    }

    [Fact]
    public void StatePollDefinitions_WorkMode_EnumMap_HasCorrectValues()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "work_mode");
        var enumMap = def.Oids.First().EnumMap!;

        enumMap.Should().HaveCount(2);
        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("manualMode");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("autoMode");
    }

    [Fact]
    public void StatePollDefinitions_ActiveHeartStatus_EnumMap_HasFourEntries()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "active_heart_status");
        def.Oids.First().EnumMap.Should().HaveCount(4);
    }

    [Fact]
    public void StatePollDefinitions_ActiveHeartStatus_EnumMap_IncludesNa()
    {
        // MIB-authoritative: alarm(0), normal(1), off(2), na(3)
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "active_heart_status");
        var enumMap = def.Oids.First().EnumMap!;

        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("alarm");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("normal");
        enumMap.Should().ContainKey(2).WhoseValue.Should().Be("off");
        enumMap.Should().ContainKey(3).WhoseValue.Should().Be("na");
    }

    [Fact]
    public void StatePollDefinitions_PassiveHeartStatus_EnumMap_HasFourEntries()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "passive_heart_status");
        def.Oids.First().EnumMap.Should().HaveCount(4);
    }

    [Fact]
    public void StatePollDefinitions_PowerAlarmStatus_EnumMap_HasFourEntries()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "power_alarm_status");
        def.Oids.First().EnumMap.Should().HaveCount(4);
    }

    [Fact]
    public void StatePollDefinitions_PowerAlarmStatus_EnumMap_IncludesNormalAndNa()
    {
        // MIB-authoritative: off(0), alarm(1), normal(2), na(3)
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "power_alarm_status");
        var enumMap = def.Oids.First().EnumMap!;

        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("off");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("alarm");
        enumMap.Should().ContainKey(2).WhoseValue.Should().Be("normal");
        enumMap.Should().ContainKey(3).WhoseValue.Should().Be("na");
    }

    [Fact]
    public void TrapDefinitions_PowerAlarmBypass2Changed_EnumMap_HasFiveEntries()
    {
        var def = _sut.TrapDefinitions.First(d => d.MetricName == "power_alarm_bypass2_changed");
        def.Oids.First().EnumMap.Should().HaveCount(5);
    }

    // --- OID Correctness Tests ---

    [Fact]
    public void TrapDefinitions_PerLinkTraps_AllOidsStartWithTrapPrefix()
    {
        var perLinkTraps = _sut.TrapDefinitions
            .Where(d => d.MetricName is "work_mode_change" or "state_change" or "power_alarm_bypass2_changed");

        perLinkTraps.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(BypassPrefix + ".1.3.50")));
    }

    [Fact]
    public void TrapDefinitions_NmuTraps_AllOidsStartWithNmuTrapPrefix()
    {
        var nmuTraps = _sut.TrapDefinitions
            .Where(d => d.MetricName is "system_startup" or "card_status_changed");

        nmuTraps.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(BypassPrefix + ".60.50")));
    }

    [Fact]
    public void StatePollDefinitions_PerLinkPolls_AllOidsStartWithLinkOBPPrefix()
    {
        var perLinkPolls = _sut.StatePollDefinitions
            .Where(d => d.MetricName is not "power1_state" and not "power2_state");

        perLinkPolls.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(BypassPrefix + ".1.3")));
    }

    [Fact]
    public void StatePollDefinitions_NmuPolls_AllOidsStartWithNmuPrefix()
    {
        var nmuPolls = _sut.StatePollDefinitions
            .Where(d => d.MetricName is "power1_state" or "power2_state");

        nmuPolls.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(BypassPrefix + ".60")));
    }

    // --- Trap OID Constant Tests ---

    [Fact]
    public void WorkModeChangeTrapOid_IsCorrect()
    {
        ObpModule.WorkModeChangeTrapOid.Should().Be("1.3.6.1.4.1.47477.10.21.1.3.50.1");
    }

    [Fact]
    public void StateChangeTrapOid_IsCorrect()
    {
        ObpModule.StateChangeTrapOid.Should().Be("1.3.6.1.4.1.47477.10.21.1.3.50.2");
    }

    [Fact]
    public void PowerAlarmBypass2ChangedTrapOid_IsCorrect()
    {
        ObpModule.PowerAlarmBypass2ChangedTrapOid.Should().Be("1.3.6.1.4.1.47477.10.21.1.3.50.19");
    }

    [Fact]
    public void SystemStartupTrapOid_IsCorrect()
    {
        ObpModule.SystemStartupTrapOid.Should().Be("1.3.6.1.4.1.47477.10.21.60.50.1");
    }

    [Fact]
    public void CardStatusChangedTrapOid_IsCorrect()
    {
        ObpModule.CardStatusChangedTrapOid.Should().Be("1.3.6.1.4.1.47477.10.21.60.50.2");
    }

    // --- Non-Standard Pattern Tests ---

    [Fact]
    public void TrapDefinitions_NmuTraps_HaveNullEnumMap()
    {
        var nmuTraps = _sut.TrapDefinitions
            .Where(d => d.MetricName is "system_startup" or "card_status_changed");

        nmuTraps.SelectMany(d => d.Oids)
            .Should().AllSatisfy(oid => oid.EnumMap.Should().BeNull());
    }

    [Fact]
    public void StatePollDefinitions_AllOids_HaveMetricRole()
    {
        _sut.StatePollDefinitions.SelectMany(d => d.Oids)
            .Should().AllSatisfy(oid => oid.Role.Should().Be(OidRole.Metric));
    }

    [Fact]
    public void TrapDefinitions_AllOids_HaveMetricRole()
    {
        // All OBP trap OIDs are Metric role (OBJECT-TYPE carries value, not Label)
        _sut.TrapDefinitions.SelectMany(d => d.Oids)
            .Should().AllSatisfy(oid => oid.Role.Should().Be(OidRole.Metric));
    }
}
