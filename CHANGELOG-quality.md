# Quality tranche 1 — history / recording safety

Branch: `feature/quality-history-recording-safety`  
Date: 2026-10-09 (Asia/Shanghai)

## Files changed

| File | Change |
|------|--------|
| `Core/SampleHistoryBuffer.cs` | **New** — lock + circular array, capacity default 1_000_000; `Add` / `AddRange` / `Clear` / `Count` / `Snapshot()`; O(1) drop-oldest |
| `MainWindow.xaml.cs` | `_sampleHistory` → `SampleHistoryBuffer`; `SetTrack` / plots use `Snapshot()`; `_historyOfflineMode` on VBO load; `EnterLiveSourceMode()` → `SetLiveCore()` + clear offline flag on UDP/GSpot switch; recording drops surfaced on Lost TextBlock as `+R{n}` |
| `Recorder/VboRecorder.cs` | `DroppedSamples` (Interlocked) when `TryWrite` fails; docs note Wait + TryWrite still returns false when full |
| `Recorder/CsvRecorder.cs` | Idempotent `Dispose` (aligned with VboRecorder) |
| `Core/DataBus.cs` | `DroppedPublishCount` when `TryPublish` fails (usually 0 under DropOldest) |
| `Communication/UdpReceiver.cs` | On Faulted / non-Dispose `RunAsync` exit: dispose socket, reset `_started` so toolbar UDP retry works |
| `.gitignore` | Add `*.vbo` |
| `CHANGELOG-quality.md` | This note |

## Behavior notes

- UI never receives the live mutable history buffer; Track Map and plots always get a stable snapshot copy.
- Loading a VBO sets offline mode so live bus samples do not append into replay history until the user switches back to a live source (UDP / GSpot).
- Recording integrity: silent `TryWrite` failures are counted and shown in the status Lost area (tooltip explains network vs record drops). No MessageBox spam.
- Plot downsampling / MainWindow Session split / GSpot password URL are **out of scope** for this tranche (see tranche 2 for plots).

## Verify manually (Windows / WPF)

1. Build & run on Windows (`dotnet build` / Visual Studio).
2. Stream UDP ~100 Hz for several minutes — no InvalidOperationException from history; Track Map still updates.
3. Start recording, artificially stall disk / fill recorder queue if possible — Lost should show `+R{n}` when drops occur; VBO row count vs expected.
4. Force UDP Faulted (e.g. kill path / network), click toolbar **UDP** — should rebind and listen again (was no-op before).
5. Open a VBO (Replay) while UDP still running — curves stay on file data; switch to UDP/GSpot — channel dropdown returns to live core set; new samples append again.
6. Confirm `*.vbo` is ignored by git outside intentional adds.

---

# Quality tranche 2 — curve performance (visible window + downsample)

Branch: `feature/quality-history-recording-safety`  
Date: 2026-10-09 (Asia/Shanghai)

## Files changed

| File | Change |
|------|--------|
| `Core/PlotDownsampler.cs` | **New** — `MaxPlotPoints = 12_000`; `FindVisibleIndexRange`; min-max `BuildDownsampleIndices`; `ExtractXs` / `ExtractYs` / `ExtractSamples` |
| `MainWindow.xaml.cs` | Shared one `Snapshot()` per `RefreshAllPlots` tick; `RefreshPlot` uses visible-window + min-max downsample; dirty-check (`Count` + last `Timestamp`/`Sequence`) + `_forcePlotRebuild` for UI/gesture; UI callers use `RefreshAllPlots(force: true)`; timer stays unforced |
| `CHANGELOG-quality.md` | This tranche |

## Behavior notes

- **MaxPlotPoints = 12000.** If visible index count ≤ cap, copy all; else bucket min-max (primary channel Y) so peaks survive better than stride.
- **One Snapshot per tick** shared by all plots and Track Map `SetTrack` (Track Map still has its own 250 ms throttle internally).
- **Dirty check:** skip Clear+rebuild when history `Count` and last sample `Timestamp`/`Sequence` match the last successful rebuild. Channel / X-axis / AutoScale / Add-Remove plot-channel / Reset / VBO load call `force: true`. Pan/zoom end and SuspendAutoScale set `_forcePlotRebuild` so a new visible window rebuilds even if samples are unchanged.
- **Cursor / Dashboard:** `LastXs` / `LastChannelSeries` / `LastSamples` stay **aligned on the downsampled series** (not full history). Nearest-index cursor still works; freeze readout uses the downsampled neighbor. Beijing time axis / AutoScale / LockedLimits / manual pan path unchanged.
- MainWindow Session split **not** started in this tranche.

## Verify manually (Windows / WPF)

1. Sync `/workspace/cmts-edit` → P16 `Chassis Master Test Suite` working tree; `dotnet build` Release.
2. Load a large VBO (or stream until history ≫ 12k). Curves should stay responsive at ~10 Hz; CPU/alloc lower than full-array Clear.
3. Pause / idle with no new samples — plot rebuild should skip (no flicker / Clear storm); change channel or X axis — must redraw immediately.
4. Turn off Auto X, pan/zoom — after gesture, series should re-downsample to the new window; peaks in view should remain visible.
5. Place cursor, drag, Escape; Dashboard freeze / Track Map cursor sample still track. Beijing time tick labels still look correct.
6. Track Map still updates (throttled); no regression from shared Snapshot.

## Build on Linux box

WPF (`net10.0-windows` + `UseWPF`) typically cannot produce a full UI binary on Linux; treat Linux `dotnet build` failure as expected. Sync back to P16/Windows for compile.

**This box:** `dotnet` 10.0.401 under `~/.dotnet`. Default `dotnet build` fails with NETSDK1100 (Windows TFM). With `-p:EnableWindowsTargeting=true`, Release build **succeeded** (0 errors; NU1701 SkiaSharp.Views.WPF warnings only). Prefer full verify on P16/Windows.
