# Build624 Steam Frame: three-room hardwareevidence

The supplied run supports the report that the revealed scenario is unplayable:
threecomplete post-spinner, tracked-headset windows average **74.41 ms/frame**
(about 13.4 application frames/s), with individual means of61.04–103.46 ms.
Already applied zero-detail/effect budgets do not remove the remaining scene,
presentation andengine work. Theevidencedoes **not** isolatea GPU bottleneck
or measureadditional multiplayer cost.

## Provenanceand reproduction

Both sinks identify **1.1.0 / ModBuild624 / b123cefc9 [dev]**, built
2026-10-05 16:01:31 UTC: Player lines 42/155 and LogOutput 17/71. Theaudit's
worktree starts at `9cc7e589`; this is a later sourcecheckpoint, not the
hardwarebinary's commit. Thedifffrom `b123cefc9` to that checkpoint is empty
for Core/Perf, Defaults, Core/WallFadeand WorldUI/Conversion.

Only Player.log, LogOutput.log andappend-only openxr-diagnostics.log were
supplied; thereare no screenshots or videos to inspect. Immutable read-only
copies andextraction are in this worktree's gitignored
`.planning/debug/frame624-audit/`. Run `python 3 .planning/debug/frame624-audit/extract.py`
to reproduceframes/steps CSV and JSON, sink correspondence, events, spikes and
weighted summary. `log-triage.txt` records theactual
`scripts/log-triage.py --expect-build624` run against both inputs.

| Input | Bytes | SHA-256 |
|---|---:|---|
| LogOutput.log | 9,273,015 | `3efd72e895b4792dfa773059359ebfd4161d8051c28ad2b86c48a41fab6e0709` |
| Player.log | 9,910,151 | `c056ceaadb714957824900617801b644d916be5feb70d5743eee663cd99b6521` |
| openxr-diagnostics.log | 101,536 | `b3f87cdd40d7ed72cbe04df0e96f7298263170bcc27e908f3c15b982eab1b6f5` |

All **16 FRAME, 16 SPLIT, 16 NATIVE, 16 ranked STEPS and16 STEPS TAIL messages** agree in
sequenceandcontent between sinks. Theseare two outputs of one run. Player
has 371 emitted SPIKE rows versus LogOutput 370: its final extra row is frame7397
(Player 16373), after headset presence loss. LogOutput's last wall tick report
(7844) is absent from Player's final flushed tail. Neither differenceaffects
the selected summary windows. Debug-only preparation reports are readfrom
Player; their absencefrom LogOutput is not absence of work.

Theactual backend is Unity 2021.3.5f1, D 3D 11/Turnip Adreno 750, Wine OpenXR,
SteamVR/OpenXR 2.17.10, MultiPass (Player 94/111). Thefinal OpenXR opening is
2026-10-05 19:09:35; older appended sessions are not pooled into this analysis.

## Loading, reveal and settings boundaries

1. Boot, MainMenu and CampaignMap precede the scenario. Initial user presence is
   lost and regained (LogOutput 126/382); theentire scenario comes afterwards.
   Headandboth controllers remain tracked through the reviewed scenario
   heartbeats. Thefinal presence loss is Player 16340/LogOutput 7813, and the
   subsequent tail is excluded.
2. Room 1 is thefirst visible room (Player 7396/LogOutput 2815).
   Native loading ends at Player 10719/LogOutput 3221. Initial interaction
   preparation finishes after 52.77 s (Player 12682), and the spinner disappears
   at Player 12705/LogOutput 4780. A preceding 61.93-ms window contains initial
   preparation; subsequent 61.49/64.43-ms windows still cross spinner closure.
   **No completeentirely post-spinner one-room summary exists.**
3. `DebugMenu.RevealAllRooms()` opens both additional rooms in oneaction
   (Player 13151/LogOutput 5164): threedoors become open, two hidden rooms become
   visible, and the log explicitly reads **multiplayer session active: no**.
   Registry 1→3 follows at Player 13612/LogOutput 5369. There is **no separately
   measured two-room phase** or matched one/two/three-room A/B experiment.
4. Reveal-time native work and preparation are separatefrom ordinary play.
   The room spinner closes at Player 13897/LogOutput 5623. The103.61-ms window
   (Player 14369/LogOutput 6055) crosses this boundary and is retainedas mixed.
   Background ghost/metadata preparation continues after spinner closureand
   completes after 70.13 s at Player 14839, inside the later 61.04-ms window. The
   selected windows are wholly post-spinner, not wholly after every cosmetic
   cache job. This matches thecurrent loading contract and is not hidden.

Grass, decoration, vegetation, player/enemy figuredetail andambient figure
FX arealready 0%; cloth simulation is off. Player 10715 proves 252 scenery
renderers actually masked, plus nine projections. Player 10753 then 13582 prove
10→16 actors,21→27 disabledcloth solvers,48→112 masked optional figure FX and
40→88 paused particle solvers. Current admittedbody vertices rise93,644→122,643;
the scene-wide last-frame visible-body sample rises 50/61,018 to 84/97,115
meshes/vertices. Thoseare not head-cameradraw counts. Only fiveactors have
nativeauthoredcoarse-body LODs;37 verified mesh derivatives areadmittedafter
reveal. `players=0 enemies=0` in FRAME means **budget percentages**, not no actors.

Player 10717's original environment report contains 38 compatible surfaces,
38 unreadable originals, **zero batched sources/chunks**, one simpler material
andfiveambient solvers. It establishes an unproductivebatching path for that
reported state, not thefinal three-room population: no later completeenvironment
report was emitted. The scenecensus remains incompleteat 143.38 s/59,458 visited
nodes (LogOutput 7398); no completed SCENE/SIM/GFX census supports final draw-call,
behaviour or total-renderer claims.

## Sustainedcost after the room spinner

| FRAME location, Player / LogOutput | Seconds / frames | Mean / p 95 / max ms | Logic ms | Render span ms | Unbracketed ms |
|---|---:|---:|---:|---:|---:|
| 14682 / 6313 | 10.0 / 122 | 82.70 / 126.68 / 144.57 | 41.03 | 8.56 | 33.12 |
| 15364 / 6970 | 30.2 / 495 | 61.04 / 113.64 / 276.75 | 29.40 | 5.32 | 26.33 |
| 15876 / 7391 | 20.1 / 193 | 103.46 / 123.89 / 158.90 | 44.39 | 10.06 | 49.01 |
| Frame-weighted, 810 frames | 60.3 / 810 | **74.41** | **34.72** | **6.94** | **32.76** |

There is no pooled p 95 obtainablefrom per-window percentiles. Rounded printed
seconds and means are thecalculation inputs. These spans partition theframe;
named native/mod timers overlap them and must not beadded or subtractedas
exclusive CPU costs. The mod's depth-zero named scopes separately average22.29 ms,
about 30 % of the interval andalready abovea72 Hz/13.89 ms framebudget.

The same three windows show these substantial recurring mod scopes:
EnvironmentBudget.PreCull 3.11 ms; WallFade.Late2.12 ms; ActorBars.Late1.92 ms;
SceneryBudget.Update1.61 ms; Cards.Driver 1.59 ms; CanvasConversion 1.26 ms and its
separate Late pass 1.03 ms. Nestedchildren are not added to parents. Scenery
Updatefalls 5.64→1.10→0.36 ms as latediscovery settles; WallFade.Late instead
varies 2.44→0.52→6.01 ms with the observed view/activity. Environment PreCull is
present every frame, including both nativeeye-culling callbacks. Source shows
repeated native material validation hereeven when no batch can render. This
is aconcrete work-removal target; its timer does not promisea74→14 ms result.

HeadCamera has two passes/frame, with no other camera named in these three
SPLIT camera ledgers. Eye targets stay 3408×3408 per eye,1 x MSAA/scale1.00:
23.2 million nominal pixel samples per MultiPass frame. Lower resolution is a
plausible test candidate, not ademonstratedfix for the34.72-ms logic span.

The runtime reports 24↔12 Hz changes. Its XR `gpu` means are82.95,59.34 and103.83 ms,
97–100 % of their corresponding frame intervals; the instrument itself marks
them unusableas GPU-busy time. FrameTimingManager returns no samples; Animator,
Canvas.BuildBatch and several engine markers are unavailable, while the reported
Gfx.WaitForPresentOnGfxThread marker remains zero. **None proves GPU headroom or
a GPU-bound scene.** Unbracketed32.76 ms can includeengine work and waits.

## Hitches and strongest attribution

Thebiggest spikes are not sustained loaded-play FPS samples:

| Context / frame | Player / LogOutput | Frame ms | Positiveattribution |
|---|---|---:|---|
| Initial load4975 | 8472 / 3083 | 19,358.83 | Named mod185.57; selected Apparancecallback 617.84. Most remains unassigned. |
| Reveal 5986 | 13441 / 5231 | 3,715.31 | ActorBars.Pose.Prepare855.77; native message ActivateProp 711.51. |
| Reveal 5987 | 13498 / 5264 | 11,198.54 | Native message ActivateProp 9,301.63 inside Choreographer; Apparance690.05. |
| Initial preparation 5184 | 11360 / 3536 | 538.82 | Original atlas/sprite preparation 499.73–502.31, nested. |
| Reveal preparation 6097 | 13863 / 5591 | 1,024.85 | Interaction preparation 622.53 before spinner closure. |
| Loaded6624 | 14999 / 6619 | 170.94 | PlatformLayer.Update135.505 inclusive. |
| Loaded6557 | 14901 / 6526 | 247.74 | Named mod24.75; selected nativecallbacks small. Cause remains unassigned. |

The threecomplete loaded windows contain 70 **printed**, rate-limited SPIKE rows
(104 including the preceding mixed post-spinner interval),
all with zero GC collections since their prior frame. Their summary nevertheless
contains one GC collection; unprintedframes must not beclassifiedas GC-free.
GC is not an explanation for most measured printed hitches. Likewise small
selected nativecallbacks, sampledfor only thefirst 120 frames/window, do not
exonerateall native work or all uninstrumented mod work. The old SPIKE verdict
“NOT the mod” describes low named-scopecoverage, not acompleteattribution.

Thereare six native Hydra DNS error rows, loading-time missing-door messages
and oneboundedbar-adoption warning. They do not prove the sustained slowdown's
cause. Probe setup 6824.01 ms is reportedas a one-timediagnosticcost, separate
from ordinary sampledcallbacks; it is not a recurring native CPU estimate.

## Implications for the next strategy

Previous [618 preparation/message work](FRAME-618-IMPLEMENTATION.md),
[619 loading/environment work](FRAME-619-IMPLEMENTATION.md),
[620 loading/detail corrections](FRAME-620-IMPLEMENTATION.md) and
[621 exact small-decoration coverage](FRAME-621-IMPLEMENTATION.md) are present.
The1190/1190 native-art preparation receipt andeffective masks demonstrate
execution, while zero environment chunks/unreadable originals demonstratean
important limit. Do not label already closed privateelement-array preparation
or all older scene-signaturechurn as thecause of this new capture.

Start with recurring redundant native property reads in theenvironment pass
and other measured presentation loops, retaining native-write invalidation and
each eye's original state. Theexisting floor/structurebatching delivers no
chunks in its reported state; a useful next approach must address unreadable
original geometry or genuinely reducedraw units, rather than raising abatch
toggle that cannot act. Reducing retained walls/ornaments, replacing distant
whole-room geometry, simplifiedfigure/healthbar presentation and lower eye
resolution aredistinct larger compromises to evaluateagainst the user's
presentation rulings. This log cannot certify their appearance, safety or gain.

Thereare no remote scenario peers or online measurements in this capture.
NPC publication fixes on another agent's branch do not establish scenario
multiplayer headroom here. A future matching build needs a post-spinner paired
run with the same three-room state, first single-player and then the intended
peer population; sharedcontent, pose, effects andanimation parity remain the
standing contract. A few milliseconds of source-proven work removal is useful
progress, but this evidencedemands substantially more than one small pass
beforeafluid multiplayer result can beclaimed.
