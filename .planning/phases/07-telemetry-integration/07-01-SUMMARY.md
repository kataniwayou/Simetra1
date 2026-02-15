---
phase: "07-telemetry-integration"
plan: "01"
subsystem: "telemetry"
tags: [opentelemetry, otlp, metrics, tracing, leader-election]
dependency-graph:
  requires: ["01-01", "04-01"]
  provides: ["MeterProvider", "TracerProvider", "ILeaderElection", "RoleGatedExporter"]
  affects: ["08-ha", "09-lifecycle"]
tech-stack:
  added: ["OpenTelemetry.Extensions.Hosting 1.15.0", "OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0", "OpenTelemetry.Instrumentation.Runtime 1.15.0"]
  patterns: ["IHostApplicationBuilder extension for telemetry", "BaseExporter decorator pattern for role-gated export"]
key-files:
  created:
    - "src/Simetra/Telemetry/TelemetryConstants.cs"
    - "src/Simetra/Telemetry/ILeaderElection.cs"
    - "src/Simetra/Telemetry/AlwaysLeaderElection.cs"
    - "src/Simetra/Telemetry/RoleGatedExporter.cs"
  modified:
    - "src/Simetra/Simetra.csproj"
    - "src/Simetra/Extensions/ServiceCollectionExtensions.cs"
    - "src/Simetra/Program.cs"
decisions:
  - id: "DI-ORDER-TELEMETRY"
    summary: "AddSimetraTelemetry on IHostApplicationBuilder called first in Program.cs, before AddSimetraConfiguration"
  - id: "ROLE-GATED-SUCCESS"
    summary: "RoleGatedExporter returns ExportResult.Success (not Failure) when follower to prevent SDK retry backoff"
metrics:
  duration: "2 min"
  completed: "2026-02-15"
---

# Phase 7 Plan 01: OpenTelemetry Provider Infrastructure Summary

**OTLP MeterProvider/TracerProvider with runtime instrumentation and leader election abstraction for HA-gated export**

## What Was Done

### Task 1: Install OpenTelemetry NuGet packages and create Telemetry types
- Installed three OpenTelemetry packages (Extensions.Hosting, Exporter.OTLP, Instrumentation.Runtime) all at 1.15.0
- Created `TelemetryConstants` with `MeterName = "Simetra.Metrics"` matching MetricFactory's meter creation string
- Created `ILeaderElection` interface with `IsLeader` and `CurrentRole` properties
- Created `AlwaysLeaderElection` default implementation (always reports leader)
- Created `RoleGatedExporter<T>` BaseExporter decorator that silently drops batches when follower (returns Success to avoid retry)
- **Commit:** `370e367`

### Task 2: Create AddSimetraTelemetry extension and update Program.cs DI order
- Added `AddSimetraTelemetry` extension method on `IHostApplicationBuilder`
- MeterProvider subscribes to `TelemetryConstants.MeterName` ("Simetra.Metrics") and adds runtime instrumentation
- TracerProvider subscribes to `TelemetryConstants.TracingSourceName` ("Simetra.Tracing")
- Both providers export via OTLP to endpoint from `OtlpOptions` configuration
- `ILeaderElection` registered as singleton `AlwaysLeaderElection`
- Updated Program.cs: `builder.AddSimetraTelemetry()` is now the first call after `CreateBuilder`
- **Commit:** `cf416a7`

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| DI-ORDER-TELEMETRY | AddSimetraTelemetry called first in Program.cs via IHostApplicationBuilder (not IServiceCollection) | Registered first = disposed last; ensures ForceFlush during shutdown before other services tear down |
| ROLE-GATED-SUCCESS | RoleGatedExporter returns ExportResult.Success when follower | Returning Failure triggers SDK retry/backoff logic which wastes resources on followers |
| DI-REGISTRATION | DI order: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks | Telemetry providers must be first for correct disposal ordering |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] OtlpOptions required property initialization**
- **Found during:** Task 2
- **Issue:** `OtlpOptions` uses C# `required` keyword on `Endpoint` and `ServiceName` properties, preventing `new OtlpOptions()` without initializers
- **Fix:** Changed to `new OtlpOptions { Endpoint = "", ServiceName = "" }` with Bind overwriting defaults
- **Files modified:** ServiceCollectionExtensions.cs
- **Commit:** cf416a7

## Verification Results

| Check | Result |
|-------|--------|
| dotnet build zero errors | PASS |
| Telemetry/ has 4 files | PASS |
| Csproj has 3 OTel packages | PASS |
| AddSimetraTelemetry before AddSimetraConfiguration in Program.cs | PASS |
| AddMeter(TelemetryConstants.MeterName) present | PASS |
| AddRuntimeInstrumentation() present | PASS |

## Next Phase Readiness

- **Phase 8 (HA):** ILeaderElection interface ready for distributed lease implementation; RoleGatedExporter ready for wiring into OTLP exporter chain
- **Phase 9 (Lifecycle):** MeterProvider and TracerProvider registered first, enabling ForceFlush during graceful shutdown
- **No blockers identified**
