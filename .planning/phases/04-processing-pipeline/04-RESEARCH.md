# Phase 4: Processing Pipeline - Research

**Researched:** 2026-02-15
**Domain:** OTLP metric creation, in-memory state management, source-based routing
**Confidence:** HIGH

## Summary

Phase 4 implements Layer 4 of the Simetra pipeline: the processing layer that consumes `ExtractionResult` objects and routes them through two independent branches. Branch A creates OTLP-ready metrics using `System.Diagnostics.Metrics` with enforced base labels, and Branch B updates an in-memory State Vector. Source-based routing (`MetricPollSource.Module` vs `MetricPollSource.Configuration`) controls which branches activate for each data flow.

The standard approach uses `System.Diagnostics.Metrics` (built into .NET 9) with `IMeterFactory` from DI for metric creation. No external OpenTelemetry NuGet packages are needed in this phase -- the `System.Diagnostics.Metrics` namespace is part of the .NET runtime and provides `Counter<T>`, `Gauge<T>`, and the tag-based measurement API. OpenTelemetry exporter wiring happens in Phase 7. The State Vector is a simple `ConcurrentDictionary`-backed singleton service. The processing coordinator is a plain service (not a `BackgroundService`) invoked by upstream consumers (channel readers in Phase 5, poll jobs in Phase 6).

**Primary recommendation:** Use `System.Diagnostics.Metrics` with `IMeterFactory` for metric instrument creation, `ConcurrentDictionary` for the State Vector, and a coordinator service with independent try/catch blocks for branch isolation.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Diagnostics.Metrics | Built-in (.NET 9) | Metric instrument creation (Counter, Gauge) | Microsoft's official metrics API, integrates with OpenTelemetry via MeterProvider |
| System.Collections.Concurrent | Built-in (.NET 9) | ConcurrentDictionary for State Vector | Thread-safe dictionary for multi-producer state updates |
| Microsoft.Extensions.DependencyInjection | Built-in (.NET 9) | IMeterFactory, service registration | Standard DI pattern already used by the project |
| Microsoft.Extensions.Options | Built-in (.NET 9) | IOptions<SiteOptions> for base labels | Already used throughout Simetra for config access |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Logging | Built-in (.NET 9) | ILogger for processing diagnostics | Error logging in branch failures |
| System.Diagnostics.TagList | Built-in (.NET 9) | Efficient tag passing for >3 tags | When base labels + dynamic labels exceed 3 KVPs |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Diagnostics.Metrics | OpenTelemetry.Api direct | Unnecessary dependency; System.Diagnostics.Metrics IS the .NET implementation that OpenTelemetry hooks into |
| ConcurrentDictionary | Channel<T> for state updates | Overengineered; State Vector is simple last-write-wins, no ordering needed |
| Coordinator service | Dataflow blocks (TPL Dataflow) | Overengineered; two branches with independent try/catch is simpler |

### No Additional NuGet Packages Required

Phase 4 uses only built-in .NET 9 APIs. The OpenTelemetry OTLP exporter packages (`OpenTelemetry.Exporter.OpenTelemetryProtocol`, currently at v1.15.0) are deferred to Phase 7 (Telemetry Integration). The `System.Diagnostics.Metrics` API creates instruments that OpenTelemetry's `MeterProvider` will later collect -- the decoupling is by design.

## Architecture Patterns

### Recommended Project Structure
```
src/Simetra/
  Pipeline/
    IMetricFactory.cs           # Interface for metric creation (Branch A)
    MetricFactory.cs            # Implementation with base label enforcement
    IStateVectorService.cs      # Interface for State Vector (Branch B)
    StateVectorService.cs       # ConcurrentDictionary-backed implementation
    StateVectorEntry.cs         # Entry record: domain data + timestamp + correlationId
    IProcessingCoordinator.cs   # Interface for routing + branch orchestration
    ProcessingCoordinator.cs    # Source-based routing, independent branch execution
```

### Pattern 1: Coordinator Service (Processing Entry Point)
**What:** A single `IProcessingCoordinator` service that accepts an `ExtractionResult`, device context, and correlation context, then routes to Branch A and/or Branch B based on the `Source` field from the `PollDefinitionDto`.
**When to use:** Every time extracted data needs processing (channel consumer reads, poll job completions).
**Example:**
```csharp
// Source: Project architecture (Layer 4 processing)
public interface IProcessingCoordinator
{
    /// <summary>
    /// Processes an extraction result through Branch A (metrics) and optionally
    /// Branch B (State Vector), based on source routing rules.
    /// </summary>
    void Process(ExtractionResult result, DeviceInfo device, string correlationId);
}
```

### Pattern 2: Independent Branch Execution (PROC-08)
**What:** Branch A and Branch B execute in independent try/catch blocks. A failure in one does not prevent the other from executing.
**When to use:** Always -- this is a hard requirement (PROC-08).
**Example:**
```csharp
// Source: PROC-08 requirement
public void Process(ExtractionResult result, DeviceInfo device, string correlationId)
{
    // Branch A: Always runs (both Module and Configuration sources)
    try
    {
        _metricFactory.RecordMetrics(result, device);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Branch A (metrics) failed for {MetricName} on {DeviceName}",
            result.Definition.MetricName, device.Name);
    }

    // Branch B: Only runs for Source=Module
    if (result.Definition.Source == MetricPollSource.Module)
    {
        try
        {
            _stateVector.Update(device.Name, result.Definition.MetricName, result, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Branch B (state vector) failed for {MetricName} on {DeviceName}",
                result.Definition.MetricName, device.Name);
        }
    }
}
```

### Pattern 3: IMetricFactory with Enforced Base Labels (PROC-03)
**What:** `IMetricFactory` always attaches four base labels (site, device_name, device_ip, device_type) to every metric. Callers cannot omit or rename these labels. Additional labels from `Role:Label` OIDs are merged in.
**When to use:** Every metric recording operation.
**Example:**
```csharp
// Source: PROC-03 requirement + System.Diagnostics.Metrics official docs
public void RecordMetrics(ExtractionResult result, DeviceInfo device)
{
    var definition = result.Definition;

    foreach (var (propertyName, value) in result.Metrics)
    {
        var metricName = $"{definition.MetricName}_{propertyName}";

        // Build tag list: base labels + Role:Label values
        var tags = new TagList
        {
            { "site", _siteOptions.Name },
            { "device_name", device.Name },
            { "device_ip", device.IpAddress },
            { "device_type", device.DeviceType }
        };

        // Add dynamic labels from Role:Label OIDs (PROC-04)
        foreach (var (labelName, labelValue) in result.Labels)
        {
            tags.Add(labelName, labelValue);
        }

        // Record the metric value
        var instrument = GetOrCreateInstrument(metricName, definition.MetricType);
        RecordValue(instrument, value, tags);
    }
}
```

### Pattern 4: State Vector as ConcurrentDictionary Singleton (PROC-07)
**What:** A `ConcurrentDictionary<string, StateVectorEntry>` keyed by a composite key (device name + definition metric name). Each entry stores the last-known `ExtractionResult`, timestamp, and correlationId. No persistence, no TTL.
**When to use:** State Vector updates from Source=Module flows only.
**Example:**
```csharp
// Source: PROC-05, PROC-07 requirements
public sealed class StateVectorEntry
{
    public required ExtractionResult Result { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string CorrelationId { get; init; }
}
```

### Pattern 5: Dynamic Instrument Creation and Caching
**What:** Metric instruments (`Gauge<long>` or `Counter<long>`) are created on first use via `IMeterFactory` and cached in a `ConcurrentDictionary<string, object>` by metric name. This avoids re-creating instruments on every measurement.
**When to use:** Every metric recording operation uses cached instruments.
**Example:**
```csharp
// Source: Microsoft Learn - Creating Metrics (.NET)
private readonly ConcurrentDictionary<string, object> _instruments = new();
private readonly Meter _meter;

public MetricFactory(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions)
{
    _meter = meterFactory.Create("Simetra.Metrics");
    _siteOptions = siteOptions.Value;
}

private object GetOrCreateInstrument(string metricName, MetricType metricType)
{
    return _instruments.GetOrAdd(metricName, name => metricType switch
    {
        MetricType.Gauge => _meter.CreateGauge<long>(name),
        MetricType.Counter => _meter.CreateCounter<long>(name),
        _ => throw new ArgumentOutOfRangeException(nameof(metricType))
    });
}
```

### Anti-Patterns to Avoid
- **Creating new Meter/Instrument per measurement:** Each `CreateGauge`/`CreateCounter` call creates a new instrument. These MUST be cached and reused. Creating instruments per call will leak memory.
- **Blocking Branch B on Branch A completion:** Branches must be independent (PROC-08). Do NOT use await/continuation patterns that chain them.
- **Persisting State Vector:** PROC-07 explicitly forbids persistence. No serialization, no file writes, no database.
- **Using ObservableGauge when Gauge is available:** .NET 9 has `Gauge<T>` (not just `ObservableGauge`). Use `Gauge<T>` with `Record()` for push-based metrics. `ObservableGauge` requires callbacks and is wrong for this use case.
- **Putting OTLP exporter logic in MetricFactory:** The `MetricFactory` records measurements. The OTLP exporter (Phase 7) collects from the `MeterProvider`. These are decoupled by design.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe metric recording | Custom locking around metric writes | System.Diagnostics.Metrics instruments | Counter.Add() and Gauge.Record() are documented as thread-safe; <10ns per call with no listeners |
| Thread-safe state dictionary | Manual lock()/ReaderWriterLockSlim | ConcurrentDictionary<K,V> | Fine-grained locking, well-tested, handles concurrent reads/writes |
| Metric tag construction | String concatenation or custom tag builders | TagList struct | Stack-allocated for <=8 tags, avoids heap allocation on hot path |
| Meter lifecycle management | Manual Meter creation and disposal | IMeterFactory from DI | Automatically registered in .NET 8+, integrates with OpenTelemetry, handles disposal |
| Instrument caching | Dictionary with manual locking | ConcurrentDictionary.GetOrAdd | Atomic get-or-add, no double-creation race conditions |

**Key insight:** The `System.Diagnostics.Metrics` API is purpose-built for this exact scenario. It handles thread safety, tag aggregation, and instrument lifecycle. Custom metric recording infrastructure would be strictly worse.

## Common Pitfalls

### Pitfall 1: Creating Instruments Per Measurement
**What goes wrong:** Calling `meter.CreateGauge<long>(name)` every time a metric needs recording. Each call creates a new instrument object. The Meter holds references to all created instruments internally.
**Why it happens:** Developers treat `CreateGauge`/`CreateCounter` like a factory that returns existing instances. It does not -- it always creates new ones.
**How to avoid:** Cache instruments in a `ConcurrentDictionary<string, object>` keyed by metric name. Use `GetOrAdd` for thread-safe lazy creation.
**Warning signs:** Growing memory usage over time, instrument count in diagnostics increasing without bound.

### Pitfall 2: Tag Cardinality Explosion
**What goes wrong:** Adding high-cardinality tags (like correlationId, timestamps, or unbounded string values) to metrics causes the metrics backend to allocate storage per unique tag combination.
**Why it happens:** Including data that changes per-measurement (like correlationId) as a metric tag.
**How to avoid:** Base labels (site, device_name, device_ip, device_type) are low-cardinality by nature (~5 devices, 1 site). Role:Label values should also be low-cardinality (enum-mapped strings). Do NOT add correlationId, timestamps, or raw OID values as metric tags. CorrelationId goes on the State Vector entry, not on metrics.
**Warning signs:** Grafana dashboards with thousands of series, high memory usage in OTLP collector.

### Pitfall 3: Coupling Metric Creation to OTLP Export
**What goes wrong:** Importing OpenTelemetry exporter packages in Phase 4 and trying to configure the full export pipeline alongside metric creation.
**Why it happens:** Developers think metrics need an exporter to "work." They don't -- `System.Diagnostics.Metrics` records measurements independently of any collector.
**How to avoid:** Phase 4 creates instruments and records measurements using `System.Diagnostics.Metrics`. Phase 7 wires up `MeterProvider` with OTLP exporter to collect those measurements. The decoupling is architectural.
**Warning signs:** Phase 4 adding OpenTelemetry NuGet packages, trying to configure MeterProvider.

### Pitfall 4: State Vector Key Design
**What goes wrong:** Using only device name as the State Vector key. A device can have multiple PollDefinitions (multiple metric names), each producing different ExtractionResults.
**Why it happens:** Thinking of State Vector as "one entry per device" when it's actually "one entry per device+definition."
**How to avoid:** Use a composite key: `$"{deviceName}:{metricName}"` or a dedicated `StateVectorKey` record.
**Warning signs:** Later State Vector entries overwriting earlier ones from the same device but different definitions.

### Pitfall 5: TagList vs KeyValuePair Overloads
**What goes wrong:** Using the `params KeyValuePair<string, object?>[]` overload with more than 3 tags causes heap allocation per call. With 4 base labels + dynamic labels, this will always exceed 3.
**Why it happens:** The convenience overloads with 1-3 individual KeyValuePair parameters are allocation-free, but adding more tags requires the array overload.
**How to avoid:** Use `TagList` (a struct) for all metric recordings. `TagList` is stack-allocated for up to 8 tags and avoids heap allocation on the hot path. Since we always have at least 4 base labels, `TagList` is the correct choice.
**Warning signs:** GC pressure in metric recording hot path, profiler showing allocations in `Add`/`Record` calls.

### Pitfall 6: Synchronous Processing in Coordinator
**What goes wrong:** Making the coordinator async when neither branch requires async I/O. Adding unnecessary Task overhead.
**Why it happens:** Assuming everything in a pipeline should be async.
**How to avoid:** Both branches are synchronous operations: `Gauge.Record()` and `ConcurrentDictionary.AddOrUpdate()` are synchronous. The coordinator method should be `void Process(...)`, not `Task ProcessAsync(...)`. Async is only needed if the exporter or persistence layer requires it (they don't in Phase 4).
**Warning signs:** Unnecessary async state machines, `Task.CompletedTask` returns.

## Code Examples

Verified patterns from official sources:

### Creating a Meter via IMeterFactory (DI-aware)
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation
// IMeterFactory is automatically registered in .NET 8+ hosts
public class MetricFactory : IMetricFactory
{
    private readonly Meter _meter;

    public MetricFactory(IMeterFactory meterFactory)
    {
        // Create a named Meter via the factory -- lifecycle managed by DI
        _meter = meterFactory.Create("Simetra.Metrics");
    }
}
```

### Recording a Gauge Measurement with Tags
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.gauge-1
// Gauge<T>.Record() sets the current value (non-additive, last-write-wins)
var gauge = _meter.CreateGauge<long>("simetra_cpu_utilization");

var tags = new TagList
{
    { "site", "site-nyc-01" },
    { "device_name", "router-core-1" },
    { "device_ip", "10.0.1.1" },
    { "device_type", "router" },
    { "interface_name", "ge0/1" }  // Role:Label value
};

gauge.Record(85, tags);
```

### Recording a Counter Measurement with Tags
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation
// Counter<T>.Add() increments a monotonically increasing value
var counter = _meter.CreateCounter<long>("simetra_bytes_transferred");

var tags = new TagList
{
    { "site", "site-nyc-01" },
    { "device_name", "router-core-1" },
    { "device_ip", "10.0.1.1" },
    { "device_type", "router" }
};

counter.Add(42567, tags);
```

### ConcurrentDictionary State Vector Update
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/how-to-add-and-remove-items
// AddOrUpdate is atomic: adds if key doesn't exist, updates if it does
_entries.AddOrUpdate(
    key: $"{deviceName}:{metricName}",
    addValue: new StateVectorEntry
    {
        Result = result,
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = correlationId
    },
    updateValueFactory: (_, _) => new StateVectorEntry
    {
        Result = result,
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = correlationId
    });
```

### Metric Name Construction (PROC-01)
```csharp
// Source: PROC-01 requirement -- {MetricName}_{Property}
// ExtractionResult.Metrics keys are PropertyName values from OidEntryDto
// ExtractionResult.Definition.MetricName is the metric prefix

foreach (var (propertyName, value) in result.Metrics)
{
    // Example: "simetra_cpu" + "_" + "utilization" = "simetra_cpu_utilization"
    var fullMetricName = $"{result.Definition.MetricName}_{propertyName}";
    var instrument = GetOrCreateInstrument(fullMetricName, result.Definition.MetricType);
    // ...
}
```

### Source-Based Routing (PROC-06)
```csharp
// Source: PROC-06 requirement
// Module -> Branch A (metrics) + Branch B (State Vector)
// Configuration -> Branch A (metrics) only

var source = result.Definition.Source;

// Branch A: Always
RecordMetrics(result, device);

// Branch B: Module only
if (source == MetricPollSource.Module)
{
    UpdateStateVector(device.Name, result, correlationId);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ObservableGauge (callback-based) | Gauge<T>.Record() (push-based) | .NET 9 / DiagnosticSource 9.0 | Can push values directly instead of requiring observable callbacks |
| EventCounters | System.Diagnostics.Metrics | .NET 6+ | Modern API designed for OpenTelemetry integration |
| Static Meter instances | IMeterFactory from DI | .NET 8 | Proper DI integration, testable, isolated between service collections |
| params KVP[] for tags | TagList struct | .NET 8 | Stack-allocated for <=8 tags, avoids heap allocation |

**Deprecated/outdated:**
- **EventCounters:** Legacy .NET metrics API. Do not use -- `System.Diagnostics.Metrics` is the replacement.
- **Static `new Meter(...)` in DI-aware code:** Use `IMeterFactory.Create()` instead. Static meters are not DI-aware and cannot be properly tested or isolated.
- **ObservableGauge for push-based values:** Use `Gauge<T>` (available in .NET 9). ObservableGauge requires a callback and is designed for pull-based collection, not push-based recording.

## Open Questions

Things that couldn't be fully resolved:

1. **PROC-02: Leader-only OTLP gating**
   - What we know: The requirement says "Branch A sends metrics to OTLP (leader only, gated by role)." Phase 8 implements leader election, Phase 7 implements role-gated exporters.
   - What's unclear: Whether Phase 4 needs ANY awareness of leader/follower role, or if the gating happens entirely at the exporter level.
   - Recommendation: Phase 4 should record metrics unconditionally. Leader gating happens at the MeterProvider exporter level in Phase 7/8. The architecture document confirms this: "Branch A: Create metric -> send to OTLP (always, leader-only gating at exporter level)." This means Phase 4 MetricFactory records all metrics regardless of role. **Confidence: HIGH** -- architecture doc is explicit.

2. **State Vector entry key granularity**
   - What we know: State Vector stores "last-known domain object with timestamp and correlationId per device/definition." The ARCHITECTURE.md says "One entry per registered device/tenant."
   - What's unclear: Whether "per device/definition" means composite key `device+metricName` or just `device`. The ARCHITECTURE.md mentions "One entry per registered device/tenant" which could mean per-device only.
   - Recommendation: Use composite key `{deviceName}:{metricName}` to support multiple definitions per device. This is more granular but correct -- a device can have multiple Source=Module definitions (e.g., heartbeat trap, state poll). Each should update independently. The planner should make this decision explicit.

3. **Whether IMetricFactory needs an async API**
   - What we know: `Gauge<T>.Record()` and `Counter<T>.Add()` are synchronous. `ConcurrentDictionary.AddOrUpdate()` is synchronous.
   - What's unclear: Whether future phases will need async processing.
   - Recommendation: Keep synchronous. Both branches are CPU-bound, in-memory operations. No I/O. If async is needed later (unlikely for metric recording), it can be added without breaking the interface.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn - Creating Metrics (.NET)](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) - Complete guide on System.Diagnostics.Metrics, IMeterFactory, Counter, Gauge, TagList, multi-dimensional metrics
- [Microsoft Learn - Gauge<T> Class API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.gauge-1?view=net-9.0) - Gauge<T>.Record() overloads, type constraints, tag parameters
- [OpenTelemetry .NET - Metric Instruments](https://opentelemetry.io/docs/languages/dotnet/metrics/instruments/) - Counter, Gauge, UpDownCounter, Histogram creation and usage patterns
- [Microsoft Learn - ConcurrentDictionary](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/how-to-add-and-remove-items) - Thread-safe dictionary operations, AddOrUpdate pattern
- Existing codebase analysis (ExtractionResult, PollDefinitionDto, DeviceInfo, MetricPollSource, SiteOptions, OidRole, MetricType)

### Secondary (MEDIUM confidence)
- [OpenTelemetry .NET GitHub - Metrics README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md) - MeterProvider integration with System.Diagnostics.Metrics
- [OpenTelemetry .NET - Adding default tags discussion](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/5669) - Default tags are NOT supported at instrument level; must be per-measurement via TagList

### Tertiary (LOW confidence)
- None -- all findings verified with official sources.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - System.Diagnostics.Metrics is built-in .NET 9, verified via official Microsoft docs
- Architecture: HIGH - Branch A/B pattern, source-based routing, and State Vector design all derived from existing project architecture docs and PROC requirements
- Pitfalls: HIGH - Instrument caching, tag cardinality, TagList usage all documented in official .NET metrics best practices
- Code examples: HIGH - All examples verified against Microsoft Learn documentation for .NET 9

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (30 days -- System.Diagnostics.Metrics is stable, unlikely to change)
