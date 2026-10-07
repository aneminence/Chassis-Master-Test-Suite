# Chassis Master Test Suite (CMTS)

面向**汽车底盘与车辆动态性能测试**的实时数据采集、监控与记录软件。

目标形态：**VBOX + VBTS 的一体化简化版**。

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
                                    DataBus (Channel, 容量 2000)
                                          │
                          ┌───────────────┴───────────────┐
                          ▼                               ▼
                  数据消费循环                       CsvRecorder
                          │                      (后台线程 → CSV)
                          ▼
                  DispatcherTimer (100 ms)
                          │
              ┌───────────┼────────────┐
              ▼           ▼            ▼
          实时数值    网络统计      ScottPlot 曲线
```

---

## 项目结构

```text
Chassis Master Test Suite/
├─ Core/
│   ├─ VehicleSample.cs        统一车辆数据模型（不可变）
│   └─ DataBus.cs              实时数据总线（Channel）
├─ Communication/
│   ├─ UdpPacket.cs            UDP 线格式对象
│   ├─ UdpPacketSerializer.cs  89 字节小端序列化 / 反序列化
│   ├─ UdpReceiver.cs          接收 + 解析 + 质量统计
│   └─ UdpSender.cs            发送（Simulator 使用）
├─ Recorder/
│   └─ CsvRecorder.cs          异步 CSV 原始数据记录（不丢数据）
├─ Simulator/
│   └─ VehicleSimulator.cs     人工驾驶车辆运动学模型
├─ MainWindow.xaml(.cs)        主界面：Plot 系统 / 实时数值 / 数据消费
├─ SimulatorWindow.xaml(.cs)   三维车辆仿真视景
└─ App.xaml(.cs)
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

---

## 使用方法

1. 启动 CMTS，主窗口自动开始接收 UDP 数据并录制 CSV
2. 点击 **Simulator** 按钮打开三维仿真窗口
3. 驾驶控制：

| 按键 | 功能 |
|---|---|
| `W` | 加速 |
| `S` | 制动 |
| `A` | 左转 |
| `D` | 右转 |
| `Space` | 紧急制动 |
| `R` | 车辆复位 |

4. 主界面右侧显示实时数值与网络质量
5. 曲线可自由添加 Plot、添加 Channel，并选择 X 轴信号

### 数据记录位置

```text
bin\Debug\net10.0-windows\Recordings\CMTS_yyyyMMdd_HHmmss.csv
```

---

## 坐标系约定

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

---

## 数据格式

`VehicleSample` 字段（同时也是 CSV 列）：

| 字段 | 单位 | 说明 |
|---|---|---|
| `Timestamp` | ms | 数据源相对时间 |
| `Sequence` | — | 包序号，用于丢包检测 |
| `SpeedKph` | km/h | 车速 |
| `LongitudinalAcceleration` | m/s² | 纵向加速度 |
| `LateralAcceleration` | m/s² | 横向加速度 |
| `VerticalAcceleration` | m/s² | 垂向加速度 |
| `YawRate` | deg/s | 横摆角速度 |
| `Latitude` | deg | 纬度 |
| `Longitude` | deg | 经度 |
| `Altitude` | m | 海拔 |
| `Heading` | deg | 航向角 |

---

## 开发路线

- [x] UDP 实时接收 / 解析 / DataBus
- [x] 统一数据模型 `VehicleSample`
- [x] 三维车辆仿真（人工驾驶）
- [x] 实时曲线（多 Plot / 多 Channel / 可选 X 轴）
- [x] 实时数值显示
- [x] CSV 原始数据记录
- [ ] 通用 Channel 注册表（字段元数据系统）
- [ ] VBTS 风格 X/Y 轴选择
- [ ] 时间轴：UTC / 北京时间 / 本地时间 / 相对时间
- [ ] 距离轴
- [ ] 离线数据回放（CSV / VBO）
- [ ] 测试项目 / 工况管理
- [ ] 自动测试评价（0-100 km/h、制动距离、稳态区间等）
- [ ] 测试报告生成

---

## 已知限制

- 车辆模型为**纯运动学**自行车模型，未包含轮胎侧偏特性。
  高速转向时横摆率与侧向加速度会偏大（实车不可能达到），
  后续需引入线性二自由度模型。
- 时间基准目前只有数据源相对时间，尚无绝对时间（UTC）。
- 曲线绘制当前为全量重绘，数据量增大后需要改为固定时间窗口。
