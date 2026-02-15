# Roadmap: Simetra

## Overview

Simetra is built bottom-up through its four-layer SNMP pipeline, then layered with cross-cutting concerns (telemetry, HA, health), and finally validated end-to-end with the Simetra virtual device heartbeat loopback. The 10-phase roadmap follows the natural data flow: foundation and configuration first, then extraction engine, listener/routing, processing, plugin system, scheduling, telemetry, HA, lifecycle/probes, and finally integration testing. Each phase delivers a coherent, independently verifiable capability. All 95 v1 requirements map to exactly one phase with zero orphans.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Project Foundation + Configuration** - .NET 9 scaffolding, DI wiring, options classes, appsettings.json binding
- [x] **Phase 2: Domain Models + Extraction Engine** - PollDefinitionDto, Role-based OID system, generic extractor, SNMP type handling
- [x] **Phase 3: SNMP Listener + Device Routing** - UDP trap listener, device filter, trap filter, channels, middleware chain
- [x] **Phase 4: Processing Pipeline** - Metric creation, IMetricFactory, State Vector, source-based routing
- [x] **Phase 5: Plugin System + Simetra Module** - IDeviceModule interface, device registry, SimetraModule with heartbeat definition
- [x] **Phase 6: Scheduling System** - Quartz scheduler, poll jobs, heartbeat job, correlation job, liveness vector
- [x] **Phase 7: Telemetry Integration** - OpenTelemetry setup, OTLP exporters, role-gated exporter pattern, structured logging
- [x] **Phase 8: High Availability** - ILeaderElection abstraction, AlwaysLeaderElection, K8sLeaseElection, dynamic role gating
- [x] **Phase 9: Health Probes + Lifecycle** - K8s startup/readiness/liveness probes, 11-step startup sequence, graceful shutdown
- [ ] **Phase 10: End-to-End Integration + Testing** - Heartbeat loopback validation, full unit test suite, pipeline verification

## Phase Details

### Phase 1: Project Foundation + Configuration
**Goal**: The .NET 9 project compiles, runs as a Worker Service with ASP.NET minimal API, and binds all configuration sections from appsettings.json into strongly typed options classes
**Depends on**: Nothing (first phase)
**Requirements**: CONF-01, CONF-02, CONF-03, CONF-04, CONF-05, CONF-06, CONF-07, CONF-08, CONF-09, CONF-10, CONF-11, CONF-12
**Success Criteria** (what must be TRUE):
  1. Running `dotnet run` starts a Worker Service host with ASP.NET minimal API (health endpoint skeleton returns 200)
  2. All 12 configuration sections (Site, Lease, SnmpListener, HeartbeatJob, CorrelationJob, Liveness, Channels, Devices[], MetricPolls[], OTLP, Logging, Console) bind from appsettings.json into strongly typed Options classes validated at startup
  3. Devices[] array with nested MetricPolls[] deserializes correctly, and Source field is set to Configuration automatically (not exposed in config)
  4. Invalid configuration (missing required fields, bad types) causes startup failure with descriptive error messages
  5. Project directory structure matches architecture (Configuration/, Models/, Devices/, Services/, Jobs/, Health/, Middleware/, Telemetry/, Extensions/)
**Plans**: 3 plans

Plans:
- [x] 01-01-PLAN.md -- Project scaffolding, directory structure, solution setup, health endpoint skeleton
- [x] 01-02-PLAN.md -- Configuration options classes, validators, DI registration, Program.cs wiring
- [x] 01-03-PLAN.md -- Configuration validation test suite (binding, validation, edge cases)

### Phase 2: Domain Models + Extraction Engine
**Goal**: The generic extractor transforms raw SNMP varbinds into strongly typed domain objects using PollDefinitionDto definitions, with Role-based extraction producing metric values and labels without any per-device-type logic
**Depends on**: Phase 1
**Requirements**: EXTR-01, EXTR-02, EXTR-03, EXTR-04, EXTR-05, EXTR-06, EXTR-07, EXTR-08, EXTR-09
**Success Criteria** (what must be TRUE):
  1. PollDefinitionDto with MetricName, MetricType, Oids[], IntervalSeconds, and Source field correctly represents trap definitions, state polls, and metric polls using the same structure
  2. Given SNMP varbinds and a PollDefinitionDto, the extractor produces metric values from Role:Metric OIDs (raw SNMP integers) and label values from Role:Label OIDs (enum-mapped strings or raw strings)
  3. Extractor handles all required SNMP types (INTEGER, STRING, Counter32, Counter64, Gauge32, Timeticks, IpAddress) without error
  4. EnumMap values are stored as metadata on the extraction result but NOT used as metric values -- raw integers are always preserved for metrics
  5. Source field is set automatically at load time (Module vs Configuration) and cannot be overridden via appsettings.json
**Plans**: 2 plans

Plans:
- [x] 02-01-PLAN.md -- PollDefinitionDto, OidEntryDto, ExtractionResult domain models, ISnmpExtractor interface, SharpSnmpLib package
- [x] 02-02-PLAN.md -- TDD: SnmpExtractorService implementation with 15 test cases covering all SNMP types, roles, and EnumMap semantics

### Phase 3: SNMP Listener + Device Routing
**Goal**: The SNMP listener receives v2c traps on UDP, identifies the source device, filters by OID, and routes into device-specific bounded channels with backpressure monitoring -- forming Layers 1 and 2 of the pipeline
**Depends on**: Phase 2
**Requirements**: PIPE-01, PIPE-02, PIPE-03, PIPE-04, PIPE-05, PIPE-06, PIPE-07, PIPE-08
**Success Criteria** (what must be TRUE):
  1. SNMP listener binds to the configured UDP port and receives v2c trap PDUs parsed by SharpSnmpLib
  2. Traps from unknown IPs (not in registered device list) are rejected; traps with OIDs not in the device's trap definitions are rejected
  3. Accepted traps route into the correct device-specific bounded Channel with DropOldest behavior, and dropped items trigger an itemDropped callback logged at Debug level
  4. Poll responses bypass Layer 2 channels entirely, going directly to the Layer 3 extractor
  5. Every trap receives a correlationId attached at the listener before forwarding to Layer 2, and the composable middleware chain handles cross-cutting concerns (correlationId propagation, structured logging, error handling)
**Plans**: 3 plans

Plans:
- [x] 03-01-PLAN.md -- Foundational types and services (TrapEnvelope, TrapContext, DeviceRegistry, TrapFilter, DeviceChannelManager, ICorrelationService)
- [x] 03-02-PLAN.md -- Middleware chain infrastructure and implementations (ErrorHandling, CorrelationId, Logging)
- [x] 03-03-PLAN.md -- SnmpListenerService BackgroundService and DI wiring

### Phase 4: Processing Pipeline
**Goal**: Extracted data flows through two processing branches -- Branch A creates OTLP-ready metrics with enforced base labels, and Branch B updates the in-memory State Vector -- with source-based routing controlling which branches activate
**Depends on**: Phase 2
**Requirements**: PROC-01, PROC-02, PROC-03, PROC-04, PROC-05, PROC-06, PROC-07, PROC-08
**Success Criteria** (what must be TRUE):
  1. Branch A produces metrics named {MetricName}_{Property} for each Role:Metric OID, with base labels (site, device_name, device_ip, device_type) auto-attached by IMetricFactory
  2. Role:Label OID values appear as additional labels on all metrics from the same PollDefinitionDto
  3. Source=Module data flows to both Branch A (metrics) and Branch B (State Vector); Source=Configuration data flows to Branch A only
  4. State Vector stores last-known domain object with timestamp and correlationId per device/definition, in-memory only, no persistence
  5. A failure in metric creation does not block State Vector update, and vice versa -- branches are independent
**Plans**: 2 plans

Plans:
- [x] 04-01-PLAN.md -- IMetricFactory and MetricFactory (Branch A) + IStateVectorService, StateVectorEntry, and StateVectorService (Branch B)
- [x] 04-02-PLAN.md -- ProcessingCoordinator with source-based routing and branch isolation + DI registration

### Phase 5: Plugin System + Simetra Module
**Goal**: The IDeviceModule plugin system enables adding new device types without modifying existing code, proven by the Simetra virtual device module that defines a heartbeat trap flowing through the full extraction and processing pipeline
**Depends on**: Phases 2, 3, 4
**Requirements**: PLUG-01, PLUG-02, PLUG-03, PLUG-04, PLUG-05, PLUG-06
**Success Criteria** (what must be TRUE):
  1. IDeviceModule interface exposes device type, trap definitions (PollDefinitionDto), state poll definitions, and a device-specific channel
  2. SimetraModule is hardcoded in code (not in appsettings.json Devices[] array) and defines a heartbeat trap definition with Source=Module
  3. Simetra heartbeat data flows through the pipeline uniformly -- no special-case branches anywhere in listener, routing, extraction, or processing
  4. Adding a new device type requires only: a new module class, a config entry, and registration -- zero changes to existing framework code
**Plans**: 2 plans

Plans:
- [x] 05-01-PLAN.md -- IDeviceModule interface + DeviceRegistry/DeviceChannelManager refactoring to accept modules
- [x] 05-02-PLAN.md -- SimetraModule implementation, AddDeviceModules extension, Program.cs wiring

### Phase 6: Scheduling System
**Goal**: Quartz scheduler executes poll jobs, heartbeat jobs, and correlation jobs on configurable intervals, with each job stamping the liveness vector on completion and reading the current correlationId before execution
**Depends on**: Phases 3, 5
**Requirements**: SCHED-01, SCHED-02, SCHED-03, SCHED-04, SCHED-05, SCHED-06, SCHED-07, SCHED-08, SCHED-09, SCHED-10, LIFE-02
**Success Criteria** (what must be TRUE):
  1. Quartz scheduler runs with DisallowConcurrentExecution per job key and DoNothing misfire handling on all triggers -- skipped jobs produce no liveness stamp
  2. State poll jobs (Source=Module) and metric poll jobs (Source=Configuration) both use the generic extractor with PollDefinitionDto, feeding results to Layer 3/4
  3. Heartbeat job sends a loopback trap to the SNMP listener using the OID from SimetraModule's trap definition (single source of truth), and stamps the liveness vector
  4. Correlation job generates a new correlationId and stamps the liveness vector at its configured interval; first correlationId is generated directly on startup before any job fires
  5. Liveness vector has one entry per scheduled job, stamped only by job completion (not by incoming traps)
**Plans**: 3 plans

Plans:
- [x] 06-01-PLAN.md -- Quartz.AspNetCore install, RotatingCorrelationService, LivenessVectorService, PollDefinitionRegistry, DeviceRegistry extension, AddScheduling + Program.cs wiring with stub jobs
- [x] 06-02-PLAN.md -- StatePollJob and MetricPollJob full implementations (SNMP GET + extract + process)
- [x] 06-03-PLAN.md -- HeartbeatJob (loopback trap) and CorrelationJob (correlationId rotation) full implementations

### Phase 7: Telemetry Integration
**Goal**: OpenTelemetry emits .NET runtime metrics, SNMP-derived metrics, structured logs, and distributed traces to the OTLP collector, with metric and trace exporters gated by leader role while log exporters run on all pods
**Depends on**: Phase 4
**Requirements**: TELEM-01, TELEM-02, TELEM-03, TELEM-04, TELEM-05, TELEM-06, TELEM-07
**Success Criteria** (what must be TRUE):
  1. MeterProvider collects .NET runtime metrics (CPU, memory, GC, thread pool) and SNMP-derived metrics with base labels and Role:Label values attached
  2. OTLP metric and trace exporters are active only when the instance is leader; log exporter is active on all pods regardless of role
  3. Structured logs include site name, role (leader/follower), and correlationId on every log entry; console logging is toggleable via EnableConsole config
  4. EnumMap values are never reported as metric values to OTLP -- raw SNMP integers are always the metric values
  5. Telemetry providers are registered first in DI (disposed last) with ForceFlush called during shutdown to prevent final metric loss
**Plans**: 2 plans

Plans:
- [x] 07-01-PLAN.md -- OpenTelemetry packages, ILeaderElection + AlwaysLeaderElection, RoleGatedExporter, MeterProvider + TracerProvider with OTLP exporters, DI order update
- [x] 07-02-PLAN.md -- SimetraLogEnrichmentProcessor, OTLP log exporter, ClearProviders + conditional AddConsole, ForceFlush on ApplicationStopping

### Phase 8: High Availability
**Goal**: Leader-follower HA ensures exactly one instance exports metrics and traces at any time, with automatic failover via Kubernetes Lease API and near-instant handoff on graceful shutdown
**Depends on**: Phase 7
**Requirements**: HA-01, HA-02, HA-03, HA-04, HA-05, HA-06, HA-07
**Success Criteria** (what must be TRUE):
  1. ILeaderElection abstraction works with AlwaysLeaderElection (local dev, always returns true) and K8sLeaseElection (production, uses coordination.k8s.io/v1 Lease)
  2. K8s Lease election renews at configurable interval (~10s) with configurable TTL (~15s); on SIGTERM, the leader explicitly releases the lease for near-instant failover
  3. Leader activates metric + trace OTLP exporters; followers keep only log exporter -- role-gated exporter decorator checks IsLeader dynamically on each Export call
  4. All pods execute identical business logic and maintain identical internal state -- only OTLP export behavior differs by role
  5. Role changes at runtime (follower becomes leader on failover) take effect immediately on the next Export call without restart
**Plans**: 2 plans

Plans:
- [x] 08-01-PLAN.md -- K8sLeaseElection BackgroundService + ILeaderElection, KubernetesClient install, environment-based DI registration
- [x] 08-02-PLAN.md -- RoleGatedExporter wiring into OTLP metric + trace exporter chain via manual construction

### Phase 9: Health Probes + Lifecycle
**Goal**: Kubernetes health probes accurately reflect service state at each lifecycle stage, and the 11-step startup sequence plus graceful shutdown with time-budgeted steps ensure reliable pod lifecycle management
**Depends on**: Phases 6, 8
**Requirements**: HLTH-01, HLTH-02, HLTH-03, HLTH-04, HLTH-05, HLTH-06, HLTH-07, HLTH-08, HLTH-09, LIFE-01, LIFE-03, LIFE-04, LIFE-05, LIFE-06, LIFE-07
**Success Criteria** (what must be TRUE):
  1. Startup probe returns healthy only after the pipeline is wired and the first correlationId exists; readiness probe checks all device channels are open and Quartz scheduler is running
  2. Liveness probe checks liveness vector staleness -- each job's stamp age must be less than (job interval x GraceMultiplier); stale stamps return 503 with diagnostic log, healthy returns 200 silently
  3. Heartbeat send job stamps liveness vector (scheduler alive proof); heartbeat arrival updates Simetra State Vector entry (pipeline flow proof, informational only in this milestone)
  4. 11-step startup sequence executes in order via IHostedService registration, merging hardcoded (Source=Module) and configurable (Source=Configuration) poll definitions per device
  5. Graceful shutdown follows the order: release lease, stop listener, stop scheduler, drain channels, flush telemetry -- each step has bounded time budget, total ~30s, and telemetry flush is protected with its own budget regardless of prior step outcomes
**Plans**: 2 plans

Plans:
- [x] 09-01-PLAN.md -- Health probe IHealthCheck implementations (startup, readiness, liveness), IJobIntervalRegistry, DI registration, tag-filtered endpoint mapping
- [x] 09-02-PLAN.md -- Graceful shutdown with time-budgeted steps, channel drain API, 11-step startup documentation

### Phase 10: End-to-End Integration + Testing
**Goal**: The complete pipeline is proven working via heartbeat loopback (trap flows through all four layers and produces metrics + State Vector update), and comprehensive unit tests cover all core logic
**Depends on**: Phases 1-9
**Requirements**: TEST-01, TEST-02, TEST-03, TEST-04, TEST-05, TEST-06, TEST-07, TEST-08, TEST-09, TEST-10, TEST-11, TEST-12
**Success Criteria** (what must be TRUE):
  1. Heartbeat loopback completes end-to-end: HeartbeatJob sends trap to SnmpListener, routes through Simetra channel, extractor processes it, State Vector updates, metric is created, OTLP export fires (leader only), liveness stamp is recorded
  2. Unit tests pass for generic extractor covering all SNMP types, Role:Metric, Role:Label, and EnumMap behavior
  3. Unit tests pass for pipeline components: device filter, trap filter, channel backpressure (DropOldest + itemDropped), middleware chain composition and execution order
  4. Unit tests pass for processing: State Vector updates, source-based routing, IMetricFactory base label enforcement, correlation ID generation and propagation
  5. Unit tests pass for operational concerns: liveness vector stamping and staleness detection, K8s health probe handlers, graceful shutdown ordering and time budgets, role-gated exporter pattern (leader/follower switching)
**Plans**: TBD

Plans:
- [ ] 10-01: End-to-end heartbeat loopback integration test
- [ ] 10-02: Unit tests for extraction and pipeline (TEST-01 through TEST-03, TEST-07 through TEST-09)
- [ ] 10-03: Unit tests for processing and scheduling (TEST-04, TEST-05, TEST-06)
- [ ] 10-04: Unit tests for operational concerns (TEST-10, TEST-11, TEST-12)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Project Foundation + Configuration | 3/3 | Complete | 2026-02-15 |
| 2. Domain Models + Extraction Engine | 2/2 | Complete | 2026-02-15 |
| 3. SNMP Listener + Device Routing | 3/3 | Complete | 2026-02-15 |
| 4. Processing Pipeline | 2/2 | Complete | 2026-02-15 |
| 5. Plugin System + Simetra Module | 2/2 | Complete | 2026-02-15 |
| 6. Scheduling System | 3/3 | Complete | 2026-02-15 |
| 7. Telemetry Integration | 2/2 | Complete | 2026-02-15 |
| 8. High Availability | 2/2 | Complete | 2026-02-15 |
| 9. Health Probes + Lifecycle | 2/2 | Complete | 2026-02-15 |
| 10. End-to-End Integration + Testing | 0/4 | Not started | - |

---
*Roadmap created: 2026-02-15*
*Depth: comprehensive (10 phases, 28 plans)*
*Coverage: 95/95 v1 requirements mapped*
