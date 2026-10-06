# Build 631 PC graphics-settings audit (2026-10-06)

The PC run confirms substantial renderer/mesh/cloth reductions and cheaper UI
maintenance. It does **not** establish a settled, isolated FPS improvement, a
working live MSAA change, or a successful live-resolution crash retest. The paired
Frame run never starts VR, so it supplies no new headset rendering evidence.

## Evidence and row convention

Immutable inputs are in the main checkout at
`.planning/debug/frame-startup-regression-20261006T174635Z/inputs/`; the adjacent
`input-manifest.json` records the original paths and hashes. All six byte sizes
and SHA-256 values were independently verified. References `L`, `P`, and `F`
below mean `pc/LogOutput.log`, `pc/Player.log`, and `frame/LogOutput.log`.
Rows are **one-based raw LF rows**, calculated from decoded bytes with
`split('\n')`; embedded CR characters are not additional rows. Player contains
Debug details absent from LogOutput.

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| pc/LogOutput.log | 8, 233, 790 | `113712ed6c992b17006ccb43f9f5939cff8408fb9c9c5f665c125124c420258a` |
| pc/Player.log | 8, 900, 413 | `1daacbecf0aca47cc146b504026bebfdac83906c77337a336ef57b5834195791` |
| pc/openxr-diagnostics.log | 2, 331, 400 | `60bbfadae375778e454b7732e8e5d9abcf386119471f20a7a16c379cf070b353` |
| frame/LogOutput.log | 87, 259 | `0845ef3f9343e37039de0726639502aa85134d51c413dc62a6d2a9551eaf2e42` |
| frame/Player.log | 183, 674 | `5ee6886d1adee840c13f72286739d6b4cded4bfef11c298ac17d7a5a90bb0e96` |
| frame/openxr-diagnostics.log | 106, 427 | `19eb836fe90cbd819e7e95392e2c519dc49c196e2d4e2c0418df83a2d29a3d25` |

Both banners identify ModBuild 631 / `674ea6bc4`: L17/L71, P40/P154,
F17/F43, and frame Player40/176. PC uses an RTX 4090, D3D11 (P13–14),
SteamVR/OpenXR 2.17.10 (P461), MultiPass/Forward and 3760×3996 allocated
pixels per eye (L3673). Worktree base `366884877` already contains unrelated
NPC632 source; the audited RenderQuality, GraphicsProfiles, PerfConfig and
PerfMonitor.Figures sources are unchanged from the logged commit.

## Profile requests and achieved effects

The native callback runs for Fastest → Good → Simple → Fastest → Fantastic
(P14986/16095/16376/16577/16687), followed by confirmed VR profile application
(L6588/7535/7780/7958/8060). There are 23 distinct changed `[Optimize]`
keys, with change groups at L6565–6587, 7510–7534, 7755–7779, 7941–7957,
and 8037–8059. A repeated value emits no change marker.

| Changed keys | Fastest / Good / Simple / Fantastic requests | Achieved evidence and limit |
| --- | --- | --- |
| ScenarioSceneryDensityPercent, ScenarioDecorationDensityPercent, ScenarioVegetationDensityPercent | 0 / 60 / 25 / 100% | L6627/7600/7852/8033: 470/198/348/470 actual detail renderer masks, out of 470 eligible details; 3 tiles and 4427 unique mesh renderers inspected. Grass and trees/bushes/vines have **zero eligible renderers**, so those dials cannot save work here. Decorative projection masks: 12/4/7/12 of 20. Fantastic retune to100% is L8069; no final scenery census proves restoration. |
| ScenarioPlayerFigureDetailPercent, ScenarioEnemyFigureDetailPercent | 0 / 66 / 33 / 100% | L6622/7565/7811/7978: 10 actors, only 5 with authored coarse bodies, 23 admitted derivatives. Original/current admitted body vertices 147208/40632 for first Fastest and Good/Simple, 147208/29794 for second Fastest. Requested percentages are not achieved geometry percentages. |
| ScenarioFigureEffectsDensityPercent, ScenarioFigureClothSimulation | 0% /off; 60% /on; 25% /off; 100% /on | Same rows: actual FX renderer masks 48/20/36/48; paused optional particle solvers 38/12/27/39. Cloth solvers disabled 21/0/21/21. Fantastic100% /cloth on is requested and final measurement revision 7 becomes steady at L8603; no subsequent figure census. |
| ScenarioEnvironmentEffectsDensityPercent | 0 / 60 / 25 / 100% | P15080/16163/16438/16617: 5 identified ambient solvers and corresponding requested percentages. These rows do not report actual paused-solver counts. |
| ReduceScenarioGenerationDetail | on / off / on / off | L3690 freezes **original** generation through room reveals. All profile switches occur after that load; this dial has no effect on the current board and requires another scenario load. |
| ScenarioSimpleEnvironmentShading, ScenarioStaticBatching, ScenarioStructuralBatching, ScenarioEnvironmentMeshBank | simple on/off/on/off; other3 on/on/on/off | P15080/16163/16438/16617: 42→40 source renderers grouped into8 chunks; 1/0/1/1 simpler material. Structural grouping is effectively off in Good because native shading is retained, despite the batching request. Mesh bank enabled; no unreadable-source refusal. L8598 has zero chunk sources/groups after Fantastic disables them. |
| ScenarioExplicitEnvironmentInstancing | on / on / on / off | **Zero actual instance groups and sources throughout** (L7514/7759/7945/8598). P15081/15083: all42 candidates rejected by native flags; shadows, light probes and reflections each observed on 42 sources. Flags overlap. This switch supplies no instance-draw saving for these candidates. |
| ScenarioCheapWallShading, ScenarioTerrainDetailPercent, ScenarioDistantTerrainDetailPercent | cheap on/off/on/off; near 0/100/50/100; far 0/50/0/100% | L6624/7538/7783/7961/8063: 299 prepared surfaces. Actual surviving paired-camera triangle and cheap-material counters below confirm substitution. Prepared membership includes unopened/nonvisible surfaces and is not the rendered population. |
| FigureDistanceLod, SkinningBoneLimit | LOD on/on/on/off; bones 2/4/2/0 | Figure rows show 20 far-tier selections initially, 23 in second Fastest, plus actual admitted mesh counts. Bone-limit values are reported, but no per-bone runtime-work counter isolates their effect. |
| OffscreenIdleAnimation | on / on / on / off | L7514/7759/7945/8598: native authored culling for 10 tracked actors, **zero added transform-culling**. This scene already has authored culling; no incremental saving is shown. This retained offscreen-only dial does not reinstate withdrawn visible idle sampling. |
| UiMaintenanceIntervalSeconds, ActorBarPoseCheckIntervalSeconds, InitiativeDepthEvalInterval | .15/.10/.10 s; 0/0/0 s;.10/.10/.10 s; 0/0/0 s | L7514: 5478 pose-loop skips and 4782 checks for 10260 bars; Good L7759: 0 skips, 3850 checks; Simple L7945: 1932 skips, 1688 checks for 3620 bars; Fantastic L8598: 0 skips, 14490 checks. Pose writes continue every frame, preserving movement. Initiative tracked evaluations fall from 597/s in Good to 65/s in Simple. No dedicated counter here proves the generic panel-maintenance interval's individual saving. |

SharedEnvironmentMaterialReads and SharedUiWindowReads stay on; structural
instancing stays off and terrain distance stays 0.75 m. These are profile writes,
not changed-key experiments. Shared UI reuse is real: L7759 has 385 shared scans,
zero independent scans and 5775 panel reuses; L7945 has 362/0/5430. Material reuse
and multiplayer performance have no isolated on/off or paired-peer measurement
in this capture. SDK teardown reports Offline→Offline (P17558); local town
mirror work should not be mistaken for evidence of a connected peer.

## RenderQuality: live application gap

Startup/readback is confirmed: L76 corrects native MSAA 0→8;
L134 reports request/allocation 1.00; L3673 reports viewport 1.00 and
QualitySettings MSAA 8 (descriptor `msaaSamples=1` is a different readback).
**No later live-resolution request/change, viewport change, MSAA push or MSAA
reassertion is logged in either PC log.** Profiles request MSAA 0/4/2/0/8, while
resolution stays 1.00. The last measured MSAA value precedes every profile switch;
it is not proof that MSAA remains 8 afterward. This capture does not record the
prior 0.8-resolution transition sequence or validate its fix on hardware.

A source-grounded candidate for the missing MSAA confirmation is
`RenderQuality.ApplyMsaa`/`ApplyEyeScale` requiring `Camera.current == null`.
The existing `CanvasConversion.6.Hide.cs` contract and `VRRigDriver.Update`
history explicitly establish that Unity can retain a **stale nonnull** current
camera during valid Update/LateUpdate. The guarded tail still runs: anisotropic
reassertions occur at L7541/7785 and pixel-cap corrections are counted at L8099.
The camera value was not sampled here, so this candidate needs a focused fix/test
and an actual requested-versus-effective hardware readback.

Subsequent source checkpoint `193746287` gives the rig's existing Update tail
a call-local `TickFromUpdate` boundary while retaining conservative unmarked
calls. Startup checkpoint `3f9bc7e3f` also moves preparation after successful XR
initialization and removes pre-loader legacy setters. Neither change is present
in these Build631 inputs; hardware verification remains pending. Their focused
source/runtime checks do not retroactively establish what happened in this run.

Other RenderQuality writes are force-anisotropic on, full texture resolution on,
force-streaming-off false, pixel-light cap 0, and streaming budget 900 MB for
Fastest/4096 MB otherwise. Anisotropic reassertion is observed; texture limit 0 is
confirmed at L78; the cap is enforced at L359/L8099. The startup cap census
has zero eligible lights (L360), not a scenario-wide light census. L358 raises
streaming 900→4096 MB. `ApplyTextureStreaming` intentionally only raises the
budget: requesting 900 after 4096 does **not** reclaim that budget unless the game
rewrites it. There is no later budget readback proving a memory reduction.

## Work reduction versus frame cost

The triangle totals below count surviving paired-camera substitution leases
through each window, not unique scene triangles or GPU draw calls.

| Active profile before the closing marker | FRAME row, seconds / frames | Mean / p95 ms; mod ms | Original → submitted terrain triangles | Chunk sources → groups |
| --- | --- | --- | --- | --- |
| Fastest | L7511, 11.9/1027 |11.63/13.20; 4.50 |32310792→14080324 (**56.4% fewer**, L7514) |164400→32880 |
| Good | L7756, 4.5/385 |11.72/13.14; 4.33 |7631400→5529424 (**27.5% fewer**, L7759) |62772→12312 |
| Simple | L7942, 4.3/362 |11.89/15.13; 4.84 |11400104→6146552 (**46.1% fewer**, L7945) |57920→11584 |
| Fantastic | L8595, 16.9/1449 |11.69/12.75; 2.67 |566856→566856 residual transition leases, then native 100% |0→0 (L8598) |

Cheap-surface totals are 158004/0/55748/0 respectively. The savings are real
within eligible substitutions, while the handlers cost CPU: Fastest
Terrain.PreCull 1.573 ms and Environment.PreCull 0.405 ms per application frame
(L7512); Simple 1.599/0.408 ms (L7943). Two terrain callbacks and approximately
four environment callbacks occur per frame. Counters do not establish a net
GPU or FPS gain from that extra CPU work.

All four FRAME rows still say figure `state=preparing`; only L8603 opens a
steady final-Fantastic figure window. Short 1.9 s scenery preparation windows
(L7592/7844/8025) are separate. The user's view also changes: distance medians
37.1/27.5/25.7/53.6 world units. Do not label these as controlled settled A/B
results. Means are about 84–86 application frames/s, medians about 11.1 ms,
consistent with near 90 Hz pacing on this PC. Neither runtime GPU estimates nor
reported adaptive refresh rates establish GPU headroom/reprojection: L7171
reports 18 Hz while measuring 865 application frames in 10 s.

## Loading and anomalies

Scenario loading starts L3439; native loading ends after 6.40 s at L4106;
the indicator finishes its fade at L4129. Optional caches continue afterward
without owning the spinner. L4559 still includes preparation (21.35 ms mean,
204.73 ms maximum); exclude it from the post-load performance target.
This shows the Build 630 spinner ownership path being used; absent timestamps
on these rows cannot quantify the fade duration or prove subjective spinner
timing satisfactory on the headset. Frame VR fails before a session
(F32/F43; frame Player105–109 `XR_ERROR_RUNTIME_UNAVAILABLE`), which is a
startup/runtime issue outside this PC settings audit.

Two material failures precede every profile change: L6304/6305 give up on
`ST_Cult_Clutter_Chain` after 5 null-result retries, retaining hidden renderers.
They are not evidence that the new profile caused missing geometry. Startup
water-shader fallback warnings also predate the changes. PC reaches normal
teardown (P17555 onward) and allocator summaries, with six shutdown
NullReferenceException lines at P17571/17577–17581; no stack establishes their
owner. Do not hide them or call this an exception-free run. No live-resolution
crash retest can be inferred from these missing transition readbacks.

Next hardware evidence should wait for a figure steady marker, hold the same
view for each comparison, and record requested/effective viewport and MSAA.
Test the restored Frame startup first; repeat the three-room scene and then a
paired multiplayer run. Keep loading hitches separate from the settled result.
