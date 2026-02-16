using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// OBP bypass reference device module for a non-standard SNMP device. Unlike the NPB module
/// (which uses NOTIFICATION-TYPE traps with multiple varbinds and table-based OIDs), the OBP
/// device uses OBJECT-TYPE definitions under a trap subtree where each trap OID is both the
/// identifier and the value carrier. The entire OID tree is duplicated per-link (link1 through
/// link32) rather than using SNMP table indexing; link number 1 is hardcoded for the test device.
/// All OIDs derive from the BYPASS-CGS.mib hierarchy rooted at
/// enterprises(1) > cgs(47477) > EBP-1U2U4U(10) > bypass(21).
/// </summary>
public sealed class ObpModule : IDeviceModule
{
    // --- OID Constants (derived from BYPASS-CGS.mib) ---

    /// <summary>
    /// Base OID prefix for the OBP bypass device:
    /// enterprises.cgs(47477).EBP-1U2U4U(10).bypass(21)
    /// </summary>
    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    // Per-link OID prefix for link 1 (link number hardcoded for test device)
    // linkN = bypass.N, linkNOBP = linkN.3
    private const string LinkOBPPrefix = BypassPrefix + ".1.3";  // link1OBP

    // linkNOBPTrap prefix
    private const string TrapPrefix = LinkOBPPrefix + ".50";     // link1OBPTrap

    // NMU (device-level) prefix
    private const string NmuPrefix = BypassPrefix + ".60";       // nmu
    private const string NmuTrapPrefix = NmuPrefix + ".50";      // nmuTrap

    // --- Per-link poll OIDs ---
    private const string StateOid              = LinkOBPPrefix + ".1";   // linkN_State
    private const string WorkModeOid           = LinkOBPPrefix + ".3";   // linkN_WorkMode
    private const string ChannelOid            = LinkOBPPrefix + ".4";   // linkN_Channel

    // R1Power and R2Power are polled via Configuration source in appsettings.json (OBP-06, OBP-07).
    // Included here as documentation of the OID hierarchy.
    private const string R1PowerOid            = LinkOBPPrefix + ".5";   // linkN_R1Power (Source=Configuration)
    private const string R2PowerOid            = LinkOBPPrefix + ".6";   // linkN_R2Power (Source=Configuration)

    private const string ActiveHeartStatusOid  = LinkOBPPrefix + ".24";  // linkN_ActiveHeartStatus
    private const string PassiveHeartStatusOid = LinkOBPPrefix + ".25";  // linkN_PassiveHeartStatus
    private const string PowerAlarmStatusOid   = LinkOBPPrefix + ".26";  // linkN_PowerAlarmStatus

    // --- Per-link trap OIDs (public -- needed for trap matching) ---

    /// <summary>OBJECT-TYPE trap OID for linkN_WorkModeChange (linkNOBPTrap.1).</summary>
    public const string WorkModeChangeTrapOid = TrapPrefix + ".1";

    /// <summary>OBJECT-TYPE trap OID for linkN_StateChange (linkNOBPTrap.2).</summary>
    public const string StateChangeTrapOid = TrapPrefix + ".2";

    /// <summary>OBJECT-TYPE trap OID for linkN_PowerAlarmBypass2Changed (linkNOBPTrap.19).</summary>
    public const string PowerAlarmBypass2ChangedTrapOid = TrapPrefix + ".19";

    // --- NMU poll OIDs ---
    private const string Power1StateOid = NmuPrefix + ".11";   // power1State
    private const string Power2StateOid = NmuPrefix + ".12";   // power2State

    // --- NMU trap OIDs (public -- needed for trap matching) ---

    /// <summary>OBJECT-TYPE trap OID for systemStartup (nmuTrap.1).</summary>
    public const string SystemStartupTrapOid = NmuTrapPrefix + ".1";

    /// <summary>OBJECT-TYPE trap OID for cardStatusChanged (nmuTrap.2).</summary>
    public const string CardStatusChangedTrapOid = NmuTrapPrefix + ".2";

    // --- EnumMaps ---

    private static readonly IReadOnlyDictionary<int, string> ChannelEnumMap =
        new Dictionary<int, string>
        {
            { 0, "bypass" },
            { 1, "primary" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> WorkModeEnumMap =
        new Dictionary<int, string>
        {
            { 0, "manualMode" },
            { 1, "autoMode" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> HeartStatusEnumMap =
        new Dictionary<int, string>
        {
            { 0, "alarm" },
            { 1, "normal" },
            { 2, "off" },
            { 3, "na" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> PowerAlarmStatusEnumMap =
        new Dictionary<int, string>
        {
            { 0, "off" },
            { 1, "alarm" },
            { 2, "normal" },
            { 3, "na" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> LinkStateEnumMap =
        new Dictionary<int, string>
        {
            { 0, "off" },
            { 1, "on" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> PowerStateEnumMap =
        new Dictionary<int, string>
        {
            { 0, "off" },
            { 1, "on" }
        }.AsReadOnly();

    private static readonly IReadOnlyDictionary<int, string> PowerAlarmBypass2ChangedEnumMap =
        new Dictionary<int, string>
        {
            { 0, "off" },
            { 1, "powerAlarmR1" },
            { 2, "powerAlarmR2" },
            { 3, "anyAlarmR1-R2" },
            { 4, "allAlarmR1-R2" }
        }.AsReadOnly();

    // --- IDeviceModule Implementation ---

    /// <inheritdoc />
    public string DeviceType => "OBP";

    /// <inheritdoc />
    public string DeviceName => "obp-01";

    /// <inheritdoc />
    public string IpAddress => "10.0.20.1";

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new List<PollDefinitionDto>
    {
        // OBP-03: link_WorkModeChange trap (linkNOBPTrap.1)
        new PollDefinitionDto(
            MetricName: "work_mode_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(WorkModeChangeTrapOid, "work_mode_change",
                    OidRole.Metric, WorkModeEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // OBP-02: link_StateChange trap (linkNOBPTrap.2)
        new PollDefinitionDto(
            MetricName: "state_change",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(StateChangeTrapOid, "state_change",
                    OidRole.Metric, ChannelEnumMap) // bypass(0), primary(1)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // OBP-04: link_PowerAlarmBypass2Changed trap (linkNOBPTrap.19)
        new PollDefinitionDto(
            MetricName: "power_alarm_bypass2_changed",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(PowerAlarmBypass2ChangedTrapOid,
                    "power_alarm_bypass2_changed",
                    OidRole.Metric, PowerAlarmBypass2ChangedEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // OBP-05: systemStartup NMU trap (nmuTrap.1)
        new PollDefinitionDto(
            MetricName: "system_startup",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(SystemStartupTrapOid, "system_startup",
                    OidRole.Metric, null) // DisplayString, no EnumMap
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // OBP-05: cardStatusChanged NMU trap (nmuTrap.2)
        new PollDefinitionDto(
            MetricName: "card_status_changed",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(CardStatusChangedTrapOid, "card_status_changed",
                    OidRole.Metric, null) // DisplayString, no EnumMap
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),
    }.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } = new List<PollDefinitionDto>
    {
        // OBP-08: link_State (linkNOBP.1)
        new PollDefinitionDto(
            MetricName: "link_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(StateOid, "link_state", OidRole.Metric, LinkStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-09: link_Channel (linkNOBP.4)
        new PollDefinitionDto(
            MetricName: "link_channel",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ChannelOid, "link_channel", OidRole.Metric, ChannelEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-10: link_WorkMode (linkNOBP.3)
        new PollDefinitionDto(
            MetricName: "work_mode",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(WorkModeOid, "work_mode", OidRole.Metric, WorkModeEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-11: link_ActiveHeartStatus (linkNOBP.24)
        new PollDefinitionDto(
            MetricName: "active_heart_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ActiveHeartStatusOid, "active_heart_status",
                    OidRole.Metric, HeartStatusEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-12: link_PassiveHeartStatus (linkNOBP.25)
        new PollDefinitionDto(
            MetricName: "passive_heart_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(PassiveHeartStatusOid, "passive_heart_status",
                    OidRole.Metric, HeartStatusEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-13: link_PowerAlarmStatus (linkNOBP.26)
        new PollDefinitionDto(
            MetricName: "power_alarm_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(PowerAlarmStatusOid, "power_alarm_status",
                    OidRole.Metric, PowerAlarmStatusEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-14: NMU power1State (nmu.11)
        new PollDefinitionDto(
            MetricName: "power1_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Power1StateOid, "power1_state",
                    OidRole.Metric, PowerStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // OBP-14: NMU power2State (nmu.12)
        new PollDefinitionDto(
            MetricName: "power2_state",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(Power2StateOid, "power2_state",
                    OidRole.Metric, PowerStateEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),
    }.AsReadOnly();
}
