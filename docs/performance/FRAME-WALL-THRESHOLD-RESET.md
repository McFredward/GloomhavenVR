# Live Auto wall threshold repair

The maintainer requested a 30 FPS maximum instead of 90, and a fresh Auto
experiment whenever its threshold changes. Previously the threshold was read
only while Auto was unlatched: a latched scenario remained hidden regardless of
later edits. The resulting slider change could not show what the new threshold
would do with walls visible.

The original `Optimize/WallAutoHideBelowFpsCount` key, integer stepping, minimum
5, default 15 and wall-mode indexes remain. BepInEx's acceptable range and the
separate effective getter now both cap at 30. Previously stored higher values
are bounded by the same binding/getter; no replacement key or profile is added.
English/German config descriptions and the actual curated player help explain
restoration and a fresh measurement.

## Policy and cost

The existing policy caches the effective Auto threshold. A changed effective
value while Auto is selected follows the existing visibility reset, including
renderer-mask ownership restitution, current material/terrain consumer hooks,
cleared latch/clock, renewed half-second loading grace, and a new two-second
observation window. If hidden walls belonged to the same live scenario, the
existing native sender keys are rebuilt exactly once before ordinary fading
resumes. This is the same recovery used for Hide all -> unlatched Auto or
Regular; it does not replace the wall inventory or network key algorithm.

Changing an inactive threshold in manual Hide all does not expose walls, restore
consumers or rebuild sender keys. Rewriting the same effective value, including
equivalent out-of-range values, likewise performs no recovery. The cached scalar
survives the reset itself so a stable value cannot cause repeated resets.
Genuine loading/reveal/focus eligibility, hitch cap, exact wall membership,
protected door frames/arches, and per-scenario latch remain unchanged.

Settled frames add only a managed scalar comparison. The threshold getter is
read once in Auto, including while latched, and not polled in other modes. No
new renderer scan, native query, per-eye work, timer or diagnostic formatting is
introduced. Real edits necessarily pay one bounded restitution/recovery, then
restore the ordinary wall workload during the requested new experiment. No
claim is made that visible walls cost the same as hidden walls.

Existing normal-level `PERFORMANCE WALLS AUTO`, `HIDDEN` and `RESTORED` tokens
remain. Auto's explanation includes threshold edits, and a real restoration
reports `automatic threshold changed`. No frame-by-frame report is added.

## Focused evidence

Production checkpoints are `6432cfcd0` (range/reset) and `3c03d51ed` (precise
bilingual help), based on current dev `041c42f3d`. The integration owner controls
the next build number and final integrated checks.

The production Unity 2021.3.5f1 wall policy case passes **730 assertions**, up
from 695 with all existing assertions retained. It executes the complete
current production Performance partial and extracted native wall entry,
wire-key recovery, ClearAllBlocks, diagnostics, protection primitives, plus the
actual production effective FPS getter. New cases cover raised/lowered edits
while latched, raised edits while observing, exact visible renderer/attachment
pixels and current MPBs after restoration, retired fade state, one-time key
recovery and stable cross-machine sender keys, fresh grace/full window,
re-latching only after sustained poor FPS, new sufficiently fast gameplay,
30-FPS clamping, same/equivalent values, manual Hide all, mode recovery and
native loading after an edit.

All **27 causal controls** are detected: 22 retained and five added for missing
threshold reset, missing threshold cache, missing new grace, missing threshold
wire recovery and a lost effective maximum. Each compiles before running and
fails its specified semantic assertion. The original wire recovery cases run
before the new threshold cases so their existing controls still reach their
original assertions. A first run caught this fixture-order issue; that failed
receipt is retained and does not qualify as a pass.

The options case uses actual BepInEx and original config/catalog/dropdown paths:
**77 assertions**, 75 retained plus two explicit unbounded-storage checks of the
production effective getter. All **16 variants** pass: the previous 13 controls
and production, plus two range/getter-maximum controls. BepInEx already clamps
bound edits, so the separate getter control required explicit unbounded config
storage to avoid passing accidentally; the first unqualified control attempt
is retained. Both PC/Frame defaults and saved manual choices survive rebinding.

The native policy fixture uses real Unity scene handles, hierarchy, renderer
flags, bounds, meshes, MPBs and camera pixels. Original game controllers/config
storage, deterministic frame clock/focus, collector and broad classifiers remain
explicit boundaries. The material/terrain restoration integration calls are
source-proven; this fixture does not execute every actual consumer or a connected
multiplayer session. It does not measure Steam Frame FPS or establish headset
appearance. Next-headset threshold-reset acceptance remains pending.

## Receipts

All paths below are private to the worker checkout until the integrator archives
its evidence:

- `.planning/debug/frame-wall-reset/all-controls-final/run-m2t96k53/`: production
  730, all 27 controls, captured source hashes and unchanged-source receipt.
- `.planning/debug/frame-wall-reset/options-final-help/`: final bilingual-help
  source, 77 assertions and all 16 variants.
- `.planning/debug/frame-wall-reset/source-final/results.json`: all 16 source
  checks on final source.
- `.planning/debug/frame-wall-reset/build-debug-final.log` and
  `build-release-final.log`: strict builds, zero errors and zero warnings.
- `git diff --check`: pass.

These are focused worker checks, not a fresh complete local gate or golden-wire
run. The integration owner records the final composed tree and inherited
unchanged evidence separately.
