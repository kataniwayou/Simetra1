# Simetra

## What This Is

Simetra is a headless .NET Core 9 background service that runs in Kubernetes and supervises network devices via SNMP. It receives SNMP traps, performs scheduled SNMP polls, extracts structured data, and emits telemetry (logs, metrics, traces) to an OTLP collector. One instance per site, monitoring ~5 devices through a plugin-based device module system.

## Core Value

The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP — with automatic leader-follower failover ensuring no single point of failure.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] 4-layer pipeline: Listener → Routing/Filtering → Extraction → Processing
- [ ] Single SNMP listener (SharpSnmpLib/Lextm) receiving traps on configurable UDP port
- [ ] Device filter (identify source device by IP) and trap filter (per-device OID filtering)
- [ ] Channel-per-device isolation (System.Threading.Channels, bounded, drop-oldest)
- [ ] Generic extractor using PollDefinitionDto with Oids (OID → PropertyName → optional EnumMap)
- [ ] Unified PollDefinitionDto structure for traps, state polls, and metric polls
- [ ] Source-based routing: Module → metric + State Vector; Configuration → metric only
- [ ] Processing Branch A: create metric from extracted data, send to OTLP (leader only)
- [ ] Processing Branch B: update State Vector (Source=Module only)
- [ ] IMetricFactory enforcing base labels (site, device_name, device_ip, device_type)
- [ ] Device module plugin system (strategy pattern, Open/Closed Principle)
- [ ] Simetra virtual device with heartbeat loopback through full pipeline
- [ ] Leader-follower HA via Kubernetes Lease API (coordination.k8s.io/v1)
- [ ] Leader sends logs + metrics + traces to OTLP; followers send logs only
- [ ] Graceful failover: lease release on SIGTERM for near-instant failover
- [ ] Quartz scheduler with DisallowConcurrentExecution per job key
- [ ] Correlation ID system: scheduled job generates new ID per interval, all components read current ID
- [ ] First correlationId generated directly on startup before any job fires
- [ ] SNMP state poll jobs (per device, Source=Module) — hardcoded in device module
- [ ] SNMP metric poll jobs (per device, Source=Configuration) — from appsettings.json
- [ ] Heartbeat job sending loopback trap to SNMP listener
- [ ] Correlation job generating correlationId + stamping liveness vector
- [ ] Liveness vector: one entry per scheduled job, stamped on completion
- [ ] K8s startup probe: healthy after pipeline wired + first correlationId exists
- [ ] K8s readiness probe: all channels open + Quartz scheduler running
- [ ] K8s liveness probe: HTTP handler checks liveness vector, stale stamps → 503
- [ ] OpenTelemetry integration: MeterProvider for .NET runtime + SNMP-derived metrics
- [ ] Log exporter active on all pods; metric/trace exporters gated by leader role
- [ ] Static configuration via appsettings.json (restart required for changes)
- [ ] Configurable MetricPolls[] per device in config (Source=Configuration set at load time)
- [ ] 11-step startup sequence as specified in design doc
- [ ] Graceful shutdown with CancellationToken and time-budgeted steps (~30s total)
- [ ] Composable middleware chain for cross-cutting concerns (correlationId, logging, error handling)
- [ ] Thorough unit tests for all core logic

### Out of Scope

- State Vector staleness checking (detecting dead listener) — future milestone
- Direct listener health check in readiness probe — future milestone
- Real device modules (router, switch, etc.) — added after framework is proven
- SNMPv3 support — v2c only for this milestone
- UI or API endpoints (headless service, probes only)
- Dynamic configuration (hot reload) — static config, restart required

## Context

- Design document: `requirements and basic design.txt` (v4, comprehensive)
- Brownfield codebase map exists at `.planning/codebase/` but no implementation code yet
- Pipeline architecture inspired by ASP.NET middleware pattern
- PollDefinitionDto is the unified structure across all three definition categories (trap defs, state polls, metric polls) — same DTO, different origin and Source field
- Migration path: developers prototype metric polls in config (Source=Configuration), then promote to code (Source=Module) to enable State Vector updates
- Heartbeat produces two separate stamps: liveness vector (scheduler alive) and State Vector (pipeline flowing) — only liveness vector checked in this milestone
- K8s liveness probe runs on ASP.NET thread pool, independent of Quartz jobs

## Constraints

- **Runtime**: .NET Core 9, hosted in Kubernetes
- **SNMP Library**: SharpSnmpLib (Lextm) — MIT licensed, .NET 8/9 compatible, 4.4M NuGet downloads
- **Scheduler**: Quartz.NET for scheduled jobs
- **Telemetry**: OpenTelemetry with OTLP exporter
- **HA Pattern**: Kubernetes Lease API (coordination.k8s.io/v1), not custom leader election
- **Channels**: System.Threading.Channels for device isolation
- **Config**: Static appsettings.json only, no hot reload
- **SNMP Version**: v2c only
- **Persistence**: None — state rebuilt from next poll cycle on restart

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| SharpSnmpLib (Lextm) over SnmpSharpNet | .NET 8/9 support, 4.4M downloads, actively maintained, MIT license | — Pending |
| Framework-first, no real device modules | Prove pipeline architecture before adding device-specific logic | — Pending |
| Unified PollDefinitionDto for traps + polls | Single structure simplifies extractor, enables config→code migration path | — Pending |
| Source field on DTO, not in config | Prevents misconfiguration, behavior determined by definition location | — Pending |
| Probe-driven liveness (no watchdog) | K8s triggers checks on demand, simpler than background watchdog | — Pending |
| Channel-per-device with drop-oldest | Real-time data: stale data less valuable than current, prevents backpressure cascading | — Pending |

---
*Last updated: 2026-02-15 after initialization*
