---
phase: 07-telemetry-integration
plan: 02
subsystem: telemetry
tags: [opentelemetry, otlp, logging, structured-logs, forceflush, log-enrichment]

# Dependency graph
requires:
  - phase: 07-telemetry-integration/07-01
    provides: "MeterProvider, TracerProvider, OTLP metric/trace exporters, ILeaderElection, TelemetryConstants"
  - phase: 03-snmp-listener-device-routing/03-01
    provides: "ICorrelationService interface"
  - phase: 01-project-foundation-configuration/01-02
    provides: "SiteOptions, LoggingOptions, OtlpOptions configuration classes"
provides:
  - "SimetraLogEnrichmentProcessor adding site/role/correlationId to all log records"
  - "OTLP log exporter active on all pods (not role-gated)"
  - "Console logging controlled by EnableConsole config flag"
  - "Default logging providers cleared (no stdout when EnableConsole=false)"
  - "ForceFlush on ApplicationStopping with 5-second timeout budget per provider"
  - "Complete three-signal telemetry stack (metrics, traces, logs)"
affects:
  - 08-high-availability
  - 09-health-probes-lifecycle
  - 10-end-to-end-integration-testing

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "BaseProcessor<LogRecord> for log enrichment with DI-resolved dependencies via factory overload"
    - "ClearProviders + conditional AddConsole for console toggle"
    - "ApplicationStopping ForceFlush with null-safe GetService resolution"

key-files:
  created:
    - "src/Simetra/Telemetry/SimetraLogEnrichmentProcessor.cs"
  modified:
    - "src/Simetra/Extensions/ServiceCollectionExtensions.cs"
    - "src/Simetra/Program.cs"

key-decisions:
  - "Factory overload AddProcessor(Func<IServiceProvider, BaseProcessor<LogRecord>>) for DI resolution at runtime"
  - "Func<string> roleProvider delegate on processor to track runtime role changes without reconstruction"
  - "ClearProviders before AddConsole ensures EnableConsole=false produces zero stdout"
  - "OTLP log exporter not role-gated (TELEM-04) -- all pods export logs"
  - "GetService (not GetRequiredService) for ForceFlush provider resolution -- null-safe in test scenarios"

patterns-established:
  - "Log enrichment via BaseProcessor<LogRecord>.OnEnd -- append attributes to existing list with null-check"
  - "ApplicationStopping callback for explicit ForceFlush before DI disposal"

# Metrics
duration: 2min
completed: 2026-02-15
---

# Phase 7 Plan 02: Log Enrichment, OTLP Log Export, and ForceFlush Summary

**SimetraLogEnrichmentProcessor enriches all logs with site/role/correlationId, OTLP log exporter active on all pods, console toggle via ClearProviders, ForceFlush registered on ApplicationStopping with 5s budget**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-15T12:24:00Z
- **Completed:** 2026-02-15T12:26:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- SimetraLogEnrichmentProcessor enriches every log record with site name, leader/follower role, and correlationId attributes
- OTLP log exporter wired on all pods (not role-gated) completing the three-signal telemetry stack
- Default logging providers cleared; console conditionally re-added via EnableConsole flag
- ForceFlush registered on ApplicationStopping with 5-second timeout budget per provider (MeterProvider + TracerProvider)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SimetraLogEnrichmentProcessor and wire logging into AddSimetraTelemetry** - `c8e101f` (feat)
2. **Task 2: Register ForceFlush on ApplicationStopping** - `a545158` (feat)

**Plan metadata:** (pending)

## Files Created/Modified
- `src/Simetra/Telemetry/SimetraLogEnrichmentProcessor.cs` - Log processor adding site, role, correlationId to every LogRecord via OnEnd override
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - Extended AddSimetraTelemetry with ClearProviders, conditional AddConsole, OTLP log exporter, and enrichment processor registration
- `src/Simetra/Program.cs` - Added ApplicationStopping callback with MeterProvider and TracerProvider ForceFlush (5s timeout each)

## Decisions Made
- **Factory overload for processor DI:** Used `AddProcessor(Func<IServiceProvider, BaseProcessor<LogRecord>>)` on `OpenTelemetryLoggerOptions` to resolve SiteOptions, ICorrelationService, and ILeaderElection at runtime. This avoids early construction before DI container is built.
- **Func<string> roleProvider:** Processor takes a delegate `() => leaderElection.CurrentRole` rather than a static string, enabling runtime role changes (Phase 8 HA) without processor reconstruction.
- **ClearProviders first:** Calling `builder.Logging.ClearProviders()` before conditionally adding Console removes Debug and EventSource providers that WebApplication.CreateBuilder adds by default, ensuring EnableConsole=false produces zero stdout output.
- **GetService for ForceFlush:** Used `GetService<MeterProvider>` (not `GetRequiredService`) with null-conditional `?.ForceFlush()` so the shutdown callback is safe in test configurations where providers may not be registered.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 7 (Telemetry Integration) is now complete: all three OTel signals (metrics, traces, logs) export to OTLP
- RoleGatedExporter is built (07-01) but not yet wired into the exporter chain -- Phase 8 handles dynamic role gating
- ForceFlush provides baseline shutdown protection; Phase 9 will integrate into the full time-budgeted shutdown sequence
- Ready for Phase 8 (High Availability): ILeaderElection abstraction and K8sLeaseElection implementation

---
*Phase: 07-telemetry-integration*
*Completed: 2026-02-15*
