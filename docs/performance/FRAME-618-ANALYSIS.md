# Build617 Steam Frame hardware: preparation works, ordinary hitches remain

This document reviews the supplied **Build617 / e1da52eaa** single-player capture.
It does not describe a shipped Build618. This follow-up changes developer notes
only; the validated617 binary, bundles, wire and configuration defaults stay
unchanged. There are three new log files and no new screenshots or videos in
this evidence set. Immutable copies, hashes and reproducible analysis scripts
are in `.planning/debug/frame618-review/` in the main checkout.

## Result after loading

Both Player.log and LogOutput.log identify the same build and report identical
metrics for all 22 measurement windows. They are two sinks from one run, not two
independent benchmarks. The final OpenXR opening matches this run; earlier
append-only sessions are not pooled with it. The eye target is 3408×3408 per eye,
MultiPass, D3D11 on Turnip/Adreno750. Loading, the accepted initial preparation,
mixed windows and the lost-user-presence tail are excluded from ordinary-play
comparisons. Natural VR head motion and actions remain valid evidence; this
analysis does not require the maintainer to maintain a fixed gaze.

| Ordinary-play measurement | Result |
|---|---|
| Build617, all 12 fully loaded/worn windows | 4687 frames / 240.4 s, **51.37 ms/frame** |
| Build617, 11 windows before the actor revision changes | 4496 frames / 230.4 s, **51.33 ms/frame** |
| Prior Build616 comparison interval | **49.35 ms/frame** |
| Build617 window p95 range | **83.97–99.16 ms**; individual window percentiles, not a pooled percentile |
| Worst frame in these completed windows | **382.03 ms** |
| Frame split, all 12 loaded windows | Main-thread logic 30.78 ms, render submission 6.95 ms, residual 13.65 ms |

There is **no demonstrated overall pacing improvement** in this run. The observed
mean is about 4% higher than the earlier616 interval. Different poses, actions,
actor revisions and runtime pacing prevent attributing that difference to the617
code alone. Including later ordinary summon/burn gameplay changes the mean from
51.33 to 51.37 ms, so the conclusion does not depend on dropping that gameplay.
The final summary crosses user-presence loss and is retained separately, rather
than classified as wholly worn play. The 432 ms spike afterwards is not an active-
gameplay maximum. The native and mod timers are inclusive and overlap; neither
their sum nor subtraction from the frame split gives an exact remaining CPU cost.

The new census finally completes: 4415 enabled renderers, 1539 enabled candidates
in the VR mask, 10481 active / 9630 enabled behaviours. These are inventory counts,
not measured draw calls. The sampled camera ledger contains HeadCamera with two
MultiPass visits per frame, without a second native scene camera. GFX reports
`desktopMirror=True`: `[WorldUI] DesktopMirrorLeftEye=False` would remove the
spectator blit on this saved profile, but native flat cameras are already
suppressed independently. This is a minor configuration opportunity, not proof
of an extra full-scene render or a measured GPU saving.

Full pacing, applied actor budgets, allocation and attribution evidence:
[FRAME-618-PERF-AUDIT.md](FRAME-618-PERF-AUDIT.md).

## What preparation actually improved

Preparation completes under the spinner in 38.82 s: four original class skins,
271/271 **collected** sprites visited, 29/29 inert backing reservations, no reported
failure or timeout. Three subsequent SunDemon/SpittingDrake ghost acquisitions
are actual prepared-cache hits. All nine fan-readiness receipts report no pending
face and no readiness wait. These establish working resource preparation and
cache reuse, not zero CPU cost for fan construction or universal native-art closure.

Original widgets still hold unvisited art in private arrays: `ConsumeElement`
owns `elementSprites` and `highlightElementSprites`; `InfuseElement` owns its own
`elementSprites`. ConsumeDark and other element sprites are first baked after
preparation. The current test boundary models configuration icons but not those
actual widget arrays. That is a source-proven coverage gap, rather than a cache
eviction inferred from a grey card. No cache eviction/ceiling warning is logged.

Post-loading work still includes Cards.Driver 118.78 ms on first opening and
122.33 ms on a later rebuild, StatPanelSurface 126.19 ms, new sprite/atlas work,
and cold fallback ghosts for some heroes and a newly summoned actor. Unseen
portraits/native conversion were explicitly outside617's preparation closure.
Hero miss reasons need a bounded original-identity report; validation must stay
intact because distance/held detail policy can legitimately change the source mesh.
See [the cold-resource audit](FRAME-618-COLD-PREPARATION.md).

## The largest concrete recurring target

The wall driver performs **21 commits in 67 judged loaded/worn cycles**: 20 are
scene-signature changes and one is the 30-cycle safety ceiling. These counts exclude
the first mixed report after spinner closure and all unworn tail reports. At least 13 emitted
spikes include a heavy WallFade.Rescan, with **133.23–209.93 ms** inside the rescan.
This is ordinary play, without options retuning, room reveal or board movement.

The row census explains the changes: births/deaths/activation of original
waypoint particles, detached cast/healing FX and summon visuals invalidate the
scene signature. Source establishes that WorldspaceStarHexDisplay publishes
WaypointHolder prefabs under WaypointLine; the existing native-selection visual
predicate covers HexSelect_Control and particle arrays, but not this family.
The actor-only particle exemption is already unconditional. Turning on the
separate FigureExemptSkip option would save none of the commits observed here.

Do not skip particles by name or simply remove the safety ceiling. Native
particle renderers can still enter wall-mounted/free-standing rider collectors;
real wall torches and scenery effects require ownership and restoration. A safe
next change must share an **exact original-owner/reference exclusion** between
the signature and every relevant collector. Test authored wall/scenery particles,
path particles, detached action FX, room reveal and mid-fade carry/restoration.
If dependency closure cannot be proved, retain the conservative commit and
prepare the expensive phases incrementally behind the current complete table.

The ceiling commit's measured phases total 161.59 ms: WallCache 47.66 ms and
Mounted 43.16 ms are 56% of that time. Reducing recurring invalidations has higher
observed leverage than merely raising the periodic check interval. The full
timeline and collector boundaries are in
[FRAME-618-WALL-EVIDENCE.md](FRAME-618-WALL-EVIDENCE.md).

## What the new diagnostics do and do not establish

Native per-frame probes now identify **207.26 ms in Choreographer.Update** on the
382 ms spike, after the old first 120-frame sampling limit. That callback includes
message processing and native presentation, plus any nested Harmony work. Its
source has an 8 ms outer queue-loop budget, but an individual ProcessMessage is
not preempted by that budget. The precise expensive message/substep is not yet
measured. Next attribution should time actual selected native message/substeps,
bounded at Debug, without delaying gameplay, changing continuation or reordering
network messages.

Only two of 171 emitted primary-interval spikes coincide with a GC collection;
169 have none. One wall spike includes 20.48 ms GC.Collect. Allocation is higher
than the earlier run, but neither this nor the heap range proves a memory leak
or makes GC the explanation for most hitches. Several large frames remain
unassigned, including a 364.94 ms worn-gameplay frame with only 16.27 ms of named
mod work and small selected native callbacks. Missing engine markers and an XR
GPU figure that follows frame interval do not establish GPU-busy time. The
existing SPIKE verdict's “NOT the mod” phrase means only low **named-scope**
coverage, not proof that all uninstrumented mod work is innocent.

There are eight native Hydra SDK DNS errors. They do not establish a mod deadlock,
a resource preparation fault or the cause of the measured peaks. There are no
new supplied headset pixels to certify remaining visual issues or multiplayer
timing parity.

Wall delivery now has 72 real head-camera pre-render samples across six episodes.
The sampled HIGH/native wall materials retain the expected block/gates, without
a late overwrite, force-off or indexed-block override. This establishes delivery
for those inputs, **not smooth pixels**. Only the left-eye HIGH route is captured.
The diagnostic also misses the zero/block-clear endpoint because bucket reuse and
the 12-sample cap can consume it. Reserve a completion sample and obtain the actual
HIGH/LOW/toggle restoration paths before treating the reported wall pop as solved.

## Next implementation order

1. Eliminate audited path/action-FX-only wall invalidations with consistent exact
   ownership filtering, retaining wall dependencies and animated restoration.
2. Complete native card-widget art preparation and the actual fixture shape;
   prepare safe original portrait references during loading where already available.
3. Attribute remaining native stat conversion, fan rebuild and ghost invalidations;
   reuse valid inert resources without starving immediate local/remote interaction.
4. Complete bounded draw-return/message attribution for the still-open wall pop and
   native/unassigned hitches. Retain the same functional/1:1 contracts on all platforms.

No runtime optimization is claimed from this documentation-only review. Existing
successful617 runtime suites are not repeated for unchanged source. Reproduced log
extraction, input hashes, sink equality and documentation checks validate this audit;
they do not substitute for the next hardware capture after actual implementation.
