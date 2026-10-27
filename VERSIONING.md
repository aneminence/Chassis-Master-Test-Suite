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

## 当前基线与发布史

### 现行账本（2026-10-27 起）

| 项 | 状态 |
|----|------|
| 最新已发布号 | **`0.3.0`**（GitHub **prerelease**） |
| Tag | **`v0.3.0`**（挂在发版时的 `main`） |
| 主 `.csproj` `<Version>` | **`0.3.0`**（与 tag / Release 一致） |
| GitHub Release | [v0.3.0](https://github.com/aneminence/Chassis-Master-Test-Suite/releases/tag/v0.3.0)（2026-10-27 Asia/Shanghai；Windows x64 portable zip + sha256） |
| 发版方式 | 推送匹配 `v*` 的 tag 后，`.github/workflows/release.yml` 自动构建并创建 **prerelease** |
| 下一补丁预览（仅修复/文档/测试/同内容重打） | **`0.3.1`** |
| 下一功能预览（含新功能或可能破坏既有用法） | **`0.4.0`** |

**禁止**再延续 `0.21.x` 写法。未经用户宣告稳定，不得升 **`1.0.0`**。

### 本地打包史（规范写入前；仍作对照）

早期版本只存在于本机 `D:\CMTS\release\`：

| 本地文件夹 | `VERSION.txt` 所写 | Git | 日期（Asia/Shanghai） | 规范认定 |
|------------|-------------------|-----|----------------------|----------|
| `Chassis-Master-Test-Suite-0.1-preview-win-x64` | （无 / 预览） | — | — | 历史：`0.1-preview` |
| `Chassis-Master-Test-Suite-0.2-win-x64` | `0.2.0` | `120b56f` | 2026-10-10 | **`0.2.0`** |
| `Chassis-Master-Test-Suite-0.2.1-win-x64` | **`0.21.0`（误写）** | `0ab045d` | 2026-10-12 | 文件夹意图为 **`0.2.1`**；**禁止**再延续 `0.21.x` |

---

## 版本号放在哪里（单一事实来源）

版本只应有 **一处权威来源**，发版时再派生 tag / 本地 `VERSION.txt` / GitHub Release。

| 位置 | 角色 |
|------|------|
| 主项目 `Chassis Master Test Suite.csproj` 的 **`<Version>`** | **推荐 SSOT**：产品版本写在这里 |
| 可选 `<AssemblyVersion>` / `<FileVersion>` | 未单独说明时跟随 `<Version>` |
| Git tag（发版时） | **`vX.Y.Z`**，与 `<Version>` 的 `X.Y.Z` 一致 |
| 本地包内 `VERSION.txt` | 打包脚本从 SSOT **生成**，禁止手写另一套号 |
| GitHub Release（prerelease，由 `release.yml` 创建） | 基于已推送的 `vX.Y.Z` tag |
| `D:\CMTS\release\` 文件夹名 | 应含同一 `X.Y.Z`（如 `...-0.3.0-win-x64`） |
| `CHANGELOG-quality.md` / README | 可记变更，**不**定义产品版本 |

**对齐**：发版前把 `<Version>` 写成目标 `0.x.y`（现行账本为 **`0.3.0`**）。  
**未获用户发版/bump 指示前**：Agent 不得擅自改 `<Version>`、打 tag、触发 Release，或在 `D:\CMTS\release` 打新包。

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

### 从 `main` 打预览包 / GitHub Release

适用：用户明确要求「把当前 `main` 打成预览包 / 发版」（仍为 `0.x.y`，不是 `1.0.0`）。

1. **基线** = 最近一次按本规范发布的号（现行：**`0.3.0`**）。
2. **分类 `main` 相对基线的变更**（禁止拍脑袋）：
   - 仅修复 / 文档 / 测试 / 同内容重打 → **`y+1`**（例：`0.3.0` → `0.3.1`）
   - 含新功能或可能破坏既有用法 → **`x+1`，`y=0`**（例：`0.3.0` → **`0.4.0`**）
   - 两者都有 → 走较高档（功能档）
3. **禁止跳号**，除非用户写明目标版本。
4. **发版步骤（仅用户要求时）**：
   - 更新 `.csproj` `<Version>` 到新的 `0.x.y`
   - 打并推送 tag **`v0.x.y`**（打在含该版本号的提交上；通常基于当时 `main`）
   - GitHub Actions `release.yml` 会构建 Windows x64 portable 包，并以 **prerelease** 挂到该 tag（附 zip + sha256）
   - 若仍需本机包：产出到 `D:\CMTS\release\...-0.x.y-win-x64\`，`VERSION.txt` 与 SSOT 一致
5. **文档-only / 本规范 PR**：**不** bump、**不**打包、**不**打 tag。

示例（基线 `0.3.0`）：

| 场景 | 下一版本 |
|------|----------|
| 用户要求打功能预览包 | **`0.4.0`** |
| 仅热修上一包、无新功能 | `0.3.1` |
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

## Tag 与 GitHub Release 格式

- Tag：**`vX.Y.Z`**（例：`v0.3.0`、`v0.3.1`）
- 禁止：无 `v` 前缀、与 `<Version>` 不一致、`0.21.0` 式中间段
- GitHub Release：由 `release.yml` 在推送 `v*` tag 后创建 **prerelease**，必须挂在已有 `vX.Y.Z` tag 上，不另造号段
- 预览与正式共用 `vX.Y.Z`；当前流水线默认 `--prerelease`；是否改为正式 Release 由用户定

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
[ ] 账本基线：已发布 0.3.0（prerelease）；下一补丁多为 0.3.1，下一功能多为 0.4.0（用户要求时）
[ ] 未经用户宣告稳定不得升 1.0.0
[ ] SSOT = 主 csproj <Version>；发版 tag = vX.Y.Z；VERSION.txt 从 SSOT 生成
[ ] 不把 SDK 默认 1.0.0 认成正式 1.0.0
[ ] 文档-only 任务：不打包、不 bump、不建 Release
```

---

## English (short)

CMTS follows **SemVer 2.0.0** on **`0.x.y`** until the maintainer declares **`1.0.0`**. Current shipped baseline is **`0.3.0`** (GitHub **prerelease** tag `v0.3.0`; `<Version>` matches). Older local-only packs under `D:\CMTS\release` (`0.1-preview` → `0.2.0` → folder `0.2.1` whose `VERSION.txt` wrongly said `0.21.0`) are historical. **Do not continue `0.21`-style numbers.** Next patch preview, when asked, is usually **`0.3.1`**; next feature preview is usually **`0.4.0`**. SSOT: `<Version>` in `Chassis Master Test Suite.csproj`; shipping pushes tag **`vX.Y.Z`** so `release.yml` builds the Windows portable zip as a prerelease. Docs-only work must not pack or bump. Humans and AI agents must read this file before any version change.