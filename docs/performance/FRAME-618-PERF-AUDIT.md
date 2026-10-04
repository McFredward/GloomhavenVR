# Steam Frame Build 617 hardware audit

The new capture does **not** demonstrate an overall steady-play improvement over
Build 616. Original figure ghosts are prepared and reused, but ordinary gameplay
still contains large native callback, stat-panel and wall-publication spikes.
This is an evidence report, not a shipped Build 618 performance fix.

## Inputs and scope

Immutable inputs are retained in the main checkout under
`.planning/debug/frame618-review/inputs/`. Both sinks identify **ModBuild 617,
commit e1da52eaa**, Unity 2021.3.5f1, D3D11/Turnip Adreno 750, SteamVR/OpenXR
2.17.10 and MultiPass. The scenario requests **3408 × 3408 per eye**, scale 1.00.
The run goes directly into a scenario; it does not establish a 3D-map visit.

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| LogOutput.log | 11,313,709 | `8712621e64320bfbfe62ff0d369f00b12993e5a1fab9b81aedb7c4d24007517b` |
| Player.log | 12,302,050 | `c3eaba8e17571e461201ac4cf770080969c892a2348892412dad6f8a0da69a47` |
| openxr-diagnostics.log | 95,190 | `1e428cd02b82f81d624144bcc799a3cd1b7d108cbbb84b5be186582c999cbf35` |

All line references below use Python `str.splitlines()` normalization. This can
differ from physical LF line numbers in Player.log. Unique marker/frame names are
provided where useful. Reproduction, input/source hashes, all parsed windows and
individual spike receipts are retained under
`.planning/debug/frame618-review/pacing/{reproduce.py,frame-perf-report.py,metrics.json,reproduce.out}`.
Run `python3 .planning/debug/frame618-review/pacing/reproduce.py` from the main
checkout. The script reads the immutable inputs and retained 615/616 baseline
receipts; it never edits the inputs or runs tests.

The numerical fields of all **22 FRAME summaries match between LogOutput.log and
Player.log**. Only LogOutput.log contributes frame counts, durations and aggregates;
the duplicate sink is verification, not another sample population.

The scenario loading indicator becomes hidden at LogOutput line 2621. Player
markers show **38.82 s** of interaction preparation inside that loading period.
Summary 9, line 3172, owns the preceding 30.1 s and still mixes loading with play,
so it is excluded from the headline aggregate. Initial loading/configuration work
is not an optimization target in this comparison.

Summaries **10–20** are the primary fully loaded revision-2 comparison. Summary
**21** is also fully loaded and worn: its ordinary summon/burn actions and new
actor revision remain gameplay, not loading. It is reported separately and included
in the all-loaded result. Summary 22 crosses presence loss at line 9099 and cannot
be partitioned from these summaries; it and the doffed tail are excluded. The
382.03 ms maximum below is the maximum of the selected completed windows, not a
claim about the entire file's tail.

## Loaded frame times

Means are weighted by the actual frame counts. Percentiles are **ranges of the
original window p95 readings**, never a pooled p95.

| Capture/selection | Windows / frames / seconds | Frame mean | Mod inclusive | Logic bracket | Render bracket | Unbracketed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 615 retained loaded baseline | 7 / 2,223 / 110.2 | 49.60 ms | 15.53 ms | 28.13 ms | 6.14 ms | 15.33 ms |
| 616 retained steady baseline | 6 / 2,236 / 110.3 | 49.35 ms | 16.18 ms | 28.69 ms | 7.10 ms | 13.56 ms |
| 617 revision 2, windows 10–20 | 11 / 4,496 / 230.4 | 51.33 ms | 17.32 ms | 30.67 ms | 6.96 ms | 13.70 ms |
| 617 all complete loaded/worn, 10–21 | 12 / 4,687 / 240.4 | 51.37 ms | 17.43 ms | 30.78 ms | 6.95 ms | 13.65 ms |

The 617 revision-2 mean is about **4.0% higher than 616**, not lower. Its window
means span **47.92–54.57 ms**, p95 readings **83.97–99.16 ms**, and maximum
**382.03 ms**. The first six fully loaded windows average 50.79 ms; the last five
52.19 ms. The later revision-3 window averages 52.47 ms. Including it does not
change the conclusion. Natural changes of view, action, actor population and Debug
work limit causal attribution, but do not prevent this practical comparison.
The capture supports the user's lack of a noticeable new improvement; it does not
prove that any one new optimization made the run slower.

## What actually applied

- Player markers 8135, 8227 and 8287 report **three prepared ghost reuses, zero
  misses and zero invalidations** for SunDemon and SpittingDrake. The measured
  `Hands.Ghost` scope peaks at **2.42 ms** in the selected windows. Total
  `Hands.NearGrip.Pickup` still reaches **67.85 ms**, so ghost reuse does not
  establish that every part of pickup is cheap.
- At Player line 6513 the figure budget reports 17 actors, **8/8 original LOD caps**,
  **15 disabled cloth solvers**, **36 verified mesh derivatives**, and admitted
  body vertices **278,708 → 126,276**. Distance LOD is enabled, 18 far-level
  meshes selected, bone limit 2. Optional actor FX masks 53 renderers and pauses
  38 particle solvers. The later 18-actor reading has 39 derivatives and
  **291,750 → 186,776** admitted vertices, with zero far-level selections.
  These are actual selected-resource changes; different poses/views mean they
  do not measure an isolated mesh slider FPS gain.
- GFX line 5308 confirms Fastest, shadows disabled, pixel lights/AA zero,
  lodBias 0.30, scenery/detail/FX densities zero, cloth off, floor chunks/simple
  floor shading on, reduced generation and shared wall/light read caches on.
  These settings are active; remaining work cannot be attributed to an unchanged
  quality preset.

## Ordinary-play spikes that remain

These are measured inclusive scopes or aligned native callback observations.
Native, mod and child scopes can nest; **do not add the table's costs together**.

| LogOutput marker | Whole frame / mod | Measured contributor | Interpretation |
| --- | ---: | --- | --- |
| frame 5739, line 2722 | 225.31 / 152.54 ms | `Cards.Driver` 118.78 ms | After the spinner disappeared, before the first completely loaded summary; genuine initial gameplay hitch, outside the headline means. |
| frame 6463, line 3636 | 227.76 / 149.33 ms | `StatPanelSurface` **126.19 ms** | Stat preview work remains expensive after loading. Preparing existing original artwork did not prebuild all conversion, asynchronous content or second-hand snapshot work. |
| frame 7689, line 5463 | 262.12 / 125.53 ms | `UseBarsSurface` **68.96 ms**, native `Choreographer.Update` **68.060 ms** | Original bonus-widget/native game work remains an interaction target. Neither observation alone separates all nested native/mod work. |
| frame 8646, line 6260 | **382.03 / 79.04 ms** | native `Choreographer.Update` **207.257 ms**; `Cards.Driver` 58.01 ms | A selected native game callback now has direct positive evidence on this long frame; attributing the remainder to GPU would be unsupported. |
| frames 7719, 8686, 9373, 9547, 9869, 9953, 10048, 10511 | 184–312 ms | `WallFade.Rescan` **145–210 ms** | Repeated ordinary-play synchronous publication bursts. The first is a staleness ceiling; subsequent BUDGET records identify scene-signature changes, not a new room, moved board or slider load. |
| frame 10503, line 8088 | **364.94 / 16.27 ms** | Listed mod steps only about 2 ms each | Large residual cost remains unattributed by the selected callback/marker coverage. This must remain unknown rather than being assigned to GC or GPU. |

The selected summaries also record **24 sprite-mip and two atlas-mip misses**;
`Cards.SpriteMipMiss` peaks at **94.16 ms**. Prewarming has reduced a specific
figure construction path, but it has not eliminated all first-use presentation
resources. The stat-source boundary is explicit in
[the preparation report](FRAME-617-FIGURE-PREPARATION.md): only existing original
panel artwork is warmed, never synthetic native `Show` calls or stale previews.

Ranked next work is therefore: price/remove false-positive wall publications
without weakening true scenery-change safety; separate the 126 ms stat/69 ms
bonus-widget work into resource construction versus required native refresh;
identify remaining sprite misses; and instrument the actual long Choreographer
subpath before considering optional native work suppression. The selected
171 emitted SPIKE lines use changing runtime thresholds and capped logging;
their count is not an uncapped stall rate or a comparable baseline count.

## Completed scene and simulation inventories

Unlike the previous incomplete captures, SIM/SCENE/GFX lines **5305/5306/5308**
complete for the loaded scenario. Their observation spans **106.73 s**, so the
inventory is not an instantaneous snapshot. They report:

- **4,735 active renderers**, 4,415 enabled, **2,389 forceRenderingOff**, and
  **1,539 enabled/visible/in-head-mask candidates**. `isVisible` can refer to any
  camera; these are candidate submissions, not measured draw calls. MeshRenderer
  contributes 1,424 candidates, ParticleSystemRenderer 58, SkinnedMeshRenderer 55.
  The main Maps/ABCHL group contributes 1,253 candidates; mod layer 27 has 31 total
  renderers and 30 enabled. Native scenery remains the largest renderer population.
- **10,481 active-object MonoBehaviours**, 9,630 enabled; 440 Update, 62 LateUpdate
  and 24 FixedUpdate declarations, including 40 mod Update and 14 mod LateUpdate.
  Instance counts alone do not measure CPU cost. Examples include 116 ExtendedButton,
  43 ActivateWallFadeInGame, 42 ProceduralWall and 21 CInteractableActor components.
- **129 enabled Animators**, 35 with runtime controllers; 99 AlwaysAnimate and
  30 CullUpdateTransforms. **197 particle systems**, 59 playing, 40 emitting,
  1,985 live particles; 143 AlwaysSimulate. Optional effect work is a potential
  compromise, but gameplay-linked `IsAlive` effects cannot simply be paused.
- **223 active/enabled original LOD groups**. The empty menu's LOD reading must
  not be used to claim that native scenario LOD is absent.

The full diagnostic inventory itself costs **614.8 ms across 2,083 bounded
slices**, maximum slice **3.270 ms**, and then invokes its cooldown. It is Debug
measurement work, not a single 615 ms gameplay pause. The selected `Perf.SceneProfileSlice`
scope averages about **0.276 ms per measured frame**, with a worst selected
scope of **10.68 ms**. These timers have different boundaries:
`PerfSceneProfile.Incremental.cs:222` opens the scope around the entire `Tick`
job; line 239 records the internal slice maximum **before** final roster
publication, GFX formatting, logging and cleanup, which remain inside the scope.
Thus 3.270 ms does not bound the whole scoped call, and the log does not identify
which individual finalization operation owns the additional peak. This overhead belongs in
the comparison and does not account for the 145–210 ms wall bursts.

## GC, cameras and remaining limits

The 11 primary summaries report **15/15/15 collection deltas** and **1,251 MB**
allocated over 230.4 s, about **5.43 MB/s** versus the retained 616 reading
2.27 MB/s. Of 171 emitted primary SPIKE lines, **169 report 0/0/0 collections**.
Only one of the 16 emitted frames at least 180 ms has a collection: frame 9373,
which simultaneously measures a 209.93 ms wall rescan. Collection counts do not
measure pause duration, establish causality or prove a memory leak. Changing
actions, resources and Debug instrumentation also change allocation.

Actual main-thread `GC.Collect` markers are positive on three emitted primary
spikes: **20.479 ms** on frame 9373 (311.58 ms total, collection deltas 1/1/1),
**26.477 ms** on frame 10521 (163.62 ms total, 1/1/1), and **3.300 ms** on
frame 6809 (123.95 ms total, 0/0/0). The latter also demonstrates that zero
collection deltas must not be treated as proof of zero marked GC work. The
20.479 ms marker is real coincident GC cost, not an explanation for the entire
311.58 ms frame or permission to add it to nested wall scopes.

The actual `[WorldUI] DesktopMirrorLeftEye` value is **True** here. Player line
275 requests LEFT EYE; GFX confirms the value. The Frame default in
`Core/Startup/FrameDefaults.cs` is False, while `WorldUIConfig.cs` preserves
saved profiles. Changing that exact existing key to False requests a black
spectator backbuffer. `FrameDesktopPolicy` and `FlatScreen.3.Desktop.cs` suppress
discarded native flat drawing independently of this setting. Loaded SPLITs show
only **GloomhavenVR.HeadCamera, two MultiPass passes/frame**. This capture does
not establish a second native scene-camera rendering regression, and no GPU
saving from the spectator setting is quantified.

FrameTimingManager returns no samples. `Canvas.BuildBatch`, `Animator.Update`
and `WaitForTargetFPS` recorders remain unavailable in this player.
`GC.Collect` has the positive samples listed above and reads 0.000 ms on the
other cited spikes; `Gfx.WaitForPresentOnGfxThread` reads 0.000 ms on these cited
spikes. Those samples do not cover unmeasured engine/native threads,
all XR waits or GPU execution. Logic spans average about 30.7 ms, render-callback
spans 7.0 ms and unbracketed work/waits 13.7 ms; there is measured CPU work to
address, but no measured GPU-busy split. SteamVR refresh/adaptive pacing changes
also mean these are application frame times, not compositor/headset FPS.

Player.log contains eight Hydra DNS endpoint errors and expected platform/overlay
startup warnings. It contains no throw/stack report of `InvalidCastException`,
`NullReferenceException`, `ArgumentException` or `MissingReferenceException`.
Informational startup text mentions exception types without reporting a throw. The DNS failures
are real, but no temporal causal link to the reported long gameplay frames is
established. Absence of those exceptions does not prove the headset appearance
or interaction correctness.
