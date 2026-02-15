# Phase 7: Telemetry Integration - Research

**Researched:** 2026-02-15
**Domain:** OpenTelemetry .NET -- OTLP exporters, runtime metrics, structured logging, distributed tracing, role-gated export
**Confidence:** HIGH

## Summary

Phase 7 wires OpenTelemetry into the existing Simetra pipeline, connecting the `System.Diagnostics.Metrics` instruments already created by `MetricFactory` (Phase 4) to an OTLP backend, adding .NET runtime metrics, structured log enrichment, and distributed tracing. The standard approach uses the `OpenTelemetry.Extensions.Hosting` package's `AddOpenTelemetry()` fluent API on `IServiceCollection`, which provides `WithTracing()`, `WithMetrics()`, and the logging bridge via `builder.Logging.AddOpenTelemetry()`. All three signals export to the same OTLP endpoint via `OpenTelemetry.Exporter.OpenTelemetryProtocol`.

The critical architectural constraint is that telemetry providers must be registered **first** in DI (disposed last) per success criterion 5 -- this means the `AddOpenTelemetry()` call must precede all existing `AddSimetraConfiguration()`, `AddDeviceModules()`, etc. calls in `Program.cs`. The role-gated exporter pattern (leader-only for metrics/traces, all-pods for logs) is designed in this phase but uses a stub `ILeaderElection` with `AlwaysLeaderElection` default -- Phase 8 provides the real implementation. The gating mechanism wraps `BaseExporter<T>` with a decorator that checks `IsLeader` on each `Export()` call.

**Primary recommendation:** Use `OpenTelemetry.Extensions.Hosting` 1.15.0 with per-signal OTLP exporter configuration (not `UseOtlpExporter` cross-cutting), register telemetry first in DI, create a `BaseProcessor<LogRecord>` for log enrichment (site, role, correlationId), and design a `RoleGatedExporter<T>` decorator wrapping `BaseExporter<T>` that Phase 8 activates.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| OpenTelemetry.Extensions.Hosting | 1.15.0 | `AddOpenTelemetry()` on `IServiceCollection`, lifecycle management of TracerProvider/MeterProvider | Official hosting integration. Manages provider lifecycle via DI disposal. Targets net8.0+. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.0 | OTLP export for metrics, traces, and logs | Unified OTLP exporter supporting gRPC (default) and HTTP/Protobuf. Single package for all three signals. |
| OpenTelemetry.Instrumentation.Runtime | 1.15.0 | .NET runtime metrics (CPU, memory, GC, thread pool) | Official runtime instrumentation. On .NET 9+, registers built-in System.Runtime meters automatically. |
| OpenTelemetry | 1.15.0 | Core SDK (BaseExporter, BaseProcessor, MeterProviderBuilder, etc.) | Transitive dependency. Provides `BaseExporter<T>` and `BaseProcessor<LogRecord>` needed for custom extensions. |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| OpenTelemetry.Instrumentation.Quartz | 1.12.0-beta.1 | Auto-instrument Quartz job executions as spans | Optional for Phase 7. Captures job execution timing. Beta but stable (part of opentelemetry-dotnet-contrib). Can be added now or deferred to Phase 10. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Per-signal `AddOtlpExporter()` | `UseOtlpExporter()` cross-cutting | `UseOtlpExporter()` is simpler but cannot be combined with per-signal exporter customization. Role-gated exporters require per-signal control -- metrics/traces need wrapping, logs do not. **Per-signal is required.** |
| `BaseProcessor<LogRecord>` for enrichment | `ILogger.BeginScope` in middleware | BeginScope only works within a request/scope context. Background jobs and startup code would miss enrichment. A processor intercepts ALL log records regardless of call context. **Processor is required for global enrichment.** |
| Custom `RoleGatedExporter<T>` decorator | Collector-side filtering | Collector-side filtering wastes network bandwidth sending metrics/traces from followers. Client-side gating is more efficient and matches the design doc. |
| `builder.Logging.AddConsole()` for EnableConsole | `OpenTelemetry.Exporter.Console` | The EnableConsole flag sends logs to stdout for container log capture. The standard .NET console logging provider (`AddConsole()`) is the correct choice -- the OTel Console exporter is a debugging tool that formats differently. |

**Installation:**
```bash
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.15.0
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.15.0
dotnet add package OpenTelemetry.Instrumentation.Runtime --version 1.15.0
```

## Architecture Patterns

### DI Registration Order (Updated)

The telemetry registration must come FIRST in `Program.cs`, before all other service registrations. This ensures the OpenTelemetry providers are disposed LAST during shutdown (DI disposes in reverse registration order), giving the telemetry flush the final word.

```
Program.cs registration order:
1. AddOpenTelemetry()          -- NEW: Telemetry providers (disposed last)
2. Logging configuration       -- NEW: AddOpenTelemetry log provider + conditional AddConsole
3. AddSimetraConfiguration()   -- Existing
4. AddDeviceModules()          -- Existing
5. AddSnmpPipeline()           -- Existing
6. AddProcessingPipeline()     -- Existing
7. AddScheduling()             -- Existing
8. AddHealthChecks()           -- Existing
```

### Pattern 1: OpenTelemetry Provider Setup

**What:** Register MeterProvider, TracerProvider, and LoggerProvider with OTLP exporters
**When to use:** At application startup, before all other service registrations

```csharp
// Source: OpenTelemetry .NET official docs + OTLP exporter README
var builder = WebApplication.CreateBuilder(args);

// Step 1: Telemetry providers registered FIRST (disposed last)
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "simetra-supervisor"))  // From OtlpOptions.ServiceName
    .WithMetrics(metrics => metrics
        .AddMeter("Simetra.Metrics")         // Must match MetricFactory's meter name
        .AddRuntimeInstrumentation()          // .NET runtime metrics (TELEM-01)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://localhost:4317");  // From OtlpOptions.Endpoint
        }))
    .WithTracing(tracing => tracing
        .AddSource("Simetra.Tracing")        // Custom ActivitySource name
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://localhost:4317");
        }));

// Logging: OTLP exporter on all pods + conditional console
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter(o =>
    {
        o.Endpoint = new Uri("http://localhost:4317");
    });
});
```

### Pattern 2: Log Enrichment Processor

**What:** A `BaseProcessor<LogRecord>` that adds site name, role, and correlationId to every log entry
**When to use:** Registered in the OpenTelemetry logging pipeline to enrich all logs globally

```csharp
// Custom processor for TELEM-03: structured logs include site, role, correlationId
public sealed class SimetraLogEnrichmentProcessor : BaseProcessor<LogRecord>
{
    private readonly ICorrelationService _correlationService;
    private readonly string _siteName;
    private readonly Func<string> _roleProvider;  // "leader" or "follower"

    public SimetraLogEnrichmentProcessor(
        ICorrelationService correlationService,
        string siteName,
        Func<string> roleProvider)
    {
        _correlationService = correlationService;
        _siteName = siteName;
        _roleProvider = roleProvider;
    }

    public override void OnEnd(LogRecord record)
    {
        // Add structured attributes to every log record
        var attributes = record.Attributes?.ToList()
            ?? new List<KeyValuePair<string, object?>>();

        attributes.Add(new KeyValuePair<string, object?>("site", _siteName));
        attributes.Add(new KeyValuePair<string, object?>("role", _roleProvider()));
        attributes.Add(new KeyValuePair<string, object?>(
            "correlationId", _correlationService.CurrentCorrelationId));

        record.Attributes = attributes;
    }
}
```

### Pattern 3: Role-Gated Exporter Decorator

**What:** A decorator wrapping `BaseExporter<T>` that conditionally exports based on leader role
**When to use:** Wraps metric and trace OTLP exporters; log exporter is NOT wrapped

```csharp
// Designed in Phase 7, wired by Phase 8 with real ILeaderElection
public sealed class RoleGatedExporter<T> : BaseExporter<T> where T : class
{
    private readonly BaseExporter<T> _inner;
    private readonly ILeaderElection _leaderElection;

    public RoleGatedExporter(BaseExporter<T> inner, ILeaderElection leaderElection)
    {
        _inner = inner;
        _leaderElection = leaderElection;
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        // Dynamic check on every export call (HA-07: role changes take effect immediately)
        if (!_leaderElection.IsLeader)
            return ExportResult.Success;  // Silently drop -- follower

        return _inner.Export(batch);
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
        => _inner.ForceFlush(timeoutMilliseconds);

    protected override bool OnShutdown(int timeoutMilliseconds)
        => _inner.Shutdown(timeoutMilliseconds);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
```

### Pattern 4: ILeaderElection Stub Interface

**What:** Interface for leader election with a default always-leader implementation
**When to use:** Phase 7 defines the interface; Phase 8 provides K8s implementation

```csharp
// Interface -- Phase 7 creates, Phase 8 implements K8sLeaseElection
public interface ILeaderElection
{
    bool IsLeader { get; }
    string CurrentRole { get; }  // "leader" or "follower"
}

// Default implementation for local dev -- always leader
public sealed class AlwaysLeaderElection : ILeaderElection
{
    public bool IsLeader => true;
    public string CurrentRole => "leader";
}
```

### Pattern 5: Console Logging Toggle

**What:** Conditionally add the .NET console logging provider based on `EnableConsole` config
**When to use:** During logging configuration, before app build

```csharp
// TELEM-06: Console logging configurable via EnableConsole flag
var loggingOptions = new LoggingOptions();
builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

if (loggingOptions.EnableConsole)
{
    builder.Logging.AddConsole();  // Standard .NET console provider -- sends to stdout
}
else
{
    // By default, WebApplication.CreateBuilder adds console logging.
    // If EnableConsole is false, we need to clear the default console provider.
    builder.Logging.ClearProviders();
    // Re-add OpenTelemetry logging (it was added above)
    // This is handled by the order of operations -- see implementation notes
}
```

**Important:** `WebApplication.CreateBuilder()` adds console logging by default. If `EnableConsole = false`, the default console provider must be removed. The cleanest approach is to call `builder.Logging.ClearProviders()` early, then selectively add back only the providers needed (OpenTelemetry always, Console conditionally).

### Pattern 6: Telemetry Shutdown with ForceFlush

**What:** Explicit ForceFlush during graceful shutdown to prevent final metric loss
**When to use:** In the shutdown sequence (Phase 9 step 5), with its own time budget

```csharp
// Success criterion 5: ForceFlush called during shutdown
// The AddOpenTelemetry() hosting extension disposes providers on host shutdown.
// However, for explicit time-budgeted flush in the shutdown sequence:
app.Lifetime.ApplicationStopping.Register(() =>
{
    var meterProvider = app.Services.GetService<MeterProvider>();
    var tracerProvider = app.Services.GetService<TracerProvider>();

    // ForceFlush with timeout budget (~5s as specified in design doc)
    meterProvider?.ForceFlush(timeoutMilliseconds: 5000);
    tracerProvider?.ForceFlush(timeoutMilliseconds: 5000);
    // LoggerProvider flush handled by the host's logging infrastructure disposal
});
```

### Recommended Project Structure (Telemetry directory)

```
src/Simetra/Telemetry/
    ILeaderElection.cs              # Interface for role checking
    AlwaysLeaderElection.cs         # Default: always leader (local dev)
    RoleGatedExporter.cs            # BaseExporter<T> decorator
    SimetraLogEnrichmentProcessor.cs # BaseProcessor<LogRecord> for log enrichment
```

### Anti-Patterns to Avoid

- **Wrapping the log exporter with role gating:** Logs must flow from ALL pods (TELEM-04). Only metrics and traces are role-gated.
- **Using `UseOtlpExporter()` cross-cutting extension:** Cannot customize per-signal exporter behavior. Role gating requires wrapping metric/trace exporters individually.
- **Creating a new Meter in the telemetry setup:** MetricFactory already creates a Meter named "Simetra.Metrics" via `IMeterFactory`. The MeterProvider must `.AddMeter("Simetra.Metrics")` to listen to it -- do NOT create a second meter.
- **Adding EnumMap values as metric tags:** EnumMap metadata is stored on ExtractionResult for Grafana. Raw SNMP integers are the metric values (TELEM-07). MetricFactory already enforces this -- no changes needed.
- **Registering telemetry after other services:** Telemetry providers must be registered FIRST in DI so they are disposed LAST during shutdown.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| .NET runtime metrics collection | Custom CPU/memory polling | `OpenTelemetry.Instrumentation.Runtime` 1.15.0 | Collects GC, thread pool, JIT, memory, CPU metrics automatically. On .NET 9+, uses built-in System.Runtime meters. |
| OTLP wire protocol | Custom gRPC/HTTP serialization | `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.0 | Implements full OTLP spec with batch processing, retry, and connection management. |
| Log enrichment pipeline | Custom ILoggerProvider | `BaseProcessor<LogRecord>` in OpenTelemetry SDK | Processors intercept all log records regardless of logging scope or call context. Handles threading, ordering, and batching. |
| Metric provider lifecycle | Manual MeterProvider creation/disposal | `OpenTelemetry.Extensions.Hosting` 1.15.0 | Ties provider lifecycle to DI container. Auto-disposes on shutdown. Provides `ForceFlush` on hosted providers. |
| Console log output | Custom stdout writer | `builder.Logging.AddConsole()` (built-in) | Standard .NET console provider handles formatting, coloring, thread safety. |

**Key insight:** The OpenTelemetry .NET SDK is designed around extension points (`BaseExporter<T>`, `BaseProcessor<LogRecord>`) rather than configuration switches. The role-gated exporter and log enrichment patterns use these official extension points -- they are NOT hacks.

## Common Pitfalls

### Pitfall 1: Meter Name Mismatch
**What goes wrong:** Metrics don't appear in OTLP output despite MetricFactory recording them.
**Why it happens:** `MeterProviderBuilder.AddMeter("X")` must match the exact name passed to `IMeterFactory.Create("X")` in MetricFactory. The name is case-insensitive but must be identical.
**How to avoid:** MetricFactory uses `meterFactory.Create("Simetra.Metrics")`. The telemetry setup must call `.AddMeter("Simetra.Metrics")`. Extract this as a constant.
**Warning signs:** Metrics visible in debug output but not reaching OTLP collector.

### Pitfall 2: WebApplication.CreateBuilder Default Providers
**What goes wrong:** Console logs appear even when `EnableConsole = false`.
**Why it happens:** `WebApplication.CreateBuilder()` adds Console, Debug, and EventSource logging providers by default. Simply not calling `AddConsole()` is insufficient.
**How to avoid:** Call `builder.Logging.ClearProviders()` first, then add back only the OpenTelemetry provider (always) and Console provider (conditionally).
**Warning signs:** Log output to stdout when EnableConsole is false.

### Pitfall 3: ForceFlush Timing During Shutdown
**What goes wrong:** Final metrics/traces lost during pod termination.
**Why it happens:** The OTLP exporter uses batch processing with a scheduled delay. If the host shuts down before the batch interval fires, queued telemetry is lost. The `Dispose()` on providers calls `Shutdown()` which should flush, but rapid termination can cut this short.
**How to avoid:** Explicitly call `ForceFlush()` with a timeout budget before the host disposes providers. Register on `ApplicationStopping` event (fires before disposal). Phase 9 implements this as step 5 of the shutdown sequence with its own ~5s budget.
**Warning signs:** Shutdown diagnostics missing from OTLP backend.

### Pitfall 4: Role-Gated Exporter Returns Failure
**What goes wrong:** If the `RoleGatedExporter` returns `ExportResult.Failure` when not leader, the SDK may log errors or trigger retry logic.
**Why it happens:** The SDK interprets `Failure` as a transient error and may retry.
**How to avoid:** Return `ExportResult.Success` when not leader. The data is intentionally dropped -- this is not an error.
**Warning signs:** Error logs about export failures on follower pods.

### Pitfall 5: LogRecord.Attributes Modification in Processor
**What goes wrong:** NullReferenceException or missing attributes in enrichment processor.
**Why it happens:** `LogRecord.Attributes` can be null if no structured log parameters were provided. Also, the attributes collection returned is not always mutable.
**How to avoid:** Always null-check `record.Attributes`, create a new list from existing attributes, append custom attributes, and reassign. Use `?.ToList() ?? new List<>()` pattern.
**Warning signs:** NullReferenceException in log enrichment processor.

### Pitfall 6: Duplicate Service Registration
**What goes wrong:** `IMeterFactory` already registered by `builder.Services.AddMetrics()` (implicit in .NET 9 Web SDK). Calling it again or conflicting with OpenTelemetry's registration causes duplicate metrics.
**Why it happens:** `WebApplication.CreateBuilder()` in .NET 9 already calls `AddMetrics()` which registers `IMeterFactory`. `AddOpenTelemetry()` integrates with this existing registration.
**How to avoid:** Do not call `AddMetrics()` manually. `AddOpenTelemetry().WithMetrics()` hooks into the existing `IMeterFactory` registration. MetricFactory already injects `IMeterFactory` -- no changes needed.
**Warning signs:** Duplicate metric registrations or unexpected meter behavior.

## Code Examples

### Complete AddTelemetry Extension Method

```csharp
// Source: Verified against OpenTelemetry .NET 1.15.0 official docs and OTLP exporter README
public static class TelemetryExtensions
{
    public const string MeterName = "Simetra.Metrics";  // Must match MetricFactory
    public const string TracingSourceName = "Simetra.Tracing";

    public static IHostApplicationBuilder AddSimetraTelemetry(
        this IHostApplicationBuilder builder)
    {
        // Read config for OTLP endpoint and service name
        var otlpOptions = new OtlpOptions();
        builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

        var loggingOptions = new LoggingOptions();
        builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

        // Register ILeaderElection stub (Phase 8 replaces with real implementation)
        builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();

        // --- OpenTelemetry providers ---
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: otlpOptions.ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(MeterName)              // SNMP-derived metrics from MetricFactory
                .AddRuntimeInstrumentation()       // TELEM-01: .NET runtime metrics
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpOptions.Endpoint);
                }))
            .WithTracing(tracing => tracing
                .AddSource(TracingSourceName)       // TELEM-05: Distributed tracing
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpOptions.Endpoint);
                }));

        // --- Logging ---
        // Clear default providers (removes Console, Debug, EventSource)
        builder.Logging.ClearProviders();

        // TELEM-06: Console logging conditionally re-added
        if (loggingOptions.EnableConsole)
        {
            builder.Logging.AddConsole();
        }

        // TELEM-03/04: OTLP log exporter on ALL pods (not role-gated)
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;

            logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName: otlpOptions.ServiceName));

            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpOptions.Endpoint);
            });

            // TELEM-03: Enrich all logs with site, role, correlationId
            // Processor registered via factory to resolve DI services
            logging.AddProcessor(sp =>
            {
                var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
                var correlationService = sp.GetRequiredService<ICorrelationService>();
                var leaderElection = sp.GetRequiredService<ILeaderElection>();

                return new SimetraLogEnrichmentProcessor(
                    correlationService,
                    siteOptions.Name,
                    () => leaderElection.CurrentRole);
            });
        });

        return builder;
    }
}
```

**Note on AddProcessor with factory:** The `AddProcessor(Func<IServiceProvider, BaseProcessor<LogRecord>>)` overload may not exist on `OpenTelemetryLoggerOptions`. If not available, the processor must be constructed after DI is available, or use a service-locator pattern within the processor itself. An alternative is to register the processor as a singleton and resolve it. This needs validation during implementation.

### TELEM-07 Verification: EnumMap Values Never Reported

```csharp
// Already enforced by MetricFactory (Phase 4)
// MetricFactory.RecordMetrics() iterates result.Metrics (raw SNMP integers)
// It does NOT read result.EnumMapMetadata
// No changes needed in Phase 7 -- this is inherent in the existing design
//
// The MeterProvider's AddMeter("Simetra.Metrics") listens to the same Meter
// that MetricFactory writes raw integers to. EnumMap values never enter
// the metrics pipeline.
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-signal provider construction (`Sdk.CreateTracerProviderBuilder()`) | `AddOpenTelemetry()` hosting extension | OTel .NET 1.4.0+ | Unified DI lifecycle. Providers auto-disposed with host. |
| Separate OTLP packages per signal | Single `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTel .NET 1.7.0+ | One package exports all three signals. |
| `UseOtlpExporter()` cross-cutting | Per-signal `AddOtlpExporter()` | Both available since 1.8.0 | Cross-cutting is simpler but inflexible. Per-signal required when exporters need different wrapping. |
| `OpenTelemetry.Instrumentation.Runtime` custom meters | .NET 9 built-in System.Runtime meters | .NET 9 / OTel Runtime 1.10.0+ | On .NET 9+, the runtime instrumentation package automatically uses built-in meters. |
| `Quartz.OpenTelemetry.Instrumentation` (3.x) | `OpenTelemetry.Instrumentation.Quartz` (1.12.0-beta.1) | 2024 | Quartz team deprecated their own package in favor of the OTel contrib package. |

**Deprecated/outdated:**
- `Quartz.OpenTelemetry.Instrumentation` 3.x: Deprecated by Quartz team. Use `OpenTelemetry.Instrumentation.Quartz` from contrib repo.
- `Sdk.CreateTracerProviderBuilder()` for hosted apps: Use `AddOpenTelemetry().WithTracing()` instead for automatic lifecycle management.

## Open Questions

1. **AddProcessor factory overload availability**
   - What we know: `OpenTelemetryLoggerOptions.AddProcessor(BaseProcessor<LogRecord>)` exists. The factory overload (`Func<IServiceProvider, BaseProcessor>`) may or may not exist in 1.15.0.
   - What's unclear: Whether the log enrichment processor can resolve DI services via a factory delegate at registration time, or if it needs to use a different pattern (e.g., service locator via `IServiceProvider` stored at registration).
   - Recommendation: During implementation, check the `OpenTelemetryLoggerOptions` API surface. If no factory overload exists, create the processor after `builder.Build()` using `app.Services`, or inject `IServiceProvider` into the processor and resolve lazily on first `OnEnd` call.

2. **ClearProviders() interaction with OpenTelemetry**
   - What we know: `builder.Logging.ClearProviders()` removes all default providers including Console, Debug, EventSource. `AddOpenTelemetry()` on the logging builder re-adds the OpenTelemetry provider.
   - What's unclear: Whether `ClearProviders()` called before `AddOpenTelemetry()` causes any issues, or if the order (clear then add) is safe.
   - Recommendation: Call `ClearProviders()` first, then `AddConsole()` conditionally, then `AddOpenTelemetry()`. Test that logs reach OTLP after clearing.

3. **Role-gated exporter integration point**
   - What we know: `BaseExporter<T>` can be wrapped with a decorator. The OTLP exporter is added via `AddOtlpExporter()` which internally creates the exporter instance.
   - What's unclear: How to intercept the exporter creation to wrap it with `RoleGatedExporter<T>`. May need to use `AddProcessor` with a custom export processor instead, or use `ConfigureServices` to replace the exporter after registration.
   - Recommendation: For Phase 7, register the OTLP exporters WITHOUT role gating. Design the `RoleGatedExporter<T>` class but do not wire it. Phase 8 handles the wiring when `ILeaderElection` has a real implementation. The `AlwaysLeaderElection` stub means all exporters are active anyway.

4. **Interaction between AddOpenTelemetry and existing IMeterFactory**
   - What we know: .NET 9 Web SDK implicitly registers `IMeterFactory`. MetricFactory already injects `IMeterFactory` to create its Meter. `AddOpenTelemetry().WithMetrics()` hooks into this.
   - What's unclear: Whether `AddOpenTelemetry()` modifies the `IMeterFactory` registration or creates its own MeterProvider alongside.
   - Recommendation: Test that MetricFactory's existing instruments are captured by the new MeterProvider. The `.AddMeter("Simetra.Metrics")` call should subscribe to the existing Meter.

## Sources

### Primary (HIGH confidence)
- [OpenTelemetry .NET Exporters docs](https://opentelemetry.io/docs/languages/dotnet/exporters/) -- OTLP setup patterns, per-signal vs cross-cutting
- [OpenTelemetry.Exporter.OpenTelemetryProtocol README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md) -- UseOtlpExporter, OtlpExporterOptions, configuration
- [OpenTelemetry .NET Instrumentation docs](https://opentelemetry.io/docs/languages/dotnet/instrumentation/) -- AddOpenTelemetry setup, ActivitySource, Meter registration
- [OpenTelemetry.Instrumentation.Runtime 1.15.0 NuGet](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Runtime/) -- Version, dependencies, .NET 9 built-in meters
- [OpenTelemetry.Extensions.Hosting 1.15.0 NuGet](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) -- Version, hosting lifecycle
- [OpenTelemetry .NET extending trace SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md) -- Custom exporter (BaseExporter), registration patterns
- [OpenTelemetry .NET extending metrics SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/extending-the-sdk/README.md) -- Custom metric exporter (BaseExporter<Metric>)
- [OpenTelemetry .NET extending logs SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/logs/extending-the-sdk/README.md) -- Custom log processor (BaseProcessor<LogRecord>)
- [OpenTelemetry .NET customizing logs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/logs/customizing-the-sdk/README.md) -- OpenTelemetryLoggerOptions, AddProcessor, IncludeScopes
- [Microsoft Learn: .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) -- Official Microsoft guidance, package table
- [OpenTelemetry .NET graceful shutdown issue #5261](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5261) -- ForceFlush pattern, disposal timing

### Secondary (MEDIUM confidence)
- [OpenTelemetry .NET graceful shutdown discussion #3614](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3614) -- Dispose vs ForceFlush, batch exporter timing
- [AWS Blog: Custom Processors in .NET 8](https://aws.amazon.com/blogs/dotnet/developing-custom-processors-using-opentelemetry-in-net-8/) -- LogRecord attribute enrichment pattern

### Tertiary (LOW confidence)
- Training data knowledge on `BaseProcessor<LogRecord>.OnEnd()` attribute modification pattern -- needs implementation validation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- All packages verified via NuGet with exact versions and release dates (1.15.0, Jan 21 2026)
- Architecture: HIGH -- Patterns verified against official OpenTelemetry .NET docs (extending SDK, customizing SDK)
- Pitfalls: HIGH -- Meter name mismatch and ClearProviders() are well-documented gotchas; ForceFlush timing verified via GitHub issues
- Role-gated exporter pattern: MEDIUM -- BaseExporter decorator pattern is architecturally sound per official extension docs, but the specific wiring with AddOtlpExporter is an open question (deferred to Phase 8)
- Log enrichment processor: MEDIUM -- BaseProcessor<LogRecord> is official, but LogRecord.Attributes mutation API needs implementation validation

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (stable -- OpenTelemetry .NET 1.15.0 is a stable release)
