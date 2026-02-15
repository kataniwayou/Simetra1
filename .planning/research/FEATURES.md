# Feature Research

**Domain:** Headless SNMP supervisor background service (.NET, Kubernetes)
**Researched:** 2026-02-15
**Confidence:** HIGH (domain well-established; features validated across multiple NMS/exporter tools and SNMP RFCs)

## Feature Landscape

### Table Stakes (Users Expect These)

Features that a reliable SNMP supervisor must have. Missing these means the service cannot fulfill its core purpose of supervising network devices and emitting telemetry.

#### Core Pipeline

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| SNMP trap reception (UDP listener) | Traps are the primary event-driven mechanism in SNMP; without a listener the service is blind to device-initiated events | MEDIUM | Must handle concurrent trap arrivals from multiple devices. UDP is fire-and-forget, so the listener must be always-on. Port 162 is standard. |
| SNMP polling (scheduled GET/GETBULK) | Polling is the only way to detect silent failures (device unable to send traps) and to collect metrics that are only available via polling. Industry consensus: polling + traps together, never traps alone | MEDIUM | Quartz.NET with DisallowConcurrentExecution is the right pattern. GETBULK (SNMPv2c) is preferred over individual GETs for multi-OID polls. |
| OID-to-metric extraction | Raw SNMP varbinds are opaque (OID/type/value tuples). Extraction maps them into domain-meaningful metrics. Without this the service produces unusable data | MEDIUM | Unified PollDefinitionDto pattern (as designed) is correct. Must handle INTEGER, STRING, Counter32, Gauge32, IpAddress types at minimum. Enum mapping (integer-to-label) is essential for status fields. |
| Device-level isolation | One misbehaving device (trap floods, slow polls) must not impact monitoring of other devices. This is the single most common failure mode in monolithic SNMP services | MEDIUM | Channel-per-device with bounded capacity and drop-oldest is correct. Critical that polls bypass channels (already designed). |
| Telemetry export (metrics to OTLP) | The service is a telemetry producer, not a dashboard. If it cannot emit metrics reliably, it has no output. OTLP is the modern standard | MEDIUM | Base labels (site, device_name, device_ip, device_type) on every metric are table stakes for queryability. Leader-only export is correct for HA. |
| Structured logging with context | Operators need structured logs with site, device, role, and correlation context to troubleshoot. Unstructured logs make SNMP services effectively undebuggable | LOW | OpenTelemetry logging with correlationId propagation. All pods must emit logs (not just leader). |
| Health probes (liveness, readiness, startup) | Kubernetes requires probes to manage pod lifecycle. Without them, K8s cannot restart hung pods or route traffic correctly | MEDIUM | Three-probe pattern is correct. Liveness checking the liveness vector (staleness detection) is the right approach. Startup probe gating on first correlationId is sound. |
| Graceful shutdown | SNMP services hold in-flight data and open UDP sockets. Ungraceful shutdown causes metric gaps and potential port conflicts on restart | MEDIUM | CancellationToken propagation, time-budgeted steps, lease release on SIGTERM. The 30s budget is standard for Kubernetes terminationGracePeriodSeconds. |
| Static device configuration | Operators must configure which devices to monitor, their IPs, types, and community strings. Startup-time validation prevents runtime surprises | LOW | appsettings.json with validation at startup is appropriate. Static config with restart-required is simpler and safer for a small device count (~5). |
| Device plugin system (strategy pattern) | Different device types have different OID trees, trap formats, and state semantics. A monolithic extractor becomes unmaintainable as device types grow | HIGH | IDeviceModule with Open/Closed Principle. Each module owns its trap definitions, state polls, and channel. Virtual device ("Simetra") as first plugin validates the framework. |

#### Reliability

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Leader-follower HA | A single-instance SNMP supervisor is a single point of failure. At minimum two replicas with automatic failover are expected for any production monitoring service | HIGH | K8s Lease API (coordination.k8s.io/v1) is the correct modern pattern. ~10s renewal, ~15s TTL provides fast failover. Graceful lease release on SIGTERM for near-instant failover is a strong choice. |
| Correlation ID system | Without correlation, operators cannot trace a single event through the 4-layer pipeline. Every production monitoring service needs request-level traceability | MEDIUM | Time-window based correlation (shared ID across all events in an interval) is practical for SNMP where events are bursty, not request/response. Generation at startup before first job fires is essential. |
| Liveness vector (job health tracking) | Operators need to know if scheduled jobs are still running. A stale liveness vector entry means a poll job has stopped firing, which means device monitoring is silently broken | MEDIUM | One entry per scheduled job, stamped on completion. K8s liveness probe checks staleness against expected intervals. This is better than a simple heartbeat because it detects partial failures. |
| Error isolation (fail-open per flow) | A single failed extraction or metric creation must not crash the entire pipeline. SNMP data is inherently noisy (devices return unexpected types, OID trees change between firmware versions) | LOW | Already designed: log error, continue processing. Critical that metric creation failure does not block State Vector update and vice versa. |

### Differentiators (Competitive Advantage)

Features not required for basic operation but that provide significant operational value. These distinguish a well-engineered supervisor from a basic SNMP forwarder.

#### Operational Intelligence

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| State Vector (last-known-state per device) | Enables operators to query "what is the current state of device X?" without waiting for next poll. Most SNMP exporters (e.g., Prometheus SNMP exporter) are stateless -- they only produce metrics on scrape. A state vector provides instant situational awareness | MEDIUM | Source-based routing (Module -> State Vector, Configuration -> metrics only) is a strong design. No persistence needed -- rebuilt from next poll. The key differentiator is distinguishing "state polls" from "metric polls" via the Source field. |
| Heartbeat loopback (end-to-end self-test) | Validates that the entire pipeline is flowing, not just individual components. Most monitoring services check "is the process alive?" but not "is the pipeline actually processing?" The heartbeat trap exercises Listener -> Routing -> Extraction -> Processing end-to-end | MEDIUM | Simetra virtual device sending loopback traps through the full pipeline is a strong pattern. Two stamps: liveness vector (scheduler alive) + State Vector (pipeline flowing). This is genuinely uncommon in SNMP supervisors. |
| Configurable metric polls (Source=Configuration) | Enables operators to add new metrics without code changes. Most SNMP tools require MIB compilation or code changes. Config-driven metric polls with a migration path to code (Source=Module) provide a rapid iteration workflow | LOW | PollDefinitionDto in appsettings.json with Source=Configuration set at load time. Migration path: prototype in config, promote to code for State Vector integration. |
| Composable middleware chain | Cross-cutting concerns (correlationId, logging, error handling) applied uniformly without polluting device module code. ASP.NET middleware pattern applied to SNMP pipeline is unusual and clean | HIGH | Must be designed carefully to avoid middleware ordering bugs. Clear separation of per-device vs pipeline-wide middleware. |
| Distributed tracing (OTLP traces) | Enables visualizing the full journey of a trap or poll through the pipeline. Most SNMP tools offer metrics but not traces. Tracing enables latency analysis and bottleneck identification | MEDIUM | Leader-only trace export. Useful for debugging extraction issues ("why did this trap produce wrong metrics?"). |

#### Resilience

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Trap flood protection (channel backpressure) | A misbehaving device sending thousands of traps/second must not crash the service or starve other devices. Bounded channels with drop-oldest provide protection without requiring explicit rate limiting | LOW | Already designed via bounded Channel<T> with drop-oldest. Channel-level notification on drops (not per-item) prevents log flooding. This is better than most NMS platforms that either queue unbounded or drop silently. |
| Poll bypass of channels | Polls are already device-targeted and should not compete with traps for channel capacity. Most SNMP services run polls and traps through the same queue, causing poll starvation during trap floods | LOW | Direct routing from scheduler to Layer 3 extractor, bypassing Layer 2 channels. This is a thoughtful design decision that avoids a common anti-pattern. |
| Graceful lease release on SIGTERM | Most K8s leader election implementations wait for lease TTL to expire on shutdown (~15s gap). Explicit release on SIGTERM enables near-instant failover | LOW | Depends on SIGTERM handler reliably executing before pod termination. The 30s terminationGracePeriodSeconds provides ample time. |

#### Observability

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| .NET runtime metrics (auto-instrumented) | CPU, memory, GC, thread pool metrics without code. Enables capacity planning and performance debugging of the supervisor itself, not just the devices it monitors | LOW | OpenTelemetry .NET runtime instrumentation provides process.cpu.time, process.memory.usage, dotnet.gc.*, dotnet.thread_pool.* automatically. |
| Leader/follower role awareness in all telemetry | Every log, metric, and trace tagged with current role. Enables filtering "show me only leader logs" or "did metrics stop because of role change?" | LOW | Role field in structured logging context. Must update dynamically on role transitions, not just at startup. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem valuable but create complexity disproportionate to their benefit for a focused, headless supervisor of ~5 devices per site. These belong in downstream systems (NMS platforms, dashboards, alerting tools) -- not in the supervisor.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Built-in alerting / threshold evaluation | "The service already has the data, why not alert on it?" | Alerting requires: threshold configuration UI, notification channels (email/Slack/PagerDuty), escalation policies, silencing/acknowledgment, deduplication. This is an entire product category (PagerDuty, Alertmanager, Grafana Alerts). Building it into the supervisor couples data collection with alert policy, making both harder to change. A supervisor that alerts is no longer a supervisor -- it is an NMS. | Emit metrics to OTLP. Use downstream alerting tools (Grafana Alerting, Prometheus Alertmanager, or cloud-native equivalents) that consume OTLP metrics. These tools are purpose-built for alerting with rich features the supervisor should never replicate. |
| SNMP auto-discovery (CDP/LLDP/network scan) | "Automatically find new devices instead of configuring them" | Auto-discovery requires: network scanning infrastructure, CDP/LLDP protocol support, credential management, device type classification, and security review. For ~5 devices per site, static configuration is simpler, safer, and easier to audit. Auto-discovery introduces a class of failure modes (discovered but unconfigurable devices, network scan interference, credential sprawl) that overwhelms the simplicity benefit. | Static device configuration in appsettings.json. For larger deployments (50+ devices), consider auto-discovery as a separate service that generates configuration files for supervisors. |
| MIB compilation / MIB browser | "Load MIB files to auto-generate OID definitions" | MIB compilation requires a MIB parser (complex, platform-specific), MIB file management, and produces auto-generated configurations that are hard to validate. For ~5 device types, hand-authored PollDefinitionDto definitions in device modules are clearer, testable, and version-controlled. Prometheus SNMP exporter requires a separate generator tool for MIBs -- this complexity is appropriate at their scale (thousands of device types) but wrong for ~5. | Device module authors manually define OIDs in PollDefinitionDto. Use a MIB browser tool (iReasoning, MG-SOFT) externally during development to find OIDs, then encode them in code. |
| Persistence / historical data storage | "Store historical state for trend analysis" | The supervisor is a telemetry producer, not a time-series database. Adding persistence means: database management, retention policies, query APIs, backup/restore. The OTLP collector already routes metrics to purpose-built storage (Prometheus, InfluxDB, Grafana Mimir). Duplicating storage in the supervisor creates consistency problems and operational burden. | State Vector is in-memory, rebuilt on restart. Historical data lives in the OTLP backend. If "last known state across restarts" is needed later, a lightweight state snapshot to ConfigMap or PVC is far simpler than a database. |
| Web UI / API endpoints | "Provide a dashboard for device status" | A web UI requires: frontend framework, authentication, authorization, real-time updates, responsive design. This is an entire product. The supervisor is headless by design. Adding a UI couples deployment lifecycle (UI changes require supervisor restart) and increases attack surface. | Consume OTLP metrics in Grafana dashboards. State Vector could be exposed via a minimal read-only API endpoint if needed, but not a UI. |
| Dynamic configuration (hot reload) | "Change device config without restart" | Hot reload introduces: configuration versioning, partial-apply semantics (what if some devices reload and others fail?), concurrent access to config during reload, and testing complexity. For ~5 devices, a pod restart (rolling update in K8s) takes seconds and is safer, more predictable, and easier to reason about. Hot reload is a feature for NMS platforms managing thousands of devices. | Static appsettings.json with restart required. K8s rolling updates provide zero-downtime config changes when running 2+ replicas with leader-follower HA. |
| SNMPv3 support | "V3 is more secure with authentication and encryption" | SNMPv3 adds: USM user management, engine ID discovery, authentication (MD5/SHA), privacy (DES/AES), context name handling. This triples the SNMP stack complexity. If the network already uses v2c (which this project specifies), adding v3 is premature. v3 should be a separate milestone when a real security requirement drives it. | Stick with v2c for this milestone. Network-level security (VLANs, firewalls) provides adequate protection for SNMP community strings in controlled environments. Plan v3 as a future milestone with dedicated research. |
| Trap deduplication / correlation engine | "Suppress duplicate traps, correlate related events" | Full trap deduplication requires: stateful tracking of recent traps, configurable comparison parameters, time-window correlation, and suppression policies. NNMi (HP/Micro Focus) dedicates an entire subsystem to this. For ~5 devices with known trap definitions, device modules can handle deduplication logic in their extraction code if needed. Building a generic correlation engine is scope creep toward NMS territory. | Device modules handle trap-specific deduplication if needed (e.g., ignoring repeated link-down traps within a window). Generic correlation belongs in downstream event processing (ServiceNow, PagerDuty, Grafana OnCall). |
| SNMP SET operations (device configuration) | "The service can already talk to devices, so let it configure them too" | SNMP SET changes device state and introduces write-path concerns: authorization, audit logging, rollback on failure, change management. A supervisor that modifies devices is a configuration management tool, not a monitoring tool. Mixing read and write operations creates dangerous failure modes (monitoring service accidentally reconfigures a device). | The supervisor is read-only. Device configuration belongs in dedicated tools (Ansible, Terraform, vendor CLIs). |

## Feature Dependencies

```
[SNMP Trap Reception]
    +-- requires --> [UDP Listener Infrastructure]
    +-- requires --> [Device Configuration (IP/Type mapping)]
    +-- feeds -----> [Device Filter + Trap Filter (Layer 2)]
                        +-- feeds --> [Channel-per-Device Isolation]
                                        +-- feeds --> [OID-to-Metric Extraction (Layer 3)]
                                                        +-- feeds --> [Metric Export to OTLP (Layer 4, Branch A)]
                                                        +-- feeds --> [State Vector Update (Layer 4, Branch B)]

[SNMP Polling]
    +-- requires --> [Quartz Scheduler]
    +-- requires --> [Device Configuration]
    +-- requires --> [PollDefinitionDto System]
    +-- feeds -----> [OID-to-Metric Extraction (Layer 3)] (bypasses Layer 2)
                        +-- feeds --> [Metric Export to OTLP]
                        +-- feeds --> [State Vector Update] (Source=Module only)

[Leader-Follower HA]
    +-- requires --> [K8s Lease API]
    +-- gates -----> [Metric Export to OTLP] (leader only)
    +-- gates -----> [Trace Export to OTLP] (leader only)
    +-- independent of --> [Log Export] (all pods)
    +-- independent of --> [Trap Reception] (all pods receive)

[Health Probes]
    +-- requires --> [Liveness Vector]
    +-- requires --> [Quartz Scheduler running]
    +-- requires --> [Channels open]
    +-- requires --> [CorrelationId exists (startup)]

[Heartbeat Loopback]
    +-- requires --> [SNMP Trap Reception]
    +-- requires --> [Simetra Virtual Device Module]
    +-- requires --> [Full Pipeline (Layers 1-4)]
    +-- validates -> [End-to-end pipeline flow]

[Device Plugin System]
    +-- requires --> [PollDefinitionDto]
    +-- requires --> [IDeviceModule interface]
    +-- enables ---> [Adding new device types without code changes to framework]

[Correlation ID System]
    +-- requires --> [Correlation Job (Quartz)]
    +-- consumed by --> [All pipeline layers]
    +-- consumed by --> [Structured Logging]
    +-- consumed by --> [Distributed Tracing]
```

### Dependency Notes

- **Pipeline layers are strictly ordered:** Layer 1 (Listener) must be functional before Layer 2 (Routing/Filtering), which must be functional before Layer 3 (Extraction), which feeds Layer 4 (Processing). This is the critical path.
- **HA is independent of pipeline:** Leader election can be implemented before or after the pipeline, but metric/trace export gating requires both to exist.
- **Health probes depend on everything:** Probes are the last feature to implement because they validate all other components (liveness vector requires scheduler, readiness requires channels, startup requires correlationId).
- **Heartbeat requires full pipeline:** The heartbeat loopback is the integration test for the framework. It can only work after all four pipeline layers are operational with the Simetra virtual device module.
- **Correlation system is cross-cutting:** Must be wired early (startup sequence step) because all subsequent features consume correlationId.

## MVP Definition

### Launch With (v1 -- First Milestone)

Minimum viable supervisor: framework proven with virtual device only, no real device modules.

- [x] SNMP trap reception (UDP listener) -- core input mechanism
- [x] Device filter and trap filter (Layer 2) -- route traps to correct device module
- [x] Channel-per-device isolation -- prevent cross-device interference
- [x] OID-to-metric extraction (Layer 3) -- transform raw SNMP data
- [x] Metric export to OTLP (Layer 4, Branch A) -- core output mechanism
- [x] State Vector update (Layer 4, Branch B) -- differentiating feature, prove it works
- [x] Quartz scheduler with poll jobs -- scheduled data collection
- [x] PollDefinitionDto unified structure -- foundation for all poll types
- [x] Simetra virtual device module -- proves plugin system + enables end-to-end testing
- [x] Heartbeat loopback -- end-to-end pipeline validation
- [x] Leader-follower HA via K8s Lease -- production reliability
- [x] Correlation ID system -- operational traceability
- [x] Liveness vector + K8s health probes -- Kubernetes integration
- [x] Structured logging with OTLP export -- operational visibility
- [x] Static configuration with startup validation -- safe, predictable operation
- [x] Graceful startup (11-step sequence) and shutdown -- clean lifecycle management

### Add After Validation (v1.x -- Subsequent Milestones)

Features to add once core framework is proven with the virtual device.

- [ ] First real device module (router or switch) -- trigger: framework validated, real device available for testing
- [ ] State Vector staleness checking -- trigger: real devices where silent failures matter
- [ ] Listener health check in readiness probe -- trigger: need to detect dead listener
- [ ] Additional metric poll types per device -- trigger: operators request specific metrics
- [ ] Configurable metric polls in appsettings.json (Source=Configuration) -- trigger: operators want ad-hoc metrics without code
- [ ] Distributed tracing (OTLP traces) -- trigger: debugging pipeline issues in production

### Future Consideration (v2+)

Features to defer until the supervisor is running in production with real devices.

- [ ] SNMPv3 support -- defer until security audit requires it; adds significant complexity
- [ ] Multiple community strings per device -- defer until heterogeneous SNMP environments encountered
- [ ] Dynamic configuration (hot reload) -- defer until device count exceeds restart tolerance (~20+ devices)
- [ ] State Vector persistence (crash recovery) -- defer until restart-rebuild latency is unacceptable
- [ ] Minimal read-only API for State Vector -- defer until Grafana dashboards are insufficient
- [ ] Multi-site awareness (cross-site telemetry) -- defer until multi-site deployment exists

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| SNMP trap reception | HIGH | MEDIUM | P1 |
| SNMP polling (scheduled) | HIGH | MEDIUM | P1 |
| OID-to-metric extraction | HIGH | MEDIUM | P1 |
| Device-level isolation (channels) | HIGH | MEDIUM | P1 |
| Metric export to OTLP | HIGH | MEDIUM | P1 |
| Leader-follower HA | HIGH | HIGH | P1 |
| Device plugin system (IDeviceModule) | HIGH | HIGH | P1 |
| Simetra virtual device | HIGH | LOW | P1 |
| Health probes (K8s) | HIGH | MEDIUM | P1 |
| Correlation ID system | MEDIUM | MEDIUM | P1 |
| Structured logging | HIGH | LOW | P1 |
| State Vector | MEDIUM | MEDIUM | P1 |
| Heartbeat loopback | MEDIUM | LOW | P1 |
| Liveness vector | MEDIUM | MEDIUM | P1 |
| Graceful shutdown | HIGH | MEDIUM | P1 |
| Static configuration + validation | HIGH | LOW | P1 |
| Composable middleware chain | MEDIUM | HIGH | P1 |
| PollDefinitionDto unified structure | HIGH | MEDIUM | P1 |
| .NET runtime metrics | MEDIUM | LOW | P2 |
| Distributed tracing | MEDIUM | MEDIUM | P2 |
| Real device modules | HIGH | MEDIUM | P2 |
| State Vector staleness checking | MEDIUM | MEDIUM | P2 |
| Configurable metric polls (config-driven) | MEDIUM | LOW | P2 |
| SNMPv3 support | LOW | HIGH | P3 |
| Dynamic configuration | LOW | HIGH | P3 |
| State Vector persistence | LOW | MEDIUM | P3 |
| Read-only State Vector API | LOW | LOW | P3 |

**Priority key:**
- P1: Must have for first milestone launch (framework + virtual device)
- P2: Should have, add when framework is proven with real devices
- P3: Nice to have, future consideration driven by production needs

## Competitor Feature Analysis

| Feature | Prometheus SNMP Exporter | OpenTelemetry SNMP Receiver | Zabbix | Simetra (Our Approach) |
|---------|--------------------------|----------------------------|--------|------------------------|
| Trap handling | No (polling only) | No (polling only) | Yes (built-in trap receiver) | Yes (primary input path) |
| Polling | Yes (on-demand scrape) | Yes (interval-based) | Yes (interval-based) | Yes (Quartz-scheduled) |
| Device state tracking | No (stateless) | No (stateless) | Yes (trigger-based) | Yes (State Vector, in-memory) |
| Plugin/module system | MIB-based YAML modules | Config-based metrics | Templates + plugins | Code-based IDeviceModule |
| HA support | N/A (proxy model) | N/A (collector model) | Active/passive DB | K8s Lease leader-follower |
| Telemetry export | Prometheus metrics | OTLP (metrics) | Proprietary + export | OTLP (metrics, traces, logs) |
| Auto-discovery | No | No | Yes (network scan) | No (static config, deliberate) |
| Alerting | No (Alertmanager) | No (downstream) | Yes (built-in) | No (downstream, deliberate) |
| MIB compilation | Yes (generator tool) | No (manual OIDs) | Yes (built-in) | No (manual, deliberate for ~5 types) |
| Self-health monitoring | Basic | Basic | Comprehensive | Heartbeat loopback (end-to-end) |
| Scope | Metrics proxy | Metrics collector | Full NMS platform | Focused supervisor |

**Key positioning:** Simetra sits between the lightweight exporters (Prometheus, OTel) and full NMS platforms (Zabbix). Its differentiators are: trap handling + polling combined, State Vector for instant state queries, heartbeat loopback for end-to-end validation, and K8s-native HA. Its discipline is in what it deliberately excludes: no alerting, no UI, no discovery, no persistence.

## Sources

- [Prometheus SNMP Exporter (GitHub)](https://github.com/prometheus/snmp_exporter) -- architecture, module system, MIB generator [HIGH confidence]
- [OpenTelemetry SNMP Receiver (GitHub)](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/receiver/snmpreceiver) -- alpha-status receiver, config-based OID polling [HIGH confidence]
- [LogicMonitor: How SNMP Monitoring Works](https://www.logicmonitor.com/blog/how-snmp-monitoring-works) -- industry overview of trap + polling combination [MEDIUM confidence]
- [Exabeam: Network Monitoring with SNMP Guide](https://www.exabeam.com/explainers/network-security/network-monitoring-with-snmp-complete-guide/) -- polling interval best practices, tiered strategies [MEDIUM confidence]
- [Obkio: The Power of SNMP Polling](https://obkio.com/blog/snmp-polling/) -- polling interval optimization, impact analysis [MEDIUM confidence]
- [WhatsUp Gold: SNMP Trap Receiver Best Practices](https://www.whatsupgold.com/snmp/snmp-trap-receiver) -- trap receiver architecture [MEDIUM confidence]
- [Micro Focus NNMi: Trap Deduplication](https://docs.microfocus.com/NNMi/10.30/Content/Administer/nmAdminHelp/nmAdmConfInci1800DeDupSNMP.htm) -- deduplication/enrichment/correlation features [HIGH confidence]
- [Kubernetes Leases Documentation](https://kubernetes.io/docs/concepts/architecture/leases/) -- official lease API for leader election [HIGH confidence]
- [TechTarget: Telemetry vs SNMP](https://www.techtarget.com/searchnetworking/answer/Telemetry-vs-SNMP-Is-one-better-for-network-management) -- alerting approach comparison [MEDIUM confidence]
- [SigNoz: Zabbix Alternatives 2026](https://signoz.io/comparisons/zabbix-alternatives/) -- NMS platform comparison [MEDIUM confidence]
- [OneUptime: OTel SNMP Receiver Configuration (Feb 2026)](https://oneuptime.com/blog/post/2026-02-06-snmp-receiver-opentelemetry-collector/view) -- current OTel SNMP receiver status [MEDIUM confidence]
- [Gigamon: SNMP Throttling](https://docs.gigamon.com/doclib511/Content/GV-Admin/SNMP_Throttling.html) -- trap storm protection [MEDIUM confidence]

---
*Feature research for: Headless SNMP supervisor background service*
*Researched: 2026-02-15*
