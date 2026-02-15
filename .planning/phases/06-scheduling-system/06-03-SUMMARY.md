---
phase: 06-scheduling-system
plan: 03
subsystem: scheduling
tags: [quartz, snmp, heartbeat, correlation, sharpsnmplib, loopback-trap]

# Dependency graph
requires:
  - phase: 06-scheduling-system (plan 01)
    provides: Stub HeartbeatJob and CorrelationJob classes, ICorrelationService, ILivenessVectorService
  - phase: 05-plugin-system-simetra-module
    provides: SimetraModule.HeartbeatOid const (single source of truth)
provides:
  - Fully implemented HeartbeatJob sending loopback SNMP v2c trap
  - Fully implemented CorrelationJob rotating correlationId
  - Both jobs stamp liveness vector on completion
affects: [07-health-checks, 08-otlp-telemetry, 10-testing-validation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Loopback trap pattern: HeartbeatJob sends SNMP trap to 127.0.0.1 on listener port, flowing through full pipeline"
    - "Single-writer correlation rotation: CorrelationJob is sole scheduled writer after startup"
    - "Task.Run wrapper for synchronous SNMP operations in async Quartz jobs"

key-files:
  created: []
  modified:
    - src/Simetra/Jobs/HeartbeatJob.cs
    - src/Simetra/Jobs/CorrelationJob.cs

key-decisions:
  - "Task.Run wraps synchronous Messenger.SendTrapV2 -- avoids blocking Quartz thread pool"
  - "CorrelationId format is Guid.NewGuid().ToString('N') -- 32-char hex, no hyphens"
  - "Correlation rotation logged at Information level (not Debug) -- operational visibility for ID transitions"

patterns-established:
  - "Loopback heartbeat trap: proves scheduler + full pipeline alive via single SNMP trap to self"
  - "Liveness stamping in finally block: every job stamps regardless of success/failure"

# Metrics
duration: 2min
completed: 2026-02-15
---

# Phase 6 Plan 3: HeartbeatJob and CorrelationJob Summary

**HeartbeatJob sends loopback SNMP v2c trap via SimetraModule.HeartbeatOid; CorrelationJob rotates correlationId via ICorrelationService -- both stamp liveness vector in finally block**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-15T09:46:46Z
- **Completed:** 2026-02-15T09:48:24Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- HeartbeatJob sends SNMP v2c trap to 127.0.0.1 on configured listener port using SimetraModule.HeartbeatOid as single source of truth (SCHED-05, SCHED-06)
- CorrelationJob generates new correlationId via Guid.NewGuid and sets via ICorrelationService.SetCorrelationId (SCHED-07)
- Both jobs read correlationId before execution and stamp liveness vector in finally block (SCHED-08)
- DisallowConcurrentExecution ensures skipped jobs produce no liveness stamp (SCHED-09)

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement HeartbeatJob with loopback trap send** - `9a243a5` (feat)
2. **Task 2: Implement CorrelationJob with correlationId rotation** - `605e874` (feat)

## Files Created/Modified
- `src/Simetra/Jobs/HeartbeatJob.cs` - Sends loopback SNMP v2c trap to prove scheduler is alive; reads OID from SimetraModule.HeartbeatOid
- `src/Simetra/Jobs/CorrelationJob.cs` - Rotates correlationId for log grouping; sole scheduled writer after startup

## Decisions Made
- Task.Run wraps synchronous Messenger.SendTrapV2 to avoid blocking Quartz thread pool (SendTrapV2Async not available in SharpSnmpLib)
- CorrelationId format: Guid.NewGuid().ToString("N") producing 32-character hex string without hyphens
- Correlation rotation logged at Information level for operational visibility of ID transitions
- HeartbeatJob stores listenerPort and communityString from SnmpListenerOptions in constructor (not re-read per execution)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All four Quartz job types fully implemented: HeartbeatJob, CorrelationJob, StatePollJob, MetricPollJob
- Phase 6 (Scheduling System) complete -- all 3 plans executed
- Ready for Phase 7 (Health Checks) which consumes ILivenessVectorService stamps from these jobs

---
*Phase: 06-scheduling-system*
*Completed: 2026-02-15*
