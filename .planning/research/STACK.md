# Stack Research

**Domain:** Headless .NET Core 9 SNMP Supervisor Service (Kubernetes, Leader-Follower HA)
**Researched:** 2026-02-15
**Confidence:** HIGH (all core packages verified via NuGet/official docs/GitHub)

---

## Recommended Stack

### Core Framework

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 9 | 9.0 (LTS successor track) | Runtime and SDK | Current production-ready .NET release. Worker Service template provides the exact project shape needed: `Microsoft.NET.Sdk.Worker`. Native AOT support available if needed for container startup. |
| ASP.NET Core 9 (minimal API) | 9.0 | Health probe endpoints only | Needed solely for `/healthz`, `/readyz`, `/startupz` HTTP endpoints for Kubernetes probes. Use `Microsoft.NET.Sdk.Web` to get both Worker and minimal API capabilities in one host. |
| Microsoft.Extensions.Hosting | 9.0.x (ships with SDK) | Host lifecycle, DI, configuration, logging | The backbone of any .NET headless service. Provides `BackgroundService`, `IHostedService`, `IHostApplicationLifetime`, scoped DI, and graceful shutdown with configurable `ShutdownTimeout`. |

**Confidence:** HIGH -- verified via Microsoft Learn official documentation for ASP.NET Core 9 hosted services.

### SNMP Library

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Lextm.SharpSnmpLib | 12.5.7 | SNMP v1/v2c/v3 GET, SET, WALK, BULK WALK, TRAP receive, INFORM | The only actively maintained, MIT-licensed, pure .NET SNMP library. Targets net8.0+ (compatible with net9.0). Zero dependencies. Full async API: `GetAsync`, `SetAsync`, `WalkAsync`, `BulkWalkAsync`, `SendTrapV2Async`, `SendInformAsync`. Published Nov 3, 2025. |

**Key API patterns for Simetra:**

```csharp
// Polling: GET operation (async)
var result = await Messenger.GetAsync(
    VersionCode.V2,
    new IPEndPoint(IPAddress.Parse(target), 161),
    new OctetString(community),
    new List<Variable> { new Variable(new ObjectIdentifier(oid)) },
    cancellationToken);

// Polling: BULK WALK (async, for table walks)
var results = new List<Variable>();
await Messenger.BulkWalkAsync(
    VersionCode.V2,
    new IPEndPoint(IPAddress.Parse(target), 161),
    new OctetString(community),
    OctetString.Empty,
    new ObjectIdentifier(tableOid),
    results,
    maxRepetitions: 10,
    WalkMode.WithinSubtree,
    null,  // privacy provider for v3
    null,  // report
    cancellationToken);

// Trap listener: event-based pattern
var trapListener = new TrapListener();
trapListener.MessageReceived += (sender, args) => {
    // args.Message contains the trap
    // Push into Channel<SnmpTrapMessage> for async processing
};
trapListener.Start();
```

**What about SharpSnmpLib Pro?** The open-source library covers all operations Simetra needs. The Pro/commercial version adds MIB compilation and SNMP v3 engine management -- not needed for a supervisor service that uses pre-configured OIDs from `PollDefinitionDto`.

**Confidence:** HIGH -- version/API verified via NuGet page (12.5.7, Nov 2025) and GitHub source code (`Messenger.cs` confirms all async methods).

### Job Scheduling

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Quartz.NET | 3.15.1 | Cron-based and interval-based SNMP poll scheduling | Enterprise-grade scheduler with net8.0/net9.0 targets. In-memory store sufficient (no persistence requirement). DI-native since v3.7: jobs resolved from container automatically via `ActivatorUtilities`. |
| Quartz.Extensions.DependencyInjection | 3.15.1 | DI integration | `AddQuartz()` on `IServiceCollection` with `IServiceCollectionQuartzConfigurator` for strongly-typed job/trigger configuration. |
| Quartz.AspNetCore | 3.15.1 | Hosted service + health checks | `AddQuartzHostedService()` manages scheduler lifecycle. On .NET 6+, automatically registers ASP.NET Core health check for scheduler status. |

**Key DI pattern for Simetra:**

```csharp
services.AddQuartz(q =>
{
    q.SchedulerId = "Simetra-Scheduler";
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

    // Jobs added dynamically at runtime from PollDefinitionDto config
    // via ISchedulerFactory after host starts
});

services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true; // Graceful shutdown
});
```

**Why not Hangfire?** Hangfire requires persistent storage (SQL/Redis) and is designed for web application background jobs with dashboards. Simetra has no persistence and no UI -- Quartz.NET with in-memory store is the correct fit.

**Why not `PeriodicTimer` / raw timers?** Simple timers lack: cron expressions, misfire handling, job identity/grouping, concurrent execution policies, and trigger pause/resume. Quartz provides all of these out of the box.

**Confidence:** HIGH -- versions verified via NuGet (3.15.1, Oct 26 2025), DI integration verified via official Quartz.NET docs.

### Observability (OpenTelemetry)

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| OpenTelemetry | 1.15.0 | Core SDK | Unified observability framework. Stable since 1.0. The .NET ecosystem standard -- both Microsoft and CNCF endorsed. |
| OpenTelemetry.Api | 1.15.0 | Instrumentation API | For creating custom `ActivitySource` (traces) and `Meter` (metrics) in Simetra's domain code. |
| OpenTelemetry.Extensions.Hosting | 1.15.0 | Host integration | `AddOpenTelemetry()` on `IServiceCollection`. Manages lifecycle of TracerProvider and MeterProvider. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.0 | OTLP export | Sends traces, metrics, and logs to any OTLP-compatible collector (Grafana Alloy, OpenTelemetry Collector, Datadog Agent). Supports gRPC and HTTP/Protobuf. mTLS support added in 1.15.0. |
| OpenTelemetry.Instrumentation.Quartz | 1.12.0-beta.1 | Auto-instrument Quartz jobs | Captures job execution spans automatically. Beta but stable in practice (8.8M total downloads). Part of `opentelemetry-dotnet-contrib`. |

**Configuration pattern for Simetra:**

```csharp
const string ServiceName = "simetra-supervisor";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)            // Custom ActivitySource
        .AddQuartzInstrumentation()        // Auto-instrument jobs
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(ServiceName)             // Custom Meter
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter();
});
```

**Why not Application Insights SDK directly?** OTLP is vendor-neutral. Simetra can ship telemetry to any backend. Application Insights can consume OTLP via Azure Monitor OpenTelemetry exporter if needed later -- but the base should remain vendor-agnostic.

**Note:** `Quartz.OpenTelemetry.Instrumentation` (3.15.0) is the Quartz team's own package but is **deprecated** in favor of `OpenTelemetry.Instrumentation.Quartz` from the contrib repo. Use the contrib package.

**Confidence:** HIGH -- all versions verified via NuGet and official OpenTelemetry .NET releases page (v1.15.0, Jan 21 2025). Quartz instrumentation is MEDIUM (beta, but widely used).

### Kubernetes Integration

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| KubernetesClient | 18.0.13 | K8s API access for Lease-based leader election | Official CNCF-maintained C# client. Targets net8.0/net9.0/net10.0. Published Dec 2, 2025. Includes `LeaseLock`, `LeaderElector`, and `LeaderElectionConfig` classes in `k8s.LeaderElection` namespace. |

**Leader election pattern for Simetra:**

```csharp
// Create Kubernetes client (auto-detects in-cluster config)
var k8sConfig = KubernetesClientConfiguration.InClusterConfig();
var k8sClient = new Kubernetes(k8sConfig);

// Create LeaseLock (uses coordination.k8s.io/v1 Lease)
var leaseLock = new LeaseLock(
    k8sClient,
    @namespace: "simetra",
    name: "simetra-leader",
    identity: Environment.MachineName  // Pod name
);

var leaderConfig = new LeaderElectionConfig(leaseLock)
{
    LeaseDuration = TimeSpan.FromSeconds(15),
    RenewDeadline = TimeSpan.FromSeconds(10),
    RetryPeriod = TimeSpan.FromSeconds(2)
};

var leaderElector = new LeaderElector(leaderConfig);

// Events
leaderElector.OnStartedLeading += () => { /* Activate polling, enable trap processing */ };
leaderElector.OnStoppedLeading += () => { /* Pause polling, drain channels */ };
leaderElector.OnNewLeader += (leader) => { /* Log leader change */ };

// Run in a BackgroundService
await leaderElector.RunUntilLeadershipLostAsync(stoppingToken);
```

**Why LeaseLock over ConfigMapLock?** `LeaseLock` uses the purpose-built `coordination.k8s.io/v1 Lease` resource. `ConfigMapLock` is the legacy approach that overloads ConfigMaps for leader election. Kubernetes itself uses Leases for node heartbeats and component leader election. LeaseLock is the correct modern choice.

**RBAC requirements:**

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
rules:
  - apiGroups: ["coordination.k8s.io"]
    resources: ["leases"]
    verbs: ["get", "create", "update", "patch"]
```

**Confidence:** HIGH -- KubernetesClient version verified via NuGet (18.0.13, Dec 2025). LeaseLock API verified via official C# client API docs. Leader election pattern verified via GitHub source and community examples.

### Internal Async Communication

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| System.Threading.Channels | Ships with .NET 9 runtime | Async producer/consumer pipelines between services | Built into the BCL -- zero additional dependency. Thread-safe, high-performance, backpressure-aware. .NET 9 redesigned internal data structures to allocate less memory. |

**Channel patterns for Simetra:**

```csharp
// Trap ingestion pipeline: TrapListener -> Channel -> TrapProcessor
var trapChannel = Channel.CreateBounded<SnmpTrapMessage>(
    new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest trap under pressure
        SingleWriter = true,   // Single TrapListener
        SingleReader = true    // Single TrapProcessor
    });

// Poll result pipeline: QuartzJob -> Channel -> DataExtractor
var pollChannel = Channel.CreateBounded<PollResult>(
    new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.Wait, // Backpressure on poll jobs
        SingleWriter = false,  // Multiple concurrent poll jobs
        SingleReader = true    // Single extractor pipeline
    });

// Consumer pattern (in BackgroundService.ExecuteAsync)
await foreach (var trap in trapChannel.Reader.ReadAllAsync(stoppingToken))
{
    await ProcessTrapAsync(trap, stoppingToken);
}
```

**Why not `BufferBlock<T>` (TPL Dataflow)?** `Channel<T>` is the modern replacement. It is simpler, faster, and built into the runtime. TPL Dataflow is a separate NuGet package with more complex API surface. For simple producer/consumer pipelines, Channels are the standard choice.

**Why not `ConcurrentQueue<T>` + manual signaling?** Channels provide built-in `WaitToReadAsync`/`WaitToWriteAsync`, `ReadAllAsync` (IAsyncEnumerable), completion signaling, and backpressure -- all things you would have to build manually with ConcurrentQueue.

**Confidence:** HIGH -- verified via Microsoft Learn official Channels documentation (updated Dec 2025). .NET 9 improvements verified via multiple technical articles.

### Resilience

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Polly | 8.6.5 (via Polly.Core) | Retry, timeout, circuit breaker for SNMP operations | Industry-standard .NET resilience library. v8 redesign uses `ResiliencePipeline` composable API. Microsoft's official `Microsoft.Extensions.Resilience` is built on top of Polly v8. |
| Microsoft.Extensions.Resilience | 8.x | DI-integrated resilience | Provides `AddResiliencePipeline()` for named pipelines injectable via `ResiliencePipelineProvider<string>`. |

**Pattern for Simetra SNMP operations:**

```csharp
services.AddResiliencePipeline("snmp-poll", builder =>
{
    builder
        .AddTimeout(TimeSpan.FromSeconds(10))  // Per-attempt timeout
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            ShouldHandle = new PredicateBuilder()
                .Handle<TimeoutException>()
                .Handle<SnmpException>()
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(60)
        });
});
```

**Why not hand-rolled retry loops?** SNMP devices are unreliable. You need retry with backoff, per-device circuit breakers (to stop hammering a down device), and timeouts. Polly v8 gives you all three composable in a single pipeline with DI integration.

**Confidence:** HIGH -- Polly.Core 8.6.5 verified via NuGet. Pattern verified via official Polly v8 docs.

### Health Checks

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Microsoft.Extensions.Diagnostics.HealthChecks | 9.x (ships with ASP.NET Core 9) | Health check framework | Built into ASP.NET Core. Provides `AddHealthChecks()`, `MapHealthChecks()`, tag-based filtering for separate liveness/readiness endpoints. |
| Quartz.AspNetCore | 3.15.1 | Quartz scheduler health check | Auto-registers health check on .NET 6+ when using `Quartz.AspNetCore`. Reports scheduler status. |

**Health probe endpoints for Kubernetes:**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck("leader-election", () =>
        isLeader
            ? HealthCheckResult.Healthy("Leading")
            : HealthCheckResult.Healthy("Following"),
        tags: new[] { "ready" })
    .AddCheck("trap-listener", () =>
        trapListenerRunning
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Trap listener stopped"),
        tags: new[] { "live" });

var app = builder.Build();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

**Confidence:** HIGH -- verified via Microsoft Learn health checks documentation for ASP.NET Core 9.

### Configuration & Serialization

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Microsoft.Extensions.Configuration | 9.x (ships with SDK) | Static config from appsettings.json, env vars | Built-in. Supports hierarchical config, IOptions pattern, IOptionsMonitor for hot reload. |
| System.Text.Json | 9.x (ships with runtime) | JSON serialization | Built-in, AOT-compatible, high performance. Simetra uses appsettings.json only (no external API serialization needed). |

**Confidence:** HIGH -- ships with .NET 9, no external dependency.

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| Docker / Podman | Container build & local test | `dotnet publish` with container support or multi-stage Dockerfile. |
| Kubernetes (minikube/kind) | Local K8s cluster for HA testing | Leader election only testable in K8s. Use `kind` for fast local clusters. |
| SNMP simulator (snmpsim / net-snmp) | Mock SNMP agents for testing | `snmpsim` (Python) or `snmpd` can simulate device responses for integration tests. |
| OpenTelemetry Collector | Local telemetry backend | Run as a sidecar or docker container. Configure OTLP receiver, console/file exporter for development. |
| Aspire Dashboard (standalone) | Dev-time telemetry viewer | Microsoft's free standalone OTLP viewer. No Aspire framework dependency needed. `dotnet tool install -g aspire-dashboard` or run as container. |

---

## Installation

```xml
<!-- Simetra.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- SNMP -->
    <PackageReference Include="Lextm.SharpSnmpLib" Version="12.5.7" />

    <!-- Scheduling -->
    <PackageReference Include="Quartz.AspNetCore" Version="3.15.1" />

    <!-- Kubernetes -->
    <PackageReference Include="KubernetesClient" Version="18.0.13" />

    <!-- OpenTelemetry -->
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Quartz" Version="1.12.0-beta.1" />

    <!-- Resilience -->
    <PackageReference Include="Microsoft.Extensions.Resilience" Version="8.10.0" />
  </ItemGroup>
</Project>
```

**Note on SDK choice:** Using `Microsoft.NET.Sdk.Web` instead of `Microsoft.NET.Sdk.Worker` because we need minimal API for health check endpoints. This is a common pattern for headless services that need HTTP probes. The service is still a background worker -- it just also exposes health endpoints.

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| SharpSnmpLib (Lextm) | SnmpSharpNet | Never for new projects. SnmpSharpNet is unmaintained (last release 2019), sync-only, targets .NET Framework. |
| SharpSnmpLib (Lextm) | Native SNMP via P/Invoke | Only if you need Windows-only WinSNMP API. Not cross-platform. Adds native dependency complexity in containers. |
| Quartz.NET | Hangfire | When you need a persistent job store with dashboard UI. Simetra has no persistence requirement and no UI. |
| Quartz.NET | Coravel | For simple timer-based scheduling in small apps. Lacks cron expressions, misfire policies, job persistence, and mature ecosystem. |
| Quartz.NET | `PeriodicTimer` in BackgroundService | For single fixed-interval timers only. No cron, no job identity, no concurrent execution control. |
| KubernetesClient (official) | KubeOps | When building a Kubernetes Operator (CRD controllers). Simetra is a regular workload using Lease API, not an operator. |
| OpenTelemetry OTLP | Serilog + seq/Elasticsearch | When you only need structured logging without traces/metrics. Simetra requires all three pillars. OpenTelemetry log bridge works with `ILogger` -- Serilog adds unnecessary indirection. |
| Polly v8 | Hand-rolled retry | Never for production SNMP. You will miss circuit breakers, jitter, and composable pipeline patterns. |
| System.Threading.Channels | TPL Dataflow (`BufferBlock<T>`) | When you need complex dataflow graphs with linking, batching, and transformation blocks. Simetra's pipelines are simple producer/consumer -- Channels are lighter and faster. |
| System.Text.Json | Newtonsoft.Json | When you need features like `JsonPath`, `JObject` dynamic access, or compatibility with legacy APIs. Simetra only deserializes config -- System.Text.Json is sufficient. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| SnmpSharpNet | Unmaintained since 2019, sync-only, .NET Framework target | Lextm.SharpSnmpLib 12.5.7 |
| `Quartz.OpenTelemetry.Instrumentation` (3.15.x) | Deprecated by Quartz team in favor of contrib package | `OpenTelemetry.Instrumentation.Quartz` (1.12.0-beta.1) |
| Serilog as primary logging | Adds unnecessary dependency when OpenTelemetry log bridge handles structured logging via `ILogger<T>` natively. Serilog sinks duplicate what OTLP exporter already provides. | `builder.Logging.AddOpenTelemetry()` with OTLP exporter |
| `Microsoft.Extensions.Hosting` alone (Worker SDK) | Cannot expose HTTP health endpoints without adding Kestrel manually | `Microsoft.NET.Sdk.Web` with minimal API |
| ConfigMapLock for K8s leader election | Legacy approach. Abuses ConfigMaps for coordination. | `LeaseLock` (uses purpose-built `coordination.k8s.io/v1 Lease`) |
| In-process timers for poll scheduling | No cron support, no misfire handling, no job grouping, no graceful coordination with scheduler lifecycle | Quartz.NET with in-memory store |
| `Task.Run` for fire-and-forget background work | Unobserved exceptions, no backpressure, no graceful shutdown | `Channel<T>` + `BackgroundService` consumer |
| `HttpClient` for K8s API calls | Manual auth, no typed API, error-prone | `KubernetesClient` typed client |

---

## Stack Patterns by Variant

**If adding database persistence later:**
- Add `Quartz.Serialization.SystemTextJson` + `Quartz.Extensions.DependencyInjection` with AdoJobStore
- Add `Npgsql` for PostgreSQL or `Microsoft.Data.SqlClient` for SQL Server
- Because Quartz.NET natively supports persistent job stores with zero code changes to job/trigger definitions

**If deploying outside Kubernetes (bare metal/VM):**
- Replace `KubernetesClient` + `LeaseLock` with a `PeriodicTimer`-based file lock or Consul-based leader election
- Replace HTTP health probes with TCP health checks or systemd watchdog integration
- Because K8s Lease API requires a K8s API server

**If adding SNMP v3 with complex security:**
- Use `SharpSnmpLib` with `DES`/`AES` privacy providers and `MD5`/`SHA` auth providers
- All built into the base `Lextm.SharpSnmpLib` package
- Because the open-source library fully supports v3 USM (User-based Security Model)

**If telemetry volume becomes a concern:**
- Add `OpenTelemetry.Exporter.OpenTelemetryProtocol` with batch export configuration
- Configure `BatchExportProcessorOptions` with appropriate `MaxQueueSize`, `MaxExportBatchSize`, and `ScheduledDelayMilliseconds`
- Because the OTLP exporter batches by default but may need tuning at scale

---

## Version Compatibility Matrix

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| Lextm.SharpSnmpLib 12.5.7 | net8.0, net9.0 | Targets net8.0+. No dependencies. |
| Quartz.AspNetCore 3.15.1 | net8.0, net9.0 | Explicit net9.0 target. Depends on Quartz 3.15.1 + Microsoft.Extensions.Diagnostics.HealthChecks. |
| KubernetesClient 18.0.13 | net8.0, net9.0, net10.0 | Depends on Fractions 7.3.0+, YamlDotNet 16.3.0+. |
| OpenTelemetry.* 1.15.0 | net8.0, net9.0 | All core OTel packages aligned at 1.15.0. Instrumentation.Quartz at 1.12.0-beta.1 is compatible. |
| Polly.Core 8.6.5 | netstandard2.0+ | Compatible with all .NET versions. |
| Microsoft.Extensions.Resilience 8.x | net8.0+ | Built on Polly.Core 8.x. |

---

## Sources

**Official NuGet pages (version/date verification):**
- [Lextm.SharpSnmpLib 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib/) -- v12.5.7, Nov 3 2025, net8.0+, MIT license, zero dependencies (HIGH)
- [Quartz.AspNetCore 3.15.1](https://www.nuget.org/packages/Quartz.AspNetCore) -- v3.15.1, Oct 26 2025, net8.0/net9.0 (HIGH)
- [KubernetesClient 18.0.13](https://www.nuget.org/packages/KubernetesClient/) -- v18.0.13, Dec 2 2025, net8.0/net9.0/net10.0 (HIGH)
- [OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol) -- v1.15.0, Jan 21 2025 (HIGH)
- [OpenTelemetry.Instrumentation.Quartz 1.12.0-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) -- v1.12.0-beta.1, prerelease, May 2025 (MEDIUM)
- [Polly.Core 8.6.5](https://www.nuget.org/packages/polly.core/) -- v8.6.5 (HIGH)

**Official documentation (API/pattern verification):**
- [Microsoft Learn: Background tasks with hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0) -- BackgroundService patterns, scoped DI, queued tasks (HIGH)
- [Microsoft Learn: Channels in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) -- Updated Dec 2025, bounded/unbounded patterns (HIGH)
- [Microsoft Learn: Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) -- Kubernetes liveness/readiness probes (HIGH)
- [Quartz.NET: Microsoft DI Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) -- AddQuartz, job factory, scoped jobs (HIGH)
- [Quartz.NET: Hosted Services Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html) -- AddQuartzHostedService, graceful shutdown (HIGH)
- [OpenTelemetry .NET: Exporters](https://opentelemetry.io/docs/languages/dotnet/exporters/) -- OTLP exporter configuration (HIGH)
- [OpenTelemetry .NET: Getting Started](https://opentelemetry.io/docs/languages/dotnet/getting-started/) -- Full setup pattern (HIGH)

**GitHub source code (API verification):**
- [SharpSnmpLib Messenger.cs](https://github.com/lextudio/sharpsnmplib/blob/master/SharpSnmpLib/Messaging/Messenger.cs) -- Confirmed all async methods: GetAsync, SetAsync, WalkAsync, BulkWalkAsync, SendTrapV1Async, SendTrapV2Async, SendInformAsync (HIGH)
- [KubernetesClient LeaseLock](https://kubernetes-client.github.io/csharp/api/k8s.LeaderElection.ResourceLock.LeaseLock.html) -- LeaseLock constructor (IKubernetes, namespace, name, identity) (HIGH)
- [KubernetesClient LeaderElector](https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/LeaderElection/LeaderElector.cs) -- RunUntilLeadershipLostAsync, events (HIGH)

**Community/blog (pattern validation):**
- [Leader Election in Kubernetes with the Official C# Client](https://martowen.com/posts/2022/leader-election-in-kubernetes/) -- Full LeaseLock/LeaderElector pattern with code (MEDIUM)
- [Polly v8 Resilience Pipelines](https://www.pollydocs.org/pipelines/) -- Pipeline composition patterns (HIGH -- official Polly docs)
- [Microsoft Learn: Resilient app development](https://learn.microsoft.com/en-us/dotnet/core/resilience/) -- Microsoft.Extensions.Resilience integration (HIGH)

---

*Stack research for: Simetra -- Headless .NET 9 SNMP Supervisor Service*
*Researched: 2026-02-15*
