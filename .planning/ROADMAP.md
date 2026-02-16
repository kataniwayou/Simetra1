# Roadmap: Simetra v1.0 (Extension)

**Created:** 2026-02-16
**Milestone:** v1.0 (continuing from phase 10)
**Phases:** 11-13

## Overview

Complete the v1.0 SNMP supervisor with trap channel consumers and two reference device modules (NPB, OBP). These serve as demonstration/templates for adding future device types, showing both standard and non-standard SNMP MIB patterns.

| Phase | Name | Goal | Requirements | Plans (est) |
|-------|------|------|-------------|-------------|
| 11 | Trap Channel Consumers | Complete the trap pipeline — read from channels, process through middleware, extract, emit telemetry. Establish metric naming convention. | METR-01, TRAP-01..07 | 3 |
| 12 | NPB Device Module | Reference implementation for standard SNMP device (NOTIFICATION-TYPE traps, table-based stats) | NPB-01..09 | 2 |
| 13 | OBP Device Module | Reference implementation for non-standard SNMP device (per-link OIDs, OBJECT-TYPE traps) | OBP-01..17 | 2-3 |

## Phase Details

### Phase 11: Trap Channel Consumers

**Goal:** Complete the trap pipeline by adding channel consumers that read traps, drive them through the middleware chain, extract data via Layer 3, and route to Layer 4 processing (metrics + State Vector). Establish the metric naming convention: PropertyName value as metric name (snake_case), base labels provide context.

**Requirements:** METR-01, TRAP-01, TRAP-02, TRAP-03, TRAP-04, TRAP-05, TRAP-06, TRAP-07

**Success Criteria:**
1. Metric naming convention implemented: PropertyName value used directly as metric name (e.g., `port_rx_octets`), no MetricName prefix — base labels (site, device_name, device_ip, device_type) provide context
2. ChannelConsumerService starts as BackgroundService and reads from device channel via ReadAllAsync
3. Each trap passes through middleware chain (correlationId, logging, error handling) before extraction
4. Extracted trap data produces OTLP metrics (all traps) and updates State Vector (Source=Module traps only)
5. Consumer shuts down gracefully when channel writer completes — no orphan tasks or lost traps
6. End-to-end test: simulated trap → listener → filter → channel → consumer → extractor → metrics + State Vector

**Plans:** 3 plans

Plans:
- [ ] 11-01-PLAN.md -- METR-01 metric naming convention (PropertyName as metric name)
- [ ] 11-02-PLAN.md -- ChannelConsumerService + middleware + DI registration + unit tests
- [ ] 11-03-PLAN.md -- End-to-end trap consumer flow integration test (TRAP-07)

**Dependencies:** None (builds on existing v1.0 infrastructure)

**Key context:**
- Existing channel infrastructure: Channel<TrapContext> per device, bounded, DropOldest with itemDropped callback
- Existing middleware chain: proven with poll pipeline, same pattern applies to trap consumption
- Existing extractor: SnmpExtractorService handles PollDefinitionDto-based extraction generically
- Consumer registration: must integrate with IHostedService ordering (after channels, before shutdown)

---

### Phase 12: NPB Device Module

**Goal:** Implement NpbModule as the reference implementation for a standard SNMP device — proper NOTIFICATION-TYPE traps, table-based port statistics. Demonstrates how to add a new device module following the IDeviceModule pattern.

**Requirements:** NPB-01, NPB-02, NPB-03, NPB-04, NPB-05, NPB-06, NPB-07, NPB-08, NPB-09

**Success Criteria:**
1. NpbModule implements IDeviceModule and registers in the device module registry
2. portLinkUp and portLinkDown trap definitions match NPB-TRAPS.mib OIDs and varbinds
3. Configuration polls (RxOctets, TxOctets) produce metrics only — no State Vector update
4. Module polls (RxPackets, TxPackets) produce metrics AND update State Vector
5. EnumMap for LinkStatusType resolves integer values to human-readable names
6. NPB device configured in appsettings.json with IP, community string, poll intervals

**Dependencies:** Phase 11 (trap consumers needed for trap definitions to be meaningful end-to-end)

**Key context:**
- Enterprise OID: 1.3.6.1.4.1.47477 (CGS) → npb(100) → npb-2e(4)
- Traps under npb-2e.10.2 (notificationsMib.notifications)
- Port statistics under npb-2e.2.2.5 (portsMib.portStatistics.portStatisticsSummary)
- MIB files at NPB/mibs/ — NPB-TRAPS.mib, NPB-PORTS.mib, CGS.mib, NPB-NPB.mib, NPB-2E.mib

---

### Phase 13: OBP Device Module

**Goal:** Implement ObpModule as the reference implementation for a non-standard SNMP device — per-link duplicated OIDs instead of tables, OBJECT-TYPE trap definitions instead of NOTIFICATION-TYPE. Demonstrates handling vendor MIB quirks.

**Requirements:** OBP-01, OBP-02, OBP-03, OBP-04, OBP-05, OBP-06, OBP-07, OBP-08, OBP-09, OBP-10, OBP-11, OBP-12, OBP-13, OBP-14, OBP-15, OBP-16, OBP-17

**Success Criteria:**
1. ObpModule implements IDeviceModule and registers in the device module registry
2. Trap definitions handle non-standard OID format (linkNOBPTrap subtree, OBJECT-TYPE not NOTIFICATION-TYPE)
3. Per-link OID pattern (link1OBP, link2OBP, ...) handled — configurable link number
4. Configuration polls (R1Power, R2Power) produce optical power metrics
5. Module polls produce State Vector entries for link state, channel, work mode, heartbeat status, power alarm
6. EnumMaps for all INTEGER status fields resolve to human-readable values
7. OBP device configured in appsettings.json with IP, community string, link number, poll intervals

**Dependencies:** Phase 11 (trap consumers), Phase 12 (NPB establishes the module pattern first)

**Key context:**
- Enterprise OID: 1.3.6.1.4.1.47477 (CGS) → EBP-1U2U4U(10) → bypass(21)
- Per-link structure: bypass.N → linkN → linkN.3 (linkNOBP) → linkNOBP.50 (linkNOBPTrap)
- NMU (device-level): bypass.60 → nmu (system info, power states)
- NMU traps: nmu.50 → nmuTrap (systemStartup, cardStatusChanged)
- MIB file at V5.2.4/BYPASS-CGS.mib (821KB, repetitive per-link structure)
- Non-standard: traps defined as OBJECT-TYPE under trap subtree, not NOTIFICATION-TYPE

---

## Requirement Coverage

| Category | Count | Phase |
|----------|-------|-------|
| TRAP | 7 | 11 |
| NPB | 9 | 12 |
| OBP | 17 | 13 |
| **Total** | **33** | **3 phases** |

Unmapped: 0

---
*Roadmap created: 2026-02-16*
*Last updated: 2026-02-16 (Phase 11 planned)*
