---
phase: 01-project-foundation-configuration
plan: 01
subsystem: infra
tags: [dotnet, net9, aspnet, xunit, fluentassertions, moq, health-checks, solution-scaffold]

# Dependency graph
requires: []
provides:
  - "Simetra.sln with two projects (Simetra + Simetra.Tests)"
  - "WebApplication.CreateBuilder with health check endpoints (/healthz/startup, /healthz/ready, /healthz/live)"
  - "appsettings.json with all 10 config sections and sample device data"
  - "Test project wired with xUnit 2.9.3, FluentAssertions 7.2.0, Moq 4.20.72"
  - "Architecture directory structure (Configuration, Models, Pipeline, Devices, Services, Jobs, Health, Middleware, Telemetry, Extensions)"
affects:
  - 01-project-foundation-configuration (Plan 02 builds config binding on this)
  - 01-project-foundation-configuration (Plan 03 tests config on this)
  - All subsequent phases (build on this foundation)

# Tech tracking
tech-stack:
  added:
    - "Microsoft.NET.Sdk.Web (net9.0)"
    - "xunit 2.9.3"
    - "xunit.runner.visualstudio 2.8.2"
    - "FluentAssertions 7.2.0 (Apache 2.0 -- pinned, do NOT upgrade to 8.x)"
    - "Moq 4.20.72"
    - "Microsoft.NET.Test.Sdk 17.12.0"
    - "coverlet.collector 6.0.4"
  patterns:
    - "Top-level statements in Program.cs (no Startup class)"
    - "WebApplication.CreateBuilder pattern"
    - "Flat config sections at JSON root (no wrapping namespace)"
    - "GlobalUsings.cs for explicit project-wide imports"
    - ".editorconfig with Allman braces, 4-space indent, System-first usings"

key-files:
  created:
    - "Simetra.sln"
    - "src/Simetra/Simetra.csproj"
    - "src/Simetra/Program.cs"
    - "src/Simetra/GlobalUsings.cs"
    - "src/Simetra/appsettings.json"
    - "src/Simetra/appsettings.Development.json"
    - "src/Simetra/Configuration/Placeholder.cs"
    - "tests/Simetra.Tests/Simetra.Tests.csproj"
    - "tests/Simetra.Tests/GlobalUsings.cs"
    - ".editorconfig"
    - ".gitignore"
  modified: []

key-decisions:
  - "Used Microsoft.NET.Sdk.Web (not Worker) for combined HTTP + BackgroundService host"
  - "Flat config sections (Site, Lease, etc.) at JSON root -- no wrapping object"
  - "FluentAssertions pinned to 7.2.0 (Apache 2.0) -- 8.x requires commercial license"
  - "Configuration namespace placeholder with marker class for compile-time resolution"

patterns-established:
  - "Top-level Program.cs: var builder = WebApplication.CreateBuilder(args)"
  - "Health endpoints: /healthz/startup, /healthz/ready, /healthz/live"
  - "Project layout: src/Simetra/ for main, tests/Simetra.Tests/ for tests"
  - "Architecture folders: Configuration/, Models/, Pipeline/, Devices/, Services/, Jobs/, Health/, Middleware/, Telemetry/, Extensions/"

# Metrics
duration: 6min
completed: 2026-02-15
---

# Phase 01 Plan 01: Project Scaffold Summary

**.NET 9 Web SDK solution with health check endpoints, xUnit+FluentAssertions+Moq test project, and 10-section appsettings.json with sample SNMP device data**

## Performance

- **Duration:** 5 min 49 sec
- **Started:** 2026-02-15T05:43:43Z
- **Completed:** 2026-02-15T05:49:32Z
- **Tasks:** 2/2
- **Files modified:** 20

## Accomplishments
- Solution compiles and runs with 0 errors, 0 warnings
- Three health check endpoints (/healthz/startup, /healthz/ready, /healthz/live) all return HTTP 200
- appsettings.json contains all 10 configuration sections with sample device data (router-core-1 with MetricPolls, switch-floor-2 without)
- Test project wired with xUnit 2.9.3, FluentAssertions 7.2.0 (Apache 2.0), Moq 4.20.72 -- dotnet test runs clean
- Full architecture directory structure established with .gitkeep placeholders

## Task Commits

Each task was committed atomically:

1. **Task 1: Create solution, projects, and directory structure** - `94227f5` (feat)
2. **Task 2: Create Program.cs, GlobalUsings, and appsettings.json skeleton** - `91af4e7` (feat)

**Plan metadata:** (pending)

## Files Created/Modified
- `Simetra.sln` - Solution file referencing both projects
- `src/Simetra/Simetra.csproj` - Main project: Microsoft.NET.Sdk.Web, net9.0, nullable enabled
- `src/Simetra/Program.cs` - WebApplication.CreateBuilder with health check endpoint mapping
- `src/Simetra/GlobalUsings.cs` - Project-wide using for Simetra.Configuration
- `src/Simetra/appsettings.json` - Full config: Site, Lease, SnmpListener, HeartbeatJob, CorrelationJob, Liveness, Channels, Devices (2 samples), Otlp, Logging
- `src/Simetra/appsettings.Development.json` - Debug logging and local OTLP endpoint overrides
- `src/Simetra/Configuration/Placeholder.cs` - Namespace marker for compile-time resolution (removed in Plan 02)
- `src/Simetra/Properties/launchSettings.json` - Template-generated, HTTP on port 5022
- `tests/Simetra.Tests/Simetra.Tests.csproj` - Test project with xUnit, FluentAssertions 7.2.0, Moq, coverlet
- `tests/Simetra.Tests/GlobalUsings.cs` - Test global usings: Xunit, FluentAssertions, Moq, Simetra.Configuration
- `.editorconfig` - Standard .NET rules: Allman braces, 4-space indent, nullable warnings, naming conventions
- `.gitignore` - Standard .NET gitignore from dotnet template

## Decisions Made
- **Web SDK over Worker SDK:** Microsoft.NET.Sdk.Web provides both BackgroundService and Kestrel for health endpoints in a single host, avoiding manual HTTP server setup.
- **Flat config sections:** Each section (Site, Lease, SnmpListener, etc.) at JSON root, matching the design document Section 9 exactly. No wrapping namespace.
- **FluentAssertions 7.2.0 pinned:** Version 8.x changed to commercial license (Xceed, $130/dev/year). 7.2.0 is Apache 2.0. Comment added to csproj.
- **Configuration namespace placeholder:** Added a marker class (`ConfigurationNamespaceMarker`) in `Simetra.Configuration` to enable the `global using` directive to compile before actual options classes exist. Will be removed in Plan 02.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added Configuration namespace placeholder with marker class**
- **Found during:** Task 2 (GlobalUsings.cs creation)
- **Issue:** `global using Simetra.Configuration;` in both GlobalUsings files caused CS0246 compile error because no types existed in the Simetra.Configuration namespace yet. An empty `namespace Simetra.Configuration;` declaration without types is not sufficient -- the compiler needs at least one type to recognize the namespace.
- **Fix:** Created `src/Simetra/Configuration/Placeholder.cs` with an internal static marker class (`ConfigurationNamespaceMarker`) to establish the namespace. This file will be removed when Plan 02 creates actual configuration options classes.
- **Files modified:** src/Simetra/Configuration/Placeholder.cs
- **Verification:** dotnet build succeeds with 0 errors across both projects
- **Committed in:** 91af4e7 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary for compilation. Zero scope creep -- the placeholder is temporary and will be removed in the next plan.

## Issues Encountered
None beyond the deviation above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Solution compiles and runs, ready for Plan 02 (strongly typed configuration binding)
- All architecture directories in place for subsequent phases
- Test infrastructure ready for Plan 03 (configuration validation tests)
- Configuration namespace placeholder must be removed when actual options classes are added in Plan 02

---
*Phase: 01-project-foundation-configuration*
*Completed: 2026-02-15*
