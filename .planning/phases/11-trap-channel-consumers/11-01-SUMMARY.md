---
phase: 11-trap-channel-consumers
plan: 01
subsystem: pipeline
tags: [otlp, metrics, naming, system-diagnostics-metrics]

# Dependency graph
requires:
  - phase: 05-extraction-metric-recording
    provides: MetricFactory with instrument caching and base labels
provides:
  - PropertyName-only OTLP metric naming (METR-01)
  - Cleaner metric names for downstream consumers (e.g., "beat", "uptime", "port_rx_octets")
affects: [11-02, 11-03, 12-npb-device-module, 13-obp-device-module]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "METR-01: PropertyName IS the metric name; base labels provide device context"

key-files:
  created: []
  modified:
    - src/Simetra/Pipeline/MetricFactory.cs
    - src/Simetra/Pipeline/IMetricFactory.cs
    - tests/Simetra.Tests/Processing/MetricFactoryTests.cs

key-decisions:
  - "METR-01: Use PropertyName directly as OTLP metric name instead of {MetricName}_{PropertyName} -- base labels already disambiguate devices/types"

patterns-established:
  - "Metric naming: PropertyName is the instrument name, base labels (site, device_name, device_ip, device_type) provide context"

# Metrics
duration: 2min
completed: 2026-02-16
---

# Phase 11 Plan 01: Metric Naming Summary

**METR-01 metric naming: PropertyName used directly as OTLP instrument name, eliminating redundant MetricName prefix**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-16T05:30:32Z
- **Completed:** 2026-02-16T05:32:04Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- MetricFactory emits instruments named by PropertyName only (e.g., "beat", "uptime", "value") instead of "{MetricName}_{PropertyName}"
- Base labels (site, device_name, device_ip, device_type) remain unchanged on every measurement
- All 139 existing tests pass with updated assertions, zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Change MetricFactory metric name construction** - `56d81c9` (feat)
2. **Task 2: Update all test assertions for new metric name format** - `f8788d5` (test)

## Files Created/Modified
- `src/Simetra/Pipeline/MetricFactory.cs` - Changed metric name from `{MetricName}_{PropertyName}` to `propertyName`; simplified error log
- `src/Simetra/Pipeline/IMetricFactory.cs` - Updated XML doc comment to reflect new naming pattern
- `tests/Simetra.Tests/Processing/MetricFactoryTests.cs` - Updated 3 test assertions to expect PropertyName-only instrument names

## Decisions Made
None - followed plan as specified.

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- METR-01 naming convention active, all future device modules will automatically benefit from cleaner metric names
- HeartbeatLoopbackTests confirmed no assertion changes needed (test doesn't assert on emitted instrument names)
- Ready for 11-02 (trap channel consumer pipeline) and 11-03 (trap definition wiring)

---
*Phase: 11-trap-channel-consumers*
*Completed: 2026-02-16*
