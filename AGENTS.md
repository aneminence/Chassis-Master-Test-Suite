# CMTS AI 开发规范

面向 Grok Bot 与任意能读仓库文件的 Agent。  
本仓库**没有** ECC 插件 / Hooks / 全局安装；规矩写在本文件与 `.ai-workflows/` 里。Agent 须 **主动 Read** 相关文件后再动手。

用大白话：先看说明书，再改代码；测过才能说测过。

## 资料在哪

完整索引：`.ai-workflows/README.md`（必读入口之一）

**版本号**：仓库根目录 `VERSIONING.md`（SemVer 2.0.0；开发期 `0.x.y`；稳定后 `1.0.0+`）。改 `<Version>` / 打 tag / 发 Release 前必须先 Read，禁止临时编造版本号。

**产品管理**：需求 / PRD / Roadmap / 优先级见 `.ai-workflows/product-management/`（规范与模板）与 `docs/product/`（愿景与 PRD 正文）。产品决策与 ECC 工程规范**互不覆盖**——范围听产品侧，实现与测试听工程侧。小 Bug 不强制完整 PRD；重大或跨模块需求须按 `product-management/process.md` 评估。

```
.ai-workflows/
  custom/              ← CMTS 自制短清单（日常优先）
  product-management/  ← 产品需求 / PRD / 优先级（与工程互不覆盖）
  skills/              ← 精选的 ECC 官方技能（MIT，见 SOURCE.md）
  agents/              ← 精选的 ECC 角色说明
  rules/               ← 精选的 common + csharp 规则
```

**同名主题**：`custom/` 针对本仓库；`skills/` 是通用官方稿。冲突时 **CMTS 日常以 custom 为准**，官方稿作补充。

## 开工五步（每次新任务）

1. **Read** 本文件（`AGENTS.md`）与 `.ai-workflows/README.md`。
2. **看清任务范围**：只做用户点名的事；无关功能先问再动。若属新功能/跨模块，先确认是否已按 `.ai-workflows/product-management/process.md` 评估（小 Bug 可豁免 PRD）。
3. **按需打开技能**：写测试 → `custom/csharp-testing`；收工验证 → `custom/verification-loop`；审查 → `custom/code-review`；架构/安全再读 `agents/` 或 `skills/`。
4. **在正确分支上改**：从最新 `main` 拉功能分支；PowerShell **禁止**用 `&&` 串命令。
5. **改完按「修改代码后」清单做完再汇报**——未运行的测试 **不得**写成「已通过」。

## 开发新功能前

1. **分析现有架构**：`Core` / `Controls` / `Recorder` / `Analysis` / `Communication` / `Session`；数据流 `VehicleSample` → enricher → 图 / TrackMap / Dashboard。
2. **制定修改计划**：拟改文件、接口影响、是否触及 VBO / KML / GSpot / 在线源。
3. **确定验收标准**：可观察行为、自动化测试、适用的 F5 项（见 `custom/verification-loop`）。
4. 大改时可 Read `.ai-workflows/agents/architect.md` 或 `skills/change-impact/`。

## 修改代码后

1. **编译与测试**：严格按 `.ai-workflows/custom/verification-loop/SKILL.md`。  
   - 跑过的写结果；**没跑的不要声称通过**。
2. **审查**：按 `.ai-workflows/custom/code-review/SKILL.md`；需要时再读 `agents/code-reviewer.md` / `skills/security-review/`。
3. **汇报**：修改文件、测试结果、剩余风险、未做事项；需用户确认的决策单独列出。

## 硬性约束

- **未经确认不得修改无关功能**（含顺手重构、改默认主题、改未点名的格式）。
- **不合并 `main`**，除非用户明确要求。
- **版本号**：遵守根目录 `VERSIONING.md`；未经用户要求发版/bump，不得改 `.csproj` 的 `Version` / 打 `v*` tag。
- 不提交密钥、隐私路径、大体积原始路测（`*.vbo` 默认忽略；测试夹具除外）。
- 破坏性 git（force-push、reset --hard、amend 已推送）须先说明并获确认。
- 不向外发邮件/消息等不可逆操作，除非用户逐条批准。
- **不要把 ECC 装进任何 Agent 全局配置**；资料只留在本仓库 `.ai-workflows/`。

## 仓库速查

- 技术栈：WPF / `net10.0-windows`；测试项目 `ChassisMasterTestSuite.Tests` 为 `net10.0`（链接纯逻辑源码）。
- 离线轨迹：VBO / KML → OfflineFileSet → TrackMap。
- GSpot：实时 WS 与设备 `.vbo` 列含义可能不同于 Racelogic；改解析前先看测试与 `custom/cmts-gnss-imu.md`（占位）。
- 机器：在用户 PC（如 P16）改代码；命令分条执行。

## English (short)

Read `AGENTS.md` + `.ai-workflows/README.md` first. Product scope/PRD: `.ai-workflows/product-management/` + `docs/product/` (does not override engineering QC). Prefer `custom/` for day-to-day CMTS work; official ECC copies under `skills/` `agents/` `rules/` are MIT supplements (`SOURCE.md`). Never claim tests passed if you did not run them. Version bumps: read `VERSIONING.md` first; do not invent versions. Do not touch unrelated features without confirmation. Do not install ECC into agent globals.
