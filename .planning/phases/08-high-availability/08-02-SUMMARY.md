---
phase: 08-high-availability
plan: 02
subsystem: telemetry
tags: [opentelemetry, otlp, role-gating, leader-election, metrics, traces, exporter]

# Dependency graph
requires:
  - phase: 07-telemetry-integration
    provides: "RoleGatedExporter<T>, OpenTelemetry OTLP exporter chain, log enrichment processor"
  - phase: 08-high-availability-01
    provides: "K8sLeaseElection, ILeaderElection DI registration, AlwaysLeaderElection"
provides:
  - "Metric OTLP exports dynamically gated by ILeaderElection.IsLeader via RoleGatedExporter<Metric>"
  - "Trace OTLP exports dynamically gated by ILeaderElection.IsLeader via RoleGatedExporter<Activity>"
  - "Complete HA exporter pipeline: K8sLeaseElection -> ILeaderElection.IsLeader -> RoleGatedExporter.Export -> OTLP"
affects: [09-health-endpoints, 10-testing]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Manual OTLP exporter construction with RoleGatedExporter wrapping (replaces AddOtlpExporter convenience)"
    - "Factory overload DI pattern: AddReader(Func<IServiceProvider, MetricReader>) for deferred resolution"
    - "Factory overload DI pattern: AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>) for deferred resolution"

key-files:
  created: []
  modified:
    - "src/Simetra/Extensions/ServiceCollectionExtensions.cs"

key-decisions:
  - "Manual OtlpMetricExporter/OtlpTraceExporter construction required -- AddOtlpExporter() creates and registers internally, preventing RoleGatedExporter wrapping"
  - "AddReader(sp => ...) factory overload for metrics -- enables ILeaderElection resolution from DI at runtime"
  - "AddProcessor(sp => ...) factory overload for traces -- same deferred DI resolution pattern"
  - "Log OTLP exporter intentionally NOT wrapped (TELEM-04) -- all pods export logs regardless of leader/follower role"

patterns-established:
  - "Role-gated exporter wiring: manual exporter -> RoleGatedExporter wrapper -> SDK reader/processor"

# Metrics
duration: 2min
completed: 2026-02-15
---

# Phase 8 Plan 2: Role-Gated OTLP Exporter Wiring Summary

**Manual OTLP exporter construction with RoleGatedExporter wrapping for metrics (PeriodicExportingMetricReader) and traces (BatchActivityExportProcessor), enabling dynamic leader/follower gating without restart**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-15T13:21:47Z
- **Completed:** 2026-02-15T13:23:41Z
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments
- Metric OTLP exporter wrapped in RoleGatedExporter<Metric> via manual OtlpMetricExporter construction and PeriodicExportingMetricReader
- Trace OTLP exporter wrapped in RoleGatedExporter<Activity> via manual OtlpTraceExporter construction and BatchActivityExportProcessor
- ILeaderElection resolved from DI at runtime via AddReader/AddProcessor factory overloads -- role changes take effect on next Export call
- Log OTLP exporter remains unwrapped -- all pods export logs (TELEM-04)
- Complete HA chain verified: K8sLeaseElection._isLeader -> ILeaderElection.IsLeader -> RoleGatedExporter.Export -> OtlpExporter.Export (leader) or Success (follower)

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace AddOtlpExporter with manual role-gated exporter wiring** - `ce06774` (feat)

**Plan metadata:** (pending)

## Files Created/Modified
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - Replaced AddOtlpExporter() with manual exporter construction wrapped in RoleGatedExporter for metrics and traces; added using directives for System.Diagnostics, OpenTelemetry, OpenTelemetry.Exporter

## Decisions Made
- Manual OtlpMetricExporter/OtlpTraceExporter construction required because AddOtlpExporter() creates and registers the exporter internally, preventing wrapping with RoleGatedExporter
- AddReader(Func<IServiceProvider, MetricReader>) factory overload used for metrics to enable ILeaderElection resolution from DI at runtime
- AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>) factory overload used for traces (same deferred DI pattern)
- Log OTLP exporter intentionally NOT wrapped -- all pods export logs regardless of role (TELEM-04, HA-03)
- PeriodicExportingMetricReader default 60s export interval matches previous AddOtlpExporter() default -- no override needed

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 8 (High Availability) is complete -- K8sLeaseElection (08-01) + role-gated exporter wiring (08-02) fully integrated
- Ready for Phase 9 (Health Endpoints) -- all HA infrastructure in place for health check integration
- The complete HA chain is operational: Kubernetes Lease election drives ILeaderElection.IsLeader, which gates metric and trace OTLP exports via RoleGatedExporter on every Export call

---
*Phase: 08-high-availability*
*Completed: 2026-02-15*
