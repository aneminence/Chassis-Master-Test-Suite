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
- Plot downsampling / MainWindow Session split / GSpot password URL are **out of scope** for this tranche.

## Verify manually (Windows / WPF)

1. Build & run on Windows (`dotnet build` / Visual Studio).
2. Stream UDP ~100 Hz for several minutes — no InvalidOperationException from history; Track Map still updates.
3. Start recording, artificially stall disk / fill recorder queue if possible — Lost should show `+R{n}` when drops occur; VBO row count vs expected.
4. Force UDP Faulted (e.g. kill path / network), click toolbar **UDP** — should rebind and listen again (was no-op before).
5. Open a VBO (Replay) while UDP still running — curves stay on file data; switch to UDP/GSpot — channel dropdown returns to live core set; new samples append again.
6. Confirm `*.vbo` is ignored by git outside intentional adds.

## Build on Linux box

WPF (`net10.0-windows` + `UseWPF`) typically cannot produce a full UI binary on Linux; treat Linux `dotnet build` failure as expected. Sync `/workspace/cmts-edit` back to the P16 working tree and build there.

**This box:** No `dotnet` SDK installed (`dotnet: command not found`). Code reviewed statically; full compile must happen on P16/Windows.
