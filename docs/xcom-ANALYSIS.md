# OxTS XCOM Import — Phase A Analysis

**Date:** 2026-10-26 (Asia/Shanghai)  
**Branch (planned):** `feature/xcom-import` (from `main` @ `331ecf2`)  
**Worktree:** `D:\CMTS\cmts-xcom` (isolated from `feature/gate-sync-file-color`)  
**Samples:** `mobile-015.xcom` / `.ncom` / `.rcom` (+ XCOM Manual PDF)  
**CSV:** not provided in this drop — numerical validation uses demux vs standalone binaries + NCOM field decode spot-checks.

---

## 1. XCOM Manual (official)

Source: user-supplied XCOM Manual (rev 250512) + [xiph Ogg](http://www.xiph.org/ogg/).

| Fact | Detail |
|------|--------|
| Role | Container only — multiplexes streams; **does not** decode NCOM/RCOM |
| Capture | Page starts with ASCII `OggS` |
| Header | version, header_type bits (continued/BOS/EOS), **granule** (int64 LE), serial (u32), page seq, CRC, segment count |
| Granule | Time in **nanoseconds**, usually GPS-aligned. Values **≤ 31190400000000000** mean GPS time unknown (free-running). Above threshold ⇒ GPS time known. GPS epoch 1980-01-06; no leap seconds |
| Serial | Separates logical streams; BOS page carries initial text header |
| Segments | Lacing values; 255 = continue length; packet ends when lacing < 255 |
| Initial header | First line capture pattern e.g. `[OxTS-NCom]`, `[OxTS-RCom]`; `key=value` pairs; **`headers=N`** = number of **auxiliary header packets** before data |
| Aux headers | Codec config / grouping; data packets start on a **new page** after aux |
| Footer | Final packet repeats codec identifier (e.g. `[OxTS-NCom]`) |
| Broadcast quirks | Streams may restart; granule may go backwards; page order not guaranteed |

NCOM/RCOM field layouts are **out of scope** for the XCOM manual — use NCOM Manual / RCOM Manual / NCOMdecoder.

---

## 2. Sample file probe (`mobile-015`)

### 2.1 Container streams

| Serial | BOS | `headers=` | Aux (observed) | Role |
|--------|-----|------------|----------------|------|
| 0 | `[OxTS-NCom]headers=2` | 2 | `{OxTS-Grouping}Grouping=0.0`, `{OxTS-DataStorePrevious}…StreamSerial=0` | **Secondary / other NCOM** (not equal to standalone `.ncom`) |
| 1 | `[OxTS-RCom]headers=3` | 3 | Grouping=1.0, `{OxTS-Range}WhoAmI=Range Hunter.XCom.Serial=2`, DataStorePrevious | **RCOM** (RT-Range) |
| 2 | `[OxTS-NCom]headers=3` | 3 | Grouping=1.0, `{OxTS-Range}WhoAmI=Hunter Address.IPv4=195.0.0.23 Provenance=UDP Range.XCom.Serial=1`, DataStorePrevious StreamSerial=385876163 | **Primary hunter NCOM** ≡ standalone `.ncom` |

Pages total: **315**. All three streams have BOS+EOS.

### 2.2 Time (granule / packet prefix)

- GPS-known granules ≈ **1472383616.400 … 1472383637.430** GPS seconds (span ≈ **21.0 s**).
- Each **data** packet (after aux) is: **`int64` LE time (ns, same as granule family) + raw codec payload**.
- Standalone `.ncom` / `.rcom` are **raw codec bytes only** (no 8-byte prefix).

### 2.3 Demux vs standalone

| Check | Result |
|-------|--------|
| Serial 2 NCOM: strip 8-byte prefix from each data pkt, concat, drop footer | **Exact byte match** to `mobile-015.ncom` (151056 bytes, **2098** × 72-byte packets) |
| Serial 1 RCOM: same strip+concat | **Exact match** to standalone `.rcom` **excluding** packet type **0x03 (Wrapped NCOM)** — 664871 bytes, 4175 packets (2077× type 0x02 Ext.range, 2077× type 0x01 Lane, 21× type 0x06) |
| Standalone `.rcom` alone | Also contains **2098× type 0x03** Wrapped NCOM (redundant with NCOM file / serial 2) |
| Serial 0 NCOM | Valid NCOM-shaped stream (**2188** packets after demux); **≠** standalone `.ncom` — keep as **separate OfflineFileEntry** (`…#ncom-s0` or Grouping label) |

Spot-check NCOM structure-A (nav status 4 Locked): lat≈42.61939°, lon≈91.52227°, alt≈469.1 m, speed≈22.7 m/s — identical in XCOM serial 2 vs standalone for first samples.

### 2.4 Multi-vehicle rule

- Do **not** merge serial 0 and serial 2 NCOM into one vehicle.
- Labels from aux: Grouping / WhoAmI / StreamSerial / IPv4 when present.
- RCOM is relative/lane data tied to Range (serial 1), not a second map vehicle by default — expose as **channels on a dedicated entry** or secondary entry labeled `RCOM` / `Range`.

---

## 3. NCOM decode (official NCOM Manual rev 260702)

Packet: **72 bytes**, LE, sync **0xE7**. Structure-A when nav status ∈ {0–7,10,20–22}; status **11** = structure-B → **ignore**.

Checksums: sum of bytes (sync excluded) at offsets 22 / 61 / 71.

### Batch A (host frame after IMU→host)

| Bytes | Field | Scale / unit |
|-------|-------|----------------|
| 1–2 | Time ms into GPS minute | 0–59999 |
| 3–5 | Accel X | 1e-4 m/s² (signed 24-bit) |
| 6–8 | Accel Y | 1e-4 m/s² |
| 9–11 | Accel Z | 1e-4 m/s² |
| 12–14 | Ang rate X | 1e-5 rad/s |
| 15–17 | Ang rate Y | 1e-5 rad/s |
| 18–20 | Ang rate Z | 1e-5 rad/s |

### Batch B

| Bytes | Field | Unit |
|-------|-------|------|
| 23–30 | Latitude | double **radians** |
| 31–38 | Longitude | double **radians** |
| 39–42 | Altitude | float m |
| 43–45 | North vel | 1e-4 m/s |
| 46–48 | East vel | 1e-4 m/s |
| 49–51 | Down vel | 1e-4 m/s |
| 52–54 | Heading | 1e-6 rad (±π) |
| 55–57 | Pitch | 1e-6 rad |
| 58–60 | Roll | 1e-6 rad |

Full time: status channel 0 carries GPS minutes since 1980-01-06; combine with Batch A ms. Invalid nav status 0 / reserved → do not treat as valid kinematics (NaN/skip).

Reference implementation: [OxfordTechnicalSolutions/NCOMdecoder](https://github.com/OxfordTechnicalSolutions/NCOMdecoder).

---

## 4. RCOM decode (official RCOM Manual)

Header: sync **0x57**, type u8, length u16 (data section incl. trailing checksum).

| Type | Name | Priority for CMTS |
|------|------|-------------------|
| 0x02 | Extended range | **Yes** — target lateral/longitudinal range & rates (Table 16) |
| 0x01 | Lane | Optional — lane distances |
| 0x03 | Wrapped NCOM | Skip in RCOM path (use NCOM stream) |
| 0x06 | Multiple sensor point | Later / diagnostics |
| others | | Skip with diagnostic count |

Extended range (documented): Target number; Lateral/Longitudinal range (Long, 0.001 m, invalid 0x80000000); range rates (Short, 0.01 m/s, invalid 0x8000); plus further fields per Table 16 — decode documented fields only; ignore trailing unknown bytes (forward compatible).

---

## 5. CMTS architecture map (reuse)

| Piece | Location | Role |
|-------|----------|------|
| Unified sample | `Core/VehicleSample.cs` | Timestamp, lat/lon/alt, speed km/h, accels, yaw, heading, `Channels` dict |
| Channel ids | `Core/ChannelIds.cs` / `ChannelRegistry` | VBO-like short names; curves/TrackMap use `GetChannel` |
| Offline set | `Session/OfflineFileSet.cs` | One `OfflineFileEntry` per file/stream; color palette |
| VBO import | `Recorder/VboReader.cs` | Pattern for file→`List<VehicleSample>` + column names |
| KML import | `Analysis/KmlTrackImporter.cs` | Alternate importer → same OfflineFileSet |
| UI open | `MainWindow.xaml.cs` ~885 | `OpenFileDialog` filters `*.vbo;*.kml;*.kmz`; dispatch by extension; background load with progress |

**Plan:** Add `Recorder/OxTS/` (or `Analysis/OxTS/`):

1. `XcomContainerReader` — demux pages → streams (BOS, aux count, data pkts with optional 8-byte time prefix)
2. `NcomDecoder` — 72-byte structure-A → measurements (checksum optional warn)
3. `RcomDecoder` — framed packets → Ext.range (+ lane if easy)
4. `XcomImporter` / `NcomFileImporter` — build `VehicleSample` lists; **one OfflineFileEntry per NCOM serial**; RCOM → separate entry `basename#rcom` with relative channels (lat/lon may be empty or hunter-linked later)
5. Wire dialog filter `*.xcom` (+ optional `*.ncom` / `*.rcom` later)
6. Do **not** touch TrackMap3D / gate / cursor files

CMTS units mapping (NCOM → VehicleSample):

| NCOM | CMTS |
|------|------|
| speed = hypot(Vn,Ve) m/s | SpeedKph = * 3.6 |
| Accel X/Y/Z host | Longacc / Latacc / Z_Accel (confirm host X=forward with OxTS host frame — document assumption) |
| Ang rate Z | Yaw_Rate deg/s = rad/s * 180/π |
| Heading/Pitch/Roll | degrees |
| Lat/Lon | degrees (from radians) |
| Altitude | Height m |

Extra channels (keep in `Channels`): `vn_mps`, `ve_mps`, `vd_mps`, `pitch_deg`, `roll_deg`, `nav_status`, `ax_mps2`, … and RCOM `range_lat_m`, `range_long_m`, `range_lat_rate_mps`, …

Timestamp: prefer packet int64 ns → .NET ticks/DateTimeOffset from GPS epoch; fallback Batch A + channel 0 minutes.

---

## 6. Open questions / limits

1. **No CSV** in drop — cannot do NAVgraph-style column CSV golden compare; rely on binary demux + NCOM manual scales.
2. Host-frame axis naming (X forward?) — follow NCOM manual “host object”; document; align with CMTS Longacc/Latacc convention used by VBO.
3. Serial 0 identity — Grouping=0.0 only; display as `mobile-015.xcom#ncom-s0` until better ID.
4. RCOM→TrackMap: no absolute lat/lon in Ext.range alone — curves yes; map polyline only for NCOM entries.
5. Live UDP NCOM/RCOM — out of scope; decoders kept file-agnostic for later.
6. Page CRC verification — optional; Ogg CRC poly available; soft-fail.

---

## 7. Implementation checklist (Phase B)

- [ ] `XcomContainerReader` + unit test demux serial2 ≡ fixture ncom
- [ ] `NcomDecoder` + unit test first packets lat/lon/speed
- [ ] `RcomDecoder` Ext.range fields + type counts
- [ ] Importer → OfflineFileSet; MainWindow filter + async progress
- [ ] Regression: existing tests still pass
- [ ] Commit/push `feature/xcom-import`; no merge main; no version bump
