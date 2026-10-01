# Build 599 Steam Frame render review

Read-only analysis of the Build 599 files in the main checkout's gitignored
`.planning/debug/steam_frame/`. No rendering change or hardware gain is claimed.

## What the run actually measured

- `LogOutput.log:17` and `Player.log:40` both report ModBuild 599. Matching
  `FRAME`/`SPLIT` windows (including frame 4060) show that `Player.log` is an
  earlier partial copy of this run, not a different-build comparison. Its file
  mtime is 14:28 versus 17:39 for `LogOutput.log`. `LogOutput.log` omits Debug
  entries; `Player.log` retains the `NATIVE` probe lines. Use the latter for
  those measurements and the former for the later session tail.
- The only `HEAD RENDER COUNTERS` line (`LogOutput.log:1270`, also
  `Player.log:4732`) reports **only zero samples** for Draw Calls, Batches,
  SetPass and Triangles. The probe correctly labels every counter `n/a`.
  There is no measured draw-call count, batching efficiency or GPU busy time.
- The last complete scenario window (`LogOutput.log:2727–2734`, matching
  `Player.log:7310–7317`) has mean 117.64 ms/frame, p50 109.14 ms, 171
  frames. The mean main-thread logic span is 57.34 ms, render loop 23.29 ms,
  remainder 37.01 ms. The head camera accounts for 20.79 ms of the render
  loop (6.04 cull + 14.75 submit) over two MultiPass eye renders;
  `ScenarioCamera` 1.14 ms and `UI Camera` 0.25 ms. This window's viewpoint
  changes substantially (head distance 26.6–102.0 world units, visible
  estimate p50 4,341); it is not a matched fixed-view A/B. The remainder
  includes waits and work between the timed spans, so it cannot be called GPU
  busy time. The XR `gpu` number tracks the interval and is unusable here.
- In the matching `Player.log:7315` Debug capture, selected game callbacks
  total roughly 1.13 ms/frame *before accounting for inclusive overlap*:
  `ProceduralTileObserver.Update` 0.305, `WorldspaceDisplayPanelBase.LateUpdate`
  0.040, `ActorBehaviour.Update/LateUpdate` 0.178/0.140,
  `ExtendedButton.Update` 0.366, and the remaining four targets about 0.10.
  These targets do not explain the 57 ms logic span. Harmony dispatch is not
  isolated, and unmeasured game/Unity work remains.
- The sole `SCENE`/`SIM` census in this Build 599 run is the early menu sample
  at `LogOutput.log:363–364`: five renderers and no animators or particles.
  Its 77.9 ms sample invokes the eight-window budget cooldown; subsequent
  scenario `SCENE` lines all say `skipped`. It must not be used to describe
  the later 4,341-visible-renderer scenario. Build 598's separate census of
  5,689 visible-to-any-camera renderers and 5,786 material slots remains a
  candidate inventory, not a measured number of per-eye draws. In particular,
  the displayed `~11,572 draw calls before batching` is an *unbatched slot
  estimate*, not a lower bound on actual draw calls.

## Structural levers checked against source

| Candidate | Current evidence and verdict |
| --- | --- |
| Enable Single-Pass Instanced | `Core/StereoModeConfig.cs` records the game shader extraction: stereo keywords occur in metadata but compiled game subprograms lack stereo variants. Its mod-shader list is historically worded (newer town shaders do include stereo macros), but the game-shader blocker remains. `WorldUI/FlatScreen/FlatScreenStereo.4.PerEye.cs` switches the 2D screen's texture in separate left/right pre-render callbacks; a single pass selects only one. `Core/Startup/OpenXRBootstrap.cs` therefore fixes MultiPass. This cannot be offered as a safe runtime toggle without game-shader replacements, a rebuilt XR-enabled asset bundle and a stereo UI rewrite. |
| Skip the game's scenario camera | The Frame's desktop scrub already retargets `ScenarioCamera` to an unused sink and zeroes its culling mask during its render (`FlatScreen.3.Desktop.cs:213–311`; run `LogOutput.log:983`). Its residual 1.14 ms includes clear/callback/image-effect cost. Disabling the component breaks `Camera.main` users and map/flat camera ownership. Even a proven lossless residual skip would be a small fraction of 117.64 ms. |
| Toggle more ordinary graphics options | `GFX` at `LogOutput.log:2734` already has shadows disabled, zero pixel lights and MSAA, no head depth texture and no head command buffers. Graphics jobs are on (`LogOutput.log:25`). The head mask is broad, but Build 598's populated layers (Default, Ground, Wall, actors, UI and mod content) are already in the game's own scenario mask plus protected UI/mod layers. A mask-only change has no established large removable group. All 223 `LODGroup`s resolve to LOD2 in the Build 599 model (`LogOutput.log:1467`), so reducing `lodBias` further could remove geometry, but changes the image and is not lossless. |
| Disable `OnWillRenderObject` callbacks | The decompiled game references four callback types: `AutomaticLOD`, Decalicious `Decal`, standard `Water` and `WaterTile`. The 447 active `AutomaticLOD` behaviours in the scenario are all in `UnityLODGroup` mode and are already disabled by `AutomaticLodIdleSkip` (`LogOutput.log:1467–1468`); their `OnWillRenderObject` merely assigns a camera that their early-returning `Update` never reads. `Decal` can register render commands and `Water` can issue reflection renders; their active scenario populations and costs are not measured. The head reports zero command buffers, which argues against a large Decalicious head-camera path but does not prove none. Disabling callbacks blindly risks native effects and water. |
| Batch or merge map meshes | Build 15 removed blanket static batching after Apparance clones of batched sources appeared without material slots and revealed rooms went invisible (`NetProtocol.cs`, Build 15 note). Build 598's `Amp_Basic_Foliage` 2,851 and `Amp_Basic_WallFade` 1,601 submitted slots suggest where a *narrow* load-time renderer consolidation might matter, but the release player exposes no actual draw counter. Tile reveal, LOD membership, per-renderer fade blocks, material loading and collider/gameplay provenance must stay intact. There is no source-proven safe subset from this run. |

## Concrete next path

The first useful render experiment is a **loading-time eligibility audit** in one
fixed, fully revealed large scenario. Capture the same renderer inventory used
by the wall and scene profiles *after scenario loading*, not during the menu's
cooldown. For each candidate foliage group, record shared mesh/material,
submesh and shader pass, active LOD level, property block, transform/static
status, wall/room attachment, reveal and material-loader ownership. Record a
head-specific visible count instead of inferring it from `Renderer.isVisible`,
which means visible to any camera. This can identify a small group of truly
immutable, non-fading instances to consolidate during loading; if none exists,
stop before changing renderers. A graphics capture or supported runtime draw
counter is still needed to prove whether Unity already instances the shared
materials and whether consolidation can save actual submissions. Match the
head pose, eye target and native quality setting for every hardware A/B.

Independently, the large unassigned logic span needs a broader Unity/native
phase attribution than the nine cheap callbacks just sampled. It is a separate
performance wall: even eliminating the measured 23.29 ms render loop would
leave the mean 57.34 ms logic span above the target 72 Hz budget of 13.89 ms.
The continuing 250–292 ms wall-table commits are a separate hitch problem;
the maintainer's wall-off trial says they do not alone explain steady FPS.
