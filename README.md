# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

---

## 最近更新（2026-10-26）

相对上一版 README（2026-10-12 / `0ab045d`）之后合入 `main` 的已交付能力汇总。更早的 KML Phase 1、在线底图、中英语言、主题外壳、Dashboard / Gate / Maths / 多文件对比等仍见下文各节，此处不再重复展开。

### OxTS XCOM / NCOM / RCOM 离线导入
- **Offline Open** 过滤器增加 `*.xcom;*.ncom`（与 VBO / KML / KMZ 并列）。
- **多流拆分**：一个 XCOM 内每个 NCOM/RCOM 流各自成为一条 Offline 条目，命名形如 `basename#sN-…`，**不混成一路**。
- **NCOM** → 导航量写入 `VehicleSample`；**RCOM** → 相对测距等通道（见 `docs/xcom-channels.md`）。
- 实现：`Recorder/OxTS/`（`XcomContainerReader` / `NcomDecoder` / `RcomDecoder` / `XcomImporter`）；说明见 `docs/xcom-ANALYSIS.md`、`docs/xcom-channels.md`；冒烟工具 `tools/OxTSSmoke`。
- 说明：部分 XCOM 内两路 NCOM 地速在文件内容上即近乎相同，曲线会重合；RCOM 的通用 Speed 映射为猎人车前进速度，可能与 NCOM 地速接近——相对运动请看 RCOM 自有通道（如 `range_long_m` / `range_lat_m`）。

### Track Map 2D / 3D（Cesium WebView2）
- 可选 **3D** 轨迹（高度）；默认 **Ground** 海拔模式（DEM 采样贴地）；**无多倍高程夸张**（固定 1×）。
- **车辆光标**：航向对齐的长方体约 5×2×1.8 m；2D/3D 与曲线光标双向同步（含圈速对比地图点击）。
- **Gate**：贴轨扁条（无 U 型立柱、无圆点）；**无 S/E 起终点标记**。
- **GateStore 共享**：2D / 3D / 曲线竖线同步；门颜色走 `ColorHex`。
- **底图开** → 隐藏轨迹区网格；**底图关** → 保留网格；去掉左下角 online/terrain 状态角标。
- 在线底图图源逻辑与此前一致（OSM / OpenTopo / Esri / Carto / 自定义 XYZ）。
- 其它：工具栏控件更紧凑；KML 航向估计；渲染开销与闪烁修复。

### 按文件着色
- Files 芯片颜色按钮：色块（swatch）+ 十六进制；曲线与 Track Map 共用 `ColorHex`，按文件路径记忆。
- 单文件打开时曲线也跟芯片色（不再只走默认调色板）；色板菜单可见色块（`adfbd86`）。

### KML 非破坏裁剪 / 会话
- KML 轨迹支持**非破坏裁剪**（不改源文件）。
- 启动时从会话记忆恢复上次的 KML/KMZ 离线轨。

### 启动动画 Splash
- 启动时显示动画 Splash（`Assets/Splash`）。

### 圈速 / 扇区与对比联动
- Lap / Sector 计时分析；圈速操作区保持可见。
- 对比模式：尊重所选 X 轴；对比光标与地图同步；圈速对比下地图点击同步；对比 Elapsed 轴对齐。

### 曲线区
- 多 Plot **共用 X 轴缩放/平移**。
- 新增通道示例：`slope_pct`（距离窗坡度）。

### GSpot / iCell VBO
- 修正 GSpot/iCell VBO 经纬度解码（Track Map 可用）。
- 重映射 speed / heading / time / height 列；识别标准与移位布局。

### 主题 / 语言 UI
- Light 主题对齐 VI skin H（白底红黑）；UI chrome 走 `DynamicResource`。
- 顶栏 **Language / Theme** 改为分体按钮（主面快速切换 + chevron 其它项）；其它主题含 Icon Gold；中英切换时宽度固定，避免挤动 Start。

### 文档与代理工作流
- `AGENTS.md` + `.ai-workflows/`（skills / rules / product-management 模板）。
- `VERSIONING.md`（SemVer 2.0.0，0.x.y 预览线；本地 packs 基线）。
- 硬件 / 架构 / 产品文档归入 `docs/`（`docs/hardware`、`docs/architecture`、`docs/product`）。

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
     VboRecorder        ScottPlot 曲线      Dashboard / Track Map 2D·3D
     (可选录制)         + 光标 / Measure     + Test Results / Gate

离线：VboReader / KmlTrackImporter / OxTS XcomImporter ──► OfflineFileSet / _sampleHistory ──► 同一条曲线 / 轨迹 / Dashboard 管线
地图：TrackMapPanel（2D 瓦片）+ TrackMap3DController（Cesium WebView2，可选 3D）
分析：Analysis（SpeedToSpeed / GateRun / Maths / Lap·Sector）──► Test Results 表 + 区间标注
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
│ Dashboard        │ Test Results        │ Track Map（2D / 3D）         │
│ 可定制表盘       │ Accel/Decel/Custom/ │ GPS/KML/XCOM · Gate · .vbts │
│ Live / Test 绑定 │ Gate · Lap · Maths  │ run 高亮 · 左上 Gates 图例   │
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
├─ Recorder/
│   ├─ VboRecorder / VboReader / CsvRecorder（未接线）
│   └─ OxTS/   XcomContainerReader / NcomDecoder / RcomDecoder / XcomImporter
├─ Simulator/   VehicleSimulator
├─ Session/     DataSourceSession / OfflineFileSet / RecordingSession / SessionMetadata
├─ Analysis/
│   ├─ SpeedToSpeedEngine / TestDefinition / TestRunResult / CSV 导出
│   ├─ GateStore / GateRunEngine / GatePassCondition / VbtsGateImporter
│   ├─ KmlTrackImporter（KML/KMZ → TrackMap；含非破坏裁剪）
│   ├─ MathsChannelStore / MathsExpression / MathsEnricher
│   ├─ LapTimeEngine（圈速 / 扇区）
│   └─ SelectionMeasure / SampleSource / AnnotatedTestRun
├─ Controls/    DashboardPanel / TestResultsPanel / TrackMapPanel / TrackMap3DController
│               MathsChannelsDialog / GaugeBindingPickerDialog / SessionEditDialog
├─ Assets/      Splash/ · TrackMap3D/（Cesium 静态资源）
├─ docs/        xcom-*.md · architecture/ · hardware/ · product/
├─ tools/       OxTSSmoke（XCOM/NCOM 冒烟）
├─ samples/     oxts-xcom/（示例 .xcom）
├─ AGENTS.md · .ai-workflows/ · VERSIONING.md
├─ ChassisMasterTestSuite.Tests/   xUnit（纯逻辑，无 WPF）
├─ MainWindow.* 外壳、Layout、录制、多文件曲线、光标联动、文件着色
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

### 5. Offline / Load：打开 VBO / KML / KMZ / XCOM / NCOM

1. 点顶部 **Load**（或切到 **Offline** 后在离线页打开）。  
2. 对话框过滤：`*.vbo;*.kml;*.kmz;*.xcom;*.ncom`（也可按类型单选）。  
3. 可打开多个文件；Files 芯片常显；VBO / NCOM 曲线默认叠加；KML/KMZ 叠画到 Track Map；**XCOM 按流拆成多条**（`#sN-…`），不混轨。  
4. 芯片可改颜色（色块 + 色号）；曲线与地图轨迹跟 `ColorHex`。  
5. 曲线 / Track Map / Dashboard 共用历史缓冲；点芯片 focus 某一文件。  
6. **Clear** 清空已打开文件。  
7. **注意**：打开文件会替换/合并离线历史；若 Online 源仍在推流，行为以当前 Online/Offline 模式为准。

### 6. 曲线交互

| 操作 | 行为 |
|---|---|
| 横轴标签 | 同会话约 2h 内用**北京时间**；跨度更大时用各文件相对 **Elapsed (s)** |
| **左键** 点击 / 拖动 | 竖向光标；Dashboard 冻结为该点 |
| **中键** 拖动 | 平移视野（取消 Auto；对比模式同样可用） |
| **滚轮**（未聚焦） | 默认滚动曲线区；**点击**曲线聚焦后滚轮才缩放 |
| **右键** / 聚焦后滚轮 | 缩放（取消 Auto） |
| **拖选 X 区间** | Measure：左下 min/max/avg；`Ctrl+Shift+C` 复制；`Esc` 清除 |
| **X Auto Scale** | 勾选时随数据自动缩放；人手操作后取消勾选 |
| Reset View | 恢复自动缩放视野 |
| 多 Plot | + Channel 右侧通道芯片；拖分割条改高度；黄色光标跨图联动；**共用 X 缩放/平移** |

### 7. Track Map 与 Gate（2D / 3D）

- 实时/离线轨迹同步曲线光标与 Dashboard；**2D / 3D 可切换**（3D 经 Cesium WebView2）。  
- 3D：默认 **Ground**（DEM 贴地）；无多倍高程夸张；车辆光标为航向长方体；门为贴轨扁条（无圆点 / 无 S·E 标记）。  
- **GateStore** 在 2D、3D、曲线竖线间同步；门色走 `ColorHex`。  
- Offline 可叠加 **KML/KMZ**（Phase 1 + 非破坏裁剪）与 **XCOM/NCOM** 轨迹。  
- **底图开**隐藏轨迹区网格，**底图关**保留网格；无左下角 online/terrain 角标。  
- **Add Gate** → 布置 / 切换 / 改宽度 / Rename / Delete；地图点选高亮。  
- **Import**：`.vbts` 导入门（确认是否替换；优先 `.vbts`，不用 `.spl`）。  
- Compute / 圈速对比：run 段加粗；对比光标与地图点击可互相同步。  
- Layout 关掉 Test Results 后，Map 与 Dashboard 并排，仍可拖水平分割条。

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

- `+ Add Plot` 增加图；每图可改名、`Auto Y`、`+ Channel`（通道芯片在右侧）、`Remove Plot`。  
- 各图高度可拖拽调整并记忆；滚轮默认滚动，点击聚焦后再缩放。  
- `X Axis` 可选横轴信号（默认时间）；多图黄色光标联动。

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
- [x] UI 主题（暗黑 / 明亮 / Light H / Icon Gold / 自定义占位）+ 中英语言（分体按钮）  
- [x] Track Map 在线底图（可开关、多图源）  
- [x] KML/KMZ 导入（Phase 1：gx:Track / LineString → TrackMap 叠画；两步路速度 m/s→km/h）  
- [x] Layout：关 Test Results 后 Dashboard|Map 并排可拖  
- [x] Test Curve UX：滚轮滚动优先、点击聚焦缩放、通道芯片、可调高度、光标联动  
- [x] Cesium 3D Track Map（Ground 默认、航向光标、贴轨 Gate、无夸张倍率、底图开关网格）  
- [x] Gate 跨 2D / 3D / 曲线同步（GateStore + ColorHex）  
- [x] 按文件着色（芯片色块 + 曲线/地图同色，路径记忆）  
- [x] OxTS XCOM/NCOM/RCOM 离线导入（多流拆分）  
- [x] KML 非破坏裁剪 + 会话恢复 KML/KMZ  
- [x] 启动动画 Splash  
- [x] Lap / Sector 计时与对比↔地图光标同步  
- [x] 多 Plot 共用 X 轴缩放/平移；`slope_pct` 通道  
- [x] GSpot/iCell VBO 经纬度与列布局兼容  
- [x] Light（VI H）/ Icon Gold 等主题 + Language/Theme 分体按钮  
- [x] `AGENTS.md` / `.ai-workflows` / `VERSIONING.md` / `docs/` 文档布局  

---

## 开发路线（剩余）

与用户确认后的规划一致（不做 / 砍掉的项已从路线图移除）。

**已完成（本分支 / 近期）**
- [x] 在线地图底图（开关 + OSM/Esri/Carto/OpenTopo/自定义 XYZ）  
- [x] 界面语言中/英；Language/Theme 分体按钮；Light（VI H）/ Icon Gold  
- [x] 皮肤：暗黑 / 明亮 + 自定义配色（占位）  
- [x] KML/KMZ Phase 1 + 非破坏裁剪 + 会话恢复  
- [x] Layout 关 Test Results 后 Dashboard/Map 并排  
- [x] Cesium 3D 轨迹图（Ground、航向光标、贴轨门、无夸张）  
- [x] Gate 2D/3D/曲线同步；按文件着色  
- [x] OxTS XCOM/NCOM/RCOM 离线导入  
- [x] Lap/Sector；对比↔地图同步；多 Plot 共用 X 缩放  
- [x] Splash；`docs/` + AGENTS / `.ai-workflows` / VERSIONING  

**延后**
- [ ] Settings 正式页  
- [ ] `.vbts` 整包导入（条件 / Pass / Maths / Dashboard）  
- [ ] Maths 高级函数（累计 / 积分等）  
- [ ] 法规 / 场景试验插件包  
- [ ] Dashboard 更多表盘类型（Angular / Chart / Target / Level）与告警  
- [ ] RCOM Speed 与通用 Speed 解耦 / 导入时默认只开差异流（体验优化）  

**明确不做**
- GSpot 实车标定、同步视频、正式报告 PDF/XLSX/DOCX、Gate `.spl`（用 `.vbts` 替代）、原生 VBOX Online、`CsvRecorder`  
- 3D 多倍高程夸张（已去掉，固定 1×）

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
- 产品 / 架构 / 硬件：`docs/product/`、`docs/architecture/`、`docs/hardware/`  
- OxTS：`docs/xcom-ANALYSIS.md`、`docs/xcom-channels.md`；示例 `samples/oxts-xcom/`  
- 版本策略：`VERSIONING.md`；代理约定：`AGENTS.md`、`.ai-workflows/`

## Tests / quality

Minimal xUnit project: `ChassisMasterTestSuite.Tests`（`net10.0`，stock xUnit）。

覆盖纯逻辑（经 linked 生产源）：历史缓冲、DataBus、降采样、UDP 序列化、VBO 经度往返、GSpot `pos`、Maths、Gate/`.vbts` 导入、KML/KMZ（含两步路速度换算）、OxTS 相关单测、SessionMemory 等——无 WPF / 无实网。XCOM 冒烟另见 `tools/OxTSSmoke`。

```bash
dotnet test "Chassis Master Test Suite.sln" -c Release
```

Quality notes: see `CHANGELOG-quality.md`.
