# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

---

## 最近更新

各功能后的日期为合入 `main` 的落地日（`YYYY-MM-DD`，Asia/Shanghai 本地 git 日期）。文首不再单独充当更新记录。

### 四面板最大化 / 工具栏布局（2026-11-03）
- **Dashboard / Test Results / Track Map / Curves** 各面板标题栏**右上角**可最大化：最大化后填满主工作区；再点恢复四格布局。图标为 **45° 外扩 / 内收双箭头**（Path 绘制）。（`0c3dc74`）
- **Track Map** 工具栏为**两行**：Follow、2D·3D、Basemap、瓦片图源等控件**靠右对齐**；**Gate 操作**叠在地图视口 **左上角**，位于 **Gates 图例上方**（避免与图例重叠）。（`8a9cbf5`）
- **Follow**：2D / 3D Track Map 跟随 / 居中当前车辆。（`54c5a13`，2026-11-03）
- **Curves** 工具栏：左侧 **Reset View → Mark Region → Add Gate**；**Add Plot** 在 **Remove Plot** 左侧；标记/裁剪类编辑按钮靠右；**最大化箭头在最右侧**。

### Simulator（Helix 3D）（2026-11-02）
- 仿真窗由粗略 **Viewport3D** 换为 **HelixToolkit.Wpf**（`HelixViewport3D`）三维场景。（`0a33175`）
- 赛道：**Spa 风格**大型封闭赛道、高程起伏明显（风格参考，**非官方 Spa 地图**），**非椭圆**；地形与赛道连续贴合（不悬空），边线/屏障处已处理自交与 Z-fight。（`7c560e2` / `2e2020d`，抛光 `fc86664`）
- 车体/世界坐标：**X 前、Y 左、Z 上**（右手系）；**Roll / Pitch / Yaw** 分别绕 **X / Y / Z**。
- 动力学：方向盘 **一阶滤波 + 车速衰减**（软转向）；**单轨 / 自行车**横向模型；**追逐相机**（位置/视线滞后，过弯不侧倾）。
- 仍经 **UDP / `VehicleSimulator`**（约 100 Hz）推主窗 Dashboard / 曲线 / Track Map——数据通路不变。

### GSpot 多车房间监控（2026-11-02）
- GSpot WebSocket 房间支持**多车监控**（过滤 / 选车等，见 `GSpotDataSource`）。（`08225f9`）

### OxTS XCOM / NCOM / RCOM 离线导入（2026-10-26）
- **Offline Open** 过滤器增加 `*.xcom;*.ncom`（与 VBO / KML / KMZ 并列）。
- **多流拆分**：一个 XCOM 内每个 NCOM/RCOM 流各自成为一条 Offline 条目，命名形如 `basename#sN-…`，**不混成一路**。
- **NCOM** → 导航量写入 `VehicleSample`；**RCOM** → 相对测距等通道（见 `docs/xcom-channels.md`）。
- 实现：`Recorder/OxTS/`（`XcomContainerReader` / `NcomDecoder` / `RcomDecoder` / `XcomImporter`）；说明见 `docs/xcom-ANALYSIS.md`、`docs/xcom-channels.md`；冒烟工具 `tools/OxTSSmoke`。
- 说明：部分 XCOM 内两路 NCOM 地速在文件内容上即近乎相同，曲线会重合；RCOM 的通用 Speed 映射为猎人车前进速度，可能与 NCOM 地速接近——相对运动请看 RCOM 自有通道（如 `range_long_m` / `range_lat_m`）。

### Track Map 2D / 3D（Cesium WebView2）（2026-10-14 … 2026-10-26）
- 可选 **3D** 轨迹（高度）；默认 **Ground** 海拔模式（DEM 采样贴地）；**无多倍高程夸张**（固定 1×）。（初版 `e31fa40` 2026-10-14；贴地/门/光标抛光至 2026-10-26）
- **车辆光标**：航向对齐的长方体约 5×2×1.8 m；2D/3D 与曲线光标双向同步（含圈速对比地图点击）。
- **Gate**：贴轨扁条（无 U 型立柱、无圆点）；**无 S/E 起终点标记**。
- **GateStore 共享**：2D / 3D / 曲线竖线同步；门颜色走 `ColorHex`。（`047dc14`，2026-10-26）
- **底图开** → 隐藏轨迹区网格；**底图关** → 保留网格；去掉左下角 online/terrain 状态角标。
- 在线底图图源逻辑与此前一致（OSM / OpenTopo / Esri / Carto / 自定义 XYZ）。（底图初版 `621121f`，2026-10-10）
- 其它：工具栏控件更紧凑；KML 航向估计；渲染开销与闪烁修复。

### 按文件着色（2026-10-26）
- Files 芯片颜色按钮：色块（swatch）+ 十六进制；曲线与 Track Map 共用 `ColorHex`，按文件路径记忆。（`047dc14`）
- 单文件打开时曲线也跟芯片色（不再只走默认调色板）；色板菜单可见色块（`adfbd86`）。

### KML 导入 / 非破坏裁剪 / 会话（2026-10-12 … 2026-10-26）
- KML/KMZ Phase 1 导入叠画 Track Map。（`4a11f66`，2026-10-12）
- KML 轨迹支持**非破坏裁剪**（不改源文件）。（`320c409`，2026-10-26）
- 启动时从会话记忆恢复上次的 KML/KMZ 离线轨。（`d737c32`，2026-10-14）

### 启动动画 Splash（2026-10-26）
- 启动时显示动画 Splash（`Assets/Splash`）。（`331ecf2`）

### 圈速 / 扇区与对比联动（2026-10-14）
- Lap / Sector 计时分析；圈速操作区保持可见。（`0b086d3`）
- 对比模式：尊重所选 X 轴；对比光标与地图同步；圈速对比下地图点击同步；对比 Elapsed 轴对齐。

### 曲线区（2026-10-09 … 2026-10-14）
- 北京时间轴 / 光标 / 区间 Measure。（`efa4c82`，2026-10-09）
- 多 Plot **共用 X 轴缩放/平移**。（`5396dcc`，2026-10-14）
- 新增通道示例：`slope_pct`（距离窗坡度）。（`4f153f1`，2026-10-14）

### GSpot / iCell VBO（2026-10-14）
- 修正 GSpot/iCell VBO 经纬度解码（Track Map 可用）。（`c5ec939`）
- 重映射 speed / heading / time / height 列；识别标准与移位布局。（`c5a7755` / `307bb8c`）

### 主题 / 语言 UI（2026-10-10 … 2026-10-27）
- 在线底图 + 中/英 i18n + 暗黑/明亮/自定义主题骨架。（`621121f`，2026-10-10）
- Light 主题对齐 VI skin H（白底红黑）；UI chrome 走 `DynamicResource`；Icon Gold。（`1799d73` / `6c6f136`，2026-10-13）
- 顶栏 **Language / Theme** 分体按钮（主面快速切换 + chevron 其它项）；中英切换时宽度固定，避免挤动 Start。（`82bad1b` 等，2026-10-13）
- 半透明亚克力玻璃主题。（`302ffde`，2026-10-27）

### 文档与代理工作流（2026-10-24 … 2026-10-26）
- `AGENTS.md` + `.ai-workflows/`（skills / rules / product-management 模板）。（`e2a183d`，2026-10-24）
- `VERSIONING.md`（SemVer 2.0.0，0.x.y 预览线；本地 packs 基线）。（`e2857a6`，2026-10-25）
- 硬件 / 架构 / 产品文档归入 `docs/`。（`f4166ba`，2026-10-26）

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
│ Dashboard     [⤢]│ Test Results    [⤢] │ Track Map（2D / 3D）     [⤢]│
│ 可定制表盘       │ Accel/Decel/Custom/ │ 工具两行靠右 · Follow/2D·3D │
│ Live / Test 绑定 │ Gate · Lap · Maths  │ Gate 操作在地图左上（图例上）│
├──────────────────┴─────────────────────┴──────────────────────────────┤
│ Curves [Reset][Mark][Add Gate] … [Add Plot][Remove] …编辑…      [⤢] │
│ Online 工具：[Simulator] [GSpot…] [UDP] [Stop]      Rx / Lost / OOO   │
│ Plot：多文件叠加 · 光标/Measure · 对比模式叠图 · Files 芯片（常显）   │
└───────────────────────────────────────────────────────────────────────┘
```

- **顶部栏**：Layout 分区开关、Load、Online/Offline、Clear、录制、已录时长、系统时钟。  
- **四面板最大化**：每个面板标题栏右上角 45° 双箭头；最大化填满主工作区，再点恢复四格。  
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
│   ├─ WorkspaceMaximizeState.cs（四面板最大化）
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
├─ Simulator/   VehicleSimulator · SimulatorSceneBuilder · SpaTrackCenterline（Helix 3D）
├─ Session/     DataSourceSession / OfflineFileSet / RecordingSession / SessionMetadata
├─ Analysis/
│   ├─ SpeedToSpeedEngine / TestDefinition / TestRunResult / CSV 导出
│   ├─ GateStore / GateRunEngine / GatePassCondition / VbtsGateImporter
│   ├─ KmlTrackImporter（KML/KMZ → TrackMap；含非破坏裁剪）
│   ├─ MathsChannelStore / MathsExpression / MathsEnricher
│   ├─ LapTimeEngine（圈速 / 扇区）
│   └─ SelectionMeasure / SampleSource / AnnotatedTestRun
├─ Controls/    DashboardPanel / TestResultsPanel / TrackMapPanel / TrackMap3DController
│               WorkspaceMaximizeIcons / MathsChannelsDialog / GaugeBindingPickerDialog / SessionEditDialog
├─ Assets/      Splash/ · TrackMap3D/（Cesium 静态资源）
├─ docs/        xcom-*.md · architecture/ · hardware/ · product/
├─ tools/       OxTSSmoke（XCOM/NCOM 冒烟）
├─ samples/     oxts-xcom/（示例 .xcom）
├─ AGENTS.md · .ai-workflows/ · VERSIONING.md
├─ ChassisMasterTestSuite.Tests/   xUnit（纯逻辑，无 WPF）
├─ MainWindow.* 外壳、Layout、录制、多文件曲线、光标联动、文件着色
├─ SimulatorWindow.* Helix 3D 仿真窗（Spa 风格赛道 + 追逐相机）
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
| 3D 仿真 | HelixToolkit.Wpf 3.x |
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
2. 点 **Simulator** 打开 Helix 3D 窗（Spa 风格高程赛道），用 WASD / Space / R 驾驶；样本仍经本机 UDP 进主窗：

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
| Reset View | 恢复自动缩放视野（工具栏左侧：Reset View → Mark Region → Add Gate） |
| 多 Plot | **Add Plot** 在 **Remove Plot** 左侧；+ Channel 右侧通道芯片；拖分割条改高度；黄色光标跨图联动；**共用 X 缩放/平移**；标记/裁剪编辑按钮靠右；面板最大化箭头在最右侧 |

### 7. Track Map 与 Gate（2D / 3D）

- 实时/离线轨迹同步曲线光标与 Dashboard；**2D / 3D 可切换**（3D 经 Cesium WebView2）。  
- 工具栏**两行、靠右对齐**：Follow（跟随/居中当前车）、2D·3D、Basemap、瓦片图源等；标题栏最右侧为面板最大化。  
- **Gate 操作**叠在地图视口 **左上角**，位于 **Gates 图例上方**（避免叠在一起）。  
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

- `+ Add Plot` 增加图（位于 `Remove Plot` 左侧）；每图可改名、`Auto Y`、`+ Channel`（通道芯片在右侧）、`Remove Plot`。  
- 各图高度可拖拽调整并记忆；滚轮默认滚动，点击聚焦后再缩放。  
- `X Axis` 可选横轴信号（默认时间）；多图黄色光标联动。  
- Curves 面板标题栏最右侧为最大化/恢复。

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
| 三维仿真车体/世界 +X / +Y / +Z | 前（航向 0°）/ 左 / 上（右手系；Roll/Pitch/Yaw 绕 X/Y/Z） |
| Track Map 米平面 | +X 东 / +Y 北 |

---

## 已完成功能（对照代码）

日期为合入 `main` 的落地日（可核对对应 commit）；同一能力后续抛光仍以首次落地日为主，重大升级另注。

- [x] UDP + DataBus + Simulator（2026-10-07）
- [x] Simulator Helix 3D：Spa 风格封闭高程赛道、软转向 + 单轨横向、追逐相机；UDP 仍推主窗（2026-11-02）
- [x] `IDataSource` + GSpot（WebSocket）（2026-10-09）
- [x] GSpot 多车房间监控（2026-11-02）
- [x] VBO 录制（Start/Pause/Stop）与多文件回放（Load）（2026-10-08）
- [x] 多 Plot / 通道曲线 / 北京时间轴 / 光标联动 / Auto 缩放（2026-10-09）
- [x] 多文件曲线始终叠加 + Test Results 勾选对比叠图（2026-10-10）
- [x] Track Map（轨迹、光标同步、run 高亮）（2026-10-08）
- [x] Layout 分区开关（Dashboard / Test Results / Map / Chart，无 Video）（2026-10-10）
- [x] 四面板最大化/恢复（45° 双箭头；填满主工作区）（2026-11-03）
- [x] Track Map 两行工具栏靠右；Gate 操作地图左上（Gates 图例上方）；Follow 跟随/居中（2026-11-03）
- [x] Curves 工具栏：Reset→Mark→Add Gate；Add Plot 左于 Remove；编辑靠右；最大化最右（2026-11-03）
- [x] Online / Offline / Load / Clear（2026-10-10）
- [x] 可定制 Dashboard（增删/拖动/缩放、Live + Test Results 绑定）（2026-10-10）
- [x] Test Results：Accel / Decel / Custom / Gate + CSV（2026-10-10）
- [x] Gate 管理（颜色、宽度、重命名、地图点选）+ `.vbts` 门导入（2026-10-10）
- [x] Pass Condition + Pass values 高亮（2026-10-10）
- [x] Maths Channels 对话框（基础表达式）（2026-10-10）
- [x] Chart Measure（min/max/avg）（2026-10-09）
- [x] Session 元数据写入 VBO `[SessionData]`（2026-10-10）
- [x] 会话记忆（VBO / `.vbts` 门 / Test Results / Maths）（2026-10-10）
- [x] 通道注册表（实时 vs 文件）（2026-10-09）
- [x] UI 主题（暗黑 / 明亮 / Light H / Icon Gold / 自定义占位）+ 中英语言（分体按钮）（2026-10-10 … 2026-10-13）
- [x] 半透明亚克力玻璃主题（2026-10-27）
- [x] Track Map 在线底图（可开关、多图源）（2026-10-10）
- [x] KML/KMZ 导入（Phase 1：gx:Track / LineString → TrackMap 叠画；两步路速度 m/s→km/h）（2026-10-12）
- [x] Layout：关 Test Results 后 Dashboard|Map 并排可拖（2026-10-12）
- [x] Test Curve UX：滚轮滚动优先、点击聚焦缩放、通道芯片、可调高度、光标联动（2026-10-10）
- [x] Cesium 3D Track Map（Ground 默认、航向光标、贴轨 Gate、无夸张倍率、底图开关网格）（2026-10-14 … 2026-10-26）
- [x] Gate 跨 2D / 3D / 曲线同步（GateStore + ColorHex）（2026-10-26）
- [x] 按文件着色（芯片色块 + 曲线/地图同色，路径记忆）（2026-10-26）
- [x] OxTS XCOM/NCOM/RCOM 离线导入（多流拆分）（2026-10-26）
- [x] KML 非破坏裁剪 + 会话恢复 KML/KMZ（2026-10-26 / 2026-10-14）
- [x] 启动动画 Splash（2026-10-26）
- [x] Lap / Sector 计时与对比↔地图光标同步（2026-10-14）
- [x] 多 Plot 共用 X 轴缩放/平移；`slope_pct` 通道（2026-10-14）
- [x] GSpot/iCell VBO 经纬度与列布局兼容（2026-10-14）
- [x] Light（VI H）/ Icon Gold 等主题 + Language/Theme 分体按钮（2026-10-13）
- [x] `AGENTS.md` / `.ai-workflows` / `VERSIONING.md` / `docs/` 文档布局（2026-10-24 … 2026-10-26）
- [x] xUnit 纯逻辑测试套件（2026-10-09）

---

## 开发路线（剩余）

与用户确认后的规划一致（不做 / 砍掉的项已从路线图移除）。

**已完成（本分支 / 近期）**
- [x] 在线地图底图（开关 + OSM/Esri/Carto/OpenTopo/自定义 XYZ）（2026-10-10）
- [x] 界面语言中/英；Language/Theme 分体按钮；Light（VI H）/ Icon Gold（2026-10-10 … 2026-10-13）
- [x] 皮肤：暗黑 / 明亮 + 自定义配色（占位）（2026-10-10）；亚克力玻璃（2026-10-27）
- [x] KML/KMZ Phase 1 + 非破坏裁剪 + 会话恢复（2026-10-12 … 2026-10-26）
- [x] Layout 关 Test Results 后 Dashboard/Map 并排（2026-10-12）
- [x] Cesium 3D 轨迹图（Ground、航向光标、贴轨门、无夸张）（2026-10-14 … 2026-10-26）
- [x] Gate 2D/3D/曲线同步；按文件着色（2026-10-26）
- [x] OxTS XCOM/NCOM/RCOM 离线导入（2026-10-26）
- [x] Lap/Sector；对比↔地图同步；多 Plot 共用 X 缩放（2026-10-14）
- [x] Splash；`docs/` + AGENTS / `.ai-workflows` / VERSIONING（2026-10-24 … 2026-10-26）
- [x] 四面板最大化/恢复；Track Map / Curves 工具栏布局（Gate 左上、Curves 按钮分区）；Follow（2026-11-03）
- [x] Simulator Helix 3D：Spa 风格高程赛道、软转向 + 单轨模型、追逐相机（UDP 通路不变）（2026-11-02）
- [x] GSpot 多车房间监控（2026-11-02）

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

- 仿真为简化单轨（自行车）模型 + 软转向，高速极限工况仍偏理想。  
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
