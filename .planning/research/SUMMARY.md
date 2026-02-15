# Project Research Summary

**Project:** Simetra — Headless .NET Core 9 SNMP Supervisor Service
**Domain:** Network Monitoring & Telemetry Collection (Kubernetes-native)
**Researched:** 2026-02-15
**Confidence:** HIGH

## Executive Summary

Simetra is a headless SNMP supervisor service that combines trap reception with scheduled polling to collect device telemetry and emit it via OpenTelemetry Protocol (OTLP). The expert approach is a four-layer pipeline architecture: Layer 1 (UDP listener) receives SNMP traps, Layer 2 (routing/filtering) isolates devices using bounded channels with backpressure, Layer 3 (extraction) transforms SNMP varbinds to domain objects using a unified PollDefinitionDto system, and Layer 4 (processing) splits into two branches — metrics export (OTLP) and state vector updates (in-memory last-known-state). Scheduled polling via Quartz.NET intentionally bypasses the channel layer to avoid trap-flood interference. Kubernetes Lease-based leader-follower HA ensures only one pod exports metrics/traces (avoiding duplicates) while all pods process traps and export logs. A plugin system (IDeviceModule) using the Strategy Pattern enables adding new device types without framework changes.

The recommended stack centers on .NET 9 with SharpSnmpLib (12.5.7) for SNMP operations, Quartz.NET (3.15.1) for cron-based scheduling, KubernetesClient (18.0.13) for Lease API leader election, OpenTelemetry SDK (1.15.0) for vendor-neutral observability, System.Threading.Channels for bounded device isolation, and Polly v8 for resilience (retry/timeout/circuit breaker). All core dependencies are actively maintained with verified .NET 9 compatibility.

The primary risks are silent data loss from UDP buffer drops under trap storms (requires explicit SO_RCVBUF tuning and kernel rmem_max config), Quartz misfire handling creating burst polling (mitigated with explicit DoNothing instruction), and .NET's 5-second default shutdown timeout starving late-registered services (requires extending HostOptions.ShutdownTimeout to 25s). Additional pitfalls include Kubernetes Lease API split-brain windows (mitigate with leader fencing delay), channel DropOldest mode losing data silently without callbacks (requires .NET 6+ itemDropped delegate), and MeterProvider disposal ordering causing final metrics loss (register OpenTelemetry providers first in DI). All critical pitfalls have documented prevention strategies that must be implemented in Phase 1 (framework setup) before adding real device modules.

## Key Findings

### Recommended Stack

The stack is optimized for a headless, Kubernetes-native supervisor service monitoring approximately 5 devices per site. All core libraries are production-ready with explicit .NET 9 support, actively maintained (all published within past 6 months), and dependency-free or minimal-dependency. The architecture avoids heavy frameworks (no full NMS platform, no dashboard UI, no persistent storage) in favor of focused, composable components.

**Core technologies:**
- **.NET 9 SDK + Worker Service template** — current LTS-successor release with BackgroundService pattern for headless services. ASP.NET Core 9 minimal API added solely for Kubernetes health probe endpoints (/healthz, /readyz, /startupz). Native AOT support available if container startup speed becomes critical.
- **SharpSnmpLib 12.5.7** — the only actively maintained, MIT-licensed, pure .NET SNMP library with full async API (GetAsync, SetAsync, WalkAsync, BulkWalkAsync, SendTrapV2Async). Supports SNMPv1/v2c/v3. Zero dependencies. Published Nov 2025, targets net8.0+ (compatible with net9.0). Trap listener uses event-based pattern; polls use async Messenger API.
- **Quartz.NET 3.15.1** — enterprise-grade job scheduler with .NET 9 support, DI-native job resolution, in-memory store (no persistence requirement), cron expressions, misfire handling, and graceful shutdown via WaitForJobsToComplete. Quartz.AspNetCore auto-registers health checks. Published Oct 2025.
- **KubernetesClient 18.0.13** — official CNCF C# client for K8s API, targets net8.0/net9.0. Provides LeaseLock and LeaderElector classes for coordination.k8s.io/v1 Lease-based leader election. Published Dec 2025.
- **OpenTelemetry SDK 1.15.0** — unified observability (traces, metrics, logs) with OTLP exporter for vendor-neutral telemetry. All core packages aligned at v1.15.0 (Jan 2025). Quartz instrumentation (1.12.0-beta.1) auto-captures job execution spans.
- **System.Threading.Channels** — ships with .NET 9 runtime, zero dependency. Provides bounded channels with backpressure (DropOldest mode) for device isolation. High-performance, allocation-free async producer/consumer pipelines. .NET 9 redesigned internals for lower memory usage.
- **Polly v8 / Microsoft.Extensions.Resilience** — composable retry/timeout/circuit breaker pipelines for SNMP operations. Polly.Core 8.6.5 (Oct 2025) is the industry-standard .NET resilience library. Microsoft.Extensions.Resilience 8.x provides DI integration with named pipelines.

**Critical version requirements:**
- Lextm.SharpSnmpLib: >=12.5.7 (earlier versions lack async trap sending)
- Quartz.AspNetCore: >=3.15.1 (net9.0 target, health check auto-registration)
- OpenTelemetry.Instrumentation.Quartz: 1.12.0-beta.1 (do NOT use deprecated Quartz.OpenTelemetry.Instrumentation 3.15.x)
- KubernetesClient: >=18.0.13 (LeaseLock API, net9.0 target)

**What NOT to use:**
- SnmpSharpNet (unmaintained since 2019, sync-only, .NET Framework)
- Hangfire (requires persistent storage, designed for web app background jobs)
- Serilog as primary logging (OpenTelemetry log bridge handles structured logging via ILogger natively)
- ConfigMapLock for leader election (legacy; LeaseLock uses purpose-built Lease resource)

### Expected Features

Research across Prometheus SNMP Exporter, OpenTelemetry SNMP Receiver, Zabbix, and industry NMS platforms reveals a clear feature landscape. Simetra positions between lightweight exporters (stateless, polling-only) and full NMS platforms (alerting, UI, discovery, persistence) by combining trap handling + polling with a State Vector for instant state queries while deliberately excluding features that belong in downstream systems.

**Must have (table stakes):**
- **SNMP trap reception (UDP port 162)** — device-initiated events are the primary input path. Without traps, the service is blind to real-time events.
- **SNMP polling (scheduled GET/GETBULK)** — required to detect silent failures (device unable to send traps) and collect metrics only available via polling. Industry consensus: polling + traps together, never traps alone.
- **OID-to-metric extraction** — transforms raw SNMP varbinds (OID/type/value tuples) into domain-meaningful metrics using PollDefinitionDto system. Must handle INTEGER, STRING, Counter32, Gauge32, IpAddress types with enum mapping for status fields.
- **Device-level isolation via channels** — one bounded channel per device prevents trap floods from one device starving others. BoundedChannelFullMode.DropOldest with itemDropped callback for monitoring.
- **Telemetry export (OTLP)** — the service is a telemetry producer. Metrics/traces/logs must export reliably. Base labels (site, device_name, device_ip, device_type) on every metric are table stakes for queryability.
- **Leader-follower HA via K8s Lease** — single-instance supervisor is a single point of failure. Minimum 2 replicas with automatic failover (lease TTL ~15s). Leader-only metric/trace export; all pods export logs.
- **Health probes (startup/readiness/liveness)** — Kubernetes requires probes for pod lifecycle. Startup probe gates on first correlationId, readiness checks channels + scheduler, liveness checks job staleness via liveness vector.
- **Device plugin system (IDeviceModule)** — different device types have different OID trees and trap formats. Strategy pattern with Open/Closed Principle enables adding device types without framework changes.
- **Correlation ID system** — time-window-based correlation (shared ID across events in an interval) for tracing events through the 4-layer pipeline. Generated at startup before first job fires.
- **Graceful shutdown** — 30s time budget for draining channels, completing in-flight polls, releasing lease, flushing telemetry. K8s terminationGracePeriodSeconds alignment required.

**Should have (competitive advantage):**
- **State Vector (last-known-state per device)** — enables operators to query current device state without waiting for next poll. Most SNMP exporters are stateless. Source-based routing (Module polls update State Vector, Configuration polls do not) distinguishes state from metrics.
- **Heartbeat loopback (end-to-end self-test)** — Simetra virtual device sends SNMP trap to itself through full pipeline. Validates scheduler alive (liveness stamp) AND pipeline flowing (State Vector update). Uncommon in SNMP supervisors.
- **Configurable metric polls (Source=Configuration)** — operators add metrics in appsettings.json without code changes. Migration path: prototype in config, promote to code for State Vector integration.
- **Composable middleware chain** — ASP.NET-style middleware pattern for SNMP pipeline (non-HTTP). Cross-cutting concerns (correlationId, structured logging, error handling) applied uniformly.
- **Trap flood protection** — bounded channels with DropOldest provide backpressure without explicit rate limiting. Channel-level notification on drops prevents log flooding.
- **Poll bypass of channels** — polls skip Layer 2 routing entirely, going directly to Layer 3 extractor. Prevents poll starvation during trap floods.
- **Graceful lease release on SIGTERM** — explicit release enables near-instant failover vs. waiting for lease TTL expiry (~15s).

**Defer to v2+ (anti-features for v1):**
- **Built-in alerting / threshold evaluation** — belongs in downstream tools (Grafana Alerting, Prometheus Alertmanager). Adding alerting couples data collection with alert policy.
- **SNMP auto-discovery (CDP/LLDP/network scan)** — for ~5 devices per site, static configuration is simpler and safer. Discovery introduces failure modes that overwhelm simplicity benefit.
- **MIB compilation / MIB browser** — for ~5 device types, hand-authored PollDefinitionDto definitions are clearer and testable. Use external MIB browser during development.
- **Persistence / historical data storage** — the supervisor is a telemetry producer, not a time-series database. OTLP backend handles storage.
- **Web UI / dashboard** — headless by design. Consume OTLP metrics in Grafana.
- **Dynamic configuration (hot reload)** — for ~5 devices, pod restart (rolling update in K8s) is safer and more predictable. Hot reload requires versioning, partial-apply semantics, testing complexity.
- **SNMPv3 support** — adds USM user management, engine ID discovery, authentication (MD5/SHA), privacy (DES/AES). Defer until security audit requires it.
- **SNMP SET operations** — the supervisor is read-only. Device configuration belongs in dedicated tools (Ansible, Terraform).

### Architecture Approach

The architecture is a four-layer pipeline with strict separation of concerns. Layer 1 (SnmpListenerService) binds UDP socket on port 162, receives v2c traps, attaches correlationId, and routes to Layer 2. Layer 2 (DeviceFilter + TrapFilter) identifies source device by IP, matches trap OIDs against device module definitions, and writes to device-specific Channel<TrapContext> (bounded, DropOldest). Layer 3 (SnmpExtractorService) transforms varbinds to DomainObject using PollDefinitionDto. Layer 4 splits: Branch A creates OTLP metrics with enforced base labels (leader-gated export), Branch B updates State Vector (Source=Module only).

Scheduled polling intentionally bypasses Layer 2 channels. Quartz jobs execute SNMP GET directly, route responses to Layer 3 extractor, and stamp liveness vector on completion (even if poll failed — detects hangs, not errors). This prevents polls from being blocked by trap-flood backpressure in channels.

**Major components:**
1. **LeaderElectionService (IHostedService)** — K8s Lease acquire/renew/release loop. Exposes IsLeader property. On role change, gates RoleGatedExporter. Graceful lease release on SIGTERM for fast failover.
2. **SnmpListenerService (BackgroundService)** — UDP socket loop reading datagrams. SharpSnmpLib parses SNMP PDU. Attaches correlationId from CorrelationService. Calls DeviceFilter.Identify() → TrapFilter.Filter() → Channel.TryWrite() (non-blocking).
3. **Quartz Scheduler + Jobs** — hosts StatePollJob (Source=Module, updates State Vector + metrics), MetricPollJob (Source=Configuration, metrics only), HeartbeatJob (loopback trap), CorrelationJob (ID rotation + liveness stamp). WaitForJobsToComplete=true for graceful shutdown.
4. **Channel<TrapContext> per device** — bounded buffer isolating trap processing. BoundedChannelOptions { FullMode = DropOldest, SingleReader = true }. ChannelConsumerService per device reads via ReadAllAsync() and drives middleware chain.
5. **Device Module Registry (IDeviceModule)** — Strategy Pattern. Each module provides TrapDefinitions, StatePollDefinitions, MetricPollDefinitions. SimetraModule (virtual device) proves framework before adding real devices.
6. **SnmpExtractorService** — stateless transformation from SNMP varbinds to DomainObject. Driven by PollDefinitionDto.Oids with EnumMap resolution and SNMP type conversion.
7. **MetricFactoryService + StateVectorService** — parallel processing branches. MetricFactory creates System.Diagnostics.Metrics instruments with base labels, records measurements, exports via RoleGatedExporter (wraps OTLP exporter, checks IsLeader). StateVectorService stores ConcurrentDictionary<string, StateVectorEntry> with timestamp + correlationId.
8. **Health Probes (Minimal API)** — three endpoints with tag-based filtering. StartupHealthCheck (correlationId exists), ReadinessHealthCheck (channels + scheduler running), LivenessHealthCheck (liveness vector staleness check).

**Key architectural patterns:**
- **IHostedService registration ordering** — sequential startup (DO NOT enable ServicesStartConcurrently). Order: LeaderElection → Listener → Consumers → Quartz. Reversed on shutdown.
- **Channel-per-device with BackgroundService consumers** — one thread per device at ~5 devices is acceptable. Switch to worker pool at 20+ devices.
- **Quartz.NET with DI integration** — jobs resolved from container, scoped dependencies injected. DisallowConcurrentExecution prevents poll overlap.
- **Custom pipeline middleware (non-HTTP)** — composable delegate chain (CorrelationIdMiddleware → LoggingMiddleware → ErrorHandlingMiddleware → terminal handler). Same pattern as ASP.NET, zero HTTP overhead.
- **Role-gated OTLP exporters** — decorator around BaseExporter<T> checks IsLeader before forwarding. Logs always exported (all pods), metrics/traces leader-only.

**Project structure:** Single .NET 9 project. Directories: Configuration/ (Options classes), Models/ (DTOs), Devices/ (IDeviceModule + implementations), Services/ (pipeline layers), Jobs/ (Quartz), Health/ (IHealthCheck), Middleware/ (non-HTTP), Telemetry/ (OTel setup), Extensions/ (DI wiring). No multi-project solution needed at this scale.

### Critical Pitfalls

Research identified six critical pitfalls that cause rewrites, data loss, or production outages. All have documented prevention strategies that must be implemented during framework setup (Phase 1).

1. **SharpSnmpLib UDP buffer drops under load** — default OS buffer sizes (8KB-64KB) insufficient during trap storms. Kernel drops packets before application sees them; no notification. Prevention: explicitly set SO_RCVBUF to 4MB, configure sysctl net.core.rmem_max=8MB, call ThreadPool.SetMinThreads() before listener start. Monitor /proc/net/snmp RcvbufErrors. Address in Phase 1 (Framework/Listener).

2. **Quartz misfire handling causes burst polling** — default SmartPolicy creates unpredictable behavior when jobs exceed their interval. Misfires queue with DisallowConcurrentExecution, then execute back-to-back, overwhelming devices. Prevention: explicitly set WithMisfireHandlingInstructionDoNothing() on all triggers (skip late polls), set misfire threshold to match interval, add SNMP timeouts shorter than poll interval. Address in Phase 1 (Framework/Scheduler).

3. **.NET graceful shutdown timeout starvation** — default HostOptions.ShutdownTimeout of 5 seconds for ALL services combined. Late-registered services (OTel providers) get zero time, losing final metrics. Prevention: extend to 25 seconds, control registration order (OTel first = stops last), implement per-step timeout budgets in StopAsync, match K8s terminationGracePeriodSeconds. Address in Phase 1 (Framework/Host Setup).

4. **Kubernetes Lease API split-brain** — optimistic concurrency does not guarantee mutual exclusion. Network partitions or clock skew allow two leaders briefly, causing duplicate OTLP metrics. Prevention: leader fence pattern (wait one TTL before exporting), leader epoch counter in lease annotations, set poll intervals >15s (longer than TTL). Address in Phase 1 (Framework/HA).

5. **MeterProvider disposal ordering loses final metrics** — if MeterProvider disposes before pipeline components finish, last measurements are silently lost. If not disposed at all, PeriodicExportingMetricReader never flushes final batch (default 60s interval). Prevention: register OTel providers first in DI (dispose last), call ForceFlush() with timeout in shutdown, reduce export interval to 15-30s. Address in Phase 1 (Framework/Telemetry).

6. **Channel DropOldest loses data silently** — bounded channel drops oldest item when full, but without itemDropped callback (added .NET 6+), there's zero notification. Prevention: always use Channel.CreateBounded<T> overload with Action<T> callback, increment per-device counter metric (simetra_channel_drops_total), log at Warning level with device name. Address in Phase 1 (Framework/Channels).

**Additional risks:**
- Poll jobs firing simultaneously (thundering herd) — stagger start times using device_index * (interval / device_count).
- SNMP community string in appsettings.json committed to Git — load from K8s Secret via environment variable.
- Cardinality explosion from per-OID instruments — create instruments by metric name (from PollDefinitionDto), not by OID.
- Synchronous SNMP blocking Quartz threads — use async operations (GetRequestMessage.GetResponseAsync()).

## Implications for Roadmap

Based on combined research, the roadmap should follow a pipeline-first build strategy where core data transformation logic is proven before adding I/O complexity. The framework must be fully operational with the Simetra virtual device (heartbeat loopback validating end-to-end pipeline) before adding real device modules. All six critical pitfalls must be addressed during Phase 1 framework setup — retrofitting UDP buffer tuning, misfire policies, shutdown timeouts, or channel callbacks is error-prone and risks data loss.

### Phase 1: Framework + Virtual Device (MVP)
**Rationale:** Prove the complete four-layer pipeline, leader-follower HA, health probes, graceful startup/shutdown, and device plugin system with zero external dependencies (no real SNMP devices needed). The Simetra virtual device sending heartbeat traps to itself validates both the scheduler (job fires) and the pipeline (trap flows through all layers). This is the critical path — all subsequent phases depend on this foundation.

**Delivers:**
- Complete four-layer pipeline (Listener → Routing/Filtering → Extraction → Processing)
- Channel-per-device isolation with DropOldest + itemDropped callback monitoring
- Quartz scheduler with StatePollJob, MetricPollJob, HeartbeatJob, CorrelationJob
- K8s Lease-based leader election with graceful release on SIGTERM
- OTLP export (metrics/traces/logs) with role-gated exporters
- State Vector (in-memory, ConcurrentDictionary) with Source-based routing
- Correlation ID system (time-window-based, generated at startup)
- Liveness vector with job staleness checking
- Three health probes (startup/readiness/liveness) via minimal API
- 11-step graceful startup sequence + 30s shutdown with time budgets
- IDeviceModule plugin system proven with SimetraModule
- Heartbeat loopback end-to-end validation

**Addresses pitfalls:**
- Pitfall 1: SO_RCVBUF tuning, ThreadPool.SetMinThreads, rmem_max config
- Pitfall 2: Explicit DoNothing misfire instruction on all triggers
- Pitfall 3: HostOptions.ShutdownTimeout = 25s, DI registration ordering
- Pitfall 4: Leader fence delay (wait 1 TTL before export), epoch counter
- Pitfall 5: OTel provider DI registration first, ForceFlush in shutdown
- Pitfall 6: Channel.CreateBounded with itemDropped callback, drop counter metric

**Implements architecture:**
- All major components (LeaderElection, Listener, Scheduler, Channels, Registry, Extractor, MetricFactory, StateVector)
- All architectural patterns (IHostedService ordering, channel-per-device, Quartz DI, pipeline middleware, role-gated exporters)
- PollDefinitionDto unified structure for traps, state polls, metric polls

**Technology used:**
- .NET 9 Worker + Minimal API for health endpoints
- SharpSnmpLib 12.5.7 (trap listener + SNMP GET)
- Quartz.NET 3.15.1 (scheduler + jobs)
- KubernetesClient 18.0.13 (Lease API)
- OpenTelemetry SDK 1.15.0 + OTLP exporter
- System.Threading.Channels (bounded, DropOldest)
- Polly v8 / Microsoft.Extensions.Resilience (retry/timeout/circuit breaker for SNMP)

**Research needed:** None — standard patterns verified via official docs. Potential integration testing with kind/minikube for Lease API behavior under partition scenarios (Pitfall 4 validation).

**Success criteria:**
- Heartbeat loopback completes end-to-end: HeartbeatJob sends trap → SnmpListener receives → routes to Simetra channel → extractor processes → State Vector updates + metric created → OTLP export (leader only) → liveness stamp recorded
- All six critical pitfalls have implemented prevention (verified via code review checklist)
- Health probes return correct status at each lifecycle stage (startup gates, readiness comprehensive, liveness staleness check)
- Graceful shutdown completes all steps within 25s budget, no telemetry loss
- Load test: 1000 traps/second to single device channel with drops monitored via callback metric

### Phase 2: First Real Device Module
**Rationale:** Proves the plugin system works for actual SNMP devices, not just virtual ones. Validates OID extraction, enum mapping, multi-OID polls, real device trap formats. Use a simple device type (router or switch with standard MIB-II support) to avoid complex vendor-specific OID trees. This phase tests whether the PollDefinitionDto abstraction is sufficient or needs refinement before adding more device types.

**Delivers:**
- First IDeviceModule implementation for a real device (e.g., RouterModule)
- Trap definitions for device-specific traps (linkUp/linkDown from IF-MIB)
- State poll definitions (interface status, uptime)
- Metric poll definitions (interface counters, CPU, memory)
- EnumMap for device status fields (ifAdminStatus, ifOperStatus)
- Multi-OID BULK WALK for interface tables
- Device-specific resilience policy (per-device SNMP timeout/retry tuning)

**Uses stack elements:**
- SharpSnmpLib BulkWalkAsync for table walks (interface tables)
- Polly resilience pipeline with per-device timeout configuration
- Existing PollDefinitionDto system (validate sufficiency)

**Implements architecture:**
- Device module registration in Program.cs
- Channel creation for new device type
- ChannelConsumerService instantiation per device
- Quartz trigger creation from device module poll definitions
- State Vector tenant for real device

**Addresses features:**
- First step toward supporting 5 device types
- Validates configurable metric polls (Source=Configuration) vs. state polls (Source=Module)
- Tests poll bypass of channels (direct to Layer 3)

**Research needed:** Likely YES — phase-level research for specific device MIB (IF-MIB, HOST-RESOURCES-MIB, CISCO-PROCESS-MIB depending on device vendor). Use `/gsd:research-phase` to document OID mappings, typical trap formats, polling intervals, known device quirks.

**Dependencies:** Phase 1 complete, real device available for testing (or SNMP simulator like snmpsim).

### Phase 3: Multi-Device Scaling + Performance
**Rationale:** Validate the architecture scales from 1 device to 5 devices per site. Test poll staggering, concurrent SNMP operations, channel isolation under mixed load, OTLP cardinality management. Identify performance bottlenecks before production deployment.

**Delivers:**
- Poll job staggering (StartAt offset = device_index * interval / device_count)
- Quartz thread pool tuning (MaxConcurrency >= 2x concurrent polls)
- OTLP metric cardinality limits per instrument (max 2000 unique tag combos)
- Channel capacity tuning per device type (chatty vs. quiet devices)
- Kubernetes resource requests/limits based on observed usage
- Performance benchmarks (traps/second, polls/second, OTLP export batch size)

**Addresses pitfalls:**
- Poll thundering herd (stagger triggers)
- Quartz thread pool starvation (async operations + pool sizing)
- Cardinality explosion (instruments by metric name, not OID)
- OTLP export batch size tuning (PeriodicExportingMetricReader interval)

**Implements architecture:**
- Scaling Considerations from ARCHITECTURE.md (5-20 device range)
- Performance Traps prevention strategies

**Research needed:** Unlikely — standard performance tuning patterns. May need quick reference lookup for Quartz thread pool config and OTel cardinality options.

**Dependencies:** Phase 2 complete, 5 device configurations ready.

### Phase 4: Operational Readiness
**Rationale:** Add observability, monitoring, and operational tooling needed for production. This includes distributed tracing (debugging pipeline issues), state vector staleness checking (detect silent device failures), Quartz job listeners (misfire monitoring), and runbook documentation.

**Delivers:**
- Distributed tracing (OTLP traces) with end-to-end spans for trap/poll processing
- State Vector staleness detection (alert if last update >N * poll interval)
- Quartz ITriggerListener for misfire event logging
- Listener health check added to readiness probe
- .NET runtime metrics auto-instrumentation (GC, thread pool, memory)
- Operational runbooks (troubleshooting, deployment, rollback)
- Kubernetes manifests (Deployment, Service, RBAC, NetworkPolicy, ConfigMap, Secret)

**Addresses features:**
- Distributed tracing (differentiator, MEDIUM complexity)
- State vector staleness checking (MEDIUM complexity)
- .NET runtime metrics (LOW complexity, auto-instrumentation)

**Research needed:** No — all standard patterns. OpenTelemetry tracing docs + Quartz listener API are well-documented.

**Dependencies:** Phase 3 complete, production K8s cluster available.

### Phase 5: Additional Device Modules (v1.x)
**Rationale:** Expand device type coverage based on actual deployment needs. Each new module follows the proven IDeviceModule pattern from Phase 2. These can be added incrementally post-launch as new device types are required.

**Delivers:**
- Device modules for remaining device types (switches, UPS, servers, etc.)
- Per-module trap definitions, state polls, metric polls
- Device-specific enum maps and OID trees
- Per-device resilience tuning

**Research needed:** YES per device type — each new device type requires phase-level research for its MIB structure, trap formats, polling best practices, vendor quirks.

**Dependencies:** Phase 4 complete, production deployment validated with initial device types.

### Phase Ordering Rationale

- **Framework before devices:** Phase 1 builds the complete pipeline with zero external dependencies (virtual device only). This enables parallel work: framework development + device research can happen concurrently. Adding real devices to an unproven framework causes confusion between "framework bug" vs. "device-specific issue."

- **One real device before scaling:** Phase 2 validates the plugin abstraction with one real device. If PollDefinitionDto is insufficient or device module pattern has issues, we discover it with one device, not five. Cheaper to refactor early.

- **Performance after functionality:** Phase 3 tunes what Phase 1-2 built. No point optimizing poll staggering before the scheduler works. Scaling validation requires multiple devices (Phase 2 prerequisite).

- **Operational readiness before production:** Phase 4 adds tracing, monitoring, runbooks after core functionality is proven. Distributed tracing is debugging tooling — needed for production, but not for proving the framework works.

- **Device expansion post-launch:** Phase 5 is incremental. Each new device module is independent work following an established pattern.

**Dependencies chain:** Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 (iterative). Phases 1-4 are sequential. Phase 5 modules can be added in any order.

### Research Flags

**Needs phase-level research:**
- **Phase 2 (First Real Device)** — device-specific MIB research required. OID mappings, trap formats, polling intervals, vendor quirks. Use `/gsd:research-phase` to document findings for specific device type (router, switch, UPS, etc.).
- **Phase 5 (Additional Devices)** — each new device type needs its own MIB research. Standard process established by Phase 2.

**Standard patterns (skip research-phase):**
- **Phase 1 (Framework)** — all patterns verified via official Microsoft, Quartz.NET, OpenTelemetry, and Kubernetes docs. No additional research needed. Proceed directly to requirements.
- **Phase 3 (Performance)** — standard tuning (Quartz thread pool, OTel cardinality, K8s resources). Quick reference lookup sufficient.
- **Phase 4 (Operational)** — standard observability patterns. OpenTelemetry tracing, Quartz listeners, K8s manifests all well-documented.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All versions verified via NuGet (published Nov 2025-Jan 2026). API compatibility confirmed via official docs and GitHub source. Zero deprecated packages. All libraries actively maintained. |
| Features | HIGH | Feature landscape validated across multiple tools (Prometheus exporter, OTel receiver, Zabbix, NNMi). Table stakes vs. differentiators vs. anti-features backed by industry consensus and competitor analysis. |
| Architecture | HIGH | Four-layer pipeline pattern verified via Microsoft official docs (BackgroundService, Channels, HostedService). Quartz DI integration official docs. K8s Lease API official docs. Custom middleware is MEDIUM (community pattern, not Microsoft-documented), but delegate shape is proven from ASP.NET Core. |
| Pitfalls | HIGH | All six critical pitfalls verified via official sources (Microsoft Learn, Quartz docs, OTel docs, K8s issues) or high-quality community analysis (Andrew Lock, Steve Gordon). Split-brain Lease API behavior documented in K8s GitHub issues #23731, #67651. |

**Overall confidence:** HIGH

The combination of official documentation verification, active library maintenance (all packages updated within past 6 months), explicit .NET 9 compatibility, and well-documented pitfall prevention strategies provides strong confidence in the recommended approach. The only MEDIUM-confidence element is the custom non-HTTP middleware pattern, which is a community practice rather than framework-provided, but the 30-50 lines of infrastructure code is low-risk and well-understood from ASP.NET Core.

### Gaps to Address

**Gap 1: KubernetesClient Lease API fencing specifics**
- **Issue:** The .NET KubernetesClient (18.0.13) provides raw Lease CRUD but no high-level LeaderElector abstraction like Go's client-go. The acquire/renew/release loop must be hand-coded. Split-brain mitigation (leader fence delay, epoch counter) is based on distributed systems best practices, not .NET-specific verified implementations.
- **Handle during:** Phase 1 planning. Research existing .NET Lease-based leader election implementations (open-source projects, GitHub samples). Implement leader fence as a configurable delay (default 1 TTL = 15s) before enabling metric/trace export. Add integration test with kind cluster simulating network partition (block API server traffic for >TTL, verify single leader after recovery).

**Gap 2: SharpSnmpLib trap listener thread pool tuning values**
- **Issue:** SharpSnmpLib documentation recommends calling ThreadPool.SetMinThreads() but does not specify values. Default CLR thread pool grows slowly, causing initial trap bursts to queue. Research sources cite "set min threads" but not how many.
- **Handle during:** Phase 1 implementation. Start with ThreadPool.SetMinThreads(workerThreads: 50, completionPortThreads: 50). Monitor ThreadPool.GetAvailableThreads() under simulated trap storm (1000 traps/second). Tune based on observed queue depth. Document final values in operational runbook.

**Gap 3: OTLP cardinality limit enforcement mechanism**
- **Issue:** Research identifies cardinality explosion as a performance trap and recommends "limit unique tag combinations to <2000 per instrument (OTel default cap)," but does not document how OpenTelemetry .NET SDK enforces this limit or what happens when exceeded (drop, overflow attribute, error).
- **Handle during:** Phase 1 telemetry setup. Verify via OpenTelemetry .NET SDK source code or experimentation: create instrument, record >2000 unique tag combinations, observe behavior. Document whether SDK silently drops, aggregates into overflow bucket, or requires explicit configuration. Add metric for cardinality overflow events if available.

**Gap 4: Quartz misfire threshold interaction with DisallowConcurrentExecution**
- **Issue:** Research confirms explicit misfire instruction (DoNothing) prevents burst polling, but the interaction between quartz.jobStore.misfireThreshold (default 60s) and DisallowConcurrentExecution when job duration exceeds interval is not fully documented. Does the misfire threshold apply before or after concurrency check?
- **Handle during:** Phase 1 scheduler setup. Test scenario: job with 30s interval, DisallowConcurrentExecution, takes 35s to execute. Verify whether (a) next trigger waits for completion then fires immediately, or (b) next trigger is considered misfired and skipped per DoNothing. Document observed behavior. Adjust misfire threshold if needed.

## Sources

### Primary (HIGH confidence)

**Official Documentation:**
- [Microsoft Learn: Background tasks with hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0) — BackgroundService patterns, scoped DI, queued tasks
- [Microsoft Learn: Channels in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) — Updated Dec 2025, bounded/unbounded patterns, DropOldest, itemDropped callback
- [Microsoft Learn: Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) — Kubernetes liveness/readiness probes, tag-based filtering
- [Quartz.NET: Microsoft DI Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/microsoft-di-integration.html) — AddQuartz, job factory, scoped jobs
- [Quartz.NET: Hosted Services Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/hosted-services-integration.html) — AddQuartzHostedService, graceful shutdown, WaitForJobsToComplete
- [Quartz.NET: Simple Triggers](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/simpletriggers.html) — Misfire instructions, SmartPolicy behavior
- [OpenTelemetry .NET: Exporters](https://opentelemetry.io/docs/languages/dotnet/exporters/) — OTLP exporter configuration, gRPC vs HTTP/protobuf
- [OpenTelemetry .NET: Getting Started](https://opentelemetry.io/docs/languages/dotnet/getting-started/) — Full setup pattern, AddMeter, AddSource
- [OpenTelemetry .NET Metrics Best Practices](https://opentelemetry.io/docs/languages/dotnet/metrics/best-practices/) — Meter lifecycle, cardinality limits, instrument disposal
- [Kubernetes Leases Documentation](https://kubernetes.io/docs/concepts/architecture/leases/) — Official lease API for leader election, coordination.k8s.io/v1
- [.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host) — Microsoft official docs, updated 2026-02-04, IHostedService lifecycle

**NuGet Package Verification:**
- [Lextm.SharpSnmpLib 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib/) — v12.5.7, Nov 3 2025, net8.0+, MIT license, zero dependencies
- [Quartz.AspNetCore 3.15.1](https://www.nuget.org/packages/Quartz.AspNetCore) — v3.15.1, Oct 26 2025, net8.0/net9.0
- [KubernetesClient 18.0.13](https://www.nuget.org/packages/KubernetesClient/) — v18.0.13, Dec 2 2025, net8.0/net9.0/net10.0
- [OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol) — v1.15.0, Jan 21 2025
- [OpenTelemetry.Instrumentation.Quartz 1.12.0-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) — v1.12.0-beta.1, May 2025, beta but 8.8M downloads
- [Polly.Core 8.6.5](https://www.nuget.org/packages/polly.core/) — v8.6.5, Oct 2025

**GitHub Source Code:**
- [SharpSnmpLib Messenger.cs](https://github.com/lextudio/sharpsnmplib/blob/master/SharpSnmpLib/Messaging/Messenger.cs) — Confirmed async methods: GetAsync, SetAsync, WalkAsync, BulkWalkAsync
- [KubernetesClient LeaderElector](https://github.com/kubernetes-client/csharp/blob/master/src/KubernetesClient/LeaderElection/LeaderElector.cs) — RunUntilLeadershipLostAsync, events
- [KubernetesClient LeaseLock](https://kubernetes-client.github.io/csharp/api/k8s.LeaderElection.ResourceLock.LeaseLock.html) — LeaseLock constructor API

### Secondary (MEDIUM confidence)

**Verified Blog Posts:**
- [Andrew Lock: Extending Shutdown Timeout for IHostedService](https://andrewlock.net/extending-the-shutdown-timeout-setting-to-ensure-graceful-ihostedservice-shutdown/) — 5s default, reverse ordering, shared token
- [Andrew Lock: Using Quartz.NET with ASP.NET Core](https://andrewlock.net/using-quartz-net-with-asp-net-core-and-worker-services/) — DI integration, hosted service lifecycle
- [Steve Gordon: IHostedLifecycleService in .NET 8](https://www.stevejgordon.co.uk/introducing-the-new-ihostedlifecycleservice-interface-in-dotnet-8) — StartingAsync/StartedAsync hooks
- [Steve Gordon: Concurrent Hosted Service Start/Stop](https://www.stevejgordon.co.uk/concurrent-hosted-service-start-and-stop-in-dotnet-8) — ServicesStartConcurrently risks
- [Polly v8 Resilience Pipelines](https://www.pollydocs.org/pipelines/) — Pipeline composition patterns (official Polly docs)
- [Microsoft Learn: Resilient app development](https://learn.microsoft.com/en-us/dotnet/core/resilience/) — Microsoft.Extensions.Resilience integration

**GitHub Issues:**
- [Kubernetes Issue #23731: Split-Brain in Leader Election](https://github.com/kubernetes/kubernetes/issues/23731) — Documented split-brain as known property
- [Kubernetes Issue #67651: Client-Go Leader Election Split-Brain](https://github.com/kubernetes/kubernetes/issues/67651) — Race condition in resource version handling
- [dotnet/runtime Issue #36522: BoundedChannel Drop Notification](https://github.com/dotnet/runtime/issues/36522) — API proposal and implementation of itemDropped callback
- [OpenTelemetry .NET Discussion #3614](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3614) — ForceFlush on shutdown, provider disposal
- [Quartz.NET Issue #1109: DoNothing with Cron Triggers](https://github.com/quartznet/quartznet/issues/1109) — Inconsistent behavior based on misfire threshold

**Industry Sources:**
- [Prometheus SNMP Exporter (GitHub)](https://github.com/prometheus/snmp_exporter) — Architecture, module system, MIB generator
- [OpenTelemetry SNMP Receiver (GitHub)](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/receiver/snmpreceiver) — Alpha-status receiver, config-based OID polling
- [Micro Focus NNMi: Trap Deduplication](https://docs.microfocus.com/NNMi/10.30/Content/Administer/nmAdminHelp/nmAdmConfInci1800DeDupSNMP.htm) — Deduplication/enrichment/correlation features
- [LogicMonitor: How SNMP Monitoring Works](https://www.logicmonitor.com/blog/how-snmp-monitoring-works) — Industry overview of trap + polling combination

### Tertiary (LOW confidence — community sources)

- [NetCraftsmen: Syslog, SNMP Traps, and UDP Packet Loss](https://netcraftsmen.com/syslog-snmp-traps-and-udp-packet-loss/) — snmptrapd buffer sizing recommendations
- [PipelineNet (GitHub)](https://github.com/ipvalverde/PipelineNet) — Community middleware framework, referenced for non-HTTP middleware pattern validation
- [Pipeline Design Pattern in .NET (Medium)](https://medium.com/pragmatic-programming/net-things-pipeline-design-pattern-bb27e65e741e) — Community pattern article

---
*Research completed: 2026-02-15*
*Ready for roadmap: yes*
