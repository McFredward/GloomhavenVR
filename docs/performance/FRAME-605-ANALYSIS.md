# Steam Frame Build 605 hardware analysis

Internal engineering report, 2026-10-02. This is analysis of the supplied run,
not a new implementation or a headset acceptance claim.

## Evidence and scope

Both supplied game logs identify **ModBuild 605, b80989772**. Immutable inputs,
SHA-256 manifests, parsed measurement windows and the original-asset audit are
retained in `.planning/debug/frame605-run-analysis/`. No new screenshots were
present in the supplied Steam Frame folder for this analysis. The scene sequence
is Intro, Gloomhaven_unified, MainMenu, Game and ProcGen; registration of a map
scene does not establish that the player visited the 3D map.

Log anchors below use POSIX LF numbering. Two lone CR characters in the supplied
logs shift the report parser's universal-newline anchors by two lines later in
the file. Window IDs refer to `perf.json`, not to manual LF anchors.

Exclude startup/generation windows 1–6 and mixed-tracking window 11. Tracked,
loaded windows 7–10 and 12–16 contain 3,236 frames over 170.4 seconds:

| Measurement | Result |
|---|---:|
| Window mean frame time | 47.64–58.77 ms |
| Frame-weighted mean | 52.67 ms, approximately 19 application frames/s |
| Instrumented logic phase | 27.54 ms/frame |
| Instrumented camera/render phase | 11.28 ms/frame |
| Instrumented mod steps | 15.22 ms/frame, overlapping the phases above |
| Eye target | 3408 × 3408 per eye, MultiPass |

Later loaded windows, with changing settings/view and menu state, reach 72.30 ms.
Earlier Build604 observations were 65.61–88.37 ms, but these runs are not matched
A/B views. The lower observed range supports the player's reported progress;
it cannot establish a percentage improvement. A 72 FPS target needs 13.89 ms.
XR-reported 12/24 Hz presentation changes must not be read as physical headset
refresh rate or direct application FPS. The reported XR GPU statistic includes
interval/wait behavior; actual GPU busy samples remain unavailable. Neither the
residual frame time nor camera callback spans prove GPU saturation.

## What the current budgets actually remove

LogOutput.log:1896–1898 records 287 deferred native decorative prefab instances,
representing 1,234 renderer instances, and 2,332 owned masks after preparation.
Source prefab/resource bundles remain loaded. Restoring vegetation during the
test creates the original deferred visuals; returning to zero masks them again
and eventually reports 3,242 masks. This restoration is expected, not evidence
of a leak or a failed zero budget.

Figure diagnostics report 17 actors, 53 masked ambient renderer parts, 35–38
paused particle solvers and 15 disabled cloth solvers. The visible demon-effect
change therefore has direct log support. Quality is already Fastest with shadows
disabled, pixel lights zero, MSAA zero, LOD bias 0.30, reduced native generation
and cloth disabled. Recommending these as new measures would repeat active work.

## Why the figure sliders can look unchanged

Player.log:6432, 10812, 10880, 10914 and 10980 show the enemy slider changing its
native LOD cap and restoring it. Player detail remains zero in every reported
sample: this run does not contain a logged player-detail 0–100 comparison.
The native near-LOD table changes from 95,227 → 46,748 vertices at enemy zero
to 95,227 → 77,433 with enemy detail 100. The cached any-camera visible body
aggregate nevertheless stays **39 meshes / 125,614 vertices** throughout.

Read-only original prefab/mesh inspection explains the incomplete coverage:

| Actors in this scenario | Count | Existing coarse native body meshes |
|---|---:|---|
| Summoner, Brute, Elementalist | 3 | Yes |
| Normal / elite Spitting Drake | 2 / 3 | Yes |
| Berserker | 1 | No native LODGroup |
| Wind / Sun Demon | 3 / 5 | No native LODGroup |

Eight of 17 actors can therefore be capped to a coarse native body. The eight
demons alone retain 61,216 main-body vertices regardless of this slider. The
current setting restricts original LOD tables; it does not create simpler meshes
or force a different level when distance/quality already selects the coarsest.
The already-coarse explanation is consistent with the source and unchanged
aggregate, but the aggregate uses `Renderer.isVisible` for any camera and cached
mesh metadata. It is not a headset-only current-LOD or GPU submission readback.
No actor-admission failure or source evidence of a native cap override was found.

To make zero detail visibly and materially stronger, generate genuine coarse
derivatives, starting with Wind/Sun Demon and Berserker and then below Drake LOD2.
Preserve rig, bind poses, weights, UVs, material boundaries, silhouette, collision
and native action/death callbacks. Prepare/cache during loading and restore
original meshes at 100. Polygon reduction alone does not reduce renderer count.

## Remaining measured costs

- **Environment render population:** ordinary loaded summaries have roughly
  1,600–1,900 visible renderer candidates. The SCENE sample at LogOutput.log:2120
  has 1,530 MeshRenderers, 65 SkinnedMeshRenderers and 60 particle renderers;
  665 visible material slots use Amp_Basic_N_MRAO and 639 Amp_Basic_WallFade.
  No static batching is reported. These are any-camera visibility/material
  estimates, not actual draw calls. Structural scenery remains the larger
  population than the 39 visible actor body meshes.
- **WallFade:** ordinary loaded windows spend about 2.2–5.6 ms/frame inside
  WallFade.Late, rising during restored vegetation. Atomic rescan commits create
  additional hitches. LogOutput.log:7587 records a 321.55 ms rescan inside a
  329.77 ms WallFade.Late span; the whole frame takes 1,070.14 ms, of which
  instrumented mod work is 341.82 ms. Do not attribute the entire frame to walls
  or add nested phase timings. The rescan has 25 unsliced commit phases.
- **Scenery ownership checks:** SceneryBudget.Update is normally about 0.18–0.20
  ms, but reaches 4.6 ms in early loaded windows and 8.0 ms during a later window.
  `ScenarioSceneryBudget.cs:1410–1424` scans all records when any local/remote
  map prop is held; actor grabs are separate. Indexing only affected held props
  avoids unrelated hidden-decoration work. This source path is a candidate,
  not proof that every measured peak came from a hold.
- **UI inspection:** CanvasConversion has approximately 1.0–2.1 ms parent spans,
  additional Late work and repeated fit/hit-content walks. HiddenWindowVeil
  scans about 21 panels/frame. Cache unchanged membership/layout and react to
  native dirty/visibility changes; retain live animation, input and captures.
- **Measurement hitches:** enabled SceneProfile walks take 102.9 and 87.0 ms in
  individual census frames (LogOutput.log:2120 and 4716). These occur infrequently;
  they explain isolated diagnostic hitches, not sustained 52.67 ms frame time.

Wall diagnostics also emit 322 LATCH, 54 RELEASED, 27 OWNERSHIP CHURN and 15
LEFTOVER warnings. The drawing predicate in `WallSegmentFade.Mounted.cs:1670–1680`
does not consider `forceRenderingOff`; masked decorative meshes can therefore
be counted as drawing. LATCH instead compares fade/coverage state, not pixels.
These are evidence of diagnostic/state work, not proof that every counted plant
is visible. Tighten effective-visibility reporting before treating these counts
as visual regressions. No GloomhavenVR error-level exception is recorded in this
run. Native online-service DNS failures and one XR invalid-space destruction
near a compensated tracking-origin change do not explain the steady frame cost.

## Immediate existing-setting comparison

Before the next launch, change only the existing key in
`/home/steamos/.local/share/Steam/steamapps/common/Gloomhaven/BepInEx/config/dev.gloomhavenvr.worldui.cfg`:

```ini
[WorldUI]
DesktopMirrorLeftEye = true
```

Every measured GFX sample currently reports **false**. This retains the native
ScenarioCamera and UI Camera desktop drawing in addition to both VR eyes. True
engages the existing draw-skip path while the flat menu capture is hidden. It
keeps Camera.main, projection, original dimensions and native callbacks intact,
zeroing only the unused camera's culling mask during its own render. Fresh Frame
profiles already default true; this saved false value was correctly preserved.
Scenario/UI camera callback spans around 3.15/1.37 ms are not exclusive GPU
timings or promised recoverable savings. Compare the same view, with no holds,
all budgets zero and no menu changes. Confirm DRAW SKIP engagement in the log.

A separate measurement comparison can set `[Perf] SceneProfile = false` and
`SceneCensus = false` in `BepInEx/config/dev.gloomhavenvr.perf.cfg`, retaining
Debug, Enabled, FrameSplit and basic FRAME/STEPS data. That removes full scene
diagnostic walks; it also removes the renderer census, so it is not the same
evidence as the current detailed run.

## Ranked next engineering work and configurable concessions

1. Remove unnecessary duplicate desktop draw, cache/prepare WallFade facts before
   a small atomic publication, skip purely visual wall processing of owned masked
   decoration, target held-prop invalidation and cache unchanged window content.
   These require no visible content reduction and apply on PC as well as Frame.
2. Add optional room/material/fade-compatible static visual chunks. Preserve
   native colliders, picking, reveal and fade ownership; exclude held/animated/
   interactive pieces. Individual culling and memory matter: Unity distinguishes
   [static batching from manually merging entire meshes](https://docs.unity3d.com/2021.3/Documentation/Manual/DrawCallBatching.html).
   A single combined level would defeat fine room visibility and interaction.
3. Add optional simpler environment shading that preserves albedo, cutout, floor
   boundaries, reveal and wall fade. Removing normal/MRAO detail is an explicit
   visual compromise; GPU savings still need measurement.
4. Extend a separate ambient environment-effects budget to positively identified
   insects, shafts and decorative smoke. The 197 active particle objects include
   only 61 playing systems; do not assume every AlwaysSimulate entry is costly.
   Preserve combat/condition effects and systems whose native IsAlive gates flow.
5. Build the genuine coarse actor meshes described above. Optional per-renderer
   two-bone skinning can further trade deformation quality for work, but retain
   four-bone player hands. Never disable native actor/Animator controllers as an
   idle shortcut; they coordinate gameplay and action completion.

Implement concessions as reversible settings on all platforms; only fresh Frame
defaults differ. Preserve saved choices and multiplayer card/window presentation.
Source audit and native asset evidence were checked in parallel. No runtime code,
assets, defaults or build number changed, and no redundant full test run was made.
