# CMTS 优先级方法（轻量）

目标：在试车与办公室场景下，快速决定「先做 / 后做 / 不做」，避免用复杂打分假装精确。

默认组合：**MoSCoW** 定档 + **试车价值四问** 微调。需要在多个大项之间排序时，再用简化 RICE 作参考（非强制）。

---

## 1. MoSCoW 定档

| 档 | 含义（CMTS） | 例子 |
|----|--------------|------|
| **Must** | 不做则当前试车主路径不可用或数据不可信 | 解析错误导致距离/速度错；崩溃；丢录 |
| **Should** | 主路径可用但明显痛；本版本强烈希望带上 | 底图失败提示；国内常用图源说明 |
| **Could** | 明显更好，可下一包 | 更多表盘类型；高级 Maths |
| **Won't（本轮）** | 明确不做或永久不做 | 已写进 README「明确不做」的项；未拍板的全量 Android Online |

定档后写入 PRD / Roadmap 条目；**Won't** 必须写原因，避免反复争论。

---

## 2. 试车价值四问（CMTS 风味）

对每个候选问：

1. **现场有多痛？**（停车操作、网差、手套/阳光、平板单手）
2. **数据有多真？**（影响 Pass/Fail、轨迹、通道单位则加权）
3. **与 Win 主路径关系？**（补强现有 Studio 优先于开新平台，除非用户拍板）
4. **回归面多大？**（协议/格式/默认主题 → 成本高，需更高收益才进 Must/Should）

经验规则：

- 场边 Offline 看结果 > 办公室锦上添花，除非用户另有指示。
- **跨硬件/协议** 默认成本高：未评估前不要进 Must。
- 「有人觉得酷」≠ Should。

---

## 3. 简化 RICE（可选）

当 Must/Should 内仍排不过来时：

| 因子 | CMTS 解读 | 粗分 |
|------|-----------|------|
| Reach | 多少试车班次/用户会碰到 | 1–3 |
| Impact | 对效率或数据可信度 | 1–3 |
| Confidence | 需求与方案把握 | 0.5–1.0 |
| Effort | 人周粗估（功能+质量+可能美化） | 人周，≥0.5 |

`Score ≈ Reach × Impact × Confidence / Effort`  
**仅作排序参考**；用户一票可推翻。不要求每个小 Bug 都算 RICE。

---

## 4. 与四条产品线

排期时标注产品线，避免 Computing 塞满而忽略 Modules/Transducers 接口债：

| 线 | 代号 | 排期注意 |
|----|------|----------|
| Computing | C | Studio（Win）与 Field（Android 等）分开评估 |
| Modules | M | Logger/DAQ 协议与软件默认行为绑定，跨线=强制 PRD |
| Transducers | T | 传感器语义/标定影响通道，勿只当 UI 需求 |
| Solutions | S | Dynamics/Safety/Energy 等解决方案包，常依赖 C+T |

---

## 5. 输出格式（建议）

Roadmap 或 PRD 头部：

```text
优先级：Should
产品线：C（Computing / Studio）
试车价值：场边看轨迹；网差可关底图
Effort 粗估：S / M / L
本轮 Won't：...
```

---

## English (short)

Use MoSCoW first, tune with field-pain / data-trust / Win-path / regression-cost. Optional light RICE for ties. Tag C/M/T/S product lines. User override wins.
