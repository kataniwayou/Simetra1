# Architecture Research

**Domain:** Headless .NET SNMP supervisor service (pipeline-based, K8s-native)
**Researched:** 2026-02-15
**Confidence:** HIGH (Microsoft official docs, Quartz.NET docs, OpenTelemetry docs verified)

## Standard Architecture

### System Overview

```
                              K8s Control Plane
                            (coordination.k8s.io/v1)
                                     |
                                     | Lease acquire/renew/release
                                     v
 +---------------------------------------------------------------------------+
 |  Program.cs  (Host Composition Root)                                      |
 |                                                                           |
 |  HostApplicationBuilder                                                   |
 |  +-- AddHostedService<LeaderElectionService>()   [IHostedService]         |
 |  +-- AddHostedService<SnmpListenerService>()     [BackgroundService]      |
 |  +-- AddQuartz(q => { ... })                     [Quartz scheduler]       |
 |  +-- AddQuartzHostedService(o => WaitForJobs)    [IHostedService]         |
 |  +-- AddOpenTelemetry().WithMetrics/Tracing/Logs [OTel providers]         |
 |  +-- AddHealthChecks()                           [3 probes]               |
 |  +-- WebApplication.MapHealthChecks(...)         [Minimal API endpoints]  |
 |  +-- services.AddSingleton/Scoped/Transient      [Pipeline components]    |
 +---------------------------------------------------------------------------+
                  |                        |                    |
          ________v________       _________v_________     _____v_____
         |                 |     |                   |   |           |
         |  SNMP Listener  |     |  Quartz Scheduler |   | Minimal  |
         |  (UDP :162)     |     |  (IScheduler)     |   | API      |
         |  BackgroundSvc  |     |  HostedService    |   | (Probes) |
         |_________________|     |___________________|   |___________|
                  |                   |    |    |    |
           Trap   |            StatePoll  MetricPoll  Heartbeat  Correlation
                  v                   v         v        v           v
 +---------------------------------------------------------------------------+
 |                    Layer 2: Routing & Filtering                           |
 |                                                                           |
 |  IDeviceFilter (IP -> device)  +  ITrapFilter (OID filtering)            |
 |                                                                           |
 |  Channels:                                                                |
 |  +---------------------------+                                            |
 |  | Channel<TrapContext>  [D1]| BoundedCapacity=100, DropOldest            |
 |  | Channel<TrapContext>  [D2]| (one per registered device)                |
 |  | Channel<TrapContext>  [Sm]| (Simetra virtual device)                   |
 |  +---------------------------+                                            |
 +---------------------------------------------------------------------------+
             | (via channel consumer)       | (polls skip channels)
             v                              v
 +---------------------------------------------------------------------------+
 |                    Layer 3: Extraction                                     |
 |                                                                           |
 |  ISnmpExtractor  (generic, PollDefinitionDto-driven)                      |
 |  +-- OID-to-PropertyName mapping                                          |
 |  +-- EnumMap resolution                                                   |
 |  +-- SNMP type conversion (INTEGER->int, STRING->string, etc.)            |
 |  Output: DomainObject (key-value property bag)                            |
 +---------------------------------------------------------------------------+
                              |
                     +--------+--------+
                     |                 |
                     v                 v
 +-------------------------------+  +-------------------------------+
 |  Layer 4 - Branch A           |  |  Layer 4 - Branch B           |
 |  Metric Creation              |  |  State Vector Update          |
 |                               |  |                               |
 |  IMetricFactory               |  |  IStateVectorService          |
 |  +-- base labels enforced     |  |  +-- Source=Module only       |
 |  +-- MetricName from DTO      |  |  +-- domain obj + timestamp  |
 |  +-- leader-gated export      |  |  +-- correlationId attached  |
 |                               |  |                               |
 |  --> OtlpExporter (gated)     |  |  --> In-memory per-tenant    |
 +-------------------------------+  +-------------------------------+
             |
             v
 +---------------------------------------------------------------------------+
 |                    OpenTelemetry Export Layer                              |
 |                                                                           |
 |  RoleGatedExporter (decorating pattern)                                   |
 |  +-- Leader: logs + metrics + traces --> OTLP                             |
 |  +-- Follower: logs only --> OTLP                                         |
 +---------------------------------------------------------------------------+
             |
             v
      OTLP Collector
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **Program.cs** | Composition root: wires all DI, configures host, maps health endpoints, runs 11-step startup | `HostApplicationBuilder` with `WebApplication` for minimal API probes |
| **LeaderElectionService** | Acquires/renews/releases K8s Lease, exposes `IsLeader` state, triggers role change events | `IHostedService` (not BackgroundService) with explicit Start/Stop lifecycle |
| **SnmpListenerService** | Binds UDP socket, receives SNMP v2c traps, attaches correlationId, routes to Layer 2 | `BackgroundService` with `ExecuteAsync` loop reading UDP datagrams |
| **Quartz Scheduler** | Hosts all scheduled jobs (state polls, metric polls, heartbeat, correlation) | `AddQuartz()` + `AddQuartzHostedService()` via official Quartz.Extensions.Hosting |
| **DeviceFilterService** | Identifies source device by IP address from configured device list | Singleton, stateless lookup from `IOptions<SimetraOptions>` |
| **TrapFilterService** | Filters traps by OID against device module trap definitions | Singleton, matches OIDs from `IDeviceModule.TrapDefinitions` |
| **Channel<TrapContext>** | Per-device bounded buffer isolating trap processing threads | `Channel.CreateBounded<TrapContext>(BoundedChannelOptions { FullMode = DropOldest })` |
| **ChannelConsumerService** | Reads from device channel, drives extraction and processing | `BackgroundService` per device, `await foreach (ReadAllAsync)` pattern |
| **SnmpExtractorService** | Maps SNMP varbinds to domain objects using PollDefinitionDto | Singleton, stateless transformation |
| **MetricFactoryService** | Creates OTLP metrics with enforced base labels | Singleton, uses `System.Diagnostics.Metrics.Meter` |
| **StateVectorService** | Stores last-known-state per tenant (device + Simetra virtual) | Singleton, `ConcurrentDictionary<string, StateVectorEntry>` |
| **CorrelationService** | Generates/stores current correlationId, provides read access | Singleton, thread-safe volatile field or `Interlocked` |
| **LivenessVectorService** | Stores last-completion timestamp per scheduled job | Singleton, `ConcurrentDictionary<string, LivenessVectorEntry>` |
| **RoleGatedExporter** | Decorator around OTLP exporters that checks `IsLeader` before export | Custom `BaseExporter<T>` subclass wrapping real OTLP exporter |
| **Health Probes** | Three minimal API endpoints for K8s startup, readiness, liveness probes | `MapHealthChecks` with tag filtering, custom `IHealthCheck` implementations |
| **DeviceModuleRegistry** | Discovers and registers `IDeviceModule` implementations | Singleton, populated at startup from DI container |
| **Middleware Chain** | Cross-cutting concerns: correlationId propagation, structured logging, error handling | Custom delegate chain (non-HTTP), composed at startup |

## Recommended Project Structure

```
src/
+-- Simetra.csproj                     # .NET 9, single project
+-- Program.cs                         # Composition root, 11-step startup
+-- appsettings.json                   # Static configuration
|
+-- Configuration/                     # Options classes (bind appsettings)
|   +-- SimetraOptions.cs              # Root config
|   +-- SiteOptions.cs                 # Site:Name, Site:PodIdentity
|   +-- LeaseOptions.cs                # Lease:Name, Namespace, Renew, Duration
|   +-- SnmpListenerOptions.cs         # SnmpListener:Port, BindAddress, Community
|   +-- ChannelsOptions.cs             # Channels:BoundedCapacity
|   +-- OtlpOptions.cs                # Otlp:Endpoint, ServiceName
|   +-- HeartbeatJobOptions.cs         # HeartbeatJob:IntervalSeconds
|   +-- CorrelationJobOptions.cs       # CorrelationJob:IntervalSeconds
|   +-- LivenessOptions.cs            # Liveness:GraceMultiplier
|   +-- LoggingOptions.cs             # Logging:LogLevel, EnableConsole
|
+-- Models/                            # DTOs and domain objects (no logic)
|   +-- PollDefinitionDto.cs           # Unified trap/poll definition
|   +-- OidEntryDto.cs                 # OID + PropertyName + EnumMap
|   +-- DomainObject.cs                # Extracted key-value bag
|   +-- StateVectorEntry.cs            # DomainObj + timestamp + correlationId
|   +-- LivenessVectorEntry.cs         # Job completion timestamp
|   +-- TrapContext.cs                 # SNMP trap + metadata (correlationId, deviceId)
|   +-- PollContext.cs                 # SNMP response + metadata
|   +-- Metric.cs                      # Metric name, type, value, labels
|   +-- DeviceConfiguration.cs         # Per-device config from appsettings
|
+-- Devices/                           # Plugin system (strategy pattern)
|   +-- IDeviceModule.cs               # Interface: DeviceType, TrapDefs, StatePollDefs
|   +-- DeviceModuleRegistry.cs        # Discovery + registration
|   +-- SimetraModule.cs               # Virtual device (heartbeat)
|   +-- (RouterModule.cs)              # Future: real device modules
|
+-- Services/                          # Core pipeline services
|   +-- SnmpListenerService.cs         # Layer 1: UDP listener (BackgroundService)
|   +-- DeviceFilterService.cs         # Layer 2: IP-based device identification
|   +-- TrapFilterService.cs           # Layer 2: OID filtering
|   +-- ChannelConsumerService.cs      # Channel reader (BackgroundService per device)
|   +-- SnmpExtractorService.cs        # Layer 3: varbind extraction
|   +-- MetricFactoryService.cs        # Layer 4A: metric creation
|   +-- StateVectorService.cs          # Layer 4B: state management
|   +-- CorrelationService.cs          # CorrelationId generation + access
|   +-- LivenessVectorService.cs       # Job stamp management
|   +-- LeaderElectionService.cs       # K8s Lease lifecycle (IHostedService)
|   +-- SnmpPollerService.cs           # SNMP GET request execution
|
+-- Jobs/                              # Quartz job implementations
|   +-- StatePollJob.cs                # Source=Module polls (State Vector + metric)
|   +-- MetricPollJob.cs               # Source=Configuration polls (metric only)
|   +-- HeartbeatJob.cs                # Loopback trap sender
|   +-- CorrelationJob.cs              # CorrelationId refresh + liveness stamp
|
+-- Health/                            # K8s probe handlers
|   +-- StartupHealthCheck.cs          # IHealthCheck: pipeline wired + correlationId
|   +-- ReadinessHealthCheck.cs        # IHealthCheck: channels + scheduler
|   +-- LivenessHealthCheck.cs         # IHealthCheck: liveness vector staleness
|
+-- Middleware/                        # Pipeline middleware (non-HTTP)
|   +-- IPipelineMiddleware.cs         # Delegate signature for middleware
|   +-- CorrelationIdMiddleware.cs     # Attaches correlationId to context
|   +-- LoggingMiddleware.cs           # Structured logging with site/role
|   +-- ErrorHandlingMiddleware.cs     # Catch-and-continue per item
|   +-- MiddlewareChainBuilder.cs      # Composes middleware into pipeline delegate
|
+-- Telemetry/                         # OpenTelemetry integration
|   +-- OtelSetup.cs                   # AddOpenTelemetry extension
|   +-- RoleGatedMetricExporter.cs     # Decorator: export only if leader
|   +-- RoleGatedTraceExporter.cs      # Decorator: export only if leader
|   +-- SimetraMeterProvider.cs        # Meter registration for SNMP-derived metrics
|
+-- Extensions/                        # DI registration extensions
|   +-- ServiceCollectionExtensions.cs # Core service registration
|   +-- QuartzExtensions.cs            # Quartz job/trigger registration
|   +-- OtelExtensions.cs              # OpenTelemetry wiring
|   +-- HealthCheckExtensions.cs       # Health check registration
|
+-- Utils/                             # Stateless helpers
    +-- SnmpTypeConverter.cs           # SNMP type -> CLR type
    +-- OidValidator.cs                # OID format validation
    +-- EnumMappingHelper.cs           # Enum resolution from OidEntry

tests/
+-- Simetra.Tests.csproj               # xUnit + FluentAssertions + Moq
+-- Services/                           # Unit tests per service
+-- Jobs/                              # Job unit tests
+-- Health/                            # Probe handler tests
+-- Devices/                           # Device module tests
+-- Integration/                       # Multi-layer integration tests
+-- Fixtures/                          # Shared test data builders
+-- Mocks/                             # Mock SNMP, K8s, OTLP
```

### Structure Rationale

- **Single project:** No need for multi-project solution at ~5 devices per instance scale. Keeps dependency graph simple and build fast. If the project grows to support multiple SNMP protocol versions or external plugin loading, split into `Simetra.Core` and `Simetra.Plugins`.
- **Services/ flat:** All Layer 1-4 services in one directory. The 4-layer pipeline is conceptual, not a folder hierarchy. Service names encode their layer role (e.g., `DeviceFilterService` is obviously Layer 2).
- **Devices/ separate:** Plugin boundary. New device types only touch this directory + appsettings. Open/Closed Principle enforced by directory isolation.
- **Middleware/ non-HTTP:** The pipeline middleware is NOT ASP.NET HTTP middleware. It is a custom delegate chain operating on `TrapContext`/`PollContext` objects. Keep separate from ASP.NET's request pipeline.
- **Health/ with IHealthCheck:** Uses ASP.NET Core's built-in health check infrastructure (`IHealthCheck` interface + `MapHealthChecks`) rather than custom HTTP handlers. Tag-based filtering routes probes to the correct health checks.

## Architectural Patterns

### Pattern 1: IHostedService Registration Ordering for Startup Sequencing

**What:** The .NET Generic Host starts `IHostedService` implementations sequentially in registration order (default behavior). Simetra's 11-step startup sequence depends on this ordering. `LeaderElectionService` registers before `SnmpListenerService`, which registers before Quartz, ensuring lease acquisition happens before the listener binds and before jobs schedule.

**When to use:** When multiple hosted services have startup dependencies (e.g., lease must be acquired before metrics export is enabled).

**Trade-offs:**
- Pro: Simple, deterministic startup ordering without explicit coordination
- Pro: `.NET 8+` `IHostedLifecycleService` provides `StartingAsync`/`StartedAsync` hooks for finer control
- Con: Fragile if someone reorders `AddHostedService` calls. Mitigate with comments and integration tests.
- Con: Sequential startup is slower. Do NOT enable `ServicesStartConcurrently = true` because the 11-step sequence depends on ordering.

**Confidence:** HIGH -- verified via [Microsoft official docs](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) (updated 2026-02-04) and [Steve Gordon's analysis](https://www.stevejgordon.co.uk/introducing-the-new-ihostedlifecycleservice-interface-in-dotnet-8).

**Example:**
```csharp
// Program.cs -- registration ORDER matters for startup sequence
var builder = Host.CreateApplicationBuilder(args);

// Step 1-4: Configuration, validation, device module registration
builder.Services.Configure<SimetraOptions>(builder.Configuration);
builder.Services.AddSingleton<DeviceModuleRegistry>();
builder.Services.AddSingleton<CorrelationService>();

// Step 5: First correlationId (generated in LeaderElectionService.StartAsync
// or via IHostedLifecycleService.StartingAsync on a dedicated init service)

// Step 6-7: Core services (singletons, no startup work)
builder.Services.AddSingleton<IDeviceFilter, DeviceFilterService>();
builder.Services.AddSingleton<ITrapFilter, TrapFilterService>();
builder.Services.AddSingleton<ISnmpExtractor, SnmpExtractorService>();
builder.Services.AddSingleton<IMetricFactory, MetricFactoryService>();
builder.Services.AddSingleton<IStateVectorService, StateVectorService>();
builder.Services.AddSingleton<ILivenessVector, LivenessVectorService>();

// Step 8: Channels created per device (factory in DI)
builder.Services.AddSingleton<ChannelRegistry>(); // creates channels at startup

// Step 9: Leader election (IHostedService -- starts FIRST)
builder.Services.AddHostedService<LeaderElectionService>();

// Step 10: SNMP listener (BackgroundService -- starts SECOND)
builder.Services.AddHostedService<SnmpListenerService>();

// Step 10b: Channel consumers (BackgroundService per device -- start THIRD)
builder.Services.AddHostedService<ChannelConsumerOrchestrator>();

// Step 11: Quartz (starts LAST -- jobs begin firing)
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    // Jobs registered via QuartzExtensions
});
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true; // graceful shutdown
});
```

### Pattern 2: Channel-per-Device with BackgroundService Consumers

**What:** Each registered device gets a dedicated `Channel<TrapContext>` with bounded capacity and `DropOldest` full mode. A `BackgroundService` per device reads from its channel using `ReadAllAsync()`, driving extraction and processing. Polls bypass channels entirely -- they go directly from the Quartz job to the extractor.

**When to use:** When you need thread isolation between devices so one device's trap flood cannot starve processing for another device. Also when real-time data means stale data is less valuable than current data (hence `DropOldest`).

**Trade-offs:**
- Pro: Full device isolation -- a trap storm on device A cannot delay device B
- Pro: `Channel<T>` is allocation-free on the fast path, lock-free single-reader scenario
- Pro: `DropOldest` ensures the channel always contains the most recent data
- Con: One `BackgroundService` thread per device at ~5 devices is fine; at 100+ devices, use a worker pool reading from multiple channels
- Con: Dropped items are silent unless you register the `itemDropped` callback (added in .NET 6)

**Confidence:** HIGH -- verified via [Microsoft Channels documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) (updated 2025-12-23).

**Example:**
```csharp
// Channel creation with drop callback
public Channel<TrapContext> CreateDeviceChannel(string deviceName, int capacity)
{
    return Channel.CreateBounded<TrapContext>(
        new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,   // listener thread + heartbeat can both write
            SingleReader = true,    // one consumer per device
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        },
        droppedItem =>
        {
            _logger.LogWarning(
                "Trap dropped from channel {DeviceName} (capacity {Capacity}) " +
                "[CorrelationId: {CorrelationId}]",
                deviceName, capacity, droppedItem.CorrelationId);
            _channelDropsCounter.Add(1,
                new KeyValuePair<string, object?>("device", deviceName));
        });
}

// Consumer pattern (BackgroundService)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await foreach (var trapContext in _channel.Reader.ReadAllAsync(stoppingToken))
    {
        try
        {
            var domainObj = _extractor.Extract(trapContext.Varbinds, trapContext.PollDefinition);
            await _pipeline.ProcessAsync(domainObj, trapContext, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Trap processing failed for {DeviceName}", _deviceName);
        }
    }
}
```

### Pattern 3: Quartz.NET with Generic Host Integration

**What:** Quartz.NET integrates with .NET Generic Host via `Quartz.Extensions.Hosting`. The scheduler is registered as an `IHostedService` that starts/stops with the application lifecycle. Jobs are resolved from DI, enabling constructor injection of scoped services. `[DisallowConcurrentExecution]` prevents job overlap per job key.

**When to use:** For all scheduled work: device polls, heartbeat sends, correlationId rotation. Quartz provides cron-like scheduling, job persistence (optional), and misfire handling.

**Trade-offs:**
- Pro: Battle-tested scheduler with 15+ years of .NET usage
- Pro: DI integration via `Quartz.Extensions.DependencyInjection` -- jobs get constructor-injected dependencies
- Pro: `WaitForJobsToComplete = true` enables graceful shutdown (jobs finish before app stops)
- Pro: `[DisallowConcurrentExecution]` prevents overlapping polls to the same device
- Con: Quartz uses its own thread pool separate from the .NET thread pool. Monitor thread usage.
- Con: If jobs take longer than the scheduled interval, they queue up (mitigated by `DisallowConcurrentExecution` which skips the overlapping trigger)

**Confidence:** HIGH -- verified via [Quartz.NET official docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html) and [Andrew Lock's guide](https://andrewlock.net/using-quartz-net-with-asp-net-core-and-worker-services/).

**Example:**
```csharp
builder.Services.AddQuartz(q =>
{
    // Use DI job factory (Quartz resolves jobs from IServiceProvider)
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Correlation job -- single instance, runs at fixed interval
    var correlationJobKey = new JobKey("correlation-job");
    q.AddJob<CorrelationJob>(opts => opts.WithIdentity(correlationJobKey));
    q.AddTrigger(opts => opts
        .ForJob(correlationJobKey)
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(correlationIntervalSec)
            .RepeatForever()));

    // Heartbeat job -- single instance
    var heartbeatJobKey = new JobKey("heartbeat-job");
    q.AddJob<HeartbeatJob>(opts => opts.WithIdentity(heartbeatJobKey));
    q.AddTrigger(opts => opts
        .ForJob(heartbeatJobKey)
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(heartbeatIntervalSec)
            .RepeatForever()));

    // Per-device poll jobs -- registered dynamically from config + device modules
    foreach (var device in devices)
    {
        foreach (var pollDef in device.AllPollDefinitions)
        {
            var jobKey = new JobKey($"poll-{device.Name}-{pollDef.MetricName}");
            q.AddJob<PollJob>(opts => opts
                .WithIdentity(jobKey)
                .UsingJobData("deviceName", device.Name)
                .UsingJobData("metricName", pollDef.MetricName));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(pollDef.IntervalSeconds)
                    .RepeatForever()));
        }
    }
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```

### Pattern 4: Custom Pipeline Middleware (Non-HTTP)

**What:** Simetra's composable middleware chain is inspired by ASP.NET Core's middleware pattern but operates on `TrapContext`/`PollContext` objects, NOT HTTP requests. The middleware delegate signature is `Func<PipelineContext, Func<Task>, Task>` -- same shape as ASP.NET middleware but with a domain-specific context.

**When to use:** For cross-cutting concerns that apply to every item flowing through the pipeline: correlationId attachment, structured logging enrichment, error handling with continue-on-failure, timing/metrics.

**Trade-offs:**
- Pro: Composable -- add/remove concerns without modifying pipeline stages
- Pro: Familiar pattern for ASP.NET developers
- Pro: Testable in isolation -- each middleware is a single-responsibility unit
- Con: Must build custom composition (no framework support for non-HTTP middleware). 30-50 lines of infrastructure code.
- Con: Ordering matters. CorrelationId middleware must run before logging middleware.

**Confidence:** MEDIUM -- the non-HTTP middleware pattern is a community practice (multiple blog posts, PipelineNet library), not a Microsoft-documented pattern. The delegate shape is well-established from ASP.NET Core.

**Example:**
```csharp
// Middleware interface
public interface IPipelineMiddleware
{
    Task InvokeAsync(PipelineContext context, Func<Task> next);
}

// Composing the chain
public class MiddlewareChainBuilder
{
    private readonly List<IPipelineMiddleware> _middlewares = new();

    public MiddlewareChainBuilder Use(IPipelineMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public Func<PipelineContext, Task> Build(Func<PipelineContext, Task> terminal)
    {
        Func<PipelineContext, Task> pipeline = terminal;

        // Build from inside out (last registered runs first)
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = ctx => middleware.InvokeAsync(ctx, () => next(ctx));
        }

        return pipeline;
    }
}

// Usage at startup
var chain = new MiddlewareChainBuilder()
    .Use(new CorrelationIdMiddleware(correlationService))
    .Use(new LoggingMiddleware(logger, siteOptions))
    .Use(new ErrorHandlingMiddleware(logger))
    .Build(terminalHandler: async ctx =>
    {
        var domainObj = extractor.Extract(ctx.Varbinds, ctx.PollDefinition);
        await processingService.ProcessAsync(domainObj, ctx);
    });
```

### Pattern 5: K8s Lease-Based Leader Election

**What:** Leader-follower HA via the Kubernetes coordination.k8s.io/v1 Lease API. One pod acquires the lease (leader), others wait (followers). The leader renews periodically; if it fails to renew (crash/network), the lease expires and a follower acquires it. On graceful shutdown (SIGTERM), the leader explicitly releases the lease for near-instant failover.

**When to use:** When exactly one pod should perform a specific role (metric/trace export) while all pods do baseline work (trap processing, log export). The Lease API is the Kubernetes-native way to do this -- no external coordination service (etcd, Redis, ZooKeeper) needed beyond the K8s API server.

**Trade-offs:**
- Pro: Kubernetes-native, no additional infrastructure
- Pro: Lease TTL provides automatic failover on leader crash (~15s)
- Pro: Explicit release on SIGTERM gives near-instant failover
- Con: Lease renewal adds K8s API load (~1 request per `RenewIntervalSeconds`)
- Con: .NET Kubernetes client does not have a built-in leader election helper (unlike Go's `client-go`). Must implement acquire/renew/release loop manually.
- Con: Brief "split-brain" window possible if clock skew between pods and API server

**Confidence:** HIGH for the Lease API concept (official K8s docs). MEDIUM for .NET implementation specifics -- the `KubernetesClient` NuGet package (v18+/v19) provides raw Lease CRUD but no high-level leader election helper, so the acquire/renew/release loop must be hand-coded.

**Example:**
```csharp
public class LeaderElectionService : IHostedService, IDisposable
{
    private readonly IKubernetes _k8sClient;
    private readonly LeaseOptions _leaseOptions;
    private readonly string _podIdentity;
    private CancellationTokenSource? _cts;
    private Task? _leaseLoop;

    public bool IsLeader { get; private set; }
    public event Action<bool>? RoleChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _leaseLoop = RunLeaseLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_leaseLoop != null) await _leaseLoop;

        // Explicit release for near-instant failover
        if (IsLeader)
        {
            await ReleaseLease(cancellationToken);
            IsLeader = false;
            RoleChanged?.Invoke(false);
        }
    }

    private async Task RunLeaseLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (IsLeader)
                    await RenewLeaseAsync(ct);
                else
                    await TryAcquireLeaseAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Lease operation failed");
                if (IsLeader) { IsLeader = false; RoleChanged?.Invoke(false); }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_leaseOptions.RenewIntervalSeconds), ct);
        }
    }

    private async Task TryAcquireLeaseAsync(CancellationToken ct)
    {
        var lease = await _k8sClient.CoordinationV1
            .ReadNamespacedLeaseAsync(_leaseOptions.Name, _leaseOptions.Namespace,
                cancellationToken: ct);

        if (lease == null || LeaseExpired(lease))
        {
            // Create or update lease with our identity
            lease = CreateOrUpdateLease(lease);
            await _k8sClient.CoordinationV1
                .ReplaceNamespacedLeaseAsync(lease, _leaseOptions.Name,
                    _leaseOptions.Namespace, cancellationToken: ct);
            IsLeader = true;
            RoleChanged?.Invoke(true);
        }
    }
}
```

### Pattern 6: Role-Gated OpenTelemetry Exporters

**What:** OpenTelemetry exporters for metrics and traces are wrapped in a decorator that checks `LeaderElectionService.IsLeader` before forwarding data. Log exporters run unconditionally on all pods. This is implemented as a custom `BaseExporter<T>` that delegates to the real OTLP exporter only when the pod is the leader.

**When to use:** When only one pod in a replica set should export metrics/traces (to avoid duplicate data in the backend) but all pods should export logs (for debugging followers).

**Trade-offs:**
- Pro: Clean separation of export gating from pipeline logic. Pipeline always creates metrics; export decision is at the boundary.
- Pro: No data loss during failover -- metrics are created but not exported by followers. If a follower becomes leader, it immediately starts exporting.
- Con: OpenTelemetry SDK does not natively support runtime enable/disable of exporters. The decorator pattern works around this by intercepting the `Export()` method.
- Con: Metrics accumulated during follower role are lost (not buffered for later export when promoted to leader). This is acceptable because the next poll cycle generates fresh data.

**Confidence:** MEDIUM -- the decorator pattern around `BaseExporter<T>` is a community practice. OpenTelemetry .NET SDK (verified via [official docs](https://opentelemetry.io/docs/languages/dotnet/exporters/)) does not provide built-in role-gating. The approach is sound architecturally but requires custom code.

**Example:**
```csharp
public class RoleGatedMetricExporter : BaseExporter<Metric>
{
    private readonly BaseExporter<Metric> _innerExporter;
    private readonly LeaderElectionService _leaderService;

    public RoleGatedMetricExporter(
        BaseExporter<Metric> innerExporter,
        LeaderElectionService leaderService)
    {
        _innerExporter = innerExporter;
        _leaderService = leaderService;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (!_leaderService.IsLeader)
        {
            // Silently discard -- follower does not export metrics
            return ExportResult.Success;
        }

        return _innerExporter.Export(batch);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _innerExporter.Shutdown(timeoutMilliseconds);
    }
}
```

### Pattern 7: Three Health Probes via Minimal API

**What:** Three distinct Kubernetes health probes implemented as ASP.NET Core health checks, exposed via `MapHealthChecks` with tag-based filtering. Each probe checks different system aspects. The minimal API web server runs on a separate port from the SNMP listener.

**When to use:** For all K8s-deployed services that need startup gating, readiness signaling, and liveness monitoring. The three-probe pattern is the Kubernetes best practice for long-running services.

**Trade-offs:**
- Pro: Built-in ASP.NET Core infrastructure -- no custom HTTP server needed
- Pro: Tag filtering allows separate endpoints with different health check sets
- Pro: Health check framework integrates with DI, so checks can inject services
- Con: Requires ASP.NET Core dependency for a headless service (adds `Microsoft.AspNetCore.App` framework reference). Worth it for the health check infrastructure.

**Confidence:** HIGH -- verified via [Microsoft health check docs](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).

**Example:**
```csharp
// Registration
builder.Services.AddHealthChecks()
    .AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" })
    .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" })
    .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" });

var app = builder.Build();

// Endpoint mapping with tag filtering
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// Custom health check example
public class LivenessHealthCheck : IHealthCheck
{
    private readonly ILivenessVector _livenessVector;
    private readonly LivenessOptions _options;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var staleJobs = _livenessVector.GetStaleJobs(_options.GraceMultiplier);
        if (staleJobs.Any())
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Stale jobs: {string.Join(", ", staleJobs.Select(j => j.Key))}"));
        }
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
```

## Data Flow

### Trap Flow (Device -> OTLP)

```
External Device                   Simetra Pod
     |
     | SNMP v2c Trap (UDP)
     v
[SnmpListenerService]  (BackgroundService, UDP socket loop)
     |
     | 1. Parse SNMP PDU (SharpSnmpLib)
     | 2. Attach current correlationId (from CorrelationService)
     | 3. Wrap in TrapContext { Varbinds, SourceIp, CorrelationId }
     v
[DeviceFilterService]  (identify device by IP)
     |
     | 4. Lookup DeviceConfiguration by IP
     | 5. If unknown IP: log warning, drop
     v
[TrapFilterService]  (filter by OID)
     |
     | 6. Match trap OIDs against device module TrapDefinitions
     | 7. If no match: drop (not a configured trap)
     | 8. Attach matching PollDefinitionDto to context
     v
[Channel<TrapContext>]  (bounded, per-device, DropOldest)
     |
     | 9. TryWrite to device's channel
     | 10. If full: DropOldest fires itemDropped callback
     v
[ChannelConsumerService]  (BackgroundService, ReadAllAsync loop)
     |
     | 11. Dequeue TrapContext from channel
     v
[Middleware Chain]  (non-HTTP pipeline)
     |
     | 12. CorrelationIdMiddleware: set ambient correlationId
     | 13. LoggingMiddleware: enrich log scope with site/role/correlationId
     | 14. ErrorHandlingMiddleware: try/catch with continue-on-failure
     v
[SnmpExtractorService]  (Layer 3)
     |
     | 15. Map varbinds to DomainObject using PollDefinitionDto.Oids
     | 16. Apply EnumMap if present
     | 17. Convert SNMP types to CLR types
     v
+-- Branch A: [MetricFactoryService]  (Layer 4A)
|   | 18. Create metric with name/type from PollDefinitionDto
|   | 19. Attach base labels (site, device_name, device_ip, device_type)
|   | 20. Record on System.Diagnostics.Metrics.Meter
|   | 21. MeterProvider -> RoleGatedExporter -> OTLP (leader only)
|
+-- Branch B: [StateVectorService]  (Layer 4B, Source=Module only)
    | 22. If Source=Module: update tenant entry
    | 23. Store: DomainObject + DateTime.UtcNow + correlationId
```

### Poll Flow (Scheduler -> OTLP)

```
[Quartz Scheduler]
     |
     | Trigger fires (SimpleSchedule, IntervalSeconds from PollDefinitionDto)
     v
[StatePollJob / MetricPollJob]  (IJob, [DisallowConcurrentExecution])
     |
     | 1. Read current correlationId from CorrelationService
     | 2. Read PollDefinitionDto from JobDataMap
     v
[SnmpPollerService]  (SNMP GET request)
     |
     | 3. Send SNMP v2c GetRequest to device IP
     | 4. Await response (with CancellationToken + socket timeout)
     | 5. Parse response varbinds
     v
[SnmpExtractorService]  (Layer 3 -- DIRECT, no Channel, no Layer 2)
     |
     | 6. Same extraction logic as trap path
     v
+-- Branch A: [MetricFactoryService]  (always)
|   | 7. Create metric, attach base labels, record on Meter
|
+-- Branch B: [StateVectorService]  (Source=Module only)
|   | 8. If StatePollJob (Source=Module): update State Vector
|   | 9. If MetricPollJob (Source=Configuration): skip
|
+-- [LivenessVectorService]
    | 10. Stamp job completion: jobKey -> DateTime.UtcNow
    | (ALWAYS stamped, even if poll failed -- detects hangs, not errors)
```

### Key Data Flows

1. **Trap ingestion:** UDP socket -> parse -> correlationId attach -> device filter -> OID filter -> bounded channel -> consumer -> middleware chain -> extractor -> processing branches. The channel provides isolation so a noisy device cannot starve others.

2. **Scheduled polling:** Quartz trigger -> job execution -> SNMP GET -> response parse -> extractor -> processing branches -> liveness stamp. Polls intentionally bypass the channel layer because they are already device-targeted and should not be subject to trap-flood backpressure.

3. **Heartbeat loopback:** Quartz heartbeat job -> send trap to localhost:162 -> trap enters listener as any other trap -> flows through full pipeline including channel -> proves both scheduler AND listener/pipeline are functioning. Dual verification: (a) the job stamps liveness vector (scheduler alive), (b) the trap updates "Simetra" tenant in State Vector (pipeline flowing).

4. **Leader election:** Lease loop runs on a timer. If leader, renew lease. If follower, try to acquire expired lease. On SIGTERM, leader releases lease before shutdown. Role changes propagate to `RoleGatedExporter` via the `IsLeader` property.

5. **CorrelationId rotation:** Correlation job fires at configured interval -> generates new GUID -> stores in `CorrelationService` -> all subsequent traps and jobs read the new ID. First correlationId is generated synchronously at startup (before any job fires) to ensure the startup probe can pass.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1-5 devices | Current architecture is optimal. One `BackgroundService` per device channel. Single Quartz scheduler. In-memory state vector. |
| 5-20 devices | Monitor thread count (one consumer thread per device). Consider increasing `BoundedCapacity` if trap rates are high. Watch SNMP socket ephemeral port usage. Stagger poll intervals to avoid thundering herd. |
| 20-50 devices | Replace per-device `BackgroundService` with a worker pool of N consumers reading from multiple channels. Use `Task.WhenAny` on `WaitToReadAsync` across channels. Increase Quartz thread pool size. |
| 50+ devices | Consider running multiple Simetra instances, each monitoring a subset of devices. This is the intended scaling model ("one instance per site, ~5 devices"). |

### Scaling Priorities

1. **First bottleneck: SNMP poll concurrency.** If all devices have the same poll interval, all polls fire simultaneously. Fix: stagger poll start times by spreading triggers across the interval window. Add `startDelay` per device in Quartz trigger configuration.

2. **Second bottleneck: Channel consumer thread count.** At 20+ devices, 20+ `BackgroundService` instances consume threads. Fix: switch to a configurable worker pool (e.g., 4 workers round-robining across 20 channels).

3. **Third bottleneck: OTLP export batch size.** High-frequency metrics from many devices can overwhelm the OTLP collector. Fix: increase `PeriodicExportingMetricReader` interval, aggregate metrics per-device before export, or use delta temporality.

## Anti-Patterns

### Anti-Pattern 1: Shared Channel for All Devices

**What people do:** Use a single `Channel<TrapContext>` for all devices, routing at the consumer level.
**Why it's wrong:** A trap storm from one device fills the channel, causing trap drops for ALL devices. No isolation. Violates the core principle of channel-per-device.
**Do this instead:** One bounded channel per registered device. Each channel has independent capacity and drop behavior.

### Anti-Pattern 2: Polls Going Through Channels

**What people do:** Route SNMP poll responses through the same channel pipeline as traps, "for consistency."
**Why it's wrong:** Polls are already device-targeted -- they don't need routing or filtering. Putting them in channels subjects them to backpressure from trap floods, causing poll data loss. Polls should be the most reliable data source.
**Do this instead:** Polls skip Layer 2 entirely and go directly from the Quartz job to the Layer 3 extractor. The job controls its own concurrency via `[DisallowConcurrentExecution]`.

### Anti-Pattern 3: Enabling ServicesStartConcurrently

**What people do:** Set `ServicesStartConcurrently = true` in `HostOptions` for faster startup.
**Why it's wrong:** Simetra's 11-step startup sequence has dependencies (lease before listener, listener before scheduler). Concurrent start breaks these dependencies. The SnmpListener could start receiving traps before the correlationId service is initialized.
**Do this instead:** Keep sequential startup (the default). The 11-step sequence takes <5 seconds total, so the startup time saving from concurrency is negligible.

### Anti-Pattern 4: Using HTTP Middleware for Pipeline Processing

**What people do:** Route trap/poll data through ASP.NET HTTP middleware by making internal HTTP requests to the same process.
**Why it's wrong:** Massive overhead -- HTTP serialization/deserialization for in-process data flow. Adds latency, complexity, and makes the system depend on HTTP infrastructure for core pipeline functionality.
**Do this instead:** Build a custom non-HTTP middleware chain using the delegate pattern. Same composability, zero HTTP overhead.

### Anti-Pattern 5: Hot-Swapping OpenTelemetry Exporters

**What people do:** Try to rebuild `MeterProvider` or swap exporters at runtime when leader/follower role changes.
**Why it's wrong:** `MeterProvider` is designed to be built once at startup. Rebuilding it loses accumulated metric state, can cause race conditions, and is not supported by the OTel SDK.
**Do this instead:** Use the `RoleGatedExporter` decorator pattern. The exporter is built once, but the `Export()` method checks `IsLeader` on every export cycle. Role changes take effect immediately without provider reconstruction.

### Anti-Pattern 6: Blocking Channel Writes in the Listener

**What people do:** Use `BoundedChannelFullMode.Wait` and call `WriteAsync` from the SNMP listener, which blocks the listener thread when a channel is full.
**Why it's wrong:** The listener handles ALL devices on a single UDP socket. If one device's channel is full and the listener blocks, ALL trap processing halts -- including traps for other devices with empty channels.
**Do this instead:** Use `BoundedChannelFullMode.DropOldest` with `TryWrite` (synchronous, non-blocking). If the channel is full, the oldest item is dropped, and the listener continues processing the next trap immediately.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| SNMP Devices (v2c) | UDP listener on port 162 for traps; SNMP GET requests for polls | SharpSnmpLib (Lextm.SharpSnmpLib 12.x). Single community string. Socket timeout must be shorter than Quartz job timeout. |
| Kubernetes API | REST via `KubernetesClient` (v18+/v19) NuGet | In-cluster config (`InClusterConfig()`). Lease CRUD on `coordination.k8s.io/v1`. RBAC: `get, create, update, delete` on leases in app namespace. |
| OTLP Collector | gRPC (default) or HTTP/protobuf | `OpenTelemetry.Exporter.OpenTelemetryProtocol` NuGet. Endpoint from `Otlp:Endpoint` config. Role-gated for metrics/traces; always-on for logs. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Listener -> Routing | Method call (in-process) | `SnmpListenerService` calls `DeviceFilterService.Identify()` then `TrapFilterService.Filter()` synchronously |
| Routing -> Channel | `Channel<T>.Writer.TryWrite()` | Non-blocking. Returns bool. Caller (listener) never waits. |
| Channel -> Extraction | `Channel<T>.Reader.ReadAllAsync()` | Consumer BackgroundService awaits indefinitely via `IAsyncEnumerable<T>` |
| Extraction -> Processing | Method call (in-process) | Extractor returns `DomainObject`, caller (consumer or job) passes to both processing branches |
| LeaderElection -> Exporters | Shared `IsLeader` property | `RoleGatedExporter` reads `LeaderElectionService.IsLeader` on every export cycle. No events needed -- polling is sufficient at export interval frequency. |
| CorrelationService -> All | Shared `CurrentCorrelationId` property | Thread-safe read via `volatile` field. Written by CorrelationJob and at startup. Read by listener (on trap arrival) and jobs (before execution). |
| LivenessVector -> LivenessProbe | Method call from health check | `LivenessHealthCheck` calls `LivenessVectorService.GetStaleJobs()` on each probe request. Runs on ASP.NET thread pool, independent of Quartz. |

## Build Order Implications

The following dependency chain determines what should be built first:

```
Phase 1: Foundation (no dependencies)
  +-- Models (PollDefinitionDto, DomainObject, TrapContext, etc.)
  +-- Configuration Options classes
  +-- CorrelationService (standalone, generates GUIDs)
  +-- LivenessVectorService (standalone, ConcurrentDictionary)
  +-- StateVectorService (standalone, ConcurrentDictionary)

Phase 2: Infrastructure (depends on Phase 1 models)
  +-- Channel creation (depends on Models.TrapContext)
  +-- SnmpExtractorService (depends on Models.PollDefinitionDto, DomainObject)
  +-- DeviceFilterService (depends on Configuration)
  +-- TrapFilterService (depends on IDeviceModule interface)
  +-- MetricFactoryService (depends on Models, needs OTel Meter)

Phase 3: Device System (depends on Phase 1-2)
  +-- IDeviceModule interface + SimetraModule
  +-- DeviceModuleRegistry
  +-- Channel registry (creates channels per device from modules)

Phase 4: Pipeline Composition (depends on Phase 1-3)
  +-- Middleware chain (CorrelationId, Logging, ErrorHandling)
  +-- ChannelConsumerService (reads channel, runs middleware, calls extractor)
  +-- Processing orchestration (Branch A + Branch B routing)

Phase 5: Scheduled Work (depends on Phase 1-4)
  +-- SnmpPollerService (SNMP GET wrapper)
  +-- StatePollJob, MetricPollJob (use poller + extractor + processing)
  +-- HeartbeatJob (sends loopback trap)
  +-- CorrelationJob (generates correlationId + stamps liveness)
  +-- Quartz registration

Phase 6: Network + HA (depends on Phase 1-5)
  +-- SnmpListenerService (UDP socket, routes to channels)
  +-- LeaderElectionService (K8s Lease loop)
  +-- Role-gated exporters

Phase 7: Observability + Health (depends on Phase 1-6)
  +-- OpenTelemetry setup (MeterProvider, TracerProvider, LoggerProvider)
  +-- Health checks (startup, readiness, liveness)
  +-- Program.cs composition root (wires everything)

Phase 8: Startup/Shutdown Orchestration
  +-- 11-step startup sequence in Program.cs
  +-- Graceful shutdown with time-budgeted steps
  +-- Integration tests for full lifecycle
```

**Key dependency insight:** The extraction and processing layers (Phase 2) can be built and fully unit-tested before the listener, scheduler, or leader election exist. This enables a "pipeline-first" build strategy where core data transformation logic is proven before adding I/O complexity.

## Sources

- [.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) -- Microsoft official docs, updated 2026-02-04 (HIGH confidence)
- [IHostedLifecycleService in .NET 8](https://www.stevejgordon.co.uk/introducing-the-new-ihostedlifecycleservice-interface-in-dotnet-8) -- Steve Gordon analysis (HIGH confidence)
- [Concurrent Hosted Service Start/Stop](https://www.stevejgordon.co.uk/concurrent-hosted-service-start-and-stop-in-dotnet-8) -- Steve Gordon analysis (HIGH confidence)
- [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) -- Microsoft official docs, updated 2025-12-23 (HIGH confidence)
- [Quartz.NET Hosted Services Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html) -- Official Quartz docs (HIGH confidence)
- [Quartz.NET with ASP.NET Core](https://andrewlock.net/using-quartz-net-with-asp-net-core-and-worker-services/) -- Andrew Lock guide (HIGH confidence)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) -- Microsoft official docs (HIGH confidence)
- [OpenTelemetry .NET Exporters](https://opentelemetry.io/docs/languages/dotnet/exporters/) -- Official OTel docs (HIGH confidence)
- [Kubernetes Leases](https://kubernetes.io/docs/concepts/architecture/leases/) -- Official K8s docs (HIGH confidence)
- [KubernetesClient .NET](https://github.com/kubernetes-client/csharp) -- Official K8s .NET client (HIGH confidence for API surface, MEDIUM for Lease-specific usage patterns)
- [SharpSnmpLib](https://github.com/lextudio/sharpsnmplib) -- Official repo (HIGH confidence)
- [PipelineNet](https://github.com/ipvalverde/PipelineNet) -- Community middleware framework (MEDIUM confidence, referenced for pattern validation)
- [Pipeline Design Pattern in .NET](https://medium.com/pragmatic-programming/net-things-pipeline-design-pattern-bb27e65e741e) -- Community pattern (MEDIUM confidence)

---
*Architecture research for: Simetra headless .NET SNMP supervisor service*
*Researched: 2026-02-15*
