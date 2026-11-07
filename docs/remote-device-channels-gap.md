# Remote 通道缺口：Win 侧映射 vs 动态通道

> 分支：`feature/cmts-remote-datasource` @ `269b95a`  
> 日期：2026-11-07（Asia/Shanghai）  
> 状态：分析；**等 Android 字段表后再改代码**（本文件可合入文档，不 bump Version）

## 1. `TryMapSample` 当前映射（固定）

| JSON `payload` | VehicleSample typed | Channels Id |
|---|---|---|
| `measureTimeMs`（必填） | `Timestamp` | — |
| `seq` | `Sequence` | — |
| `speedKph` | `SpeedKph` | `velocity` |
| `ax` | `LongitudinalAcceleration` | `Longacc` |
| `ay` | `LateralAcceleration` | `Latacc` |
| `az` | `VerticalAcceleration` | `Z_Accel` |
| `yawRateDps` | `YawRate` | `Yaw_Rate` |
| `lat` | `Latitude` | `lat` |
| `lon` | `Longitude` | `long` |
| `altM` | `Altitude` | `height` |
| `headingDeg` | `Heading` | `heading` |
| `deviceId` / env | `VehicleId` | — |
| `vehicleLabel` | `VehicleLabel` | — |
| `receiveTimeMs` | 仅 `SampleParseResult` | — |
| `valid.*` | 门控（false/null → NaN、不进 Channels） | — |

**未读的 payload 数值键一律丢弃**（无 pass-through）。

`device.status` 现状：只取 `recordState`；卫星数 / RTK / 气压等**未解析**。

## 2. 曲线下拉（Curves / Gauge）实际可见集

Remote 连接走 `EnterLiveSourceMode()` → `ChannelRegistry.SetLiveCore()`，可选 Y 通道仅：

- `velocity`, `Longacc`, `Latacc`, `Z_Accel`, `Yaw_Rate`, `heading`, `lat`, `long`, `height`
- 合成：`__elapsed_test__`, `__distance_m__`（+ X 轴 `__axis_time__`）

目录 `SeedCatalogFromOnsShot7` 里虽有 `sats`、`PitchRate`、`RollRate`、`Temp` 等，**live 模式不会出现在下拉**。

离线 VBO：`SetFromVboColumns(columns)` 会按文件列动态登记未知 Id（`Humanize` + `GuessUnit`）并刷新 Available。

数学通道：`SyncMathsChannels` 合并进 Available，不替换源通道。

## 3. 改动面评估（动态通道）

| 已有能力 | 结论 |
|---|---|
| `VehicleSample.Channels` 字典 | **已有**；Remote 已写入核心 Id |
| `ChannelRegistry.SetFromVboColumns` | **已有动态登记模式**；可仿「Remote 见到的键」 |
| Maths | 独立路径，可继续 merge |
| `TryMapSample` | 需扩：已知别名 + 可选 numeric extras |
| `EnterLiveSourceMode` / `SetLiveCore` | 若不改，动态键写进 Channels 也**进不了下拉** |
| `device.status` | 低频字段（卫星等）需另挂或写入慢更新通道 |

**不需要**大改 DataBus / 重写 ChannelRegistry；改面集中在 Parser 映射 + Remote 连接后 Available 扩展（类 VBO）。

## 4. 方案建议

**推荐：等 Android 贴真实 sample / status 字段表后，走混合小步（偏 A，带一点 B）**

1. **(A) 固定扩展**：对已约定字段（如 `sats`、气压、pitch/roll…）加 `ChannelIds`（或复用目录已有短名）+ `TryMapSample` 映射 + valid 门控。  
2. **轻量 (B)**：其余数值键 pass-through → `Channels`；连接后 / 首包后 `SetAvailable(liveCore ∪ seenKeys)`（或抽 `SetFromRemoteKeys`，内部同 `SetFromVboColumns`）。  
3. 协议：卫星数等协议写在 `device.status` 的，与 Android 对齐「进 sample」还是「status→伪通道/仪表」；**不要 silently 假定已在 sample 里**。

**暂不做**：全动态 JSON 任意嵌套、改协议冻结 core、bump Version、合 main。

## 5. Win 侧已知「会丢 / 选不到」的键

相对用户口述（三轴加计/角速度/姿态角、卫星数、气压、高度）与协议 v0.1：

| 能力 | Win 现状 |
|---|---|
| 三轴加计 ax/ay/az | 已映射；live 可选 |
| 偏航角速度 yawRateDps | 已映射；live 可选 |
| 高度 altM | 已映射；live 可选 |
| 航向 headingDeg | 已映射；live 可选 |
| 卫星数 | 协议放 status；**未解析、下拉无**（目录有 `sats` 名） |
| 气压 | **sample 未映射；下拉无** |
| 姿态角 pitch/roll（及 pitchRate/rollRate） | **sample 未映射；下拉无**（目录有 Rate 名，live 不展示） |
| 任意 Android 新增数值键 | **整键丢弃** |

精确「丢掉了哪些 JSON 名」以 Android 字段表为准；上表是 Win 侧已知缺口。

## 6. 下一步

1. @CMTS · Android开发 贴当前 `stream.sample` + `device.status` 字段表（含单位 / valid）。  
2. Win 对照本文件开小 PR：映射 + Available 扩展；契约测试加「extras 进 Channels / 下拉可见」。  
3. 对打后再谈合入。