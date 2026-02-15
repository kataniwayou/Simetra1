# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-15)

**Core value:** The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP -- with automatic leader-follower failover ensuring no single point of failure.
**Current focus:** Phase 10: End-to-End Integration + Testing

## Current Position

Phase: 10 of 10 (End-to-End Integration + Testing) -- In progress
Plan: 2 of 4 in current phase
Status: In progress
Last activity: 2026-02-15 -- Completed 10-02-PLAN.md (Processing Pipeline + Liveness Detection Tests)

Progress: [████████████████████████░░░] 23/25 (92%)

## Performance Metrics

**Velocity:**
- Total plans completed: 23
- Average duration: 2.6 min
- Total execution time: 1.0 hours

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
| 08-high-availability | 2/2 | 5 min | 2.5 min |
| 09-health-probes-lifecycle | 2/2 | 7 min | 3.5 min |
| 10-end-to-end-integration-testing | 2/4 | 8 min | 4.0 min |

**Recent Trend:**
- Last 5 plans: 2 min, 3 min, 4 min, 4 min, 4 min
- Trend: stable (test plans slightly longer due to MeterListener setup complexity)

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
- [08-02]: Manual OtlpMetricExporter/OtlpTraceExporter construction -- AddOtlpExporter() prevents RoleGatedExporter wrapping
- [08-02]: AddReader(Func<IServiceProvider, MetricReader>) factory overload for deferred ILeaderElection resolution in metrics
- [08-02]: AddProcessor(Func<IServiceProvider, BaseProcessor<Activity>>) factory overload for deferred ILeaderElection resolution in traces
- [08-02]: Log OTLP exporter intentionally NOT wrapped -- all pods export logs (TELEM-04, HA-03)
- [09-01]: JobIntervalRegistry created inline in AddScheduling -- interval values only available during Quartz config, registered as singleton instance
- [09-01]: IJobIntervalRegistry registration stays in AddScheduling (not AddSimetraHealthChecks) -- data lives where intervals are configured
- [09-01]: Liveness staleEntries uses anonymous objects cast to IReadOnlyDictionary<string, object> for HealthCheckResult.Unhealthy data
- [09-02]: GracefulShutdownService implements IHostedService (not BackgroundService) -- only needs StopAsync, no background work
- [09-02]: SnmpListenerService resolved via GetServices<IHostedService>().OfType<SnmpListenerService>() -- AddHostedService does not register concrete type directly
- [09-02]: K8sLeaseElection resolved via GetService<K8sLeaseElection>() -- registered as concrete singleton, null in local dev mode
- [09-02]: FlushTelemetryAsync uses independent CTS (not linked to outer token) -- telemetry flush gets full budget regardless of prior outcomes
- [09-02]: ForceFlush consolidated from ApplicationStopping lambda into GracefulShutdownService Step 5
- [09-02]: DI registration order finalized: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks -> Lifecycle
- [10-02]: Mock IMeterFactory to return real Meter, use MeterListener for measurement capture -- avoids needing real OTLP pipeline in tests
- [10-02]: IDisposable on MetricFactoryTests to dispose Meter and MeterListener -- prevents test pollution across test classes
- [10-02]: Real LivenessVectorService for fresh-stamps test, Mock ILivenessVectorService for stale-stamps test -- real for simple behavior, mock for time manipulation

### Pending Todos

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-02-15
Stopped at: Completed 10-02-PLAN.md (Processing Pipeline + Liveness Detection Tests)
Resume file: None
