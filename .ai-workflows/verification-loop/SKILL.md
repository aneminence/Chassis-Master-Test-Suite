# Skill: verification-loop（CMTS）

在声称「完成 / 可推送 / 可合并」前执行。PowerShell：**不要用 `&&`**，分条命令。

## 1. 工作区

```powershell
Set-Location 'D:\CMTS\Chassis Master Test Suite'
git status -sb
git branch --show-current
```

确认：在预期功能分支；无把无关 WIP 混进本次提交。

## 2. 编译

Debug（日常）：

```powershell
dotnet build 'Chassis Master Test Suite.sln' -c Debug
```

交付 / 发版前再：

```powershell
dotnet build 'Chassis Master Test Suite.sln' -c Release
```

失败则停：贴错误，勿继续「假完成」。

## 3. 测试

```powershell
dotnet test ChassisMasterTestSuite.Tests\ChassisMasterTestSuite.Tests.csproj
```

改了局部逻辑时可先过滤：

```powershell
dotnet test ChassisMasterTestSuite.Tests\ChassisMasterTestSuite.Tests.csproj --filter 'FullyQualifiedName~Vbo'
```

（按模块改 `Vbo` / `GSpot` / `Kml` / `TrackProjection` / `Slope` 等。）

## 4. F5 手动清单（按改动勾选）

| 改动类型 | 最少手动确认 |
|----------|----------------|
| VBO / GSpot 解析 | 导入样例 → Dashboard 速度合理 → TrackMap 有轨迹（非「等待 GPS」） |
| KML | 加载 → TrackMap → 会话重启后仍能恢复（若触及 restore） |
| TrackMap / 投影 | 缩放平移、多文件图层、异常点不被 5km 滤光杀光 |
| 曲线 / X 同步 | 多图缩放是否仍对齐 |
| 在线 GSpot / UDP | 连接、断线、通道列表（勿在无凭证时硬编密码） |
| 仅测试 / 文档 / workflows | 可跳过 F5，注明原因 |

## 5. 汇报模板

```
## Verification
- Build: Debug/Release — pass/fail
- Tests: N passed / failures…
- F5: 做过的项 / 跳过原因
- Risks / follow-ups:
```

未跑通编译或测试时，**不要**写「已完成」。
