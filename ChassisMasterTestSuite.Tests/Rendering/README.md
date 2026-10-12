# Track Map rendering regressions (Windows)

Run from the application directory:

```powershell
dotnet test .\ChassisMasterTestSuite.Tests\Rendering\ChassisMasterTestSuite.RenderingTests.csproj
```

This suite uses the application's actual ScottPlot 5.1.59 and WPF controls on STA
threads. It renders offscreen; no desktop windows, network tile servers, user
preferences, or user map cache are required. If the application is running, use
`-p:BaseOutputPath=D:\CMTS\_map_alignment_probe\tests-build\` to avoid replacing
its executable. The separate pure-logic test project remains cross-platform.

The same geographic point takes two independent paths: ScottPlot's track pixel
conversion, and XYZ fractional coordinates inside the positioned tile image.
The paths must agree within 0.01 WPF DIP. Cases cover 100%, 125%, 150%, 175%, and
200% simulated display scaling, an independent Plot.ScaleFactor, three window
sizes, nonzero control offsets, plot margins, square-axis rules, map zoom levels
5/12/15/19, and near/far views. These are simulated render-surface sizes, not
changes to the physical monitor's DPI setting.

The original formula is retained only in a regression assertion: at 150% DPI,
the center of an 800x600 DIP viewport was displaced by (200,150) DIPs because
unscaled ScottPlot DataRect coordinates were assigned directly to a WPF Canvas.

Controller tests verify immediate pan/zoom/resize placement, use of completed
frames rather than pending axis changes, delayed image completion after a pan,
download scheduling during continuous rendering, source switching, and ignoring
queued work after disable/dispose. The same snapshot supplies the tile clip.

Manual acceptance on the target display: open a known GPS track, switch on the
basemap, compare a distinctive bend at several zoom levels, pan continuously,
resize the window, and move it between monitors with different DPI settings.
Geographic alignment of real satellite imagery and GPS data is separate from
screen-coordinate alignment and still depends on the selected source/data.

## Verification on 2026-10-12

- Release application build: passed (the pre-existing SkiaSharp.Views.WPF NU1701
  compatibility warning remains).
- Rendering suite: 39 passed, 0 failed, 0 skipped. Includes 3,600 independent
  track/tile point comparisons with a 0.01 DIP tolerance.
- Existing pure-logic suite: 80 passed, 0 failed, 0 skipped.
- VSTest could not attach its child testhost in the agent environment (Windows
  process access denied). Both test assemblies were instead run using xUnit's
  own `XunitTestFramework.GetExecutor(...).RunAll(...)` in the same process.
  Local runner: `D:\CMTS\_map_test_runner\MapTestRunner.csproj`.
  Output: `D:\CMTS\_map_alignment_probe\verification-results.txt`.
- Ready-to-run application: `D:\CMTS\_build_verify\track-map-fix\Chassis Master Test Suite.exe`.
- Application-level manual acceptance: the user tested the fixed build and
  confirmed it works on 2026-10-12. Physical monitor switching was not verified
  by the automated suite.
