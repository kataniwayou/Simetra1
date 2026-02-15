# Phase 10 Plan 01: Extraction Pipeline Component Unit Tests Summary

Pipeline component tests covering TrapFilter OID matching, DeviceRegistry IP normalization, CorrelationService state management, DeviceChannelManager DropOldest backpressure, TrapPipelineBuilder middleware composition, and PollDefinitionDto model conversion with defensive copies.

## Completed Tasks

| # | Task | Commit | Key Files |
|---|------|--------|-----------|
| 1 | TrapFilter, DeviceRegistry, and PollDefinitionDto tests | ae1f115 | Pipeline/TrapFilterTests.cs, Pipeline/DeviceRegistryTests.cs, Models/PollDefinitionDtoTests.cs |
| 2 | CorrelationService, DeviceChannelManager, and TrapPipelineBuilder tests | ffb7764 | Pipeline/CorrelationServiceTests.cs, Pipeline/DeviceChannelManagerTests.cs, Pipeline/TrapPipelineBuilderTests.cs |

## Test Coverage Added

| Component | Tests | Coverage Areas |
|-----------|-------|----------------|
| TrapFilter | 5 | OID match, no-match, multi-definition first-match, multi-varbind any-match, empty varbinds |
| DeviceRegistry | 6 | IP lookup, unknown IP, IPv6-mapped-to-IPv4 normalization, case-insensitive name, unknown name, module override on IP collision |
| PollDefinitionDto | 5 | Field preservation, OID conversion, defensive EnumMap copy, Source propagation, ReadOnly Oids |
| RotatingCorrelationService | 3 | Initial empty, set updates value, overwrite replaces previous |
| CorrelationIdMiddleware | 2 | Stamps correlationId on envelope, calls next delegate |
| DeviceChannelManager | 8 | Writer/reader access, unknown device KeyNotFoundException, DropOldest backpressure, CompleteAll lifecycle, module device channels, DeviceNames collection |
| TrapPipelineBuilder | 5 | Empty pipeline no-op, registration order execution, short-circuit skips downstream, ITrapMiddleware interface overload, terminal delegate no-op |

**Total new tests:** 34
**Total tests in suite:** 121 (all passing, zero regressions)

## TEST Coverage Map

| TEST ID | Description | Status |
|---------|-------------|--------|
| TEST-01 | SnmpExtractor (15 existing tests) | Confirmed passing |
| TEST-02 | PollDefinitionDto.FromOptions | 5 tests added |
| TEST-03 | Device filter + trap filter | 11 tests added (6 DeviceRegistry + 5 TrapFilter) |
| TEST-07 | Correlation ID lifecycle | 5 tests added (3 service + 2 middleware) |
| TEST-08 | Channel backpressure (DropOldest) | 8 tests added (including capacity-2 drop verification) |
| TEST-09 | Middleware chain composition | 5 tests added (order, short-circuit, terminal) |

## Decisions Made

- [10-01]: TrapFilter tests use real instances with mock logger (no interface mocking) -- validates actual HashSet OID matching logic
- [10-01]: DeviceRegistry tests use Options.Create() for direct instantiation without DI container
- [10-01]: DeviceChannelManager backpressure test uses capacity=2 with 3 writes to verify env1 dropped, env2+env3 readable
- [10-01]: CompleteAll test uses WriteAsync (not TryWrite) -- TryWrite returns false silently, WriteAsync throws ChannelClosedException
- [10-01]: TrapPipelineBuilder order test uses shared List<int> to track execution sequence [1, 2, 3]

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed namespace collision in DeviceChannelManagerTests**
- **Found during:** Task 2 compilation
- **Issue:** `Models.PollDefinitionDto` resolved to test namespace `Simetra.Tests.Models` instead of `Simetra.Models`
- **Fix:** Used unqualified `PollDefinitionDto` which resolves correctly via `using Simetra.Models;`
- **Files modified:** tests/Simetra.Tests/Pipeline/DeviceChannelManagerTests.cs

**2. [Rule 1 - Bug] Fixed FluentAssertions Equal overload mismatch in TrapPipelineBuilderTests**
- **Found during:** Task 2 compilation
- **Issue:** `Equal(1, 2, "because string")` -- string parameter interpreted as third int element, causing CS1503
- **Fix:** Changed to `Equal([1, 2])` collection expression syntax
- **Files modified:** tests/Simetra.Tests/Pipeline/TrapPipelineBuilderTests.cs

**3. [Rule 1 - Bug] Fixed CompleteAll test assertion method**
- **Found during:** Task 2 test execution
- **Issue:** `TryWrite()` returns false on completed channel instead of throwing `ChannelClosedException`
- **Fix:** Changed to `WriteAsync()` which correctly throws `ChannelClosedException`
- **Files modified:** tests/Simetra.Tests/Pipeline/DeviceChannelManagerTests.cs

## Duration

- Start: 2026-02-15T16:16:48Z
- End: 2026-02-15T16:21:44Z
- Duration: ~5 min
