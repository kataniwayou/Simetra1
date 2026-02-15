---
phase: 10-end-to-end-integration-testing
plan: 03
subsystem: operational-infrastructure-tests
tags: [health-checks, graceful-shutdown, role-gated-exporter, unit-tests, xunit, moq]
dependencies:
  requires:
    - "09-01: StartupHealthCheck, ReadinessHealthCheck implementations"
    - "09-02: GracefulShutdownService implementation"
    - "07-01: RoleGatedExporter implementation"
  provides:
    - "TEST-10: Health probe handler unit tests (startup + readiness)"
    - "TEST-11: Graceful shutdown ordering and resilience tests"
    - "TEST-12: Role-gated exporter delegation pattern tests"
  affects:
    - "10-04: Integration/E2E tests may depend on operational infrastructure correctness"
tech-stack:
  added: []
  patterns:
    - "TestExporter concrete test double for BaseExporter (Moq cannot override protected methods called through public API)"
    - "IServiceProvider mock with typeof() setup for GetService resolution"
key-files:
  created:
    - tests/Simetra.Tests/Health/StartupHealthCheckTests.cs
    - tests/Simetra.Tests/Health/ReadinessHealthCheckTests.cs
    - tests/Simetra.Tests/Lifecycle/GracefulShutdownServiceTests.cs
    - tests/Simetra.Tests/Telemetry/RoleGatedExporterTests.cs
  modified:
    - tests/Simetra.Tests/Pipeline/TrapPipelineBuilderTests.cs
    - tests/Simetra.Tests/Pipeline/DeviceChannelManagerTests.cs
decisions:
  - key: "TestExporter test double"
    choice: "Concrete sealed class extending BaseExporter<Activity> instead of Moq"
    reason: "Moq cannot easily override protected OnForceFlush/OnShutdown/Dispose that BaseExporter calls through public ForceFlush/Shutdown/Dispose API"
  - key: "IServiceProvider mocking for GracefulShutdownService"
    choice: "Mock<IServiceProvider> with typeof() Setup for each resolved type"
    reason: "GetService<T>() extension calls GetService(typeof(T)); GetServices<T>() calls GetService(typeof(IEnumerable<T>))"
metrics:
  duration: "4 min"
  completed: "2026-02-15"
  tests-added: 17
  total-tests: 137
---

# Phase 10 Plan 03: Operational Infrastructure Tests Summary

Unit tests for K8s health probes (startup, readiness), graceful shutdown service with time-budgeted steps, and role-gated OTLP exporter delegation pattern using Moq mocks and concrete test doubles.

## Tasks Completed

### Task 1: StartupHealthCheck and ReadinessHealthCheck tests
**Commit:** `1961dd0`

Created `tests/Simetra.Tests/Health/StartupHealthCheckTests.cs` with 3 tests:
- `CheckHealth_CorrelationIdSet_ReturnsHealthy` -- non-empty correlationId yields Healthy
- `CheckHealth_CorrelationIdEmpty_ReturnsUnhealthy` -- empty string yields Unhealthy
- `CheckHealth_CorrelationIdNull_ReturnsUnhealthy` -- null yields Unhealthy

Created `tests/Simetra.Tests/Health/ReadinessHealthCheckTests.cs` with 4 tests:
- `CheckHealth_ChannelsExistAndSchedulerRunning_ReturnsHealthy` -- both conditions met
- `CheckHealth_NoChannels_ReturnsUnhealthy` -- empty DeviceNames
- `CheckHealth_SchedulerNotStarted_ReturnsUnhealthy` -- IsStarted=false
- `CheckHealth_SchedulerShutdown_ReturnsUnhealthy` -- IsStarted=true + IsShutdown=true

### Task 2: GracefulShutdownService and RoleGatedExporter tests
**Commit:** `1ac6650`

Created `tests/Simetra.Tests/Lifecycle/GracefulShutdownServiceTests.cs` with 5 tests:
- `StopAsync_CallsSchedulerStandby` -- verifies Standby() invocation
- `StopAsync_CallsChannelCompleteAllAndDrain` -- verifies CompleteAll + WaitForDrainAsync
- `StopAsync_NullLeaseService_DoesNotThrow` -- local dev mode null K8sLeaseElection
- `StopAsync_TelemetryFlushRuns_EvenWhenSchedulerFails` -- resilience: scheduler throws, drain still runs
- `StartAsync_ReturnsCompletedTask` -- no-op startup

Created `tests/Simetra.Tests/Telemetry/RoleGatedExporterTests.cs` with 5 tests:
- `Constructor_NullInner_ThrowsArgumentNullException` -- null guard on inner exporter
- `Constructor_NullLeaderElection_ThrowsArgumentNullException` -- null guard on leader election
- `ForceFlush_DelegatesToInner` -- delegates through to inner exporter
- `Shutdown_DelegatesToInner` -- delegates through to inner exporter
- `Dispose_DisposesInner` -- disposes inner exporter

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed compilation errors in Plan 02 untracked test files**

- **Found during:** Task 1 (test compilation)
- **Issue:** `TrapPipelineBuilderTests.cs` had `order.Should().Equal([1, 2])` which caused CS1503 overload resolution error with FluentAssertions 7.2.0. `DeviceChannelManagerTests.cs` had `Simetra.Models.PollDefinitionDto` resolving to `Simetra.Tests.Models.PollDefinitionDto` (namespace shadowing from test project namespace).
- **Fix:** Changed `Equal([1, 2])` to `Equal(1, 2)` (params overload). Added `using Simetra.Models;` and used unqualified `PollDefinitionDto` type reference.
- **Files modified:** `TrapPipelineBuilderTests.cs`, `DeviceChannelManagerTests.cs`
- **Commit:** `1961dd0` (included with Task 1)

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet test tests/Simetra.Tests/` (all tests) | 137 passed, 0 failed |
| `--filter StartupHealthCheckTests` | 3 passed |
| `--filter ReadinessHealthCheckTests` | 4 passed |
| `--filter GracefulShutdownServiceTests` | 5 passed |
| `--filter RoleGatedExporterTests` | 5 passed |

## Test Coverage Summary

| Requirement | Tests | Status |
|-------------|-------|--------|
| TEST-10: Health probe handlers | 7 (3 startup + 4 readiness) | Covered |
| TEST-11: Graceful shutdown | 5 (standby, drain, null lease, resilience, no-op start) | Covered |
| TEST-12: Role-gated exporter | 5 (null guards, ForceFlush, Shutdown, Dispose) | Covered |

## Next Phase Readiness

Plan 03 complete. Plan 04 (heartbeat loopback integration test) can proceed. No blockers.
