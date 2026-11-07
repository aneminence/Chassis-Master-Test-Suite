# CMTS Windows P0：远程 Android 设备对接清单 + 协议 v0.1

> **Protocol v0.1 FROZEN 2026-11-07**（Asia/Shanghai）  
> 状态：协议已冻结；实现中（分支 eature/cmts-remote-datasource）  
> 分支：`docs/remote-device-p0`  
> 权威对照（Android）：`docs/GROKBOT_HANDOFF_2026-11-07_RECORDING_STATE.md`（RecordingState 七态）  
> 范围：手动 IP + 实时订阅 + 录制启停；**不**改 GSpot 协议；**不**合 `main`（除非另派）

## 0. 首版范围（冻结）

| 做 | 不做（后置） |
|---|---|
| Windows 客户端连 Android：`ws://IP:8787/cmts/v1`（手动填 IP） | mDNS / 自动发现 |
| 实时 `stream.subscribe` → 样本进 DataBus → 地图/曲线/仪表 | 文件列表/下载/断点续传 |
| `record.start` / `record.stop` + 状态同步（幂等） | NTRIP 远程配置、无屏热点配网 |
| `device.hello` / `device.status` 握手与配对 token | 公网穿透、蜂窝远程管理、TLS |

架构原则：**Android = 采集服务端；Windows = 控制/显示客户端**。新增旁路数据源（`CMTSRemoteDataSource`），**禁止**把 CMTS 远程帧伪装成 GSpot 帧。

---

## PROTOCOL v0.1（FROZEN）

与 Android 2026-11-07 群聊对齐的 10 条 + 既有骨架，一并冻结。实现分支开干前以本节为准。

### P.1 传输与端点

| 项 | 约定 |
|---|---|
| 默认 URL | `ws://IP:8787/cmts/v1` |
| TLS | **首版不强制**（局域网）；`wss://` 后置 |
| 编码 | 单条 WebSocket，文本 JSON（UTF-8） |
| 同连接 | 控制与高速流同 WS；实现必须**控制帧优先**，sample 不得堵 `record.*` / hello |
| 订阅 | 允许多订户（本机 UI + Win）；背压 **丢旧保新** |
| batch | 首版**单条** `stream.sample`；Hz 升高后再加 `samples:[]` |

### P.2 公共信封

```json
{
  "v": 1,
  "type": "device.hello",
  "id": "req-or-evt-uuid",
  "ts": 1730966400123,
  "deviceId": "<persistent-uuid>",
  "payload": { }
}
```

| 字段 | 含义 |
|---|---|
| `v` | 协议主版本；v0.1 时值为 `1` |
| `type` | 消息类型 |
| `id` | 请求/事件 ID；响应用同一 `id` 或 `refId` |
| `ts` | **发送端墙钟** Unix ms（调试用，≠ GNSS 测量时） |
| `deviceId` | 安装后持久 UUID（Android DataStore）；重装即新车 → Win `VehicleSample.VehicleId` |
| `payload` | 类型相关体 |

### P.3 鉴权：`device.hello`

**C→S**

```json
{
  "v": 1, "type": "device.hello", "id": "h1", "ts": 0,
  "payload": {
    "client": "cmts-win",
    "clientVer": "0.3.1",
    "token": "<pairing-token>",
    "protoMin": 1,
    "protoMax": 1
  }
}
```

**S→C ack（成功）**

```json
{
  "v": 1, "type": "device.hello", "id": "h1", "deviceId": "<uuid>",
  "payload": {
    "ok": true,
    "deviceLabel": "Phone-01",
    "capabilities": ["gnss", "imu", "record"],
    "recordState": "READY",
    "proto": 1
  }
}
```

**失败码（冻结）：**

| errorCode | 含义 |
|---|---|
| `auth_failed` | 配对 token 错误 / 未配对 |
| `proto_mismatch` | `protoMin`/`protoMax` 与设备不兼容 |

token：本机生成、用户可重置的配对串。hello 失败不得进入 Connected。

### P.4 `device.status`

连接后周期性或变更时推送。首版可含：电量、GNSS fix、**RTK / 卫星数**、存储、网络、`recordState`。  
**RTK / 卫星数放这里（或低频扩展），不要塞进每条 `stream.sample`。**

### P.5 `stream`

**C→S `stream.subscribe`**

```json
{
  "type": "stream.subscribe",
  "payload": {
    "channels": ["core"],
    "maxHz": 20
  }
}
```

**S→C `stream.sample`（单条；core 字段）**

```json
{
  "type": "stream.sample",
  "deviceId": "<uuid>",
  "payload": {
    "seq": 1042,
    "measureTimeMs": 1730966400100,
    "receiveTimeMs": 1730966400112,
    "vehicleLabel": "Phone-01",
    "speedKph": 15.8,
    "ax": -0.12,
    "ay": 0.05,
    "az": 9.81,
    "yawRateDps": 1.2,
    "lat": 39.9,
    "lon": 116.4,
    "altM": 45.2,
    "headingDeg": 182.0,
    "valid": {
      "gnss": true,
      "speed": true,
      "imu": true,
      "heading": false
    }
  }
}
```

#### 时间戳（硬规则）

1. **`measureTimeMs`**：优先 GNSS `elapsedRealtimeNanos` 换算到会话墙钟锚点后的 Unix ms。进 `VehicleSample.Timestamp`。  
2. **`receiveTimeMs`**：样本到达 Android Core 的时间（单调时钟映射）。Win 可另存统计；**不得**覆盖 `Timestamp`。  
3. 无可靠测量时：**省略** `measureTimeMs`（或 `null`）+ `valid.*=false`；**禁止**用接收时间冒充测量时间；Win **禁止**用本地 `UtcNow`  silently 填成真测量时。

#### 无效字段（硬规则）

- `valid` map + 字段可为 JSON `null` / 省略。  
- **禁止静默填 0**（0 是合法物理值）。  
- Win：缺键 / `null` / `valid.*=false` → 当无效；不写入 `Channels` 或按曲线约定写 NaN（实现时与仪表对齐，语义不变）。

#### `seq`

- 类型：`uint64` 单调。  
- **进程级生命周期**：录制启停**不重置**；App 进程重启才从 0。  
- Win 映射 `VehicleSample.Sequence`；可据此算 Lost / OutOfOrder。

#### → `VehicleSample` 映射

| JSON | VehicleSample |
|---|---|
| `measureTimeMs` | `Timestamp`（优先） |
| `seq` | `Sequence` |
| `speedKph` … `headingDeg` | 同名 typed 字段 |
| `deviceId` | `VehicleId` |
| `vehicleLabel` / deviceLabel | `VehicleLabel` |
| typed 核心 | `Channels` ← `BuildCoreChannels(...)` |

### P.6 `record`（与 Android RecordingState 对齐）

七态（冻结名）：`IDLE | INITIALIZING | READY | RECORDING | STOPPING | SAVING | ERROR`。  
合法转换表以 Android `docs/GROKBOT_HANDOFF_2026-11-07_RECORDING_STATE.md` 为准。

**C→S**

- `record.start` — `{ "mode": "manual", "requestId": "..." }`
- `record.stop` — `{ "requestId": "..." }`

**S→C `record.status`**

```json
{
  "type": "record.status",
  "payload": {
    "state": "RECORDING",
    "fileId": "...",
    "startedAtMs": 1730966400000,
    "reason": null,
    "ok": true
  }
}
```

**幂等**：已 RECORDING 时重复 `start` → 返回当前状态，不新建第二文件；已非 RECORDING 时重复 `stop` → 返回当前状态，不报硬错（除非 ERROR 需带 `reason`）。

### P.7 `config`（首版最小）

`config.get` / `config.set` 仅：流频率上限 / 订阅通道、录制模式（manual）、设备显示名。  
NTRIP、Wi‑Fi SSID、开机策略等**不进**首版。  
响应：`{ "ok": true/false, "errorCode": "...", "values": { } }`。

### P.8 消息类型一览（v0.1）

| type | 方向 | 用途 |
|---|---|---|
| `device.hello` | C↔S | 握手 / 鉴权 ack |
| `device.status` | S→C | 设备态；含 RTK/卫星数等低频 |
| `stream.subscribe` | C→S | 订阅 core |
| `stream.sample` | S→C | 单条样本 |
| `record.start` / `record.stop` | C→S | 录制启停 |
| `record.status` | S→C | 状态回推 |
| `config.get` / `config.set` | C↔S | 最小配置 |

---

## 1. 复用清单（对照现仓）

### 1.1 原样复用（as-is）

| 模块 | 路径 | 用法 |
|---|---|---|
| `IDataSource` | `Communication/IDataSource.cs` | 新源实现同一接口：`Name` / `State` / `Stats` / `StartAsync` / `StopAsync` |
| `DataSourceState` / `DataSourceStats` | `Communication/DataSourceState.cs`, `DataSourceStats.cs` | Connecting→Connected→Reconnecting/Faulted；有设备序号时可填 Lost/OutOfOrder |
| `DataBus` | `Core/DataBus.cs` | `TryPublish(VehicleSample)`；UI/Recorder 只订总线 |
| `VehicleSample` | `Core/VehicleSample.cs` | 统一样本；`Timestamp`/`Sequence`/`VehicleId`/`VehicleLabel` + typed 核心 + `Channels` |
| `ChannelIds` / `VehicleSample.BuildCoreChannels` | `Core/ChannelIds.cs` 等 | 速度/加计/航向/经纬高映射到现有曲线通道 |
| `LiveVehicleRoster` | `Core/LiveVehicleRoster.cs` | 多设备时用 `VehicleId` upsert；单机可 `VehicleId=deviceId` |
| `DataSourceSession` | `Session/DataSourceSession.cs` | `CreateXxx` + `SwitchAsync` / `CommitConnectedAsync` / `WaitForConnectedAsync` 模式可照搬 |

数据流（已存在，远程源应挂同一条）：

```
IDataSource  --TryPublish-->  DataBus  -->  MainWindow live 消费
                                      -->  LiveVehicleRoster（多车/多设备）
                                      -->  TrackMap / Curves / Dashboard / Recorder
```

### 1.2 应包装 / 旁路仿照（wrap，勿改协议）

| 参考 | 路径 | 建议 |
|---|---|---|
| `GSpotDataSource` | `Communication/GSpot/GSpotDataSource.cs` | **只借形态**：后台循环、重连退避、`LastError`、State 迁移、WS 读循环。新建 `Communication/Remote/` 下的 `CMTSRemoteDataSource` + `CMTSRemoteOptions`（Host/Port/Token/DeviceId） |
| `GSpotParser` / `GSpotPropertyIndex` | `Communication/GSpot/GSpotParser.cs` | **不要复用**解析器。单独写 `CMTSRemoteParser` → `VehicleSample` |
| MainWindow GSpot 按钮路径 | `MainWindow.xaml.cs` | 首版加「远程设备」入口：手动 IP → `CreateRemote` → `WaitForConnected` → `EnterLiveSourceMode`；**不要**塞进 GSpot 对话框 |

### 1.3 明确不要动（do not touch）

- **GSpot HTTP API / WS 帧格式 / `GSpotParser` 字段约定**
- GSpot 房间密码、token、subscribe properties CSV
- 离线 VBO/KML/`OfflineFileSet` 主路径
- Simulator / TrackMap3D / XCOM 通道语义（除非协议显式映射）
- 现有 UDP `UdpReceiver` 包格式

### 1.4 与 GSpot 的关键差异

| 点 | GSpot 现状 | CMTS Remote v0.1 |
|---|---|---|
| 鉴权 | HTTP `/api/token` + header | WS 内 `device.hello` + 配对 token |
| 时间戳 | `Timestamp` ← `serverTime` | **双戳**：`measureTimeMs` → Timestamp；`receiveTimeMs` 另存 |
| 序号 | 本地合成 | Android `seq`（进程级，录制不重置） |
| 不可用传感器 | 缺失常落 0 | `valid` + null；Win 绝不填 0 |
| 多车 | `GUserId`/`cNum` | `deviceId`（持久 UUID；重装=新车） |

---

## 2. Windows 落地顺序（协议已冻；**等派活再开实现分支**）

1. `CMTSRemoteOptions` + `CMTSRemoteDataSource`（hello → subscribe → sample→DataBus）
2. `DataSourceSession.CreateRemote` + MainWindow 手动 IP 入口
3. `record.start/stop` 与状态展示（不阻塞采样循环；控制优先）
4. 测试：假 Android 回放 WS / 契约测试（字段→`VehicleSample`）
5. 再谈发现、文件、NTRIP UI

**本文所在 `docs/remote-device-p0` 仅为文档；不开 `CMTSRemoteDataSource` 实现，直至调度另派。**

---

## 3. 冻结结论（给调度）

- **可复用面充足**：`IDataSource` → `DataBus` → `VehicleSample` → UI，旁路新源即可。  
- **不可复用**：GSpot 解析与鉴权。  
- **协议 v0.1 已冻**（本节 PROTOCOL）：端点 `8787/cmts/v1`、无 TLS、配对 token、双时间戳、valid/null、seq 进程级、七态 record、RTK 走 status、背压丢旧、单条 sample、持久 deviceId。  
- **下一步**：@CMTS · 项目 派实现分支后，Win 开 `CMTSRemoteDataSource`；Android 开七态枚举 + 服务侧起停 + WS 服务端。

---

## 附录：开放问题关闭表（原 §4 → 已关闭）

| # | 原问题 | v0.1 决议 |
|---|---|---|
| 1 | WS 路径/端口/TLS | `ws://IP:8787/cmts/v1`；无 TLS |
| 2 | token / 失败码 | 可重置配对串；`auth_failed` / `proto_mismatch` |
| 3 | measureTimeMs 时钟源 | GNSS elapsedRealtimeNanos→会话墙钟 Unix ms；禁止接收时冒充 |
| 4 | 无效字段 | valid map + null；Win 缺/null=无效，不填 0 |
| 5 | seq 重置 | 进程级；录制不重置；App 重启归 0 |
| 6 | recordState | IDLE…ERROR；对照 Android RecordingState 文档 |
| 7 | sample 是否含 RTK | 否；RTK/卫星数 → `device.status` |
| 8 | 多订户/背压 | 允许多订户；丢旧保新；控制优先 |
| 9 | batch | 首版单条；后置 |
| 10 | deviceId 稳定性 | 安装持久 UUID；重装=新 VehicleId |
