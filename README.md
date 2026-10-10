# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

---

## 最近更新（2026-10-10）

### 外观 / 语言 / 在线地图（`feature/map-i18n-theme`）
- **语言**：顶部 `Appearance` → Language，中文 / English 运行时切换并写入 `%LocalAppData%\CMTS\app-preferences.json`。
- **主题**：暗黑 / 明亮；自定义配色（强调色 / 窗口与面板背景），持久化。
- **在线底图**：Track Map 工具栏 `Basemap` 开关 + 图源下拉（OSM / OpenTopo / Esri 影像·街道·地形 / Carto 浅·深 / 自定义 XYZ）；瓦片画在轨迹与 Gate **之下**；磁盘缓存 `map-tiles/`。商业图源（谷歌/高德等）请用自定义 XYZ 自行填入合法地址。

### 外壳与工作区
- **顶部栏（VBTS 风格）**：`Layout` 分区示意菜单可单独开关 Dashboard / Test Results / Map / Chart（**无 Video**），并带 Reset；`Load` 打开 VBO；`Online` / `Offline` 切换线上线下；`Clear` 清空已打开文件；Files 芯片**始终展开**不折叠。
- **已删除**顶部 Dashboard / Data / Analysis / Replay / Settings 导航行（无实际用途）。
- **会话记忆**：启动自动恢复上次未关闭的 VBO、已导入 `.vbts` 门点、Test Results 条件设置、Maths 通道。

### Dashboard（可定制）
- 可 **Add Gauge / Reset**；表盘可拖动、缩放、删除；点标题选 **Live 通道** 或 **Test Results**（含 Pass 实测值）。
- 数值随外框自动放大（无字号上限）、居中；缩小主分区时**布局不重排**，可向右/下溢出并裁切，避免挤叠。

### 多文件曲线 / 对比
- 打开多个 VBO 时底部曲线 **始终叠加**（芯片色 + 图例）；点文件芯片只 **focus 加粗**，不整图切换。
- Test Results **Compute 扫全部已打开文件**；结果表有 **Source** 列与行复选框（单击勾选 + 表头全选）。
- 勾选 ≥2 行进入 **compare overlay**：X = 各 run 起点起的秒，曲线叠画；对比模式下可中键平移、右键/滚轮缩放；图例在曲线区外侧不挡数据。
- Compute 后曲线彩色区间 + Track Map 加粗高亮轨迹段；点结果行两边联动。

### Test Results（P0 + Gate）
- **Accel / Decel / Custom / Gate**；条件区可上下拖调高度，一键 Hide/Show。
- Gate：Start When / End When + 多条 Pass（Channel + Min/Max + At 门）；结果表 **Pass values** 列高亮实测值（绿=范围内 / 红=超限）；CSV 导出含该列。
- **Session** 按钮编辑 Driver / Vehicle / Track…，新录制写入 VBO `[SessionData]`。

### Track Map / Gate / `.vbts`
- 底轨加粗；每扇门独立颜色，**左上角 Gates 图例**（与右上角轨迹图例分离）。
- 工具栏 Add / 下拉切换 / 改宽度 / Rename / Delete；地图可点选门段。
- **Import** 可从 VBTS 工程 `.vbts` 导入门（分→度、航向校正）；Export `.spl` 仍为占位。

### Maths / Measure
- **Maths Channels…** 对话框：增删、公式编辑（Channels / fx 插入）、名称与单位；通道可供曲线 / Custom / Dashboard 使用（基础四则；累计/积分未做）。
- 曲线拖选 X 区间 → 左下 min/max/avg；`Ctrl+Shift+C` 复制；`Esc` 清除。

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
     (可选录制)         + 光标 / Measure     + Test Results / Gate

离线：VboReader ──► OfflineFileSet / _sampleHistory ──► 同一条曲线 / 轨迹 / Dashboard 管线
分析：Analysis（SpeedToSpeed / GateRun / Maths）──► Test Results 表 + 区间标注
```

要点（与代码一致）：

- 生产者实现 `IDataSource`，经构造注入的 `DataBus.TryPublish` 写入；UI / Recorder 只读总线与历史缓冲。  
- `DataBus`：`BoundedChannel` 容量 2000，`DropOldest`，允许多写多读。  
- `VehicleSample`：typed 核心物理量 + `Channels` 字典；`GetChannel` 优先字典、核心 Id 有 typed 回退。  
- GSpot：HTTP token → properties（按 `pos`）→ subscribe → WebSocket；多车过滤 / 断线指数退避；Lost/OOO 恒为 0。  
- `SessionMemoryStore`：本地 JSON 持久化打开文件、门、试验条件、Maths。

---

## 界面结构

```text
┌───────────────────────────────────────────────────────────────────────┐
│ CMTS  Layout│Appearance│Load│Online│Offline│Clear  [●][■] Elapsed     │  顶部栏
├──────────────────┬─────────────────────┬──────────────────────────────┤
│ Dashboard        │ Test Results        │ Track Map                    │
│ 可定制表盘       │ Accel/Decel/Custom/ │ GPS 轨迹 · Gate · .vbts 导入 │
│ Live / Test 绑定 │ Gate · Maths · CSV  │ run 高亮 · 左上 Gates 图例   │
├──────────────────┴─────────────────────┴──────────────────────────────┤
│ [X Axis] [X Auto Scale] [+ Add Plot] [Reset]   Files 芯片（常显）     │
│ Online 工具：[Simulator] [GSpot…] [UDP] [Stop]      Rx / Lost / OOO   │
│ Plot：多文件叠加 · 光标/Measure · 对比模式叠图                        │
└───────────────────────────────────────────────────────────────────────┘
```

- **顶部栏**：Layout 分区开关、Load、Online/Offline、Clear、录制、已录时长、系统时钟。  
- **无**旧版 Dashboard/Data/Analysis/Replay/Settings 导航行。  
- **Online 工具**：UDP / GSpot / Simulator 在 Online 模式下显示（Settings 正式页尚未做）。

---

## 项目结构

```text
Chassis Master Test Suite/
├─ Core/
│   ├─ VehicleSample.cs / DataBus.cs / SampleHistoryBuffer.cs
│   ├─ ChannelIds.cs / ChannelInfo.cs / ChannelRegistry.cs
│   ├─ DashboardGaugeModels.cs / SessionMemoryStore.cs / AppPreferencesStore.cs
│   └─ PlotDownsampler.cs / SampleEnricher.cs
├─ Localization/   Loc.cs（中/英）
├─ Themes/         Theme.Dark/Light.xaml · ThemeManager · AppearanceService
├─ Map/            MapTileMath · MapTileSource · MapTileCache · MapTileLayerController
├─ Communication/
│   ├─ IDataSource.cs / DataSourceState.cs / DataSourceStats.cs
│   ├─ UdpPacket*.cs / UdpReceiver.cs / UdpSender.cs
│   └─ GSpot/   GSpotDataSource / GSpotParser / GSpotOptions
├─ Recorder/    VboRecorder / VboReader / CsvRecorder（未接线）
├─ Simulator/   VehicleSimulator
├─ Session/     DataSourceSession / OfflineFileSet / RecordingSession / SessionMetadata
├─ Analysis/
│   ├─ SpeedToSpeedEngine / TestDefinition / TestRunResult / CSV 导出
│   ├─ GateStore / GateRunEngine / GatePassCondition / VbtsGateImporter
│   ├─ MathsChannelStore / MathsExpression / MathsEnricher
│   └─ SelectionMeasure / SampleSource / AnnotatedTestRun
├─ Controls/    DashboardPanel / TestResultsPanel / TrackMapPanel
│               MathsChannelsDialog / GaugeBindingPickerDialog / SessionEditDialog
├─ ChassisMasterTestSuite.Tests/   xUnit（纯逻辑，无 WPF）
├─ MainWindow.* 外壳、Layout、录制、多文件曲线、光标联动
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

或在 Visual Studio 中 **F5**。建议日常从 `bin\Release\net10.0-windows\` 启动已发布副本，避免 Debug 被占用。

### 一键重建 `rebuild.cmd`

强杀占用中的 exe → 删 `bin` / `obj` → `dotnet build`。适用于：

- VS XAML 误报大量 `CS0103`  
- `MSB3021` / `MSB3027` 文件被占用  

```bash
dotnet test "Chassis Master Test Suite.sln" -c Release
```

质量备注见 `CHANGELOG-quality.md`。

---

## 软件操作指南

### 1. 启动与默认数据源

启动后默认走 **UDP**（`127.0.0.1:50000`），**不会自动录制**。  
若有会话记忆，会尝试重新打开上次的 VBO / 门 / 试验设置。  
若端口被占用，会提示错误而不是闪退；请结束残留 CMTS 进程后再开。

### 2. Online：UDP + Simulator（本机仿真）

1. 顶部切到 **Online**，确认工具为 **UDP**（或点一次 **UDP**）。  
2. 点 **Simulator**，用 WASD / Space / R 驾驶：

| 按键 | 功能 |
|---|---|
| `W` / `S` | 加速 / 制动 |
| `A` / `D` | 左转 / 右转 |
| `Space` | 紧急制动 |
| `R` | 复位 |

3. 主窗口 **Rx** 应上涨，Dashboard、曲线、Track Map 同步更新。

### 3. Online：GSpot 连接（实车 / 设备房间）

1. 点 **GSpot…**。  
2. 填写 **房间号**、**密码**；**Filter cNum 建议先留空**（填错会滤掉全部车辆）。  
3. 也可预先设环境变量 `CMTS_GSPOT_ROOM` / `CMTS_GSPOT_PASSWORD` / `CMTS_GSPOT_CNUM`（选车过滤，可选）。  
4. 只有 WebSocket **真正连上**才会切源并提示成功；密码错误会报错并保持原数据源。  
5. 连上后顶部为 **Online**；有车上报时 Rx 上涨。空房间可能长期 Rx=0（正常）。  
6. 切回本机仿真：点 **UDP**；停止当前源：点 **Stop**。

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

### 5. Offline / Load：打开 VBO

1. 点顶部 **Load**（或切到 **Offline** 后在离线页打开）。  
2. 可打开多个 VBO；Files 芯片常显，曲线默认全部叠加。  
3. 曲线 / Track Map / Dashboard 共用历史缓冲；点芯片 focus 某一文件。  
4. **Clear** 清空已打开文件。  
5. **注意**：打开 VBO 会替换/合并离线历史；若 Online 源仍在推流，行为以当前 Online/Offline 模式为准。

### 6. 曲线交互

| 操作 | 行为 |
|---|---|
| 横轴标签 | 同会话约 2h 内用**北京时间**；跨度更大时用各文件相对 **Elapsed (s)** |
| **左键** 点击 / 拖动 | 竖向光标；Dashboard 冻结为该点 |
| **中键** 拖动 | 平移视野（取消 Auto；对比模式同样可用） |
| **右键** / 滚轮 | 缩放（取消 Auto） |
| **拖选 X 区间** | Measure：左下 min/max/avg；`Ctrl+Shift+C` 复制；`Esc` 清除 |
| **X Auto Scale** | 勾选时随数据自动缩放；人手操作后取消勾选 |
| Reset View | 恢复自动缩放视野 |

### 7. Track Map 与 Gate

- 点击轨迹同步曲线光标与 Dashboard。  
- **Add Gate** → 点轨迹放置；下拉切换、改宽度、Rename、Delete；地图点选高亮。  
- **Import**：选择 `.vbts` 导入门（会确认是否替换现有门）。  
- Compute 后的 run 段以加粗彩色轨迹显示；右上角为轨迹/文件图例，左上角为 Gates 图例。

### 8. Dashboard

- **Add Gauge**：添加数字表盘；拖动移动，边角缩放；`×` 删除。  
- 点标题打开绑定选择器：Live 通道或 Test Results / Pass 值。  
- 无光标显示最新实时值；有光标（曲线或地图）冻结为选中样本。  
- 缩小 Dashboard 分区时表盘位置/大小不变，可溢出裁切。

### 9. Test Results

1. 打开含加减速的 VBO（可多文件），或 Online 积累历史。  
2. 选 **Accel**（默认 0→100）、**Decel**（100→0）、**Custom** 或 **Gate**。  
3. Gate：配置 Start/End When 与 Pass（可多条）；条件区可拖高或 Hide。  
4. **Compute** → 结果含 Source、时长、ΔV、距离、Pass/Fail、**Pass values**。  
5. 勾选多行 → 底部进入对比叠图；**Export CSV** 导出（UTF-8 BOM）。  
6. **Maths Channels…** 编辑计算通道；**Session** 编辑会话元数据。

距离优先用 DistanceTraveled，否则速度积分，再否则起终点 Haversine。

### 10. 通道选择（Channel Registry）

- 下拉只列当前 `ChannelRegistry.Available` 中的通道。  
- 实时：`SetLiveCore()` 约 9 个核心通道 + 合成 `Time`。  
- 打开 VBO：`SetFromVboColumns`，下拉为文件实际列。  
- Maths 通道注册后进入可用列表。  
- 切回 UDP/GSpot 成功后恢复 `SetLiveCore()`。

### 11. 多 Plot

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

## 已完成功能（对照代码）

- [x] UDP + DataBus + Simulator  
- [x] `IDataSource` + GSpot（WebSocket）  
- [x] VBO 录制（Start/Pause/Stop）与多文件回放（Load）  
- [x] 多 Plot / 通道曲线 / 北京时间轴 / 光标联动 / Auto 缩放  
- [x] 多文件曲线始终叠加 + Test Results 勾选对比叠图  
- [x] Track Map（轨迹、光标同步、run 高亮）  
- [x] Layout 分区开关（Dashboard / Test Results / Map / Chart，无 Video）  
- [x] Online / Offline / Load / Clear  
- [x] 可定制 Dashboard（增删/拖动/缩放、Live + Test Results 绑定）  
- [x] Test Results：Accel / Decel / Custom / Gate + CSV  
- [x] Gate 管理（颜色、宽度、重命名、地图点选）+ `.vbts` 门导入  
- [x] Pass Condition + Pass values 高亮  
- [x] Maths Channels 对话框（基础表达式）  
- [x] Chart Measure（min/max/avg）  
- [x] Session 元数据写入 VBO `[SessionData]`  
- [x] 会话记忆（VBO / `.vbts` 门 / Test Results / Maths）  
- [x] 通道注册表（实时 vs 文件）  
- [x] UI 主题（暗黑 / 明亮 / 自定义）+ 中英语言  
- [x] Track Map 在线底图（可开关、多图源）  

---

## 开发路线（剩余）

与用户确认后的规划一致（不做 / 砍掉的项已从路线图移除）。

**已完成（本分支）**
- [x] 在线地图底图（开关 + OSM/Esri/Carto/OpenTopo/自定义 XYZ）  
- [x] 界面语言中/英  
- [x] 皮肤：暗黑 / 明亮 + 自定义配色  

**延后**
- [ ] Settings 正式页  
- [ ] `.vbts` 整包导入（条件 / Pass / Maths / Dashboard）  
- [ ] Maths 高级函数（累计 / 积分等）  
- [ ] 法规 / 场景试验插件包  
- [ ] Dashboard 更多表盘类型（Angular / Chart / Target / Level）与告警  

**明确不做**
- GSpot 实车标定、同步视频、正式报告 PDF/XLSX/DOCX、Gate `.spl`（用 `.vbts` 替代）、原生 VBOX Online、`CsvRecorder`

---

## 已知限制

- 仿真为纯运动学模型，高速极限工况偏大。  
- GSpot 部分 IMU 映射为临时方案，需标定（`acc`/`gyro`/`exts` 临时约定见 `GSpotParser`）。  
- 曲线按可见窗口 + min-max 降采样绘制（上限约 12,000 点）；全量历史仍在内存缓冲。  
- 内存历史缓冲上限约 **1,000,000** 条样本（环形丢最旧，线程安全快照）。  
- Track Map 显示上限约 50,000 点（抽稀）。  
- 录制路径若 `TryWrite` 失败会在 Lost 区显示 `+R{n}`（仍建议长跑时关注磁盘与队列）。  
- `CsvRecorder` 保留但未接线。  
- GSpot 密码仍走 HTTP GET query（待与供应商确认 POST/Header）。  
- Dashboard 目前为 Default 数字表盘；VBTS 式 Angular / Chart / Level 表盘未做。  
- `.vbts` Import 仅解析门点，不导入完整 CustomTest 条件（条件需在 Test Results 里手动或后续整包导入）。

---

## 相关资源

- VI 设计系统：`D:\CMTS\VI`（独立目录，不在本仓库）  
- 仓库：https://github.com/aneminence/Chassis-Master-Test-Suite

## Tests / quality

Minimal xUnit project: `ChassisMasterTestSuite.Tests`（`net10.0`，stock xUnit）。

覆盖纯逻辑（经 linked 生产源）：历史缓冲、DataBus、降采样、UDP 序列化、VBO 经度往返、GSpot `pos`、Maths、Gate/`.vbts` 导入、SessionMemory 等——无 WPF / 无实网。

```bash
dotnet test "Chassis Master Test Suite.sln" -c Release
```

Quality notes: see `CHANGELOG-quality.md`.
