# Skill: code-review（CMTS）

在**准备提交或声称完成前** Read 本文件，对**本次 diff** 做审查（不要全库漫游）。

## 范围

- 只审：本次改动的 `.cs` / `.xaml` / 测试 / `AGENTS.md` / 工作流文件。
- 跳过：`bin/` `obj/` 生成物、无关大文档、未改动的邻域「顺手优化」。

## 检查清单

### 正确性

- [ ] 空引用 / 未初始化集合；WPF 绑定对象是否在 UI 线程更新。
- [ ] GPS / 速度 / 时间单位：Racelogic arcmin vs GSpot 缩放；东经符号；`IsValidGps` 过滤后是否仍有点。
- [ ] 离线加载路径：`LoadOfflineTrackFile` vs 仅 `LoadVboFile`；会话恢复是否覆盖 KML。
- [ ] `SampleEnricher` / `SlopeEnricher` 顺序是否被破坏。

### 架构边界

- [ ] 测试项目只链接纯逻辑源码；勿把 WPF / ScottPlot 依赖拖进 `ChassisMasterTestSuite.Tests`。
- [ ] `Recorder` 不宜强依赖 UI；跨层引用是否必要。
- [ ] 通道 Id 与 `ChannelIds` / VBO column names 一致（大小写约定）。

### 回归与范围

- [ ] 未改无关功能（主题、默认布局、其他数据源）除非任务要求。
- [ ] 行为变更有测试或明确的手动验收步骤。
- [ ] 公共 API / 文件格式变更有注释或 README/CHANGELOG 一句说明（若用户要求文档）。

### 安全与仓库卫生

- [ ] 无密钥、token、本机绝对路径写入仓库。
- [ ] 无大体积原始 `.vbo`（夹具除外且已 gitignore 例外）。
- [ ] 错误路径有用户可见反馈，而非静默空图。

## 输出格式（给用户 / 父 Agent）

```
## Code review
- Findings: （按严重度：阻断 / 应修 / 建议）
- Residual risks:
- Out of scope (intentionally untouched):
```

有**阻断**项则不要建议合并；先修或请示。
