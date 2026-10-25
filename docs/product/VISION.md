# CMTS 产品愿景（草案）

| 字段 | 内容 |
|------|------|
| 状态 | 草案（随 Roadmap 演进） |
| 更新日期 | 2026-10-25（Asia/Shanghai） |
| 依据 | 仓库 README、本地产品架构约定、现行协作口径 |

## 定位

**Chassis Master Test Suite（底盘大师测试套件）** 面向汽车底盘与车辆动态性能测试，提供采集、监控、记录与分析结果的统一平台能力。

当前仓库中的可运行主体是 **Windows 桌面端（CMTS Studio 方向）**：.NET 10 WPF + ScottPlot，覆盖 Online（UDP / GSpot / 仿真）与 Offline（VBO / KML 等），含 Dashboard、Test Results（Accel/Decel/Custom/Gate）、Track Map（含底图与 3D 相关能力视 `main` 而定）、多曲线与会话记忆等。

长期品牌结构按四条产品线理解（详见团队产品架构约定，非本仓库硬件实现清单）：

| 线 | 含义 | 与本仓库关系（现状） |
|----|------|----------------------|
| **C Computing** | 计算与软件客户端 | **主战场**：Win WPF Studio；Android Field 仅讨论中 |
| **M Modules** | Logger / DAQ 等采集设备 | 协议与数据约定的对接面；本阶段以软件兼容为主 |
| **T Transducers** | 轮速 / IMU / 力 / 位移等 | 通道语义与标定；勿当纯 UI |
| **S Solutions** | Dynamics / Safety / Energy 等方案包 | 试验流程与限值/报告方向；多数仍在规划 |

口号级意图（架构约定）：**One Platform. Complete Testing.** —— 软硬件与算法可组合，现场与办公室同一套数据语言。

## 当前阶段目标（软件）

1. 把 **Win Studio** 做成可靠的试车/分析主力：Offline 打开 → 看轨迹/曲线 → 算 Accel/Gate → 导出，路径短、结果可信。  
2. Online 与多源（UDP/GSpot）稳定可用；地图与主题服务场边可读，而非堆功能。  
3. 用产品规范（本目录）约束「做什么」；用 ECC 风格工程规范约束「怎么做」。  
4. **不**在未拍板时默认开做全量移动端 Online / 现场标定。

## 明确不追求（现行）

与 README「明确不做」及群结论对齐，例如：Gate `.spl` 主路径、原生 VBOX Online、未接线的 `CsvRecorder` 当正式能力、把 Win 三栏信息密度硬搬手机等。具体以 README + 已批准 PRD 为准。

## 演进规则

- 愿景变更须用户确认，并在 `ROADMAP.md` 留一句痕迹。  
- 本文件**不**替代 `VERSIONING.md`，也**不**授权发版。
