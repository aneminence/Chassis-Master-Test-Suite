# CMTS 产品路线图（草案）

| 字段 | 内容 |
|------|------|
| 状态 | 草案；**非**承诺交付日期 |
| 更新日期 | 2026-10-28（Asia/Shanghai） |
| 代码基线 | `main` @ 建立本文件时的 tip（以合入后仓库为准） |
| 版本策略 | `VERSIONING.md`：开发期 `0.x.y`；已发布 GitHub prerelease **`0.3.0`**（`v0.3.0`）；下一补丁多为 **`0.3.1`**，下一功能多为 **`0.4.0`**（**仅当用户要求发版时**） |

阶段用语：**Now** = 已在 `main` 或刚落地；**Next** = 已对齐方向、待排期/PRD；**Later** = 有价值但未立项；**候选** = 讨论中未拍板。

---

## Now（已具备的能力切片）

| 主题 | 产品线 | 说明 |
|------|--------|------|
| Win Studio 外壳 | C | Layout / Load / Online·Offline / 录制 |
| 曲线与多文件对比 | C | ScottPlot、光标联动、Measure |
| Test Results | C/S | Accel/Decel/Custom/Gate、CSV、Pass 高亮 |
| Track Map + 底图 | C | 门点、`.vbts` 门导入、在线瓦片（国内可达性仍有使用端风险） |
| 主题 / 中英 | C | Appearance 持久化 |
| KML/KMZ Phase 1 | C | Offline 叠画 |
| 数据源 | C/M 边界 | UDP、GSpot、Simulator；IDataSource |
| 工程与版本文档 | — | `AGENTS.md`、`.ai-workflows/`、`VERSIONING.md`、本产品管理目录 |
| 发版与包 | — | GitHub prerelease **`v0.3.0`**（2026-10-27；portable zip + sha256）；`release.yml` 已启用；历史本地包曾有 `0.2.1` |

> 具体提交以 git 历史为准；本表只服务产品沟通。

---

## Next（方向已齐、待 PRD 或实现收口）

| 主题 | 优先级 | 产品线 | 备注 |
|------|--------|--------|------|
| 底图加载/失败轻提示 + 图源名称可读 | Should | C | 试验提出；质量进冒烟；美化样式 |
| 国内常用图源实机确认 | Should | C | 自动化难覆盖 |
| CI：`dotnet test` 门禁 | Should | — | 质量侧 |
| GSpot VBO / 布局等（若仍有未合分支） | 视用户 | C | **须用户明确合入**；不默认 |

---

## Later

| 主题 | 产品线 | 备注 |
|------|--------|------|
| Settings 正式页 | C | README 延后 |
| `.vbts` 整包导入 | C | 条件/Pass/Maths/Dashboard |
| Maths 高级（累计/积分） | C | |
| 更多 Dashboard 表盘类型 | C | |
| 法规/场景试验插件包 | S | |
| VI skins tokens 深度对齐 | C | 美化；资产可在 `D:\CMTS\VI` |

---

## 候选（未拍板）

| 主题 | 状态 | 说明 |
|------|------|------|
| **Android Field MVP** | 讨论中，**用户未拍板** | 建议范围：平板优先、Offline：开文件→轨迹/曲线→Accel·Gate→导出；Online/标定留 Win。功能/质量/美化/试验曾表示同意该 MVP 边界，**仍等用户确认后再拆排期** |

---

## Won't（现行）

以 README「明确不做」与群口径为准，例如：Gate `.spl` 主路径、同步视频主线、正式报告套件作为当前必须项、试验侧黄金 VBO/Pass 草案等。变更须用户声明并改本表。

---

## 维护方式

1. 大项进入 Next 前：评估 → 必要时 PRD（`process.md`）。  
2. 状态变更改表，不删历史行（可移到「已交付」附录）。  
3. 发版时用一句话列出本版本对应条目（仍服从 `VERSIONING.md`）。
