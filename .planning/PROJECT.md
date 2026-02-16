# Simetra

## What This Is

Simetra is a headless .NET 9 background service that runs in Kubernetes and supervises network devices via SNMP. It receives SNMP traps, performs scheduled SNMP polls, extracts structured data using a generic Role-based OID system, and emits telemetry (logs, metrics, traces) to an OTLP collector. One instance per site, monitoring ~5 devices through a plugin-based device module system. Leader-follower HA via K8s Lease API ensures no single point of failure.

## Core Value

The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP -- with automatic leader-follower failover ensuring no single point of failure.

## Current State

**Shipped:** v1.0 (2026-02-16)

The v1.0 milestone delivered a fully integrated, production-ready SNMP supervisor with:
- 4-layer pipeline (Listener -> Routing/Filtering -> Extraction -> Processing)
- 3 device modules: Simetra (virtual heartbeat), NPB (standard SNMP), OBP (non-standard SNMP)
- Trap channel consumers completing the full pipeline
- OpenTelemetry OTLP export with role-gated metrics/traces
- K8s Lease-based leader-follower HA
- 216 tests, 129 requirements, 0 tech debt

## Requirements

### Validated

- 4-layer pipeline: Listener -> Routing/Filtering -> Extraction -> Processing -- v1.0
- Single SNMP listener (SharpSnmpLib/Lextm) receiving traps on configurable UDP port -- v1.0
- Device filter (identify source device by IP) and trap filter (per-device OID filtering) -- v1.0
- Channel-per-device isolation (System.Threading.Channels, bounded, drop-oldest) -- v1.0
- Generic extractor using PollDefinitionDto with Role-based OID extraction -- v1.0
- PropertyName as metric name (METR-01): clean snake_case, base labels provide context -- v1.0
- EnumMap as metadata only (stored for Grafana value mappings, NOT reported to OTLP) -- v1.0
- Unified PollDefinitionDto structure for traps, state polls, and metric polls -- v1.0
- Source-based routing: Module -> metric + State Vector; Configuration -> metric only -- v1.0
- IMetricFactory enforcing base labels (site, device_name, device_ip, device_type) -- v1.0
- Device module plugin system (IDeviceModule, strategy pattern, Open/Closed Principle) -- v1.0
- Simetra virtual device with heartbeat loopback through full pipeline -- v1.0
- Leader-follower HA via Kubernetes Lease API with ILeaderElection abstraction -- v1.0
- Quartz scheduler with DisallowConcurrentExecution, poll/heartbeat/correlation jobs -- v1.0
- Correlation ID system: scheduled job generates new ID per interval -- v1.0
- K8s health probes: startup (correlationId exists), readiness (channels + scheduler), liveness (vector staleness) -- v1.0
- OpenTelemetry integration with role-gated OTLP export -- v1.0
- 11-step startup sequence and graceful shutdown with time-budgeted steps -- v1.0
- Composable middleware chain for cross-cutting concerns -- v1.0
- Trap channel consumers (channel -> middleware -> extract -> process) -- v1.0
- NPB device module: NOTIFICATION-TYPE traps, table-based port statistics, EnumMap -- v1.0
- OBP device module: OBJECT-TYPE traps, per-link duplicated OIDs, 7 EnumMaps -- v1.0
- End-to-end trap flow verified through full pipeline -- v1.0
- 216 unit + integration tests covering all core logic -- v1.0

### Active

(None -- run `/gsd:new-milestone` to define next milestone)

### Out of Scope

- State Vector staleness checking (detecting dead listener) -- future milestone
- Direct listener health check in readiness probe -- future milestone
- Real device modules beyond NPB/OBP reference implementations -- future milestone
- SNMPv3 support -- v2c only for now
- UI or API endpoints (headless service, probes only)
- Dynamic configuration (hot reload) -- static config, restart required

## Context

- Design document: `requirements and basic design.txt` (v4, comprehensive)
- Shipped v1.0 with ~8,300 LOC C# (~4,500 src + ~3,500 test), 216 tests
- Tech stack: .NET 9, SharpSnmpLib, Quartz.NET, OpenTelemetry, System.Threading.Channels
- Test stack: xUnit 2.9.3, FluentAssertions 7.2.0 (Apache 2.0), Moq 4.20.72
- Pipeline architecture inspired by ASP.NET middleware pattern
- Local development runs single instance as always-leader via ILeaderElection abstraction
- NPB (CGS Network Packet Broker) and OBP (GLSUN OTS3000 Optical Bypass) as reference device modules
- NPB MIBs at `NPB/mibs/`, OBP MIB at `V5.2.4/BYPASS-CGS.mib`
- Both devices share enterprise OID 1.3.6.1.4.1.47477 (CGS)
- Codebase map at `.planning/codebase/`

## Constraints

- **Runtime**: .NET 9, hosted in Kubernetes
- **SNMP Library**: SharpSnmpLib (Lextm) -- MIT licensed, .NET 8/9 compatible
- **Scheduler**: Quartz.NET for scheduled jobs
- **Telemetry**: OpenTelemetry with OTLP exporter
- **HA Pattern**: Kubernetes Lease API (coordination.k8s.io/v1)
- **Channels**: System.Threading.Channels for device isolation
- **Config**: Static appsettings.json only, no hot reload
- **SNMP Version**: v2c only
- **Persistence**: None -- state rebuilt from next poll cycle on restart
- **Test Assertions**: FluentAssertions pinned to 7.2.0 (Apache 2.0) -- 8.x requires commercial license

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| SharpSnmpLib (Lextm) over SnmpSharpNet | .NET 8/9 support, 4.4M downloads, actively maintained, MIT license | Good |
| Framework-first, no real device modules | Prove pipeline architecture before adding device-specific logic | Good |
| Unified PollDefinitionDto for traps + polls | Single structure simplifies extractor, enables config->code migration path | Good |
| Role field (Metric/Label) on each OID entry | Fully generic extraction -- no per-device logic in extractor | Good |
| PropertyName as metric name (METR-01) | Clean snake_case, base labels provide context, no MetricName prefix needed | Good |
| EnumMap as metadata only | Raw SNMP integers for metrics; EnumMap stored for Grafana dashboards | Good |
| ILeaderElection abstraction | AlwaysLeader for local dev, K8s Lease for production | Good |
| Source field on DTO, not in config | Prevents misconfiguration, behavior determined by definition location | Good |
| Probe-driven liveness (no watchdog) | K8s triggers checks on demand, simpler than background watchdog | Good |
| Channel-per-device with drop-oldest | Real-time data: stale data less valuable than current, prevents backpressure cascading | Good |
| Microsoft.NET.Sdk.Web for host | Combined HTTP (probes) + BackgroundService in single host | Good |
| Flat config sections at JSON root | Matches design doc Section 9, no wrapping namespace | Good |
| Telemetry registered first in DI | Disposed last, ensures ForceFlush on shutdown | Good |
| GracefulShutdownService as IHostedService | Registered last = stops first, orchestrates 5-step shutdown | Good |
| RoleGatedExporter decorator pattern | Checks IsLeader on each Export call, dynamic role switching | Good |
| Inverted TDD (implementation first) | Tests validate correctness after implementation, avoided rework from interface changes | Good |
| NPB + OBP as reference device modules | Demonstrate both standard (NOTIFICATION-TYPE, tables) and non-standard (OBJECT-TYPE traps, per-link OIDs) patterns | Good |
| Trap consumers complete pipeline in v1.0 | Full end-to-end flow needed for reference implementations to be meaningful | Good |
| OBP OBJECT-TYPE traps use single OidEntryDto | OID is both identifier and value carrier for non-standard trap pattern | Good |
| OBP EnumMaps follow MIB-authoritative values | Including na(3) for HeartStatus and PowerAlarmStatus -- matches actual device behavior | Good |

---
*Last updated: 2026-02-16 after v1.0 milestone completion*
