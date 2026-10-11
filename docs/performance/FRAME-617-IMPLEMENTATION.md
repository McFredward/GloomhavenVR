# Frame 617: scenario preparation and hitch evidence

Build616 is the hardware baseline: 49.35 ms mean over six loaded pre-options
windows, with ordinary fan construction at 172.37 ms, pickup at 109.10 ms
(including a nested 55.89 ms ghost) and stat steps at 65.93–98.54 ms. This build
has source/runtime evidence; it does not yet have a headset performance result.
Loading and deliberate slider preparation remain accepted costs, not targets.

## Original interaction resources

`ScenarioInteractionPreparation` runs once per native Game/ProcGen loading epoch
on every VR platform. After native loading establishes the original roster,
it alternates one card and one figure/stat preparation job per frame. The existing
loading indicator remains visible. Native loading flags, callbacks, continuation
and input are not changed or blocked. 2D map, original non-NPC windows and map
preparation retain their existing paths.

[Card preparation](FRAME-617-CARDS.md) reuses the original asset pins and shared
mip caches for all real party classes, original state/element/condition art,
and at most 64 inert ordinary backings. No native hand/widget is fabricated or
selected. Local and remote consumers keep the same originals and face permissions.

[Figure preparation](FRAME-617-FIGURE-PREPARATION.md) constructs inert original
ghosts once, validates current source identities and rebinds current evaluated
poses/masks before either local or remote pickup. A sleeping dragon cannot reuse a
stale flying pose. Already-held actors are skipped; native actions retain immediate
return from the hand. Stat preparation warms existing authored shared art only.
Unseen async portraits, native conversion and newly introduced content retain
immediate lazy fallback: the complete stat-panel hitch is not proven eliminated.

Jobs fail open. Prerequisites/addressables have their existing finite waits;
the coordinator cancels unfinished work after 90 seconds, with one useful normal
warning. Cancellation retains live holds, valid cached originals and reserved
backings. Actual scene/load/VR teardown resets them. Ordinary completion and
fine-grained resource traces are Debug; routine normal-level logging is not expanded.

## Bounded hitch evidence

The scene inventory still respects its configured millisecond and work-item caps.
Small already-borrowed component/child groups are processed together; only ancestor
chains proven complete to a root can terminate repeated walks. Every visited node
still yields, inactive descendants retain the original Animator predicate, and
real scene/config/debug changes invalidate the inventory. Display-refresh-only
changes retain the existing separate job. A completed census remains a capture
span, not one instantaneous picture.

Debug SPIKE reports now retain a fixed-size per-frame ledger of the existing
selected native callbacks after the summary's first 120 frames. GC collection
deltas belong to the same observed boundary; they do not measure pause duration
or prove a leak. Hook installation/calibration finishes before real-frame capture:
its synthetic calls cannot appear as native gameplay calls. Five optional
main-thread engine markers use actual Unity
recorders with finite capacity and validated time units. Each boundary resets
and **resumes** the recorder: Unity 2021 Reset stops it. A real-frame fixture
checks sporadic positive samples and fresh absent-frame zero sums; stale positive
samples cannot survive into the next absent frame. Stripped/unavailable release
markers stay n/a. Inclusive/nested values must not be added, and waits are not GPU
busy time. All formatting uses the existing capped SPIKE emitter. Normal logging
does not collect these ledgers or engine samples.

## Walls: preserved safety, new actual-delivery evidence

[The wall implementation](FRAME-617-WALL-IMPLEMENTATION.md) observes original
renderer and indexed MPBs, material/keyword gates and enable state at the actual
head-camera pre-render event. It performs no rendering/readback or native write.
Existing ceiling commit phase timers now expose their top four costs in a bounded
Debug report.

The current fast hashes do not close every native classifier, mutable material,
map/tile membership and restoration-ledger input. Therefore the safety ceiling
remains. The recurring forced publication is **not removed**, and the hardware
wall-pop report is **not declared fixed** by numeric/native delivery assertions.
The next capture must identify any overwrite or shader-route disagreement and
which native collector phases account for the approximately 141 ms ceiling burst.

## Validation and next capture

Focused runtime evidence includes original GPU card pixels, actual sleeping/flying
native rig states, current quality/material masks, local/remote shared acquisition,
held-input cancellation and real camera callback delivery. The complete integrated
coverage, strict build and compiled comparison are recorded in STATE and under
`.planning/debug/frame617-implementation/`; successful focused checks alone are not
full-tree proof.

The integrated full local attempt recorded all 120 suites; 118 passed directly.
Two bounded fixture-only resumes cover the public-cabinet timing/readiness and
inactive prepared-surface negative-control failures. Original failures remain,
and their bound production hashes match the resumed sources. The final native
calibration correction passed 1,427 production assertions, 13 actual engine-frame
assertions and three relevant causal controls; unrelated passing suites were retained.
All 14 source gates and 307,473 wire/golden assertions are covered, together with
the strict zero-warning Release build, unchanged bundle/index, preserved public
surfaces and five bilingual document pairs. The wire wrapper's accidentally nested
local scheduler was canceled; the primary full local coverage and the same built
wire binary's golden stage complete the ledger without repeating the full run.
The wrapper's interrupted exit is not a passing gate receipt. Immutable compiled
616 comparison shows 14 intended behavior changes, eight build-constant consumers
and four added types; references/resources match. Details and the complete
coverage reconciliation are in `coverage-ledger.json` beside the original logs.

Use the ordinary Debug test profile. Test the first fan/class switch and first
local/remote figure pickup after the indicator disappears, then ordinary play and
wall fades. No precise gaze alignment is required; interpret the available spans
and counters with their real confounds. Exclude loading, deliberate slider
preparation and doffed-headset tail stalls. Compare post-preparation hitch counts,
cache-hit/fallback receipts and completed inventories before assigning savings.
