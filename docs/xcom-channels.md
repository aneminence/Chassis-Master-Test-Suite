# OxTS XCOM — channel & unit mapping (CMTS)

## NCOM → VehicleSample / Channels

| Source (NCOM Manual) | CMTS channel | Unit in CMTS |
|----------------------|--------------|--------------|
| hypot(Vn,Ve) | `velocity` / SpeedKph | km/h |
| Accel X (host) | `Longacc` | m/s² |
| Accel Y (host) | `Latacc` | m/s² |
| Accel Z (host, down) | `Z_Accel` | m/s² (sign flipped to up-positive) |
| Ang rate Z | `Yaw_Rate` | deg/s |
| Heading | `heading` | deg (±180 from OxTS) |
| Lat/Lon | `lat` / `long` | deg |
| Altitude | `height` | m |
| Vn/Ve/Vd | `vn_mps` / `ve_mps` / `vd_mps` | m/s |
| Pitch/Roll | `pitch_deg` / `roll_deg` | deg |
| Nav status | `nav_status` | enum byte |
| Accel/rate raw | `ax_mps2` … `wy_degps` | SI |

Host-frame assumption: OxTS host X forward, Y right, Z down (NCOM Manual Batch A).

Timestamp: XCOM 8-byte packet time prefix (GPS ns when above manual threshold).

## RCOM Ext.range → Channels

| Field | Channel | Unit |
|-------|---------|------|
| Lateral range | `range_lat_m` | m |
| Longitudinal range | `range_long_m` | m |
| Lat/long range rate | `range_lat_rate_mps` / `range_long_rate_mps` | m/s |
| Hunter/target heading | `hunter_hdg_deg` / `target_hdg_deg` | deg |
| Hunter fwd/lat vel | `hunter_fwd_mps` / `hunter_lat_mps` | m/s |
| Range accel | `range_lat_acc_mps2` / `range_long_acc_mps2` | m/s² |
| Target # | `rcom_target` | 1–4 |

No absolute WGS84 in Ext.range alone — TrackMap will not draw an RCOM-only polyline.

## Multi-stream

Each NCOM serial → separate offline file entry `basename#sN-Ncom-…`.  
RCOM → separate entry. Never merge serials.
