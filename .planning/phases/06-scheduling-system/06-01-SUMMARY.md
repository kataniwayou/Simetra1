---
phase: 06-scheduling-system
plan: 01
subsystem: scheduling
tags: [quartz, scheduling, correlation, liveness, poll-registry]

# Dependency graph
requires:
  - phase: 05-plugin-system-simetra-module
    provides: "IDeviceModule interface and SimetraModule with TrapDefinitions/StatePollDefinitions"
  - phase: 03-snmp-listener-device-routing
    provides: "IDeviceRegistry, DeviceRegistry, ICorrelationService, StartupCorrelationService"
  - phase: 01-project-foundation-configuration
    provides: "HeartbeatJobOptions, CorrelationJobOptions, DevicesOptions configuration"
provides:
  - "Quartz.NET scheduler with in-memory store and DisallowConcurrentExecution on all jobs"
  - "RotatingCorrelationService with volatile field replacing StartupCorrelationService"
  - "ILivenessVectorService + LivenessVectorService with ConcurrentDictionary"
  - "IPollDefinitionRegistry + PollDefinitionRegistry with composite key O(1) lookup"
  - "IDeviceRegistry.TryGetDeviceByName for name-based device lookup"
  - "Four stub job classes (StatePollJob, MetricPollJob, HeartbeatJob, CorrelationJob)"
  - "Dynamic job+trigger registration per device per poll definition"
  - "First correlationId generated on startup before any job fires (LIFE-02)"
affects: [06-02, 06-03, 09-health-checks]

# Tech tracking
tech-stack:
  added: [Quartz.AspNetCore 3.15.1]
  patterns: [DisallowConcurrentExecution on all jobs, SimpleTrigger with NextWithRemainingCount misfire, volatile field for single-writer/multi-reader, composite string key for dictionary lookup]

key-files:
  created:
    - src/Simetra/Pipeline/RotatingCorrelationService.cs
    - src/Simetra/Pipeline/ILivenessVectorService.cs
    - src/Simetra/Pipeline/LivenessVectorService.cs
    - src/Simetra/Pipeline/IPollDefinitionRegistry.cs
    - src/Simetra/Pipeline/PollDefinitionRegistry.cs
    - src/Simetra/Jobs/StatePollJob.cs
    - src/Simetra/Jobs/MetricPollJob.cs
    - src/Simetra/Jobs/HeartbeatJob.cs
    - src/Simetra/Jobs/CorrelationJob.cs
  modified:
    - src/Simetra/Simetra.csproj
    - src/Simetra/Pipeline/ICorrelationService.cs
    - src/Simetra/Pipeline/IDeviceRegistry.cs
    - src/Simetra/Pipeline/DeviceRegistry.cs
    - src/Simetra/Extensions/ServiceCollectionExtensions.cs
    - src/Simetra/Program.cs

key-decisions:
  - "RotatingCorrelationService uses volatile string for lock-free single-writer/multi-reader pattern"
  - "PollDefinitionRegistry uses composite string key deviceName::metricName with OrdinalIgnoreCase"
  - "DeviceRegistry adds second Dictionary<string, DeviceInfo> with OrdinalIgnoreCase for name lookup"
  - "SimetraModule instantiated directly in AddScheduling for compile-time module enumeration"
  - "All SimpleTriggers use WithMisfireHandlingInstructionNextWithRemainingCount (not DoNothing which is CronTrigger-only)"
  - "AddScheduling binds options directly from IConfiguration rather than using temporary ServiceProvider"

patterns-established:
  - "Job stub pattern: sealed class with [DisallowConcurrentExecution] + IJob, Execute returns Task.CompletedTask"
  - "Dynamic job registration: iterate device config/modules, create JobKey with deviceName-metricName, pass deviceName/metricName via UsingJobData"
  - "DI registration order: Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks"

# Metrics
duration: 5min
completed: 2026-02-15
---

# Phase 6 Plan 1: Scheduling Infrastructure Summary

**Quartz.NET 3.15.1 scheduler with RotatingCorrelationService, LivenessVectorService, PollDefinitionRegistry, and four stub jobs registered with dynamic per-device triggers**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-15T09:35:35Z
- **Completed:** 2026-02-15T09:40:02Z
- **Tasks:** 2
- **Files modified:** 15 (9 created, 6 modified, 1 deleted)

## Accomplishments
- Installed Quartz.AspNetCore 3.15.1 with in-memory store and 10-thread pool
- Replaced StartupCorrelationService with RotatingCorrelationService (volatile field, SetCorrelationId method)
- Created ILivenessVectorService + LivenessVectorService backed by ConcurrentDictionary for job liveness tracking
- Extended IDeviceRegistry/DeviceRegistry with TryGetDeviceByName (OrdinalIgnoreCase)
- Created IPollDefinitionRegistry + PollDefinitionRegistry with composite key O(1) lookup
- Created four stub job classes with [DisallowConcurrentExecution]
- Registered all jobs and triggers dynamically from config and modules
- Wired Program.cs with first correlationId before app.Run() (LIFE-02)

## Task Commits

Each task was committed atomically:

1. **Task 1: Install Quartz.AspNetCore and create core scheduling services** - `f4651ad` (feat)
2. **Task 2: Create stub job classes, AddScheduling method, and Program.cs wiring** - `7a46115` (feat)

## Files Created/Modified
- `src/Simetra/Simetra.csproj` - Added Quartz.AspNetCore 3.15.1 package reference
- `src/Simetra/Pipeline/ICorrelationService.cs` - Added SetCorrelationId method to interface
- `src/Simetra/Pipeline/RotatingCorrelationService.cs` - Thread-safe volatile string correlation service
- `src/Simetra/Pipeline/ILivenessVectorService.cs` - Liveness vector abstraction (Stamp, GetStamp, GetAllStamps)
- `src/Simetra/Pipeline/LivenessVectorService.cs` - ConcurrentDictionary-backed liveness stamps
- `src/Simetra/Pipeline/IDeviceRegistry.cs` - Added TryGetDeviceByName method
- `src/Simetra/Pipeline/DeviceRegistry.cs` - Added second dictionary for name-based O(1) lookup
- `src/Simetra/Pipeline/IPollDefinitionRegistry.cs` - Poll definition lookup interface
- `src/Simetra/Pipeline/PollDefinitionRegistry.cs` - Indexed poll definitions from config + modules
- `src/Simetra/Jobs/StatePollJob.cs` - Stub state poll job with DisallowConcurrentExecution
- `src/Simetra/Jobs/MetricPollJob.cs` - Stub metric poll job with DisallowConcurrentExecution
- `src/Simetra/Jobs/HeartbeatJob.cs` - Stub heartbeat job with DisallowConcurrentExecution
- `src/Simetra/Jobs/CorrelationJob.cs` - Stub correlation job with DisallowConcurrentExecution
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - AddScheduling method + RotatingCorrelationService swap
- `src/Simetra/Program.cs` - AddScheduling call + LIFE-02 first correlationId

## Decisions Made
- [06-01]: RotatingCorrelationService uses volatile string -- single writer (startup then CorrelationJob), multiple readers (all jobs), no locks needed
- [06-01]: PollDefinitionRegistry uses composite string key "deviceName::metricName" with StringComparer.OrdinalIgnoreCase -- simpler than tuple comparer
- [06-01]: DeviceRegistry adds OrdinalIgnoreCase name dictionary alongside existing IP dictionary -- device names are user-configured
- [06-01]: SimetraModule instantiated directly in AddScheduling -- modules are code-defined, avoids needing temporary ServiceProvider
- [06-01]: All SimpleTriggers use WithMisfireHandlingInstructionNextWithRemainingCount -- DoNothing is CronTrigger-only (SCHED-10)
- [06-01]: AddScheduling binds HeartbeatJobOptions/CorrelationJobOptions/DevicesOptions directly from IConfiguration at registration time

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Moved RotatingCorrelationService DI swap from Task 2 to Task 1**
- **Found during:** Task 1 (after deleting StartupCorrelationService.cs)
- **Issue:** Task 1 Step 4 deletes StartupCorrelationService.cs, but the DI registration in ServiceCollectionExtensions still references it, causing CS0246 compile error. Plan had this swap scheduled for Task 2 Step 3.
- **Fix:** Updated `AddSingleton<ICorrelationService, StartupCorrelationService>` to `AddSingleton<ICorrelationService, RotatingCorrelationService>` in Task 1 instead of Task 2
- **Files modified:** src/Simetra/Extensions/ServiceCollectionExtensions.cs
- **Verification:** Build succeeds with 0 errors after Task 1
- **Committed in:** f4651ad (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary to maintain compilable state between task commits. No scope creep.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All four job stubs ready for Plans 06-02 (StatePollJob, MetricPollJob) and 06-03 (HeartbeatJob, CorrelationJob)
- RotatingCorrelationService.SetCorrelationId ready for CorrelationJob to call
- ILivenessVectorService.Stamp ready for all jobs to call in finally blocks
- IPollDefinitionRegistry.TryGetDefinition ready for poll jobs to resolve their definitions
- IDeviceRegistry.TryGetDeviceByName ready for poll jobs to resolve their target devices
- No blockers or concerns

---
*Phase: 06-scheduling-system*
*Completed: 2026-02-15*
