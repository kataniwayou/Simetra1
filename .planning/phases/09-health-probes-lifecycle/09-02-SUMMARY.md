---
phase: 09-health-probes-lifecycle
plan: 02
subsystem: lifecycle
tags: [graceful-shutdown, IHostedService, time-budget, channel-drain, telemetry-flush, Kubernetes]

# Dependency graph
requires:
  - phase: 09-01
    provides: Health check endpoints, IJobIntervalRegistry, AddSimetraHealthChecks DI method
  - phase: 08-high-availability
    provides: K8sLeaseElection, ILeaderElection, lease delete on shutdown
  - phase: 07-telemetry-integration
    provides: MeterProvider, TracerProvider, ForceFlush pattern
  - phase: 06-scheduling-system
    provides: ISchedulerFactory, Quartz scheduler, QuartzHostedService
  - phase: 03-snmp-listener-device-routing
    provides: SnmpListenerService, IDeviceChannelManager, DeviceChannelManager
provides:
  - GracefulShutdownService -- time-budgeted 5-step LIFE-05 shutdown orchestrator
  - CompleteAll() + WaitForDrainAsync() on IDeviceChannelManager/DeviceChannelManager
  - AddSimetraLifecycle() DI registration method (registered LAST, stops FIRST)
  - HostOptions.ShutdownTimeout = 30s
  - 11-step startup sequence documentation
  - 5-step shutdown sequence documentation
affects: [10-end-to-end-validation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ExecuteWithBudget: CancellationTokenSource.CreateLinkedTokenSource + CancelAfter for per-step time budgets"
    - "Protected telemetry flush: own CTS independent of host shutdown token (LIFE-07)"
    - "IServiceProvider.GetService<T>() null-safe resolution for optional services"
    - "IHostedService reverse-order stop: registered LAST = stops FIRST"

key-files:
  created:
    - src/Simetra/Lifecycle/GracefulShutdownService.cs
  modified:
    - src/Simetra/Pipeline/IDeviceChannelManager.cs
    - src/Simetra/Pipeline/DeviceChannelManager.cs
    - src/Simetra/Extensions/ServiceCollectionExtensions.cs
    - src/Simetra/Program.cs

key-decisions:
  - "GracefulShutdownService implements IHostedService (not BackgroundService) -- no ExecuteAsync needed, only StopAsync"
  - "SnmpListenerService resolved via IEnumerable<IHostedService>.OfType<T>() -- AddHostedService does not register concrete type directly"
  - "K8sLeaseElection resolved via GetService<K8sLeaseElection>() -- registered as concrete singleton, null in local dev mode"
  - "FlushTelemetryAsync uses independent CTS (not linked to outer token) -- telemetry flush gets full budget regardless of prior outcomes"
  - "ForceFlush lambda removed from Program.cs ApplicationStopping and consolidated into GracefulShutdownService Step 5"
  - "DeviceChannelManager._logger field added for CompleteAll/WaitForDrainAsync logging (previously only captured in closure)"

patterns-established:
  - "Time-budgeted shutdown steps: ExecuteWithBudget wraps each step with CancelAfter, logs warnings on timeout, errors on failure"
  - "Protected final step: telemetry flush uses own CTS independent of host shutdown budget"
  - "Registered-last-stops-first: GracefulShutdownService registered after all other IHostedService instances"

# Metrics
duration: 4min
completed: 2026-02-15
---

# Phase 9 Plan 2: Graceful Shutdown with Time-Budgeted Steps Summary

**GracefulShutdownService orchestrating 5-step LIFE-05 shutdown (release lease -> stop listener -> scheduler standby -> drain channels -> flush telemetry) with per-step CancelAfter time budgets and protected telemetry flush**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-15T15:36:50Z
- **Completed:** 2026-02-15T15:40:23Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- GracefulShutdownService implements complete LIFE-05 shutdown: release lease (3s) -> stop listener (3s) -> scheduler standby (3s) -> drain channels (8s) -> flush telemetry (5s protected)
- Each shutdown step bounded by CancelAfter time budget; exceeded steps abandoned without blocking remaining sequence
- Telemetry flush uses independent CTS (LIFE-07) -- gets full 5s budget regardless of prior step outcomes
- DeviceChannelManager gains CompleteAll() and WaitForDrainAsync() for channel drain during shutdown
- ForceFlush consolidated from Program.cs ApplicationStopping lambda into GracefulShutdownService Step 5
- HostOptions.ShutdownTimeout set to 30s, GracefulShutdownService registered last (stops first)
- 11-step startup and 5-step shutdown sequences documented in ServiceCollectionExtensions.cs

## Task Commits

Each task was committed atomically:

1. **Task 1: Add channel drain API and create GracefulShutdownService with full 5-step LIFE-05 orchestration** - `e886e41` (feat)
2. **Task 2: Wire GracefulShutdownService into DI, configure ShutdownTimeout, remove ForceFlush lambda, document startup sequence** - `d70c66d` (feat)

## Files Created/Modified
- `src/Simetra/Lifecycle/GracefulShutdownService.cs` - Sealed IHostedService: 5-step shutdown with ExecuteWithBudget per step, FlushTelemetryAsync with protected budget
- `src/Simetra/Pipeline/IDeviceChannelManager.cs` - Added CompleteAll() and WaitForDrainAsync() to interface
- `src/Simetra/Pipeline/DeviceChannelManager.cs` - Implemented CompleteAll (Writer.Complete per channel) and WaitForDrainAsync (Reader.Completion await), added _logger field
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - Added AddSimetraLifecycle() method, 11-step startup + 5-step shutdown documentation, updated DI order comment
- `src/Simetra/Program.cs` - Added AddSimetraLifecycle() as last registration, removed ForceFlush lambda, added DI order comments

## Decisions Made
- [09-02]: GracefulShutdownService implements IHostedService (not BackgroundService) -- only needs StopAsync, no background work
- [09-02]: SnmpListenerService resolved via `GetServices<IHostedService>().OfType<SnmpListenerService>()` -- AddHostedService does not register concrete type in DI container
- [09-02]: K8sLeaseElection resolved via `GetService<K8sLeaseElection>()` directly -- registered as concrete singleton, returns null in local dev mode (AlwaysLeaderElection registered instead)
- [09-02]: FlushTelemetryAsync uses independent CancellationTokenSource (not linked to outer token) -- ensures telemetry flush gets full 5s budget regardless of host shutdown state
- [09-02]: ForceFlush consolidated from ApplicationStopping lambda into GracefulShutdownService -- single orchestrator for all shutdown steps
- [09-02]: DeviceChannelManager._logger field added (was only captured via closure for item-dropped callback) -- needed for CompleteAll/WaitForDrainAsync logging

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added _logger field to DeviceChannelManager**
- **Found during:** Task 1 (channel drain implementation)
- **Issue:** Logger parameter was only captured via closure in the constructor for item-dropped callbacks, not stored as a field. New CompleteAll/WaitForDrainAsync methods needed the logger for lifecycle operation logging.
- **Fix:** Added `private readonly ILogger<DeviceChannelManager> _logger` field and stored constructor parameter
- **Files modified:** src/Simetra/Pipeline/DeviceChannelManager.cs
- **Verification:** Build succeeds, both methods log correctly
- **Committed in:** e886e41 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Necessary for correct logging in new methods. No scope creep.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Complete LIFE-05 graceful shutdown with all 5 ordered steps and time budgets
- Full DI registration chain: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks -> Lifecycle
- Phase 9 (Health Probes + Lifecycle) complete -- ready for Phase 10 (End-to-End Validation)

---
*Phase: 09-health-probes-lifecycle*
*Completed: 2026-02-15*
