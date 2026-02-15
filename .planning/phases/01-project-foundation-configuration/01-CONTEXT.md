# Phase 1: Project Foundation + Configuration - Context

**Gathered:** 2026-02-15
**Status:** Ready for planning

<domain>
## Phase Boundary

.NET 9 project scaffolding with Worker Service + ASP.NET minimal API, dependency injection wiring, and strongly typed configuration binding for all Simetra settings from appsettings.json. This phase delivers the compilable foundation that all subsequent phases build on.

</domain>

<decisions>
## Implementation Decisions

### Project Structure
- Two projects in solution: Simetra (main) + Simetra.Tests (test project)
- Root namespace: `Simetra` (e.g. `Simetra.Configuration`, `Simetra.Pipeline`, `Simetra.Devices`)
- Folder organization by layer: Pipeline/, Devices/, Services/, Jobs/, Health/, Middleware/, Telemetry/, Configuration/
- Nullable reference types enabled project-wide

### Config Validation
- Fail-fast on any invalid config — missing required fields, bad types, invalid ranges all crash at startup with descriptive errors
- Empty Devices[] array is valid (only Simetra virtual device runs — useful for framework testing)
- Unknown DeviceType (references a device module that isn't registered) causes startup failure

### appsettings.json Shape
- Environment-specific config files: appsettings.json + appsettings.Development.json (local overrides for ports, endpoints, etc.)
- Ship with sample device entries in appsettings.json showing MetricPolls structure — serves as documentation

### Testing Setup
- xUnit + FluentAssertions + Moq
- Test project mirrors main project folder structure (Tests/Configuration/, Tests/Pipeline/, etc.)

### Claude's Discretion
- Solution file location and src/tests subfolder layout
- Model placement (co-located vs shared Models/ folder)
- Global/implicit usings approach
- .editorconfig inclusion and rules
- Top-level config key wrapping (namespaced vs flat)
- Role field JSON representation in MetricPolls OID entries
- Validation approach (DataAnnotations, IValidateOptions<T>, or FluentValidation)
- Test naming convention
- Whether Phase 1 includes config validation tests or defers to Phase 10
- Exact folder names and any additional organizational conventions

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard .NET 9 approaches. The design document (`requirements and basic design.txt`) specifies the full config shape in Section 9 with all fields, defaults, and types.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-project-foundation-configuration*
*Context gathered: 2026-02-15*
