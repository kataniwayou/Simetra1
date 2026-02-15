---
phase: 06-scheduling-system
plan: 02
subsystem: scheduling
tags: [snmp-polling, quartz-jobs, state-poll, metric-poll, extractor, pipeline]

dependency-graph:
  requires: ["06-01"]
  provides: ["StatePollJob implementation", "MetricPollJob implementation", "SNMP GET poll execution"]
  affects: ["06-03", "08-health-checks"]

tech-stack:
  added: []
  patterns: ["SNMP GET poll + extract + process pipeline", "Liveness stamping in finally block", "CorrelationId read-before-execute"]

key-files:
  created: []
  modified:
    - src/Simetra/Jobs/StatePollJob.cs
    - src/Simetra/Jobs/MetricPollJob.cs

decisions: []

metrics:
  duration: "1 min"
  completed: "2026-02-15"
---

# Phase 6 Plan 2: Poll Job Implementations Summary

**Full StatePollJob and MetricPollJob with SNMP GET + extract + process pipeline, replacing 06-01 stubs**

## What Was Done

### Task 1: Implement StatePollJob with full SNMP GET + extract + process
- Replaced stub with full 115-line implementation
- Constructor injection for 8 dependencies: IDeviceRegistry, IPollDefinitionRegistry, ISnmpExtractor, IProcessingCoordinator, ICorrelationService, ILivenessVectorService, IOptions<SnmpListenerOptions>, ILogger<StatePollJob>
- Execute method reads correlationId before any work (SCHED-08)
- Looks up DeviceInfo by name via IDeviceRegistry.TryGetDeviceByName
- Looks up PollDefinitionDto by (deviceName, metricName) via IPollDefinitionRegistry.TryGetDefinition
- Builds SNMP Variable list from definition OIDs
- Performs SNMP GET to device:161 via Messenger.GetAsync with CancellationToken
- Extracts response via ISnmpExtractor.Extract, processes via IProcessingCoordinator.Process
- Stamps liveness in finally block (always executes, even on failure)
- Rethrows OperationCanceledException for clean Quartz shutdown
- **Commit:** `95217fb`

### Task 2: Implement MetricPollJob with full SNMP GET + extract + process
- Structurally identical to StatePollJob with metric-specific log messages
- Same 8 constructor dependencies confirming SCHED-04 (same generic extractor)
- Same Execute flow: correlationId -> lookup device -> lookup definition -> SNMP GET -> extract -> process -> liveness stamp
- Source routing (Branch A only vs Branch A + B) handled by IProcessingCoordinator, not the job
- **Commit:** `1d5565c`

## Design Decisions

No new architectural decisions. All patterns follow established conventions from 06-01 and research.

## Requirements Satisfied

| Requirement | How Satisfied |
|-------------|---------------|
| SCHED-02 | StatePollJob polls device via SNMP GET and feeds to Layer 3/4 |
| SCHED-03 | MetricPollJob polls device via SNMP GET and feeds to Layer 3/4 |
| SCHED-04 | Both jobs use same ISnmpExtractor.Extract and same PollDefinitionDto |
| SCHED-08 | Both read correlationId before execution, stamp liveness in finally |
| SCHED-09 | DisallowConcurrentExecution prevents overlapping executions; skipped jobs never enter Execute, so no stamp |
| PIPE-06 | Both jobs bypass Layer 2 channels, feeding directly to Layer 3/4 |

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

1. `dotnet build src/Simetra/Simetra.csproj` -- 0 errors, 0 warnings
2. StatePollJob.cs: [DisallowConcurrentExecution] (line 17), _correlation.CurrentCorrelationId (line 52), _liveness.Stamp in finally (line 112), Messenger.GetAsync (line 83), _extractor.Extract (line 91), _coordinator.Process (line 92)
3. MetricPollJob.cs: identical structure with class-specific names and log messages
4. Both use TryGetDeviceByName for device lookup
5. Both use TryGetDefinition for definition lookup
6. Both use SnmpListenerOptions.CommunityString
7. CancellationToken overload used (no int timeout overload exists in SharpSnmpLib 12.5.7)

## Next Phase Readiness

Plan 06-03 (HeartbeatJob + remaining scheduling jobs) can proceed. Both poll job implementations are complete and available for Quartz execution.
