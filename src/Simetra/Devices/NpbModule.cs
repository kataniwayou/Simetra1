using Simetra.Configuration;
using Simetra.Models;

namespace Simetra.Devices;

/// <summary>
/// NPB-2E reference device module for a standard SNMP device with NOTIFICATION-TYPE traps
/// and table-based port statistics. Defines two trap definitions (portLinkUp, portLinkDown),
/// three module-source state polls (RxPackets, TxPackets, LinkStatus), and an EnumMap for
/// the LinkStatusType textual convention. All OIDs are derived from the NPB MIB hierarchy
/// rooted at enterprises(1) > cgs(47477) > npb(100) > npb-2e(4).
/// </summary>
public sealed class NpbModule : IDeviceModule
{
    // --- OID Constants (single source of truth, derived from MIB hierarchy) ---

    /// <summary>
    /// Base OID prefix for the NPB-2E device: enterprises.cgs(47477).npb(100).npb-2e(4).
    /// </summary>
    private const string Npb2ePrefix = "1.3.6.1.4.1.47477.100.4";

    // Trap notification OIDs (public -- needed for trap matching by notification OID)

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portLinkUp (notifications.101).
    /// </summary>
    public const string PortLinkUpTrapOid = Npb2ePrefix + ".10.2.101";

    /// <summary>
    /// SNMP NOTIFICATION-TYPE OID for portLinkDown (notifications.102).
    /// </summary>
    public const string PortLinkDownTrapOid = Npb2ePrefix + ".10.2.102";

    // Trap variable OIDs (varbinds carried in trap PDUs)
    private const string ModuleOid   = Npb2ePrefix + ".10.1.1"; // variables.1
    private const string SeverityOid = Npb2ePrefix + ".10.1.2"; // variables.2
    private const string TypeOid     = Npb2ePrefix + ".10.1.3"; // variables.3
    private const string MessageOid  = Npb2ePrefix + ".10.1.4"; // variables.4

    // Port entry OIDs (portsPortEntry columns)
    private const string PortEntryPrefix          = Npb2ePrefix + ".2.1.4.1";
    private const string PortLogicalPortNumberOid = PortEntryPrefix + ".1"; // portsPortEntry.1
    private const string PortLinkStatusOid        = PortEntryPrefix + ".3"; // portsPortEntry.3
    private const string PortSpeedOid             = PortEntryPrefix + ".4"; // portsPortEntry.4

    // Port statistics summary OIDs (portStatisticsSummaryPortEntry columns)
    private const string SummaryEntryPrefix = Npb2ePrefix + ".2.2.5.1.1";
    private const string RxPacketsOid       = SummaryEntryPrefix + ".5"; // portStatisticsSummaryPortEntry.5
    private const string TxPacketsOid       = SummaryEntryPrefix + ".6"; // portStatisticsSummaryPortEntry.6

    // --- EnumMap: LinkStatusType ---

    /// <summary>
    /// Maps LinkStatusType integer values to human-readable names per NPB-PORTS.mib
    /// TEXTUAL-CONVENTION: {unknown(-1), down(0), up(1), receiveDown(2), forcedDown(3)}.
    /// Stored as metadata for Grafana value mappings; the raw integer is the metric value.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> LinkStatusEnumMap =
        new Dictionary<int, string>
        {
            { -1, "unknown" },
            {  0, "down" },
            {  1, "up" },
            {  2, "receiveDown" },
            {  3, "forcedDown" }
        }.AsReadOnly();

    // --- IDeviceModule Implementation ---

    /// <inheritdoc />
    public string DeviceType => "NPB";

    /// <inheritdoc />
    public string DeviceName => "npb-2e-01";

    /// <inheritdoc />
    public string IpAddress => "10.0.10.1";

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new List<PollDefinitionDto>
    {
        // portLinkUp trap (NPB-02): 6 varbinds
        new PollDefinitionDto(
            MetricName: "port_link_up",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null),
                new OidEntryDto(PortSpeedOid, "port_speed", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module),

        // portLinkDown trap (NPB-03): 5 varbinds
        new PollDefinitionDto(
            MetricName: "port_link_down",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
                new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
                new OidEntryDto(TypeOid, "type", OidRole.Label, null),
                new OidEntryDto(MessageOid, "message", OidRole.Label, null),
                new OidEntryDto(PortLogicalPortNumberOid, "port_number", OidRole.Label, null)
            }.AsReadOnly(),
            IntervalSeconds: 0,
            Source: MetricPollSource.Module)
    }.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } = new List<PollDefinitionDto>
    {
        // RxPackets poll (NPB-06): Module-source, Counter
        new PollDefinitionDto(
            MetricName: "port_rx_packets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(RxPacketsOid, "port_rx_packets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // TxPackets poll (NPB-07): Module-source, Counter
        new PollDefinitionDto(
            MetricName: "port_tx_packets",
            MetricType: MetricType.Counter,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(TxPacketsOid, "port_tx_packets", OidRole.Metric, null)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module),

        // LinkStatus poll (NPB-08): Module-source, Gauge, with EnumMap
        new PollDefinitionDto(
            MetricName: "port_link_status",
            MetricType: MetricType.Gauge,
            Oids: new List<OidEntryDto>
            {
                new OidEntryDto(PortLinkStatusOid, "port_link_status", OidRole.Metric, LinkStatusEnumMap)
            }.AsReadOnly(),
            IntervalSeconds: 30,
            Source: MetricPollSource.Module)
    }.AsReadOnly();
}
