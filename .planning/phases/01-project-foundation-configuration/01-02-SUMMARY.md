---
phase: 01-project-foundation-configuration
plan: 02
subsystem: configuration
tags: [options-pattern, data-annotations, ivalidateoptions, di, fail-fast, nested-binding]

# Dependency graph
requires:
  - phase: 01-project-foundation-configuration (plan 01)
    provides: Solution structure, Simetra.csproj, appsettings.json with 12 config sections, Configuration namespace
provides:
  - 15 strongly typed Options classes for all 12 configuration sections
  - 3 enums (MetricType, OidRole, MetricPollSource)
  - 5 IValidateOptions validators with descriptive indexed error messages
  - AddSimetraConfiguration DI extension with ValidateOnStart fail-fast
  - PostConfigure for PodIdentity default and MetricPollSource stamping
affects:
  - 02-channel-infrastructure (ChannelsOptions)
  - 03-snmp-listener (SnmpListenerOptions, DevicesOptions)
  - 04-snmp-poller (DevicesOptions, MetricPollOptions, OidEntryOptions)
  - 05-trap-extraction (DevicesOptions)
  - 06-metric-emission (OtlpOptions, MetricType)
  - 07-leader-election (LeaseOptions, SiteOptions)
  - 08-lifecycle-jobs (HeartbeatJobOptions, CorrelationJobOptions, LivenessOptions)
  - 09-health-observability (LoggingOptions, OtlpOptions)
  - 10-integration-tests (all Options classes for test configuration)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Options pattern with IOptions<T> and ValidateOnStart for fail-fast"
    - "IValidateOptions<T> for complex/nested validation (DataAnnotations insufficient for object graphs)"
    - "PostConfigure<T> for computed defaults (PodIdentity, MetricPollSource)"
    - "Custom Configure delegate for binding JSON arrays to wrapper classes"
    - "SectionName const string convention on all Options classes"

key-files:
  created:
    - src/Simetra/Configuration/SiteOptions.cs
    - src/Simetra/Configuration/LeaseOptions.cs
    - src/Simetra/Configuration/SnmpListenerOptions.cs
    - src/Simetra/Configuration/HeartbeatJobOptions.cs
    - src/Simetra/Configuration/CorrelationJobOptions.cs
    - src/Simetra/Configuration/LivenessOptions.cs
    - src/Simetra/Configuration/ChannelsOptions.cs
    - src/Simetra/Configuration/OtlpOptions.cs
    - src/Simetra/Configuration/LoggingOptions.cs
    - src/Simetra/Configuration/DevicesOptions.cs
    - src/Simetra/Configuration/DeviceOptions.cs
    - src/Simetra/Configuration/MetricPollOptions.cs
    - src/Simetra/Configuration/OidEntryOptions.cs
    - src/Simetra/Configuration/MetricType.cs
    - src/Simetra/Configuration/OidRole.cs
    - src/Simetra/Configuration/MetricPollSource.cs
    - src/Simetra/Configuration/Validators/SiteOptionsValidator.cs
    - src/Simetra/Configuration/Validators/LeaseOptionsValidator.cs
    - src/Simetra/Configuration/Validators/SnmpListenerOptionsValidator.cs
    - src/Simetra/Configuration/Validators/DevicesOptionsValidator.cs
    - src/Simetra/Configuration/Validators/OtlpOptionsValidator.cs
    - src/Simetra/Extensions/ServiceCollectionExtensions.cs
  modified:
    - src/Simetra/Program.cs

key-decisions:
  - "IValidateOptions for complex validation -- DataAnnotations cannot walk nested object graphs"
  - "DevicesOptions wrapper with custom Configure delegate -- top-level JSON array cannot bind directly to IOptions"
  - "Known device types in static HashSet (router/switch/loadbalancer/simetra) -- extensible in future phases"
  - "MetricPollOptions.Source excluded from JSON via [JsonIgnore] -- set programmatically via PostConfigure"

patterns-established:
  - "Options class pattern: sealed class, const SectionName, DataAnnotations on simple props"
  - "Validator pattern: IValidateOptions<T> with List<string> failures and descriptive section-path errors"
  - "DI registration pattern: AddSimetra* extension method on IServiceCollection"
  - "Nested validation pattern: indexed error messages like Devices[0].MetricPolls[1].Oids[2].Oid"

# Metrics
duration: 5min
completed: 2026-02-15
---

# Phase 1 Plan 2: Configuration Options Summary

**15 strongly typed Options classes with IValidateOptions validators, fail-fast ValidateOnStart, and PostConfigure defaults for PodIdentity and MetricPollSource**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-15T05:53:30Z
- **Completed:** 2026-02-15T05:58:59Z
- **Tasks:** 2
- **Files modified:** 23 (22 created, 1 modified, 1 deleted)

## Accomplishments

- All 12 configuration sections bound to strongly typed Options classes with DataAnnotations
- Deep nested validation for Devices[] -> MetricPolls[] -> Oids[] via IValidateOptions with indexed error paths
- Fail-fast startup validation: invalid config crashes immediately with descriptive error messages
- PodIdentity defaults to HOSTNAME env var; MetricPollSource stamped as Configuration on all loaded polls
- Deleted Configuration/Placeholder.cs (temporary marker from Plan 01)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create all Options classes and enums** - `8fc56f0` (feat)
2. **Task 2: Create validators, DI registration, and wire Program.cs** - `31e1aaf` (feat)

## Files Created/Modified

- `src/Simetra/Configuration/SiteOptions.cs` - Site identification (Name required, PodIdentity optional)
- `src/Simetra/Configuration/LeaseOptions.cs` - Leader election lease config
- `src/Simetra/Configuration/SnmpListenerOptions.cs` - SNMP trap listener bind/port/community
- `src/Simetra/Configuration/HeartbeatJobOptions.cs` - Heartbeat timing
- `src/Simetra/Configuration/CorrelationJobOptions.cs` - Correlation sweep timing
- `src/Simetra/Configuration/LivenessOptions.cs` - Device liveness grace multiplier
- `src/Simetra/Configuration/ChannelsOptions.cs` - Channel bounded capacity
- `src/Simetra/Configuration/OtlpOptions.cs` - OTLP endpoint and service name
- `src/Simetra/Configuration/LoggingOptions.cs` - Simetra-specific EnableConsole flag
- `src/Simetra/Configuration/DevicesOptions.cs` - Wrapper for Devices[] array binding
- `src/Simetra/Configuration/DeviceOptions.cs` - Single device: Name, IpAddress, DeviceType, MetricPolls
- `src/Simetra/Configuration/MetricPollOptions.cs` - Metric poll: MetricName, MetricType, Oids, IntervalSeconds, Source
- `src/Simetra/Configuration/OidEntryOptions.cs` - OID entry: Oid, PropertyName, Role, EnumMap
- `src/Simetra/Configuration/MetricType.cs` - Enum: Gauge, Counter (JsonStringEnumConverter)
- `src/Simetra/Configuration/OidRole.cs` - Enum: Metric, Label (JsonStringEnumConverter)
- `src/Simetra/Configuration/MetricPollSource.cs` - Enum: Configuration, Module (not serialized)
- `src/Simetra/Configuration/Validators/SiteOptionsValidator.cs` - Site:Name required
- `src/Simetra/Configuration/Validators/LeaseOptionsValidator.cs` - Lease duration > renew interval
- `src/Simetra/Configuration/Validators/SnmpListenerOptionsValidator.cs` - v2c only, port range
- `src/Simetra/Configuration/Validators/DevicesOptionsValidator.cs` - Deep graph walk with indexed errors
- `src/Simetra/Configuration/Validators/OtlpOptionsValidator.cs` - Endpoint and ServiceName required
- `src/Simetra/Extensions/ServiceCollectionExtensions.cs` - AddSimetraConfiguration DI extension
- `src/Simetra/Program.cs` - Wired AddSimetraConfiguration call
- `src/Simetra/Configuration/Placeholder.cs` - DELETED (replaced by real Options classes)

## Decisions Made

- **IValidateOptions for complex validation:** DataAnnotations do not recurse into nested objects. Used IValidateOptions<T> for SiteOptions, LeaseOptions, SnmpListenerOptions, DevicesOptions, and OtlpOptions to provide descriptive section-path error messages.
- **DevicesOptions wrapper pattern:** Top-level JSON array "Devices" cannot bind directly to IOptions<T>. Used a wrapper class with `List<DeviceOptions>` and a custom `Configure<IConfiguration>` delegate to bind the array into the list property.
- **Known device types as static HashSet:** DevicesOptionsValidator maintains {router, switch, loadbalancer, simetra} with case-insensitive comparison. Can be extended as new device modules are added.
- **MetricPollOptions.Source via PostConfigure:** Source is excluded from JSON binding ([JsonIgnore]) and stamped as MetricPollSource.Configuration in PostConfigure, allowing future module-discovered polls to use MetricPollSource.Module.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Lingering Simetra.exe process from previous session locked the build output binary, preventing compilation. Resolved by killing the process (PID 26164) before building.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All configuration Options classes are available for injection via IOptions<T> in any subsequent service
- Validators ensure invalid configuration is caught at startup before any pipeline component starts
- Ready for Phase 1 Plan 3 (appsettings.json validation / final wiring)
- Ready for Phase 2 (Channel Infrastructure) which will consume ChannelsOptions
- No blockers or concerns

---
*Phase: 01-project-foundation-configuration*
*Completed: 2026-02-15*
