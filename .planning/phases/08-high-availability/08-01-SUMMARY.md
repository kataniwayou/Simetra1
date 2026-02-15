---
phase: "08-high-availability"
plan: "01"
subsystem: "leader-election"
tags: ["kubernetes", "leader-election", "lease-api", "ha", "backgroundservice"]

dependency_graph:
  requires:
    - "07-telemetry-integration (ILeaderElection interface, RoleGatedExporter, SimetraLogEnrichmentProcessor)"
    - "01-project-foundation-configuration (LeaseOptions, SiteOptions)"
  provides:
    - "K8sLeaseElection: BackgroundService + ILeaderElection for Kubernetes Lease-based leader election"
    - "Environment-based DI: auto-detects in-cluster vs local dev"
    - "Graceful lease release on SIGTERM for near-instant failover"
  affects:
    - "08-02 (role-gated OTLP exporter wiring consumes ILeaderElection)"
    - "09-docker-kubernetes (Kubernetes RBAC for coordination.k8s.io/v1 leases)"
    - "10-testing (K8sLeaseElection unit tests with mocked IKubernetes)"

tech_stack:
  added:
    - "KubernetesClient 18.0.13 (IKubernetes, LeaderElector, LeaseLock)"
  patterns:
    - "Singleton forwarding: concrete type registered once, resolved for multiple interfaces"
    - "volatile bool for thread-safe single-writer-multiple-reader leadership flag"
    - "Environment-based DI auto-detection via KubernetesClientConfiguration.IsInCluster()"

key_files:
  created:
    - "src/Simetra/Telemetry/K8sLeaseElection.cs"
  modified:
    - "src/Simetra/Simetra.csproj"
    - "src/Simetra/Extensions/ServiceCollectionExtensions.cs"

decisions:
  - id: "08-01-01"
    decision: "Single-instance DI pattern for K8sLeaseElection"
    rationale: "Register as concrete singleton, then forward to ILeaderElection and IHostedService via GetRequiredService -- avoids two-instance pitfall where hosted service updates leadership on one instance but consumers read from a different one"
  - id: "08-01-02"
    decision: "RunAndTryToHoldLeadershipForeverAsync over RunUntilLeadershipLostAsync"
    rationale: "Pod remains a candidate for re-election after leadership loss -- no restart needed, LeaderElector retries internally"
  - id: "08-01-03"
    decision: "Explicit lease delete on StopAsync for near-instant failover"
    rationale: "On SIGTERM, leader deletes the lease via CoordinationV1.DeleteNamespacedLeaseAsync so followers can acquire immediately instead of waiting for TTL expiry"

metrics:
  duration: "3 min"
  completed: "2026-02-15"
---

# Phase 8 Plan 1: K8sLeaseElection and Environment-Based DI Summary

**One-liner:** K8s Lease-based leader election via KubernetesClient LeaderElector with volatile bool, singleton-forwarding DI, and explicit lease delete on SIGTERM for near-instant failover.

## What Was Built

### K8sLeaseElection (src/Simetra/Telemetry/K8sLeaseElection.cs)
- Sealed class implementing both `BackgroundService` and `ILeaderElection`
- `volatile bool _isLeader` for thread-safe single-writer (LeaderElector events) multiple-reader (RoleGatedExporter, SimetraLogEnrichmentProcessor) access
- `IsLeader` property and `CurrentRole` ("leader"/"follower") for the ILeaderElection contract
- Constructor injection: `IOptions<LeaseOptions>`, `IOptions<SiteOptions>`, `IKubernetes`, `IHostApplicationLifetime`, `ILogger<K8sLeaseElection>`
- `ExecuteAsync`: creates `LeaseLock` + `LeaderElectionConfig` + `LeaderElector` and calls `RunAndTryToHoldLeadershipForeverAsync` (retries indefinitely on loss)
- `StopAsync`: calls `base.StopAsync` first, then if leader, deletes lease via `CoordinationV1.DeleteNamespacedLeaseAsync` for near-instant failover

### Environment-Based DI (src/Simetra/Extensions/ServiceCollectionExtensions.cs)
- `KubernetesClientConfiguration.IsInCluster()` auto-detects Kubernetes environment
- In-cluster: registers `IKubernetes`, then `K8sLeaseElection` as concrete singleton with forwarding to both `ILeaderElection` and `IHostedService`
- Local dev: preserves `AlwaysLeaderElection` (always reports leader, no Kubernetes dependency)

### KubernetesClient Package (src/Simetra/Simetra.csproj)
- Added `KubernetesClient` version 18.0.13 package reference

## Decisions Made

1. **Single-instance DI pattern** -- Concrete singleton registered first, resolved via `GetRequiredService<K8sLeaseElection>()` for both `ILeaderElection` and `IHostedService`. Prevents the two-instance pitfall (see 08-RESEARCH.md Pitfall 1).

2. **RunAndTryToHoldLeadershipForeverAsync** -- Pod remains a candidate for re-election after leadership loss without restart. More resilient than RunUntilLeadershipLostAsync which would exit and require process restart.

3. **Explicit lease delete on SIGTERM** -- On graceful shutdown, the leader explicitly deletes the lease via `DeleteNamespacedLeaseAsync` so followers can acquire immediately. Falls back to TTL expiry if delete fails (logged as Warning).

## Deviations from Plan

None -- plan executed exactly as written.

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` zero errors | PASS |
| K8sLeaseElection.cs exists in Telemetry/ | PASS |
| Implements BackgroundService + ILeaderElection | PASS |
| volatile bool _isLeader field | PASS |
| RunAndTryToHoldLeadershipForeverAsync used | PASS |
| StopAsync deletes lease when _isLeader | PASS |
| KubernetesClient 18.0.13 in csproj | PASS |
| IsInCluster() environment detection | PASS |
| GetRequiredService<K8sLeaseElection> for both interfaces | PASS |
| AlwaysLeaderElection preserved in else branch | PASS |

## Commit History

| Task | Commit | Description |
|------|--------|-------------|
| 1 | 3a4e962 | feat(08-01): add K8sLeaseElection with KubernetesClient LeaderElector |
| 2 | 14cb5d9 | feat(08-01): add environment-based leader election DI registration |

## Next Phase Readiness

Plan 08-02 can proceed immediately. It will wire the role-gated OTLP metrics/tracing exporters that consume the `ILeaderElection` instance provided by this plan. The `K8sLeaseElection` (production) and `AlwaysLeaderElection` (local dev) both satisfy the same interface contract.
