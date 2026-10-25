# CMTS 产品管理规范（AI 辅助）

本目录是 **Chassis Master Test Suite（CMTS）** 的产品管理（Product Management）工作流入口。  
面向：Grok Bot（统筹 / Chief）、群内产品相关 Agent、以及任何会提需求或写 PRD 的维护者。

**第一阶段（本目录建立时）**：只提供规范与模板；**不**改软件功能代码。

## 与 ECC / 工程规范的边界（必读）

| 领域 | 权威位置 | 负责什么 |
|------|----------|----------|
| **产品决策** | 本目录 + `docs/product/` | 要不要做、做到哪、优先级、验收口径、Roadmap / PRD |
| **工程实现与质量（ECC 风格）** | `AGENTS.md`、`.ai-workflows/custom/`、`skills/` 等 | 怎么写代码、怎么测、怎么审、怎么合入 |

二者**互不覆盖**：

- 产品规范**不得**规定实现细节、禁止替代 `verification-loop` / 单测要求。
- 工程规范**不得**在未走产品流程时擅自扩大范围、私自定优先级或跳过重大需求的 PRD。
- 冲突时：范围与「做不做」听产品侧（用户拍板 + 本规范）；「怎么做、是否可合并」听工程侧。

版本号仍只服从仓库根目录 **`VERSIONING.md`**（SemVer `0.x.y` → `1.0.0`）。

## 目录

```
.ai-workflows/product-management/
  README.md                 ← 本索引
  ATTRIBUTION.md            ← 灵感来源声明（无第三方正文拷贝）
  process.md                ← 端到端流程 + 何时必须写 PRD
  roles.md                  ← 角色与职责（含 ECC vs 产品）
  prioritization.md         ← 轻量优先级（MoSCoW + 试车价值）
  templates/
    prd.md
    epic.md
    user-story.md
    roadmap-entry.md
    competitive-brief.md
    acceptance-checklist.md
```

产品愿景 / Roadmap / 已立项 PRD 正文放在 **`docs/product/`**（见该目录 README）。

## Agent 怎么用

1. **新功能或跨模块需求**：先 Read `process.md` 与 `roles.md`，再按模板在 `docs/product/` 落稿（或更新 Roadmap）。
2. **小 Bug / 文案 / 测试补强**：一般**不**强制完整 PRD；见 `process.md`「豁免」。
3. **开发开工前**：确认需求状态为「已批准」；工程侧再走 `AGENTS.md` 开工五步。
4. **发版 / 打包**：只按 `VERSIONING.md`；本目录不定义版本号。

## 什么时候读什么

| 时机 | 先读 |
|------|------|
| 有人提议新功能 | `process.md` → 是否需要 PRD |
| 写 / 审 PRD | `templates/prd.md` + `docs/product/README.md` |
| 排期与砍范围 | `prioritization.md` + `docs/product/ROADMAP.md` |
| 多 Agent 谁拍板 | `roles.md` |
| 验收 / 试车过包 | `templates/acceptance-checklist.md` |
| 竞品对照（可选） | `templates/competitive-brief.md` |
