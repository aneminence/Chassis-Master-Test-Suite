# CMTS 版本规范（VERSIONING）

本文件是 **Chassis Master Test Suite（CMTS）** 的唯一版本策略说明。  
**所有人类维护者与 AI Agent** 在改版本号、打 tag、发 GitHub Release、本地打包，或改 `.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion` 之前，**必须先 Read 本文件并遵守**。禁止凭感觉编造版本号。

规范基础：[Semantic Versioning 2.0.0](https://semver.org/)（SemVer 2.0.0）。

---

## 一句话

- **开发期**：`0.x.y`（尚未宣告产品稳定）
- **首次正式稳定发布**：`1.0.0`
- **1.0.0 之后**：严格按 SemVer — `MAJOR.MINOR.PATCH`

---

## 当前基线与本地打包史（写入本规范时）

**尚未**在 GitHub 上创建 Releases，也 **没有** `v*` tag。迄今版本只存在于本机打包目录：

`D:\CMTS\release\`

| 本地文件夹 | `VERSION.txt` 所写 | Git | 日期（Asia/Shanghai） | 规范认定 |
|------------|-------------------|-----|----------------------|----------|
| `Chassis-Master-Test-Suite-0.1-preview-win-x64` | （无 / 预览） | — | — | 历史：`0.1-preview` |
| `Chassis-Master-Test-Suite-0.2-win-x64` | `0.2.0` | `120b56f` | 2026-10-10 | **`0.2.0`** |
| `Chassis-Master-Test-Suite-0.2.1-win-x64` | **`0.21.0`（误写）** | `0ab045d` | 2026-10-12 | 文件夹意图为 **`0.2.1`**；**禁止**再延续 `0.21.x` 这种写法 |

**账本起点（下一包之前）**：上一**意图**预览号为 **`0.2.1`**（以文件夹名为准；`VERSION.txt` 的 `0.21.0` 视为笔误，不纳入 SemVer 序列）。

| 项 | 状态 |
|----|------|
| `main` | 已明显超前于 `0ab045d` / `0.2.1` 包 |
| 主 `.csproj` `<Version>` | **尚未**显式设置；SDK 默认会表现为 `1.0.0`，**不得**当成正式 `1.0.0` |
| GitHub Releases / tags | 无 |
| 是否现在打 `0.3.0` 包 | **否**（维护者已明确：本轮只定规范与文档 PR，不打包） |

下一功能向预览包（当用户将来要求时）应为 **`0.3.0`**（升 `x`），而不是 `0.21.1` / `0.22.0` / `0.2.2`（若相对 `0.2.1` 已含大量功能，按规则走 `0.3.0`）。

---

## 版本号放在哪里（单一事实来源）

版本只应有 **一处权威来源**，发版时再派生 tag / 本地 `VERSION.txt` /（将来）GitHub Release。

| 位置 | 角色 |
|------|------|
| 主项目 `Chassis Master Test Suite.csproj` 的 **`<Version>`** | **推荐 SSOT**：产品版本写在这里 |
| 可选 `<AssemblyVersion>` / `<FileVersion>` | 未单独说明时跟随 `<Version>` |
| Git tag（发版时） | **`vX.Y.Z`**，与 `<Version>` 的 `X.Y.Z` 一致 |
| 本地包内 `VERSION.txt` | 打包脚本从 SSOT **生成**，禁止手写另一套号 |
| GitHub Release（将来若启用） | 基于已推送的 `vX.Y.Z` tag |
| `D:\CMTS\release\` 文件夹名 | 应含同一 `X.Y.Z`（如 `...-0.3.0-win-x64`） |
| `CHANGELOG-quality.md` / README | 可记变更，**不**定义产品版本 |

**对齐**：用户确认后，再把 `<Version>` 设为当前账本号（例如先对齐 `0.2.1`，或发下一包时直接写成新号）。  
**未获用户确认前**：Agent 不得擅自改 `<Version>`、打 tag、写 GitHub Release，或在 `D:\CMTS\release` 打新包。

---

## 开发期：`0.x.y`

在首次发布 `1.0.0` 之前，版本形如 **`0.x.y`**（三个十进制数字，点分）：

| 分量 | 含义（CMTS 约定） |
|------|-------------------|
| `0` | 固定；尚未进入正式稳定产品线 |
| `x` | **功能 / 兼容性** 档：用户可见功能，或可能破坏既有用法 / 格式 / 协议时 +1；此时 **`y` 归零** |
| `y` | **补丁**：缺陷修复、文档、测试、同功能线重新打包；只 +1 `y` |

硬性禁止：

- **`0.21.0` 这类把「2.1」挤进中间段的写法**（已在 `0.2.1` 包的 `VERSION.txt` 出现过一次，**到此为止**）
- 日期版、`vNext`、与文件夹名不一致的手写号
- 未经用户宣告稳定就升 **`1.0.0`**

其他：

1. **`0.x` 允许破坏性变更**，不必为此跳到 `1.0.0`。
2. 日常合入 `main` 的功能 PR **默认不改** `<Version>`；只在用户要求 **发版 / 本地打包 / bump** 时改。
3. 预发布后缀可选（如 `0.3.0-alpha.1`）；无用户要求时，预览包直接用 `0.x.y`。

### 从 `main` 打预览包 /（将来）Release

适用：用户明确要求「把当前 `main` 打成预览包」（仍为 `0.x.y`，不是 `1.0.0`）。

1. **基线** = 上一意图发布号（写入时为 **`0.2.1`**；之后以最近一次按本规范发布的为准）。
2. **分类 `main` 相对基线的变更**（禁止拍脑袋）：
   - 仅修复 / 文档 / 测试 / 同内容重打 → **`y+1`**（例：`0.2.1` → `0.2.2`）
   - 含新功能或可能破坏既有用法 → **`x+1`，`y=0`**（例：`0.2.1` → **`0.3.0`**）
   - 两者都有 → 走较高档（功能档）
3. **禁止跳号**，除非用户写明目标版本。
4. **发版步骤（仅用户要求时）**：
   - 更新 `.csproj` `<Version>` 到新的 `0.x.y`
   - 打 tag **`v0.x.y`**（打在含该版本号的提交上；通常基于当时 `main`）
   - 本地产出到 `D:\CMTS\release\...-0.x.y-win-x64\`，`VERSION.txt` 与 SSOT 一致
   - 若用户还要求：再发 GitHub Release（当前默认未启用）
5. **文档-only / 本规范 PR**：**不** bump、**不**打包、**不**打 tag。

示例（基线 `0.2.1`，且 `main` 已合入大量功能）：

| 场景 | 下一版本 |
|------|----------|
| 用户要求打功能预览包 | **`0.3.0`** |
| 仅热修上一包、无新功能 | `0.2.2` |
| 用户指定确切号 | 以指定为准（须仍是合法 `0.x.y`） |
| 用户说「现在不打包」 | **不改版本、不产出** |

---

## 稳定后：`1.0.0` 与之后

当用户明确要求发布 **1.0.0** 后：

| 分量 | 何时递增 |
|------|----------|
| **MAJOR** | 不兼容的 API / 文件格式 / 协议 / 行为变更 |
| **MINOR** | 向后兼容的功能新增 |
| **PATCH** | 向后兼容的问题修复 |

递增 MAJOR 时 MINOR、PATCH 归零；递增 MINOR 时 PATCH 归零。

---

## 谁可以改版本号

| 角色 | 权限 |
|------|------|
| 用户（维护者） | 可要求合法 SemVer bump，或指定确切 `0.x.y` / `1.x.y` |
| AI Agent | 仅在用户明确要求发版/打包/bump/打 tag 时按本文件执行；普通功能/文档/测试 PR **不得**顺手改版本；**不得**擅自打包到 `D:\CMTS\release` |
| 其他 | 同等约束 |

**强制流程（Agent）**：

1. Read `VERSIONING.md`。
2. 无用户发版/打包/bump 指示 → **停下**，不改 `<Version>`，不打 tag，不写 Release，不打本地包。
3. 有指示 → 从上一规范版本起算，说明拟用 `0.x.y` 及理由，再改 SSOT → tag → 包内 `VERSION.txt`。
4. Tag / 文件夹名 / `VERSION.txt` / `<Version>` **必须一致**。

---

## Tag 与（将来）GitHub Release 格式

- Tag：**`vX.Y.Z`**（例：`v0.2.1`、`v0.3.0`）
- 禁止：无 `v` 前缀、与 `<Version>` 不一致、`0.21.0` 式中间段
- GitHub Release：可选；启用时必须挂在已有 `vX.Y.Z` tag 上，不另造号段
- 预览与正式共用 `vX.Y.Z`；是否在说明里写「预览」由用户定

---

## 与日常开发的关系

- 功能分支、文档、CI、重构：**不**自动 bump。
- `main` 超前于上次本地包是常态；**超前本身不是 bump 理由**。
- `CHANGELOG-quality.md`、README「最近更新」**不是** SemVer 账本。
- NuGet 依赖版本（如 ScottPlot）与 **CMTS 产品版本** 无关。

---

## Agent 速查（违者视为流程错误）

```text
[ ] 改 Version / 打包 / 打 tag / Release 前已 Read VERSIONING.md
[ ] 用户明确要求发版、打包或 bump
[ ] 使用 SemVer 0.x.y（三数字）；禁止 0.21.0 式写法
[ ] 账本基线：上一意图包 0.2.1；下一功能包多为 0.3.0（用户要求时）
[ ] 未经用户宣告稳定不得升 1.0.0
[ ] SSOT = 主 csproj <Version>；发版 tag = vX.Y.Z；VERSION.txt 从 SSOT 生成
[ ] 不把 SDK 默认 1.0.0 认成正式 1.0.0
[ ] 文档-only 任务：不打包、不 bump、不建 Release
```

---

## English (short)

CMTS follows **SemVer 2.0.0** on **`0.x.y`** until the maintainer declares **`1.0.0`**. There are **no GitHub Releases/tags yet**; versions so far are **local packs** under `D:\CMTS\release` (`0.1-preview` → `0.2.0` → folder `0.2.1` whose `VERSION.txt` wrongly said `0.21.0`). **Do not continue `0.21`-style numbers.** Next feature preview, when asked, should be **`0.3.0`**. SSOT: `<Version>` in `Chassis Master Test Suite.csproj`; shipping adds tag **`vX.Y.Z`** and generated `VERSION.txt`. Docs-only work must not pack or bump. Humans and AI agents must read this file before any version change.