# Phase 6: Scheduling System - Research

**Researched:** 2026-02-15
**Domain:** Quartz.NET job scheduling + SNMP polling + liveness vector + correlation rotation
**Confidence:** HIGH

## Summary

Phase 6 integrates Quartz.NET into the existing .NET 9 SNMP pipeline to execute four categories of scheduled work: state poll jobs (Source=Module), metric poll jobs (Source=Configuration), a heartbeat job (loopback trap to the SNMP listener), and a correlation job (rotate correlationId + stamp liveness). Each job reads the current correlationId before execution and stamps a liveness vector entry on completion.

The standard approach is: install `Quartz.AspNetCore` 3.15.1 (which bundles `Quartz`, `Quartz.Extensions.DependencyInjection`, and `Quartz.Extensions.Hosting`), configure it via `AddQuartz` + `AddQuartzHostedService` in the DI pipeline, use `[DisallowConcurrentExecution]` on all job classes, and set misfire handling to "skip stale" behavior on all triggers. The liveness vector is a new `ConcurrentDictionary<string, DateTimeOffset>` service stamped only by job completion. The `ICorrelationService` interface must be extended to support `SetCorrelationId()` so the correlation job can rotate the ID and startup code can set the first one before Quartz starts.

**Primary recommendation:** Use Quartz.AspNetCore 3.15.1 with in-memory RAMJobStore, register all jobs/triggers at startup via `AddQuartz` fluent API, and use `WithMisfireHandlingInstructionNextWithRemainingCount()` on all SimpleTrigger schedules (the SimpleTrigger equivalent of "DoNothing" -- skip stale, wait for next).

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Quartz.AspNetCore | 3.15.1 | Job scheduling with ASP.NET Core integration | Official Quartz.NET ASP.NET Core package; includes DI, hosted service, and health check support; explicit .NET 9 TFM |
| Lextm.SharpSnmpLib | 12.5.7 (existing) | SNMP GET polling + trap sending | Already in project; provides `Messenger.GetAsync` for polls and `Messenger.SendTrapV2` for heartbeat |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Quartz.Extensions.DependencyInjection | 3.15.1 | Microsoft DI integration for Quartz | Transitive via Quartz.AspNetCore; provides `AddQuartz()` |
| Quartz.Extensions.Hosting | 3.15.1 | Hosted service wrapper for Quartz scheduler | Transitive via Quartz.AspNetCore; provides `AddQuartzHostedService()` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Quartz.NET | System.Threading.Timer + BackgroundService | Simpler for trivial cases but loses DisallowConcurrentExecution, misfire handling, job keys, and scheduler lifecycle management -- all required by SCHED-01/SCHED-09/SCHED-10 |
| Quartz.NET | Hangfire | Requires a persistent store (SQL Server/Redis); overkill for in-memory job scheduling with ~10 jobs |
| Quartz.AspNetCore | Quartz + manual hosted service | Quartz.AspNetCore bundles health checks and proper hosted service wiring; no benefit to doing it manually |

**Installation:**
```bash
dotnet add src/Simetra/Simetra.csproj package Quartz.AspNetCore --version 3.15.1
```

## Architecture Patterns

### Recommended Project Structure
```
src/Simetra/
├── Jobs/                           # NEW -- Quartz job implementations
│   ├── StatePollJob.cs             # SCHED-02: per-device state poll (Source=Module)
│   ├── MetricPollJob.cs            # SCHED-03: per-device metric poll (Source=Configuration)
│   ├── HeartbeatJob.cs             # SCHED-05/06: loopback trap sender
│   └── CorrelationJob.cs           # SCHED-07: correlationId rotation + liveness stamp
├── Services/
│   └── LivenessVectorService.cs    # NEW -- LIFE-02/SCHED-08: per-job completion timestamps
├── Pipeline/
│   ├── ICorrelationService.cs      # MODIFIED -- add SetCorrelationId()
│   ├── StartupCorrelationService.cs# REPLACED -- by RotatingCorrelationService
│   ├── RotatingCorrelationService.cs # NEW -- thread-safe get/set with volatile field
│   ├── ILivenessVectorService.cs   # NEW -- interface for liveness vector
│   └── ...existing files...
├── Extensions/
│   └── ServiceCollectionExtensions.cs # MODIFIED -- add AddScheduling() method
└── Program.cs                       # MODIFIED -- call AddScheduling(), generate first correlationId
```

### Pattern 1: Quartz DI Registration with Dynamic Job/Trigger Creation
**What:** Register Quartz in DI, then dynamically create one job+trigger per device per poll definition at startup.
**When to use:** When the number and configuration of jobs is determined by runtime configuration (devices + poll definitions).
**Example:**
```csharp
// Source: Quartz.NET official DI docs + ASP.NET Core example
// https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html
builder.Services.AddQuartz(q =>
{
    q.UseInMemoryStore();

    // Static jobs: heartbeat + correlation
    q.AddJob<HeartbeatJob>(j => j
        .WithIdentity("heartbeat-job")
        .StoreDurably());

    q.AddTrigger(t => t
        .ForJob("heartbeat-job")
        .WithIdentity("heartbeat-trigger")
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(heartbeatInterval)
            .RepeatForever()
            .WithMisfireHandlingInstructionNextWithRemainingCount()));

    // Dynamic jobs: one per device per poll definition
    foreach (var device in allDevices)
    {
        foreach (var poll in device.StatePollDefinitions)
        {
            var jobKey = new JobKey($"state-poll-{device.Name}-{poll.MetricName}");
            q.AddJob<StatePollJob>(j => j
                .WithIdentity(jobKey)
                .UsingJobData("deviceName", device.Name)
                .UsingJobData("metricName", poll.MetricName));

            q.AddTrigger(t => t
                .ForJob(jobKey)
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(poll.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));
        }
    }
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```

### Pattern 2: Job with DI Constructor Injection + JobDataMap Parameters
**What:** Jobs receive services via constructor injection (Quartz DI resolves them) and per-instance configuration via JobDataMap.
**When to use:** All poll jobs need shared services (extractor, coordinator, correlation, liveness) plus device-specific parameters.
**Example:**
```csharp
// Source: Quartz.NET docs -- jobs are scoped, DI resolves constructor params
// https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-jobs.html
[DisallowConcurrentExecution]
public sealed class StatePollJob : IJob
{
    private readonly ISnmpExtractor _extractor;
    private readonly IProcessingCoordinator _coordinator;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<StatePollJob> _logger;

    public StatePollJob(
        ISnmpExtractor extractor,
        IProcessingCoordinator coordinator,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        ILogger<StatePollJob> logger)
    {
        _extractor = extractor;
        _coordinator = coordinator;
        _correlation = correlation;
        _liveness = liveness;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var correlationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.ToString();
        try
        {
            var deviceName = context.MergedJobDataMap.GetString("deviceName")!;
            var metricName = context.MergedJobDataMap.GetString("metricName")!;
            // ... poll device, extract, process ...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State poll job {JobKey} failed", jobKey);
        }
        finally
        {
            _liveness.Stamp(jobKey);
        }
    }
}
```

### Pattern 3: RotatingCorrelationService with Thread-Safe Read/Write
**What:** Replace `StartupCorrelationService` with a service that supports atomic correlation ID rotation.
**When to use:** SCHED-07/LIFE-02 require generating the first correlationId at startup and rotating it on a schedule.
**Example:**
```csharp
public sealed class RotatingCorrelationService : ICorrelationService
{
    private volatile string _correlationId = string.Empty;

    public string CurrentCorrelationId => _correlationId;

    public void SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
    }
}
```

### Pattern 4: Heartbeat Job Sends Loopback Trap via SharpSnmpLib
**What:** HeartbeatJob constructs an SNMP v2c trap with the HeartbeatOid from SimetraModule and sends it to 127.0.0.1 on the configured listener port.
**When to use:** SCHED-05/SCHED-06 require the heartbeat to flow through the full pipeline.
**Example:**
```csharp
// Source: SharpSnmpLib API -- Messenger.SendTrapV2
// https://help.sharpsnmp.com/html/Overload_Lextm_SharpSnmpLib_Messaging_Messenger_SendTrapV2.htm
[DisallowConcurrentExecution]
public sealed class HeartbeatJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var correlationId = _correlation.CurrentCorrelationId;
        try
        {
            var oid = SimetraModule.HeartbeatOid; // single source of truth
            var variables = new List<Variable>
            {
                new(new ObjectIdentifier(oid), new Integer32(1))
            };

            var endpoint = new IPEndPoint(IPAddress.Loopback, _listenerPort);
            Messenger.SendTrapV2(
                requestId: 0,
                version: VersionCode.V2,
                receiver: endpoint,
                community: new OctetString(_communityString),
                enterprise: new ObjectIdentifier(oid),
                timestamp: 0,
                variables: variables);
        }
        finally
        {
            _liveness.Stamp(context.JobDetail.Key.ToString());
        }
    }
}
```

### Pattern 5: LivenessVectorService -- ConcurrentDictionary of Timestamps
**What:** Simple in-memory dictionary keyed by job key string, valued by last-completion DateTimeOffset.
**When to use:** SCHED-08/SCHED-09 require per-job completion timestamps for liveness probe checking.
**Example:**
```csharp
public sealed class LivenessVectorService : ILivenessVectorService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stamps = new();

    public void Stamp(string jobKey)
    {
        _stamps[jobKey] = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset? GetStamp(string jobKey)
    {
        return _stamps.TryGetValue(jobKey, out var ts) ? ts : null;
    }

    public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
    {
        return _stamps.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
```

### Anti-Patterns to Avoid
- **Injecting IScheduler directly into jobs to schedule sub-jobs:** Jobs should be stateless executors, not schedulers. All job+trigger registration happens at startup in `AddQuartz`.
- **Using CronTrigger for simple fixed-interval jobs:** SimpleTrigger with `RepeatForever()` is simpler and more appropriate for fixed-interval polling. CronTrigger is for calendar-based schedules.
- **Stamping liveness vector inside try block:** The stamp MUST be in the finally block so it always executes, even when the job body throws. A missing stamp signals a hung/stuck job to the liveness probe (SCHED-09).
- **Sending heartbeat trap asynchronously and awaiting receipt:** The heartbeat SEND job only proves the scheduler is alive. Receipt is proven by the trap flowing through the pipeline. Do not couple send and receive.
- **Storing PollDefinitionDto in JobDataMap as serialized object:** JobDataMap values should be primitive strings. Store device name + metric name as strings; look up the full PollDefinitionDto from services at job execution time.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Concurrent execution prevention | Custom locking/semaphore per job | `[DisallowConcurrentExecution]` attribute on job class | Quartz handles this atomically at the scheduler level including across misfires |
| Misfire handling (skip stale triggers) | Timer-based "if overdue, skip" logic | `WithMisfireHandlingInstructionNextWithRemainingCount()` on trigger | Quartz evaluates misfires correctly even after process restart or long GC pauses |
| Job lifecycle management | Custom BackgroundService per job type | `AddQuartzHostedService` | Handles startup, shutdown, WaitForJobsToComplete, cancellation token propagation |
| Thread-safe correlation ID rotation | Lock-based get/set | `volatile` string field (single writer, multiple readers) | Correlation job is the only writer; `volatile` guarantees visibility across threads without lock contention |
| SNMP GET polling | Raw UDP socket construction | `Messenger.GetAsync(VersionCode.V2, endpoint, community, variables, ct)` | SharpSnmpLib handles SNMP PDU encoding/decoding, request IDs, timeout, and response correlation |
| SNMP v2c trap sending | Raw UDP packet construction | `Messenger.SendTrapV2(requestId, version, receiver, community, enterprise, timestamp, variables)` | SharpSnmpLib handles TrapV2 PDU encoding per RFC 3416 |

**Key insight:** Quartz.NET provides the exact concurrency and misfire semantics required by SCHED-01, SCHED-09, and SCHED-10. Building these from scratch with timers would require reimplementing scheduler-level concerns that Quartz already handles correctly.

## Common Pitfalls

### Pitfall 1: DisallowConcurrentExecution Scoping
**What goes wrong:** Developers assume `[DisallowConcurrentExecution]` prevents all instances of a job CLASS from running concurrently. It actually prevents concurrent execution per JobKey (identity), not per class.
**Why it happens:** The attribute name implies class-level scoping.
**How to avoid:** Each unique JobKey (e.g., `state-poll-router-core-1-simetra_cpu`) gets its own concurrency lock. Two different devices can poll simultaneously, which is the desired behavior. One device's long poll does not block another device's poll.
**Warning signs:** If you see all polls executing sequentially instead of in parallel, you likely registered all polls under the same JobKey.

### Pitfall 2: Forgetting First CorrelationId Before Scheduler Starts
**What goes wrong:** If the first correlationId is generated by the CorrelationJob (via scheduler), there is a window where jobs fire with an empty/default correlationId.
**Why it happens:** Quartz scheduler startup is asynchronous; the CorrelationJob may not be the first job to fire.
**How to avoid:** LIFE-02 requires generating the first correlationId DIRECTLY in Program.cs (or a startup hosted service) before calling `app.Run()`. Set it via `ICorrelationService.SetCorrelationId()` immediately after building the service provider.
**Warning signs:** Log entries with empty or "startup-" prefixed correlationIds after the first few seconds of operation.

### Pitfall 3: SimpleTrigger Misfire vs CronTrigger "DoNothing"
**What goes wrong:** The requirement says "DoNothing misfire handling" but DoNothing is a CronTrigger-specific instruction. Using it on SimpleTrigger causes a runtime error or unexpected SmartPolicy fallback.
**Why it happens:** Quartz.NET has different misfire instruction sets per trigger type. "DoNothing" exists only on `MisfirePolicy.CronTrigger`.
**How to avoid:** For SimpleTrigger with RepeatForever, use `WithMisfireHandlingInstructionNextWithRemainingCount()` -- this is the semantic equivalent of "DoNothing" (skip all stale fires, wait for next scheduled time). For indefinite-repeat triggers, `NextWithRemainingCount` and `NextWithExistingCount` produce identical behavior.
**Warning signs:** If you use default SmartPolicy on SimpleTrigger, it maps to `RescheduleNextWithRemainingCount` anyway for indefinite repeats, but being explicit is better for documentation and intent.

### Pitfall 4: Liveness Stamp in Try Block Instead of Finally
**What goes wrong:** If a job throws an exception and the liveness stamp is in the try block, the stamp is never written. The liveness probe then incorrectly detects a "stuck" job when it actually failed and returned.
**Why it happens:** Natural tendency to put all "job completion" logic in the happy path.
**How to avoid:** ALWAYS stamp liveness in the `finally` block. The liveness vector detects hung/stuck jobs, not failed jobs. A job that fails and returns is still "alive" from the scheduler's perspective. Failed jobs are detected via error logging, not liveness.
**Warning signs:** Liveness probe returning 503 after transient SNMP errors, even though the scheduler is functioning correctly.

### Pitfall 5: SNMP Poll Timeout Blocking Scheduler Thread Pool
**What goes wrong:** SNMP GET requests to unreachable devices block with default timeout, consuming Quartz thread pool threads.
**Why it happens:** SharpSnmpLib `Messenger.GetAsync` defaults may have long timeouts; combined with many device polls, this can exhaust the thread pool.
**How to avoid:** Set explicit SNMP timeout (e.g., 5 seconds) on poll requests. Use the CancellationToken overload of `Messenger.GetAsync`. Configure Quartz thread pool size appropriately (`UseDefaultThreadPool(maxConcurrency: N)` where N > number of concurrent poll jobs expected).
**Warning signs:** Multiple jobs showing as "executing" simultaneously in scheduler diagnostics, with the scheduler unable to start new jobs.

### Pitfall 6: DeviceInfo Not Available from IDeviceRegistry for Poll Jobs
**What goes wrong:** The current `IDeviceRegistry` only supports lookup by `IPAddress` (via `TryGetDevice`). Poll jobs need to look up `DeviceInfo` by device name (they get the name from JobDataMap, not an IP).
**Why it happens:** IDeviceRegistry was designed for the trap path (Layer 2) where the sender IP is known. Poll jobs know the device name, not the IP.
**How to avoid:** Either: (a) add a `TryGetDeviceByName(string name, out DeviceInfo?)` method to `IDeviceRegistry`, or (b) store the necessary DeviceInfo fields (IP, device type, poll definitions) in JobDataMap as strings during registration, or (c) pass the full device+definition context into the job via a dedicated service. Option (a) is cleanest as it extends the existing registry pattern.
**Warning signs:** Poll jobs needing to parse configuration options directly instead of using DeviceInfo.

## Code Examples

Verified patterns from official sources:

### Complete Quartz DI Registration
```csharp
// Source: Quartz.NET DI docs + ASP.NET Core example
// https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html
// https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples.AspNetCore/Startup.cs

builder.Services.AddQuartz(q =>
{
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(maxConcurrency: 10);

    // Heartbeat job -- single instance
    var heartbeatKey = new JobKey("heartbeat");
    q.AddJob<HeartbeatJob>(j => j.WithIdentity(heartbeatKey));
    q.AddTrigger(t => t
        .ForJob(heartbeatKey)
        .WithIdentity("heartbeat-trigger")
        .StartNow()
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(heartbeatIntervalSeconds)
            .RepeatForever()
            .WithMisfireHandlingInstructionNextWithRemainingCount()));

    // Correlation job -- single instance
    var correlationKey = new JobKey("correlation");
    q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
    q.AddTrigger(t => t
        .ForJob(correlationKey)
        .WithIdentity("correlation-trigger")
        .StartNow()
        .WithSimpleSchedule(s => s
            .WithIntervalInSeconds(correlationIntervalSeconds)
            .RepeatForever()
            .WithMisfireHandlingInstructionNextWithRemainingCount()));
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```

### SNMP GET Poll with SharpSnmpLib
```csharp
// Source: SharpSnmpLib API
// https://help.sharpsnmp.com/html/M_Lextm_SharpSnmpLib_Messaging_Messenger_GetAsync.htm

var variables = definition.Oids
    .Select(o => new Variable(new ObjectIdentifier(o.Oid)))
    .ToList();

var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
var community = new OctetString(communityString);

IList<Variable> response = await Messenger.GetAsync(
    VersionCode.V2,
    endpoint,
    community,
    variables,
    context.CancellationToken);

// Response varbinds feed directly into the existing generic extractor
var result = _extractor.Extract(response, definition);
_coordinator.Process(result, device, correlationId);
```

### SNMP v2c Trap Send for Heartbeat
```csharp
// Source: SharpSnmpLib API
// https://help.sharpsnmp.com/html/T_Lextm_SharpSnmpLib_Messaging_Messenger.htm

var heartbeatOid = SimetraModule.HeartbeatOid; // single source of truth
var variables = new List<Variable>
{
    new(new ObjectIdentifier(heartbeatOid), new Integer32(1))
};

var receiver = new IPEndPoint(IPAddress.Loopback, listenerPort);
Messenger.SendTrapV2(
    requestId: 0,
    version: VersionCode.V2,
    receiver: receiver,
    community: new OctetString(communityString),
    enterprise: new ObjectIdentifier(heartbeatOid),
    timestamp: 0,
    variables: variables);
```

### Job Execution Pattern with CorrelationId + Liveness Stamp
```csharp
// Source: Quartz.NET job docs + project conventions
// https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-jobs.html

[DisallowConcurrentExecution]
public sealed class ExamplePollJob : IJob
{
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<ExamplePollJob> _logger;

    // Constructor injection -- Quartz DI resolves all params
    public ExamplePollJob(
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        ILogger<ExamplePollJob> logger)
    {
        _correlation = correlation;
        _liveness = liveness;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // SCHED-08: Read correlationId BEFORE execution
        var correlationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.ToString();

        try
        {
            // Job work here...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobKey} failed [CorrelationId: {CorrelationId}]",
                jobKey, correlationId);
        }
        finally
        {
            // SCHED-08: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
        }
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Custom IJobFactory for DI | `AddQuartz()` with built-in DI support | Quartz.NET 3.3.2+ | Jobs are automatically scoped; no custom factory needed |
| Manual `IScheduler` wiring | `AddQuartzHostedService()` | Quartz.NET 3.2+ | Hosted service handles scheduler lifecycle automatically |
| Quartz 3.x targets .NET Standard 2.0 only | Quartz 3.15.1 has explicit net8.0 + net9.0 TFMs | 2024-2025 | Better integration with modern .NET framework-specific APIs |
| Separate Quartz + Quartz.Extensions.DI + Quartz.Extensions.Hosting packages | `Quartz.AspNetCore` meta-package | Quartz.NET 3.6+ | Single package reference bundles all needed integration |

**Deprecated/outdated:**
- `Quartz.Simpl.RAMJobStore` (explicit configuration): Since Quartz 3.x, `UseInMemoryStore()` is the preferred API; RAMJobStore is still used internally but configured via the fluent method.
- Manual `StdSchedulerFactory`: Replaced by `ServiceCollectionSchedulerFactory` when using DI integration.

## Open Questions

1. **DeviceInfo lookup by name for poll jobs**
   - What we know: Current `IDeviceRegistry` only supports `TryGetDevice(IPAddress)`. Poll jobs know device name (from JobDataMap) but need full `DeviceInfo` for `IProcessingCoordinator.Process()`.
   - What's unclear: Whether to extend `IDeviceRegistry` with a name-based lookup, or use an alternative approach (e.g., a dedicated poll context service, or storing IP in JobDataMap and using existing lookup).
   - Recommendation: Add `TryGetDeviceByName(string name, out DeviceInfo?)` to `IDeviceRegistry` and its implementation. This is the simplest extension that maintains the existing pattern. Alternatively, since `DeviceRegistry` already has a `_devices` dictionary, adding a second index by name is trivial.

2. **Poll definition lookup at job execution time**
   - What we know: Jobs receive device name + metric name via JobDataMap. They need the full `PollDefinitionDto` (with OIDs) to construct the SNMP GET request.
   - What's unclear: Where to look up the PollDefinitionDto -- from DeviceInfo.TrapDefinitions, from module StatePollDefinitions, or from a unified registry.
   - Recommendation: For state poll jobs (Source=Module), the definitions come from `IDeviceModule.StatePollDefinitions`. For metric poll jobs (Source=Configuration), the definitions come from `DeviceOptions.MetricPolls` (converted to DTOs). A dedicated `IPollDefinitionRegistry` service that indexes all poll definitions by `(deviceName, metricName)` key would centralize lookup. Alternatively, store all needed data in a service keyed by device+metric at registration time so jobs can look it up.

3. **SNMP community string for poll requests**
   - What we know: The SNMP listener uses `SnmpListenerOptions.CommunityString` for trap validation. Poll requests also need a community string.
   - What's unclear: Whether poll requests should use the same community string from SnmpListenerOptions, or if a per-device community string is needed.
   - Recommendation: Use `SnmpListenerOptions.CommunityString` for all poll requests (v1 requirements state single community string). Per-device community strings are SEC-02 (v2 scope, explicitly deferred).

4. **SNMP poll timeout configuration**
   - What we know: `Messenger.GetAsync` needs a timeout. No dedicated timeout config exists in current options.
   - What's unclear: Whether to add a new config option or use a sensible hardcoded default.
   - Recommendation: Use a hardcoded default of 5000ms for SNMP poll timeout. This is simple and sufficient for v1. A dedicated config option can be added in v2 if needed.

## Sources

### Primary (HIGH confidence)
- [Quartz.AspNetCore 3.15.1 on NuGet](https://www.nuget.org/packages/Quartz.AspNetCore) -- version, TFMs (.NET 8/9/netstandard2.0), dependencies
- [Quartz.NET Microsoft DI Integration docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) -- AddQuartz, AddQuartzHostedService, job resolution from DI, scoped jobs
- [Quartz.NET Jobs docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-jobs.html) -- DisallowConcurrentExecution per JobKey, JobDataMap, IJob.Execute
- [Quartz.NET SimpleTrigger docs](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/simpletriggers.html) -- misfire instructions for SimpleTrigger, RepeatForever
- [Quartz.NET ASP.NET Core example on GitHub](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples.AspNetCore/Startup.cs) -- complete DI registration pattern
- [SharpSnmpLib Messenger.GetAsync API](https://help.sharpsnmp.com/html/M_Lextm_SharpSnmpLib_Messaging_Messenger_GetAsync.htm) -- async SNMP GET method signature
- [SharpSnmpLib Messenger.cs source](https://github.com/lextudio/sharpsnmplib/blob/master/SharpSnmpLib/Messaging/Messenger.cs) -- SendTrapV2 and SendTrapV2Async signatures

### Secondary (MEDIUM confidence)
- [Quartz.NET misfire instructions explained (Nurkiewicz)](https://nurkiewicz.com/2012/04/quartz-scheduler-misfire-instructions.html) -- RescheduleNextWithRemainingCount is DoNothing equivalent for indefinite SimpleTrigger
- [Andrew Lock: Using Quartz.NET with ASP.NET Core](https://andrewlock.net/using-quartz-net-with-asp-net-core-and-worker-services/) -- DI integration patterns

### Tertiary (LOW confidence)
- None -- all findings verified with primary or secondary sources.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- Quartz.AspNetCore 3.15.1 verified on NuGet with explicit .NET 9 TFM; SharpSnmpLib already in project
- Architecture: HIGH -- Patterns derived from official Quartz.NET docs and existing codebase analysis
- Pitfalls: HIGH -- Misfire instruction mapping verified across multiple sources; DisallowConcurrentExecution per-JobKey confirmed in official docs
- Code examples: HIGH -- API signatures verified from official documentation and source code

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (Quartz.NET stable release cadence, SharpSnmpLib 12.x stable)
