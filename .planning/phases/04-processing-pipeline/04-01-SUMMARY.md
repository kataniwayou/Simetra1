---
phase: "04-processing-pipeline"
plan: "01"
subsystem: "processing-pipeline"
tags: ["metrics", "state-vector", "system-diagnostics", "concurrent-dictionary", "otlp"]

dependency_graph:
  requires: ["02-domain-models-extraction-engine"]
  provides: ["IMetricFactory", "MetricFactory", "IStateVectorService", "StateVectorService", "StateVectorEntry"]
  affects: ["04-02-processing-coordinator", "05-poll-scheduler", "09-health-observability"]

tech_stack:
  added: []
  patterns: ["instrument-caching-via-concurrent-dictionary", "taglist-for-base-plus-dynamic-labels", "composite-key-state-vector", "imetermeter-factory-di"]

key_files:
  created:
    - "src/Simetra/Pipeline/IMetricFactory.cs"
    - "src/Simetra/Pipeline/MetricFactory.cs"
    - "src/Simetra/Pipeline/IStateVectorService.cs"
    - "src/Simetra/Pipeline/StateVectorService.cs"
    - "src/Simetra/Pipeline/StateVectorEntry.cs"
  modified: []

decisions:
  - id: "04-01-01"
    decision: "System.Diagnostics using added for TagList -- not covered by System.Diagnostics.Metrics namespace"
    rationale: "TagList lives in System.Diagnostics, not System.Diagnostics.Metrics despite being part of the metrics pattern"
  - id: "04-01-02"
    decision: "StateVectorEntry as sealed class with required init properties (not record)"
    rationale: "Matches ExtractionResult pattern -- mutable construction via object initializer, immutable consumption"
  - id: "04-01-03"
    decision: "CreateEntry helper method in StateVectorService to avoid lambda duplication in AddOrUpdate"
    rationale: "Both add and update factories produce identical entries; extracted to private static method"

metrics:
  duration: "2m 36s"
  completed: "2026-02-15"
---

# Phase 4 Plan 1: MetricFactory and StateVector Summary

IMetricFactory (Branch A) creates {MetricName}_{Property} instruments via IMeterFactory DI with ConcurrentDictionary caching, recording Gauge/Counter measurements with TagList carrying 4 base labels plus dynamic Role:Label labels. StateVectorService (Branch B) stores ExtractionResult+timestamp+correlationId in ConcurrentDictionary keyed by "deviceName:metricName" with atomic AddOrUpdate.

## What Was Done

### Task 1: IMetricFactory and MetricFactory (Branch A)
- Created `IMetricFactory` interface with single `RecordMetrics(ExtractionResult, DeviceInfo)` method
- Created `MetricFactory` sealed class with constructor injection of `IMeterFactory`, `IOptions<SiteOptions>`, `ILogger<MetricFactory>`
- Meter created via `meterFactory.Create("Simetra.Metrics")` in constructor
- SiteOptions read once in constructor (not per-call)
- Instrument caching via `ConcurrentDictionary<string, object>` with `GetOrAdd`
- Metric names: `{MetricName}_{propertyName}` pattern
- TagList with 4 base labels: site, device_name, device_ip, device_type
- Dynamic labels from `result.Labels` appended to TagList
- Gauge<long>.Record() for gauges, Counter<long>.Add() for counters
- Per-metric try/catch prevents single failure from killing entire batch
- **Commit:** `b781490`

### Task 2: IStateVectorService, StateVectorEntry, StateVectorService (Branch B)
- Created `StateVectorEntry` sealed class with `required ExtractionResult Result`, `required DateTimeOffset Timestamp`, `required string CorrelationId`
- Created `IStateVectorService` interface with `Update`, `GetEntry`, `GetAllEntries`
- Created `StateVectorService` sealed class backed by `ConcurrentDictionary<string, StateVectorEntry>`
- Composite key: `"{deviceName}:{metricName}"` (device+definition, not just device)
- `AddOrUpdate` for atomic last-write-wins state updates
- `GetAllEntries` returns snapshot via `ReadOnlyDictionary` (point-in-time copy, not live view)
- No persistence, no TTL -- in-memory only
- Debug-level logging on state updates
- **Commit:** `6596b38`

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| 04-01-01 | Added `using System.Diagnostics` for TagList | TagList is in System.Diagnostics namespace, not System.Diagnostics.Metrics |
| 04-01-02 | StateVectorEntry as sealed class with required init properties | Matches ExtractionResult pattern from Phase 2 -- mutable construction, immutable consumption |
| 04-01-03 | CreateEntry helper method in StateVectorService | Avoids lambda duplication in AddOrUpdate -- both add and update produce identical entries |

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` zero errors | PASS |
| `dotnet build` zero warnings | PASS |
| No new NuGet packages | PASS (still only Lextm.SharpSnmpLib) |
| All 5 files created | PASS |
| MetricFactory uses IMeterFactory DI | PASS |
| Instruments cached in ConcurrentDictionary | PASS |
| TagList used for tags | PASS |
| StateVectorService uses composite key | PASS |
| All 60 existing tests pass | PASS |

## Anti-Patterns Avoided

- No per-measurement instrument creation (Pitfall 1) -- ConcurrentDictionary caching
- No correlationId as metric tag (Pitfall 2) -- high cardinality
- No OpenTelemetry package imports (Pitfall 3) -- pure System.Diagnostics.Metrics
- No ObservableGauge (Pitfall 4) -- Gauge<T>.Record() push-based (.NET 9)
- TagList struct used (Pitfall 5) -- efficient for 4+ tags
- Synchronous methods (Pitfall 6) -- Record() and Add() are sync

## Next Phase Readiness

Branch A (IMetricFactory) and Branch B (IStateVectorService) are independently functional. Plan 04-02 (ProcessingCoordinator) can now wire them together to route ExtractionResults to the appropriate branch based on poll definition source.
