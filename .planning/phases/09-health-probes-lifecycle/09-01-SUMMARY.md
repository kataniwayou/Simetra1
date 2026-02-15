---
phase: 09-health-probes-lifecycle
plan: 01
subsystem: health-checks
tags: [kubernetes, health-probes, IHealthCheck, startup, readiness, liveness, Quartz]

# Dependency graph
requires:
  - phase: 06-scheduling-system
    provides: ILivenessVectorService, LivenessVectorService, Quartz scheduler, all job types
  - phase: 03-snmp-listener-device-routing
    provides: ICorrelationService, IDeviceChannelManager
  - phase: 01-project-foundation-configuration
    provides: LivenessOptions with GraceMultiplier
provides:
  - StartupHealthCheck (HLTH-03) -- correlationId existence check
  - ReadinessHealthCheck (HLTH-04) -- device channels + Quartz scheduler check
  - LivenessHealthCheck (HLTH-05/06/07) -- per-job staleness with diagnostic log on failure, silent on healthy
  - IJobIntervalRegistry + JobIntervalRegistry -- bridges Quartz trigger intervals to liveness check
  - AddSimetraHealthChecks DI registration method
  - Tag-filtered MapHealthChecks endpoints (/healthz/startup, /healthz/ready, /healthz/live)
affects: [09-02 lifecycle/shutdown, 10-end-to-end-validation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Tag-filtered health check endpoints with HealthCheckOptions.Predicate"
    - "Inline registry population during DI registration (JobIntervalRegistry in AddScheduling)"
    - "Silent healthy liveness probe (HLTH-07 -- no log on 200)"

key-files:
  created:
    - src/Simetra/Pipeline/IJobIntervalRegistry.cs
    - src/Simetra/Pipeline/JobIntervalRegistry.cs
    - src/Simetra/HealthChecks/StartupHealthCheck.cs
    - src/Simetra/HealthChecks/ReadinessHealthCheck.cs
    - src/Simetra/HealthChecks/LivenessHealthCheck.cs
  modified:
    - src/Simetra/Extensions/ServiceCollectionExtensions.cs
    - src/Simetra/Program.cs

key-decisions:
  - "JobIntervalRegistry created inline in AddScheduling where interval values are available, registered as singleton instance"
  - "AddSimetraHealthChecks only registers health checks; IJobIntervalRegistry registration stays in AddScheduling"
  - "Liveness unhealthy data uses ToDictionary cast to IReadOnlyDictionary<string, object> for HealthCheckResult compatibility"

patterns-established:
  - "Tag-filtered health probes: startup/ready/live tags with Predicate filtering per endpoint"
  - "Inline singleton population: create instance, populate with config data, register as singleton"

# Metrics
duration: 3min
completed: 2026-02-15
---

# Phase 9 Plan 1: Health Probes + Job Interval Registry Summary

**Three IHealthCheck implementations (startup/readiness/liveness) with tag-filtered endpoints, IJobIntervalRegistry bridging Quartz trigger intervals to per-job staleness thresholds, and silent healthy liveness probe**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-15T15:29:01Z
- **Completed:** 2026-02-15T15:32:07Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- StartupHealthCheck returns 200 when first correlationId exists, 503 otherwise (HLTH-03)
- ReadinessHealthCheck returns 200 when device channels registered and Quartz scheduler running (HLTH-04)
- LivenessHealthCheck returns 200 silently when all stamps fresh, 503 with diagnostic LogWarning when stale (HLTH-05/06/07)
- IJobIntervalRegistry populated during AddScheduling with heartbeat, correlation, state-poll, and metric-poll intervals
- Program.cs skeleton endpoints replaced with tag-filtered MapHealthChecks returning explicit 200/503 status codes

## Task Commits

Each task was committed atomically:

1. **Task 1: Create IJobIntervalRegistry, JobIntervalRegistry, and three IHealthCheck implementations** - `87ddf6e` (feat)
2. **Task 2: Wire health checks into DI and replace skeleton endpoints with tag-filtered mapping** - `1204ad9` (feat)

## Files Created/Modified
- `src/Simetra/Pipeline/IJobIntervalRegistry.cs` - Interface: Register and TryGetInterval for job key to interval mapping
- `src/Simetra/Pipeline/JobIntervalRegistry.cs` - Dictionary-backed singleton, populated during AddScheduling
- `src/Simetra/HealthChecks/StartupHealthCheck.cs` - HLTH-03: checks correlationId existence via ICorrelationService
- `src/Simetra/HealthChecks/ReadinessHealthCheck.cs` - HLTH-04: checks device channels count + Quartz scheduler state
- `src/Simetra/HealthChecks/LivenessHealthCheck.cs` - HLTH-05/06/07: per-job staleness check with grace multiplier, diagnostic log on failure, silent on healthy
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - Added AddSimetraHealthChecks method; AddScheduling now populates IJobIntervalRegistry
- `src/Simetra/Program.cs` - Replaced skeleton AddHealthChecks/MapHealthChecks with tag-filtered versions

## Decisions Made
- [09-01]: JobIntervalRegistry created inline in AddScheduling and registered as singleton instance -- interval values only available during Quartz configuration, not during DI resolution
- [09-01]: IJobIntervalRegistry registration stays in AddScheduling (not AddSimetraHealthChecks) because that is where interval data lives
- [09-01]: Liveness staleEntries uses anonymous objects with ageSeconds/thresholdSeconds/lastStamp, cast to IReadOnlyDictionary<string, object> for HealthCheckResult.Unhealthy data parameter

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All three health check endpoints operational with tag filtering
- IJobIntervalRegistry available for any future per-job threshold calculations
- Ready for Phase 9 Plan 2: Graceful shutdown lifecycle (GracefulShutdownService, channel drain, telemetry flush budget)

---
*Phase: 09-health-probes-lifecycle*
*Completed: 2026-02-15*
