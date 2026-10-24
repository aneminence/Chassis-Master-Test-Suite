# Skill: csharp-testing（CMTS）

写或改 `ChassisMasterTestSuite.Tests` 时 Read 本文件。

## 项目形态

- 框架：**xUnit**；TFM：`net10.0`（非 `-windows`）。
- 生产逻辑通过 **Compile Link** 引入（见 `.csproj`），**不要** ProjectReference 主 WPF 工程。
- 新增纯逻辑 `.cs` 若需单测：同步加到测试 csproj 的 `<Compile Include="..\…">`。
- 夹具：`Fixtures\`，`CopyToOutputDirectory=PreserveNewest`；`*.vbo` 默认 gitignore，需 `!ChassisMasterTestSuite.Tests/Fixtures/*.vbo`。

## 模式

```csharp
[Fact]
public void Reader_MapsExpectedChannel()
{
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gspot-g4-mini.vbo");
    Assert.True(File.Exists(path));
    var samples = new VboReader(path).ReadAll();
    Assert.NotEmpty(samples);
    Assert.Equal(expected, samples[0].SpeedKph, precision: 2);
}
```

- 浮点：`Assert.Equal(a, b, precision: n)` 或 `Assert.InRange`。
- 临时文件：`Path.GetTempPath()` + `Guid`，`try/finally` 删除（参考 `VboRoundTripTests`）。
- 不测 UI：不实例化 `MainWindow` / `TrackMapPanel`；测 `TrackProjection`、`VboReader`、`SampleEnricher` 等。

## 命名与过滤

- 类名：`FooTests`；方法：`Unit_Scenario_Expected`。
- 本地快速跑：

```powershell
dotnet test ChassisMasterTestSuite.Tests\ChassisMasterTestSuite.Tests.csproj --filter 'FullyQualifiedName~FooTests'
```

## 应覆盖的边界（按模块）

| 模块 | 建议断言 |
|------|----------|
| VBO Racelogic | 往返东经仍为正；arcmin `/60` |
| VBO GSpot | 速度非 ms 量级；lat/lon 过 `IsValidGps` |
| GPS 过滤 | 长距离轨迹不被固定 5km 杀光；无效点剔除 |
| KML | 点数、首末坐标、空文件 |
| Enricher | elapsed / distance 单调；粗 GPS 跳跃时 speed×dt 回退 |

## 禁止

- 把真实路测 GB 级文件塞进仓库。
- 依赖本机绝对路径（用 `AppContext.BaseDirectory` / 相对 Fixtures）。
- 在测试里 `Thread.Sleep` 等长时间阻塞（除非测超时且有理由）。
