# OTS3000-BPS (Optical Bypass Switch) — SNMP Analysis

> **MIB File:** `BYPASS-CGS.mib`
> **MIB Module:** `OTS3000-BPS-8L`
> **Enterprise OID:** `1.3.6.1.4.1.47477` (cgs / GLSUN Corporation)
> **Device Base OID:** `1.3.6.1.4.1.47477.10.21` (EBP-1U2U4U → bypass)

---

## 1. What Is the OBP Device?

The OTS3000-BPS is an **Optical Bypass Switch** that sits inline on fiber links between the network and security/monitoring tools (IDS, IPS, firewalls, etc.). Its purpose is to guarantee network continuity: if an inline tool fails, the OBP automatically reroutes traffic through a **bypass path**, skipping the failed tool so the network stays up.

```
Normal:   Network ──► Inline Tool ──► Network   (primary channel)
Failure:  Network ─────────────────► Network   (bypass channel, tool skipped)
```

### Device Models

| Model | Channels | Receivers | Wavelengths | Notes |
|-------|----------|-----------|-------------|-------|
| **OBP2** | 2 | R1, R2 | 1310nm / 1550nm | Single-fiber pair per receiver |
| **OBP4** | 4 | R1, R2, R3, R4 | 1310nm / 1550nm | Four-receiver, single-fiber |
| **OBP8** | 8 | 1R1–1R4, 2R1–2R4 | 850nm | Dual-receiver (multimode) |

### Architecture

The device manages **32 independent optical bypass links** (link1–link32). Each link has its own OID branch with an identical set of metrics, traps, and commands. Above the links sits the **NMU (Network Management Unit)** layer for device-level settings.

---

## 2. OID Hierarchy

```
1.3.6.1.4.1.47477                          ← enterprises → cgs
  └── 10                                    ← EBP-1U2U4U
       └── 21                               ← bypass
            ├── 1                            ← link1
            │    └── 3                       ← link1OBP (polled metrics & commands)
            │         ├── .1–.68             ← individual metric/command OIDs
            │         └── 50                 ← link1OBPTrap
            │              └── .1–.33        ← individual trap OIDs
            ├── 2                            ← link2
            │    └── 3                       ← link2OBP
            │         └── 50                 ← link2OBPTrap
            ├── ...                          ← link3 through link31
            ├── 32                           ← link32
            │    └── 3                       ← link32OBP
            │         └── 50                 ← link32OBPTrap
            └── 60                           ← nmu (device-level)
                 ├── .1–.16                  ← NMU metrics & commands
                 └── 50                      ← nmuTrap
                      └── .1–.2              ← NMU trap OIDs
```

**OID Pattern for any link N:**
- Polled metrics / commands: `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`
- Traps: `1.3.6.1.4.1.47477.10.21.{N}.3.50.{suffix}`

---

## 3. SNMP Traps (Asynchronous Notifications)

Traps are **push-based** — the device sends them to a configured SNMP manager when an event occurs. No polling required.

**Total: 2 NMU-level + 33 per-link x 32 links = 1,058 trap instances**

### 3.1 NMU-Level Traps

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.50.{suffix}`

| # | Trap Name | Full OID | Type | Description |
|---|-----------|----------|------|-------------|
| 1 | `systemStartup` | `1.3.6.1.4.1.47477.10.21.60.50.1` | DisplayString | System startup notification |
| 2 | `cardStatusChanged` | `1.3.6.1.4.1.47477.10.21.60.50.2` | DisplayString | Card/module status alarm |

### 3.2 Per-Link Traps (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.50.{suffix}`

> Every trap below is replicated for link1 through link32. The table shows the generic pattern and a concrete **link1** example OID.

#### Mode & State Change Traps

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 1 | `linkN_WorkModeChange` | `.1` | `1.3.6.1.4.1.47477.10.21.1.3.50.1` | INTEGER: manualMode(0), autoMode(1) | Link mode changed (manual/auto) |
| 2 | `linkN_StateChange` | `.2` | `1.3.6.1.4.1.47477.10.21.1.3.50.2` | INTEGER: bypass(0), primary(1) | Link channel changed (bypass/primary) |

#### Wavelength Change Traps — OBP2/OBP4

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 3 | `linkN_R1WaveSet` | `.3` | `1.3.6.1.4.1.47477.10.21.1.3.50.3` | INTEGER: w1310nm(0), w1550nm(1) | R1 wavelength changed |
| 4 | `linkN_R2WaveSet` | `.4` | `1.3.6.1.4.1.47477.10.21.1.3.50.4` | INTEGER: w1310nm(0), w1550nm(1) | R2 wavelength changed |
| 5 | `linkN_R3WaveSet` | `.5` | `1.3.6.1.4.1.47477.10.21.1.3.50.5` | INTEGER: w1310nm(0), w1550nm(1) | R3 wavelength changed |
| 6 | `linkN_R4WaveSet` | `.6` | `1.3.6.1.4.1.47477.10.21.1.3.50.6` | INTEGER: w1310nm(0), w1550nm(1) | R4 wavelength changed |

#### Alarm Threshold Change Traps — OBP2/OBP4

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 7 | `linkN_R1AlarmSet` | `.7` | `1.3.6.1.4.1.47477.10.21.1.3.50.7` | DisplayString | R1 alarm threshold changed |
| 8 | `linkN_R2AlarmSet` | `.8` | `1.3.6.1.4.1.47477.10.21.1.3.50.8` | DisplayString | R2 alarm threshold changed |
| 9 | `linkN_R3AlarmSet` | `.9` | `1.3.6.1.4.1.47477.10.21.1.3.50.9` | DisplayString | R3 alarm threshold changed |
| 10 | `linkN_R4AlarmSet` | `.10` | `1.3.6.1.4.1.47477.10.21.1.3.50.10` | DisplayString | R4 alarm threshold changed |

#### Alarm Threshold Change Traps — OBP8

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 11 | `linkN_1R1AlarmSet` | `.11` | `1.3.6.1.4.1.47477.10.21.1.3.50.11` | DisplayString | 1R1 alarm threshold changed |
| 12 | `linkN_2R1AlarmSet` | `.12` | `1.3.6.1.4.1.47477.10.21.1.3.50.12` | DisplayString | 2R1 alarm threshold changed |
| 13 | `linkN_1R2AlarmSet` | `.13` | `1.3.6.1.4.1.47477.10.21.1.3.50.13` | DisplayString | 1R2 alarm threshold changed |
| 14 | `linkN_2R2AlarmSet` | `.14` | `1.3.6.1.4.1.47477.10.21.1.3.50.14` | DisplayString | 2R2 alarm threshold changed |
| 15 | `linkN_1R3AlarmSet` | `.15` | `1.3.6.1.4.1.47477.10.21.1.3.50.15` | DisplayString | 1R3 alarm threshold changed |
| 16 | `linkN_2R3AlarmSet` | `.16` | `1.3.6.1.4.1.47477.10.21.1.3.50.16` | DisplayString | 2R3 alarm threshold changed |
| 17 | `linkN_1R4AlarmSet` | `.17` | `1.3.6.1.4.1.47477.10.21.1.3.50.17` | DisplayString | 1R4 alarm threshold changed |
| 18 | `linkN_2R4AlarmSet` | `.18` | `1.3.6.1.4.1.47477.10.21.1.3.50.18` | DisplayString | 2R4 alarm threshold changed |

#### Bypass Configuration Change Traps

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 19 | `linkN_PowerAlarmBypass2Changed` | `.19` | `1.3.6.1.4.1.47477.10.21.1.3.50.19` | INTEGER: off(0), powerAlarmR1(1), powerAlarmR2(2), anyAlarmR1-R2(3), allAlarmR1-R2(4) | OBP2 bypass trigger config changed |
| 20 | `linkN_PowerAlarmBypass4Changed` | `.20` | `1.3.6.1.4.1.47477.10.21.1.3.50.20` | INTEGER: off(0), powerAlarmR1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6) | OBP4 bypass trigger config changed |
| 21 | `linkN_PowerAlarmBypass8Changed` | `.21` | `1.3.6.1.4.1.47477.10.21.1.3.50.21` | INTEGER: off(0), powerAlarm1R1(1)–2R4(8), anyAlarm1R1-2R4(9), allAlarm1R1-2R4(10) | OBP8 bypass trigger config changed |

#### Power Status Alarm Traps — OBP2/OBP4

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 22 | `linkN_powerAlarmR1` | `.22` | `1.3.6.1.4.1.47477.10.21.1.3.50.22` | DisplayString | R1 power crossed alarm threshold |
| 23 | `linkN_powerAlarmR2` | `.23` | `1.3.6.1.4.1.47477.10.21.1.3.50.23` | DisplayString | R2 power crossed alarm threshold |
| 24 | `linkN_powerAlarmR3` | `.24` | `1.3.6.1.4.1.47477.10.21.1.3.50.24` | DisplayString | R3 power crossed alarm threshold |
| 25 | `linkN_powerAlarmR4` | `.25` | `1.3.6.1.4.1.47477.10.21.1.3.50.25` | DisplayString | R4 power crossed alarm threshold |

#### Power Status Alarm Traps — OBP8

| # | Trap Name | Suffix | Link1 Example OID | Type | Description |
|---|-----------|--------|-------------------|------|-------------|
| 26 | `linkN_powerAlarm1R1` | `.26` | `1.3.6.1.4.1.47477.10.21.1.3.50.26` | DisplayString | 1R1 power status alarm |
| 27 | `linkN_powerAlarm2R1` | `.27` | `1.3.6.1.4.1.47477.10.21.1.3.50.27` | DisplayString | 2R1 power status alarm |
| 28 | `linkN_powerAlarm1R2` | `.28` | `1.3.6.1.4.1.47477.10.21.1.3.50.28` | DisplayString | 1R2 power status alarm |
| 29 | `linkN_powerAlarm2R2` | `.29` | `1.3.6.1.4.1.47477.10.21.1.3.50.29` | DisplayString | 2R2 power status alarm |
| 30 | `linkN_powerAlarm1R3` | `.30` | `1.3.6.1.4.1.47477.10.21.1.3.50.30` | DisplayString | 1R3 power status alarm |
| 31 | `linkN_powerAlarm2R3` | `.31` | `1.3.6.1.4.1.47477.10.21.1.3.50.31` | DisplayString | 2R3 power status alarm |
| 32 | `linkN_powerAlarm1R4` | `.32` | `1.3.6.1.4.1.47477.10.21.1.3.50.32` | DisplayString | 1R4 power status alarm |
| 33 | `linkN_powerAlarm2R4` | `.33` | `1.3.6.1.4.1.47477.10.21.1.3.50.33` | DisplayString | 2R4 power status alarm |

---

## 4. Polled Metrics (SNMP GET — Pull-Based)

These are values you **actively query** from the device. The same OIDs that are `read-write` also appear in the Commands section (Section 5).

**Total: 16 NMU-level + 58 per-link x 32 links = 1,872 metric instances**

### 4.1 NMU (Device-Level) Metrics

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.{suffix}`

| # | Metric Name | Suffix | Full OID | Type | Access | Description |
|---|-------------|--------|----------|------|--------|-------------|
| 1 | `deviceType` | `.1` | `1.3.6.1.4.1.47477.10.21.60.1` | DisplayString | read-only | Device type identifier |
| 2 | `ipAddress` | `.2` | `1.3.6.1.4.1.47477.10.21.60.2` | IpAddress | read-write | Device IP address |
| 3 | `subnetMask` | `.3` | `1.3.6.1.4.1.47477.10.21.60.3` | IpAddress | read-write | Subnet mask |
| 4 | `gateWay` | `.4` | `1.3.6.1.4.1.47477.10.21.60.4` | IpAddress | read-write | Default gateway |
| 5 | `macAddress` | `.5` | `1.3.6.1.4.1.47477.10.21.60.5` | DisplayString | read-only | MAC address |
| 6 | `tcpPort` | `.6` | `1.3.6.1.4.1.47477.10.21.60.6` | Integer32 | read-write | TCP port |
| 7 | `startDelay` | `.7` | `1.3.6.1.4.1.47477.10.21.60.7` | Integer32 | read-write | Start delay (seconds) |
| 8 | `keyLock` | `.8` | `1.3.6.1.4.1.47477.10.21.60.8` | INTEGER: lock(0), unlock(1) | read-write | Keyboard lock status |
| 9 | `buzzerSet` | `.9` | `1.3.6.1.4.1.47477.10.21.60.9` | INTEGER: off(0), on(1) | read-write | Buzzer on/off |
| 10 | `deviceAddress` | `.10` | `1.3.6.1.4.1.47477.10.21.60.10` | Integer32 | read-write | Device address |
| 11 | `power1State` | `.11` | `1.3.6.1.4.1.47477.10.21.60.11` | INTEGER: off(0), on(1) | read-only | Power supply 1 status |
| 12 | `power2State` | `.12` | `1.3.6.1.4.1.47477.10.21.60.12` | INTEGER: off(0), on(1) | read-only | Power supply 2 status |
| 13 | `softwareVersion` | `.13` | `1.3.6.1.4.1.47477.10.21.60.13` | DisplayString | read-only | Software version |
| 14 | `hardwareVersion` | `.14` | `1.3.6.1.4.1.47477.10.21.60.14` | DisplayString | read-only | Hardware version |
| 15 | `serialNumber` | `.15` | `1.3.6.1.4.1.47477.10.21.60.15` | DisplayString | read-only | Serial number |
| 16 | `manufacturingdate` | `.16` | `1.3.6.1.4.1.47477.10.21.60.16` | DisplayString | read-only | Manufacturing date |

### 4.2 Per-Link Metrics (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`

> Every metric below is replicated for link1 through link32. The table shows the OID suffix within `linkNOBP` and a concrete **link1** example.

#### State & Configuration

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 1 | `linkN_State` | `.1` | `1.3.6.1.4.1.47477.10.21.1.3.1` | INTEGER: off(0), on(1) | read-only | Link working status |
| 2 | `linkN_DeviceType` | `.2` | `1.3.6.1.4.1.47477.10.21.1.3.2` | DisplayString | read-only | Card type in this link slot |
| 3 | `linkN_WorkMode` | `.3` | `1.3.6.1.4.1.47477.10.21.1.3.3` | INTEGER: manualMode(0), autoMode(1) | read-write | Work mode (manual/auto) |
| 4 | `linkN_Channel` | `.4` | `1.3.6.1.4.1.47477.10.21.1.3.4` | INTEGER: bypass(0), primary(1) | read-write | Active channel |

#### Optical Power — OBP2/OBP4 (R1–R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 5 | `linkN_R1Power` | `.5` | `1.3.6.1.4.1.47477.10.21.1.3.5` | DisplayString (dBm) | read-only | R1 optical power |
| 6 | `linkN_R2Power` | `.6` | `1.3.6.1.4.1.47477.10.21.1.3.6` | DisplayString (dBm) | read-only | R2 optical power |
| 7 | `linkN_R3Power` | `.35` | `1.3.6.1.4.1.47477.10.21.1.3.35` | DisplayString (dBm) | read-only | R3 optical power |
| 8 | `linkN_R4Power` | `.36` | `1.3.6.1.4.1.47477.10.21.1.3.36` | DisplayString (dBm) | read-only | R4 optical power |

#### Optical Power — OBP8 (1R1–2R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 9 | `linkN_1R1Power` | `.37` | `1.3.6.1.4.1.47477.10.21.1.3.37` | DisplayString (dBm) | read-only | 1R1 optical power |
| 10 | `linkN_1R2Power` | `.38` | `1.3.6.1.4.1.47477.10.21.1.3.38` | DisplayString (dBm) | read-only | 1R2 optical power |
| 11 | `linkN_1R3Power` | `.39` | `1.3.6.1.4.1.47477.10.21.1.3.39` | DisplayString (dBm) | read-only | 1R3 optical power |
| 12 | `linkN_1R4Power` | `.40` | `1.3.6.1.4.1.47477.10.21.1.3.40` | DisplayString (dBm) | read-only | 1R4 optical power |
| 13 | `linkN_2R1Power` | `.41` | `1.3.6.1.4.1.47477.10.21.1.3.41` | DisplayString (dBm) | read-only | 2R1 optical power |
| 14 | `linkN_2R2Power` | `.42` | `1.3.6.1.4.1.47477.10.21.1.3.42` | DisplayString (dBm) | read-only | 2R2 optical power |
| 15 | `linkN_2R3Power` | `.43` | `1.3.6.1.4.1.47477.10.21.1.3.43` | DisplayString (dBm) | read-only | 2R3 optical power |
| 16 | `linkN_2R4Power` | `.44` | `1.3.6.1.4.1.47477.10.21.1.3.44` | DisplayString (dBm) | read-only | 2R4 optical power |

#### Wavelength Configuration — OBP2/OBP4 (R1–R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 17 | `linkN_R1Wave` | `.7` | `1.3.6.1.4.1.47477.10.21.1.3.7` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R1 wavelength |
| 18 | `linkN_R2Wave` | `.8` | `1.3.6.1.4.1.47477.10.21.1.3.8` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R2 wavelength |
| 19 | `linkN_R3Wave` | `.27` | `1.3.6.1.4.1.47477.10.21.1.3.27` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R3 wavelength |
| 20 | `linkN_R4Wave` | `.28` | `1.3.6.1.4.1.47477.10.21.1.3.28` | INTEGER: w1310nm(0), w1550nm(1), na(2) | read-write | R4 wavelength |

#### Wavelength — OBP8 (1R1–2R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 21 | `linkN_1R1Wave` | `.49` | `1.3.6.1.4.1.47477.10.21.1.3.49` | INTEGER: w850nm(0), na(2) | read-only | 1R1 wavelength |
| 22 | `linkN_1R2Wave` | `.60` | `1.3.6.1.4.1.47477.10.21.1.3.60` | INTEGER: w850nm(0), na(2) | read-only | 1R2 wavelength |
| 23 | `linkN_1R3Wave` | `.61` | `1.3.6.1.4.1.47477.10.21.1.3.61` | INTEGER: w850nm(0), na(2) | read-only | 1R3 wavelength |
| 24 | `linkN_1R4Wave` | `.62` | `1.3.6.1.4.1.47477.10.21.1.3.62` | INTEGER: w850nm(0), na(2) | read-only | 1R4 wavelength |
| 25 | `linkN_2R1Wave` | `.63` | `1.3.6.1.4.1.47477.10.21.1.3.63` | INTEGER: w850nm(0), na(2) | read-only | 2R1 wavelength |
| 26 | `linkN_2R2Wave` | `.64` | `1.3.6.1.4.1.47477.10.21.1.3.64` | INTEGER: w850nm(0), na(2) | read-only | 2R2 wavelength |
| 27 | `linkN_2R3Wave` | `.65` | `1.3.6.1.4.1.47477.10.21.1.3.65` | INTEGER: w850nm(0), na(2) | read-only | 2R3 wavelength |
| 28 | `linkN_2R4Wave` | `.66` | `1.3.6.1.4.1.47477.10.21.1.3.66` | INTEGER: w850nm(0), na(2) | read-only | 2R4 wavelength |

#### Alarm Thresholds — OBP2/OBP4 (R1–R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 29 | `linkN_R1AlarmPower` | `.9` | `1.3.6.1.4.1.47477.10.21.1.3.9` | DisplayString (dBm) | read-write | R1 power alarm threshold |
| 30 | `linkN_R2AlarmPower` | `.10` | `1.3.6.1.4.1.47477.10.21.1.3.10` | DisplayString (dBm) | read-write | R2 power alarm threshold |
| 31 | `linkN_R3AlarmPower` | `.29` | `1.3.6.1.4.1.47477.10.21.1.3.29` | DisplayString (dBm) | read-write | R3 power alarm threshold |
| 32 | `linkN_R4AlarmPower` | `.30` | `1.3.6.1.4.1.47477.10.21.1.3.30` | DisplayString (dBm) | read-write | R4 power alarm threshold |

#### Alarm Thresholds — OBP8 (1R1–2R4)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 33 | `linkN_1R1AlarmPower` | `.31` | `1.3.6.1.4.1.47477.10.21.1.3.31` | DisplayString (dBm) | read-write | 1R1 power alarm threshold |
| 34 | `linkN_1R2AlarmPower` | `.32` | `1.3.6.1.4.1.47477.10.21.1.3.32` | DisplayString (dBm) | read-write | 1R2 power alarm threshold |
| 35 | `linkN_1R3AlarmPower` | `.33` | `1.3.6.1.4.1.47477.10.21.1.3.33` | DisplayString (dBm) | read-write | 1R3 power alarm threshold |
| 36 | `linkN_1R4AlarmPower` | `.34` | `1.3.6.1.4.1.47477.10.21.1.3.34` | DisplayString (dBm) | read-write | 1R4 power alarm threshold |
| 37 | `linkN_2R1AlarmPower` | `.45` | `1.3.6.1.4.1.47477.10.21.1.3.45` | DisplayString (dBm) | read-write | 2R1 power alarm threshold |
| 38 | `linkN_2R2AlarmPower` | `.46` | `1.3.6.1.4.1.47477.10.21.1.3.46` | DisplayString (dBm) | read-write | 2R2 power alarm threshold |
| 39 | `linkN_2R3AlarmPower` | `.47` | `1.3.6.1.4.1.47477.10.21.1.3.47` | DisplayString (dBm) | read-write | 2R3 power alarm threshold |
| 40 | `linkN_2R4AlarmPower` | `.48` | `1.3.6.1.4.1.47477.10.21.1.3.48` | DisplayString (dBm) | read-write | 2R4 power alarm threshold |

#### Bypass Modes & Failover Configuration

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 41 | `linkN_PowerAlarmBypass2` | `.11` | `1.3.6.1.4.1.47477.10.21.1.3.11` | INTEGER: off(0), powerAlarmR1(1), powerAlarmR2(2), anyAlarmR1-R2(3), allAlarmR1-R2(4), na(5) | read-write | OBP2 bypass trigger mode |
| 42 | `linkN_PowerAlarmBypass4` | `.67` | `1.3.6.1.4.1.47477.10.21.1.3.67` | INTEGER: off(0), powerAlarmR1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6), na(7) | read-write | OBP4 bypass trigger mode |
| 43 | `linkN_PowerAlarmBypass8` | `.68` | `1.3.6.1.4.1.47477.10.21.1.3.68` | INTEGER: off(0), powerAlarm1R1(1), 1R2(3), 1R3(5), 1R4(7), 2R1(2), 2R2(4), 2R3(6), 2R4(8), anyAlarm1R1-2R4(9), allAlarm1R1-2R4(10), na(11) | read-write | OBP8 bypass trigger mode |
| 44 | `linkN_ReturnDelay` | `.12` | `1.3.6.1.4.1.47477.10.21.1.3.12` | Integer32 (seconds) | read-write | Auto-return delay |
| 45 | `linkN_BackMode` | `.13` | `1.3.6.1.4.1.47477.10.21.1.3.13` | INTEGER: autoNoBack(0), autoBack(1) | read-write | Back mode |
| 46 | `linkN_BackDelay` | `.14` | `1.3.6.1.4.1.47477.10.21.1.3.14` | Integer32 (seconds) | read-write | Back delay |
| 47 | `linkN_SwitchProtect` | `.23` | `1.3.6.1.4.1.47477.10.21.1.3.23` | INTEGER: off(0), on(1) | read-write | Switch protection (anti-flap) |

#### Active Heartbeat Configuration

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 48 | `linkN_ActiveHeartSwitch` | `.15` | `1.3.6.1.4.1.47477.10.21.1.3.15` | INTEGER: off(0), on(1) | read-write | Active heartbeat on/off |
| 49 | `linkN_ActiveSendInterval` | `.16` | `1.3.6.1.4.1.47477.10.21.1.3.16` | Integer32 (ms) | read-write | Active HB send interval |
| 50 | `linkN_ActiveTimeOut` | `.17` | `1.3.6.1.4.1.47477.10.21.1.3.17` | Integer32 (ms) | read-write | Active HB timeout |
| 51 | `linkN_ActiveLossBypass` | `.18` | `1.3.6.1.4.1.47477.10.21.1.3.18` | Integer32 | read-write | Consecutive losses before bypass |
| 52 | `linkN_PingIpAddress` | `.19` | `1.3.6.1.4.1.47477.10.21.1.3.19` | IpAddress | read-write | Ping target IP for heartbeat |

#### Passive Heartbeat Configuration

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 53 | `linkN_PassiveHeartSwitch` | `.20` | `1.3.6.1.4.1.47477.10.21.1.3.20` | INTEGER: off(0), on(1) | read-write | Passive heartbeat on/off |
| 54 | `linkN_PassiveTimeOut` | `.21` | `1.3.6.1.4.1.47477.10.21.1.3.21` | Integer32 (ms) | read-write | Passive HB timeout |
| 55 | `linkN_PassiveLossBypass` | `.22` | `1.3.6.1.4.1.47477.10.21.1.3.22` | Integer32 | read-write | Passive HB loss bypass threshold |

#### Status Indicators (read-only)

| # | Metric Name | Suffix | Link1 Example OID | Type | Access | Description |
|---|-------------|--------|-------------------|------|--------|-------------|
| 56 | `linkN_ActiveHeartStatus` | `.24` | `1.3.6.1.4.1.47477.10.21.1.3.24` | INTEGER: alarm(0), normal(1), off(2), na(3) | read-only | Active heartbeat health |
| 57 | `linkN_PassiveHeartStatus` | `.25` | `1.3.6.1.4.1.47477.10.21.1.3.25` | INTEGER: alarm(0), normal(1), off(2), na(3) | read-only | Passive heartbeat health |
| 58 | `linkN_PowerAlarmStatus` | `.26` | `1.3.6.1.4.1.47477.10.21.1.3.26` | INTEGER: off(0), alarm(1), normal(2), na(3) | read-only | Power alarm status |

---

## 5. SNMP Commands (SET Operations)

Commands are SNMP SET operations — you write values to change device behavior. Every `read-write` metric from Section 4 is also a command.

**Total: 8 NMU-level + 33 per-link x 32 links = 1,064 command instances**

### 5.1 NMU-Level Commands

**OID Path:** `1.3.6.1.4.1.47477.10.21.60.{suffix}`

| # | Command Name | Full OID | Type | Description |
|---|-------------|----------|------|-------------|
| 1 | `ipAddress` | `1.3.6.1.4.1.47477.10.21.60.2` | IpAddress | Set device IP address |
| 2 | `subnetMask` | `1.3.6.1.4.1.47477.10.21.60.3` | IpAddress | Set subnet mask |
| 3 | `gateWay` | `1.3.6.1.4.1.47477.10.21.60.4` | IpAddress | Set default gateway |
| 4 | `tcpPort` | `1.3.6.1.4.1.47477.10.21.60.6` | Integer32 | Set TCP port |
| 5 | `startDelay` | `1.3.6.1.4.1.47477.10.21.60.7` | Integer32 | Set start delay (seconds) |
| 6 | `keyLock` | `1.3.6.1.4.1.47477.10.21.60.8` | INTEGER: lock(0), unlock(1) | Lock/unlock front panel |
| 7 | `buzzerSet` | `1.3.6.1.4.1.47477.10.21.60.9` | INTEGER: off(0), on(1) | Enable/disable buzzer |
| 8 | `deviceAddress` | `1.3.6.1.4.1.47477.10.21.60.10` | Integer32 | Set device address |

### 5.2 Per-Link Commands (x32 links)

**OID Path:** `1.3.6.1.4.1.47477.10.21.{N}.3.{suffix}`

> Each command below is replicated for link1–link32. Link1 example OIDs shown.

#### Work Mode & Channel

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 1 | `linkN_WorkMode` | `.3` | `1.3.6.1.4.1.47477.10.21.1.3.3` | INTEGER: manualMode(0), autoMode(1) | Set manual or auto mode |
| 2 | `linkN_Channel` | `.4` | `1.3.6.1.4.1.47477.10.21.1.3.4` | INTEGER: bypass(0), primary(1) | Force bypass or primary channel |

#### Wavelength Configuration — OBP2/OBP4

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 3 | `linkN_R1Wave` | `.7` | `1.3.6.1.4.1.47477.10.21.1.3.7` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R1 wavelength |
| 4 | `linkN_R2Wave` | `.8` | `1.3.6.1.4.1.47477.10.21.1.3.8` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R2 wavelength |
| 5 | `linkN_R3Wave` | `.27` | `1.3.6.1.4.1.47477.10.21.1.3.27` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R3 wavelength |
| 6 | `linkN_R4Wave` | `.28` | `1.3.6.1.4.1.47477.10.21.1.3.28` | INTEGER: w1310nm(0), w1550nm(1), na(2) | Set R4 wavelength |

#### Alarm Thresholds — OBP2/OBP4

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 7 | `linkN_R1AlarmPower` | `.9` | `1.3.6.1.4.1.47477.10.21.1.3.9` | DisplayString (dBm) | Set R1 alarm threshold |
| 8 | `linkN_R2AlarmPower` | `.10` | `1.3.6.1.4.1.47477.10.21.1.3.10` | DisplayString (dBm) | Set R2 alarm threshold |
| 9 | `linkN_R3AlarmPower` | `.29` | `1.3.6.1.4.1.47477.10.21.1.3.29` | DisplayString (dBm) | Set R3 alarm threshold |
| 10 | `linkN_R4AlarmPower` | `.30` | `1.3.6.1.4.1.47477.10.21.1.3.30` | DisplayString (dBm) | Set R4 alarm threshold |

#### Alarm Thresholds — OBP8

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 11 | `linkN_1R1AlarmPower` | `.31` | `1.3.6.1.4.1.47477.10.21.1.3.31` | DisplayString (dBm) | Set 1R1 alarm threshold |
| 12 | `linkN_1R2AlarmPower` | `.32` | `1.3.6.1.4.1.47477.10.21.1.3.32` | DisplayString (dBm) | Set 1R2 alarm threshold |
| 13 | `linkN_1R3AlarmPower` | `.33` | `1.3.6.1.4.1.47477.10.21.1.3.33` | DisplayString (dBm) | Set 1R3 alarm threshold |
| 14 | `linkN_1R4AlarmPower` | `.34` | `1.3.6.1.4.1.47477.10.21.1.3.34` | DisplayString (dBm) | Set 1R4 alarm threshold |
| 15 | `linkN_2R1AlarmPower` | `.45` | `1.3.6.1.4.1.47477.10.21.1.3.45` | DisplayString (dBm) | Set 2R1 alarm threshold |
| 16 | `linkN_2R2AlarmPower` | `.46` | `1.3.6.1.4.1.47477.10.21.1.3.46` | DisplayString (dBm) | Set 2R2 alarm threshold |
| 17 | `linkN_2R3AlarmPower` | `.47` | `1.3.6.1.4.1.47477.10.21.1.3.47` | DisplayString (dBm) | Set 2R3 alarm threshold |
| 18 | `linkN_2R4AlarmPower` | `.48` | `1.3.6.1.4.1.47477.10.21.1.3.48` | DisplayString (dBm) | Set 2R4 alarm threshold |

#### Bypass Modes

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 19 | `linkN_PowerAlarmBypass2` | `.11` | `1.3.6.1.4.1.47477.10.21.1.3.11` | INTEGER: off(0), powerAlarmR1(1), powerAlarmR2(2), anyAlarmR1-R2(3), allAlarmR1-R2(4), na(5) | Set OBP2 bypass trigger mode |
| 20 | `linkN_PowerAlarmBypass4` | `.67` | `1.3.6.1.4.1.47477.10.21.1.3.67` | INTEGER: off(0), R1(1), R2(2), R3(3), R4(4), anyAlarmR1-R4(5), allAlarmR1-R4(6), na(7) | Set OBP4 bypass trigger mode |
| 21 | `linkN_PowerAlarmBypass8` | `.68` | `1.3.6.1.4.1.47477.10.21.1.3.68` | INTEGER: off(0), 1R1(1)–2R4(8), any(9), all(10), na(11) | Set OBP8 bypass trigger mode |

#### Failover Configuration

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 22 | `linkN_ReturnDelay` | `.12` | `1.3.6.1.4.1.47477.10.21.1.3.12` | Integer32 (seconds) | Set auto-return delay |
| 23 | `linkN_BackMode` | `.13` | `1.3.6.1.4.1.47477.10.21.1.3.13` | INTEGER: autoNoBack(0), autoBack(1) | Set back mode |
| 24 | `linkN_BackDelay` | `.14` | `1.3.6.1.4.1.47477.10.21.1.3.14` | Integer32 (seconds) | Set back delay |
| 25 | `linkN_SwitchProtect` | `.23` | `1.3.6.1.4.1.47477.10.21.1.3.23` | INTEGER: off(0), on(1) | Enable/disable switch protection |

#### Active Heartbeat Configuration

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 26 | `linkN_ActiveHeartSwitch` | `.15` | `1.3.6.1.4.1.47477.10.21.1.3.15` | INTEGER: off(0), on(1) | Enable/disable active heartbeat |
| 27 | `linkN_ActiveSendInterval` | `.16` | `1.3.6.1.4.1.47477.10.21.1.3.16` | Integer32 (ms) | Set ping send interval |
| 28 | `linkN_ActiveTimeOut` | `.17` | `1.3.6.1.4.1.47477.10.21.1.3.17` | Integer32 (ms) | Set ping response timeout |
| 29 | `linkN_ActiveLossBypass` | `.18` | `1.3.6.1.4.1.47477.10.21.1.3.18` | Integer32 | Set consecutive losses before bypass |
| 30 | `linkN_PingIpAddress` | `.19` | `1.3.6.1.4.1.47477.10.21.1.3.19` | IpAddress | Set ping target IP |

#### Passive Heartbeat Configuration

| # | Command Name | Suffix | Link1 Example OID | Type | Description |
|---|-------------|--------|-------------------|------|-------------|
| 31 | `linkN_PassiveHeartSwitch` | `.20` | `1.3.6.1.4.1.47477.10.21.1.3.20` | INTEGER: off(0), on(1) | Enable/disable passive heartbeat |
| 32 | `linkN_PassiveTimeOut` | `.21` | `1.3.6.1.4.1.47477.10.21.1.3.21` | Integer32 (ms) | Set passive HB timeout |
| 33 | `linkN_PassiveLossBypass` | `.22` | `1.3.6.1.4.1.47477.10.21.1.3.22` | Integer32 | Set loss threshold before bypass |

---

## 6. Device Bypass Section — Core Capabilities

This is the heart of the OBP device. The bypass mechanism provides **three layers of protection** to detect inline tool failure and reroute traffic.

### 6.1 Layer 1: Manual/Auto Channel Switching

The most basic control — directly choosing where traffic goes.

| OID Suffix | Link1 OID | What it controls |
|------------|-----------|------------------|
| `.3` | `1.3.6.1.4.1.47477.10.21.1.3.3` | **WorkMode** — `manualMode(0)` or `autoMode(1)` |
| `.4` | `1.3.6.1.4.1.47477.10.21.1.3.4` | **Channel** — `bypass(0)` or `primary(1)` |

- **Manual mode**: An operator explicitly switches between primary (through the tool) and bypass (skip the tool).
- **Auto mode**: The device monitors conditions (power levels, heartbeat) and switches automatically.

### 6.2 Layer 2: Power Alarm Bypass (Optical Power Monitoring)

The device continuously reads optical power levels on each receiver. When power drops below a configurable threshold (in dBm), a power alarm fires. The **bypass mode** setting determines which alarm conditions trigger an automatic switch to bypass.

**How it works:**

```
Receiver Power ──► Compare against threshold ──► Alarm? ──► Check bypass mode ──► Switch to bypass
     (dBm)            (R1AlarmPower etc.)           │            (PowerAlarmBypassN)
                                                    └─ No ──► Stay on primary
```

**Bypass mode options per device type:**

| Mode | OBP2 (`.11`) | OBP4 (`.67`) | OBP8 (`.68`) | Meaning |
|------|-------------|-------------|-------------|---------|
| `off` | off(0) | off(0) | off(0) | Power alarms do NOT trigger bypass |
| Single receiver | powerAlarmR1(1), R2(2) | R1(1), R2(2), R3(3), R4(4) | 1R1(1), 1R2(3), 1R3(5), 1R4(7), 2R1(2), 2R2(4), 2R3(6), 2R4(8) | Bypass only if that specific receiver's power drops |
| `any` | anyAlarmR1-R2(3) | anyAlarmR1-R4(5) | anyAlarm1R1-2R4(9) | Bypass if **any single** receiver loses power |
| `all` | allAlarmR1-R2(4) | allAlarmR1-R4(6) | allAlarm1R1-2R4(10) | Bypass only if **all** receivers lose power simultaneously |
| `na` | na(5) | na(7) | na(11) | Not applicable (no card in slot) |

### 6.3 Layer 3: Heartbeat Monitoring

Two independent heartbeat mechanisms verify the inline tool is alive and passing traffic.

#### Active Heartbeat (ICMP Ping)

The OBP sends ICMP pings to a configured IP address (typically the inline tool's management IP). If pings fail, bypass triggers.

| Parameter | OID Suffix | Link1 OID | Purpose |
|-----------|------------|-----------|---------|
| `ActiveHeartSwitch` | `.15` | `1.3.6.1.4.1.47477.10.21.1.3.15` | Enable/disable (off=0, on=1) |
| `ActiveSendInterval` | `.16` | `1.3.6.1.4.1.47477.10.21.1.3.16` | How often to send pings (ms) |
| `ActiveTimeOut` | `.17` | `1.3.6.1.4.1.47477.10.21.1.3.17` | Response timeout (ms) |
| `ActiveLossBypass` | `.18` | `1.3.6.1.4.1.47477.10.21.1.3.18` | Consecutive missed pings before triggering bypass |
| `PingIpAddress` | `.19` | `1.3.6.1.4.1.47477.10.21.1.3.19` | Target IP to ping |
| `ActiveHeartStatus` | `.24` | `1.3.6.1.4.1.47477.10.21.1.3.24` | Status: alarm(0), normal(1), off(2), na(3) |

#### Passive Heartbeat (Traffic Flow Monitor)

The OBP monitors whether traffic is actually flowing through the link. If traffic stops for too long, bypass triggers.

| Parameter | OID Suffix | Link1 OID | Purpose |
|-----------|------------|-----------|---------|
| `PassiveHeartSwitch` | `.20` | `1.3.6.1.4.1.47477.10.21.1.3.20` | Enable/disable (off=0, on=1) |
| `PassiveTimeOut` | `.21` | `1.3.6.1.4.1.47477.10.21.1.3.21` | Silence timeout before alarm (ms) |
| `PassiveLossBypass` | `.22` | `1.3.6.1.4.1.47477.10.21.1.3.22` | Loss threshold before triggering bypass |
| `PassiveHeartStatus` | `.25` | `1.3.6.1.4.1.47477.10.21.1.3.25` | Status: alarm(0), normal(1), off(2), na(3) |

### 6.4 Failover Recovery Settings

Once the device has switched to bypass, these settings control what happens when the inline tool comes back online.

| Parameter | OID Suffix | Link1 OID | Values | Purpose |
|-----------|------------|-----------|--------|---------|
| `ReturnDelay` | `.12` | `1.3.6.1.4.1.47477.10.21.1.3.12` | Integer32 (seconds) | Wait time before switching back from bypass to primary |
| `BackMode` | `.13` | `1.3.6.1.4.1.47477.10.21.1.3.13` | autoNoBack(0), autoBack(1) | `autoNoBack` = stay in bypass until manual intervention; `autoBack` = auto-return to primary when tool recovers |
| `BackDelay` | `.14` | `1.3.6.1.4.1.47477.10.21.1.3.14` | Integer32 (seconds) | Delay before executing the back-switch |
| `SwitchProtect` | `.23` | `1.3.6.1.4.1.47477.10.21.1.3.23` | off(0), on(1) | Anti-flap protection — prevents rapid toggling between bypass and primary |

### 6.5 End-to-End Bypass Flow

```
                    ┌─────────────────────────────────────────────┐
                    │              AUTO MODE ACTIVE                │
                    │                                             │
                    │  ┌─────────────┐    ┌────────────────────┐  │
                    │  │ Power Alarm │    │ Active Heartbeat   │  │
                    │  │ Monitoring  │    │ (ICMP Ping)        │  │
                    │  │             │    │                    │  │
                    │  │ R1 < -20dBm?│    │ Ping 10.0.0.1 ... │  │
                    │  │ R2 < -20dBm?│    │ 3 missed = alarm  │  │
                    │  └──────┬──────┘    └─────────┬──────────┘  │
                    │         │                     │             │
                    │         ▼                     ▼             │
                    │  ┌──────────────────────────────────┐       │
                    │  │     ANY failure condition met?    │       │
                    │  └───────────────┬──────────────────┘       │
                    │                  │ YES                       │
                    │                  ▼                           │
                    │  ┌──────────────────────────────────┐       │
                    │  │   SWITCH TO BYPASS CHANNEL (0)   │       │
                    │  │   Send StateChange trap          │       │
                    │  └───────────────┬──────────────────┘       │
                    │                  │                           │
                    │                  ▼                           │
                    │  ┌──────────────────────────────────┐       │
                    │  │   BackMode = autoBack?           │       │
                    │  │   YES: wait ReturnDelay seconds  │       │
                    │  │         then switch to primary   │       │
                    │  │   NO:  stay in bypass (manual)   │       │
                    │  └──────────────────────────────────┘       │
                    └─────────────────────────────────────────────┘
```

---

## 7. Quick OID Reference — Complete Suffix Map

### NMU Suffixes (`...60.{suffix}`)

| Suffix | Object | Access |
|--------|--------|--------|
| `.1` | deviceType | RO |
| `.2` | ipAddress | RW |
| `.3` | subnetMask | RW |
| `.4` | gateWay | RW |
| `.5` | macAddress | RO |
| `.6` | tcpPort | RW |
| `.7` | startDelay | RW |
| `.8` | keyLock | RW |
| `.9` | buzzerSet | RW |
| `.10` | deviceAddress | RW |
| `.11` | power1State | RO |
| `.12` | power2State | RO |
| `.13` | softwareVersion | RO |
| `.14` | hardwareVersion | RO |
| `.15` | serialNumber | RO |
| `.16` | manufacturingdate | RO |
| `.50.1` | systemStartup (trap) | — |
| `.50.2` | cardStatusChanged (trap) | — |

### Per-Link Suffixes (`...{N}.3.{suffix}`)

| Suffix | Object | Access | Notes |
|--------|--------|--------|-------|
| `.1` | State | RO | |
| `.2` | DeviceType | RO | |
| `.3` | WorkMode | RW | |
| `.4` | Channel | RW | |
| `.5` | R1Power | RO | OBP2/4 |
| `.6` | R2Power | RO | OBP2/4 |
| `.7` | R1Wave | RW | OBP2/4 |
| `.8` | R2Wave | RW | OBP2/4 |
| `.9` | R1AlarmPower | RW | OBP2/4 |
| `.10` | R2AlarmPower | RW | OBP2/4 |
| `.11` | PowerAlarmBypass2 | RW | OBP2 |
| `.12` | ReturnDelay | RW | |
| `.13` | BackMode | RW | |
| `.14` | BackDelay | RW | |
| `.15` | ActiveHeartSwitch | RW | |
| `.16` | ActiveSendInterval | RW | |
| `.17` | ActiveTimeOut | RW | |
| `.18` | ActiveLossBypass | RW | |
| `.19` | PingIpAddress | RW | |
| `.20` | PassiveHeartSwitch | RW | |
| `.21` | PassiveTimeOut | RW | |
| `.22` | PassiveLossBypass | RW | |
| `.23` | SwitchProtect | RW | |
| `.24` | ActiveHeartStatus | RO | |
| `.25` | PassiveHeartStatus | RO | |
| `.26` | PowerAlarmStatus | RO | |
| `.27` | R3Wave | RW | OBP4 |
| `.28` | R4Wave | RW | OBP4 |
| `.29` | R3AlarmPower | RW | OBP4 |
| `.30` | R4AlarmPower | RW | OBP4 |
| `.31` | 1R1AlarmPower | RW | OBP8 |
| `.32` | 1R2AlarmPower | RW | OBP8 |
| `.33` | 1R3AlarmPower | RW | OBP8 |
| `.34` | 1R4AlarmPower | RW | OBP8 |
| `.35` | R3Power | RO | OBP4 |
| `.36` | R4Power | RO | OBP4 |
| `.37` | 1R1Power | RO | OBP8 |
| `.38` | 1R2Power | RO | OBP8 |
| `.39` | 1R3Power | RO | OBP8 |
| `.40` | 1R4Power | RO | OBP8 |
| `.41` | 2R1Power | RO | OBP8 |
| `.42` | 2R2Power | RO | OBP8 |
| `.43` | 2R3Power | RO | OBP8 |
| `.44` | 2R4Power | RO | OBP8 |
| `.45` | 2R1AlarmPower | RW | OBP8 |
| `.46` | 2R2AlarmPower | RW | OBP8 |
| `.47` | 2R3AlarmPower | RW | OBP8 |
| `.48` | 2R4AlarmPower | RW | OBP8 |
| `.49` | 1R1Wave | RO | OBP8 |
| `.60` | 1R2Wave | RO | OBP8 |
| `.61` | 1R3Wave | RO | OBP8 |
| `.62` | 1R4Wave | RO | OBP8 |
| `.63` | 2R1Wave | RO | OBP8 |
| `.64` | 2R2Wave | RO | OBP8 |
| `.65` | 2R3Wave | RO | OBP8 |
| `.66` | 2R4Wave | RO | OBP8 |
| `.67` | PowerAlarmBypass4 | RW | OBP4 |
| `.68` | PowerAlarmBypass8 | RW | OBP8 |

### Per-Link Trap Suffixes (`...{N}.3.50.{suffix}`)

| Suffix | Trap | Notes |
|--------|------|-------|
| `.1` | WorkModeChange | |
| `.2` | StateChange | |
| `.3` | R1WaveSet | OBP2/4 |
| `.4` | R2WaveSet | OBP2/4 |
| `.5` | R3WaveSet | OBP4 |
| `.6` | R4WaveSet | OBP4 |
| `.7` | R1AlarmSet | OBP2/4 |
| `.8` | R2AlarmSet | OBP2/4 |
| `.9` | R3AlarmSet | OBP4 |
| `.10` | R4AlarmSet | OBP4 |
| `.11` | 1R1AlarmSet | OBP8 |
| `.12` | 2R1AlarmSet | OBP8 |
| `.13` | 1R2AlarmSet | OBP8 |
| `.14` | 2R2AlarmSet | OBP8 |
| `.15` | 1R3AlarmSet | OBP8 |
| `.16` | 2R3AlarmSet | OBP8 |
| `.17` | 1R4AlarmSet | OBP8 |
| `.18` | 2R4AlarmSet | OBP8 |
| `.19` | PowerAlarmBypass2Changed | OBP2 |
| `.20` | PowerAlarmBypass4Changed | OBP4 |
| `.21` | PowerAlarmBypass8Changed | OBP8 |
| `.22` | powerAlarmR1 | OBP2/4 |
| `.23` | powerAlarmR2 | OBP2/4 |
| `.24` | powerAlarmR3 | OBP4 |
| `.25` | powerAlarmR4 | OBP4 |
| `.26` | powerAlarm1R1 | OBP8 |
| `.27` | powerAlarm2R1 | OBP8 |
| `.28` | powerAlarm1R2 | OBP8 |
| `.29` | powerAlarm2R2 | OBP8 |
| `.30` | powerAlarm1R3 | OBP8 |
| `.31` | powerAlarm2R3 | OBP8 |
| `.32` | powerAlarm1R4 | OBP8 |
| `.33` | powerAlarm2R4 | OBP8 |
