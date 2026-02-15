# Phase 5 Plan 1: IDeviceModule Interface + Registry/ChannelManager Refactor Summary

**One-liner:** IDeviceModule plugin interface defining 5-property device contract, with DeviceRegistry and DeviceChannelManager refactored to merge config + module device sources at startup

## What Was Done

### Task 1: Create IDeviceModule interface
- Created `src/Simetra/Devices/IDeviceModule.cs` with namespace `Simetra.Devices`
- Interface exposes: DeviceType, DeviceName, IpAddress, TrapDefinitions, StatePollDefinitions
- All properties are read-only (data contract, not behavior)
- Full XML documentation on interface and all five properties
- Commit: `6def539`

### Task 2: Refactor DeviceRegistry and DeviceChannelManager for module support
- **DeviceRegistry**: Added `IEnumerable<IDeviceModule> modules` constructor parameter alongside existing `IOptions<DevicesOptions>`
- Config devices registered first, module devices second (module takes precedence on IP collision via dictionary overwrite)
- **DeviceChannelManager**: Added `IEnumerable<IDeviceModule> modules` constructor parameter
- Channel creation now iterates merged `configDeviceNames.Concat(moduleDeviceNames)` instead of config-only devices
- No interface file changes (IDeviceRegistry.cs, IDeviceChannelManager.cs unchanged)
- Commit: `587699b`

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Module devices registered after config in DeviceRegistry | Dictionary overwrite gives module precedence on IP collision -- module is authoritative for its own IP |
| DeviceChannelManager uses Concat without pre-sizing dictionary | Small collection; avoiding .Count() on IEnumerable; StringComparer.Ordinal preserved |
| Closure variable `name` captured separately from loop variable | Prevents closure-over-loop-variable issue in channel dropped callback |

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

1. `dotnet build src/Simetra/Simetra.csproj` -- 0 errors, 0 warnings
2. IDeviceModule exists at `src/Simetra/Devices/IDeviceModule.cs` with all 5 properties
3. DeviceRegistry constructor: `(IOptions<DevicesOptions> devicesOptions, IEnumerable<IDeviceModule> modules)`
4. DeviceChannelManager constructor: `(IOptions<DevicesOptions>, IOptions<ChannelsOptions>, IEnumerable<IDeviceModule>, ILogger<DeviceChannelManager>)`
5. Module devices registered after config devices (lines 48-53 in DeviceRegistry)

## Key Files

### Created
- `src/Simetra/Devices/IDeviceModule.cs` -- Plugin interface for code-defined device modules

### Modified
- `src/Simetra/Pipeline/DeviceRegistry.cs` -- Added IEnumerable<IDeviceModule> parameter, module registration loop
- `src/Simetra/Pipeline/DeviceChannelManager.cs` -- Added IEnumerable<IDeviceModule> parameter, merged device name iteration

## Duration

~1.5 minutes (2 tasks, 2 commits)

## Next Phase Readiness

Plan 05-02 can proceed immediately. It will:
- Implement `SimetraDeviceModule` as a concrete `IDeviceModule` for the heartbeat device
- Register it in DI via `ServiceCollectionExtensions`
- The `IEnumerable<IDeviceModule>` parameters added here will resolve to the registered modules
