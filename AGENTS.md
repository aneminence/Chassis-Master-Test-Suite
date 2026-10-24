# CMTS AI 开发规范

面向 Grok Bot 与任意能读仓库文件的 Agent。本仓库**没有** ECC 插件 / Hooks / 持续学习运行时；规范靠 `AGENTS.md` + `.ai-workflows/*/SKILL.md` 约定，Agent 须主动 **Read** 相关技能后再执行。

## 目录

```
CMTS/
├── AGENTS.md                 ← 本文件（总则）
├── .ai-workflows/
│   ├── code-review/SKILL.md
│   ├── verification-loop/SKILL.md
│   └── csharp-testing/SKILL.md
├── Core / Controls / Recorder / Analysis / …
└── ChassisMasterTestSuite.Tests/
```

## 何时读取技能

| 时机 | 必读 |
|------|------|
| 开发新功能 / 改架构前 | 本文件全文 |
| 写或改单元测试时 | `.ai-workflows/csharp-testing/SKILL.md` |
| 代码改完、准备提交前 | `.ai-workflows/code-review/SKILL.md` |
| 声称「完成 / 可合并」前 | `.ai-workflows/verification-loop/SKILL.md` |

不要凭记忆臆造流程；打开对应 `SKILL.md` 按清单执行。

## 开发新功能前

1. **分析现有架构**：相关命名空间（`Core` / `Controls` / `Recorder` / `Analysis` / `Communication` / `Session`）、数据流（`VehicleSample` → `SampleEnricher` → 图 / TrackMap / Dashboard）、现有测试与分支状态。
2. **制定修改计划**：列出拟改文件、接口影响、是否触及 VBO/KML/GSpot/在线数据源。
3. **确定验收标准**：可观察行为（UI / 通道值 / 轨迹）、自动化测试条目、手动 F5 检查项。
4. **分支**：从最新 `main` 拉功能分支；勿在脏工作区上混做无关提交。PowerShell **禁止** `&&` 链命令。

## 修改代码后

1. **编译与测试**：按 `verification-loop` 技能（至少 Debug/`dotnet test`；准备交付时再 Release）。
2. **按 code-review 技能审查**：焦点 diff，不扫无关目录。
3. **按 verification-loop 技能验证**：含适用的 F5 清单。
4. **汇报**：修改文件列表、测试结果、剩余风险、未做事项。需要用户确认的决策单独列出。

## 硬性约束

- **不得在未经确认的情况下修改无关功能**（含「顺手重构」、改默认主题、改未点名的导入格式等）。
- 不合并 `main`，除非用户明确要求。
- 不提交密钥、本机路径隐私、大体积原始路测文件（`*.vbo` 默认 gitignore；测试夹具除外）。
- 破坏性 git（force-push、reset --hard、amend 已推送提交）须先说明影响并获确认。
- 涉及邮件 / Slack / 对外发送等不可逆操作时，只起草、等用户批准。

## 仓库要点（给 Agent 的速查）

- 技术栈：WPF / `net10.0-windows`；纯逻辑测试项目 `ChassisMasterTestSuite.Tests` 为 `net10.0`（链接源码，无 WPF 引用）。
- 离线轨迹：`VboReader` / KML 导入 → `OfflineFileSet` → TrackMap（`TrackProjection.FilterGpsOutliers`）。
- GSpot：实时 WS（`Communication/GSpot`）与 GSpot 设备 `.vbo` 列含义可能与 Racelogic 不同；改解析前先读现有测试与注释。
- 机器：用户 PC（如 P16）上改代码；PowerShell 分条执行，不用 `&&`。

## English (short)

Before features: analyze architecture, plan changes, define acceptance criteria. After changes: build+test, Read and follow `code-review` then `verification-loop`, report files/tests/risks. Do not touch unrelated features without confirmation. Skills live under `.ai-workflows/`; Read them when the table above says so. No ECC runtime—discipline is file-based.
