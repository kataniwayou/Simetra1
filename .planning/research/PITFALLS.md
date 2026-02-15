# Pitfalls Research

**Domain:** .NET SNMP Supervisor Service (Headless Background Service with Pipeline Architecture)
**Researched:** 2026-02-15
**Confidence:** HIGH (majority verified via official docs and multiple sources)

---

## Critical Pitfalls

Mistakes that cause rewrites, data loss, or production outages.

### Pitfall 1: SharpSnmpLib Trap Listener Drops Packets Under Load Due to Default UDP Buffer Sizes

**What goes wrong:**
The SNMP trap listener uses default OS-level UDP receive buffer sizes (typically 8KB-64KB on Linux). During trap storms (device flapping, switching loops, broadcast storms), the kernel drops UDP packets before the application ever sees them. Because UDP is fire-and-forget, there is zero notification of loss. The listener appears healthy but silently misses traps. SharpSnmpLib's `TrapV2MessageReceivedEventArgs` fires only for packets the kernel successfully buffers -- dropped packets vanish without a trace.

**Why it happens:**
SharpSnmpLib does not set `SO_RCVBUF` on its UDP socket by default. The OS default `rmem_default` (often 212992 bytes on Linux, much smaller in containers) is insufficient when multiple devices send simultaneous trap bursts. In containerized environments, the effective buffer may be even smaller due to cgroup memory constraints. The trap listener is single-consumer (processes one trap at a time on a thread pool thread), creating a backlog during bursts.

**How to avoid:**
1. Explicitly set `SO_RCVBUF` on the UDP socket to at least 4MB: access the underlying `Socket` from the listener binding and call `SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 4194304)`.
2. Set the Linux kernel max: `sysctl -w net.core.rmem_max=8388608` in the container init or K8s securityContext. Without this, `setsockopt` silently caps at `rmem_max`.
3. Set `ThreadPool.SetMinThreads()` before starting `SnmpEngine.Start()` -- SharpSnmpLib documentation explicitly recommends this to avoid thread pool starvation during trap bursts.
4. Monitor kernel UDP drop counters: export `cat /proc/net/snmp | grep Udp` as a metric (`simetra_udp_rcvbuf_errors_total`).

**Warning signs:**
- `netstat -su` or `/proc/net/snmp` shows `RcvbufErrors` incrementing.
- State Vector shows stale data for devices that should be sending traps.
- Heartbeat trap arrives (proves listener alive) but device traps are missing.
- Metrics show gaps during known high-traffic windows.

**Phase to address:**
Phase 1 (Framework + Listener). Must be configured when the SNMP listener is first created. Retrofitting requires changing socket initialization code.

**Confidence:** HIGH -- verified via Linux kernel documentation, SharpSnmpLib source (GitHub), and NetCraftsmen SNMP trap analysis.

---

### Pitfall 2: Quartz.NET Misfire Handling Silently Skips or Doubles Job Executions

**What goes wrong:**
When a Quartz job takes longer than its scheduled interval (e.g., a 30s poll job that takes 35s due to slow SNMP response), the next trigger is "misfired." The default `SmartPolicy` misfire instruction behaves differently depending on trigger type, elapsed time relative to the misfire threshold (default 60s), and trigger state. This creates unpredictable behavior: some missed polls are silently skipped, others fire immediately when the previous job completes, and some fire twice in rapid succession. With `DisallowConcurrentExecution`, misfires queue up and then execute back-to-back, creating burst polling that overwhelms devices.

**Why it happens:**
Developers set `DisallowConcurrentExecution` and assume it handles everything. But it only prevents overlapping execution of the same JobKey -- it does NOT prevent misfire queuing. The default misfire threshold of 60 seconds means any job delayed more than 60s past its trigger time is considered "misfired" and handled by the misfire instruction, not by concurrency control. The `SmartPolicy` dynamically chooses behavior based on trigger type and remaining repeat count, making it nearly impossible to predict.

**How to avoid:**
1. Explicitly set `WithMisfireHandlingInstructionDoNothing()` on all poll triggers. This discards missed executions entirely and waits for the next scheduled fire time. For a real-time monitoring system, skipping a stale poll is always preferable to executing it late.
2. Set the misfire threshold to match your polling interval: `quartz.jobStore.misfireThreshold = 30000` (30s). This prevents polls delayed by less than 30s from being treated as misfires at all.
3. Add SNMP client timeouts shorter than the poll interval: if poll interval is 30s, set SNMP timeout to 10s with 2 retries max (total 20s worst case).
4. Log misfire events by implementing `ITriggerListener.TriggerMisfired` and emitting a warning-level log with the job key and misfire instruction applied.

**Warning signs:**
- Quartz logs show "Handling n triggers that missed their scheduled fire-time."
- Metrics show uneven poll intervals (some 30s, some 60s, some 90s).
- Multiple poll responses for the same device arrive within seconds of each other.
- Device responds with SNMP `tooBig` or rate-limiting errors due to burst polling.

**Phase to address:**
Phase 1 (Framework + Scheduler Setup). Misfire policy must be configured when triggers are first created. Changing misfire policy on existing triggers in production requires careful migration.

**Confidence:** HIGH -- verified via Quartz.NET official documentation, GitHub issue #1109 (DoNothing behavior with cron triggers), and quartznet/quartznet Discussion #1377 (cluster misfires).

---

### Pitfall 3: .NET Graceful Shutdown Timeout Starves Late-Registered Services

**What goes wrong:**
The .NET host's `HostOptions.ShutdownTimeout` defaults to 5 seconds for ALL `IHostedService.StopAsync()` calls combined. Services stop in reverse registration order. If the Quartz scheduler (registered early, stops late) takes 4 seconds to complete in-flight jobs, the SNMP listener (registered later, stops earlier) gets only 1 second, and the OpenTelemetry provider flush gets zero seconds. Result: telemetry data for the final shutdown window is lost, and channels may not drain.

Worse: when the shared `CancellationToken` fires after the timeout, `token.ThrowIfCancellationRequested()` throws `OperationCanceledException`, which exits the StopAsync loop entirely. Services registered before the slow service never get their `StopAsync()` called at all.

**Why it happens:**
The 5-second default is aggressively short for a multi-component system. Developers typically discover this only in production when shutdown logs show incomplete sequences. The K8s `terminationGracePeriodSeconds` (default 30s) gives the pod 30s, but the .NET host internally enforces only 5s of that for service shutdown unless explicitly configured.

**How to avoid:**
1. Set `HostOptions.ShutdownTimeout` to at least 25 seconds (leaving 5s buffer before K8s SIGKILL at 30s): `services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));`
2. Control registration order deliberately: Register OTel providers early (so they stop last in reverse order), register the SNMP listener late (so it stops first).
3. Implement per-step timeout budgets within each service's `StopAsync`: use `CancellationTokenSource.CreateLinkedTokenSource` with individual timeouts rather than relying on the shared token.
4. Match `terminationGracePeriodSeconds` in K8s spec to be >= `ShutdownTimeout` + 5s buffer.

**Warning signs:**
- Shutdown logs show "Application is shutting down..." but not all "stopped successfully" messages.
- Last few seconds of metrics/traces missing from OTLP collector after pod restart.
- `OperationCanceledException` in shutdown logs.
- K8s shows pod terminated with exit code 137 (SIGKILL) instead of 0.

**Phase to address:**
Phase 1 (Framework + Host Setup). This must be configured in `Program.cs` during initial host builder setup. Adding it later requires verifying all registration order assumptions still hold.

**Confidence:** HIGH -- verified via Andrew Lock's official blog post, Microsoft IHostedService docs, and dotnet/dotnet-docker Kubernetes graceful shutdown sample.

---

### Pitfall 4: Kubernetes Lease Leader Election Allows Brief Split-Brain

**What goes wrong:**
The K8s Lease API uses optimistic concurrency (resource versions) for leader election, but it does NOT guarantee mutual exclusion under all conditions. During network partitions, clock skew, or API server slowness, the outgoing leader may still believe it holds the lease while a new leader has already acquired it. Both pods emit metrics to OTLP simultaneously, causing duplicate metrics. Worse, both pods may attempt SNMP polls to the same device, doubling network load and potentially confusing devices that track request sources.

Kubernetes explicitly does not employ countermeasures against split-brain -- it relies on eventual convergence. GitHub issue #23731 documents this as a known property of the leader election design.

**Why it happens:**
The lease holder determines leadership by comparing timestamps, but clocks across nodes may drift. The lease renewal interval (10s) vs. TTL (15s) leaves only a 5-second margin. If a renewal RPC takes >5s due to API server load, the lease expires while the leader still thinks it's valid. The `.NET Kubernetes.Client` library does not provide a built-in leader election abstraction -- you must implement the acquire/renew/release loop yourself, and getting the timing right is subtle.

**How to avoid:**
1. Implement a "leader fence" pattern: after acquiring the lease, wait one full TTL duration before beginning leader-only work (metric/trace export). This ensures any previous leader's operations have timed out.
2. Use a "leader epoch" counter in the lease annotations. Increment on each acquisition. Include the epoch in all emitted metrics as a label. Downstream deduplication can filter by highest epoch.
3. Set SNMP poll intervals longer than the lease TTL (>15s). This means even in a split-brain window, at most one overlapping poll occurs before the old leader detects loss.
4. Implement explicit lease watch: use the K8s watch API on the Lease resource so followers detect leader changes immediately rather than polling.
5. Make OTLP export idempotent where possible: counters are additive (tolerate duplicates), gauges are last-writer-wins (natural dedup).

**Warning signs:**
- OTLP collector receives duplicate metric series with identical timestamps but different pod identity labels.
- Lease renewal logs show intermittent "conflict" or "resource version mismatch" errors.
- Two pods simultaneously log "Acquired leadership" within the same TTL window.
- SNMP devices log duplicate GET requests from different source IPs.

**Phase to address:**
Phase 1 (Framework + HA Setup). The lease loop implementation and fencing logic must be part of the initial HA design. Retrofitting fencing onto an existing lease loop is error-prone.

**Confidence:** MEDIUM -- Kubernetes issue #23731 and #67651 confirm split-brain is possible. The .NET Kubernetes.Client behavior is less documented; specific fencing recommendations are based on distributed systems best practices, not .NET-specific verified sources.

---

### Pitfall 5: OpenTelemetry MeterProvider Disposed Too Early Drops Final Metrics

**What goes wrong:**
If `MeterProvider` is disposed before all metric-producing components have finished their final work (e.g., channel drain emitting last metrics, final poll result processing), those measurements are silently lost. Conversely, if `MeterProvider` is not disposed at all, the `PeriodicExportingMetricReader` never flushes its final batch, and the last export interval's worth of metrics (default 60s) is lost. Both failure modes are silent -- no exceptions, no logs, just missing data.

**Why it happens:**
In DI-managed lifecycle, `MeterProvider` disposal order depends on registration order (reverse). If OTel is registered after application services, it disposes before them. The developer assumes DI handles it correctly, but the shutdown sequence has a specific ordering requirement that DI doesn't enforce. Additionally, the `PeriodicExportingMetricReader` has a configurable export interval (default 60s) -- if shutdown happens 55 seconds after the last export, 55 seconds of metrics are buffered and only flushed on proper disposal.

**How to avoid:**
1. Register OpenTelemetry providers FIRST in the DI container, so they are disposed LAST (reverse order).
2. In the shutdown sequence, explicitly call `meterProvider.ForceFlush(timeout)` before disposing, with a dedicated timeout budget (5s minimum).
3. Reduce the `PeriodicExportingMetricReader` export interval to 15-30s to minimize data loss on ungraceful shutdown.
4. Use the `MeterProvider.Dispose()` call -- disposing automatically calls `Shutdown()` on all registered `MetricReader` and `MetricExporter` instances, which triggers a final flush.
5. Never create `Meter` instances after `MeterProvider` disposal -- subsequent measurements are silently dropped with no error.

**Warning signs:**
- Dashboard shows metric gaps at exactly the times pods were restarted.
- Last N seconds of metrics before pod shutdown are consistently missing.
- Logs show "Application stopped" but OTLP collector logs do not show a final batch for that service.
- `ObjectDisposedException` when late-running components try to record measurements.

**Phase to address:**
Phase 1 (Framework + Telemetry Setup). DI registration order and shutdown sequence must be designed together from the start.

**Confidence:** HIGH -- verified via OpenTelemetry .NET official best practices page, opentelemetry-dotnet Discussion #3614, and opentelemetry-dotnet docs/metrics/README.md.

---

### Pitfall 6: System.Threading.Channels DropOldest Mode Loses Data Silently Without Metrics

**What goes wrong:**
When a bounded channel with `BoundedChannelFullMode.DropOldest` reaches capacity, the oldest item is removed to make room for the new one. The dropped item is gone -- no exception, no return value indicating loss (the write always succeeds). If the `itemDropped` callback delegate is not provided when creating the channel, there is absolutely no notification that data was lost. In a monitoring system, this means trap data vanishes without any indication in logs, metrics, or health checks.

**Why it happens:**
The `Channel.CreateBounded<T>(BoundedChannelOptions, Action<T>)` overload with the drop callback was added in .NET 6 but is not the default constructor developers reach for. Most examples and tutorials use the simpler `Channel.CreateBounded<T>(int capacity)` or `Channel.CreateBounded<T>(BoundedChannelOptions)` overloads, which provide no drop notification. The design intent is "fire-and-forget writes" -- the writer never blocks -- but this means the writer has no idea items were lost.

**How to avoid:**
1. Always use the overload that accepts an `Action<T> itemDropped` callback: `Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest }, dropped => { /* handle */ })`.
2. In the callback: increment a per-device counter metric (`simetra_channel_drops_total`), log at Warning level (not Debug), and include the device name and dropped item summary.
3. Set `SingleWriter = false` (trap listener is multi-threaded) and `SingleReader = true` (device channel has one consumer). This enables optimized code paths in the channel implementation.
4. Tune capacity per device based on expected trap rates: default 100 may be too low for chatty devices and wastefully high for quiet ones. Make configurable per device in appsettings.json.

**Warning signs:**
- State Vector shows stale timestamps for a device that should be sending frequent traps.
- Heartbeat metrics arrive on time (Simetra channel rarely fills) but device metrics show gaps.
- No `simetra_channel_drops_total` metric exists yet (meaning drops are completely invisible).
- Memory usage is stable during trap storms (should spike if items were actually queued).

**Phase to address:**
Phase 1 (Framework + Channel Setup). The drop callback must be provided at channel creation time. Adding it later requires changing channel construction code in the device module system.

**Confidence:** HIGH -- verified via Microsoft Learn Channels documentation, dotnet/runtime Issue #36522 (API proposal for drop notification), and .NET Blog introduction to Channels.

---

## Technical Debt Patterns

Shortcuts that seem reasonable but create long-term problems.

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Sharing one `Meter` for all device metrics | Simpler initialization, fewer objects | Cannot independently enable/disable metrics per device type; cardinality explosion from mixing device types in one meter's instruments | Never -- create one `Meter` per device module from the start |
| Using string-based OID comparison instead of `ObjectIdentifier` | Avoids SharpSnmpLib dependency in domain layer | String comparison is lexicographic ("1.3.6.1.10" < "1.3.6.1.9"), not numerical; breaks OID tree operations | Never -- always use `ObjectIdentifier` or uint[] for OID comparison |
| Hardcoding SNMP timeouts as constants | Fast to implement | Different devices have different response latencies; one timeout does not fit all; slow devices cause cascading poll delays | MVP only -- parameterize per-device before adding real devices |
| Skipping Quartz job listeners for monitoring | Less code to maintain | No visibility into misfire frequency, job execution duration, or trigger state transitions; problems only discovered via missing metrics | MVP only -- add `IJobListener` and `ITriggerListener` before production |
| Using `Task.Run` instead of proper async in trap processing | Avoids async refactoring | Thread pool exhaustion under load; prevents proper cancellation token propagation; hides exceptions in unobserved tasks | Never -- use async/await throughout the pipeline |
| Static `ConcurrentDictionary` for State Vector | No DI wiring needed | Untestable; hidden dependency; prevents per-test isolation; lifetime not controlled by DI | MVP only -- inject `IStateVectorService` from Phase 1 |

## Integration Gotchas

Common mistakes when connecting to external services.

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SharpSnmpLib Trap Listener | Starting listener on port 162 without checking if port is available; fails silently or throws on `Start()` depending on OS | Check port availability before binding; handle `SocketException` with clear error message; in K8s, ensure hostPort or container port 162 is correctly mapped |
| K8s Lease API | Using default `KubernetesClientConfiguration.InClusterConfig()` without setting explicit HTTP timeout; default is 100s, causing lease renewal to hang during API server restarts | Set `HttpClientTimeout` to less than lease TTL (e.g., 10s); implement per-operation timeout via `CancellationTokenSource` with 5s timeout on each renewal call |
| OTLP gRPC Exporter | Assuming gRPC exporter fails gracefully when collector is down; it retries indefinitely with default retry policy, consuming memory for buffered telemetry | Set `ExporterTimeoutMilliseconds` and configure bounded retry with max attempts; monitor `otel_exporter_otlp_failures_total` if available |
| Quartz Scheduler + DI | Registering jobs as transient in DI but expecting them to maintain state; Quartz creates new job instances per execution | Use `PersistJobDataAfterExecution` attribute if state is needed, or inject scoped services that manage state outside the job instance |
| SharpSnmpLib SNMP GET | Not handling `Lextm.SharpSnmpLib.Messaging.TimeoutException` vs `System.TimeoutException`; catching the wrong type lets SNMP timeouts propagate as unhandled | Catch `Lextm.SharpSnmpLib.Messaging.TimeoutException` specifically in poll jobs; wrap in domain-specific exception for pipeline error handling |
| .NET Kubernetes.Client Watch API | Using Watch without handling `WatchEventType.Error` and reconnection; watch connections drop after API server restart or network timeout | Implement reconnection loop with exponential backoff; handle `KubernetesException` with `Status.Code == 410 (Gone)` by restarting watch from latest resourceVersion |

## Performance Traps

Patterns that work at small scale but fail as usage grows.

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| All poll jobs fire at the same second | CPU spike every 30s, SNMP response timeouts, kernel socket buffer overflow | Stagger poll start times using Quartz `StartAt` offset: `device_index * (interval / device_count)` | >10 devices with same interval |
| One `Meter` instrument per unique OID across all devices | Cardinality explosion in OTLP backend; scrape duration exceeds Prometheus timeout | Create instruments by metric name (from PollDefinitionDto), not by OID; limit unique tag combinations to <2000 per instrument (OTel default cap) | >50 unique OID/device combinations |
| Synchronous SNMP poll blocking Quartz worker threads | Thread pool starvation; Quartz cannot fire other triggers; misfires cascade | Use async SNMP operations (`GetRequestMessage.GetResponseAsync()`); set Quartz thread pool size >= 2x concurrent poll count | >20 concurrent polls |
| Allocating new `ObjectIdentifier` per trap varbind comparison | GC pressure from short-lived OID objects in hot path; Gen0 collections spike during trap storms | Pre-parse all known OIDs at startup into a `HashSet<ObjectIdentifier>` or `FrozenSet<ObjectIdentifier>` (.NET 8+); reuse parsed instances | >100 traps/second |
| Channel consumer doing synchronous I/O (OTLP export) in pipeline | Channel backs up; DropOldest activates not because of trap volume but because consumer is slow | Export metrics asynchronously; batch via `PeriodicExportingMetricReader`; never call `exporter.Export()` in the pipeline hot path | >50 traps/second with >100ms export latency |

## Security Mistakes

Domain-specific security issues beyond general web security.

| Mistake | Risk | Prevention |
|---------|------|------------|
| SNMP community string in `appsettings.json` committed to Git or baked into container image | Credential exposure; any reader of the image/repo can poll or send fake traps to all monitored devices | Load from K8s Secret via environment variable: `SnmpListener__CommunityString` (double underscore for nested config); never commit to source control |
| Health probe endpoints exposed without network policy | Any pod in the cluster can probe Simetra's liveness/readiness endpoints and enumerate its health state | Apply K8s NetworkPolicy allowing probe traffic only from kubelet (source: node CIDR); restrict to health check paths only |
| Device IPs exposed as metric labels in OTLP | Network topology leakage through observability stack; OTLP collector compromise reveals all monitored device addresses | Make `device_ip` label optional/redactable in config; use device name as primary identifier; hash IP if regulatory compliance requires it |
| No SNMP community string validation on received traps | Rogue device or attacker can send traps with any community string; if listener does not validate, forged traps enter pipeline and produce fake metrics | SharpSnmpLib's listener validates community string by default, but verify this is configured correctly; log rejected traps at Warning level with source IP |

## "Looks Done But Isn't" Checklist

Things that appear complete but are missing critical pieces.

- [ ] **SNMP Listener:** Works in dev -- but verify UDP socket buffer size is tuned, `ThreadPool.SetMinThreads()` is called, and `/proc/net/snmp` RcvbufErrors is monitored
- [ ] **Quartz Scheduler:** Jobs fire on schedule -- but verify misfire instruction is explicitly set (not SmartPolicy default), `ITriggerListener` is registered for misfire monitoring, and `WaitForJobsToComplete = true` is set for shutdown
- [ ] **Channel-per-Device:** Items flow through -- but verify `itemDropped` callback is registered, drop counter metric is exported, and capacity is tuned per device type (not just default 100)
- [ ] **K8s Lease HA:** Leader acquires lease -- but verify split-brain fencing delay is implemented, renewal failure retry with backoff exists, lease release on SIGTERM is in shutdown sequence step 1
- [ ] **OpenTelemetry Metrics:** Metrics appear in collector -- but verify `Meter` is registered with `AddMeter()`, cardinality limit is configured per instrument, export interval is tuned for shutdown data loss window, and `MeterProvider` disposes last
- [ ] **Health Probes:** Return 200 -- but verify liveness does not check external dependencies (only liveness vector), readiness checks are comprehensive (channels + scheduler), startup probe gates liveness/readiness, and probe timeouts don't exceed K8s probe period
- [ ] **Graceful Shutdown:** Pod stops cleanly -- but verify `HostOptions.ShutdownTimeout` is extended from 5s default, shutdown step order matches DI registration reverse order, SNMP sockets are closed before channel drain, and telemetry flush has reserved budget
- [ ] **Structured Logging:** Logs include context fields -- but verify high-cardinality values (OIDs, varbind values) are not used as log properties, message templates use structured parameters (`{DeviceName}` not string interpolation `$"{deviceName}"`), and log level filtering works correctly per category

## Recovery Strategies

When pitfalls occur despite prevention, how to recover.

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| UDP buffer drops (Pitfall 1) | LOW | No data recovery possible (UDP is lossy). Increase buffer, restart pod. Lost traps are gone; next poll cycle rebuilds state. Add monitoring to prevent recurrence. |
| Misfire burst polling (Pitfall 2) | LOW | Reconfigure misfire instruction to DoNothing, restart scheduler. No persistent damage -- devices handle burst gracefully. Verify no SNMP rate-limiting was triggered on devices. |
| Shutdown timeout starvation (Pitfall 3) | LOW | Extend `ShutdownTimeout`, redeploy. Lost telemetry cannot be recovered but is typically low-value (shutdown window data). |
| Split-brain duplicate metrics (Pitfall 4) | MEDIUM | Identify duplicate window from OTLP collector logs. Delete duplicate series if backend supports it. Implement fencing to prevent recurrence. May need to adjust dashboards that double-counted during the window. |
| MeterProvider early disposal (Pitfall 5) | LOW | Reorder DI registration, redeploy. Lost metrics are gone but will be recollected on next export interval. |
| Silent channel drops (Pitfall 6) | MEDIUM | Add drop callback and redeploy. Historical drop data is unrecoverable -- no way to know what was lost before monitoring was added. Increase channel capacity if drops were frequent. |
| Cardinality explosion in OTLP | HIGH | Requires backend cleanup (dropping high-cardinality series), dashboard reconfiguration, and application code change to limit labels. May require OTLP backend storage reclaim. |
| Community string exposure | HIGH | Rotate community string on ALL monitored devices. Revoke compromised credentials. Audit access logs. Move to K8s Secrets. Consider SNMPv3 migration timeline. |

## Pitfall-to-Phase Mapping

How roadmap phases should address these pitfalls.

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| UDP buffer drops (Pitfall 1) | Phase 1: Framework/Listener | Integration test: send 1000 traps in 1 second burst; verify zero `RcvbufErrors`; verify all traps processed |
| Misfire burst polling (Pitfall 2) | Phase 1: Framework/Scheduler | Unit test: simulate job that exceeds interval; verify next trigger is skipped (DoNothing), not queued |
| Shutdown timeout starvation (Pitfall 3) | Phase 1: Framework/Host Setup | Integration test: send SIGTERM during active polls; verify all shutdown steps complete; verify telemetry flush succeeds |
| Split-brain duplicate metrics (Pitfall 4) | Phase 1: Framework/HA | Chaos test: partition leader from API server for >TTL; verify no duplicate metrics in collector; verify single leader after partition heals |
| MeterProvider early disposal (Pitfall 5) | Phase 1: Framework/Telemetry | Verify DI registration order in `Program.cs` code review; integration test: shut down during active metric recording; verify final batch reaches collector |
| Silent channel drops (Pitfall 6) | Phase 1: Framework/Channels | Load test: flood single device channel beyond capacity; verify `simetra_channel_drops_total` increments; verify Warning log emitted |
| Poll job stagger (Performance) | Phase 2: First Real Device Module | Load test: register 10 devices with same interval; verify polls are spread across interval window; verify no concurrent SNMP timeout spikes |
| Cardinality explosion (Performance) | Phase 1: Framework/Telemetry | Verify OTel cardinality limit is set per instrument; unit test: record >2000 unique tag combinations; verify overflow attribute is used |
| Community string in config (Security) | Phase 1: Framework/Configuration | Deployment checklist: verify community string loaded from K8s Secret; verify appsettings.json has placeholder, not real value |
| Log structured parameters (Logging) | Phase 1: Framework/Logging | Code review: verify all log calls use message templates with `{}` parameters, never `$""` interpolation; verify no OID strings used as structured properties |

## Sources

### Official Documentation (HIGH confidence)
- [OpenTelemetry .NET Metrics Best Practices](https://opentelemetry.io/docs/languages/dotnet/metrics/best-practices/) -- Meter lifecycle, cardinality limits, instrument disposal
- [OpenTelemetry .NET Metrics README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md) -- MeterProvider shutdown, ForceFlush, AddMeter
- [Microsoft Learn: System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) -- BoundedChannelOptions, FullMode, itemDropped callback
- [Quartz.NET Hosted Services Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html) -- WaitForJobsToComplete, lifecycle
- [Quartz.NET Simple Triggers](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/simpletriggers.html) -- Misfire instructions, SmartPolicy behavior
- [Kubernetes Leases](https://kubernetes.io/docs/concepts/architecture/leases/) -- Lease API design, coordination group
- [.NET Graceful Shutdown in Kubernetes (dotnet-docker)](https://github.com/dotnet/dotnet-docker/blob/main/samples/kubernetes/graceful-shutdown/graceful-shutdown.md) -- SIGTERM, IHostApplicationLifetime

### Verified Blog Posts and GitHub Issues (MEDIUM confidence)
- [Andrew Lock: Extending Shutdown Timeout for IHostedService](https://andrewlock.net/extending-the-shutdown-timeout-setting-to-ensure-graceful-ihostedservice-shutdown/) -- 5s default, reverse ordering, shared token
- [OpenTelemetry .NET Discussion #3614](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3614) -- ForceFlush on shutdown, provider disposal
- [Kubernetes Issue #23731: Split-Brain in Leader Election](https://github.com/kubernetes/kubernetes/issues/23731) -- Documented split-brain as known property
- [Kubernetes Issue #67651: Client-Go Leader Election Split-Brain](https://github.com/kubernetes/kubernetes/issues/67651) -- Race condition in resource version handling
- [dotnet/runtime Issue #36522: BoundedChannel Drop Notification](https://github.com/dotnet/runtime/issues/36522) -- API proposal and implementation of itemDropped callback
- [Quartz.NET Issue #1109: DoNothing with Cron Triggers](https://github.com/quartznet/quartznet/issues/1109) -- Inconsistent behavior based on misfire threshold
- [Quartz.NET Discussion #1641: DisallowConcurrentExecution in Cluster](https://github.com/quartznet/quartznet/discussions/1641) -- Cluster-level concurrency not guaranteed
- [SharpSnmpLib GitHub](https://github.com/lextudio/sharpsnmplib) -- Thread pool optimization, ObjectStore thread safety

### Community Sources (LOW confidence -- verify before relying on)
- [NetCraftsmen: Syslog, SNMP Traps, and UDP Packet Loss](https://netcraftsmen.com/syslog-snmp-traps-and-udp-packet-loss/) -- snmptrapd buffer sizing
- [Fairwinds: Kubernetes Liveness Probes Best Practices](https://www.fairwinds.com/blog/a-guide-to-understanding-kubernetes-liveness-probes-best-practices) -- Probe anti-patterns
- [Kubernetes Ingress-NGINX Issue #11287](https://github.com/kubernetes/ingress-nginx/issues/11287) -- Lease renewal timeout leading to pod restart

---
*Pitfalls research for: .NET SNMP Supervisor Service (Simetra)*
*Researched: 2026-02-15*
