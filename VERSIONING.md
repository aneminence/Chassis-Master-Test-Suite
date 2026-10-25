# CMTS 版本规范（VERSIONING）

本文件是 **Chassis Master Test Suite（CMTS）** 的唯一版本策略说明。  
**所有人类维护者与 AI Agent** 在改版本号、打 tag、发 GitHub Release、或改 `.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion` 之前，**必须先 Read 本文件并遵守**。禁止凭感觉编造版本号。

规范基础：[Semantic Versioning 2.0.0](https://semver.org/)（SemVer 2.0.0）。

---

## 一句话

- **开发期**：`0.x.y`（尚未宣告产品稳定）
- **首次正式稳定发布**：`1.0.0`
- **1.0.0 之后**：严格按 SemVer — `MAJOR.MINOR.PATCH`

---

## 当前基线（写入本规范时）

| 项 | 状态 |
|----|------|
| `main` 尖端 | 持续前进；功能已明显超过上次打包 |
| 最近一次**已打包 / 用户认定的预览版** | **`0.2.1`**（约 **2026-10-12** 打包） |
| GitHub Releases / `v*` tag（公开页） | 可能尚未与上述基线对齐或为空；**不以「页面上没有 Release」为由发明新号** |
| 主 `.csproj` `<Version>` | **尚未**显式设置；SDK 默认会表现为 `1.0.0`，**不得**把该默认值当成正式 `1.0.0` |

**含义**：下次从 `main` 切预览 Release 时，版本账本从上表的 **`0.2.1`** 起算（见下文「从 main 发预览 Release」），而不是从 `1.0.0` 或随意跳号。  
将 SSOT 写进 `.csproj`、补齐 `v0.2.1` tag/Release 历史等对齐动作，**须用户确认后另做**；本文件只定规则，不自动改版本、不自动发版。

---

## 版本号放在哪里（单一事实来源）

| 位置 | 角色 |
|------|------|
| 主项目 `Chassis Master Test Suite.csproj` 的 `<Version>` | **产品版本的单一事实来源（SSOT）**（应对齐到当前发布线，如 `0.2.1`） |
| 同文件可选 `<AssemblyVersion>` / `<FileVersion>` | 须与发布意图一致；未单独说明时跟随 `<Version>` |
| Git tag | `vX.Y.Z`（前缀小写 `v`，与 `<Version>` 的 `X.Y.Z` 对应） |
| GitHub Release | 基于上述 tag；Release 标题/名称使用同一 `vX.Y.Z`；可附带安装包/zip |
| `CHANGELOG-quality.md` 等笔记 | 可记录变更，**不**单独定义产品版本号 |

**对齐方式**：在用户明确确认后，把 `<Version>`（及需要时的 Assembly/FileVersion）设为约定的 `0.x.y`。  
**未获用户确认前，Agent 不得擅自写入或抬升版本号，也不得自行创建 GitHub Release。**

---

## 开发期：`0.x.y`

在首次发布 `1.0.0` 之前，版本形如 **`0.x.y`**：

| 分量 | 含义（CMTS 约定） |
|------|-------------------|
| `0` | 固定；表示 **尚未** 进入正式稳定产品线 |
| `x` | **功能 / 兼容性** 档：有用户可见功能、协议或数据格式等可能破坏既有用法的改动时递增；`x` 增加时 **`y` 归零** |
| `y` | **补丁**：缺陷修复、文档、测试、打包刷新、小改进且不改变对外约定行为；仅递增 `y` |

说明（与 SemVer 对 `0.y.z` 的精神一致，并写死本仓库规则）：

1. **`0.x` 允许破坏性变更**，不必为此跳到 `1.0.0`。
2. **进入 `1.0.0` 只能由用户（产品负责人）明确宣告「正式稳定发布」**，Agent 不得自行决定「已经够稳了所以升 1.0.0」。
3. 日常合入 `main` 的功能 PR **默认不改** `<Version>`；只在 **发版 / 打 tag / 用户点名 bump** 时改。
4. 预发布后缀（若需要）使用 SemVer 形式，例如 `0.3.0-alpha.1`，tag 为 `v0.3.0-alpha.1`。无用户要求时不要加预发布后缀；**普通预览 zip/Release 直接用 `0.x.y` 即可**（当前阶段默认如此）。

### 从 `main` 发预览 Release（开发期常用）

适用：用户要求「把当前 `main` 打成一个可分发预览包 / GitHub Release」，产品仍处于 `0.x.y`，**不是** `1.0.0`。

1. **确认基线**：上一发布版本 = 用户认定的最近预览号（写入本规范时为 **`0.2.1`**）。若之后已发过更新的 `0.x.y`，以**最近一次已发布**的为准。
2. **看相对基线，`main` 上多了什么**（只根据已合入变更分类，禁止拍脑袋）：
   - 相对 `0.2.1` **只有**修复 / 文档 / 测试 / 构建包装 → 升 **`y`**：`0.2.1` → **`0.2.2`**
   - 含用户可见新功能，或可能影响既有用法 / 文件格式 / 协议 → 升 **`x` 且 `y=0`**：`0.2.1` → **`0.3.0`**
   - 多种变更同时存在 → 取较高档（有功能/破坏性则走 `0.(x+1).0`，不要只加 `y`）
3. **禁止**：跳号（如 `0.2.1` → `0.5.0`）除非用户写明确切目标版本；把 SDK 默认 `1.0.0` 或「main 已经超前很多」当成可以直接发 `1.0.0`。
4. **发版提交（仅在用户明确要求发版时）**：
   - 更新 `.csproj` `<Version>`（及需要时的 Assembly/FileVersion）到新的 `0.x.y`
   - 打 tag **`v0.x.y`** 在包含该版本号的提交上（通常基于当时的 `main`）
   - 创建 GitHub Release（可附打包产物）；Release 名称/tag 与 `<Version>` 一致
5. **本任务类文档 PR**：只改规范、**不** bump、**不**打 tag、**不**建 Release。发版由用户拍板后另开任务。

示例（基线 `0.2.1`）：

| `main` 相对基线 | 下一预览版 |
|-----------------|------------|
| 仅修崩溃 / 文案 / CI | `0.2.2` |
| 合入地图 i18n 主题、TrackMap 3D、GSpot VBO 映射等功能 | `0.3.0` |
| 用户口头指定「就发 0.4.0」 | `0.4.0`（以用户指定为准，并在汇报中写明） |

---

## 稳定后：`1.0.0` 与之后

当用户明确要求发布 **1.0.0** 后：

| 分量 | 何时递增 |
|------|----------|
| **MAJOR** | 不兼容的 API / 文件格式 / 协议 / 行为变更（破坏性） |
| **MINOR** | 向后兼容的功能新增 |
| **PATCH** | 向后兼容的问题修复 |

递增 MAJOR 时 MINOR、PATCH 归零；递增 MINOR 时 PATCH 归零。

---

## 谁可以改版本号

| 角色 | 权限 |
|------|------|
| 用户（维护者） | 可要求任意合法 SemVer bump，或指定确切版本 |
| AI Agent | **可以**在用户明确要求发版/bump/打 tag 时，按本文件计算并提交版本改动；**不可以**在普通功能/文档/测试 PR 里顺手改版本；**不可以**未经要求创建 Release |
| 其他人 / 其他机器人 | 同等约束：先读本文件，禁止 ad hoc 版本号 |

**强制流程（Agent）**：

1. Read 本文件（`VERSIONING.md`）。
2. 确认用户是否要求 bump / preview Release / 正式发版；若无 → **不要改** `<Version>`，也不要建 tag/Release。
3. 若有 → 以「上一已发布 `0.x.y`」（当前账本起点 **`0.2.1`**）为基线，按上一节规则选出下一版本，并在 PR/汇报中说明理由。
4. 同步：`.csproj` `<Version>` →（需要时）Assembly/FileVersion → git tag `vX.Y.Z` →（用户要求时）GitHub Release + 产物。
5. Tag 必须打在包含该版本号提交的提交上；不要给错误的 commit 贴版本。

---

## Tag 与 Release 格式

- Tag：**`vX.Y.Z`**（例：`v0.2.1`、`v0.3.0`、`v1.0.0`）
- 禁止：无 `v` 前缀、日期版（`2026.10.25`）、口头号（`vNext`）、与 `<Version>` 不一致的 tag
- GitHub Release：对应已推送的 tag；说明相对上一预览版的变更即可，不另造版本号
- 预览包与正式包共用同一套 `vX.Y.Z` 编号；是否在 Release 正文标明「预览 / Preview」由用户决定，**不**因此改用另一套号段

---

## 与日常开发的关系

- 功能分支、文档、CI、重构：**不**自动 bump。
- `CHANGELOG-quality.md`：质量迭代笔记，**不是** SemVer 账本。
- README「最近更新」：功能说明，**不**定义版本号。
- 依赖包版本（如 `ScottPlot.WPF` NuGet）与 **CMTS 产品版本** 无关，勿混用。
- `main` 超前于上次打包（例如仍停在 `0.2.1` 包）是常态；**超前本身不是 bump 理由**，只有用户要求发预览/发版时才按规则升 `0.x.y`。

---

## Agent 速查（违者视为流程错误）

```text
[ ] 改 Version / 打 tag / 发 Release 前已 Read VERSIONING.md
[ ] 用户明确要求发版或 bump（或指定了确切版本）
[ ] 开发期保持 0.x.y；预览 Release 也走 0.x.y，基线从上一发布号起算（现为 0.2.1）
[ ] 未经用户宣告稳定不得升 1.0.0
[ ] 1.0.0+ 按 MAJOR / MINOR / PATCH
[ ] SSOT = 主 csproj 的 <Version>；tag = vX.Y.Z
[ ] 未获确认不把 SDK 默认 1.0.0「就地认成」正式 1.0.0
[ ] 文档-only 任务不顺手发 Release
```

---

## English (short)

CMTS follows **SemVer 2.0.0**. Pre-stable line is **`0.x.y`** (`x` = feature / possibly-breaking, `y` = fixes/docs/repack). Latest known packaged preview baseline when this file was written: **`0.2.1`** (~2026-10-12); `main` may be far ahead — that alone does not bump the version. Shipping a **preview Release from `main`**: bump from the last released `0.x.y` (`y+1` for fixes-only, `x+1.0` if features/breaking landed), set `<Version>`, tag `vX.Y.Z`, then GitHub Release — **only when the human asks**. **`1.0.0`** only when the maintainer declares the first stable release. SSOT: `<Version>` in `Chassis Master Test Suite.csproj`. Do not invent versions on ordinary PRs; docs-only tasks must not cut Releases.