# `docs/product/` — CMTS 产品文档存放处

本目录存放**产品决策产物**（愿景、路线图、PRD），与 `.ai-workflows/product-management/` 中的**规范与模板**分离。

| 路径 | 用途 |
|------|------|
| `VISION.md` | 产品愿景与定位（短，可演进） |
| `ROADMAP.md` | 分阶段路线图与候选 |
| `prds/` | 已立项 / 历史 PRD 正文 |
| `README.md` | 本说明 |

工作流与模板：`.ai-workflows/product-management/`  
版本号：仓库根目录 `VERSIONING.md`  
工程规矩：`AGENTS.md`

## PRD 命名

```text
docs/product/prds/YYYYMMDD-short-english-or-pinyin-slug.md
```

示例：`20261025-basemap-load-feedback.md`

文件头状态字段须与 `process.md` 状态机一致。

## 状态与合入

- 规范/文档变更走独立 PR；**不**与业务功能改动混提（除非用户要求）。  
- 第一阶段产品管理体系本身：只增文档，不改应用功能。  
- 合并 `main` 须用户明确要求。
