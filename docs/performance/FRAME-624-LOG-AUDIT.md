# Build624 Steam Frame: three-room hardware evidence

The supplied run supports the report that the revealed scenario is unplayable:
three complete post-spinner, tracked-headset windows average **74.41 ms/frame**
(about 13.4 application frames/s), with individual means of 61.04–103.46 ms.
Already applied zero-detail/effect budgets do not remove the remaining scene,
presentation and engine work. The evidence does **not** isolate a GPU bottleneck
or measure additional multiplayer cost.

## Provenance and reproduction

Both sinks identify **1.1.0 / ModBuild624 / b123cefc9 [dev]**, built
2026-10-05 16:01:31 UTC: Player lines 42/155 and LogOutput lines 17/71. The audit's
worktree starts at `9cc7e589`; this is a later source checkpoint, not the
hardware binary's commit. The diff from `b123cefc9` to that checkpoint is empty
for Core/Perf, Defaults, Core/WallFade and WorldUI/Conversion.

Only Player.log, LogOutput.log and append-only openxr-diagnostics.log were
supplied; there are no screenshots or videos to inspect. Immutable read-only
copies and extraction are in this worktree's gitignored
`.planning/debug/frame624-audit/`. Run `python3 .planning/debug/frame624-audit/extract.py`
to reproduce frames/steps CSV and JSON, sink correspondence, events, spikes and
the weighted summary. `log-triage.txt` records the actual
`scripts/log-triage.py --expect-build 624` run against both inputs.

| Input | Bytes | SHA-256 |
|---|---:|---|
| LogOutput.log | 9,273,015 | `3efd72e895b4792dfa773059359ebfd4161d8051c28ad2b86c48a41fab6e0709` |
| Player.log | 9,910,151 | `c056ceaadb714957824900617801b644d916be5feb70d5743eee663cd99b6521` |
| openxr-diagnostics.log | 101,536 | `b3f87cdd40d7ed72cbe04df0e96f7298263170bcc27e908f3c15b982eab1b6f5` |

All **16 FRAME, 16 SPLIT, 16 NATIVE, 16 ranked STEPS and 16 STEPS TAIL messages**
agree in sequence and content between sinks. These are two outputs of one run.
Player has 371 emitted SPIKE rows versus LogOutput's 370: its final extra row is
frame 7397 (Player 16373), after headset presence loss. LogOutput's last wall
tick report (7844) is absent from Player's final flushed tail. Neither difference
affects the selected summary windows. Debug-only preparation reports are read
from Player; their absence from LogOutput is not absence of work.

The actual backend is Unity 2021.3.5f1, D3D11/Turnip Adreno 750, Wine OpenXR,
SteamVR/OpenXR 2.17.10, MultiPass (Player 94/111). The final OpenXR opening is
2026-10-05 19:09:35; older appended sessions are not pooled into this analysis.

## Loading, reveal and settings boundaries

1. Boot, MainMenu and CampaignMap precede the scenario. Initial user presence is
   lost and regained (LogOutput 126/382); the entire scenario comes afterwards.
   The head and both controllers remain tracked through the reviewed scenario
   heartbeats. The final presence loss is Player 16340/LogOutput 7813, and the
   subsequent tail is excluded.
2. Room 1 is the first visible room (Player 7396/LogOutput 2815).
   Native loading ends at Player 10719/LogOutput 3221. Initial interaction
   preparation finishes after 52.77 s (Player 12682), and the spinner disappears
   at Player 12705/LogOutput 4780. A preceding 61.93-ms window contains initial
   preparation; subsequent 61.49/64.43-ms windows still cross spinner closure.
   **No complete entirely post-spinner one-room summary exists.**
3. `DebugMenu.RevealAllRooms()` opens both additional rooms in one action
   (Player 13151/LogOutput 5164): three doors become open, two hidden rooms become
   visible, and the log explicitly reads **multiplayer session active: no**.
   Registry 1→3 follows at Player 13612/LogOutput 5369. There is **no separately
   measured two-room phase** or matched one/two/three-room A/B experiment.
4. Reveal-time native work and preparation are separate from ordinary play.
   The room spinner closes at Player 13897/LogOutput 5623. The 103.61-ms window
   (Player 14369/LogOutput 6055) crosses this boundary and is retained as mixed.
   Background ghost/metadata preparation continues after spinner closure and
   completes after 70.13 s at Player 14839, inside the later 61.04-ms window. The
   selected windows are wholly post-spinner, not wholly after every cosmetic
   cache job. This matches the current loading contract and is explicitly retained
   in the assessment.

Grass, decoration, vegetation, player/enemy figure detail and ambient figure
FX are already 0%; cloth simulation is off. Player 10715 proves 252 scenery
renderers actually masked, plus nine projections. Player 10753 then 13582 prove
10→16 actors, 21→27 disabled cloth solvers, 48→112 masked optional figure FX and
40→88 paused particle solvers. Current admitted body vertices rise 93,644→122,643;
the scene-wide last-frame visible-body sample rises from 50/61,018 to 84/97,115
meshes/vertices. Those are not head-camera draw counts. Only five actors have
native authored coarse-body LODs; 37 verified mesh derivatives are admitted after
reveal. `players=0 enemies=0` in FRAME means **budget percentages**, not no actors.

Player 10717's original environment report contains 38 compatible surfaces,
38 unreadable originals, **zero batched sources/chunks**, one simpler material
and five ambient solvers. It establishes an unproductive batching path for that
reported state, not the final three-room population: no later complete environment
report was emitted. The scene census remains incomplete at 143.38 s/59,458 visited
nodes (LogOutput 7398); no completed SCENE/SIM/GFX census supports final draw-call,
behaviour or total-renderer claims.

## Sustained cost after the room spinner

| FRAME location, Player / LogOutput | Seconds / frames | Mean / p95 / max ms | Logic ms | Render span ms | Unbracketed ms |
|---|---:|---:|---:|---:|---:|
| 14682 / 6313 | 10.0 / 122 | 82.70 / 126.68 / 144.57 | 41.03 | 8.56 | 33.12 |
| 15364 / 6970 | 30.2 / 495 | 61.04 / 113.64 / 276.75 | 29.40 | 5.32 | 26.33 |
| 15876 / 7391 | 20.1 / 193 | 103.46 / 123.89 / 158.90 | 44.39 | 10.06 | 49.01 |
| Frame-weighted, 810 frames | 60.3 / 810 | **74.41** | **34.72** | **6.94** | **32.76** |

There is no pooled p95 obtainable from per-window percentiles. Rounded printed
seconds and means are the calculation inputs. These spans partition the frame;
named native/mod timers overlap them and must not be added or subtracted as
exclusive CPU costs. The mod's depth-zero named scopes separately average 22.29 ms,
about 30% of the interval and already above a 72 Hz/13.89 ms frame budget.

The same three windows show these substantial recurring mod scopes:
EnvironmentBudget.PreCull 3.11 ms; WallFade.Late 2.12 ms; ActorBars.Late 1.92 ms;
SceneryBudget.Update 1.61 ms; Cards.Driver 1.59 ms; CanvasConversion 1.26 ms and its
separate Late pass 1.03 ms. Nested children are not added to parents. Scenery
Update falls 5.64→1.10→0.36 ms as late discovery settles; WallFade.Late instead
varies 2.44→0.52→6.01 ms with the observed view/activity. Environment PreCull is
present every frame, including both native eye-culling callbacks. Source shows
repeated native material validation here even when no batch can render. This
is a concrete work-removal target; its timer does not promise a 74→14 ms result.

HeadCamera has two passes/frame, with no other camera named in these three
SPLIT camera ledgers. Eye targets stay 3408×3408 per eye, 1x MSAA/scale 1.00:
23.2 million nominal pixel samples per MultiPass frame. Lower resolution is a
plausible test candidate, not a demonstrated fix for the 34.72-ms logic span.

The runtime reports 24↔12 Hz changes. Its XR `gpu` means are 82.95, 59.34 and
103.83 ms, 97–100% of their corresponding frame intervals; the instrument itself
marks them unusable as GPU-busy time. FrameTimingManager returns no samples;
Animator, Canvas.BuildBatch and several engine markers are unavailable, while
the reported Gfx.WaitForPresentOnGfxThread marker remains zero. **None proves
GPU headroom or a GPU-bound scene.** The unbracketed 32.76 ms can include engine
work and waits.

## Hitches and strongest attribution

The biggest spikes are not sustained loaded-play FPS samples:

| Context / frame | Player / LogOutput | Frame ms | Positive attribution |
|---|---|---:|---|
| Initial load 4975 | 8472 / 3083 | 19,358.83 | Named mod 185.57; selected Apparance callback 617.84. Most remains unassigned. |
| Reveal 5986 | 13441 / 5231 | 3,715.31 | ActorBars.Pose.Prepare 855.77; native message ActivateProp 711.51. |
| Reveal 5987 | 13498 / 5264 | 11,198.54 | Native message ActivateProp 9,301.63 inside Choreographer; Apparance 690.05. |
| Initial preparation 5184 | 11360 / 3536 | 538.82 | Original atlas/sprite preparation 499.73–502.31, nested. |
| Reveal preparation 6097 | 13863 / 5591 | 1,024.85 | Interaction preparation 622.53 before spinner closure. |
| Loaded 6624 | 14999 / 6619 | 170.94 | PlatformLayer.Update 135.505 inclusive. |
| Loaded 6557 | 14901 / 6526 | 247.74 | Named mod 24.75; selected native callbacks small. Cause remains unassigned. |

The three complete loaded windows contain 70 **printed**, rate-limited SPIKE
rows (104 including the preceding mixed post-spinner interval), all with zero
GC collections since their prior frame. Their summary nevertheless contains
one GC collection; unprinted frames must not be classified as GC-free. GC is
not an explanation for most measured printed hitches. Likewise, small selected
native callbacks, sampled for only the first 120 frames/window, do not exonerate
all native work or all uninstrumented mod work. The old SPIKE verdict “NOT the
mod” describes low named-scope coverage, not a complete attribution.

There are six native Hydra DNS error rows, loading-time missing-door messages
and one bounded bar-adoption warning. They do not prove the sustained slowdown's
cause. Probe setup 6824.01 ms is reported as a one-time diagnostic cost, separate
from ordinary sampled callbacks; it is not a recurring native CPU estimate.

## Implications for the next strategy

Previous [618 preparation/message work](FRAME-618-IMPLEMENTATION.md),
[619 loading/environment work](FRAME-619-IMPLEMENTATION.md),
[620 loading/detail corrections](FRAME-620-IMPLEMENTATION.md) and
[621 exact small-decoration coverage](FRAME-621-IMPLEMENTATION.md) are present.
The 1190/1190 native-art preparation receipt and effective masks demonstrate
execution, while zero environment chunks/unreadable originals demonstrate an
important limit. Do not label already closed private element-array preparation
or all older scene-signature churn as the cause of this new capture.

Start with recurring redundant native property reads in the environment pass
and other measured presentation loops, retaining native-write invalidation and
each eye's original state. The existing floor/structure batching delivers no
chunks in its reported state; a useful next approach must address unreadable
original geometry or reduce draw units, rather than raising a batch toggle
that cannot act. Reducing retained walls/ornaments, replacing distant whole-room
geometry, simplifying figure/healthbar presentation and lowering eye resolution
are distinct larger compromises to evaluate against the user's presentation
rulings. This log cannot certify their appearance, safety or gain.

There are no remote scenario peers or online measurements in this capture.
NPC publication fixes on another agent's branch do not establish scenario
multiplayer headroom here. A future matching build needs a post-spinner paired
run with the same three-room state, first single-player and then the intended
peer population; shared content, pose, effects and animation parity remain the
standing contract. A few milliseconds of source-proven work removal is useful
progress, but this evidence demands substantially more than one small pass
before a fluid multiplayer result can be claimed.
