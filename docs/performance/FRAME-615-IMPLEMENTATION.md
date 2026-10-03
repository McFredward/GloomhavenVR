# Build 615: loaded-play CPU and renderer work

This implements the five remaining-work tracks in
[the Build612 hardware analysis](FRAME-612-ANALYSIS.md). The input capture remains
Build612; no new headset capture measures Build615 yet. Source-linked Unity
proofs establish the stated behavior, not a headset frame-time improvement.

| Hardware finding | Implementation | Boundary |
| --- | --- | --- |
| Repeated 158ms wall commits, predominantly scene-only invalidation | Exclude proven native actor-only particle terms; share exact static mesh bounds within one synchronous commit | Real walls, water, unknown world FX, room reveals and drift still invalidate; commits remain atomic |
| Prepared actor bounds but no admitted native rigs | Audit actual native components and the active Animator state, then permit stable idle bar checks | Unknown writers, actions, transitions and unsafe deformation retain immediate evaluation |
| Animator work survives polygon reduction | Optional native `CullUpdateTransforms` on audited event-free scenario idle states | Native time/continuation continues; actions and held figures restore synchronously; shared map NPC clocks remain unchanged |
| Repeated UI descendant walks | Event-invalidated original component inventories for row depth, hidden windows and mip arrivals | Original visibility, sprites, input and animations are read live; hierarchy/activation changes wake maintenance |
| Debug inventories introduce their own long frames | Incremental scene, simulation, texture and renderer/uGUI inventories | Soft time and work-item budgets; indivisible engine calls and observation span are explicitly measured |
| A rare 177ms hand spike lacks attribution | Separate distance, eligibility, hover, pickup/release and ghost-construction scopes | No contact sampling or callback throttling |
| Many structural renderers remain | Optional small native tile/cell chunks for audited static masonry/trim, plus a separate native instancing experiment | Original colliders/objects retained, unsupported/unreadable geometry excluded, dynamic changes restore originals |

## Configuration

All keys below are in `BepInEx/config/dev.gloomhavenvr.perf.cfg`. Optimization
controls are also available in VR Options. New Frame profiles select their own
profile; BepInEx preserves every saved setting. Existing profiles do not silently
receive the changed interval defaults.

| Section / key | PC fresh | Frame fresh | Meaning |
| --- | --- | --- | --- |
| Optimize / UiMaintenanceIntervalSeconds | 0 | 0.15 | Stable panel-size maintenance only; reveals/grabs/input remain immediate |
| Optimize / InitiativeDepthEvalInterval | 0 | 0.10 | Stable portrait depth reassertion; membership/activation/cap changes remain immediate |
| Optimize / ActorBarPoseCheckIntervalSeconds | 0 | 0.10 | Native audited stationary idle verification; previously blocked by broad eligibility guards |
| Optimize / OffscreenIdleAnimation | false | true | Audited native idle transform culling outside camera visibility |
| Optimize / ScenarioStructuralBatching | false | true | Audited static masonry chunks; requires ScenarioSimpleEnvironmentShading |
| Optimize / ScenarioStructuralInstancing | false | false | Reversible extra native material flags; many materials already have them enabled |
| Perf / SceneProfileBudgetMilliseconds | 0.5 | 0.5 | Soft incremental diagnostic budget per application frame |
| Perf / SceneProfileObjectsPerFrame | 64 | 64 | Maximum diagnostic work items per frame |

`[Perf] SceneProfile=false` still removes the detailed profile while retaining
FRAME/STEPS/SPLIT. Diagnostic capture span is different from synchronous frame
cost: samples observed over several frames must not be interpreted as one
simultaneous scene snapshot. Detailed walks require Debug and stop during load,
menu/scene transitions or disabled profiling.

The supplied run already has 4194 instanced material slots. Turning on additional
material flags is therefore an experiment, not the primary renderer-saving claim.
Structural chunks operate only where original readable geometry and exact native
identity permit reversible replacement. No essential floor/wall is removed, and
no saving is promised for unsupported cave meshes.

## Hardware comparison

Use matching Build615 peers for multiplayer. On Frame, retain both log sinks and
compare ordinary loaded scenario play; exclude the loading indicator and initial
preparation as before. Existing profiles can select 0.15s panel maintenance and
0.10s initiative depth explicitly. Toggle offscreen idle animation and structural
batching separately, then inspect effective native eligibility, skipped bone
checks, applied chunks and diagnostic slice costs. Recheck sleeping/waking/flying
bar height, held figures, room reveals, walls/doors, original 2D-map/non-NPC windows,
new card fronts and remote board/NPC animations. No fixed camera pose is required.

Unknown engine work/waits and missing actual GPU-busy data remain limits of the
capture. These changes neither prove a memory leak nor establish that all remaining
Frame stalls are fixed.
