# Requirements: Simetra v1.0 (Extension)

**Defined:** 2026-02-16
**Core Value:** The SNMP pipeline must reliably receive traps, poll devices, extract data, and emit telemetry to OTLP -- with automatic leader-follower failover ensuring no single point of failure.

**Context:** Extending v1.0 with trap channel consumers and two reference device modules (NPB, OBP) to demonstrate the full end-to-end pipeline and serve as templates for adding future devices.

## Metric Naming Convention (Project-Level)

Metric names are clean snake_case property names. Base labels provide all context — no MetricName prefix needed.

**Pattern:** `{property_name} { site, device_name, device_ip, device_type }`

**Examples:**
```
port_link_status  { site="site-lab-01", device_name="npb-lab-01", device_ip="10.0.1.10", device_type="npb" }
port_rx_octets    { site="site-lab-01", device_name="npb-lab-01", device_ip="10.0.1.10", device_type="npb" }
port_tx_octets    { site="site-lab-01", device_name="npb-lab-01", device_ip="10.0.1.10", device_type="npb" }
link_state        { site="site-lab-01", device_name="obp-lab-01", device_ip="10.0.1.20", device_type="obp" }
link_channel      { site="site-lab-01", device_name="obp-lab-01", device_ip="10.0.1.20", device_type="obp" }
r1_power          { site="site-lab-01", device_name="obp-lab-01", device_ip="10.0.1.20", device_type="obp" }
```

This refines the v1.0 `{MetricName}_{Property}` pattern — the PropertyName value alone IS the metric name. The `device_type` label disambiguates across device types.

## v1 Requirements

### Metric Naming (1)

- [ ] **METR-01**: PollDefinitionDto PropertyName value used directly as metric name (snake_case), no MetricName prefix — base labels (site, device_name, device_ip, device_type) provide context

### Trap Pipeline Completion (7)

- [ ] **TRAP-01**: ChannelConsumerService (BackgroundService) per device reads traps from Channel<TrapContext> via ReadAllAsync
- [ ] **TRAP-02**: Consumer passes each trap through composable middleware chain (correlationId, logging, error handling)
- [ ] **TRAP-03**: Consumer routes processed traps to SnmpExtractorService (Layer 3) for varbind extraction using device's trap PollDefinitionDto
- [ ] **TRAP-04**: Extracted trap data routes to MetricFactoryService (metrics) and StateVectorService (Source=Module traps only)
- [ ] **TRAP-05**: Consumer handles channel completion gracefully during shutdown (ReadAllAsync completes when channel writer is completed)
- [ ] **TRAP-06**: Consumer logs trap processing at structured log level with device name and trap OID context
- [ ] **TRAP-07**: End-to-end trap flow verified: listener → filter → channel → consumer → extractor → metrics + State Vector → OTLP export

### NPB Device Module (9)

- [ ] **NPB-01**: NpbModule implements IDeviceModule with device type "NPB"
- [ ] **NPB-02**: Trap definition for portLinkUp (notifications.101) with varbinds: module, severity, type, message, portsPortLogicalPortNumber, portsPortSpeed
- [ ] **NPB-03**: Trap definition for portLinkDown (notifications.102) with varbinds: module, severity, type, message, portsPortLogicalPortNumber
- [ ] **NPB-04**: Poll definition Source=Configuration for portStatisticsSummaryPortRxOctets → metric name `port_rx_octets` (Counter64)
- [ ] **NPB-05**: Poll definition Source=Configuration for portStatisticsSummaryPortTxOctets → metric name `port_tx_octets` (Counter64)
- [ ] **NPB-06**: Poll definition Source=Module for portStatisticsSummaryPortRxPackets → metric name `port_rx_packets` (Counter64)
- [ ] **NPB-07**: Poll definition Source=Module for portStatisticsSummaryPortTxPackets → metric name `port_tx_packets` (Counter64)
- [ ] **NPB-08**: EnumMap for LinkStatusType: unknown(-1), down(0), up(1), receiveDown(2), forcedDown(3) → metric name `port_link_status`
- [ ] **NPB-09**: NPB registered in device module registry with test device configuration in appsettings.json

### OBP Device Module (17)

- [ ] **OBP-01**: ObpModule implements IDeviceModule with device type "OBP"
- [ ] **OBP-02**: Trap definition for linkN_StateChange (linkNOBPTrap.2) with values bypass(0), primary(1)
- [ ] **OBP-03**: Trap definition for linkN_WorkModeChange (linkNOBPTrap.1) with values manualMode(0), autoMode(1)
- [ ] **OBP-04**: Trap definition for linkN_PowerAlarmBypass2Changed (linkNOBPTrap.19) with power alarm values
- [ ] **OBP-05**: Trap definitions for NMU-level traps: systemStartup (nmuTrap.1), cardStatusChanged (nmuTrap.2)
- [ ] **OBP-06**: Poll definition Source=Configuration for linkN_R1Power → metric name `r1_power` (DisplayString, optical power)
- [ ] **OBP-07**: Poll definition Source=Configuration for linkN_R2Power → metric name `r2_power` (DisplayString, optical power)
- [ ] **OBP-08**: Poll definition Source=Module for linkN_State → metric name `link_state` (INTEGER: off(0)/on(1))
- [ ] **OBP-09**: Poll definition Source=Module for linkN_Channel → metric name `link_channel` (INTEGER: bypass(0)/primary(1))
- [ ] **OBP-10**: Poll definition Source=Module for linkN_WorkMode → metric name `work_mode` (INTEGER: manualMode(0)/autoMode(1))
- [ ] **OBP-11**: Poll definition Source=Module for linkN_ActiveHeartStatus → metric name `active_heart_status` (INTEGER: alarm(0)/normal(1)/off(2))
- [ ] **OBP-12**: Poll definition Source=Module for linkN_PassiveHeartStatus → metric name `passive_heart_status` (INTEGER: alarm(0)/normal(1)/off(2))
- [ ] **OBP-13**: Poll definition Source=Module for linkN_PowerAlarmStatus → metric name `power_alarm_status` (INTEGER: off(0)/alarm(1))
- [ ] **OBP-14**: Poll definitions Source=Module for NMU-level: power1State → `power1_state`, power2State → `power2_state` (INTEGER: off(0)/on(1))
- [ ] **OBP-15**: EnumMaps for all INTEGER status fields (channel, workMode, heartStatus, powerAlarm, linkState, powerState)
- [ ] **OBP-16**: OBP registered in device module registry with test device configuration in appsettings.json
- [ ] **OBP-17**: Handle non-standard per-link OID structure (link1-link32 duplicated OIDs, not SNMP tables)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Additional device modules beyond NPB/OBP | v2.0 -- NPB + OBP serve as reference implementations |
| SNMPv3 support | v2c sufficient for demonstration |
| SNMP GETBULK/WALK for NPB tables | Simple GET sufficient for demonstration metrics |
| OBP links beyond link1 in configuration | link1 demonstrates the pattern; additional links are config, not code |
| State Vector staleness checking | Future milestone |
| Direct listener health check | Future milestone |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| METR-01 | Phase 11 | Pending |
| TRAP-01 | Phase 11 | Pending |
| TRAP-02 | Phase 11 | Pending |
| TRAP-03 | Phase 11 | Pending |
| TRAP-04 | Phase 11 | Pending |
| TRAP-05 | Phase 11 | Pending |
| TRAP-06 | Phase 11 | Pending |
| TRAP-07 | Phase 11 | Pending |
| NPB-01 | Phase 12 | Pending |
| NPB-02 | Phase 12 | Pending |
| NPB-03 | Phase 12 | Pending |
| NPB-04 | Phase 12 | Pending |
| NPB-05 | Phase 12 | Pending |
| NPB-06 | Phase 12 | Pending |
| NPB-07 | Phase 12 | Pending |
| NPB-08 | Phase 12 | Pending |
| NPB-09 | Phase 12 | Pending |
| OBP-01 | Phase 13 | Pending |
| OBP-02 | Phase 13 | Pending |
| OBP-03 | Phase 13 | Pending |
| OBP-04 | Phase 13 | Pending |
| OBP-05 | Phase 13 | Pending |
| OBP-06 | Phase 13 | Pending |
| OBP-07 | Phase 13 | Pending |
| OBP-08 | Phase 13 | Pending |
| OBP-09 | Phase 13 | Pending |
| OBP-10 | Phase 13 | Pending |
| OBP-11 | Phase 13 | Pending |
| OBP-12 | Phase 13 | Pending |
| OBP-13 | Phase 13 | Pending |
| OBP-14 | Phase 13 | Pending |
| OBP-15 | Phase 13 | Pending |
| OBP-16 | Phase 13 | Pending |
| OBP-17 | Phase 13 | Pending |

**Coverage:**
- Extension requirements: 34 total
- Mapped to phases: 34
- Unmapped: 0

---
*Requirements defined: 2026-02-16*
*Last updated: 2026-02-16 after v1.0 scope extension*
