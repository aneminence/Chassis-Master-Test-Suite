# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

---

## 最近更新（2026-10-08）

- **UI 外壳重构**：顶部栏 + 导航栏 + 上方三区 + 下方曲线区的完整布局，深色金色主题统一到 `App.xaml`。
- **VBO 记录 / 回放**：录制由 CSV 改为 Racelogic VBO 文本格式，可直接用 VBOX Test Suite 打开验证；新增 `VboReader` 支持离线回放。
- **录制控制**：顶部栏三态录制按钮（`● Start` / `❚❚ Pause` / `▶ Resume`）+ 独立 `■ Stop`，支持暂停续录。
- **Dashboard / Test Results / Track Map 三面板**：实时数值面板可用，Track Map 已实现 GPS 轨迹绘制与比例尺。
- **经纬度符号约定统一**：内部统一「正 = 东经」，VBO 的相反符号只在记录器 / 读取器做换算。
- **配套 VI 设计系统**：建于 `D:\CMTS\VI`（不纳入本工程仓库）。

---

## 设计理念

```text
Acquisition  →  Parsing  →  DataBus  →  Processing  →  Visualization  →  Recording
  数据采集   →  数据解析  →  统一数据  →  实时处理   →   UI 显示      →   原始数据记录
```

核心原则：

1. **数据采集与 UI 解耦** —— UI 不直接依赖 UDP
2. **原始数据与计算数据分离** —— 任何计算结果都不覆盖原始数据
3. **DataBus 是实时数据中心** —— 模块之间通过 DataBus 交互，不互相直接调用
4. **数据源可替换** —— Simulator 只是数据源之一，将来可替换为 CAN / VBOX / IMU / RTK 实车设备

---

## 当前架构

```text
VehicleSimulator (100 Hz)
        │  UdpPacket (89 字节小端二进制)
        ▼
   UdpSender ──UDP 127.0.0.1:50000──► UdpReceiver
                                          │  解析 + 丢包/乱序统计
                                          ▼
                                    VehicleSample  (统一数据模型)
                                          │
                                    DataBus (BoundedChannel, 容量 2000)
                                          │
                                          ▼
                                  数据消费循环 ConsumeDataAsync
                                          │
                    ┌─────────────────────┼──────────────────────┐
                    ▼                     ▼                      ▼
              VboRecorder           _sampleHistory          网络统计 Rx / Lost / OOO
          (后台线程 → VBO 文件)  (上限 1,000,000 条)
                                          │
                                  DispatcherTimer (100 ms)
                                          │
                    ┌─────────────────────┼──────────────────────┐
                    ▼                     ▼                      ▼
             DashboardPanel        ScottPlot 曲线区         TrackMapPanel
             5 项实时数值      (多 Plot / 多 Channel)   (GPS 轨迹 + 比例尺)

离线回放：VboReader ──► _sampleHistory ──► 同一条曲线 / 轨迹管线
```

---

## 界面结构

```text
┌───────────────────────────────────────────────────────────────────────┐
│ CMTS  Chassis Master Test Suite   ● Online  [● Start] [■ Stop]        │  顶部栏
│                                   Elapsed 00:00:12.3  15:04:21        │
├───────────────────────────────────────────────────────────────────────┤
│ ⊞ Dashboard │ ▤ Data │ ⌁ Analysis │ ▷ Replay │ ⚙ Settings            │  导航栏
├──────────────────┬─────────────────────┬──────────────────────────────┤
│ Dashboard        │ Test Results        │ Track Map                    │  上方三区
│ 实时数值 5 项    │ 自动评价（未实现）  │ GPS 轨迹 + 比例尺网格        │  （可拖动分隔条）
│ Speed / Yaw Rate │ "Not implemented    │ Lat / Lon / Alt              │
│ Lat / Long Acc.  │  yet"               │                              │
│ Steering Angle   │                     │                              │
├──────────────────┴─────────────────────┴──────────────────────────────┤
│ ══════════════════ 横向分隔条（可拖动）══════════════════════════════ │
├───────────────────────────────────────────────────────────────────────┤
│ Test Data Curves                                                      │  曲线区
│ [X Axis ▾] [X Auto Scale] [+ Add Plot] [Reset View] [Simulator]       │
│                                              Rx 0  Lost 0  OOO 0       │
│ ┌─ Plot 1 ─── Plot Name [        ] [Auto Y] [+ Channel] [Remove Plot]─┐│
│ │  Channel [Speed ▾]  km/h  ×                                        ││
│ │  （ScottPlot 实时曲线）                                             ││
│ └────────────────────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────────┘
```

- **顶部栏**：Logo、网络状态指示（静态）、录制控制、已录时长 `Elapsed`、系统时钟。
- **导航栏**：`Dashboard` 与 `Replay` 可用；`Data` / `Analysis` / `Settings` 为占位项，暂时禁用。
- **Replay 页**：`📂 Open VBO…` 加载录制文件后自动切回 Dashboard，离线数据进入同一条曲线 / 轨迹管线。

---

## 项目结构

```text
Chassis Master Test Suite/
├─ Core/
│   ├─ VehicleSample.cs        统一车辆数据模型（不可变）
│   └─ DataBus.cs              实时数据总线（BoundedChannel，容量 2000）
├─ Communication/
│   ├─ UdpPacket.cs            UDP 线格式对象
│   ├─ UdpPacketSerializer.cs  89 字节小端序列化 / 反序列化
│   ├─ UdpReceiver.cs          接收 + 解析 + 质量统计
│   └─ UdpSender.cs            发送（Simulator 使用）
├─ Recorder/
│   ├─ VboRecorder.cs          异步 VBO 记录器（Racelogic / VBOX 兼容）
│   ├─ VboReader.cs            VBO 解析器（离线回放）
│   └─ CsvRecorder.cs          异步 CSV 记录器（早期实现，当前未接线）
├─ Simulator/
│   └─ VehicleSimulator.cs     人工驾驶车辆运动学模型
├─ Controls/
│   ├─ DashboardPanel.xaml(.cs)   实时数值面板（固定 5 项）
│   ├─ TestResultsPanel.xaml(.cs) 自动评价面板（占位，未实现）
│   ├─ TrackMapPanel.xaml(.cs)    GPS 轨迹图 + 屏幕层比例尺网格
│   └─ TrackProjection.cs         经纬度 / 米平面 / Web Mercator 投影
├─ App.xaml(.cs)               深色金色主题与全局控件样式
├─ MainWindow.xaml(.cs)        主界面：外壳 / 录制控制 / 数据消费 / Plot 系统
├─ SimulatorWindow.xaml(.cs)   三维车辆仿真视景
└─ rebuild.cmd                 一键清理 + 重建脚本
```

---

## 环境要求

| 项目 | 版本 |
|---|---|
| 操作系统 | Windows |
| .NET | 10.0（`net10.0-windows`） |
| UI 框架 | WPF |
| 绘图库 | ScottPlot.WPF 5.1.59 |
| IDE | Visual Studio 2022 / 2026 |

> 编译时会出现 `NU1701` 警告（`SkiaSharp.Views.WPF` 兼容性），
> 这是已知情况，**不影响运行**。

---

## 编译与运行

```cmd
cd /d "D:\CMTS\Chassis Master Test Suite"
dotnet build
dotnet run
```

或在 Visual Studio 中直接 `F5`。

### 一键重建脚本 rebuild.cmd

```cmd
rebuild.cmd
```

依次执行：**强杀正在运行的 exe → 删除 `bin` / `obj` → `dotnet build`**。

适用于两种情况：

- Visual Studio 的 XAML 智能感知缓存过期，报出一堆并不存在的 `CS0103`（「不存在名称 XXX」），清理重建即可恢复；
- 上一次运行的 exe 仍被占用，导致 `MSB3021` / `MSB3027` 复制失败。

`bin` / `obj` 均为可再生目录，已在 `.gitignore` 中排除。

---

## 使用方法

### 1. 启动

启动 CMTS 后**自动开始接收 UDP 数据**（`127.0.0.1:50000`），
但**不会自动开始录制** —— 录制由顶部栏按钮显式控制。

### 2. 录制控制（顶部栏）

| 按钮 | 作用 |
|---|---|
| `● Start` | 新建 VBO 文件并开始录制 |
| `❚❚ Pause` | 暂停写入（实时曲线 / 数值 / 数据消费照常运行） |
| `▶ Resume` | 继续写入**同一个文件** |
| `■ Stop` | 结束录制并关闭文件 |

只有从 `Stopped` 状态开始才会新建文件；暂停后恢复不会另起文件。
`Elapsed` 显示最新样本与首样本的时间差，停止后显示 `--`。

### 3. 打开 Simulator

点击曲线区的 **Simulator** 按钮打开三维仿真窗口，驾驶控制：

| 按键 | 功能 |
|---|---|
| `W` | 加速 |
| `S` | 制动 |
| `A` | 左转 |
| `D` | 右转 |
| `Space` | 紧急制动 |
| `R` | 车辆复位 |

> 当前版本**没有全局键盘快捷键**，主界面的所有操作均为鼠标点击。

### 4. Dashboard 三区

- **Dashboard**：Speed（km/h）、Yaw Rate（deg/s）、Lateral Accel.（m/s²）、Longitudinal Acc.（m/s²）、Steering Angle（deg）。`Customize` 暂未开放。
- **Test Results**：占位面板，自动评价功能尚未实现。
- **Track Map**：GPS 轨迹，右下角显示 Lat / Lon / Alt，随缩放更新比例尺网格。

### 5. 曲线区

- `X Axis` 选择横轴信号（默认 `Time`），`X Auto Scale` 默认勾选。
- `+ Add Plot` 新增图表；每个 Plot 可改名、切换 `Auto Y`、`+ Channel` 增加通道、`Remove Plot` 删除。
- 每个通道行可选择信号（下拉）并显示单位，`×` 删除该通道。
- `Reset View` 恢复全部自动缩放。
- 右侧 `Rx` / `Lost` / `OOO` 为网络接收统计。

### 6. 离线回放

1. 切到 **Replay** 页，点击 `📂 Open VBO…`
2. 默认打开 `Recordings` 目录，选择 `CMTS_yyyyMMdd_HHmmss.vbo`
3. 加载成功后自动切回 Dashboard，并显示 `{count} samples loaded`
4. 历史数据进入与实时相同的曲线 / 轨迹管线

### 数据记录位置

```text
bin\Debug\net10.0-windows\Recordings\CMTS_yyyyMMdd_HHmmss.vbo
```

---

## VBO 数据格式

录制文件采用 **Racelogic VBO 文本格式**，目标是能被 **VBOX Test Suite** 直接打开。

### 文件结构

固定 6 段顺序，首行为 `File created on ...`：

```text
[header]          通道全名
[channel units]   通道单位
[comments]        通道数、经纬度单位说明、导出时间
[SessionData]     时区与会话信息
[column names]    列短名（单行、空格分隔）
[data]            数据行
```

### 列定义（11 列 = 2 时间 + 9 数据）

| # | [column names] | [header] | [channel units] | 来源字段 | 格式 |
|---|---|---|---|---|---|
| 1 | `time` | time | s | `Timestamp` → UTC `HHmmss.fff` | — |
| 2 | `Elapsed_time` | Elapsed time | s | 相对第一条数据 | F4 |
| 3 | `velocity` | velocity kmh | km/h | `SpeedKph` | F3 |
| 4 | `Longacc` | Long accel g | g | `LongitudinalAcceleration` / 9.80665 | F6 |
| 5 | `Latacc` | Lat accel g | g | `LateralAcceleration` / 9.80665 | F6 |
| 6 | `Yaw_Rate` | Yaw rate | deg/s | `YawRate` | F6 |
| 7 | `heading` | heading | deg | `Heading` | F6 |
| 8 | `lat` | latitude | arcmin | `Latitude` × 60 | F8 |
| 9 | `long` | longitude | arcmin | **−**`Longitude` × 60 | F8 |
| 10 | `height` | height | m | `Altitude` | F3 |
| 11 | `Z_Accel` | Z accel g | g | `VerticalAcceleration` / 9.80665 | F6 |

### 实现要点（踩坑记录）

- **`[header]` 名称必须对齐 Racelogic 命名**（`velocity kmh` / `Long accel g` / `Z accel g` …），
  否则 VBOX Test Suite 认不出通道。
- **`[column names]` 必须写在同一行、空格分隔、末尾留一个空格。**
  曾写成一列一行，结果 VBOX 报 `primary channel(s) missing. Speed`，通道表全部失效。
- 文件编码为 **UTF-8 无 BOM**，换行强制 **CRLF**。
- `[channel units]` 不照抄外部 VBO 文件 —— 实测 Racelogic 该段单位与列存在错位，不可信。
- `Elapsed_time` 以**第一条数据**为时间原点（首行必为 `0.0000`）。
  因此暂停后恢复时，首行 Elapsed 会自然跳过暂停时长，无需额外补偿。
- 采样率**不做任何假设**：记录器使用后台写线程 + 有界 Channel，
  按真实时间约每 1000 ms Flush 一次。
  （旧实现用 `Sequence % 200` 隐含 200 Hz 假设，已被移除。）
- `Dispose` 时排空队列、等待写入完成并做最后一次 Flush，
  刻意**不使用 CancellationToken**，以免关闭时丢数据。

### VboReader（回放解析）

- 按 `[段名]` 切分，**只解析 `[data]` 段**。
- 列定义**以 `[column names]` 为准，不使用 `[header]`**（header 名可能含空格导致列数错位）。
- **完全不信 `[channel units]`**，单位由内置规则决定，列名大小写不敏感，缺失列记 `0.0`。
- 时间列 `HHMMSS.mmm` **只能还原「当天 UTC 起毫秒数」，不含日期，不能当作绝对时间使用**。

---

## 坐标系约定

### 三维场景

三维场景经过实测验证，约定如下：

| 轴 | 含义 |
|---|---|
| `+X` | 车辆左侧（屏幕上表现为左） |
| `+Y` | 高度向上 |
| `+Z` | Heading = 0° 时的前方 |

**车头方向向量：**

```text
forward = ( sin(Heading), cos(Heading) )
```

| Heading | 含义 |
|---|---|
| `0°` | 正前方（+Z） |
| `+` | 左转 |
| `-` | 右转 |

> 位置积分、车辆模型旋转、摄像机跟随**必须使用同一个 forward 定义**。
> 任何一处写成 `-sin(Heading)` 都会导致「车头朝左、车却向右开」。

### 经纬度符号

| 字段 | 约定 |
|---|---|
| `Latitude` | 正 = 北纬，负 = 南纬 |
| `Longitude` | 正 = 东经，负 = 西经 |

内部数据模型统一使用上述约定。

**但 Racelogic VBO 的经度符号相反**（负值表示东经），
换算只在两处进行，绝不外泄：

- 记录时：`VboRecorder` 写入 `-Longitude × 60`（角分）
- 读取时：`VboReader` 还原 `-long / 60.0`（度）

> 直接拷贝经纬度而不做符号换算，会导致轨迹在地图上**左右镜像**。

### Track Map 投影

`TrackProjection` 提供三套坐标系：

| 坐标系 | 用途 |
|---|---|
| 经纬度（deg） | 数据模型与 VBO 交换 |
| 局部米平面（+X 正东 / +Y 正北，原点在轨迹中心） | 轨迹绘制与比例尺 |
| Web Mercator（米） | 预留在线地图瓦片叠加 |

- 纬度按 `111320.0` m/° 换算，经度按 `cos(lat)` 收缩
  （北纬 33° 处 1° 经度约 93 km，不做收缩会把圆轨迹拉成椭圆）。
- 投影原点只在**换数据源**时重建（锚定第一条样本），实时采集期间保持不变，
  否则整条轨迹会随原点漂移。
- 比例尺取 `1 / 2 / 5 / 10 …` 的整数步长，锚定在屏幕上：
  拖动不移动，仅在缩放时重绘。

---

## 数据格式

`VehicleSample` 字段（实时数据模型，也是 CSV 列）：

| 字段 | 单位 | 说明 |
|---|---|---|
| `Timestamp` | ms | Unix 毫秒时间戳 |
| `Sequence` | — | 包序号，用于丢包检测 |
| `SpeedKph` | km/h | 车速 |
| `LongitudinalAcceleration` | m/s² | 纵向加速度 |
| `LateralAcceleration` | m/s² | 横向加速度 |
| `VerticalAcceleration` | m/s² | 垂向加速度 |
| `YawRate` | deg/s | 横摆角速度 |
| `Latitude` | deg | 纬度（正 = 北纬） |
| `Longitude` | deg | 经度（正 = 东经） |
| `Altitude` | m | 海拔 |
| `Heading` | deg | 航向角 |

> `CsvRecorder` 仍保留 CSV 输出能力（UTF-8 带 BOM），
> 但当前界面只使用 VBO 记录，CSV 通道**尚未接线**。

---

## 开发路线

- [x] UDP 实时接收 / 解析 / DataBus
- [x] 统一数据模型 `VehicleSample`
- [x] 三维车辆仿真（人工驾驶）
- [x] 实时曲线（多 Plot / 多 Channel / 可选 X 轴）
- [x] 实时数值显示
- [x] UI 外壳（顶部栏 / 导航栏 / 三面板 / 曲线区）
- [x] 深色金色主题（全局样式集中于 `App.xaml`）
- [x] VBO 记录（VBOX / Racelogic 兼容）
- [x] VBO 离线回放
- [x] Track Map（GPS 轨迹图 + 比例尺）
- [ ] 自动测试评价（0-100 km/h、制动距离、稳态区间等）
- [ ] 在线地图底图叠加（Mercator 投影已就绪）
- [ ] 通用 Channel 注册表（字段元数据系统）
- [ ] VBTS 风格 X/Y 轴选择
- [ ] 时间轴：UTC / 北京时间 / 本地时间 / 相对时间
- [ ] 距离轴
- [ ] 测试项目 / 工况管理
- [ ] 测试报告生成
- [ ] Data / Analysis / Settings 页面
- [ ] 方向盘转角采集（需扩展 UDP 协议与 `VehicleSample`）

---

## 已知限制

- 车辆模型为**纯运动学**自行车模型，未包含轮胎侧偏特性。
  高速转向时横摆率与侧向加速度会偏大（实车不可能达到），
  后续需引入线性二自由度模型。
- **时间基准**：VBO 的 `time` 列只有 `HHMMSS.mmm`，不含日期；
  回放时只能还原「当天 UTC 起毫秒数」，尚不能当作绝对时间使用。
- **曲线绘制为全量重绘**：每次刷新都会复制完整历史并重建所有曲线，
  数据量增大后需要改为固定时间窗口 + 降采样。
- **Track Map 显示上限 50,000 点**，超出按步长抽稀；刷新限流 250 ms。
- **Test Results 面板尚未实现**，仅占位骨架。
- **Steering Angle 恒为 0**：UDP 协议与 `VehicleSample` 均无该字段，
  当前为硬编码占位值。
- **Data / Analysis / Settings 三个导航页未启用**。
- **无键盘快捷键**，全部操作为鼠标点击。
- `CsvRecorder` 仍在代码库中，但已不在 UI 链路上。

---

## 相关资源

- VI 设计系统（配色、字体、组件规范）：`D:\CMTS\VI`
  —— 独立于本工程仓库，随项目并行演进。
