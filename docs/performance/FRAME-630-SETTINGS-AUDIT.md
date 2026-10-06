# Frame 630: settings and settled-room evidence audit

This is a read-only audit of the Frame test supplied on 2026-10-06. It complements
`FRAME-630-LOADING.md`. It does not claim a Build 630 hardware result, a measured
multiplayer improvement, or an isolated settings/FPS comparison.

## Evidence identity

The immutable inputs are in the integration checkout:
`.planning/debug/frame630-review-20261006/inputs/{LogOutput,Player}.log`.
Both banners identify ModBuild **628**, commit **176beb741**, built
2026-10-06 13:49:17 UTC (`LogOutput:17,71`; `Player:40,153`).

| Input | SHA-256 |
| --- | --- |
| LogOutput.log | `da9b872b71c09765128bfb4a2a348df5a153b067fd3955e7c323e443f87c365c` |
| Player.log | `70a447146c276929c98b396a7a85e02ffa23147eca2efb01d5f235cbd05031df` |

Line references below count LF-separated lines in these files; strip a trailing
CR, but do not use `splitlines()` to renumber embedded CRs. The two files overlap
and must not be added as independent samples. Actual perf records must be matched
at the start of the log row: diagnostic prose also mentions FRAME/STEPS/COUNTS.

The adapter is **Turnip Adreno (TM) 750**, Direct3D11, Unity 2021.3.5f1
(`LogOutput:24`), with **SteamVR/OpenXR**, stereo **MultiPass**, an initial
**3408 x 3408 per-eye** request, resolution scale 1.00 and viewport scale 1.00
(`LogOutput:179,350`). Frame defaults were selected for unset configuration keys;
saved values retain precedence (`LogOutput:78`). This is Frame evidence, not the
previous Gaming PC run.

The session is explicitly **offline**, with `FFSNetwork.IsOnline=False`,
`IsHost=False`, `IsClient=False`, registry count 0 (`LogOutput:118`). The reveal
command independently reports multiplayer inactive (`LogOutput:7681`). No paired
remote log or screenshot was supplied in these immutable inputs. This capture
cannot establish multiplayer cost or visual parity.

## Which controls were actually changed?

The logs cannot reconstruct every clicked graphics row. Build 628's generic
`VROptionsTab.Apply` writes the configuration and refreshes labels but journals
only the wall-cadence controls. Most other options have neither a write record
nor a latched before/after settings snapshot. Distinguish observed values and
completed rendering work from proof of a user edit.

| Control or event | Exact evidence | Conclusion |
| --- | --- | --- |
| `[Optimize] ScenarioCheapWallShading` | `LogOutput:6230` cheap=False; `6241` True; `6296` False; `6560` True, always detail=0%, distantDetail=0%, surfaces=299 | The live boolean changed repeatedly; `ScenarioTerrainBudget` reads this exact config entry for the reported value. The counter windows straddle the changes, so there is no isolated FPS A/B. |
| `[RenderQuality] EyeResolutionScale` | `Player:17278` 1.00 -> 0.80, target before 3408²; `17289` 0.80 -> 0.85, target before 2728²; corresponding assertions at `17279,17290` | Two live requests occurred. The intermediate allocation really shrank to 2728². There is no completed performance window at either lower setting before the crash. Allocation safety is reviewed separately. |
| Native quality initialization | `Player:1953` `SetQualityLeve(Fantastic) called`; `LogOutput:7181` quality level 5 Fantastic | A native callback occurred during initialization. It does not prove a later user preset change. |
| VR graphics profile action | No `Graphics profile applied:` record in either file | No evidence that the mod's profile action ran. The Frame defaults selection banner is not that action. |
| Wall evaluation/rescan cadence rows | No `WALL CADENCE EDIT` record in either file; effective evaluation cadence 0.250s reported in the wall budget | The effective cadence is known; a user edit during this run is not evidenced. |
| Visible-idle sampling | `LogOutput:6987` 350 bakes/700 samples; `7353` 42/84; `7623` all idle submission counters zero | The pathway ceased submitting in the last completed window. Without its latched interval and eligibility state, this does not identify which control or gameplay condition caused it. Its removal follows the user's visual rejection, not a demonstrated FPS benefit. |
| Figure detail / cloth / ambient FX | Reports remain players=0%, enemies=0%, nativeCloth=False, optional FX=0% (`LogOutput:3700,7860`) | These budgets operated. No change in their stored values is proven by these reports. |
| Bone weights, panel/actor-bar cadence, distance LOD, NPC body detail | End snapshots consistently distanceLOD=True, npcBody=original100%, boneLimit=2, idleBarInterval=0.100s, panelInterval=0.050s (`LogOutput:7626` and earlier windows) | End-of-window observations only; the report explicitly says it is not a latched A/B boundary. |

Figure measurement revisions are also insufficient evidence of a slider edit:
`PerfFigureMeasurement.Observe` starts a revision on settings changes **or** when
a previously steady budget loses readiness. Opening UI or structural churn can
therefore change a revision while its printed settings remain equal. Similarly,
the observed display-rate MARKs are runtime readings; no recorded user control
write identifies them as user refresh-rate selections.

The native Fantastic quality state retains shadows=All, one cascade, distance
150, high shadow resolution, real-time reflection probes enabled, full-resolution
textures and forced anisotropic filtering (`LogOutput:7181`). The same sample
also reports pixelLights=0, MSAA=0, softParticles=False, depthTextureMode=None,
Forward rendering, HDR disabled after the menu transition, and streaming disabled.
Native shadow/probe settings are inventory observations, not measured GPU work.
Turning off MSAA/depth/optional scenery again is not an unused performance lever:
those controls were already at their cheapest recorded state.

## What work the settings actually affected

The last completed perf window precedes the all-rooms reveal:
`LogOutput:7620–7626`, 20.1s, **257 frames**, mean **78.43ms**, p95 **116.59ms**,
with **41.65ms/frame** of named mod work. Its Terrain.PreCull is
**17.317ms/frame, 514 calls**; Environment.PreCull is **3.010ms/frame,
1029 calls**. Two eye passes and additional UI capture cameras multiply callback
work. Do not interpret the per-frame scope value as the cost of one callback.

The completed-camera work counters at `LogOutput:7623` prove these paths delivered:

| Work | Delivered count in that window | What this proves / does not prove |
| --- | --- | --- |
| Environment chunk replacement | 38,950 source submissions represented by 11,275 groups; worst frame 190 sources/55 groups | Groups completed their camera leases. These are logical source/group counts, not captured GPU draw calls or a measured saved time. They are **single-room** counts. |
| Explicit environment instancing | InstanceSources=0, InstanceGroups=0 | No explicit instancing contribution was delivered in this window. An enabled option alone would not prove eligibility or a benefit. |
| Terrain derivatives | 8,061,952 original vs 2,955,264 submitted admitted triangles | Approximately **63.3% fewer triangles in this admitted terrain subset**. This is not a scene-wide triangle reduction; neither GPU savings nor faster frame time follows numerically from this counter. |
| Cheap terrain shading | 39,424 cheap-surface submissions, worst frame 154 | Cheap materials were submitted by completed leases. The False/True experiment changed the pathway, but mixed windows prevent a per-setting FPS attribution. |
| Shared UI registry | 3,840 reuse-panel events, worst frame 15; shared scans 256, shared members 11,776; independent scans/members=0 | The common registry served the panels. There is no matched On/Off frame-time experiment. |
| Multiplayer native-source reuse | NativeSourceReads=0, NativeSourceReadsReused=0 | Expected for this offline session; these logs cannot assess the multiplayer optimization. |
| Visible-idle presentation | All visible-idle bake/sample/source/cloth-approximation counts=0 | No delivered work from the removed feature in this window. |

Figure budget reports are additional evidence of actual reductions:
`LogOutput:3700` has 10 actors, 21 cloth solvers disabled, 48 optional renderer
masks and 40 particle solvers paused; 23 verified body derivatives reduce admitted
body vertices 147,208 -> 89,376. After the reveal, `LogOutput:7860` has 16 actors,
27 disabled cloth solvers, 112 masks, 88 paused particle solvers and 37 verified
derivatives reducing admitted vertices 202,930 -> 72,311. Only 5/16 actors have
authored coarse bodies, but generated derivatives cover additional admitted
meshes. This is not evidence that the other eleven actors remain wholly native.
Actual last-frame visible body meshes/vertices are separately 84/59,133; the
different populations must not be conflated.

## Native reveal completion and the missing steady summary

The command at `LogOutput:7681` measures **three rooms and three doors**:
two previously hidden rooms become revealed, and all three doors become open.
Room preparation begins at `Player:16175`; the indicator appears at
`LogOutput:7706`. Later exact preparation records distinguish native work from
optional caches:

| Player row | Elapsed preparation time | Native room work |
| --- | --- | --- |
| 16331 | 13.9s | 2 rooms pending; generator/material children not yet observed. This is not proof of readiness. |
| 16551 | 24.0s | 25 generation owners, 2,013 material requests; 1 generation and 39 material handles pending, 4 completed failed handles. |
| 16711 | 34.0s | 1 generation pending; 0 pending material handles, 4 completed failed handles. |
| 16750 | after the previous pending record | Spinner hidden after its fade. |
| 16892 / 17046 / 17155 / 17259 | 44.1 / 54.2 / 64.3 / 74.4s | roomReveal=False, rooms=0, native generation/material counts=0; optional figures=0/68 and masks=True remain. |

The last records do **not** establish a still-running native room load. Their
remaining budget preparation is not a reason to extend the spinner. The separate
initial-load audit proves a 61.81s optional-cache overhang after native completion;
the loading fix removes that hold without changing native completion flags.

There is no completed FRAME/STEPS/SPLIT/COUNTS summary for the settled three-room
state. This is an instrumentation blind spot, not a report cap: Build 628
`PerfMonitor.MarkChange` requires **at least 120 frames** before summarizing a
closing window. Runtime display-rate changes repeatedly close slow windows
before that threshold (`LogOutput:7778`: 20.8s/80 frames; `8118`: 5.6s/36 frames;
`8614`: 10.2s/60 frames). The integrator's Build 630 change retains sufficiently
long slow windows with fewer frames. It does not retrospectively reconstruct
missing measurements from this capture.

## Settled three-room costs that remain directly evidenced

There are **64 rate-limited spike reports** after spinner release at Player row
16750, covering reported frames 7935–8168. Terrain.PreCull appears in 63, at
**33.11–74.36ms**; Environment.PreCull in 50, at **4.90–11.27ms**. WallFade.Late
appears in 55, at **2.91–199.52ms**. These are selected slow frames, not a random
sample, window averages, or an isolated FPS comparison. Native inclusive timings
from the preceding frame also do not fill all unattributed engine/GPU work.

Representative settled rows:

| Player row / frame | Evidence |
| --- | --- |
| 16753 / 7935 | Frame143.99ms, mod109.06ms, Terrain69.19ms. |
| 16786 / 7954 | Frame145.47ms, Terrain72.59ms; WallLate18.45ms contains FastReclaim12.65ms, which contains FastSweep5.92ms. |
| 16906 / 7974 | Frame393.11ms; WallLate199.52ms contains Pipeline184.47ms / Rescan182.47ms / commit Figures44.96ms. This is a post-load periodic rebuild. |
| 17041 / 8031 | Frame345.07ms; Hands179.22ms contains UiClick177.94ms / TabRoot177.18ms. This is an options UI rebuild, not the continuous idle cost. |
| 17186 / 8115 | Frame290.32ms; WallLate154.79ms contains Pipeline133.67ms / Rescan132.12ms, with Terrain71.70ms. |
| 17277 / 8168 | Frame160.68ms, Terrain72.24ms and Environment10.16ms immediately before the resolution edit. |

The wall subsystem also supplies independent continuous tick windows:

| LogOutput row | Ticks | WallLate ms/tick | Pipeline ms on ticks it ran | Decide+apply ms/tick | FastReclaim ms/tick |
| --- | --- | --- | --- | --- | --- |
| 8433 | 31 | 9.956 | 1.742 | 2.756 | 4.753 |
| 8481 | 33 | 9.703 | 1.708 | 2.613 | 4.395 |
| 8520 | 33 | 9.825 | 1.765 | 2.868 | 4.347 |
| 8622 | 30 | 10.387 | 2.682 (22 timed ticks) | 2.467 | 4.075 |

These phases are contained in WallLate; their denominators also differ. The
sampled ApplyWall pathway walks **532 wall renderers/tick**, mounted dressing
87/tick and stacked pieces 7/tick. The sampled applier times cannot be added to
their Decide parent. A final rescan budget (`LogOutput:8612`) skips both commits
in that window, yet the continuous Late cost remains: optimizing only commits
will not remove the reclaim and apply paths.

Two additional source-level instrument caveats matter:

- `LogTickBudget` excludes every enum value `>= TickApplierFirst` from its named
  subtraction. That accidentally excludes **Corners, FadeCensus, FloorAudit,
  FastReclaim and PathAudit** as well as the nested appliers. The printed
  UNATTRIBUTED 166.1ms at row8433 includes named FastReclaim work; it is not proof
  of an additional exclusive 166.1ms mystery.
- The Decide phase brackets the every-frame decision **and apply** loop;
  SplitRuns also brackets a conditionally executed body. Their timed-frame counts
  equal to tick counts do not prove the expensive decision gate opened every
  frame. The effective 0.250s cadence is valid; the log's interpretation of those
  frame counts is too strong. Continuous fades must keep their frame cadence.

The integrator's Build 630 instrumentation correction excludes only
ApplyFoliage..ApplyWall from the residual, retains the later top-level phases and
corrects the cadence explanation. It changes measurement attribution, not native
ownership or fade behavior.

The FastReclaim source proves the remaining full-scene poll:
`WallSegmentFade.Stacked.cs` performs `FindObjectsOfType<MeshRenderer>()` at a
0.25s cadence while faded stack-bearing candidates exist. It rebuilds ownership
predicates so newly regenerated native pieces cannot remain visible inside a
faded wall. It is therefore unsafe simply to cache the scene forever or to
remove this sweep without replacing the event coverage.

## Prioritized follow-on options after the current Terrain read deduplication

The current Build 630 Terrain fix reuses component and derivative-bank reads
**inside one invocation**, rejects inactive renderers before full validation, and
retains live ownership/native-state checks. Its source/runtime checks establish
the causal read reduction; a new Frame run must establish the actual frame gain.
The settled Terrain evidence makes it the highest-priority first intervention.

1. **Replace quarter-second whole-scene reclaim with generation-local work.**
   Add a renderer-change feed at the proven native procedural/material/room
   completion boundaries and evaluate only changed subtrees for fast reclaim.
   Retain a bounded fallback census for unobserved native writers, exact figure,
   floor, water, arch, prop-unit and ownership guards, and restore leases on
   teardown. First measure the feed's coverage and fallback rate. A configurable
   conservative polling mode must remain available; merely raising the interval
   trades visible regenerated-wall flashes for speed and is a separate visual
   compromise requiring an explicit option.
2. **Reduce settled wall material writes without stepping the animation.**
   Skip the actual native property-block write only when the desired final block
   equals the last successfully owned write and native mutation checks still
   prove ownership. Continue computing continuous fade values; bypass the skip
   on native regeneration/material replacement, shared-peer updates, walk-in
   entry and lease restoration. A per-renderer generation/write receipt is
   required: unchecked cross-frame caching would repeat previous missing-room
   defects. Instrument writes attempted/skipped/reasserted and verify local plus
   remote fade trajectories. The current decision-cadence knob alone does not
   eliminate the 532-renderer apply walk.
3. **Offer a broader cheap opaque shading tier with the original 3D geometry.**
   The delivered cheap-material counter covers an admitted subset, not every
   visible room surface. Survey the remaining shader/material families, then
   extend verified simple albedo/diffuse variants to eligible expensive scenery
   while preserving alpha, wall dissolve, material swaps and water exclusions.
   Every stronger tier remains configurable and reversible on the shared PC/
   Frame build. Keep actual door/floor/room visibility and continuous figures.
   Fewer shader features may help GPU time, but the present logs do not measure
   shader-busy time or guarantee a large gain after the CPU fix.
4. **Offer stronger verified geometry budgets for eligible props and bodies.**
   Existing derivatives already reduce admitted geometry substantially; do not
   claim that their counters mean all room geometry is simplified. Audit remaining
   eligibility/refusal coverage and add a lower configurable derivative budget
   only where silhouettes, required submeshes, animated bodies and native
   transitions remain valid. Report original/admitted/submitted populations
   separately. Do not hide whole open rooms or replace the board with 2.5D.
5. **Evaluate stereo submission architecture as a separate major project.**
   MultiPass duplicates head-camera processing. A supported single-pass path
   could reduce that work, but this capture does not establish provider support
   or compatibility with the game's procedural shaders, wall variants, UI capture
   and per-eye callbacks. A prototype needs both-eye screenshot validation and
   exact submission/restore tests before it can become an optional render mode.
   It is not a safe toggle to enable blindly in this build.

Next-run telemetry should journal actual changed settings at bounded Debug level
and latch a complete settings/readiness snapshot at each measurement boundary.
Keep useful ordinary lifecycle/failure logs. Test unchanged pose and rooms for
long enough after native completion, then change one control per window. Use a
separate paired multiplayer run for its added cost. This does not require hiding
gameplay with an extra loading period, throttling shared visual transitions, or
reintroducing the rejected idle-animation sampling.

## Validation scope

This follow-up changes this document only. Evidence extraction, callback source
inspection and `git diff --check` validate the audit; no broad build/test gate is
rerun for prose. The earlier loading implementation and its focused production
runtime evidence are committed separately. The integrator owns Build 630's final
combined gate, build inventory, hardware limitations and integration/push.
