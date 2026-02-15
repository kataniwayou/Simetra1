# Requirements: Simetra

**Defined:** 2026-02-15
**Core Value:** The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP — with automatic leader-follower failover ensuring no single point of failure.

## v1 Requirements

Requirements for initial release. Framework + Simetra virtual device only, no real device modules. Local development runs single instance as always-leader.

### Pipeline

- [ ] **PIPE-01**: Single SNMP listener (SharpSnmpLib) receives v2c traps on configurable UDP port
- [ ] **PIPE-02**: Device filter identifies source device by IP from registered device list
- [ ] **PIPE-03**: Trap filter keeps only OIDs defined in the device's trap definitions, rejects unmatched
- [ ] **PIPE-04**: Filtered traps route into device-specific bounded Channel<T> (one channel per device module)
- [ ] **PIPE-05**: Channels use BoundedChannelFullMode.DropOldest with itemDropped callback logging at Debug level
- [ ] **PIPE-06**: Poll responses bypass Layer 2 and channels, going directly to Layer 3 extractor
- [ ] **PIPE-07**: Composable middleware chain for cross-cutting concerns (correlationId propagation, structured logging, error handling)
- [ ] **PIPE-08**: Traps attach correlationId on arrival at listener before forwarding to Layer 2

### Extraction

- [ ] **EXTR-01**: Unified PollDefinitionDto structure used for trap definitions, state polls, and metric polls
- [ ] **EXTR-02**: PollDefinitionDto contains: MetricName (prefix), MetricType (Gauge/Counter), Oids[], IntervalSeconds, Source
- [ ] **EXTR-03**: Each OID entry has: OID, PropertyName, Role (Metric/Label), optional EnumMap
- [ ] **EXTR-04**: Role:Metric produces metric value from raw SNMP integer; EnumMap stored as metadata only (Grafana value mappings)
- [ ] **EXTR-05**: Role:Label produces label on all metrics from this DTO; value is enum-mapped string or raw string
- [ ] **EXTR-06**: Generic extractor reads Oids from PollDefinitionDto — same logic for traps and polls, no per-device-type logic
- [ ] **EXTR-07**: Extractor handles SNMP types: INTEGER, STRING, Counter32, Counter64, Gauge32, Timeticks, IpAddress
- [ ] **EXTR-08**: Extractor produces strongly typed domain objects per device type (e.g. HeartbeatData)
- [ ] **EXTR-09**: Source field set automatically at load time: code → Module, config → Configuration (not exposed in appsettings.json)

### Processing

- [ ] **PROC-01**: Branch A creates metrics from each Role:Metric OID — metric name: {MetricName}_{Property}
- [ ] **PROC-02**: Branch A sends metrics to OTLP (leader only, gated by role)
- [ ] **PROC-03**: IMetricFactory auto-attaches base labels (site, device_name, device_ip, device_type) to every metric
- [ ] **PROC-04**: Role:Label OID values become additional labels on all metrics from the same PollDefinitionDto
- [ ] **PROC-05**: Branch B updates State Vector with domain object + timestamp + correlationId (Source=Module only)
- [ ] **PROC-06**: Source-based routing: Module → Branch A + Branch B; Configuration → Branch A only
- [ ] **PROC-07**: State Vector is in-memory, no persistence, no TTL — last state only, rebuilt on restart
- [ ] **PROC-08**: One metric creation failure does not block State Vector update and vice versa

### Scheduling

- [ ] **SCHED-01**: Quartz scheduler with DisallowConcurrentExecution per job key
- [ ] **SCHED-02**: SNMP state poll jobs (per device, Source=Module) poll device and feed response to Layer 3
- [ ] **SCHED-03**: SNMP metric poll jobs (per device, Source=Configuration) poll device and feed response to Layer 3
- [ ] **SCHED-04**: All poll jobs use same PollDefinitionDto and same generic extractor
- [ ] **SCHED-05**: Heartbeat job sends loopback trap to SNMP listener at configurable interval
- [ ] **SCHED-06**: Heartbeat OID is read from Simetra module's trap definition (single source of truth)
- [ ] **SCHED-07**: Correlation job generates new correlationId + stamps liveness vector at configurable interval
- [ ] **SCHED-08**: All jobs read correlationId before execution and stamp liveness vector on completion
- [ ] **SCHED-09**: Skipped job (due to DisallowConcurrentExecution) produces no new stamp → detected by liveness probe
- [ ] **SCHED-10**: Quartz misfire handling uses DoNothing (skip stale, wait for next trigger)

### High Availability

- [ ] **HA-01**: ILeaderElection abstraction with AlwaysLeaderElection (local dev) and K8sLeaseElection (production)
- [ ] **HA-02**: K8s Lease election uses coordination.k8s.io/v1 with configurable renew interval (~10s) and TTL (~15s)
- [ ] **HA-03**: Leader activates metric + trace OTLP exporters; followers keep only log exporter active
- [ ] **HA-04**: Role-gated exporter pattern (decorator wrapping BaseExporter, checking IsLeader on each Export call)
- [ ] **HA-05**: On SIGTERM, leader explicitly releases lease for near-instant failover
- [ ] **HA-06**: All pods execute same business logic and maintain identical internal state
- [ ] **HA-07**: Role can change at runtime (follower → leader on failover) — exporter gating is dynamic

### Health Monitoring

- [ ] **HLTH-01**: Liveness vector with one entry per scheduled job, stamped at end of every job completion
- [ ] **HLTH-02**: Liveness vector NOT stamped by incoming traps (only scheduled jobs)
- [ ] **HLTH-03**: K8s startup probe returns healthy once pipeline is wired and first correlationId exists
- [ ] **HLTH-04**: K8s readiness probe checks all device channels open + Quartz scheduler running
- [ ] **HLTH-05**: K8s liveness probe HTTP handler checks liveness vector — stale stamps → 503 with diagnostic log
- [ ] **HLTH-06**: Staleness threshold: tenant stamp age < (tenant's interval × GraceMultiplier)
- [ ] **HLTH-07**: Healthy liveness check returns 200 silently (no log)
- [ ] **HLTH-08**: Heartbeat send job stamps liveness vector (proves scheduler alive)
- [ ] **HLTH-09**: Heartbeat arrival updates Simetra tenant in State Vector (informational in this milestone)

### Telemetry

- [ ] **TELEM-01**: OpenTelemetry MeterProvider for .NET runtime metrics (CPU, memory, GC, thread pool) — leader only
- [ ] **TELEM-02**: SNMP-derived metrics exported to OTLP with base labels + Role:Label values
- [ ] **TELEM-03**: Structured logging via OTLP log exporter — all logs include site name, role, correlationId
- [ ] **TELEM-04**: Log exporter active on all pods (leader and followers)
- [ ] **TELEM-05**: Distributed tracing via OTLP trace exporter — leader only
- [ ] **TELEM-06**: Console logging configurable via EnableConsole flag (sends logs to stdout)
- [ ] **TELEM-07**: EnumMap values NOT reported to OTLP — raw SNMP integers are always metric values

### Configuration

- [ ] **CONF-01**: Static appsettings.json with restart required for changes
- [ ] **CONF-02**: Site config: Name, PodIdentity (defaults to HOSTNAME env var)
- [ ] **CONF-03**: Lease config: Name, Namespace, RenewIntervalSeconds, DurationSeconds
- [ ] **CONF-04**: SnmpListener config: BindAddress, Port, CommunityString, Version (v2c only)
- [ ] **CONF-05**: HeartbeatJob config: IntervalSeconds
- [ ] **CONF-06**: CorrelationJob config: IntervalSeconds
- [ ] **CONF-07**: Liveness config: GraceMultiplier
- [ ] **CONF-08**: Channels config: BoundedCapacity
- [ ] **CONF-09**: Devices[] array: Name, IpAddress, DeviceType, optional MetricPolls[]
- [ ] **CONF-10**: MetricPolls[] per device: MetricName, MetricType, Oids[], IntervalSeconds (Source set by system)
- [ ] **CONF-11**: OTLP config: Endpoint, ServiceName
- [ ] **CONF-12**: Logging config: LogLevel:Default, EnableConsole

### Plugin System

- [ ] **PLUG-01**: IDeviceModule interface encapsulating all device-specific behavior
- [ ] **PLUG-02**: Each module contains: device type, trap definitions (PollDefinitionDto), state polls, channel
- [ ] **PLUG-03**: Simetra virtual device module with heartbeat trap definition (Source=Module)
- [ ] **PLUG-04**: Simetra module hardcoded in code, not in appsettings.json Devices[] array
- [ ] **PLUG-05**: Simetra module flows through full pipeline uniformly — no special-case branches
- [ ] **PLUG-06**: Adding new device type requires: new module class + config entry + registration — no existing code changes

### Lifecycle

- [ ] **LIFE-01**: 11-step startup sequence executed in order via IHostedService registration
- [ ] **LIFE-02**: First correlationId generated directly on startup before any job fires (not via scheduler)
- [ ] **LIFE-03**: Startup merges hardcoded (Source=Module) + configurable (Source=Configuration) poll definitions per device
- [ ] **LIFE-04**: Graceful shutdown with CancellationToken and time-budgeted steps totaling ~30s
- [ ] **LIFE-05**: Shutdown order: release lease → stop listener → stop scheduler → drain channels → flush telemetry
- [ ] **LIFE-06**: Each shutdown step has bounded time budget; if exceeded, abandon and move to next step
- [ ] **LIFE-07**: Telemetry flush is protected — gets its own budget regardless of prior step outcomes

### Testing

- [ ] **TEST-01**: Unit tests for generic extractor (all SNMP types, Role:Metric, Role:Label, EnumMap)
- [ ] **TEST-02**: Unit tests for PollDefinitionDto validation and Source field assignment
- [ ] **TEST-03**: Unit tests for device filter and trap filter logic
- [ ] **TEST-04**: Unit tests for State Vector updates and Source-based routing
- [ ] **TEST-05**: Unit tests for IMetricFactory base label enforcement
- [ ] **TEST-06**: Unit tests for liveness vector stamping and staleness detection
- [ ] **TEST-07**: Unit tests for correlation ID generation and propagation
- [ ] **TEST-08**: Unit tests for channel backpressure (drop-oldest behavior, itemDropped callback)
- [ ] **TEST-09**: Unit tests for middleware chain composition and execution order
- [ ] **TEST-10**: Unit tests for K8s health probe HTTP handlers (startup, readiness, liveness)
- [ ] **TEST-11**: Unit tests for graceful shutdown ordering and time budget enforcement
- [ ] **TEST-12**: Unit tests for role-gated exporter pattern (leader/follower switching)

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Device Modules

- **DEVMOD-01**: First real device module (router with MIB-II standard OIDs)
- **DEVMOD-02**: Second device module (switch type)
- **DEVMOD-03**: Device-specific domain objects (PortStatusData, InterfaceCounterData, etc.)

### Advanced Monitoring

- **ADVMON-01**: State Vector staleness checking to detect dead listener
- **ADVMON-02**: Direct listener health check in readiness probe
- **ADVMON-03**: Poll staggering to avoid burst polling of all devices simultaneously

### Security

- **SEC-01**: SNMPv3 support with USM authentication and privacy
- **SEC-02**: Multiple community strings per device

### Operations

- **OPS-01**: Dynamic configuration hot reload without restart
- **OPS-02**: State Vector persistence for crash recovery
- **OPS-03**: Minimal read-only API endpoint for State Vector queries

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Built-in alerting / threshold evaluation | Alerting is a separate product category; use downstream tools (Grafana Alerting, Alertmanager) |
| SNMP auto-discovery (CDP/LLDP) | Unnecessary for ~5 devices; static config is simpler and safer |
| MIB compilation / MIB browser | Manual PollDefinitionDto definitions are clearer and testable for ~5 device types |
| Historical data persistence | Supervisor is a telemetry producer, not a time-series DB; OTLP backend handles storage |
| Web UI / dashboard | Headless by design; use Grafana for visualization |
| SNMP SET operations | Supervisor is read-only; device configuration belongs in Ansible/Terraform |
| Trap deduplication engine | Generic correlation belongs in downstream event processing; device modules handle specific cases |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| (populated during roadmap creation) | | |

**Coverage:**
- v1 requirements: 88 total
- Mapped to phases: 0
- Unmapped: 88

---
*Requirements defined: 2026-02-15*
*Last updated: 2026-02-15 after initial definition*
