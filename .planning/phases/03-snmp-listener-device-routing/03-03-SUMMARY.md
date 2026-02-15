---
phase: 03-snmp-listener-device-routing
plan: 03
subsystem: snmp-pipeline
tags: [snmp, udp, backgroundservice, sharpsnmplib, middleware, di, channels]

# Dependency graph
requires:
  - phase: 03-01
    provides: "TrapEnvelope, TrapContext, DeviceInfo, DeviceRegistry, TrapFilter, DeviceChannelManager, ICorrelationService"
  - phase: 03-02
    provides: "ITrapMiddleware, TrapPipelineBuilder, ErrorHandlingMiddleware, CorrelationIdMiddleware, LoggingMiddleware"
  - phase: 02-02
    provides: "SnmpExtractorService implementing ISnmpExtractor"
  - phase: 01-02
    provides: "SnmpListenerOptions, DevicesOptions, ChannelsOptions configuration"
provides:
  - "SnmpListenerService BackgroundService -- Layer 1 UDP reception + Layer 2 routing"
  - "AddSnmpPipeline DI extension method -- all Phase 3 services registered as singletons"
  - "Complete SNMP trap reception pipeline: UDP -> parse -> validate -> middleware -> device lookup -> OID filter -> channel write"
affects: [04-otlp-telemetry-emission, 06-poll-engine, 09-health-observability, 10-integration-tests]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "BackgroundService with UdpClient receive loop and CancellationToken shutdown"
    - "Middleware pipeline injected as singleton TrapMiddlewareDelegate via factory registration"
    - "Terminal processing after middleware: device lookup -> OID filter -> channel write"

key-files:
  created:
    - "src/Simetra/Services/SnmpListenerService.cs"
  modified:
    - "src/Simetra/Extensions/ServiceCollectionExtensions.cs"
    - "src/Simetra/Program.cs"

key-decisions:
  - "Middleware pipeline runs before device lookup -- allows correlationId stamping and logging on all traps including unknown devices"
  - "Device lookup and OID filtering are terminal logic after pipeline, not middleware -- keeps middleware chain generic and reusable"
  - "TrapMiddlewareDelegate injected as singleton via factory lambda, resolved from ServiceProvider at startup"
  - "ISnmpExtractor registered in DI but not wired to listener -- poll jobs will call it directly in Phase 6"

patterns-established:
  - "AddSnmpPipeline: single extension method registers all pipeline services with correct ordering dependency on AddSimetraConfiguration"
  - "ProcessDatagramAsync: extracted method pattern for clarity and testability in BackgroundService"

# Metrics
duration: 4min
completed: 2026-02-15
---

# Phase 3 Plan 3: SNMP Listener Service + DI Wiring Summary

**SnmpListenerService BackgroundService receiving UDP traps via UdpClient, parsing with SharpSnmpLib MessageFactory, running middleware pipeline, and routing to device channels via DeviceRegistry + TrapFilter + DeviceChannelManager**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-15T07:45:06Z
- **Completed:** 2026-02-15T07:49:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- SnmpListenerService binds UdpClient to configured endpoint, loops on ReceiveAsync with graceful CancellationToken shutdown
- Full trap processing pipeline: parse -> community string validation -> middleware (error/correlation/logging) -> device lookup -> OID filter -> channel write
- All Phase 3 services registered in DI as singletons via AddSnmpPipeline extension method
- Resilient to socket errors and unexpected exceptions without killing the listener loop

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SnmpListenerService BackgroundService** - `5108c0e` (feat)
2. **Task 2: Wire all Phase 3 services into DI and Program.cs** - `e54e7b1` (feat)

## Files Created/Modified
- `src/Simetra/Services/SnmpListenerService.cs` - BackgroundService: UDP receive loop, datagram parsing, community validation, middleware execution, device routing, channel write (175 lines)
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - Added AddSnmpPipeline method registering all pipeline singletons and hosted service
- `src/Simetra/Program.cs` - Added builder.Services.AddSnmpPipeline() call after configuration registration

## Decisions Made
- Middleware pipeline runs before device lookup -- correlationId and logging apply to all traps including those from unknown devices, providing full observability
- Device lookup, OID filtering, and channel write are terminal logic after the middleware pipeline rather than middleware themselves -- keeps the middleware chain generic and the terminal logic explicit in the service
- TrapMiddlewareDelegate registered as singleton via factory lambda that resolves middleware from ServiceProvider -- ensures pipeline is built once at startup
- ISnmpExtractor registered in DI but not consumed by the listener -- poll jobs will call it directly, bypassing channels entirely (PIPE-06 design)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Complete SNMP trap pipeline operational: UDP reception through device channel write
- ISnmpExtractor registered and ready for Phase 6 poll jobs to call directly
- Phase 4 (OTLP telemetry emission) can proceed -- it reads from device channels populated by this service
- All 60 existing tests continue to pass

---
*Phase: 03-snmp-listener-device-routing*
*Completed: 2026-02-15*
