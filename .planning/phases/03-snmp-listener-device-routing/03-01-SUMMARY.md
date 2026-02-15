---
phase: 03-snmp-listener-device-routing
plan: 01
subsystem: pipeline
tags: [snmp, channels, device-routing, bounded-channel, trap-filter]

# Dependency graph
requires:
  - phase: 01-project-foundation-configuration
    provides: DevicesOptions, ChannelsOptions, MetricPollOptions configuration models
  - phase: 02-domain-models-extraction-engine
    provides: PollDefinitionDto, OidEntryDto domain models and SharpSnmpLib reference
provides:
  - TrapEnvelope data envelope for channel transport
  - TrapContext middleware processing context
  - DeviceInfo immutable device metadata record
  - ICorrelationService abstraction with startup placeholder
  - IDeviceRegistry for O(1) IP-to-device lookup
  - ITrapFilter for OID-based trap matching
  - IDeviceChannelManager for per-device bounded channels
affects: [03-02, 03-03, 04-snmp-polling, 06-correlation-jobs]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Per-device bounded channels with DropOldest backpressure and itemDropped callback"
    - "IPv4-normalized device lookup via MapToIPv4()"
    - "Sealed class with init properties for mutable-then-immutable data envelopes"

key-files:
  created:
    - src/Simetra/Pipeline/TrapEnvelope.cs
    - src/Simetra/Pipeline/TrapContext.cs
    - src/Simetra/Pipeline/DeviceInfo.cs
    - src/Simetra/Pipeline/ICorrelationService.cs
    - src/Simetra/Pipeline/StartupCorrelationService.cs
    - src/Simetra/Pipeline/IDeviceRegistry.cs
    - src/Simetra/Pipeline/DeviceRegistry.cs
    - src/Simetra/Pipeline/ITrapFilter.cs
    - src/Simetra/Pipeline/TrapFilter.cs
    - src/Simetra/Pipeline/IDeviceChannelManager.cs
    - src/Simetra/Pipeline/DeviceChannelManager.cs
  modified: []

key-decisions:
  - "TrapEnvelope.CorrelationId mutable (get;set;) -- stamped after construction by listener"
  - "TrapEnvelope.MatchedDefinition mutable (get;set;) -- set by TrapFilter to avoid re-matching in Layer 3"
  - "DeviceRegistry uses Dictionary<IPAddress, DeviceInfo> for O(1) lookup with MapToIPv4 normalization"
  - "TrapFilter builds per-definition HashSet for OID matching -- acceptable allocation for correctness"
  - "DeviceChannelManager captures logger via closure in itemDropped callback"

patterns-established:
  - "Sealed class with required init + mutable set for pipeline data flowing through stages"
  - "Interface-first service design: IDeviceRegistry, ITrapFilter, IDeviceChannelManager all have corresponding sealed implementations"
  - "Configuration-to-runtime conversion at constructor time (DeviceRegistry builds from IOptions<DevicesOptions>)"

# Metrics
duration: 2min
completed: 2026-02-15
---

# Phase 3 Plan 01: Pipeline Infrastructure Summary

**TrapEnvelope/TrapContext data types, DeviceRegistry IP lookup, TrapFilter OID matching, and DeviceChannelManager per-device bounded channels with DropOldest backpressure**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-15T07:34:50Z
- **Completed:** 2026-02-15T07:36:54Z
- **Tasks:** 2
- **Files created:** 11

## Accomplishments
- TrapEnvelope carries varbinds, sender IP, timestamp, correlationId, and matched definition through the pipeline
- TrapContext wraps envelope with device info and rejection state for middleware processing
- DeviceRegistry provides O(1) IPv4-normalized device lookup from DevicesOptions configuration
- TrapFilter matches varbind OIDs against device trap definitions using HashSet intersection
- DeviceChannelManager creates per-device bounded channels with DropOldest and Debug-level drop logging

## Task Commits

Each task was committed atomically:

1. **Task 1: Create TrapEnvelope, TrapContext, DeviceInfo, and ICorrelationService** - `cd7cd0b` (feat)
2. **Task 2: Create DeviceRegistry, TrapFilter, and DeviceChannelManager** - `a3d4339` (feat)

## Files Created/Modified
- `src/Simetra/Pipeline/TrapEnvelope.cs` - Sealed class envelope carrying trap data through channels
- `src/Simetra/Pipeline/TrapContext.cs` - Sealed class middleware processing context per-trap
- `src/Simetra/Pipeline/DeviceInfo.cs` - Sealed record holding device metadata with PollDefinitionDtos
- `src/Simetra/Pipeline/ICorrelationService.cs` - Correlation ID abstraction interface
- `src/Simetra/Pipeline/StartupCorrelationService.cs` - Startup placeholder returning stable GUID-based ID
- `src/Simetra/Pipeline/IDeviceRegistry.cs` - Device lookup interface with TryGetDevice
- `src/Simetra/Pipeline/DeviceRegistry.cs` - Dictionary-backed IP-to-DeviceInfo lookup from DevicesOptions
- `src/Simetra/Pipeline/ITrapFilter.cs` - Trap OID matching interface
- `src/Simetra/Pipeline/TrapFilter.cs` - Stateless OID matching with HashSet per definition
- `src/Simetra/Pipeline/IDeviceChannelManager.cs` - Per-device channel management interface
- `src/Simetra/Pipeline/DeviceChannelManager.cs` - Bounded channel factory with DropOldest and itemDropped callback

## Decisions Made
- TrapEnvelope.CorrelationId and MatchedDefinition are mutable (get;set;) because they are stamped after construction -- follows plan guidance that correlationId is stamped after construction and MatchedDefinition is set by TrapFilter
- DeviceRegistry builds Dictionary<IPAddress, DeviceInfo> in constructor -- one-time cost at startup for O(1) runtime lookup
- TrapFilter creates a new HashSet per definition per Match call -- acceptable since trap definitions are small and this avoids stale cached state
- DeviceChannelManager uses StringComparer.Ordinal for device name dictionary -- device names are case-sensitive configuration values
- DeviceChannelManager captures logger and deviceName via closure in itemDropped callback -- clean pattern for bounded channel drop notification

## Deviations from Plan

None -- plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None -- no external service configuration required.

## Next Phase Readiness
- All 11 pipeline infrastructure types ready for Plan 03-02 (SNMP listener middleware) and Plan 03-03 (listener BackgroundService)
- Interfaces ready for DI registration in future plans
- DeviceChannelManager channels ready for Layer 3 consumers
- ICorrelationService placeholder ready for Phase 6 replacement

---
*Phase: 03-snmp-listener-device-routing*
*Completed: 2026-02-15*
