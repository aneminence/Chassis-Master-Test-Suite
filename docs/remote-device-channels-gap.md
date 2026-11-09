# Remote 通道缺口：Win 侧映射 vs 动态通道

> 分支：`feature/cmts-remote-datasource`  
> 日期：2026-11-09（Asia/Shanghai）  
> 状态：**已实现小步**（Parser pass-through + status sats + Available MergeRemoteKeys）；合入仍等 F5 验 Hz

## 1. `TryMapSample` 映射（250deba → 本提交）

### 固定 typed / ChannelIds（未变）

| JSON `payload` | VehicleSample typed | Channels Id |
|---|---|---|
| `measureTimeMs`（必填） | `Timestamp` | — |
| `seq` | `Sequence` | — |
| `speedKph` | `SpeedKph` | `velocity` |
| `ax` / `ay` / `az` | Long/Lat/Vert Accel | `Longacc` / `Latacc` / `Z_Accel` |
| `yawRateDps` | `YawRate` | `Yaw_Rate` |
| `lat` / `lon` / `altM` | Lat/Lon/Alt | `lat` / `long` / `height` |
| `headingDeg` | `Heading` | `heading` |
| `valid.*` | 门控（false/null → NaN、不进 Channels） | — |

### 新增：extras pass-through（不 bump 协议）

- 非保留数值键 → 原名写入 `Channels`（例：`phone.pressure`）。
- 数组：按 Android Live 源分量名展平（仅命名，不造值）  
  - `phone.accel` / `vehicle.accel` / `phone.gyro` / `vehicle.gyro` → `.x/.y/.z`  
  - `vehicle.attitude` → `.roll/.pitch/.yaw`  
  - `phone.attitude` → `.pitch/.roll/.azimuth/.headingAcc`  
  - `phone.rotation_vector` → `.x/.y/.z/.w/.headingAcc`  
  - `phone.gps` → `.lat/.lon/.alt/.speed/.bearing/.horizAcc/.vertAcc/.speedAcc`  
  - `phone.satellites` → `.visible/.used`  
- 浅层 object：`parent.child` 点号键。
- **线上一时没有的键不会出现在 Channels**（不 invent）。

### `device.status`（已解析）

| JSON | Channels Id |
|---|---|
| `satellitesVisible` | `phone.satellites.visible` |
| `satellitesUsed` | `phone.satellites.used` **与** `sats` |
| `recordState` | 仍更新录制态 UI（非曲线通道） |

DataSource 把最近一次 status 通道合并进后续 `stream.sample` 的 `Channels`，并 `ChannelRegistry.MergeRemoteKeys`。

## 2. Available（曲线下拉）

**BEFORE（SetLiveCore / 250deba Remote 连接后）**

- `velocity`, `Longacc`, `Latacc`, `Z_Accel`, `Yaw_Rate`, `heading`, `lat`, `long`, `height`
- 合成：`__elapsed_test__`, `__distance_m__`（+ X `__axis_time__`）

**AFTER（本提交，连上并收到 status/sample 后）**

- 同上 live core  
- **加上**实际出现过的 extras / status 键，例如：  
  - `phone.satellites.visible`, `phone.satellites.used`, `sats`（status 一到即登记）  
  - 若 Android 扩包推了 `phone.pressure` / `vehicle.accel` 数组等 → 对应展平键也会进 Available  
- API：`ChannelRegistry.SetFromRemoteKeys` / `MergeRemoteKeys`（类 VBO 动态登记，不替换 Maths merge）

目录已预置 Android 对齐显示名/单位（仅 catalog；**不**因此单独塞进 Available）。

## 3. 线上已有 vs 仍等 Android 扩包

| Android Live 键 | 远程 WS 现状（Android RemoteBridge） | Win |
|---|---|---|
| core flat (`ax`…`headingDeg`) | **已发** | 已映射 |
| `phone.satellites` | status：`satellitesVisible/Used` | **已映射** → `phone.satellites.*` + `sats` |
| `phone.accel` / `vehicle.accel` 等全键 | **尚未进 sample JSON**（仍折成 core） | Parser/Available **已就绪**；有键即通 |
| `phone.pressure` / attitude / rotation_vector / gps 细项 | 未进 remote sample | 同上，等扩包 |

## 4. 验收 / 剩余

- [x] Parser + Available 覆盖**已发射**键（core + status sats）  
- [x] extras 契约测试（透传 / 不 invent）  
- [ ] F5：重连 S23 看 Hz + Available 是否出现 sats  
- [ ] Android 扩包后对打全键表；仍不 bump Version、不合 main  

协议 SSOT 仍为 `docs/remote-device-p0-win.md` v0.1。
