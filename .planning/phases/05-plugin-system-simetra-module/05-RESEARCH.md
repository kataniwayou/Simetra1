# Phase 5: Plugin System + Simetra Module - Research

**Researched:** 2026-02-15
**Domain:** Plugin/strategy pattern for device modules, self-registration, integration with existing pipeline infrastructure
**Confidence:** HIGH

## Summary

Phase 5 introduces the `IDeviceModule` plugin interface and proves it with the `SimetraModule` virtual device. The core challenge is designing an interface that encapsulates all device-specific behavior (device type, trap definitions, state poll definitions) while integrating cleanly with the existing `DeviceRegistry`, `DeviceChannelManager`, `TrapFilter`, and `ProcessingCoordinator` -- all of which currently source their data exclusively from `IOptions<DevicesOptions>` (appsettings.json configuration).

The standard approach is a simple interface-based strategy pattern: `IDeviceModule` exposes device type, trap definitions (`IReadOnlyList<PollDefinitionDto>` with `Source=Module`), and state poll definitions. Device modules are registered in DI as `IEnumerable<IDeviceModule>`. The key architectural change is refactoring `DeviceRegistry` and `DeviceChannelManager` to accept devices from **both** configuration (appsettings.json `Devices[]`) and modules (code-defined `IDeviceModule` implementations), merging them into a unified device set at startup. `SimetraModule` is the first (and only v1) module: it hardcodes a heartbeat trap definition with a single OID and `Source=Module`, uses `127.0.0.1` as its IP (loopback for heartbeat traps), and flows through the pipeline with zero special-case branches.

No external NuGet packages are needed. This phase uses only built-in .NET 9 DI patterns (`IEnumerable<T>` injection, keyed services are unnecessary here) and the existing project types (`PollDefinitionDto`, `OidEntryDto`, `DeviceInfo`).

**Primary recommendation:** Define `IDeviceModule` as a simple interface returning device metadata and `PollDefinitionDto` collections. Register modules via `IEnumerable<IDeviceModule>`. Refactor `DeviceRegistry` and `DeviceChannelManager` constructors to accept `IEnumerable<IDeviceModule>` alongside `IOptions<DevicesOptions>`, merging both sources into the unified device map. SimetraModule is a concrete class in `Devices/` with hardcoded heartbeat definition.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.DependencyInjection | Built-in (.NET 9) | `IEnumerable<IDeviceModule>` injection pattern | Standard way to resolve all registered implementations of an interface |
| System.Collections.Immutable/ReadOnly | Built-in (.NET 9) | Immutable collections for poll definitions | Prevent mutation after module initialization |
| Existing project types | N/A | PollDefinitionDto, OidEntryDto, DeviceInfo, MetricPollSource | Module interface returns types already consumed by the pipeline |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Logging | Built-in (.NET 9) | ILogger for module registration diagnostics | Log module registration at startup |
| Microsoft.Extensions.Options | Built-in (.NET 9) | IOptions<DevicesOptions> for config-sourced devices | Config devices still loaded alongside module devices |
| Microsoft.Extensions.Options | Built-in (.NET 9) | IOptions<ChannelsOptions> for channel capacity | Channel creation uses existing config |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `IEnumerable<IDeviceModule>` injection | Keyed services (`AddKeyedSingleton`) | Unnecessary complexity; modules are not selected by key at runtime -- all are registered and iterated at startup |
| Interface-based plugin | MEF (Managed Extensibility Framework) | Massively overengineered; MEF is for runtime discovery of external assemblies, not compile-time known modules |
| Interface-based plugin | Assembly scanning (Scrutor) | Unnecessary dependency; modules are explicitly registered in code, not discovered by convention |
| Constructor injection of modules | Service Locator pattern | Anti-pattern; constructor injection is cleaner and testable |

### No Additional NuGet Packages Required

Phase 5 uses only built-in .NET 9 APIs and existing project types. The `IDeviceModule` interface, `SimetraModule` implementation, and DI registration all use standard framework capabilities.

## Architecture Patterns

### Recommended Project Structure
```
src/Simetra/
  Devices/
    IDeviceModule.cs            # Interface: DeviceType, TrapDefinitions, StatePollDefinitions
    SimetraModule.cs            # Heartbeat trap definition, Source=Module
  Pipeline/
    DeviceRegistry.cs           # MODIFIED: accept IEnumerable<IDeviceModule> + IOptions<DevicesOptions>
    DeviceChannelManager.cs     # MODIFIED: accept IEnumerable<IDeviceModule> + IOptions<DevicesOptions>
    DeviceInfo.cs               # UNCHANGED -- modules produce DeviceInfo via interface contract
  Extensions/
    ServiceCollectionExtensions.cs  # MODIFIED: AddDeviceModules() method, updated registrations
```

### Pattern 1: IDeviceModule Interface (PLUG-01, PLUG-02)
**What:** A simple interface that encapsulates all device-specific behavior. Each module provides its device type string, an IP address (for Simetra: loopback), a device name, trap definitions, and state poll definitions. All definitions are `PollDefinitionDto` with `Source=Module` set by the module itself.
**When to use:** Every device type gets one module implementation.
**Example:**
```csharp
// Source: Project architecture (Section 5 - Device Modules)
namespace Simetra.Devices;

/// <summary>
/// Encapsulates all device-specific behavior for a single device type.
/// Each module provides identity, trap definitions, and state poll definitions.
/// All definitions use Source=Module (set by the module, not by config).
/// </summary>
public interface IDeviceModule
{
    /// <summary>
    /// Device type identifier (e.g., "simetra", "router", "switch").
    /// Must match DeviceType values used in configuration.
    /// </summary>
    string DeviceType { get; }

    /// <summary>
    /// Human-readable device name (e.g., "simetra-heartbeat").
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// IP address of the device. For Simetra: "127.0.0.1" (loopback).
    /// For real devices: provided by config, NOT by the module.
    /// </summary>
    string IpAddress { get; }

    /// <summary>
    /// Trap definitions for this device type (Source=Module).
    /// These define which OIDs to accept from traps and how to extract data.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; }

    /// <summary>
    /// State poll definitions for this device type (Source=Module).
    /// These define scheduled SNMP polls that update the State Vector.
    /// </summary>
    IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; }
}
```

### Pattern 2: SimetraModule Hardcoded Implementation (PLUG-03, PLUG-04)
**What:** A concrete module class that defines the Simetra virtual device with a heartbeat trap definition. The module is hardcoded in code -- it is NOT defined in `appsettings.json` `Devices[]`. Its `Source=Module` is set directly in the constructor, not via PostConfigure. The heartbeat OID is a well-known constant (e.g., `1.3.6.1.4.1.9999.1.1.1.0` or similar private enterprise OID).
**When to use:** Exactly once -- the Simetra virtual device.
**Example:**
```csharp
// Source: Design doc Section 5 -- Virtual device "Simetra"
namespace Simetra.Devices;

public sealed class SimetraModule : IDeviceModule
{
    /// <summary>
    /// Well-known OID for the Simetra heartbeat trap.
    /// Used by HeartbeatJob (Phase 6) as the single source of truth for the OID to send.
    /// </summary>
    public const string HeartbeatOid = "1.3.6.1.4.1.9999.1.1.1.0";

    public string DeviceType => "simetra";
    public string DeviceName => "simetra-heartbeat";
    public string IpAddress => "127.0.0.1";

    public IReadOnlyList<PollDefinitionDto> TrapDefinitions { get; } = new[]
    {
        new PollDefinitionDto(
            MetricName: "simetra_heartbeat",
            MetricType: MetricType.Gauge,
            Oids: new[]
            {
                new OidEntryDto(
                    Oid: HeartbeatOid,
                    PropertyName: "beat",
                    Role: OidRole.Metric,
                    EnumMap: null)
            }.ToList().AsReadOnly(),
            IntervalSeconds: 15,
            Source: MetricPollSource.Module)
    }.ToList().AsReadOnly();

    public IReadOnlyList<PollDefinitionDto> StatePollDefinitions { get; } =
        Array.Empty<PollDefinitionDto>().ToList().AsReadOnly();
}
```

### Pattern 3: Merging Module and Config Devices in DeviceRegistry (PLUG-05, PLUG-06)
**What:** `DeviceRegistry` accepts both `IEnumerable<IDeviceModule>` (code-defined modules) and `IOptions<DevicesOptions>` (config-defined devices). It merges them into a single `Dictionary<IPAddress, DeviceInfo>`. Module-sourced devices get their trap definitions from the module. Config-sourced devices get their trap definitions from MetricPolls (converted via `PollDefinitionDto.FromOptions`). When a config device has the same DeviceType as a registered module, the module's trap definitions and state poll definitions are merged with the config's metric poll definitions (this happens in Phase 9's startup sequence, but the registry must be designed to accept the merged result).
**When to use:** At startup during DI resolution.
**Example:**
```csharp
// Source: DeviceRegistry refactoring to accept modules
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<IPAddress, DeviceInfo> _devices;

    public DeviceRegistry(
        IOptions<DevicesOptions> devicesOptions,
        IEnumerable<IDeviceModule> modules)
    {
        _devices = new Dictionary<IPAddress, DeviceInfo>();

        // Register config-sourced devices
        foreach (var d in devicesOptions.Value.Devices)
        {
            var ip = IPAddress.Parse(d.IpAddress).MapToIPv4();
            var trapDefs = d.MetricPolls
                .Select(PollDefinitionDto.FromOptions)
                .ToList().AsReadOnly();
            _devices[ip] = new DeviceInfo(d.Name, d.IpAddress, d.DeviceType, trapDefs);
        }

        // Register module-sourced devices (e.g., SimetraModule)
        foreach (var module in modules)
        {
            var ip = IPAddress.Parse(module.IpAddress).MapToIPv4();
            // Module provides its own device identity and trap definitions
            _devices[ip] = new DeviceInfo(
                module.DeviceName, module.IpAddress, module.DeviceType,
                module.TrapDefinitions);
        }
    }

    public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
    {
        return _devices.TryGetValue(senderIp.MapToIPv4(), out device);
    }
}
```

### Pattern 4: DeviceChannelManager Accepting Modules (PLUG-05)
**What:** `DeviceChannelManager` creates channels for both config devices and module devices. It needs to know all device names at construction time to create bounded channels.
**When to use:** At startup during DI resolution.
**Example:**
```csharp
// Source: DeviceChannelManager refactoring
public DeviceChannelManager(
    IOptions<DevicesOptions> devicesOptions,
    IOptions<ChannelsOptions> channelsOptions,
    IEnumerable<IDeviceModule> modules,
    ILogger<DeviceChannelManager> logger)
{
    var capacity = channelsOptions.Value.BoundedCapacity;

    // Collect all device names from both sources
    var allDeviceNames = devicesOptions.Value.Devices
        .Select(d => d.Name)
        .Concat(modules.Select(m => m.DeviceName));

    _channels = new Dictionary<string, Channel<TrapEnvelope>>(StringComparer.Ordinal);

    foreach (var deviceName in allDeviceNames)
    {
        var options = new BoundedChannelOptions(capacity) { /* ... */ };
        var channel = Channel.CreateBounded(options, /* itemDropped callback */);
        _channels[deviceName] = channel;
    }
}
```

### Pattern 5: DI Registration with AddDeviceModules (PLUG-06)
**What:** A new extension method `AddDeviceModules()` registers all `IDeviceModule` implementations. Adding a new device type requires only: (1) create the module class, (2) add a config entry for each physical device of that type, (3) register the module in `AddDeviceModules()`. No changes to existing pipeline code.
**When to use:** In `Program.cs` via `ServiceCollectionExtensions`.
**Example:**
```csharp
// Source: ServiceCollectionExtensions
public static IServiceCollection AddDeviceModules(this IServiceCollection services)
{
    // Register all device modules as IDeviceModule (singleton -- immutable after construction)
    services.AddSingleton<IDeviceModule, SimetraModule>();

    // Future modules added here:
    // services.AddSingleton<IDeviceModule, RouterModule>();
    // services.AddSingleton<IDeviceModule, SwitchModule>();

    return services;
}
```

### Anti-Patterns to Avoid
- **Special-casing Simetra in the pipeline:** The entire point is that SimetraModule flows through the pipeline uniformly. No `if (deviceType == "simetra")` branches anywhere in listener, routing, extraction, or processing. The module defines PollDefinitionDto with Source=Module; the pipeline handles it generically.
- **Putting Simetra in appsettings.json:** PLUG-04 explicitly requires SimetraModule to be hardcoded in code. It must NOT appear in the `Devices[]` array.
- **Making IDeviceModule overly complex:** The interface should expose data (definitions), not behavior (processing logic). The pipeline is the behavior; modules provide data that drives the pipeline.
- **Using keyed services for module lookup:** Keyed DI is for runtime selection by key. Modules are all registered at startup and iterated; there is no runtime "pick one by key" scenario.
- **Giving modules their own Channel<T> property:** The design doc says modules "contain" a channel, but this means the channel manager creates one per module -- the module does not own or manage the channel. Channel lifecycle is managed by `DeviceChannelManager`.
- **Making module registration dynamic (runtime hot-add):** Static config, restart required. Modules are compile-time known. MEF/assembly scanning is overkill.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Multi-implementation DI registration | Custom registry/factory pattern | `IEnumerable<IDeviceModule>` from DI container | Built-in .NET DI resolves all registered implementations of an interface automatically |
| Device-to-module matching | Manual switch/case on DeviceType | Module provides DeviceType property; registry iterates modules | Open/Closed -- new module = new class, no switch cases to update |
| Immutable poll definition collections | Custom immutable wrapper | `IReadOnlyList<T>` + `.ToList().AsReadOnly()` | Standard .NET pattern already used throughout the project |
| Source field assignment for module polls | PostConfigure callback like config polls | Module sets `Source=Module` directly in constructor | Module-defined definitions know their source at compile time |

**Key insight:** The plugin system is NOT a complex extensibility framework. It is a simple strategy pattern using .NET DI's built-in `IEnumerable<T>` resolution. The "plugin" is just a class implementing an interface, registered as a singleton, providing data (PollDefinitionDtos) that the existing pipeline consumes generically.

## Common Pitfalls

### Pitfall 1: IP Address Collision Between Module and Config Devices
**What goes wrong:** SimetraModule uses `127.0.0.1` (loopback). If a config device also uses `127.0.0.1`, the DeviceRegistry will overwrite one entry with the other.
**Why it happens:** Both sources are merged into the same `Dictionary<IPAddress, DeviceInfo>` keyed by normalized IP.
**How to avoid:** SimetraModule uses `127.0.0.1` which is reserved for loopback -- no real network device should use this IP. The DevicesOptionsValidator could also be updated to reject `127.0.0.1` in the Devices[] config. Alternatively, process modules after config devices so modules take precedence (module is authoritative for its IP).
**Warning signs:** Heartbeat traps not being matched by the device registry, "trap from unknown device" log messages for `127.0.0.1`.

### Pitfall 2: Breaking DeviceRegistry/DeviceChannelManager Constructors
**What goes wrong:** Adding `IEnumerable<IDeviceModule>` to constructors changes their DI resolution signature. If modules are registered after the services that depend on them, DI resolution order issues can occur.
**Why it happens:** .NET DI resolves dependencies at first request, so registration order does not usually matter. However, `IEnumerable<IDeviceModule>` will resolve to empty if no modules are registered.
**How to avoid:** Ensure `AddDeviceModules()` is called before `AddSnmpPipeline()` in `Program.cs`. Add defensive checks: if `modules` is empty, log a warning (a system with zero modules is valid but unusual).
**Warning signs:** Empty device lists, no channels created, heartbeat traps rejected.

### Pitfall 3: Confusing Module TrapDefinitions with Config MetricPolls
**What goes wrong:** A module's TrapDefinitions have `Source=Module`. Config MetricPolls have `Source=Configuration` (set by PostConfigure). If these get mixed up, source-based routing breaks -- config polls would update the State Vector, or module definitions would skip it.
**Why it happens:** Both use the same `PollDefinitionDto` type. The Source field is the discriminator.
**How to avoid:** Module implementations MUST set `Source=Module` explicitly in their PollDefinitionDto constructors. Config polls have Source set by the existing PostConfigure callback. Never pass raw module definitions through the config PostConfigure path.
**Warning signs:** State Vector not updating for heartbeat data, or config polls unexpectedly updating the State Vector.

### Pitfall 4: DeviceInfo.TrapDefinitions Only Containing Module Definitions
**What goes wrong:** When constructing DeviceInfo for a module device, only the module's TrapDefinitions are used. But in the full startup sequence (Phase 9), a config device of the same DeviceType might need BOTH the module's trap definitions AND the config's metric poll definitions merged.
**Why it happens:** Phase 5 focuses on the SimetraModule which is hardcoded and has no config counterpart. But the design must support future phases where a config device (e.g., a router in Devices[]) binds to a module (RouterModule) and gets merged definitions.
**How to avoid:** In Phase 5, SimetraModule is self-contained -- it has no config counterpart (PLUG-04). The DeviceInfo created for SimetraModule only needs the module's TrapDefinitions. The definition-merging logic (hardcoded + configurable) is a Phase 9 concern (LIFE-03). Phase 5 should design the interface to SUPPORT merging, but need not implement it.
**Warning signs:** Premature optimization -- trying to build the full merge logic in Phase 5 when only SimetraModule exists.

### Pitfall 5: Forgetting to Update DevicesOptionsValidator
**What goes wrong:** The existing `DevicesOptionsValidator` has a static `KnownDeviceTypes` HashSet containing `"simetra"`. If a config device uses DeviceType `"simetra"`, it would pass validation but clash with the hardcoded SimetraModule.
**Why it happens:** The validator was written before the module system existed.
**How to avoid:** Consider whether `"simetra"` should remain in the validator's `KnownDeviceTypes`. Since SimetraModule is hardcoded (PLUG-04), config devices should NOT use DeviceType `"simetra"`. The validator could either remove `"simetra"` from KnownDeviceTypes (rejecting config devices of type simetra) or leave it and rely on IP collision handling. Removing it is cleaner -- simetra devices are always code-defined, never config-defined. However, this might break existing config. Safer approach: keep "simetra" in KnownDeviceTypes for now, but document that config devices of type "simetra" are not intended.
**Warning signs:** Config file with a device of type "simetra" clashing with the hardcoded SimetraModule.

## Code Examples

Verified patterns from the existing codebase and official sources:

### IEnumerable<T> Injection for All Implementations
```csharp
// Source: Built-in .NET DI pattern
// When multiple implementations are registered:
services.AddSingleton<IDeviceModule, SimetraModule>();
services.AddSingleton<IDeviceModule, RouterModule>(); // future

// Constructor injection resolves ALL:
public DeviceRegistry(
    IOptions<DevicesOptions> devicesOptions,
    IEnumerable<IDeviceModule> modules)  // contains SimetraModule, RouterModule, etc.
{
    // ...
}
```

### Creating PollDefinitionDto Directly in Module Code
```csharp
// Source: Existing PollDefinitionDto record constructor (Models/PollDefinitionDto.cs)
// Modules create definitions directly, bypassing the FromOptions path
var trapDef = new PollDefinitionDto(
    MetricName: "simetra_heartbeat",
    MetricType: MetricType.Gauge,
    Oids: new List<OidEntryDto>
    {
        new OidEntryDto(
            Oid: "1.3.6.1.4.1.9999.1.1.1.0",
            PropertyName: "beat",
            Role: OidRole.Metric,
            EnumMap: null)
    }.AsReadOnly(),
    IntervalSeconds: 15,
    Source: MetricPollSource.Module);  // Set directly -- no PostConfigure
```

### DeviceInfo Construction from Module
```csharp
// Source: Existing DeviceInfo sealed record (Pipeline/DeviceInfo.cs)
// Module provides all four fields:
var deviceInfo = new DeviceInfo(
    Name: module.DeviceName,       // "simetra-heartbeat"
    IpAddress: module.IpAddress,   // "127.0.0.1"
    DeviceType: module.DeviceType, // "simetra"
    TrapDefinitions: module.TrapDefinitions);
```

### Program.cs Registration Order
```csharp
// Source: Program.cs pattern (existing)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSimetraConfiguration(builder.Configuration);
builder.Services.AddDeviceModules();     // NEW: register IDeviceModule implementations
builder.Services.AddSnmpPipeline();      // EXISTING: now depends on IEnumerable<IDeviceModule>
builder.Services.AddProcessingPipeline();
```

### Heartbeat OID as Public Constant (for Phase 6 HeartbeatJob)
```csharp
// Source: Design doc Section 7 -- heartbeat OID from module trap definition (SCHED-06)
// HeartbeatJob (Phase 6) reads the OID from SimetraModule's trap definition:
public class HeartbeatJob : IJob
{
    public HeartbeatJob(IEnumerable<IDeviceModule> modules)
    {
        var simetra = modules.Single(m => m.DeviceType == "simetra");
        var heartbeatOid = simetra.TrapDefinitions[0].Oids[0].Oid;
        // Or use the public constant: SimetraModule.HeartbeatOid
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Custom factory/registry patterns | `IEnumerable<T>` DI injection | .NET Core 1.0+ | Built-in, no custom code needed for multi-impl resolution |
| Assembly scanning (MEF, Scrutor) | Explicit registration | Ongoing best practice | Simpler, explicit, no reflection overhead |
| Keyed services for module lookup | `IEnumerable<T>` iteration | .NET 8 added keyed services, but not needed here | Keyed services are for runtime selection; modules are iterated, not selected |
| Abstract base class plugins | Interface-only plugins | Modern .NET patterns | Interfaces are more flexible, avoid inheritance coupling |

**Deprecated/outdated:**
- **MEF (System.ComponentModel.Composition):** For external assembly discovery and runtime composition. Overkill for compile-time known device modules.
- **Assembly scanning via Scrutor:** Convention-based auto-registration. Unnecessary when modules are explicitly registered.

## Open Questions

Things that couldn't be fully resolved:

1. **Simetra heartbeat OID value**
   - What we know: The design doc references private enterprise OIDs under `1.3.6.1.4.1.9999.*`. The existing appsettings.json uses OIDs in this range (e.g., `1.3.6.1.4.1.9999.1.3.1.0` for CPU, `1.3.6.1.4.1.9999.1.1.1.0` referenced in examples).
   - What's unclear: The exact OID for the heartbeat trap is not specified in the design doc. The number `9999` is a placeholder private enterprise number.
   - Recommendation: Use `1.3.6.1.4.1.9999.1.1.1.0` as the heartbeat OID, consistent with existing OID patterns. Expose as `SimetraModule.HeartbeatOid` public constant for Phase 6 consumption. **Confidence: MEDIUM** -- consistent with codebase patterns but not explicitly specified.

2. **Whether IDeviceModule should expose IpAddress directly**
   - What we know: SimetraModule is hardcoded with `127.0.0.1`. Real device modules (v2) would have IPs from config, not from the module.
   - What's unclear: For real devices (future), the IP comes from config. The module provides the device TYPE, not the device instance identity. Only SimetraModule provides its own IP because it is a single hardcoded virtual device.
   - Recommendation: For Phase 5, `IDeviceModule` exposes `DeviceName` and `IpAddress` because SimetraModule needs them. Future phases may refactor this interface so real modules only provide DeviceType + definitions, and device instances come from config bound by DeviceType. This refactoring is outside Phase 5 scope. **Confidence: MEDIUM** -- the interface may evolve but works correctly for the v1 single-instance module pattern.

3. **Definition merging for real devices (LIFE-03)**
   - What we know: Phase 9 requires "startup merges hardcoded (Source=Module) + configurable (Source=Configuration) poll definitions per device." This means a config device of type "router" should get both the RouterModule's hardcoded definitions AND the config's MetricPolls.
   - What's unclear: Whether Phase 5 needs to implement this merging or just make it possible.
   - Recommendation: Phase 5 does NOT implement definition merging. SimetraModule is the only module and has no config counterpart (PLUG-04). The DeviceRegistry design should support future merging by keeping the interface flexible, but the actual merge logic belongs in Phase 9 (LIFE-03). **Confidence: HIGH** -- PLUG-04 explicitly states SimetraModule is not in config.

## Sources

### Primary (HIGH confidence)
- Existing codebase analysis: DeviceInfo, PollDefinitionDto, OidEntryDto, DeviceRegistry, DeviceChannelManager, IProcessingCoordinator, MetricPollSource, TrapFilter, SnmpListenerService, ServiceCollectionExtensions -- all read and analyzed for integration points
- Design document (`requirements and basic design.txt` v4, Section 5 - Device Modules) -- authoritative specification for module structure, plugin pattern, and SimetraModule behavior
- [Microsoft Learn - Dependency injection in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection) -- IEnumerable<T> resolution, service lifetimes, registration patterns
- [Microsoft Learn - Dependency injection - .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) -- Multiple implementation registration, IEnumerable resolution behavior

### Secondary (MEDIUM confidence)
- [Keyed Services in .NET - codewithmukesh](https://codewithmukesh.com/blog/keyed-services-dotnet-advanced-di/) -- Confirmed keyed services are NOT needed for this use case (verified: modules are iterated, not selected by key)
- [Strategy Pattern + Plugin Architecture - Medium](https://medium.com/@bhargavkoya56/how-strategy-pattern-plugin-architecture-revolutionizes-c-development-e60fe154e6b9) -- Confirmed interface-based strategy pattern is standard for .NET plugin systems
- [Register Multiple Implementations - Code Maze](https://code-maze.com/aspnetcore-register-multiple-interface-implementations/) -- Verified IEnumerable<T> injection pattern for multiple implementations

### Tertiary (LOW confidence)
- None -- all findings verified with official sources or existing codebase.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Uses only built-in .NET 9 DI patterns, no external packages, verified against existing codebase
- Architecture: HIGH - Interface design directly derived from design document Section 5, integration points verified by reading all existing pipeline types
- Pitfalls: HIGH - Based on concrete analysis of existing code (DeviceRegistry/DeviceChannelManager constructors, DevicesOptionsValidator KnownDeviceTypes set, IP address handling)
- Code examples: HIGH - All examples use existing project types (PollDefinitionDto, OidEntryDto, DeviceInfo) and verified .NET DI patterns

**Research date:** 2026-02-15
**Valid until:** 2026-03-15 (30 days -- interface-based plugin patterns are stable, .NET 9 DI is stable)
