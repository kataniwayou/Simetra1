---
phase: 12-npb-device-module
plan: 01
subsystem: devices
tags: [snmp, npb, device-module, mib, trap-definitions, state-polls, enum-map]

# Dependency graph
requires:
  - phase: 05-device-registry
    provides: IDeviceModule interface and SimetraModule reference pattern
  - phase: 11-trap-channel-consumers
    provides: Trap consumer pipeline that processes trap definitions
provides:
  - NpbModule sealed class implementing IDeviceModule with DeviceType "NPB"
  - 2 trap definitions (portLinkUp with 6 varbinds, portLinkDown with 5 varbinds)
  - 3 module-source state poll definitions (RxPackets, TxPackets, LinkStatus)
  - LinkStatusType EnumMap with 5 entries
  - 29 unit tests covering all NpbModule definitions
affects: [12-02 registration and configuration, 13-obp-device-module]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Multi-trap device module with separate varbind sets per trap type"
    - "EnumMap on OidEntryDto for TEXTUAL-CONVENTION integer-to-string mapping"
    - "Event-driven trap definitions with IntervalSeconds=0"

key-files:
  created:
    - src/Simetra/Devices/NpbModule.cs
    - tests/Simetra.Tests/Devices/NpbModuleTests.cs
  modified: []

key-decisions:
  - "Trap MetricNames use concise snake_case without device prefix (port_link_up, port_link_down) since base labels provide device context per METR-01"
  - "Trap IntervalSeconds set to 0 to indicate event-driven (not polled)"
  - "LinkStatus defined as standalone StatePollDefinition with Gauge type and EnumMap, not attached to trap varbinds"

patterns-established:
  - "Multi-definition device module: multiple trap definitions with varying varbind counts, multiple state poll definitions with mixed MetricTypes"
  - "EnumMap integration: IReadOnlyDictionary<int, string> on OidEntryDto for SNMP TEXTUAL-CONVENTION values"

# Metrics
duration: 2min
completed: 2026-02-16
---

# Phase 12 Plan 01: NpbModule Implementation Summary

**NPB-2E device module with 2 NOTIFICATION-TYPE trap definitions, 3 module-source state polls, and LinkStatusType EnumMap -- all OIDs derived from MIB hierarchy rooted at 1.3.6.1.4.1.47477.100.4**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-16T06:24:12Z
- **Completed:** 2026-02-16T06:26:45Z
- **Tasks:** 2
- **Files created:** 2

## Accomplishments
- NpbModule sealed class implementing IDeviceModule with DeviceType "NPB", DeviceName "npb-2e-01", IpAddress "10.0.10.1"
- 2 trap definitions: portLinkUp (6 varbinds including module, severity, type, message, port_number, port_speed) and portLinkDown (5 varbinds, same minus port_speed)
- 3 state poll definitions: RxPackets (Counter, 30s), TxPackets (Counter, 30s), LinkStatus (Gauge, 30s with EnumMap)
- LinkStatusEnumMap mapping {-1:unknown, 0:down, 1:up, 2:receiveDown, 3:forcedDown}
- 29 unit tests covering identity, trap counts, varbind counts, source validation, metric types, intervals, EnumMap values, and OID prefix correctness
- All 176 project tests pass (147 existing + 29 new), zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Create NpbModule implementing IDeviceModule** - `995a8c6` (feat)
2. **Task 2: Create NpbModuleTests unit tests** - `7ba76cf` (test)

## Files Created/Modified
- `src/Simetra/Devices/NpbModule.cs` - NPB-2E device module with OID constants, trap definitions, state poll definitions, and LinkStatusType EnumMap
- `tests/Simetra.Tests/Devices/NpbModuleTests.cs` - 29 unit tests validating all NpbModule properties and definitions

## Decisions Made
- **Trap MetricNames:** Used `port_link_up` and `port_link_down` (concise, no device prefix) since base labels provide device context per METR-01 convention
- **Trap IntervalSeconds:** Set to 0 for both trap definitions to indicate event-driven (traps are not polled on a schedule)
- **LinkStatus placement:** Defined as a standalone StatePollDefinition in StatePollDefinitions rather than attached to trap varbinds, since portsPortStatusLinkStatus is not in the NOTIFICATION-TYPE OBJECTS list and represents a pollable metric for State Vector
- **OID visibility:** Trap notification OIDs (PortLinkUpTrapOid, PortLinkDownTrapOid) are public const for external trap matching; all other OIDs are private const

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- NpbModule is ready for DI registration and Quartz job scheduling in 12-02
- ServiceCollectionExtensions.AddDeviceModules() needs `AddSingleton<IDeviceModule, NpbModule>()`
- allModules array in AddQuartzJobs() needs NpbModule for StatePollDefinition scheduling
- appsettings.json needs NPB device entry with Configuration-source polls (NPB-04, NPB-05)
- No blockers or concerns

---
*Phase: 12-npb-device-module*
*Completed: 2026-02-16*
