# CMTS 产品需求生命周期

从「有人提一句」到「版本进包」的标准路径。工程实现细节仍走 `AGENTS.md` / `.ai-workflows/custom/`。

## 总览

```text
提议 → 需求评估 →（必要时）PRD → 审阅批准 → 技术方案
    → 开发任务 → 测试/验收 → 版本发布（VERSIONING.md）
```

每一步的**决策权**见 `roles.md`。本文件只定义「要不要做文档、何时可进入下一阶段」。

---

## 1. 功能提议（Proposal）

来源示例：用户口述、群内试验同学反馈、美化建议、竞品对照、Roadmap 条目。

最低记录（可极短）：

- 谁提出、场景（场边 / 台架 / 办公室 Offline 等）
- 要解决的问题（一句话）
- 涉及产品线：Computing / Modules / Transducers / Solutions（C/M/T/S）
- 粗优先级直觉（见 `prioritization.md`）

存放：可先记在 `docs/product/ROADMAP.md`「候选」区，或开 Issue / 群结论纪要；**重大项**随后补 PRD。

---

## 2. 需求评估（Needs assessment）

回答四个问题后再投入写代码：

1. **用户是谁？**（试车工程师、标定、办公室分析、司机场边……）
2. **不做会怎样？**（有无绕过方案：VBTS、Excel、现有 CMTS 手工步骤）
3. **范围边界？**（只要 Offline？要不要动 UDP/GSpot/硬件协议？）
4. **是否触发强制 PRD？**（见下节）

评估结论：`做 / 延后 / 不做`，并写进 Roadmap 或 PRD 状态栏。

---

## 3. 何时必须写 PRD

### 必须（完整 PRD，用 `templates/prd.md`）

- 用户可见的**重大新功能**（新面板、新试验类型、新数据源、新平台客户端等）
- **跨模块**需求：例如同时动 Communication + Analysis + UI，或软件 ↔ 硬件/协议/传感器约定
- 可能影响 **VBO / KML / `.vbts` / UDP / GSpot** 兼容性或默认行为的变更
- 新产品线切片（如 Android Field MVP、Logger 对接约定）在立项时

### 不强制完整 PRD（可只记验收点或 Issue）

- 小型 Bug 修复、崩溃、错别字、纯视觉微调（不改交互语义）
- 仅测试 / CI / 文档 / 工作流文件
- 已批准 PRD 下的**实现子任务**拆分（用用户故事或工程任务即可）

### 灰色地带

拿不准时：**先写一页纸 PRD**（问题、范围、非目标、验收），比事后争论便宜。由用户或 Chief 一锤定音是否升级为完整 PRD。

---

## 4. PRD 审阅

1. 作者按模板写到 `docs/product/prds/`（命名见该目录 README）。
2. **产品侧审**：范围、优先级、验收是否可测、与 VISION/ROADMAP 一致。
3. **工程侧审（ECC）**：可行性、风险、工作量粗估、是否触碰硬约束（版本、无关改动禁止等）——**不改写产品目标**，只标风险与建议拆分。
4. **试验侧（可选）**：使用路径是否像试车现场。
5. 状态改为「已批准」后，才允许大范围开工；批准前允许 spike / 探针分支，但不得当正式范围承诺。

---

## 5. 技术方案

- 由 **CMTS 功能**（及需要时质量）按 `AGENTS.md`「开发新功能前」输出：触点模块、拟改文件、风险、自动化测试点。
- 技术方案服从已批准 PRD 的范围；若发现必须扩 scope，**退回产品评估**，不得在 PR 里偷偷加需求。

---

## 6. 开发任务

- 从 Epic / 用户故事拆到可合并的 PR（习惯：一 PR 一清晰意图）。
- 工程遵守：功能分支 ← 最新 `main`、PowerShell 不用 `&&`、不擅自 bump 版本。
- 质量侧并行：单测、CI、回归清单。

---

## 7. 测试与验收

- **工程验收**：`custom/verification-loop`（编译 + `dotnet test`；未跑不得声称通过）。
- **产品 / 试车验收**：按 PRD 验收标准 + `templates/acceptance-checklist.md`；场边路径由试验同学把关。
- 未达验收：修复或削减范围并更新 PRD「变更记录」，禁止「先合再补文档」作为默认。

---

## 8. 版本发布

严格按根目录 **`VERSIONING.md`**：

- 开发期 `0.x.y`；稳定后用户宣告再 `1.0.0`
- 文档-only / 规范 PR：**不** bump、不打包、不打 tag
- 用户明确要求打包时，按相对上一意图包的变更档位算号（功能档多为下一 `0.x.0`）

发布说明应能追溯到本轮纳入的 PRD / Roadmap 条目（一句话列表即可）。

---

## 状态机（建议）

| 状态 | 含义 |
|------|------|
| 候选 | 仅提议，未评估 |
| 评估中 | 正在回答「做不做」 |
| PRD 起草 | 写文档中 |
| 待审 | 等产品/工程/用户审 |
| 已批准 | 可排期开发 |
| 开发中 | 有分支 / PR |
| 待验收 | 实现完成，等试车/产品过 |
| 已交付 | 已进 `main`（或已打入约定版本包） |
| 搁置 / 拒绝 | 明确不做或以后再说 |

---

## English (short)

Proposal → assess → PRD when major/cross-module → approve → tech design → build → verify (engineering + field) → release per `VERSIONING.md`. Small bugs skip full PRD. Product owns scope; ECC owns implementation quality; neither overrides the other.
