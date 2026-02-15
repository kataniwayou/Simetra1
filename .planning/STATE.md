# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-15)

**Core value:** The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP -- with automatic leader-follower failover ensuring no single point of failure.
**Current focus:** Phase 8: High Availability

## Current Position

Phase: 8 of 10 (High Availability) -- In progress
Plan: 1 of 2 in current phase
Status: In progress
Last activity: 2026-02-15 -- Completed 08-01-PLAN.md (K8sLeaseElection + environment-based DI)

Progress: [██████████████████░░░░░░░░░] 18/27 (67%)

## Performance Metrics

**Velocity:**
- Total plans completed: 18
- Average duration: 2.6 min
- Total execution time: 0.78 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-project-foundation-configuration | 3/3 | 14 min | 4.7 min |
| 02-domain-models-extraction-engine | 2/2 | 5 min | 2.5 min |
| 03-snmp-listener-device-routing | 3/3 | 8 min | 2.7 min |
| 04-processing-pipeline | 2/2 | 4 min | 2.2 min |
| 05-plugin-system-simetra-module | 2/2 | 3 min | 1.5 min |
| 06-scheduling-system | 3/3 | 8 min | 2.7 min |
| 07-telemetry-integration | 2/2 | 4 min | 2.0 min |
| 08-high-availability | 1/2 | 3 min | 3.0 min |

**Recent Trend:**
- Last 5 plans: 2 min, 2 min, 2 min, 2 min, 3 min
- Trend: stable (K8sLeaseElection with KubernetesClient LeaderElector integration)

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: 10-phase pipeline-first build strategy -- bottom-up through data flow layers, cross-cutting concerns layered after, end-to-end validation last
- [Roadmap]: Testing isolated to Phase 10 -- unit tests written after all implementation phases complete, avoiding rework from interface changes during build
- [01-01]: Microsoft.NET.Sdk.Web chosen over Worker SDK for combined HTTP + BackgroundService host
- [01-01]: Flat config sections at JSON root (no wrapping namespace), matching design doc Section 9
- [01-01]: FluentAssertions pinned to 7.2.0 (Apache 2.0) -- 8.x requires commercial license
- [01-02]: IValidateOptions for complex nested validation -- DataAnnotations cannot walk object graphs
- [01-02]: DevicesOptions wrapper with custom Configure delegate for top-level JSON array binding
- [01-02]: Known device types as static HashSet {router, switch, loadbalancer, simetra} -- extensible later
- [01-02]: MetricPollOptions.Source excluded from JSON ([JsonIgnore]), stamped via PostConfigure
- [01-03]: Inverted TDD for config tests -- implementation first, tests validate correctness (all 45 passed immediately)
- [01-03]: PostConfigure tested by replicating callback logic directly (no DI container in tests)
- [02-01]: ReadOnlyDictionary<K,V>.Empty for ExtractionResult defaults -- allocation-free empty collections
- [02-01]: EnumMap defensive copy via ToDictionary().AsReadOnly() -- prevents mutation of source config
- [02-01]: ExtractionResult as sealed class with init properties -- mutable construction, immutable consumption
- [02-02]: ExtractNumericValue/ExtractLabelValue as private static methods -- no instance state for type conversion
- [02-02]: OID lookup via ToDictionary for O(1) per-varbind matching
- [02-02]: Non-numeric Metric data logged at Warning and skipped -- config error, not runtime failure
- [03-01]: TrapEnvelope.CorrelationId and MatchedDefinition mutable (get;set;) -- stamped after construction
- [03-01]: DeviceRegistry uses Dictionary<IPAddress, DeviceInfo> with MapToIPv4 normalization for O(1) lookup
- [03-01]: TrapFilter builds HashSet per definition per Match call -- avoids stale cached state
- [03-01]: DeviceChannelManager captures logger via closure in itemDropped callback for bounded channel drops
- [03-03]: Middleware pipeline runs before device lookup -- correlationId/logging on all traps including unknown devices
- [03-03]: Device lookup and OID filtering are terminal logic after middleware, not middleware themselves
- [03-03]: TrapMiddlewareDelegate registered as singleton via factory lambda resolved from ServiceProvider
- [03-03]: ISnmpExtractor registered in DI but not consumed by listener -- poll jobs call directly (PIPE-06)
- [04-01]: System.Diagnostics using required for TagList -- not in System.Diagnostics.Metrics namespace
- [04-01]: StateVectorEntry as sealed class with required init (not record) -- matches ExtractionResult pattern
- [04-01]: CreateEntry static helper in StateVectorService -- avoids lambda duplication in AddOrUpdate
- [05-01]: Module devices registered after config in DeviceRegistry -- dictionary overwrite gives module precedence on IP collision
- [05-01]: DeviceChannelManager uses Concat of config + module device names -- unified channel creation without pre-sizing
- [05-02]: HeartbeatOid as public const on SimetraModule -- single source of truth for Phase 6 HeartbeatJob
- [05-02]: SimetraModule uses 127.0.0.1 loopback for self-directed heartbeat traps
- [05-02]: DI registration order: Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline
- [06-01]: RotatingCorrelationService uses volatile string -- single writer (startup then CorrelationJob), multiple readers, no locks
- [06-01]: PollDefinitionRegistry uses composite string key "deviceName::metricName" with OrdinalIgnoreCase
- [06-01]: DeviceRegistry adds OrdinalIgnoreCase name dictionary for TryGetDeviceByName
- [06-01]: SimetraModule instantiated directly in AddScheduling for compile-time module enumeration
- [06-01]: All SimpleTriggers use WithMisfireHandlingInstructionNextWithRemainingCount (DoNothing is CronTrigger-only)
- [06-01]: DI registration order extended: Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks
- [07-01]: DI registration order: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks
- [07-01]: AddSimetraTelemetry on IHostApplicationBuilder (not IServiceCollection) -- registered first = disposed last for ForceFlush
- [07-01]: RoleGatedExporter returns ExportResult.Success when follower -- prevents SDK retry backoff on non-leaders
- [07-02]: Factory overload AddProcessor(Func<IServiceProvider, BaseProcessor<LogRecord>>) for DI resolution at runtime
- [07-02]: Func<string> roleProvider delegate on processor to track runtime role changes without reconstruction
- [07-02]: ClearProviders before AddConsole ensures EnableConsole=false produces zero stdout
- [07-02]: OTLP log exporter not role-gated (TELEM-04) -- all pods export logs
- [07-02]: GetService (not GetRequiredService) for ForceFlush provider resolution -- null-safe in test scenarios
- [06-03]: Task.Run wraps synchronous Messenger.SendTrapV2 -- avoids blocking Quartz thread pool
- [06-03]: CorrelationId format is Guid.NewGuid().ToString("N") -- 32-char hex, no hyphens
- [06-03]: Correlation rotation logged at Information level -- operational visibility for ID transitions
- [08-01]: Single-instance DI pattern for K8sLeaseElection -- concrete singleton forwarded to ILeaderElection + IHostedService via GetRequiredService
- [08-01]: RunAndTryToHoldLeadershipForeverAsync -- pod remains candidate after leadership loss without restart
- [08-01]: Explicit lease delete on SIGTERM via DeleteNamespacedLeaseAsync -- near-instant failover

### Pending Todos

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-02-15
Stopped at: Completed 08-01-PLAN.md (K8sLeaseElection + environment-based DI)
Resume file: None
