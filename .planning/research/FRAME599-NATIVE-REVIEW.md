# Build 599 native-frame probe review

Evidence is the paired, gitignored `.planning/debug/steam_frame/Player.log` and
`.planning/debug/steam_frame/LogOutput.log`. Both identify ModBuild 599 and dev
`0035d7916` (`Player.log:40,153`; `LogOutput.log:17,70`). This run contains a
large `ProcGen` scenario, not a 3D-map performance comparison.

## The probe ran; the BepInEx file omitted its severity

`Player.log:343,590,1433,2649,5269,6950,7315` contains seven `[Perf] NATIVE`
summaries at BepInEx Debug severity. `LogOutput.log` contains none and has no
`[Debug` lines at all, but it does contain the Debug-gated `LOGIC PHASES` on
`[Perf] SPLIT` (`:2391,2731`) and `HEAD RENDER COUNTERS` (`:1270`) at Info
severity. Source explains the routing: `Core/Perf/PerfNativeLoopProbe.cs:154`
calls `VRLog.Debug`; `Core/VRLog.cs:178-180` sends that to `LogDebug`, whereas
`VRLog.Info` sends to `LogInfo` (`:168-170`). This installation's `LogOutput.log`
does not retain Debug-severity events. Its exact BepInEx disk-filter setting is
not in the supplied evidence. The native probe does **not** need another
hardware run merely to recover these readings: they are in the matching
`Player.log`.

Early zero-call captures track scene population, not a failed patch. The first
two summaries have no active sampled callbacks; `Player.log:1433` then records
101 `LeanTween.Update` calls, `:2649` records 4,200 `ExtendedButton.Update`
calls in 120 frames (35/frame), and `:7315` records 14,161 (118/frame) in the
large scenario. The probe also records actor and procedural-tile callbacks once
those objects exist.

## What the measured callbacks explain

In the final large-scenario capture, the nine selected callbacks total about
**1.13 ms per sampled frame** (`Player.log:7315`). The largest are
`ExtendedButton.Update` 0.366 ms, `ProceduralTileObserver.Update` 0.305 ms,
and `ActorBehaviour.Update` 0.178 ms. The probe estimates its managed hook
body at 0.124 ms/frame for that call volume; Harmony dispatch is not isolated.
The matching `SPLIT` line (`LogOutput.log:2731`) reports 38.33 ms Update,
2.43 ms between phases and 13.99 ms LateUpdate over 118 aligned frames from
the same initial capture. The full 171-frame window averages 57.34 ms in the
whole logic span, 23.29 ms in the render loop and 37.01 ms blocked, with
22.64 ms/frame of named mod work (`FRAME`, `:2727`). The callback totals are
inclusive, the mod's step scopes overlap, and the phase and full-window
populations differ. None of these numbers supports subtracting one category
from another to label the remainder “base game.” They do rule out the nine
measured methods as the main steady CPU bottleneck in this capture.

The Unity profiler markers remain unavailable (`SPLIT`, `:2731`); the new
render counters also return only untrusted zero samples (`HEAD RENDER
COUNTERS`, `:1270`). Managed callback probes cannot price Unity's native
Animator, Cloth, particle, UI or transform work. A view/visible-renderer
correlation alone cannot identify which of those, if any, is expensive.

## Next bounded attribution

If another method capture is justified, prefer a few singleton callbacks with
possible fan-out: `Choreographer.Update` processes queued scenario messages
inside an eight-millisecond time-limited loop
(`decompiled/GH.Runtime/Choreographer.cs:2344-2390`); the limit is **not** a
measured ongoing cost. `PlatformLayer.Update` invokes
`SteamClient.RunCallbacks()` each frame (`.../PlatformLayer.cs:282-287`).
`Updater.Update`, `ApparanceEngine.Update` and `ApparanceResources.Update` appear
in the earlier menu SIM type list (`LogOutput.log:363`), but their bodies are
not in the available decompiled source, so large-scenario presence, patch
availability and cost need measurement.
`WorldspaceUITools.Update` sorts the figure-panel list
(`.../WorldspaceUITools.cs:91-116`); it is a lower-priority control probe.
`Chronos.Clock.Update` only advances time/scale
(`decompiled/ThirdParty/Chronos/Clock.cs:163-181`) and is a weak candidate.

No performance optimization or headset gain follows from this review.
