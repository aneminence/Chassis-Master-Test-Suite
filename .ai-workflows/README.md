# CMTS `.ai-workflows` — AI 工作流资料库

本目录只放在 **CMTS 仓库里**，给 Grok Bot / 其他 Agent **按需阅读**。  
**没有**安装到任何 Agent 的全局配置，也 **没有** ECC 插件或自动 Hook。

用大白话说：这里是「说明书书架」。Agent 改代码前/后应打开对应说明书，而不是整库抄一遍。

## 两类资料（不要混）

| 文件夹 | 是什么 | 谁写的 |
|--------|--------|--------|
| `custom/` | 专为 CMTS（WPF、VBO、TrackMap、本机 F5）写的短清单 | 本仓库 |
| `product-management/` | 产品需求流程、优先级、PRD 模板（与工程 QC **互不覆盖**） | 本仓库原创 |
| `skills/` `agents/` `rules/` | 从开源项目 [ECC](https://github.com/affaan-m/ECC) **挑选**拷贝的原文 | ECC（MIT） |

出处与许可证见 `SOURCE.md`、`LICENSE`。

同名主题（如 verification-loop、csharp-testing）可能 **两边都有**：

- 做 **CMTS 日常编译 / 测试 / F5**：优先 `custom/`
- 需要更通用的 TDD / 安全审查写法：再 Read `skills/` 里对应官方稿

## 目录树

```
.ai-workflows/
  README.md                 ← 本索引
  SOURCE.md                 ← ECC 来源 commit / MIT
  LICENSE                   ← ECC MIT 许可证副本
  custom/                   ← CMTS 自制工程清单
    code-review/SKILL.md
    verification-loop/SKILL.md
    csharp-testing/SKILL.md
    cmts-gnss-imu.md        ← GNSS/IMU 占位，暂无编造内容
  product-management/       ← 产品管理（原创规范+模板；见该目录 README）
  skills/                   ← 精选官方技能
    security-review/
    tdd-workflow/
    verification-loop/
    csharp-testing/
    coding-standards/
    change-impact/
  agents/                   ← 精选官方角色说明
    code-reviewer.md
    architect.md
    csharp-reviewer.md
  rules/                    ← 精选规则（common + csharp）
    common/
    csharp/
```

## 什么时候读什么

| 时机 | 先读 |
|------|------|
| 开工前（理解仓库规矩） | 仓库根目录 `AGENTS.md` |
| 新功能 / 需求 / PRD / 优先级 | `product-management/README.md` → `process.md`；正文在 `docs/product/` |
| 写/改单元测试 | `custom/csharp-testing/SKILL.md`（需要通用 C# 测试建议时再读 `skills/csharp-testing/`） |
| 改完准备提交 | `custom/code-review/SKILL.md`；可选 `agents/code-reviewer.md` / `skills/security-review/` |
| 声称完成前 | `custom/verification-loop/SKILL.md`（必跑编译+测试；**未跑的测试不得声称通过**） |
| 大改架构 | `agents/architect.md`；影响面可用 `skills/change-impact/` |
| 先测后写习惯 | `skills/tdd-workflow/`（与 CMTS custom 验证清单互补） |

## 不要做的事

- 不要把整个 ECC 仓库拷进 CMTS
- 不要改 Agent 全局配置来「安装 ECC」
- 不要在未确认时改无关业务代码
- 不要把本目录的扩展与 `MainWindow` / 解析器等业务改动混在同一提交（除非用户要求）
