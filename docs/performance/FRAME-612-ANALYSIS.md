# Build 612: effective meshes, camera savings and remaining CPU stalls

The supplied Steam Frame standalone files identify **612 / a606f9530**. Immutable
inputs, hashes, parser output and independent inventories are preserved under
`.planning/debug/frame612-new-analysis/`. `Player.log` is another sink for the
same run, not another measurement session. It contains Debug-severity records
filtered from `LogOutput.log`; inspect both before declaring an instrument absent.
No new headset screenshot or video accompanies this run.

## Loaded-play comparison

The loading indicator ends at LogOutput line 7237. Discard the measurement window
ending at line 8027 because it still includes preparation. The following 21
windows contain 7164 frames, starting at line 8434. This run enters the scenario
directly: initialization of Gloomhaven_unified is not an active 3D-map test.

| Loaded scenario | Frame mean | Mod scopes | Logic span | Render span | Unbracketed work/waits |
| --- | ---: | ---: | ---: | ---: | ---: |
| Build 610, 3280 frames | 61.21ms | 19.60ms | 34.66ms | 9.72ms | 16.83ms |
| Build 612, 7164 frames | 53.20ms | 17.81ms | 31.67ms | 7.76ms | 13.77ms |

The observed mean improves by 8.01ms, or 13.1%. Ordinary VR view motion is valid
hardware evidence, but these sessions cannot isolate each change's contribution.
The reciprocal mean is roughly 19 application frames/s, not headset/compositor
FPS. Window p95 values still range from 73.12 to 106.12ms; do not pool those
percentiles. No actual application GPU-busy measurement is available. XR's GPU
interval/wait and the unbracketed residual must not be presented as GPU workload.
Mod scopes are inside the logic span and must not be added to it.

Camera suspension is positively verified: all 37 managed consumers were bridged,
and the later SPLIT samples contain only the two HeadCamera eye passes. Earlier
extra PanelSSCam passes belong to visible pause/options captures. Their presence
does not establish a camera leak. No native camera bridge failure is reported.

## What the detail settings actually did

Player.log's scenario readbacks show 17 actors and **36 applied derivatives**.
Original/current admitted body vertices decrease from **278708 to 140043**, then
**137055**, a 49.75–50.83% reduction. The inventory includes inactive native LOD
slots, so it is not a visible triangle or draw-call counter. The visible-unmasked
inventory reports 39 body meshes with 66674, then 63686 vertices, from any camera.
Nine mesh parts prepare successfully, including five distance parts, with 203
cached derivatives. No unavailable-part or missing-derivative failure is recorded.
Fifteen cloth solvers are disabled; 53 optional FX renderers are masked and 38
particle solvers paused.

This proves mesh replacement, not its isolated millisecond saving. Distance LOD
adds the strongest far tier only after crossing its projected-size boundary.
With scenario detail already at 0%, near and mid bodies both use tier20. The
remaining renderer count, native animator updates and skeletons are unchanged.
Similar silhouettes are an intended property of these derivatives.

The maintainer confirms the new slider was **Immersive NPC body detail**. That
control affects only merchant, priestess and enchantress on the active 3D map.
This capture has no active resident stations and loads none of their mesh parts
17–28. It does not measure that slider or demonstrate that it failed. Values
0–33 select tier20, 34–66 tier45, 67–99 tier75 and 100 the original near body.
Enabled distance LOD can still reduce mid/far bodies. The control is a discrete
quality selection rather than removal of NPC anatomy.

## A source-proven skinning conflict, corrected in Build 613

The new budget repeatedly writes global TwoBones while the existing hand driver
requires global FourBones to avoid torn fingers. LogOutput contains **12830**
repairs, about 3.35MB of repeated diagnostic text, during this single run. This is
two owners fighting over one setting, not repeated native graphics changes.

The correction caps only owned actor/NPC renderer slots. Native Auto renderers
receive an explicit local cap while hands keep their original global protection.
Off, VR shutdown and foreign native renderer changes restore exact owned slots.
The actual extracted hand guard runs in both update orders in Unity; the previous
global-writing behavior is rejected by a causal control. The source defect is
fixed; its headset frame-time saving has not yet been measured.

Debug-only, bounded readbacks now report the effective map-NPC cap, requested
distance detail, actual far5 derivative count and original/current body vertices.
Existing scenario application summaries also reach the normal log sink while
remaining hidden at the player's normal mod log level. End-of-window snapshots
include effective distance, NPC, bone and interval settings. These are explicitly
not latched A/B attribution; the original four-dial measurement grammar is retained.

## Remaining work, ranked by evidence

1. **Wall-table invalidation and atomic commit cost.** After the stable cut there
   are 12 commits averaging 158.45ms, maximum 193.50ms. Ten are requested only by
   scene-signature movement and two only by the safety age ceiling; none cite room
   reveal, wall-signature movement or AABB drift. Native figure particle creation,
   disposal and activation contributes to scene-signature churn even with FX at
   0%. Cached wall bounds and mounted-prop rebuilds are the expensive phases.
   A narrowly proven non-wall particle exemption can avoid redundant commits;
   unrelated world effects and real wall/water changes must remain conservative.
   Raising the timer reduces frequency but does not spread a 190ms atomic commit.
   This round does not claim the invalidation fix is implemented.

2. **Animation and health-bar CPU work.** ActorBars costs about 2.06ms/frame and
   skips **zero of 121499** bone checks. All 87 detailed loop reports show prepared
   bounds but `sparseEligible=False`. The blanket unknown-MonoBehaviour guard
   rejects even the mandatory native CharacterManager and other visual/audio
   components. The existing stripped visual-rig fixture proves mathematical
   bounds, not in-game eligibility. Native types need individual source-backed
   admission and original-prefab tests; blind throttling could miss deformation,
   animation events or native continuation. A higher interval alone cannot help
   while that eligibility guard refuses every actor. The broader inventory has
   123 Animators, 99 AlwaysAnimate and 23 without renderers: reducing body polygons
   does not remove their CPU work. Optional distant/offscreen idle presentation
   cadence is a further compromise, subject to gameplay/event and shared-clock
   preservation. It is not implemented by this checkpoint.

3. **Stable UI maintenance.** Initiative depth normalization costs about
   0.585ms/frame and visits 2.37 million descendants over the loaded windows.
   Its existing optional interval defaults to zero on Frame. Panel fitting's
   current 0.05s interval is shorter than the approximately 53ms mean frame, so it
   commonly expires every frame. Existing 0.1–0.2s intervals are a reasonable
   configuration experiment; immediate reveal, layout/input and grabs must remain.
   Hidden-window and mip-arrival inventories are additional general caching
   targets, rather than reasons to delay new visible artwork or remote animation.

4. **Diagnostics and hand spikes.** Synchronous Debug scene inventories still
   cost 91.6–119.9ms on their sampled frames. Rationing limits frequency but does
   not distribute each walk. Temporarily setting `[Perf] SceneProfile=false`
   retains FRAME/STEPS/SPLIT and separates diagnostic pauses from gameplay cost.
   Hands.VRHand also records a 177.18ms worst frame, including a late NearGrip
   spike. Candidate election already uses a registry, not a scene search. Split
   distance/eligibility tests, hover callbacks and pickup/ghost construction
   before assigning that spike to one cause or throttling actual contacts.
   Some 600–800ms frames have only 16–23ms of named mod work.
   They are not explained by this inventory; native engine work/waits remain
   unresolved rather than automatically blamed on the GPU.

5. **Renderer submission, rather than only polygons.** A late inventory contains
   4709 active renderers, 2389 force-hidden slots and 1673 any-camera candidates;
   these candidates are not actual eye draw calls. Required floor/wall materials
   account for much of the remaining population. Optional static-material
   batching or cheaper distant structural geometry is a plausible larger lever.
   Keep doors, interaction/culling boundaries, occlusion correctness and native
   gameplay identity intact. Extra static batching was not introduced here.

Wall fading does not override the scenery slider: later inventories retain
exactly 2389 force-hidden renderers. Wall restoration never writes their
`forceRenderingOff` slots. The logged grass/bush release churn is not evidence
that vegetation was made visible again. Heap growth in one moving session also
does not establish a memory leak.

## Next hardware comparison

Use Build 613 with the existing complete Build 612 asset set. First repeat normal
scenario play and check that repeated skin-weight repairs disappear while hands
retain their correct appearance. For the map-only NPC slider, enter the 3D map,
change 100 to 0 and back during ordinary movement, and retain both log sinks.
The new NPC readbacks must show actual original/selected body counts; preserved
eyes/faces should remain recognizable. No fixed camera pose is required.

For configuration experiments in `BepInEx/config/dev.gloomhavenvr.perf.cfg`, use
`[Optimize] UiMaintenanceIntervalSeconds=0.1` and optionally
`InitiativeDepthEvalInterval=0.1`; existing values remain player-owned. Separately
test `[Perf] SceneProfile=false` to remove synchronous diagnostic inventories.
Do not promise a saving from ActorBarPoseCheckInterval until native eligibility
is proven. No additional feature reduction or new default is silently applied by
this fix.
