# Build 600: reversible scenario scenery budget

## Authorization and scope

On 2026-10-01 the maintainer explicitly authorized visual compromises needed to
make large scenarios playable on Steam Frame standalone. The native flat camera
normally sees a bounded portion of the map, while a VR overview exposes thousands
of renderers. This authorization applies to explicit scenery quality trades;
it does not waive original-card, control-board, shared-window or remote-animation
parity. Ordinary PC VR retains full scenery by default.

The first trade is decorative **floor grass density**, not a blanket renderer,
layer, distance or Foliage-shader switch. The native Foliage shader also occurs
on game obstacles and wall-mounted dressing. Build 599's held ThreeHexObstacle
contains two grass meshes, so mesh/shader names alone cannot identify scenery
that is safe to omit. Cave crystals, trees, bushes and arbitrary prop composites
remain outside this first implementation.

`dev.gloomhavenvr.perf.cfg`, `[Optimize] ScenarioSceneryDensityPercent`:

- PC and streamed PC VR default: **100** (original rendering).
- Marked standalone Frame installation default: **25** (stable decorative subset).
- Range: **0–100**, with 5% steps in **VR Options → Graphics**.
- Already saved values are retained by BepInEx. A newly introduced key adopts
  its platform default; switching to 100 restores this budget's rendering masks.

Eligibility is tied to the actual procedural floor-grass hierarchy, native
component/collider exclusions and mesh/material provenance. It changes only
`Renderer.forceRenderingOff`; native objects, enabled flags, colliders, gameplay,
reveal state, material ownership and wall-fade property blocks remain untouched.
Placement/reveal hooks queue bounded discovery. Scene exit, teardown and 100%
restore only masks this driver actually owns. Original local/remote cards,
NPC stations, map UI, health bars and scenario game obstacles are outside its
candidate population.

## Lossless work removal shipped alongside the trade

The follow-up 2026-10-01 ruling requires every Frame optimization to be a setting
that PC users can reproduce and Frame users can disable. Frame only selects unset
defaults. The previous hard Frame desktop-mirror override and row hiding have
therefore been removed; `[WorldUI] DesktopMirrorLeftEye` is a live choice on both.
The normal PC default is unchanged, and saved Frame off choices are now respected.

- WallCache reads shared-material shader/toggle/cutoff facts once per material
  within its synchronous commit phase. The nearest figure ancestor uses the
  existing pass-scoped memo lifetime. Each renderer's material list remains live.
  `[Optimize] SharedWallReadCache=false` restores the original repeated queries.
  See [wall evidence](perf/WALL-READ-FACTS-600.md).
- LightStabiliser avoids an unused global LightFlicker diagnostic lookup when
  the scene-light census would not be emitted. It skips exact unchanged Unity
  intensity/position setters, preserving small real movements.
  `[Optimize] LightStabiliserWorkCache=false` restores the original lookup/write
  cadence. Both work-cache settings default on, are available in advanced VR
  options/config on all platforms, and retain identical appearance when off.
- The Debug scene census defers incomplete loading, then bypasses an older
  scene's cooldown once. It records actual scene provenance and rendering masks.
  Six additional selected game/third-party callback timings cover source-backed
  singleton candidates; they do not disable or delay gameplay callbacks.
  The old `blocked`/`BLOCKED`/`ZOOM` log markers remain stable, but residual time
  no longer asserts GPU wait or CPU idleness. The report parser accepts both
  generations of logs.

These work-removal changes apply to all platforms. The visible quality default
is specific to standalone Frame, and the same user control is available on PC.
No wire-format or asset-bundle change is required.

## Evidence and hardware acceptance

Build 599's complete loaded-scenario windows averaged 82.85 and 117.64 ms/frame
at different views; they are not an A/B. Its nine selected game callbacks sum to
about 1.13 ms in the last sample and do not explain the remaining logic span.
Its scenario census was suppressed by a menu cooldown. Actual Unity draw-call
and GPU busy-time counters are unavailable in that player.

For the next test use the same large loaded scenario at 3408 pixels/eye, hold
the same tracked head/board pose, then compare **25%, 0%, and 100%** for at least
one full performance window each. Exclude loading and settings-window time.
`Scenario scenery budget` identifies how many verified candidates exist and how
many it actually masks; zero candidates means no rendering saving is established
for that scenery style. Compare FRAME/SPLIT and supported SteamVR CPU/GPU timings,
not shader-slot counts labelled as draw calls. Check door/room reveals and
normal/remote card interaction after changing the quality value.

Source-linked tests establish ownership, lifecycle and removed repeated reads.
They do not establish headset appearance, actual milliseconds saved or a playable
frame rate. Larger scenery/submission compromises still require the new loaded
population and hardware results; the full optimization objective remains open.

## Final local validation

- Final integrated source gates: **14/14 passed**.
- Complete local gate: **86/86 suites passed**, 563.4 seconds with eight workers;
  evidence: `.planning/debug/test-runs/20261001-183855-790ff5b9/results.json`.
- Wire/golden vectors: **286,620 assertions passed**; all three committed bundles
  passed their format checks. There is no packet-format or bundle-content change.
- Release build: **zero warnings and errors**.
- Compiled-form review against `fc89ba757`: 24 changed types and six additions,
  matching the new budget, census gates, cache switches, options and diagnostic
  changes. Otherwise untouched network types differ only by the propagated
  Build 600 constant. The guard returns its expected nonzero fingerprint verdict
  for intentional behavioral changes after all required checks pass.
- Config/patch/log surface: 634 keys, 196 patch targets and 4,771 log tokens;
  **nothing removed**. English/German player-doc checks passed.

These checks establish code and protocol behavior, not actual Unity draw counts,
headset rendering quality or Frame FPS improvement.
