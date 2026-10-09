# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

---

## 最近更新（2026-10-09）

- **可替换数据源**：`IDataSource` 抽象；支持本机 **UDP / Simulator** 与 **GSpot WebSocket**（房间号 + 密码）。
- **曲线交互**：横轴北京时间；左键光标、中键平移、右键缩放；`X Auto Scale` 勾选才自动缩放；Esc 清除光标。
- **光标联动**：曲线光标 / Track Map 点击 / Dashboard 冻结读数互相同步。
- **通道注册表**：按 `ChannelRegistry.Available` 显示下拉；启动为核心通道，打开 VBO 后切为文件列（切回实时源暂不自动复位）。
- **VBO 记录 / 回放**：Racelogic 兼容文本格式，可用 VBOX Test Suite 打开。

---

## 设计理念

```text
Acquisition  →  Parsing  →  DataBus  →  Processing  →  Visualization  →  Recording
  数据采集   →  数据解析  →  统一数据  →  实时处理   →   UI 显示      →   原始数据记录
```

1. **数据采集与 UI 解耦** —— UI 通过 `IDataSource` / DataBus，不直接绑死 UDP  
2. **原始数据与计算数据分离** —— 计算结果不覆盖原始样本  
3. **DataBus 是实时数据中心** —— 模块经总线交互  
4. **数据源可替换** —— UDP、GSpot，后续可接 CAN / VBOX / IMU / RTK  

---

## 当前架构

```text
                    ┌─ UdpReceiver (127.0.0.1:50000) ◄── Simulator / UdpSender
  IDataSource ──────┤
                    └─ GSpotDataSource (WebSocket 房间)
                              │
                              ▼
                       VehicleSample
                              │
                         DataBus (BoundedChannel)
                              │
                     ConsumeDataAsync + 历史缓冲
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
     VboRecorder        ScottPlot 曲线      Dashboard / Track Map
     (可选录制)         + 光标 / 选区         + 通道注册表

离线：VboReader ──► _sampleHistory ──► 同一条曲线 / 轨迹 / Dashboard 管线
```

要点（与代码一致）：

- 生产者实现 `IDataSource`，经构造注入的 `DataBus.TryPublish` 写入；UI / Recorder 只读总线与 `_sampleHistory`。  
- `DataBus`：`BoundedChannel` 容量 2000，`DropOldest`，允许多写多读。  
- `VehicleSample`：typed 核心物理量 + `Channels` 字典；`GetChannel` 优先字典、核心 Id 有 typed 回退。  
- GSpot：HTTP token → properties（按 `pos`）→ subscribe → WebSocket；多车过滤 / 断线指数退避；Lost/OOO 恒为 0。

---

## 界面结构

```text
┌───────────────────────────────────────────────────────────────────────┐
│ CMTS   ● Online/Offline   [● Start] [■ Stop]   Elapsed / 时钟         │  顶部栏
├───────────────────────────────────────────────────────────────────────┤
│ ⊞ Dashboard │ ▤ Data │ ⌁ Analysis │ ▷ Replay │ ⚙ Settings            │  导航栏
├──────────────────┬─────────────────────┬──────────────────────────────┤
│ Dashboard        │ Test Results        │ Track Map                    │
│ 实时 / 光标冻结  │ （占位）            │ GPS 轨迹；点击同步光标       │
├──────────────────┴─────────────────────┴──────────────────────────────┤
│ [X Axis] [X Auto Scale] [+ Add Plot] [Reset View]                     │
│ [Simulator] [GSpot…] [UDP]                    Rx / Lost / OOO         │
│ Plot：Channel 下拉（仅显示当前可用通道）+ 曲线交互                    │
└───────────────────────────────────────────────────────────────────────┘
```

- **顶部栏**：网络状态、录制、已录时长、系统时钟。  
- **导航**：`Dashboard` / `Replay` 可用；`Data` / `Analysis` / `Settings` 暂禁用。  
- **数据源按钮**：`GSpot…` / `UDP` 在曲线工具栏（Settings 完善前的快捷入口）。

---

## 项目结构

```text
Chassis Master Test Suite/
├─ Core/
│   ├─ VehicleSample.cs / DataBus.cs
│   └─ ChannelIds.cs / ChannelInfo.cs / ChannelRegistry.cs
├─ Communication/
│   ├─ IDataSource.cs / DataSourceState.cs / DataSourceStats.cs
│   ├─ UdpPacket*.cs / UdpReceiver.cs / UdpSender.cs
│   └─ GSpot/   GSpotDataSource / GSpotParser / GSpotOptions
├─ Recorder/    VboRecorder / VboReader / CsvRecorder（未接线）
├─ Simulator/   VehicleSimulator
├─ Controls/    Dashboard / TestResults / TrackMap / TrackProjection
├─ MainWindow.* 外壳、录制、曲线、数据源切换、光标联动
├─ SimulatorWindow.* 三维仿真
└─ rebuild.cmd
```

---

## 环境要求

| 项目 | 版本 |
|---|---|
| 操作系统 | Windows |
| .NET | 10.0（`net10.0-windows`） |
| UI | WPF |
| 绘图 | ScottPlot.WPF 5.x |
| IDE | Visual Studio 2022 / 2026 |

> 编译时 `NU1701`（SkiaSharp.Views.WPF）为已知警告，**不影响运行**。

---

## 编译与运行

```cmd
cd /d "D:\CMTS\Chassis Master Test Suite"
dotnet build
dotnet run
```

或在 Visual Studio 中 **F5**。

### 一键重建 `rebuild.cmd`

强杀占用中的 exe → 删 `bin` / `obj` → `dotnet build`。适用于：

- VS XAML 误报大量 `CS0103`  
- `MSB3021` / `MSB3027` 文件被占用  

---

## 软件操作指南

### 1. 启动与默认数据源

启动后默认走 **UDP**（`127.0.0.1:50000`），**不会自动录制**。  
若端口被占用，会提示错误而不是闪退；请结束残留 CMTS 进程后再开。

### 2. UDP + Simulator（本机仿真）

1. 确认工具栏为 **UDP**（或点一次 **UDP**）。  
2. 点 **Simulator**，用 WASD / Space / R 驾驶：

| 按键 | 功能 |
|---|---|
| `W` / `S` | 加速 / 制动 |
| `A` / `D` | 左转 / 右转 |
| `Space` | 紧急制动 |
| `R` | 复位 |

3. 主窗口顶部 **Rx** 应上涨，Dashboard、曲线、Track Map 同步更新。

### 3. GSpot 连接（实车 / 设备房间）

1. 点 **GSpot…**。  
2. 填写 **房间号**、**密码**；**Filter cNum 建议先留空**（填错会滤掉全部车辆）。  
3. 也可预先设环境变量 `CMTS_GSPOT_ROOM` / `CMTS_GSPOT_PASSWORD` / `CMTS_GSPOT_CNUM`（选车过滤，可选）。  
4. 只有 WebSocket **真正连上**才会切源并提示成功；密码错误会报错并保持原数据源。  
5. 连上后：按钮可显示 `GSpot●`，顶部状态为 **Online**（连接中为 Connecting… / 断线重试为 Reconnecting… / 失败为 Faulted）；有车上报时 Rx 上涨。  
6. 空房间也能连接成功，但可能长期 Rx=0（正常）。  
7. 切回本机仿真：点 **UDP**。

> GSpot 的 Lost/OOO 无 UDP 语义，通常显示为 0。acc/gyro 映射仍待实车标定。

### 4. 录制（顶部栏）

| 按钮 | 作用 |
|---|---|
| `● Start` | 新建 VBO 并开始写 |
| `❚❚ Pause` / `▶ Resume` | 暂停 / 续写同一文件 |
| `■ Stop` | 结束并关闭文件 |

录制文件默认：

```text
bin\Debug\net10.0-windows\Recordings\CMTS_yyyyMMdd_HHmmss.vbo
```

### 5. Replay：打开 VBO

1. 导航到 **Replay** → **📂 Open VBO…**  
2. 选择录制文件或外部 VBO（如 `ons shot 7.vbo`）  
3. 加载成功后回到 Dashboard；状态栏显示样本数与通道数  
4. 曲线 / Track Map / Dashboard 使用同一套 `_sampleHistory`  
5. **注意**：打开 VBO 会清空并替换历史缓冲，但**不会停止**当前 UDP/GSpot 数据源；若实时源仍在推流，新样本可能继续追加进历史  

### 6. 曲线交互

| 操作 | 行为 |
|---|---|
| 横轴标签 | **北京时间**（`HH:mm:ss`），轴标题含 Time (Beijing) |
| **左键** 点击 / 拖动 | 竖向光标；左上角读数；Dashboard 冻结为该点 |
| **中键** 拖动 | 平移视野（会取消 Auto） |
| **右键** / 滚轮 | 缩放（会取消 Auto） |
| **X Auto Scale** 勾选 | 仅勾选时随数据自动缩放；人手操作后自动取消勾选并锁定视野 |
| **Esc** | 清除光标与选区，Dashboard 恢复实时 |
| Shift+左键拖拽 | 横向选区高亮（辅助查看） |
| Reset View | 恢复自动缩放视野 |

### 7. Track Map

- 网格在轨迹下方，随缩放更新比例尺。  
- **点击轨迹上某点**：车辆标记跳到该点，并同步曲线光标与 Dashboard。  
- 手动缩放/平移地图后视野锁定，不会被刷新强行弹回（换数据集或显式复位除外）。

### 8. Dashboard 光标冻结

- 无光标：显示最新实时值。  
- 有光标（来自曲线或 Track Map）：标题进入 Cursor 模式，数值为选中样本。  
- Esc 或清除光标后恢复实时。

### 9. 通道选择（Channel Registry）

- 下拉**只列出当前 `ChannelRegistry.Available` 中的通道**，没有的不显示。  
- **启动时 / 实时核心**：`SetLiveCore()` 注册约 9 个核心通道（`velocity`、`Longacc`、`Latacc`、`Z_Accel`、`Yaw_Rate`、`heading`、`lat`、`long`、`height`），外加合成 X 轴 `Time`。  
- **打开 VBO 后**：`SetFromVboColumns([column names])`，下拉变为文件实际列（例如完整 Racelogic 导出约 50+）。  
- **注意（当前实现）**：点 **UDP** / **GSpot…** 切换数据源时**不会**自动调用 `SetLiveCore()`；打开过 VBO 后，通道下拉会继续显示该文件的列，直到重启应用。这是已知缺口，不是「一切回实时就恢复核心列表」。

### 10. 多 Plot

- `+ Add Plot` 增加图；每图可改名、`Auto Y`、`+ Channel`、`Remove Plot`。  
- `X Axis` 可选横轴信号（默认时间）。

---

## VBO 数据格式（摘要）

Racelogic 文本 VBO，目标可被 **VBOX Test Suite** 打开。固定段：`[header]` → `[channel units]` → `[comments]` → `[SessionData]` → `[column names]` → `[data]`。

要点：

- `[column names]` 单行空格分隔；列定义以该段为准。  
- 内部经度 **正 = 东经**；写入 VBO 时经度符号取反（Racelogic 约定），读取时再还原。  
- 文件 UTF-8 无 BOM，换行 CRLF。  
- VBO 的 `time` 列为 `HHMMSS.mmm`，不含日期；UI 时间轴在有 Unix 时间戳时用北京时间显示。

---

## 坐标系约定

| 项 | 约定 |
|---|---|
| Latitude | 正 = 北纬 |
| Longitude | 正 = 东经（内部） |
| 三维场景 +X / +Y / +Z | 左 / 上 / Heading0° 前方 |
| Track Map 米平面 | +X 东 / +Y 北 |

---

## 开发路线

- [x] UDP + DataBus + Simulator  
- [x] 多 Plot / 通道曲线  
- [x] UI 外壳与深色金色主题  
- [x] VBO 录制与回放  
- [x] Track Map  
- [x] `IDataSource` + GSpot  
- [x] 北京时间轴 / 光标 / Auto 缩放策略  
- [x] 通道注册表（实时 vs 文件）  
- [ ] GSpot 轴向 / 单位实车标定  
- [ ] 自动测试评价（Test Results）  
- [ ] Settings 正式页（替代 GSpot/UDP 快捷按钮）  
- [ ] 在线地图底图  
- [ ] 测试项目 / 报告  

---

## 已知限制

- 仿真为纯运动学模型，高速极限工况偏大。  
- GSpot 部分 IMU 映射为临时方案，需标定（`acc`/`gyro`/`exts` 临时约定见 `GSpotParser`）。  
- 曲线全量重绘，大数据量后需窗口 + 降采样。  
- 内存历史缓冲上限约 **1,000,000** 条样本（超出丢最旧）。  
- Track Map 显示上限约 50,000 点（抽稀）。  
- 打开 VBO 后通道列表不会因切回 UDP/GSpot 自动恢复为核心集（需重启，或后续补 `SetLiveCore` 接线）。  
- Replay 打开 VBO 不暂停活动数据源，实时包仍可能写入历史。  
- Test Results / Data / Analysis / Settings 未完整启用。  
- Steering Angle 无 `VehicleSample` 字段，Dashboard 固定显示 0。  
- `CsvRecorder` 保留但未接线。

---

## 相关资源

- VI 设计系统：`D:\CMTS\VI`（独立目录，不在本仓库）  
- 仓库：https://github.com/aneminence/Chassis-Master-Test-Suite

## Tests / quality

Minimal xUnit project: `ChassisMasterTestSuite.Tests` (`net10.0`, stock xUnit).

Covers pure logic via **linked** production sources (history buffer, DataBus, plot downsampler, UDP serializer, VBO longitude round-trip, GSpot property `pos` mapping) — no WPF / no live network.

```bash
dotnet test "Chassis Master Test Suite.sln" -c Release
```

Quality notes: see `CHANGELOG-quality.md`.

