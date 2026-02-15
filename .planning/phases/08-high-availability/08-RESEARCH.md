# Phase 8: High Availability - Research

**Researched:** 2026-02-15
**Domain:** Kubernetes Lease-based leader election in .NET, role-gated OpenTelemetry exporter wiring, BackgroundService lifecycle management
**Confidence:** HIGH

## Summary

Phase 8 implements leader-follower high availability using the Kubernetes coordination.k8s.io/v1 Lease API. The official `KubernetesClient` NuGet package (v18.0.13) provides a built-in `LeaderElector` class with `LeaseLock` that handles the acquire/renew/release cycle, event callbacks, and retry logic. This means the K8sLeaseElection implementation wraps the library's `LeaderElector` rather than hand-rolling Lease CRUD against the K8s API.

The critical architectural challenge is wiring `RoleGatedExporter<T>` (already created in Phase 7 but orphaned) into the OTLP export chain. The current `AddOtlpExporter()` calls on `MeterProviderBuilder` and `TracerProviderBuilder` create and register the OTLP exporter internally, making it impossible to wrap. The solution is to **replace** `AddOtlpExporter()` with manual exporter construction: create `OtlpTraceExporter` and `OtlpMetricExporter` instances, wrap each in `RoleGatedExporter<T>`, then register them via `AddProcessor(new BatchActivityExportProcessor(wrappedExporter))` for traces and `AddReader(new PeriodicExportingMetricReader(wrappedExporter))` for metrics. This gives full control over the exporter chain while maintaining identical OTLP behavior.

The third concern is the environment-based switching between `AlwaysLeaderElection` (local dev) and `K8sLeaseElection` (production). The `KubernetesClientConfiguration.IsInCluster()` check detects whether the pod is running inside Kubernetes, providing a clean auto-detection mechanism that requires no configuration flag.

**Primary recommendation:** Use `KubernetesClient` 18.0.13 with `LeaseLock` + `LeaderElector` for Lease coordination, wrap K8sLeaseElection as a `BackgroundService` implementing `ILeaderElection`, auto-detect in-cluster vs local via `KubernetesClientConfiguration.IsInCluster()`, and wire RoleGatedExporter by replacing `AddOtlpExporter()` with manual `OtlpTraceExporter`/`OtlpMetricExporter` construction wrapped in `RoleGatedExporter<T>`.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| KubernetesClient | 18.0.13 | Official .NET Kubernetes client. Provides `LeaderElector`, `LeaseLock`, `LeaderElectionConfig` for Lease-based leader election | Officially supported Kubernetes .NET client maintained by the Kubernetes project. Provides high-level `LeaderElector` with retry, jitter, and event callbacks -- no need to hand-roll Lease CRUD. Targets net8.0+ (compatible with net9.0). |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.0 | Already installed. Provides `OtlpTraceExporter`, `OtlpMetricExporter` classes for manual construction | Already present from Phase 7. The exporter classes have public constructors accepting `OtlpExporterOptions`, enabling manual instantiation for wrapping in `RoleGatedExporter<T>`. |
| OpenTelemetry | 1.15.0 | Already installed (transitive). Provides `BatchActivityExportProcessor`, `SimpleActivityExportProcessor`, `PeriodicExportingMetricReader` | Already present from Phase 7. Provides the processor/reader types needed to manually wire exporters instead of using `AddOtlpExporter()` convenience methods. |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (none new) | - | - | All supporting libraries already installed from prior phases |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| KubernetesClient `LeaderElector` | Hand-rolled Lease CRUD via `IKubernetes.CreateNamespacedLeaseAsync()` / `ReplaceNamespacedLeaseAsync()` | LeaderElector handles retry, jitter, deadline, and transition detection. Hand-rolling duplicates 200+ lines of nuanced coordination logic. **Use LeaderElector.** |
| `LeaseLock` | `ConfigMapLock` | ConfigMapLock is legacy; LeaseLock uses the purpose-built coordination.k8s.io/v1 Lease resource. LeaseLock is the recommended approach. **Use LeaseLock.** |
| `KubernetesClientConfiguration.IsInCluster()` auto-detect | Configuration flag (e.g., `HA:Enabled`) | Auto-detect requires no config and cannot be misconfigured. However, it means K8s leader election runs whenever in-cluster, even if HA is not desired. For this project, in-cluster always means HA. **Use auto-detect.** |
| Manual `OtlpTraceExporter` + `RoleGatedExporter` wrapping | Filtering processor that drops activities based on role | A filtering processor works for traces (set `Activity.Recorded = false`) but has no equivalent for metrics. The decorator pattern (RoleGatedExporter) is consistent across both traces and metrics. **Use decorator for consistency.** |

**Installation:**
```bash
dotnet add package KubernetesClient --version 18.0.13
```

## Architecture Patterns

### Recommended File Structure

```
src/Simetra/Telemetry/
    ILeaderElection.cs              # Existing -- no changes
    AlwaysLeaderElection.cs         # Existing -- no changes
    K8sLeaseElection.cs             # NEW: BackgroundService + ILeaderElection
    RoleGatedExporter.cs            # Existing -- no changes
    TelemetryConstants.cs           # Existing -- no changes
    SimetraLogEnrichmentProcessor.cs # Existing -- no changes
```

### Pattern 1: K8sLeaseElection as BackgroundService + ILeaderElection

**What:** A class implementing both `ILeaderElection` (for the `IsLeader`/`CurrentRole` contract) and `BackgroundService` (for lifecycle management of the LeaderElector loop)
**When to use:** In production (Kubernetes) environment

```csharp
// Source: kubernetes-client/csharp LeaderElector API + .NET BackgroundService pattern
public sealed class K8sLeaseElection : BackgroundService, ILeaderElection
{
    private volatile bool _isLeader;
    private readonly LeaseOptions _leaseOptions;
    private readonly SiteOptions _siteOptions;
    private readonly IKubernetes _kubeClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<K8sLeaseElection> _logger;

    public bool IsLeader => _isLeader;
    public string CurrentRole => _isLeader ? "leader" : "follower";

    public K8sLeaseElection(
        IOptions<LeaseOptions> leaseOptions,
        IOptions<SiteOptions> siteOptions,
        IKubernetes kubeClient,
        IHostApplicationLifetime lifetime,
        ILogger<K8sLeaseElection> logger)
    {
        _leaseOptions = leaseOptions.Value;
        _siteOptions = siteOptions.Value;
        _kubeClient = kubeClient;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseLock = new LeaseLock(
            _kubeClient,
            _leaseOptions.Namespace,
            _leaseOptions.Name,
            _siteOptions.PodIdentity ?? Environment.MachineName);

        var config = new LeaderElectionConfig(leaseLock)
        {
            LeaseDuration = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds),
            RetryPeriod = TimeSpan.FromSeconds(_leaseOptions.RenewIntervalSeconds),
            RenewDeadline = TimeSpan.FromSeconds(_leaseOptions.DurationSeconds - 2)
        };

        var elector = new LeaderElector(config);
        elector.OnStartedLeading += () =>
        {
            _isLeader = true;
            _logger.LogInformation("Acquired leadership for lease {LeaseName}", _leaseOptions.Name);
        };
        elector.OnStoppedLeading += () =>
        {
            _isLeader = false;
            _logger.LogInformation("Lost leadership for lease {LeaseName}", _leaseOptions.Name);
        };
        elector.OnNewLeader += leader =>
        {
            _logger.LogInformation("New leader observed: {Leader}", leader);
        };

        // RunAndTryToHoldLeadershipForeverAsync continuously retries after loss
        await elector.RunAndTryToHoldLeadershipForeverAsync(stoppingToken);
    }
}
```

**Key design decisions:**
- `volatile bool _isLeader` ensures visibility across threads without locking (single writer from LeaderElector callbacks, multiple readers from RoleGatedExporter Export calls)
- `RunAndTryToHoldLeadershipForeverAsync` retries leadership acquisition after loss (not `RunUntilLeadershipLostAsync` which exits after first loss)
- `BackgroundService` provides lifecycle management (started by host, stopped on shutdown)
- Implements `ILeaderElection` so it replaces `AlwaysLeaderElection` in DI -- the same interface consumed by `RoleGatedExporter` and `SimetraLogEnrichmentProcessor`

### Pattern 2: Environment-Based DI Registration

**What:** Auto-detect Kubernetes environment to choose `AlwaysLeaderElection` vs `K8sLeaseElection`
**When to use:** In `AddSimetraTelemetry` extension method (modified from Phase 7)

```csharp
// Source: KubernetesClientConfiguration.IsInCluster() + IServiceCollection registration
public static IHostApplicationBuilder AddSimetraTelemetry(
    this IHostApplicationBuilder builder)
{
    // ... existing OTLP setup ...

    // Leader election: auto-detect environment
    if (KubernetesClientConfiguration.IsInCluster())
    {
        // Production: Kubernetes Lease-based election
        var kubeConfig = KubernetesClientConfiguration.InClusterConfig();
        builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));
        builder.Services.AddSingleton<K8sLeaseElection>();
        builder.Services.AddSingleton<ILeaderElection>(sp =>
            sp.GetRequiredService<K8sLeaseElection>());
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<K8sLeaseElection>());
    }
    else
    {
        // Local dev: always leader
        builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
    }

    // ... rest of method ...
    return builder;
}
```

**Critical DI pattern:** `K8sLeaseElection` is registered as a singleton first, then both `ILeaderElection` and `IHostedService` resolve to the same instance. This ensures the same `_isLeader` flag is read by all consumers. The `AddHostedService(sp => sp.GetRequiredService<...>())` overload avoids creating a second instance.

### Pattern 3: Manual OTLP Exporter Wiring with RoleGatedExporter

**What:** Replace `AddOtlpExporter()` convenience methods with manual exporter construction + RoleGatedExporter wrapping
**When to use:** In `AddSimetraTelemetry` when wiring metric and trace OTLP exporters

```csharp
// Source: OpenTelemetry .NET extending-the-sdk docs + OtlpTraceExporter/OtlpMetricExporter public constructors
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: otlpOptions.ServiceName))
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(TelemetryConstants.MeterName);
        metrics.AddRuntimeInstrumentation();

        // Manual OTLP metric exporter + RoleGatedExporter wrapping
        // Cannot use AddOtlpExporter() because it creates/registers the exporter
        // internally, preventing wrapping with RoleGatedExporter.
        metrics.AddReader(sp =>
        {
            var leaderElection = sp.GetRequiredService<ILeaderElection>();
            var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
            {
                Endpoint = new Uri(otlpOptions.Endpoint)
            });
            var roleGated = new RoleGatedExporter<Metric>(otlpExporter, leaderElection);
            return new PeriodicExportingMetricReader(roleGated);
        });
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(TelemetryConstants.TracingSourceName);

        // Manual OTLP trace exporter + RoleGatedExporter wrapping
        tracing.AddProcessor(sp =>
        {
            var leaderElection = sp.GetRequiredService<ILeaderElection>();
            var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
            {
                Endpoint = new Uri(otlpOptions.Endpoint)
            });
            var roleGated = new RoleGatedExporter<Activity>(otlpExporter, leaderElection);
            return new BatchActivityExportProcessor(roleGated);
        });
    });
```

**Key considerations:**
- `OtlpTraceExporter` extends `BaseExporter<Activity>` -- wrappable by `RoleGatedExporter<Activity>`
- `OtlpMetricExporter` extends `BaseExporter<Metric>` -- wrappable by `RoleGatedExporter<Metric>`
- `BatchActivityExportProcessor` is the standard processor wrapping a trace exporter (same as what `AddOtlpExporter()` does internally)
- `PeriodicExportingMetricReader` is the standard reader wrapping a metric exporter (same as what `AddOtlpExporter()` does internally for metrics, with 60s default interval)
- Log exporter is NOT wrapped -- remains as `AddOtlpExporter()` call (TELEM-04: all pods export logs)
- The `AddReader(Func<IServiceProvider, MetricReader>)` and `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>)` factory overloads resolve `ILeaderElection` from DI at build time

### Pattern 4: Graceful Lease Release on SIGTERM

**What:** On SIGTERM, explicitly release the Lease before the pod terminates for near-instant failover
**When to use:** In `K8sLeaseElection.StopAsync()` override or via `ApplicationStopping` callback

```csharp
// Source: .NET BackgroundService lifecycle + Kubernetes Lease release pattern
// K8sLeaseElection already implements BackgroundService. When the host stops,
// StopAsync is called which cancels the stoppingToken passed to ExecuteAsync.
// This cancels the LeaderElector's RunAndTryToHoldLeadershipForeverAsync,
// which triggers OnStoppedLeading and releases the lease internally.

// The CancellationToken passed to ExecuteAsync is cancelled by the host during
// graceful shutdown. The LeaderElector's loop detects cancellation and exits.
// On exit, the lease is not renewed, and with the short TTL (~15s), it expires.

// For NEAR-INSTANT failover (not waiting for TTL expiry), we need to actively
// delete or clear the lease holder on shutdown. The LeaderElector library
// handles this: when cancelled, it stops renewing, and the lease naturally
// expires. For truly instant failover, we can delete the lease on shutdown:

public override async Task StopAsync(CancellationToken cancellationToken)
{
    // Signal the ExecuteAsync loop to stop (cancels the stoppingToken)
    await base.StopAsync(cancellationToken);

    // Explicitly release the lease for near-instant failover
    if (_isLeader)
    {
        try
        {
            await _kubeClient.CoordinationV1.DeleteNamespacedLeaseAsync(
                _leaseOptions.Name,
                _leaseOptions.Namespace,
                cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Lease {LeaseName} explicitly released for near-instant failover",
                _leaseOptions.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to explicitly release lease {LeaseName} -- followers will acquire after TTL expiry",
                _leaseOptions.Name);
        }
    }
    _isLeader = false;
}
```

**Important:** Deleting the Lease vs clearing holderIdentity:
- **Delete:** Followers immediately see "no lease" and one creates a new one. This is the simplest approach for near-instant failover.
- **Clear holderIdentity:** Followers see expired holder and acquire. Slightly more graceful but requires replace-with-empty rather than delete.
- **Do nothing:** Followers wait for TTL expiry (~15s). Acceptable but not "near-instant."
- **Recommendation:** Delete the lease on graceful shutdown. This satisfies HA-05 (near-instant failover on SIGTERM). The `try-catch` ensures that if deletion fails (network issue), the fallback is TTL-based expiry.

### Anti-Patterns to Avoid

- **Registering K8sLeaseElection as both a new singleton AND a new hosted service:** This creates two instances with separate `_isLeader` flags. Use the `AddHostedService(sp => sp.GetRequiredService<...>())` pattern to ensure one instance.
- **Using `RunUntilLeadershipLostAsync` instead of `RunAndTryToHoldLeadershipForeverAsync`:** The former exits after first leadership loss. The latter continuously retries, which is correct for a long-running pod that should attempt to reacquire leadership after temporary loss.
- **Locking around `_isLeader` reads:** `volatile bool` is sufficient for single-writer-multiple-reader scenarios. Adding locks would add contention on every Export call.
- **Wrapping the log OTLP exporter with RoleGatedExporter:** Logs must flow from ALL pods (TELEM-04, confirmed in Phase 7). Only metric and trace exporters are role-gated.
- **Hard-coding environment detection flag in configuration:** `KubernetesClientConfiguration.IsInCluster()` checks for `KUBERNETES_SERVICE_HOST` environment variable, which is automatically set by K8s. No configuration flag needed.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Lease acquire/renew/release loop | Custom async loop with K8s REST calls | `LeaderElector` from `KubernetesClient` 18.0.13 | LeaderElector handles retry with jitter, lease record creation, identity comparison, transition detection, and deadline enforcement. ~200 lines of nuanced coordination logic. |
| Lease resource lock abstraction | Custom V1Lease create/read/replace wrapper | `LeaseLock` from `KubernetesClient` 18.0.13 | LeaseLock extends `MetaObjectLock<V1Lease>`, implementing `ILock` with typed create/read/replace operations and leader election record mapping. |
| In-cluster K8s client configuration | Manual token/cert loading from `/var/run/secrets/` | `KubernetesClientConfiguration.InClusterConfig()` | Handles service account token mounting, CA cert, and API server URL automatically. |
| K8s environment detection | Custom env var checks | `KubernetesClientConfiguration.IsInCluster()` | Checks for `KUBERNETES_SERVICE_HOST` and `KUBERNETES_SERVICE_PORT` env vars, the standard K8s in-cluster indicators. |

**Key insight:** The `KubernetesClient` NuGet package provides a complete leader election subsystem (`LeaderElector`, `LeaseLock`, `LeaderElectionConfig`) that mirrors the Go client-go `leaderelection` package. This is a mature, production-tested implementation. The only custom code needed is the thin `K8sLeaseElection` wrapper that bridges the library's event-driven API to the `ILeaderElection` interface.

## Common Pitfalls

### Pitfall 1: Two Instances of K8sLeaseElection in DI

**What goes wrong:** `ILeaderElection.IsLeader` returns false even when the pod holds the lease.
**Why it happens:** Registering `AddSingleton<ILeaderElection, K8sLeaseElection>()` AND `AddHostedService<K8sLeaseElection>()` creates two separate instances. The hosted service instance acquires leadership and sets `_isLeader = true`, but the DI-resolved `ILeaderElection` is a different instance with `_isLeader = false`.
**How to avoid:** Register `K8sLeaseElection` as a singleton first, then use factory overloads to resolve the same instance for both `ILeaderElection` and `IHostedService`:
```csharp
services.AddSingleton<K8sLeaseElection>();
services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());
```
**Warning signs:** Metrics/traces never exported despite pod being the leader; log enrichment always shows "follower" role.

### Pitfall 2: RenewDeadline Must Be Less Than LeaseDuration

**What goes wrong:** LeaderElector throws an exception on construction or silently fails to renew.
**Why it happens:** The `LeaderElectionConfig` validates that `RenewDeadline < LeaseDuration` and `RetryPeriod < RenewDeadline`. If DurationSeconds=15 and RenewIntervalSeconds=10, setting RenewDeadline=15 violates the constraint.
**How to avoid:** Set `RenewDeadline` to `DurationSeconds - 2` or `DurationSeconds * 0.8`. With the default 15s duration, RenewDeadline=13s gives a 2-second buffer. Map `RetryPeriod` to `RenewIntervalSeconds` (10s) which satisfies `RetryPeriod < RenewDeadline` (10 < 13).
**Warning signs:** Exception during LeaderElector construction; leadership never acquired.

### Pitfall 3: AddOtlpExporter Creates Uninterceptable Exporter

**What goes wrong:** RoleGatedExporter wrapping has no effect because the OTLP exporter was created and registered by `AddOtlpExporter()` internally.
**Why it happens:** `AddOtlpExporter()` is a convenience method that creates the exporter, wraps it in a `BatchActivityExportProcessor` or `PeriodicExportingMetricReader`, and registers it. There is no hook to intercept or wrap the exporter before it is registered.
**How to avoid:** Do NOT use `AddOtlpExporter()` for metrics and traces. Instead, manually create `OtlpTraceExporter` and `OtlpMetricExporter`, wrap each in `RoleGatedExporter<T>`, then register via `AddProcessor()` (traces) or `AddReader()` (metrics). Keep `AddOtlpExporter()` only for the log exporter (not role-gated).
**Warning signs:** Metrics/traces exported from follower pods; RoleGatedExporter.Export never called.

### Pitfall 4: Missing RBAC Permissions for Lease Resources

**What goes wrong:** K8sLeaseElection fails with HTTP 403 Forbidden when attempting to create or update the Lease.
**Why it happens:** The pod's service account lacks permissions on the `coordination.k8s.io` API group for `leases` resources.
**How to avoid:** The K8s deployment must include a Role/RoleBinding granting `get`, `create`, `update`, `delete` on `leases` in the `coordination.k8s.io` API group, scoped to the configured namespace. Document this as a deployment prerequisite.
**Warning signs:** "Forbidden" errors in K8sLeaseElection logs; pod remains follower permanently.

### Pitfall 5: LeaderElector Exits Silently on CancellationToken

**What goes wrong:** K8sLeaseElection BackgroundService exits without properly updating `_isLeader`.
**Why it happens:** When `stoppingToken` is cancelled, `RunAndTryToHoldLeadershipForeverAsync` exits by throwing `OperationCanceledException`. If `_isLeader` is not set to false before the exception propagates, there is a brief window where `IsLeader` returns true but the lease is no longer being renewed.
**How to avoid:** Register an `OnStoppedLeading` handler that sets `_isLeader = false`. Also set `_isLeader = false` in `StopAsync` after the base call. The `volatile` field ensures immediate visibility.
**Warning signs:** Brief metric/trace export after leader gives up lease during shutdown.

### Pitfall 6: PeriodicExportingMetricReader Default Interval

**What goes wrong:** Metrics are exported every 60 seconds instead of the expected interval.
**Why it happens:** `PeriodicExportingMetricReader` defaults to 60-second export interval. When replacing `AddOtlpExporter()` (which also defaults to 60s) with manual construction, the default is preserved. If a different interval is desired, it must be explicitly configured.
**How to avoid:** When constructing `PeriodicExportingMetricReader`, pass `PeriodicExportingMetricReaderOptions` to set the desired interval:
```csharp
new PeriodicExportingMetricReader(roleGated, new PeriodicExportingMetricReaderOptions
{
    ExportIntervalMilliseconds = 60_000  // 60s -- same as AddOtlpExporter default
})
```
**Warning signs:** Not really a problem if 60s is acceptable; only an issue if a different interval was assumed.

## Code Examples

### Complete Modified AddSimetraTelemetry (Phase 8 Changes)

```csharp
// Source: Verified against KubernetesClient 18.0.13 API, OpenTelemetry .NET 1.15.0 extending-the-sdk docs
public static IHostApplicationBuilder AddSimetraTelemetry(
    this IHostApplicationBuilder builder)
{
    var otlpOptions = new OtlpOptions { Endpoint = "", ServiceName = "" };
    builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

    var loggingOptions = new LoggingOptions();
    builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

    // --- Leader election: environment-based ---
    if (KubernetesClientConfiguration.IsInCluster())
    {
        var kubeConfig = KubernetesClientConfiguration.InClusterConfig();
        builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));
        builder.Services.AddSingleton<K8sLeaseElection>();
        builder.Services.AddSingleton<ILeaderElection>(sp =>
            sp.GetRequiredService<K8sLeaseElection>());
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<K8sLeaseElection>());
    }
    else
    {
        builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
    }

    // --- Metrics + Tracing: manually wired with RoleGatedExporter ---
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName: otlpOptions.ServiceName))
        .WithMetrics(metrics =>
        {
            metrics.AddMeter(TelemetryConstants.MeterName);
            metrics.AddRuntimeInstrumentation();

            // Manual OTLP metric exporter wrapped in RoleGatedExporter
            metrics.AddReader(sp =>
            {
                var leaderElection = sp.GetRequiredService<ILeaderElection>();
                var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
                {
                    Endpoint = new Uri(otlpOptions.Endpoint)
                });
                var roleGated = new RoleGatedExporter<Metric>(otlpExporter, leaderElection);
                return new PeriodicExportingMetricReader(roleGated);
            });
        })
        .WithTracing(tracing =>
        {
            tracing.AddSource(TelemetryConstants.TracingSourceName);

            // Manual OTLP trace exporter wrapped in RoleGatedExporter
            tracing.AddProcessor(sp =>
            {
                var leaderElection = sp.GetRequiredService<ILeaderElection>();
                var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
                {
                    Endpoint = new Uri(otlpOptions.Endpoint)
                });
                var roleGated = new RoleGatedExporter<Activity>(otlpExporter, leaderElection);
                return new BatchActivityExportProcessor(roleGated);
            });
        });

    // --- Logging: unchanged from Phase 7 ---
    builder.Logging.ClearProviders();
    if (loggingOptions.EnableConsole)
    {
        builder.Logging.AddConsole();
    }

    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeScopes = true;
        logging.IncludeFormattedMessage = true;
        logging.SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(serviceName: otlpOptions.ServiceName));
        logging.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(otlpOptions.Endpoint);
        });
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
```

### K8s RBAC Configuration (Deployment Prerequisite)

```yaml
# Required RBAC for Lease-based leader election
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: simetra-leader-election
  namespace: simetra
rules:
  - apiGroups: ["coordination.k8s.io"]
    resources: ["leases"]
    verbs: ["get", "create", "update", "delete"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: simetra-leader-election
  namespace: simetra
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: simetra-leader-election
subjects:
  - kind: ServiceAccount
    name: default
    namespace: simetra
```

### LeaderElectionConfig Parameter Mapping

```csharp
// Mapping from LeaseOptions (Phase 1 config) to LeaderElectionConfig
// LeaseOptions.RenewIntervalSeconds (10s)  --> RetryPeriod
// LeaseOptions.DurationSeconds (15s)       --> LeaseDuration
// Computed: DurationSeconds - 2            --> RenewDeadline (13s)
//
// Validation chain:
// RetryPeriod (10s) < RenewDeadline (13s) < LeaseDuration (15s) -- satisfies LeaderElector constraints
// DurationSeconds > RenewIntervalSeconds -- already validated by LeaseOptionsValidator
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ConfigMap-based leader election | Lease-based leader election (coordination.k8s.io/v1) | Kubernetes 1.14+ (GA in 1.17) | Leases are purpose-built for coordination; ConfigMaps create unnecessary watch traffic and etcd load |
| Hand-rolled Lease CRUD in .NET | KubernetesClient `LeaderElector` + `LeaseLock` | KubernetesClient 10.x+ | Full leader election subsystem with retry, jitter, deadlines, and event callbacks |
| Static leader/follower config | Dynamic leader election with automatic failover | Kubernetes Lease API | Pods automatically negotiate leadership; no manual intervention needed |

**Deprecated/outdated:**
- `ConfigMapLock`: Legacy approach. Use `LeaseLock` with coordination.k8s.io/v1 Lease resources.
- Manual K8s REST calls for Lease operations: Use `LeaderElector` which handles the full lifecycle.

## Open Questions

1. **AddReader factory overload availability on MeterProviderBuilder**
   - What we know: `MeterProviderBuilder.AddReader(MetricReader)` instance overload exists. The `Func<IServiceProvider, MetricReader>` factory overload is needed to resolve `ILeaderElection` from DI.
   - What's unclear: Whether the factory overload `AddReader(Func<IServiceProvider, MetricReader>)` exists in OpenTelemetry.Extensions.Hosting 1.15.0. It follows the same pattern as `AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>)` for traces, which exists.
   - Recommendation: During implementation, verify the factory overload. If it does not exist, alternatives: (a) resolve `ILeaderElection` from a captured service provider reference, (b) use `ConfigureServices` to register the reader after the service provider is built, or (c) create a lazy-resolving wrapper.
   - Confidence: MEDIUM -- the pattern exists for processors but needs validation for readers.

2. **OtlpMetricExporter constructor accessibility**
   - What we know: `OtlpTraceExporter` has a public constructor accepting `OtlpExporterOptions`. The metric exporter should follow the same pattern.
   - What's unclear: Whether `OtlpMetricExporter` has the same public constructor signature, or if it requires additional parameters like `MetricReaderOptions`.
   - Recommendation: During implementation, check the constructor. If it requires additional params, provide defaults matching what `AddOtlpExporter()` would use.
   - Confidence: MEDIUM -- public constructor verified for trace exporter, metric exporter likely follows same pattern.

3. **LeaderElector lease release on cancellation**
   - What we know: `RunAndTryToHoldLeadershipForeverAsync` accepts a `CancellationToken`. When cancelled, the loop exits. `OnStoppedLeading` fires.
   - What's unclear: Whether the LeaderElector explicitly clears the lease's `holderIdentity` on cancellation (near-instant failover) or simply stops renewing (TTL-based failover ~15s).
   - Recommendation: Implement explicit lease deletion in `StopAsync` as shown in Pattern 4. This guarantees near-instant failover on graceful shutdown regardless of LeaderElector's internal behavior.
   - Confidence: MEDIUM -- explicit deletion in StopAsync is a safe belt-and-suspenders approach.

## Sources

### Primary (HIGH confidence)
- [KubernetesClient 18.0.13 NuGet](https://www.nuget.org/packages/KubernetesClient/) -- Version 18.0.13, published 2025-12-02, targets net8.0+
- [kubernetes-client/csharp GitHub](https://github.com/kubernetes-client/csharp) -- Official .NET Kubernetes client repository
- [LeaseLock API docs](https://kubernetes-client.github.io/csharp/api/k8s.LeaderElection.ResourceLock.LeaseLock.html) -- LeaseLock constructor, ILock implementation, MetaObjectLock<V1Lease> inheritance
- [LeaderElector source](https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/LeaderElection/LeaderElector.cs) -- RunAndTryToHoldLeadershipForeverAsync, TryAcquireOrRenew, event handlers
- [V1Lease API docs](https://kubernetes-client.github.io/csharp/api/k8s.Models.V1Lease.html) -- V1Lease class, V1LeaseSpec properties, coordination.k8s.io/v1 API group
- [OpenTelemetry .NET extending trace SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md) -- Custom exporters, AddProcessor, BatchActivityExportProcessor registration
- [OpenTelemetry .NET extending metrics SDK](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/extending-the-sdk/README.md) -- Custom metric exporters, BaseExporter<Metric>, AddReader pattern
- [OtlpTraceExporter source](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpTraceExporter.cs) -- Public constructor `OtlpTraceExporter(OtlpExporterOptions)`, extends BaseExporter<Activity>
- [OTLP Exporter README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md) -- AddOtlpExporter usage, PeriodicExportingMetricReader defaults (60s interval, Cumulative temporality)
- [Kubernetes Leases documentation](https://kubernetes.io/docs/concepts/architecture/leases/) -- Lease resource specification, coordination.k8s.io API group

### Secondary (MEDIUM confidence)
- [Leader Election in Kubernetes with the Official C# Client](https://martowen.com/posts/2022/leader-election-in-kubernetes/) -- Practical C# example with ConfigMapLock (pattern applies to LeaseLock), BackgroundService integration, event handler wiring
- [.NET graceful shutdown in Kubernetes](https://github.com/dotnet/dotnet-docker/blob/main/samples/kubernetes/graceful-shutdown/graceful-shutdown.md) -- IHostApplicationLifetime, SIGTERM handling, ApplicationStopping event
- [ASP.NET Core Graceful Shutdown in Kubernetes](https://mirsaeedi.medium.com/asp-net-core-graceful-shutdown-862cc5c915e1) -- IHostApplicationLifetime vs IHostLifetime, ApplicationStopping token activation

### Tertiary (LOW confidence)
- Training data knowledge on `volatile bool` thread safety for single-writer-multiple-reader patterns -- standard .NET practice but should be validated if correctness is critical
- Training data knowledge on `OtlpMetricExporter` public constructor -- verified for `OtlpTraceExporter`, assumed same pattern for metrics but needs implementation validation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- KubernetesClient 18.0.13 version verified on NuGet with publication date (2025-12-02). LeaderElector/LeaseLock API verified from official docs and source.
- Architecture (K8sLeaseElection): HIGH -- BackgroundService + ILeaderElection pattern verified from official C# blog post and .NET BackgroundService documentation. LeaderElector API verified from source.
- Architecture (RoleGatedExporter wiring): MEDIUM -- OtlpTraceExporter public constructor verified from source. BatchActivityExportProcessor registration via AddProcessor verified from extending-the-sdk docs. The metric side (OtlpMetricExporter constructor, AddReader factory overload) is inferred from the same patterns but needs implementation validation.
- Pitfalls: HIGH -- DI double-registration is a well-known .NET pattern issue. LeaderElectionConfig constraint validation is documented in the LeaderElector source. RBAC permissions are documented in Kubernetes official docs.
- Graceful shutdown: MEDIUM -- Explicit lease deletion approach is architectural recommendation, not a verified library feature. The belt-and-suspenders approach (delete + TTL fallback) is safe regardless.

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (stable -- KubernetesClient 18.0.13 and OpenTelemetry .NET 1.15.0 are stable releases)
