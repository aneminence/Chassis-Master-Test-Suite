# CMTS Windows P0：远程 Android 设备对接清单

> 状态：分析稿（无产品代码改动）  
> 分支：`docs/remote-device-p0`  
> 日期：2026-11-07（Asia/Shanghai）  
> 范围：手动 IP + 实时订阅 + 录制启停；**不**改 GSpot 协议；**不**合 `main`（除非另派）

## 0. 首版范围（提醒）

| 做 | 不做（后置） |
|---|---|
| Windows 客户端连 Android：`ws://IP:port/...`（手动填 IP/端口） | mDNS / 自动发现 |
| 实时 `stream.subscribe` → 样本进 DataBus → 地图/曲线/仪表 | 文件列表/下载/断点续传 |
| `record.start` / `record.stop` + 状态同步 | NTRIP 远程配置、无屏热点配网 |
| 最小 `device.hello` / `device.status` 握手与鉴权骨架 | 公网穿透、蜂窝远程管理 |

架构原则：**Android = 采集服务端；Windows = 控制/显示客户端**。新增旁路数据源（建议名 `CMTSRemoteDataSource`），**禁止**把 CMTS 远程帧伪装成 GSpot 帧。

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
| `GSpotDataSource` | `Communication/GSpot/GSpotDataSource.cs` | **只借形态**：后台循环、重连退避、`LastError`、State 迁移、WS 读循环。新建 `Communication/Remote/`（或同级）下的 `CMTSRemoteDataSource` + `CMTSRemoteOptions`（Host/Port/Token/DeviceId） |
| `GSpotParser` / `GSpotPropertyIndex` | `Communication/GSpot/GSpotParser.cs` | **不要复用**解析器。GSpot 是 HTTP token + properties/pos 数组体；CMTS 远程应是显式 JSON 对象字段。单独写 `CMTSRemoteParser` → `VehicleSample` |
| `GSpotOptions` 过滤字段 | `GSpotOptions.cs` | 远程首版单设备可不做 Filter；若一机多车再议 `VehicleId` |
| MainWindow GSpot 按钮路径 | `MainWindow.xaml.cs`（`GSpotButton_Click` 一带） | 首版可加「远程设备」入口：手动 IP 对话框 → `CreateRemote` → `WaitForConnected` → `EnterLiveSourceMode`；**不要**塞进 GSpot 对话框 |

### 1.3 明确不要动（do not touch）

- **GSpot HTTP API / WS 帧格式 / `GSpotParser` 字段约定**（含 `serverTime` vs `gpsTime` 临时策略）
- GSpot 房间密码、token、subscribe properties CSV
- 离线 VBO/KML/`OfflineFileSet` 主路径（远程文件下载是后续阶段）
- Simulator / TrackMap3D / XCOM 通道语义（除非协议显式映射）
- 现有 UDP `UdpReceiver` 包格式（另一套实时源；远程 Wi‑Fi 不复用 UDP 二进制）

### 1.4 与 GSpot 的关键差异（对接时注意）

| 点 | GSpot 现状 | CMTS Remote 建议 |
|---|---|---|
| 鉴权 | HTTP `/api/token` + header | WS 内 `device.hello` + 配对凭据（首版可用共享 token） |
| 时间戳 | `VehicleSample.Timestamp` ← `serverTime`（Unix ms）；`gpsTime` 未用 | **必须**区分测量时间与接收时间（见 §2） |
| 序号 | 本地合成 `Sequence`；Lost/OOO=0 | Android 发单调 `seq`；Win 可算 Lost/OutOfOrder |
| 不可用传感器 | 缺失常落 0 | 禁止静默 0；用 `null` / `valid` 标志（见 §2） |
| 多车 | `GUserId`/`cNum` → `VehicleId`/`VehicleLabel` | `deviceId`（+ 可选 `vehicleId`）写入同样字段 |

---

## 2. 消息草案骨架（Android ↔ Win WebSocket）

约定（草案，待 Android 对齐后冻结）：

- 传输：单条 WebSocket，文本 JSON（UTF-8）
- 每条消息公共头：

```json
{
  "v": 1,
  "type": "device.hello",
  "id": "req-or-evt-uuid",
  "ts": 1730966400123,
  "deviceId": "CMTS-A1B2",
  "payload": { }
}
```

| 字段 | 含义 |
|---|---|
| `v` | 协议版本 |
| `type` | 消息类型 |
| `id` | 请求/事件 ID；响应用同一 `id` 或 `refId` |
| `ts` | **发送端墙钟** Unix ms（调试用，不等于 GNSS 测量时） |
| `deviceId` | 稳定设备键 → Win `VehicleSample.VehicleId` |
| `payload` | 类型相关体 |

控制与高速流**同连接**时：实现上必须分队列/优先级（控制优先），避免 `stream.sample` 堵 `record.*`。文件下载日后独立通道。

### 2.1 连接最小集

**C→S `device.hello`**

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

**S→C `device.hello`（ack） / `device.status`**

```json
{
  "v": 1, "type": "device.hello", "id": "h1", "deviceId": "CMTS-A1B2",
  "payload": {
    "ok": true,
    "deviceLabel": "RK3588-01",
    "capabilities": ["gnss", "imu", "record"],
    "recordState": "READY",
    "proto": 1
  }
}
```

`device.status`：连接后可周期性或变更时推送（电量、GNSS fix、RTK、存储、NET、`recordState`）。首版字段可瘦。

### 2.2 `stream`（实时）

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

首版 `channels: ["core"]` = 与 `VehicleSample` typed 核心对齐的一组字段；扩展通道后续用显式名。

**S→C `stream.sample`**（对齐 `VehicleSample`）

```json
{
  "type": "stream.sample",
  "deviceId": "CMTS-A1B2",
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

**映射到 `VehicleSample`（Win 侧解析器）：**

| JSON | VehicleSample |
|---|---|
| `measureTimeMs` | `Timestamp`（优先；见下） |
| `seq` | `Sequence` |
| `speedKph` … `headingDeg` | 同名 typed 字段 |
| `deviceId` | `VehicleId` |
| `vehicleLabel` 或 deviceLabel | `VehicleLabel` |
| typed 核心 | `Channels` ← `BuildCoreChannels(...)` |

**时间戳规则（P0 必须写进协议）：**

1. `measureTimeMs`：GNSS/传感器**测量时间**（设备时钟或 GNSS 时，Unix ms）。进 `VehicleSample.Timestamp`，供曲线 Time 轴 / 录波对齐。
2. `receiveTimeMs`：Android Core **收到/打包该样本**的时间。Win 可另存统计或 Channels 扩展键（首版可不进 typed），**不得**覆盖 `Timestamp`。
3. 若某帧无可靠测量时：`measureTimeMs` 可缺省，但必须 `valid.gnss=false`（或等价），Win **不得**用本地 `DateTime.UtcNow`  silently 填成“真测量时”而不标注。

**不可用传感器：**

- 禁止把未知当成 `0`（0 是合法物理值）。
- 用 `valid.*` 或字段 JSON `null`；Win 映射时：无效通道可不写入 `Channels`，或写入 NaN（需与曲线/仪表约定一致——**待 Android 联调前拍板**，见开放问题）。

**S→C `stream.statistics`（可选，后置也可）**：发送计数、丢包估计、实际 Hz。

### 2.3 `record`

状态机与 Android Core **同一套**（草案名）：`IDLE | INITIALIZING | READY | RECORDING | STOPPING | SAVING | ERROR`。

**C→S**

- `record.start` — `payload`: `{ "mode": "manual", "requestId": "..." }`
- `record.stop` — `payload`: `{ "requestId": "..." }`

**S→C**

- `record.status` — `{ "state": "RECORDING", "fileId": "...", "startedAtMs": ..., "reason": null }`
- 失败：`ok:false` + `errorCode` / `message`；**幂等**：重复 start 在已 RECORDING 时返回当前状态，不新建第二文件。

Windows 首版 UI：连接面板上 Start/Stop + 状态灯；本机是否另存一份远程流 = 后置（现有 Recorder 订 DataBus 即可本地旁录，但产品是否默认开要另定）。

### 2.4 `config`（首版最小）

**C→S `config.get` / `config.set`**

首版建议仅：

- 流频率上限 / 订阅通道
- 录制模式（manual；auto 后置）
- 设备显示名

NTRIP、Wi‑Fi SSID、开机策略等**不进首版** `config.set`。

响应统一：`{ "ok": true/false, "errorCode": "...", "values": { } }`。

---

## 3. Windows 落地顺序（协议冻结后）

1. `CMTSRemoteOptions` + `CMTSRemoteDataSource`（hello → subscribe → sample→DataBus）
2. `DataSourceSession.CreateRemote` + MainWindow 手动 IP 入口
3. `record.start/stop` 与状态展示（不阻塞采样循环）
4. 测试：假 Android 回放 WS / 契约测试（字段↔`VehicleSample`）
5. 再谈发现、文件、NTRIP UI

**在协议草案与 Android 状态机边界对齐前：不开功能大改分支。** 本文所在 `docs/remote-device-p0` 仅为文档。

---

## 4. 给 Android 的开放问题

1. **WebSocket 路径与端口默认值？**（例：`ws://host:8787/cmts/v1`）TLS 首版是否强制？
2. **配对 token 形态？** 设备出厂随机 / 用户可重置？hello 失败码列表？
3. **`measureTimeMs` 时钟源？** GNSS UTC / 单调 boot 时间 / Android `System.currentTimeMillis`？跨断流是否保证单调？
4. **无效传感器表达：`null` vs `valid` map vs 省略字段？** Win 曲线对 NaN/缺键的行为需一起定。
5. **`seq` 范围与重置策略？** 录制启停是否重置？跨天是否重置？
6. **录制状态机枚举最终名与合法转换表？**（与实体 LOG / UI / Win 三端一致）
7. **首版 `stream.sample` 是否只发 core，还是附带原始 GNSS fix 质量 / RTK 龄期？**（建议质量进 `device.status` 或 sample 扩展，避免堵 core）
8. **单连接多订阅者？** 仅 Win 一家，还是本机 UI + Win 同时订？背压策略？
9. **控制与 sample 同 WS 时的帧大小/合并？** 是否允许 batch（`samples: []`）降频开销？
10. **设备 `deviceId` 稳定性？** 重装 App / 换网卡是否变？与 `VehicleId` 长期对齐方式。

---

## 5. 结论（给调度）

- **可复用面充足**：`IDataSource` → `DataBus` → `VehicleSample` → `LiveVehicleRoster` / UI 无需为远程推倒重来。
- **不可复用**：GSpot 解析与鉴权；新开 `CMTSRemote*` 旁路。
- **硬前置**：与 Android 冻结 §2 骨架（尤其时间双戳、seq、valid、record 幂等）。
- **首版产品切片**：手动 IP + subscribe + record start/stop；发现/文件/NTRIP/配网后置。

P0 文档交付完毕；等 Android P0 边界结论后，两边对表改一版「协议 v0.1」再开实现分支。
