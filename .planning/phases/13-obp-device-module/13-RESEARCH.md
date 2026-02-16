# Phase 13: OBP Device Module - Research

**Researched:** 2026-02-16
**Domain:** Non-standard SNMP device module (per-link duplicated OIDs, OBJECT-TYPE traps)
**Confidence:** HIGH

## Summary

Phase 13 implements the ObpModule as the reference implementation for a non-standard SNMP device. Unlike NPB (which uses NOTIFICATION-TYPE traps and table-based OIDs), the OBP device uses OBJECT-TYPE definitions under a trap subtree and duplicates the entire OID tree per-link (link1 through link32) rather than using SNMP table indexing. The research focused on extracting exact OIDs from the BYPASS-CGS.mib file (35,032 lines), mapping every OBP requirement to verified OID paths, and identifying the architectural differences from the NPB pattern.

The OBP MIB hierarchy is: `enterprises(1.3.6.1.4.1) > cgs(47477) > EBP-1U2U4U(10) > bypass(21)`. Per-link subtrees branch as `bypass.N > linkN > linkN.3 (linkNOBP) > linkNOBP.50 (linkNOBPTrap)`. The NMU (device-level) subtree is at `bypass.60 > nmu`. The MIB defines links 1 through 32, all with identical internal structure -- only the link number in the OID path differs. The module will be parameterized by link number at compile time.

The most critical difference from NPB: OBP traps are defined as `OBJECT-TYPE` under the `linkNOBPTrap` subtree, NOT as `NOTIFICATION-TYPE`. This means each trap definition is a single OID carrying a typed value (INTEGER or DisplayString), not a notification OID with a list of varbind OBJECTS. For the Simetra pipeline, each OBP trap definition maps to a PollDefinitionDto with a single OidEntryDto (the trap OID itself carries the value), whereas NPB trap definitions have multiple varbind OIDs.

**Primary recommendation:** Follow the NpbModule pattern exactly for structure, but parameterize OIDs by link number using string interpolation on a configurable link number. Each trap definition has a single OID (the OBJECT-TYPE trap OID) with the value as the Metric role. Use EnumMaps on all INTEGER-typed OIDs for human-readable resolution.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 9 | net9.0 | Runtime | Project target framework |
| xUnit | (existing) | Unit tests | Project standard |
| FluentAssertions | 7.2.0 | Test assertions | Project standard |

### Supporting
No additional libraries needed. This phase is pure domain code using existing project infrastructure.

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hardcoded OID strings with link number interpolation | MIB parser library | Unnecessary complexity; OIDs are static and pattern is simple |
| Single link module | Multi-link module generating OIDs for all 32 links | Only one link is configured per device instance; the link number comes from config |

## Architecture Patterns

### Recommended Project Structure
```
src/Simetra/Devices/
    IDeviceModule.cs              # Existing interface
    SimetraModule.cs              # Existing reference
    NpbModule.cs                  # Existing (Phase 12)
    ObpModule.cs                  # NEW: OBP device module

tests/Simetra.Tests/Devices/
    NpbModuleTests.cs             # Existing reference for test pattern
    ObpModuleTests.cs             # NEW: Unit tests for ObpModule

src/Simetra/appsettings.json                       # MODIFY: Add OBP device config entry
src/Simetra/Extensions/ServiceCollectionExtensions.cs  # MODIFY: Register ObpModule at 3 touchpoints
```

### Pattern 1: IDeviceModule Implementation with Per-Link OID Parameterization
**What:** A sealed class implementing IDeviceModule where OID constants use a link number variable to construct the per-link OID path. Unlike NPB (which uses fixed OIDs), OBP builds OIDs from a pattern: `bypass.{linkNum}.3.{leafOid}`.
**When to use:** For devices with per-link duplicated OID structures (not tables).
**Key difference from NPB:** NPB OIDs are static constants. OBP OIDs depend on the link number. Use a private field for link number and build OIDs accordingly. Since link number is known at compile time (hardcoded for the test device), string constants with the link number embedded are acceptable. The planner can use `const string` fields with a commented link number, or build them from a prefix and link number.

**Example:**
```csharp
public sealed class ObpModule : IDeviceModule
{
    // OBP OID hierarchy:
    // enterprises.cgs(47477).EBP-1U2U4U(10).bypass(21)
    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    // Per-link OID: bypass.{linkNum}.3 = linkNOBP
    // For link 1: bypass.1.3
    private const int LinkNumber = 1;
    private const string LinkOBPPrefix = BypassPrefix + ".1.3"; // link1OBP

    // linkNOBP leaf OIDs
    private const string StateOid             = LinkOBPPrefix + ".1";   // link1_State
    private const string WorkModeOid          = LinkOBPPrefix + ".3";   // link1_WorkMode
    private const string ChannelOid           = LinkOBPPrefix + ".4";   // link1_Channel
    private const string R1PowerOid           = LinkOBPPrefix + ".5";   // link1_R1Power
    private const string R2PowerOid           = LinkOBPPrefix + ".6";   // link1_R2Power
    private const string ActiveHeartStatusOid = LinkOBPPrefix + ".24";  // link1_ActiveHeartStatus
    private const string PassiveHeartStatusOid = LinkOBPPrefix + ".25"; // link1_PassiveHeartStatus
    private const string PowerAlarmStatusOid  = LinkOBPPrefix + ".26";  // link1_PowerAlarmStatus

    // Trap OIDs: linkNOBP.50.{trapLeaf}
    private const string TrapPrefix = LinkOBPPrefix + ".50";
    // ...
}
```

### Pattern 2: OBJECT-TYPE Trap Definitions (vs NOTIFICATION-TYPE)
**What:** In NPB, traps use NOTIFICATION-TYPE with a notification OID and a list of OBJECTS (varbinds). In OBP, traps are OBJECT-TYPE definitions under the trap subtree. Each "trap" is a single OID that carries a value.
**When to use:** For vendor MIBs that define traps as OBJECT-TYPE instead of NOTIFICATION-TYPE.
**Key difference from NPB:**
- NPB trap PollDefinitionDto: multiple Oids (varbinds), notification OID is public const for matching
- OBP trap PollDefinitionDto: single Oid (the trap OID IS the value), the trap OID itself is used for matching

**Example:**
```csharp
// NPB pattern: NOTIFICATION-TYPE with varbinds
new PollDefinitionDto(
    MetricName: "port_link_up",
    MetricType: MetricType.Gauge,
    Oids: new List<OidEntryDto>
    {
        new OidEntryDto(ModuleOid, "module", OidRole.Label, null),
        new OidEntryDto(SeverityOid, "severity", OidRole.Label, null),
        // ... 6 varbinds total
    }.AsReadOnly(),
    IntervalSeconds: 0,
    Source: MetricPollSource.Module)

// OBP pattern: OBJECT-TYPE trap (single OID carries the value)
new PollDefinitionDto(
    MetricName: "state_change",
    MetricType: MetricType.Gauge,
    Oids: new List<OidEntryDto>
    {
        new OidEntryDto(StateChangeTrapOid, "state_change", OidRole.Metric,
            StateChangeEnumMap)  // INTEGER: bypass(0), primary(1)
    }.AsReadOnly(),
    IntervalSeconds: 0,
    Source: MetricPollSource.Module)
```

### Pattern 3: Configuration-Source Polls for Optical Power
**What:** R1Power and R2Power are Source=Configuration polls defined in appsettings.json, not in ObpModule. These are DisplayString values representing optical power readings.
**When to use:** For OBP-06 and OBP-07 (R1Power, R2Power are configuration polls per the requirement Source=Configuration).
**Key detail:** These are `DisplayString` SNMP type (not INTEGER), so they have no EnumMap and no MetricType=Counter. They are Gauge metrics with string-to-numeric extraction handled by the pipeline.

### Pattern 4: Registration at 3 Touchpoints (Following NPB Pattern)
**What:** ObpModule must be registered at 3 places, exactly as NpbModule was.
**Where:**
1. `ServiceCollectionExtensions.AddDeviceModules()` -- `services.AddSingleton<IDeviceModule, ObpModule>();`
2. `ServiceCollectionExtensions.AddQuartzJobs()` -- `var obpModule = new ObpModule();` added to `allModules` array
3. `appsettings.json` -- OBP device entry with Configuration-source MetricPolls (R1Power, R2Power)

### Anti-Patterns to Avoid
- **Don't generate OIDs for all 32 links.** Only one link is configured per ObpModule instance. The link number is a compile-time constant.
- **Don't model OBP traps as multi-varbind definitions.** OBP traps are OBJECT-TYPE (single OID with value), not NOTIFICATION-TYPE (notification OID + varbinds).
- **Don't put R1Power/R2Power in ObpModule.StatePollDefinitions.** Requirements OBP-06/OBP-07 specify Source=Configuration, which means they go in appsettings.json MetricPolls.
- **Don't confuse OBP trap "notification OID" with NPB pattern.** For OBP, the trap OID (e.g., `linkNOBPTrap.2` for StateChange) serves as both the identifier and the value carrier. The public const for trap matching should be this OID directly.
- **Don't omit `na(3)` from EnumMaps.** The MIB defines `na(3)` for ActiveHeartStatus, PassiveHeartStatus, and PowerAlarmStatus. Requirements show fewer values but the MIB is authoritative.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OID parameterization by link | Runtime OID builder class | `const string` with link number embedded | Link number is known at compile time; const strings are simpler and match NpbModule pattern |
| MIB parsing | Custom ASN.1 parser | Hardcoded OID map from MIB analysis (this document) | MIB is static, all OIDs are extracted and verified here |
| Immutable collections | Custom wrappers | `.AsReadOnly()` on `List<T>` | Standard BCL pattern used throughout codebase |
| EnumMap definitions | Runtime enum resolution | `Dictionary<int, string>.AsReadOnly()` | Static mapping, matches NpbModule pattern exactly |

## Common Pitfalls

### Pitfall 1: Wrong OID Hierarchy for OBP
**What goes wrong:** Confusing the OBP hierarchy with NPB. OBP uses `cgs(47477).EBP-1U2U4U(10).bypass(21)`, not `cgs(47477).npb(100)`.
**Why it happens:** Both are under the same enterprise (47477) but diverge immediately after.
**How to avoid:** The verified OBP chain is:
- `enterprises = 1.3.6.1.4.1`
- `cgs = enterprises.47477` = `1.3.6.1.4.1.47477`
- `EBP-1U2U4U = cgs.10` = `1.3.6.1.4.1.47477.10`
- `bypass = EBP-1U2U4U.21` = `1.3.6.1.4.1.47477.10.21`
- `linkN = bypass.N` (N=1..32) = `1.3.6.1.4.1.47477.10.21.N`
- `linkNOBP = linkN.3` = `1.3.6.1.4.1.47477.10.21.N.3`
- `linkNOBPTrap = linkNOBP.50` = `1.3.6.1.4.1.47477.10.21.N.3.50`
- `nmu = bypass.60` = `1.3.6.1.4.1.47477.10.21.60`
- `nmuTrap = nmu.50` = `1.3.6.1.4.1.47477.10.21.60.50`
**Warning signs:** OIDs that contain `.100.` (that is NPB, not OBP).

### Pitfall 2: Treating OBP Traps Like NPB Traps
**What goes wrong:** Creating multi-varbind trap definitions for OBP traps, expecting a notification OID + OBJECTS list.
**Why it happens:** The NPB pattern uses NOTIFICATION-TYPE with OBJECTS. OBP uses OBJECT-TYPE under a trap subtree.
**How to avoid:** Each OBP trap PollDefinitionDto has exactly ONE OidEntryDto. The trap OID (e.g., `linkNOBPTrap.1`) IS the notification OID AND the value carrier. Role should be `OidRole.Metric` with the appropriate EnumMap.
**Warning signs:** Trap definitions with multiple OIDs; looking for separate "notification OID" and "varbind OIDs".

### Pitfall 3: NMU Trap OID Comment Bug in MIB
**What goes wrong:** Using `bypass.20.50` as the nmuTrap OID.
**Why it happens:** Line 45 of the MIB has an incorrect comment: `-- 1.3.6.1.4.1.47477.10.21.20.50`. But the definition says `nmuTrap ::= { nmu 50 }` and `nmu ::= { bypass 60 }`, so nmuTrap = `bypass.60.50`.
**How to avoid:** Always derive OIDs from the `::=` definition, not from comments. The verified nmuTrap OID is `1.3.6.1.4.1.47477.10.21.60.50`.
**Warning signs:** nmuTrap OID containing `.20.50` instead of `.60.50`.

### Pitfall 4: Missing EnumMap Values from MIB
**What goes wrong:** Using only the values listed in the requirements, which omit some MIB-defined values.
**Why it happens:** Requirements list summary enum values but the MIB includes additional values like `na(3)`.
**How to avoid:** Use the MIB-authoritative enum values (documented below in the OID Reference). The MIB is the source of truth.
**Warning signs:** EnumMap missing `na(3)` for heart status or power alarm status.

### Pitfall 5: Forgetting to Update 3 Registration Touchpoints
**What goes wrong:** ObpModule registered in DI but missing from Quartz allModules or appsettings.json.
**Why it happens:** Same as NPB -- the 3 touchpoints are not DI-driven; they require manual updates.
**How to avoid:** Update all 3 files:
1. `ServiceCollectionExtensions.AddDeviceModules()` -- add `services.AddSingleton<IDeviceModule, ObpModule>();`
2. `ServiceCollectionExtensions.AddQuartzJobs()` -- add `new ObpModule()` to `allModules` array
3. `appsettings.json` -- add OBP device entry with Configuration-source polls

### Pitfall 6: Confusing Configuration vs Module Source for OBP Polls
**What goes wrong:** Putting R1Power/R2Power in Module-source StatePollDefinitions.
**Why it happens:** All OBP-06 through OBP-14 look similar but have different Source assignments.
**How to avoid:** Check each requirement carefully:
- OBP-06 (R1Power): **Source=Configuration** -> appsettings.json
- OBP-07 (R2Power): **Source=Configuration** -> appsettings.json
- OBP-08 through OBP-14: **Source=Module** -> ObpModule.StatePollDefinitions

## Code Examples

### Example 1: Complete OID Hierarchy (Verified from BYPASS-CGS.mib)

```
BYPASS-CGS.mib:
  cgs = { enterprises 47477 }              -> 1.3.6.1.4.1.47477
  EBP-1U2U4U = { cgs 10 }                  -> 1.3.6.1.4.1.47477.10
  bypass = { EBP-1U2U4U 21 }               -> 1.3.6.1.4.1.47477.10.21

Per-link (N=1..32):
  linkN = { bypass N }                      -> 1.3.6.1.4.1.47477.10.21.N
  linkNOBP = { linkN 3 }                    -> 1.3.6.1.4.1.47477.10.21.N.3
  linkNOBPTrap = { linkNOBP 50 }            -> 1.3.6.1.4.1.47477.10.21.N.3.50

NMU (device-level):
  nmu = { bypass 60 }                       -> 1.3.6.1.4.1.47477.10.21.60
  nmuTrap = { nmu 50 }                      -> 1.3.6.1.4.1.47477.10.21.60.50
```

### Example 2: link1OBP Leaf OIDs (Complete Map for Requirements)

```
linkNOBP leaf OIDs (base: 1.3.6.1.4.1.47477.10.21.N.3):
  .1  = linkN_State              INTEGER { off(0), on(1) }
  .2  = linkN_DeviceType         DisplayString
  .3  = linkN_WorkMode           INTEGER { manualMode(0), autoMode(1) }
  .4  = linkN_Channel            INTEGER { bypass(0), primary(1) }
  .5  = linkN_R1Power            DisplayString (optical power reading)
  .6  = linkN_R2Power            DisplayString (optical power reading)
  .7  = linkN_R1Wave             INTEGER { w1310nm(0), w1550nm(1), na(2) }
  .8  = linkN_R2Wave             INTEGER { w1310nm(0), w1550nm(1), na(2) }
  .9  = linkN_R1AlarmPower       DisplayString (threshold)
  .10 = linkN_R2AlarmPower       DisplayString (threshold)
  .11 = linkN_PowerAlarmBypass2  INTEGER { off(0), powerAlarmR1(1), powerAlarmR2(2), anyAlarmR1-R2(3), allAlarmR1-R2(4), na(5) }
  .12 = linkN_ReturnDelay        Integer32
  .13 = linkN_BackMode           INTEGER { autoNoBack(0), autoBack(1) }
  .14 = linkN_BackDelay          Integer32
  .15 = linkN_ActiveHeartSwitch  INTEGER { off(0), on(1) }
  .16 = linkN_ActiveSendInterval Integer32
  .17 = linkN_ActiveTimeOut      Integer32
  .18 = linkN_ActiveLossBypass   Integer32
  .19 = linkN_PingIpAddress      IpAddress
  .20 = linkN_PassiveHeartSwitch INTEGER { off(0), on(1) }
  .21 = linkN_PassiveTimeOut     Integer32
  .22 = linkN_PassiveLossBypass  Integer32
  .23 = linkN_SwitchProtect      INTEGER { off(0), on(1) }
  .24 = linkN_ActiveHeartStatus  INTEGER { alarm(0), normal(1), off(2), na(3) }
  .25 = linkN_PassiveHeartStatus INTEGER { alarm(0), normal(1), off(2), na(3) }
  .26 = linkN_PowerAlarmStatus   INTEGER { off(0), alarm(1), normal(2), na(3) }
  .27 = linkN_R3Wave             INTEGER { w1310nm(0), w1550nm(1), na(2) }
  .28 = linkN_R4Wave             INTEGER { w1310nm(0), w1550nm(1), na(2) }
  .29 = linkN_R3AlarmPower       DisplayString
  .30 = linkN_R4AlarmPower       DisplayString
  .35 = linkN_R3Power            DisplayString
  .36 = linkN_R4Power            DisplayString
  .67 = linkN_PowerAlarmBypass4  INTEGER { off(0)..na(7) }
  .68 = linkN_PowerAlarmBypass8  INTEGER { off(0)..na(11) }
```

### Example 3: linkNOBPTrap Leaf OIDs (Trap Definitions)

```
linkNOBPTrap leaf OIDs (base: 1.3.6.1.4.1.47477.10.21.N.3.50):
  .1  = linkN_WorkModeChange            INTEGER { manualMode(0), autoMode(1) }
  .2  = linkN_StateChange               INTEGER { bypass(0), primary(1) }
  .3  = linkN_R1WaveSet                 INTEGER { w1310nm(0), w1550nm(1) }
  .4  = linkN_R2WaveSet                 INTEGER { w1310nm(0), w1550nm(1) }
  .5  = linkN_R3WaveSet                 INTEGER { w1310nm(0), w1550nm(1) }
  .6  = linkN_R4WaveSet                 INTEGER { w1310nm(0), w1550nm(1) }
  .7  = linkN_R1AlarmSet                DisplayString
  .8  = linkN_R2AlarmSet                DisplayString
  .9  = linkN_R3AlarmSet                DisplayString
  .10 = linkN_R4AlarmSet                DisplayString
  .11 = linkN_1R1AlarmSet               DisplayString
  .12 = linkN_2R1AlarmSet               DisplayString
  .13 = linkN_1R2AlarmSet               DisplayString
  .14 = linkN_2R2AlarmSet               DisplayString
  .15 = linkN_1R3AlarmSet               DisplayString
  .16 = linkN_2R3AlarmSet               DisplayString
  .17 = linkN_1R4AlarmSet               DisplayString
  .18 = linkN_2R4AlarmSet               DisplayString
  .19 = linkN_PowerAlarmBypass2Changed  INTEGER { off(0), powerAlarmR1(1), powerAlarmR2(2), anyAlarmR1-R2(3), allAlarmR1-R2(4) }
  .20 = linkN_PowerAlarmBypass4Changed  INTEGER { off(0)..allAlarmR1-R4(6) }
  .21 = linkN_PowerAlarmBypass8Changed  INTEGER { off(0)..allAlarm1R1-2R4(10) }
  .22-.33 = linkN_powerAlarm*           DisplayString (individual power alarm notifications)
```

### Example 4: NMU OIDs (Device-Level)

```
NMU subtree (1.3.6.1.4.1.47477.10.21.60):
  .1  = deviceType           DisplayString
  .2  = ipAddress             IpAddress
  .3  = subnetMask            IpAddress
  .4  = gateWay               IpAddress
  .5  = macAddress             DisplayString
  .6  = tcpPort               Integer32
  .7  = startDelay            Integer32
  .8  = keyLock               INTEGER { lock(0), unlock(1) }
  .9  = buzzerSet             INTEGER { off(0), on(1) }
  .10 = deviceAddress         Integer32
  .11 = power1State           INTEGER { off(0), on(1) }         <-- OBP-14
  .12 = power2State           INTEGER { off(0), on(1) }         <-- OBP-14
  .13 = softwareVersion       DisplayString
  .14 = hardwareVersion       DisplayString
  .15 = serialNumber          DisplayString
  .16 = manufacturingdate     DisplayString

nmuTrap subtree (1.3.6.1.4.1.47477.10.21.60.50):
  .1  = systemStartup         DisplayString                     <-- OBP-05
  .2  = cardStatusChanged     DisplayString                     <-- OBP-05
```

### Example 5: ObpModule Skeleton

```csharp
public sealed class ObpModule : IDeviceModule
{
    // --- OID Constants (derived from BYPASS-CGS.mib) ---

    /// <summary>
    /// Base OID prefix for the OBP bypass device:
    /// enterprises.cgs(47477).EBP-1U2U4U(10).bypass(21)
    /// </summary>
    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    // Per-link OID prefix for link 1 (configurable link number)
    // linkN = bypass.N, linkNOBP = linkN.3
    private const string LinkOBPPrefix = BypassPrefix + ".1.3";  // link1OBP

    // linkNOBPTrap prefix
    private const string TrapPrefix = LinkOBPPrefix + ".50";     // link1OBPTrap

    // NMU (device-level) prefix
    private const string NmuPrefix = BypassPrefix + ".60";       // nmu
    private const string NmuTrapPrefix = NmuPrefix + ".50";      // nmuTrap

    // --- Per-link poll OIDs ---
    private const string StateOid             = LinkOBPPrefix + ".1";   // linkN_State
    private const string WorkModeOid          = LinkOBPPrefix + ".3";   // linkN_WorkMode
    private const string ChannelOid           = LinkOBPPrefix + ".4";   // linkN_Channel
    private const string R1PowerOid           = LinkOBPPrefix + ".5";   // linkN_R1Power
    private const string R2PowerOid           = LinkOBPPrefix + ".6";   // linkN_R2Power
    private const string ActiveHeartStatusOid = LinkOBPPrefix + ".24";  // linkN_ActiveHeartStatus
    private const string PassiveHeartStatusOid = LinkOBPPrefix + ".25"; // linkN_PassiveHeartStatus
    private const string PowerAlarmStatusOid  = LinkOBPPrefix + ".26";  // linkN_PowerAlarmStatus

    // --- Per-link trap OIDs (public -- needed for trap matching) ---
    public const string WorkModeChangeTrapOid = TrapPrefix + ".1";      // linkN_WorkModeChange
    public const string StateChangeTrapOid    = TrapPrefix + ".2";      // linkN_StateChange
    public const string PowerAlarmBypass2ChangedTrapOid = TrapPrefix + ".19"; // linkN_PowerAlarmBypass2Changed

    // --- NMU poll OIDs ---
    private const string Power1StateOid = NmuPrefix + ".11";   // power1State
    private const string Power2StateOid = NmuPrefix + ".12";   // power2State

    // --- NMU trap OIDs (public -- needed for trap matching) ---
    public const string SystemStartupTrapOid     = NmuTrapPrefix + ".1";  // systemStartup
    public const string CardStatusChangedTrapOid = NmuTrapPrefix + ".2";  // cardStatusChanged

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

    // --- IDeviceModule ---
    public string DeviceType => "OBP";
    public string DeviceName => "obp-01";
    public string IpAddress  => "10.0.20.1";

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
```

### Example 6: appsettings.json OBP Device Entry (Configuration-Source Polls)

```json
{
  "Name": "obp-01",
  "IpAddress": "10.0.20.1",
  "DeviceType": "OBP",
  "MetricPolls": [
    {
      "MetricName": "r1_power",
      "MetricType": "Gauge",
      "Oids": [
        {
          "Oid": "1.3.6.1.4.1.47477.10.21.1.3.5",
          "PropertyName": "r1_power",
          "Role": "Metric"
        }
      ],
      "IntervalSeconds": 30
    },
    {
      "MetricName": "r2_power",
      "MetricType": "Gauge",
      "Oids": [
        {
          "Oid": "1.3.6.1.4.1.47477.10.21.1.3.6",
          "PropertyName": "r2_power",
          "Role": "Metric"
        }
      ],
      "IntervalSeconds": 30
    }
  ]
}
```

### Example 7: Registration Touchpoints Update

```csharp
// Touchpoint 1: ServiceCollectionExtensions.AddDeviceModules()
public static IServiceCollection AddDeviceModules(this IServiceCollection services)
{
    services.AddSingleton<IDeviceModule, SimetraModule>();
    services.AddSingleton<IDeviceModule, NpbModule>();
    services.AddSingleton<IDeviceModule, ObpModule>();  // NEW
    return services;
}

// Touchpoint 2: ServiceCollectionExtensions.AddQuartzJobs() allModules array
var simetraModule = new SimetraModule();
var npbModule = new NpbModule();
var obpModule = new ObpModule();  // NEW
var allModules = new IDeviceModule[] { simetraModule, npbModule, obpModule };  // UPDATED
```

### Example 8: Test Pattern (Following NpbModuleTests)

```csharp
public class ObpModuleTests
{
    private readonly ObpModule _sut = new();

    private const string BypassPrefix = "1.3.6.1.4.1.47477.10.21";

    [Fact]
    public void DeviceType_IsObp()
    {
        _sut.DeviceType.Should().Be("OBP");
    }

    [Fact]
    public void TrapDefinitions_HasFiveDefinitions()
    {
        // 3 link traps + 2 NMU traps
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
    public void StatePollDefinitions_HasEightDefinitions()
    {
        // 6 per-link + 2 NMU power states
        _sut.StatePollDefinitions.Should().HaveCount(8);
    }

    [Fact]
    public void StatePollDefinitions_AllOids_StartWithBypassPrefix()
    {
        _sut.StatePollDefinitions.Should().AllSatisfy(d =>
            d.Oids.Should().AllSatisfy(oid =>
                oid.Oid.Should().StartWith(BypassPrefix)));
    }

    [Fact]
    public void StatePollDefinitions_LinkChannel_EnumMap_HasCorrectValues()
    {
        var def = _sut.StatePollDefinitions.First(d => d.MetricName == "link_channel");
        var enumMap = def.Oids.First().EnumMap!;
        enumMap.Should().ContainKey(0).WhoseValue.Should().Be("bypass");
        enumMap.Should().ContainKey(1).WhoseValue.Should().Be("primary");
    }
}
```

## OID Reference Table (Complete Requirement Mapping)

### Requirement-to-OID Mapping

| Req | Metric Name | OID (link 1) | SNMP Type | MetricType | Source | EnumMap |
|-----|-------------|-------------|-----------|------------|--------|---------|
| OBP-02 | `state_change` | `.1.3.50.2` (linkNOBPTrap.2) | INTEGER | Gauge | Module (Trap) | bypass(0), primary(1) |
| OBP-03 | `work_mode_change` | `.1.3.50.1` (linkNOBPTrap.1) | INTEGER | Gauge | Module (Trap) | manualMode(0), autoMode(1) |
| OBP-04 | `power_alarm_bypass2_changed` | `.1.3.50.19` (linkNOBPTrap.19) | INTEGER | Gauge | Module (Trap) | off(0)..allAlarmR1-R2(4) |
| OBP-05 | `system_startup` | `.60.50.1` (nmuTrap.1) | DisplayString | Gauge | Module (Trap) | None |
| OBP-05 | `card_status_changed` | `.60.50.2` (nmuTrap.2) | DisplayString | Gauge | Module (Trap) | None |
| OBP-06 | `r1_power` | `.1.3.5` (linkNOBP.5) | DisplayString | Gauge | Configuration | None |
| OBP-07 | `r2_power` | `.1.3.6` (linkNOBP.6) | DisplayString | Gauge | Configuration | None |
| OBP-08 | `link_state` | `.1.3.1` (linkNOBP.1) | INTEGER | Gauge | Module | off(0), on(1) |
| OBP-09 | `link_channel` | `.1.3.4` (linkNOBP.4) | INTEGER | Gauge | Module | bypass(0), primary(1) |
| OBP-10 | `work_mode` | `.1.3.3` (linkNOBP.3) | INTEGER | Gauge | Module | manualMode(0), autoMode(1) |
| OBP-11 | `active_heart_status` | `.1.3.24` (linkNOBP.24) | INTEGER | Gauge | Module | alarm(0), normal(1), off(2), na(3) |
| OBP-12 | `passive_heart_status` | `.1.3.25` (linkNOBP.25) | INTEGER | Gauge | Module | alarm(0), normal(1), off(2), na(3) |
| OBP-13 | `power_alarm_status` | `.1.3.26` (linkNOBP.26) | INTEGER | Gauge | Module | off(0), alarm(1), normal(2), na(3) |
| OBP-14 | `power1_state` | `.60.11` (nmu.11) | INTEGER | Gauge | Module | off(0), on(1) |
| OBP-14 | `power2_state` | `.60.12` (nmu.12) | INTEGER | Gauge | Module | off(0), on(1) |

All OID paths above are relative to the bypass prefix `1.3.6.1.4.1.47477.10.21`. Full OID for link 1 R1Power = `1.3.6.1.4.1.47477.10.21.1.3.5`.

### EnumMaps Required (OBP-15)

| EnumMap Name | Field(s) | Values |
|-------------|----------|--------|
| ChannelEnumMap | link_channel, state_change | { 0: "bypass", 1: "primary" } |
| WorkModeEnumMap | work_mode, work_mode_change | { 0: "manualMode", 1: "autoMode" } |
| HeartStatusEnumMap | active_heart_status, passive_heart_status | { 0: "alarm", 1: "normal", 2: "off", 3: "na" } |
| PowerAlarmStatusEnumMap | power_alarm_status | { 0: "off", 1: "alarm", 2: "normal", 3: "na" } |
| LinkStateEnumMap | link_state | { 0: "off", 1: "on" } |
| PowerStateEnumMap | power1_state, power2_state | { 0: "off", 1: "on" } |
| PowerAlarmBypass2ChangedEnumMap | power_alarm_bypass2_changed | { 0: "off", 1: "powerAlarmR1", 2: "powerAlarmR2", 3: "anyAlarmR1-R2", 4: "allAlarmR1-R2" } |

### Non-Standard MIB Pattern Summary (OBP-17)

| Aspect | NPB (Standard) | OBP (Non-Standard) |
|--------|----------------|---------------------|
| OID structure | Table-based (portsPortEntry, indexed by port number) | Per-link duplicated (link1OBP, link2OBP, ... link32OBP) |
| Trap type | NOTIFICATION-TYPE with OBJECTS list | OBJECT-TYPE under trap subtree |
| Trap varbinds | Multiple OIDs per trap (notification OID + varbind OIDs) | Single OID per trap (OID IS the value) |
| Trap OID role | Notification OID identifies trap; varbind OIDs carry data | Trap OID identifies AND carries data |
| Index/Link | Table index appended as instance suffix | Link number encoded in parent OID path |
| PollDefinitionDto OID count | Traps: 5-6 OIDs; Polls: 1 OID | Traps: 1 OID; Polls: 1 OID |

## Requirements-to-Implementation Mapping

| Requirement | Implementation Location | Key Details |
|-------------|------------------------|-------------|
| OBP-01 | `ObpModule.cs` | `sealed class ObpModule : IDeviceModule` with DeviceType "OBP" |
| OBP-02 | `ObpModule.TrapDefinitions` | StateChange trap: 1 OID (`linkNOBPTrap.2`), EnumMap bypass(0)/primary(1) |
| OBP-03 | `ObpModule.TrapDefinitions` | WorkModeChange trap: 1 OID (`linkNOBPTrap.1`), EnumMap manualMode(0)/autoMode(1) |
| OBP-04 | `ObpModule.TrapDefinitions` | PowerAlarmBypass2Changed trap: 1 OID (`linkNOBPTrap.19`), EnumMap with 5 values |
| OBP-05 | `ObpModule.TrapDefinitions` | 2 NMU traps: systemStartup (`nmuTrap.1`), cardStatusChanged (`nmuTrap.2`), both DisplayString |
| OBP-06 | `appsettings.json` | R1Power poll: Source=Configuration, metric `r1_power`, OID `linkNOBP.5`, DisplayString |
| OBP-07 | `appsettings.json` | R2Power poll: Source=Configuration, metric `r2_power`, OID `linkNOBP.6`, DisplayString |
| OBP-08 | `ObpModule.StatePollDefinitions` | link_state poll: OID `linkNOBP.1`, EnumMap off(0)/on(1) |
| OBP-09 | `ObpModule.StatePollDefinitions` | link_channel poll: OID `linkNOBP.4`, EnumMap bypass(0)/primary(1) |
| OBP-10 | `ObpModule.StatePollDefinitions` | work_mode poll: OID `linkNOBP.3`, EnumMap manualMode(0)/autoMode(1) |
| OBP-11 | `ObpModule.StatePollDefinitions` | active_heart_status poll: OID `linkNOBP.24`, EnumMap alarm(0)/normal(1)/off(2)/na(3) |
| OBP-12 | `ObpModule.StatePollDefinitions` | passive_heart_status poll: OID `linkNOBP.25`, EnumMap alarm(0)/normal(1)/off(2)/na(3) |
| OBP-13 | `ObpModule.StatePollDefinitions` | power_alarm_status poll: OID `linkNOBP.26`, EnumMap off(0)/alarm(1)/normal(2)/na(3) |
| OBP-14 | `ObpModule.StatePollDefinitions` | 2 NMU polls: power1_state (`nmu.11`), power2_state (`nmu.12`), EnumMap off(0)/on(1) |
| OBP-15 | `ObpModule.cs` EnumMap fields | 7 EnumMaps total (see EnumMaps table above) |
| OBP-16 | `ServiceCollectionExtensions.cs` + `appsettings.json` | Register at 3 touchpoints (DI, Quartz, config) |
| OBP-17 | `ObpModule.cs` OID constants | Per-link OID prefix with link number embedded; single-OID trap definitions |

## Registration Touchpoints

### Files to Modify
1. **`src/Simetra/Extensions/ServiceCollectionExtensions.cs`**
   - `AddDeviceModules()`: Add `services.AddSingleton<IDeviceModule, ObpModule>();`
   - `AddQuartzJobs()`: Add `var obpModule = new ObpModule();` and include in `allModules` array

2. **`src/Simetra/appsettings.json`**
   - Add OBP device to the `Devices` array with Configuration-source MetricPolls (r1_power, r2_power)

### Files to Create
1. **`src/Simetra/Devices/ObpModule.cs`** -- The ObpModule implementation
2. **`tests/Simetra.Tests/Devices/ObpModuleTests.cs`** -- Unit tests

## Design Decisions

### EnumMap Values: MIB vs Requirements Discrepancies

The requirements list simplified enum values for some fields, while the MIB defines additional values. Use the MIB-authoritative values:

| Field | Requirements Say | MIB Says | Decision |
|-------|-----------------|----------|----------|
| active_heart_status | alarm(0)/normal(1)/off(2) | alarm(0)/normal(1)/off(2)/na(3) | Use MIB: include na(3) |
| passive_heart_status | alarm(0)/normal(1)/off(2) | alarm(0)/normal(1)/off(2)/na(3) | Use MIB: include na(3) |
| power_alarm_status | off(0)/alarm(1) | off(0)/alarm(1)/normal(2)/na(3) | Use MIB: include normal(2)/na(3) |

The MIB is the authoritative source. Including additional values prevents runtime failures when the device returns an unmapped integer.

### Single-OID Trap Definitions

For OBP traps (OBJECT-TYPE, not NOTIFICATION-TYPE), each PollDefinitionDto in TrapDefinitions contains exactly ONE OidEntryDto. The OID serves as both the trap identifier (for matching) and the value carrier. The OidRole is Metric (not Label) because the single OID carries the metric value.

This differs from NPB where trap PollDefinitionDtos have multiple OIDs (the OBJECTS list from NOTIFICATION-TYPE). The NPB trap matching is done by notification OID; OBP trap matching is done by the OID in the single OidEntryDto.

### DeviceName and IpAddress

Following the pattern from NpbModule ("npb-2e-01", "10.0.10.1"), use:
- DeviceName: `"obp-01"`
- IpAddress: `"10.0.20.1"`
- DeviceType: `"OBP"`

### Link Number Encoding

The module hardcodes link number 1 for the test device. The OID prefix `BypassPrefix + ".1.3"` encodes link 1. For a different link number N, the prefix would be `BypassPrefix + ".N.3"`. The OBP-17 requirement about handling per-link structure is satisfied by this parameterization approach -- the module demonstrates the pattern, and future link configurations would adjust this constant.

### Shared EnumMaps Between Traps and Polls

Some enum value sets are shared between trap and poll definitions:
- `ChannelEnumMap` -- used by both `state_change` trap (OBP-02) and `link_channel` poll (OBP-09)
- `WorkModeEnumMap` -- used by both `work_mode_change` trap (OBP-03) and `work_mode` poll (OBP-10)

These are defined as `private static readonly` fields on ObpModule and referenced by both trap and poll definitions.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Only SimetraModule as IDeviceModule | NpbModule added (standard MIB) | Phase 12 | Established the pattern |
| N/A | ObpModule added (non-standard MIB) | Phase 13 | Demonstrates vendor MIB quirks handling |

## Open Questions

1. **Trap matching for OBJECT-TYPE traps**
   - What we know: The TrapFilter matches incoming trap notification OIDs against registered trap definitions. For NPB, the notification OID (from NOTIFICATION-TYPE) is matched. For OBP, traps are OBJECT-TYPE -- the "notification OID" would be the OID of the OBJECT-TYPE itself.
   - What's unclear: How the existing TrapFilter handles OBP-style traps where the OID is both the identifier and the value. The TrapFilter may need to match on the OID in the single OidEntryDto rather than a separate notification OID field.
   - Recommendation: The planner should verify TrapFilter behavior. Since OBP trap OIDs are public consts (like NPB's PortLinkUpTrapOid), the same matching mechanism should work if the trap PDU uses the OBJECT-TYPE OID as its notification OID. This is how SNMPv2 traps from non-standard devices typically behave.

2. **IntervalSeconds for OBP polls**
   - What we know: Not specified in requirements.
   - What's unclear: Preferred poll interval for OBP state metrics.
   - Recommendation: Use 30 seconds (consistent with NPB and existing config polls).

3. **PowerAlarmBypass2Changed vs PowerAlarmBypass4Changed vs PowerAlarmBypass8Changed**
   - What we know: OBP-04 specifically calls out PowerAlarmBypass2Changed (linkNOBPTrap.19). The MIB also defines PowerAlarmBypass4Changed (trap.20) and PowerAlarmBypass8Changed (trap.21).
   - What's unclear: Whether OBP-04 should include all three variants or only the Bypass2 variant.
   - Recommendation: Implement only OBP-04 as specified (PowerAlarmBypass2Changed only). The other variants can be added later if needed.

## Sources

### Primary (HIGH confidence)
- `V5.2.4/BYPASS-CGS.mib` -- Complete MIB file, 35,032 lines. All OIDs extracted and verified from ASN.1 definitions (not comments).
- `src/Simetra/Devices/IDeviceModule.cs` -- Interface contract
- `src/Simetra/Devices/NpbModule.cs` -- Reference implementation pattern (standard MIB)
- `src/Simetra/Devices/SimetraModule.cs` -- Minimal reference implementation
- `src/Simetra/Models/PollDefinitionDto.cs` -- DTO structure with MetricName, MetricType, Oids, IntervalSeconds, Source
- `src/Simetra/Models/OidEntryDto.cs` -- OID entry structure with Oid, PropertyName, Role, EnumMap
- `src/Simetra/Configuration/MetricType.cs` -- Gauge/Counter enum
- `src/Simetra/Configuration/MetricPollSource.cs` -- Configuration/Module enum
- `src/Simetra/Configuration/OidRole.cs` -- Metric/Label enum
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` -- 3 registration touchpoints (lines 287-293, 422-426)
- `src/Simetra/appsettings.json` -- Existing device configuration structure
- `tests/Simetra.Tests/Devices/NpbModuleTests.cs` -- Reference test pattern
- `.planning/phases/12-npb-device-module/12-RESEARCH.md` -- NPB research for comparison

### Secondary (MEDIUM confidence)
- None needed -- all information derived from codebase primary sources and the MIB file.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- Using existing project infrastructure only, no new libraries
- Architecture: HIGH -- Exact pattern exists in NpbModule; all OIDs verified from MIB file ASN.1 definitions
- OID derivation: HIGH -- Every OID traced through the MIB `::=` definition chain, not from comments (which contain errors)
- EnumMap values: HIGH -- All INTEGER syntax values extracted from MIB OBJECT-TYPE SYNTAX blocks
- Pitfalls: HIGH -- All derived from direct comparison of NPB vs OBP MIB patterns and code analysis

**Research date:** 2026-02-16
**Valid until:** No expiration (MIB file is static, codebase patterns are stable)
