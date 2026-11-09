# Remote 通道缺口：Win 侧映射 vs 动态通道

> 分支：`feature/cmts-remote-datasource`  
> 日期：2026-11-09（Asia/Shanghai）  
> 状态：**Win 全通道读取已就绪**（Parser 透传 + catalog 单位 + Available MergeRemoteKeys）；**等 Android 把 Live 全键打进 `stream.sample`**。用户已确认 Hz 足够，合入仍等全键对打后再开。

## 1. `TryMapSample` 映射

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
| `valid.*` | 门控（false/null → NaN、不写 Channels） | — |

### extras pass-through（不 bump 协议）

- 非保留数值键 → 原名写入 `Channels`（例：`phone.pressure`）。
- 数组：按 Android Live 源分量名展平（仅命名，不造值）  
  - `phone.accel` / `vehicle.accel` / `phone.gyro` / `vehicle.gyro` → `.x/.y/.z`  
  - `vehicle.attitude` → `.roll/.pitch/.yaw`  
  - `phone.attitude` → `.pitch/.roll/.azimuth/.headingAcc`  
  - `phone.rotation_vector` → `.x/.y/.z/.w/.headingAcc`  
  - `phone.gps` → `.lat/.lon/.alt/.speed/.bearing/.horizAcc/.vertAcc/.speedAcc`  
  - `phone.satellites` → `.visible/.used`  
- 浅层 object：`parent.child` 点号键。
- 数组中的 `null` / 非有限值跳过（不 invent 0）。
- **线上一时没有的键不会出现在 Channels**。

### `device.status`（已解析）

| JSON | Channels Id |
|---|---|
| `satellitesVisible` | `phone.satellites.visible` |
| `satellitesUsed` | `phone.satellites.used` **与** `sats` |
| `recordState` | 仍更新录制 UI（非曲线通道） |

DataSource 把最近一次 status 通道合并进后续 `stream.sample` 的 `Channels`，并 `ChannelRegistry.MergeRemoteKeys`。

**Win 已修（Available 不涨）**：Remote 连上后 `EnterLiveSourceMode`→`SetLiveCore` 会清掉样本阶段已 Merge 的 `phone.*`；且 `_seenChannelIds` 不再二次 Merge。现改为：`RegisterSeenChannels` 对 Available 缺失键再 Merge，且 `EnterLiveSourceMode` 后从 Remote `SnapshotSeenChannelIds` 恢复。

## 2. Available（曲线下拉）

**Live core（连接 Remote 时 `SetLiveCore`）**

- `velocity`, `Longacc`, `Latacc`, `Z_Accel`, `Yaw_Rate`, `heading`, `lat`, `long`, `height`
- 合成：`__elapsed_test__`, `__distance_m__`；X `__axis_time__`

**收到 status/sample 后（MergeRemoteKeys）**

- 加上实际出现过的 extras / status 键  
- Catalog 已预置 Android 对齐显示名 + 单位（含 `phone.gps.*`、`phone.rotation_vector.*`）  
- API：`ChannelRegistry.SetFromRemoteKeys` / `MergeRemoteKeys`

## 3. 线上已有 vs 仍等 Android 扩包

| Android Live 键 | 远程 WS 现状（Android `RemoteBridge.publishCoreSample`） | Win |
|---|---|---|
| core flat (`ax`…`headingDeg`) | **已发** | 已映射 |
| `phone.satellites` | status：`satellitesVisible/Used` | **已映射** → `phone.satellites.*` + `sats` |
| `phone.accel` / `vehicle.accel` / `phone.gyro` / `vehicle.gyro` | **尚未进 sample JSON**（仍折成 core） | Parser/Available/catalog **已就绪**；有键即显 |
| `phone.pressure` / `phone.attitude` / `vehicle.attitude` / `phone.rotation_vector` / `phone.gps` 细项 | 未进 remote sample | 同上，等扩包 |

### Android 扩包期望（不 bump v0.1 Version）

在现有 core 字段旁，把 Live 已有源原样写入 payload（数组或浅 object 均可）：

- `phone.pressure`（number）
- `phone.accel` / `vehicle.accel` / `phone.gyro` / `vehicle.gyro`（len 3）
- `vehicle.attitude`（roll,pitch,yaw）
- `phone.attitude`（pitch,roll,azimuth,headingAcc）
- `phone.rotation_vector`（x,y,z,w,headingAcc）
- `phone.gps`（lat,lon,alt,speed_m_s,bearing,horizAcc,vertAcc,speedAcc；缺测用 null）
- 可选：`phone.satellites` 数组（visible,used）——status 已够用

缺测继续 `valid`/null，不填假 0。Win 订阅仍 `channels:["core"]` + `maxHz:100`。

## 4. 验收 / 剩余

- [x] Parser + Available 覆盖已发键（core + status sats）
- [x] extras 契约测试（透传 / 不 invent）
- [x] 全 Android Live 键表 flatten 测试 + GPS null 跳过
- [x] catalog 补 `phone.gps.*` / `phone.rotation_vector.*` 单位
- [x] 用户确认 Hz 足够快
- [ ] Android 扩包进 `stream.sample` 后 F5：曲线下拉出现全键并可画
- [ ] 仍不 bump Version、不进 main（全键对打通过后再合）

协议 SSOT 仍为 `docs/remote-device-p0-win.md` v0.1。

## 5. 合入备注（merge notes）

合入前：

1. Android tip 含全键 sample，Note20/S23 已装。
2. Win F5 `feature/cmts-remote-datasource`：粘贴连接串 → 连上 → Available 含 `phone.*`/`vehicle.*` 展平键 → 任选一条非 core 曲线有动。
3. 自动化：`dotnet test` 过滤 `CMTSRemote` / `ChannelRegistryModeSwitch` 全绿。
4. PR 范围仅 Remote + ChannelRegistry + docs；不开无关重构。
