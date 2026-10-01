# Steam Frame: Build 600 large-scenario evidence

## Provenance and limits

The latest main-checkout `steam_frame/LogOutput.log` and `Player.log` both identify
ModBuild **600** (LogOutput line 17; Player line 42). The Player build stamp at
line 155 names `7198477bf`, assembly 1.1.0.0, running SteamVR/OpenXR. The matching
frame summaries establish that these are the same run. The run enters a procedural
scenario directly; it does not establish a 3D-map test. No new screenshots exist
in this Frame evidence folder.

Raw copies of all three supplied logs and line-indexed performance summaries are
retained in the main checkout's gitignored
`.planning/debug/steam-frame-evidence/build600-20261001/`. The LogOutput SHA-256 is
`49c215a92c9173f6a8d8e98acf5ded8843ec2d407c6f50894bf3b7ad3ca167e3`.
Line references below refer to that raw copy, not to generated reports.

Loading/startup windows are excluded from the table. The two full post-load
windows include normal gameplay and opening the options window. They are not
fixed-view, fixed-setting A/B samples: the grass setting changes during the run,
the head moves, and visibility counts change substantially. They cannot establish
a performance regression or improvement against Build 599.

## Complete loaded-scenario windows

| Metric | LogOutput line 2586 | LogOutput line 3118 |
| --- | ---: | ---: |
| Window duration / frames | 20.0 s / 167 | 30.1 s / 241 |
| Frame mean / p50 / p95 | 119.97 / 108.91 / 168.83 ms | 125.16 / 119.53 / 156.51 ms |
| Update→LateUpdate mean | 62.86 ms | 61.27 ms |
| Render cull+submit mean | 33.20 ms | 38.46 ms |
| Unbracketed engine work/waits | 23.91 ms | 25.43 ms |
| Measured mod scopes, exclusive total | 33.03 ms/frame | 25.13 ms/frame |
| Head-camera cull+submit | 19.21 ms | 23.81 ms |
| Scene-wide visible renderer estimate, p50 | 1,349 | 4,849 |
| Head distance from board, p50 | 27.4 world units | 30.3 world units |

The frame means correspond to about **8.3 and 8.0 completed game frames/s**.
The runtime's reported 12/24 Hz is not the physical panel's nominal capability.
The `xr gpu` values follow the frame interval and explicitly report themselves as
unusable GPU busy time. The residual span is also not proof of GPU saturation.

GFX lines 2595/3126 report 3408×3408 per eye, MultiPass, Fastest, shadows disabled,
pixel lights zero, MSAA zero, lodBias 0.30, no head depth texture, no head command
buffers, and 223 active/enabled LOD groups. Shared wall-read and light-work caches
are enabled; desktop mirroring is disabled, demonstrating that the saved Frame
off choice is now respected. Grass is 20% at the first GFX sample and 25% at the
second. These point readings do not imply those values were constant throughout
either preceding window.

## Why the grass control did not solve this run

The scenery summaries contain the decisive evidence:

| LogOutput line | Setting | Eligible leaf renderers | Owned hidden renderers |
| --- | ---: | ---: | ---: |
| 1130 | 25% | 0 | 0 |
| 2536 | 0% | 27 | 27 |
| 2616 | 20% | 27 | 20 |
| 3348 | 25% | 27 | 19 |

At zero density the implementation masks only **27 of 6,560 active scene
renderers, about 0.41%**. At 20% the later independent SCENE census reports exactly
20 `forceRenderingOff` renderers, matching the budget's owned count. The control
does write a real rendering mask after the setting changes; it affects far too
little of this scenario to support the intended reduction.

The initial scan sees only 728 hierarchy nodes and 484 mesh-renderer visits and
admits nothing. A later setting-triggered scan reaches 27,091 cumulative node
visits and 20,957 cumulative mesh-renderer visits, admitting 27. These are traversal
counters, not distinct active-renderer counts: overlap and repeated scans inflate
them. The run exposes a discovery/lifecycle gap: an early partial hierarchy scan
is not followed by a successful settled-content scan before manual retuning.
The exact missed completion edge needs the code fix; the log does not name that
callback.

Build 600's classifier also admits only `PCG_FR_Floor_Grass_Hex_*` units whose
entire renderer membership is one to eight named grass leaves, contains no
collider/effect/animator/UI, and has one `Amp_Basic_Foliage` material per admitted
leaf. It excludes trees, bushes, wall-attached dressing, other floor grass
generators and mixed-material leaves by design. At density 0 its named-candidate
rejections include 836 missing generators, 216 collider/effect exclusions, 107
actor/preview ancestry exclusions and 88 shader exclusions. Thus a slider fix
alone cannot remove the larger vegetation population.

## Actual scenario populations and effective reduction targets

Unlike Build 599's menu-only detailed census, Build 600's line 2593 samples the
loaded `Game`/`ProcGen` scene. The new-scene cooldown correction worked.

- **6,560** active renderers; 5,448 enabled; 20 force-hidden; 4,843 visible to at
  least one camera and included in the head camera's mask.
- MeshRenderer: 6,229 total / 5,170 enabled / 4,680 visible candidates.
  ParticleSystemRenderer: 197 / 192 / 93; SkinnedMeshRenderer: 92 / 84 / 68.
- Submitted material-slot candidates: `Amp_Basic_Foliage` **2,040 / 2,851** total,
  `Amp_Basic_WallFade` 1,601 / 1,607, `Amp_Basic_N_MRAO` 960 / 1,392.
- The largest generated room group alone contributes 4,488 visible candidates
  out of 5,904 active renderers. Mod layer 27 has just 40 active renderers (0.6%).
- Zero renderers report static batching; 6,062 material slots have GPU instancing
  enabled. Neither flag proves that the corresponding geometry shares a draw.

These are renderer/material submission estimates, **not measured draw calls**.
One renderer may carry several material slots. The 2,040 foliage slots are a
useful scope indicator, not a promise that exactly 2,040 renderers can be removed.

Live hierarchy diagnostics and TEX line 2594 establish broader names worth
classifying with procedural provenance:

| Kind | Names observed in this run | Protection requirement |
| --- | --- | --- |
| Floor grass | `PCG_FR_Floor_Grass_Roots_06_PR`, `PCG_FR_Floor_Grass_Hex_Half_PR`, `FR_Floor_Detail_Grass_01`, `FR_Floor_Detail_Grass_05_PR`, `FR_Floor_Scatter_Grass_Small_01` | Keep the native floor/base renderer and gameplay prop ancestry. |
| Floor shrubs | `FR_Floor_LargeBush_03` through `_07`, `FR_Floor_PlantsBushes_01`, `_02`, `_04` | Require generated room dressing, not an interactive obstacle. |
| Tree foliage | `FR_Tree_02`, `FR_Tree_05`, `FR_Tree_Glow_01` | Inspect individual mesh/material roles; a whole tree root can contain structural geometry. |
| Wall-attached vegetation | `FR_Wall_Grassy_Verge_Thin_Narrow_Bushes_01`, `FR_Wall_Grassy_Verge_Thin_Bushes_01`, `FR_Wall_Grassy_Verge_Thin_Ivy_Grass_01`, `FR_CW_UnderWall_01_Grass` | A procedural wall ancestor is compatible with decorative foliage, but the wall's structural renderer must remain. |
| Small decorative stones | `FR_Stones_01`, `_02`, `_06`, `FR_Floor_Detail_Small_01_Stones` | Do not infer that all rocks are dressing; preserve structural rock bases. |

LogOutput line 1413 shows a single grassy-verge generated unit containing a wall
renderer, stone dressing and a bush renderer. Lines 1377/1378 identify
`FR_Pillar_Tree_Trunk_01` units as wall-feature fragments with 17 renderers.
TEX identifies `FR_CW_UnderWall_01_Rock` as a substantial wall base. Consequently,
masking every `PCG_FR_*`, every tree root, every Ground-layer renderer, or every
renderer beneath a wall would damage the playable structure. Prefer leaf-level
classification with explicit generated-content provenance, positive dressing
names/material roles, and game-actor/door/prop/preview exclusions.

The native `ProceduralBase.NotifyContentPlacementComplete` propagates content
changes to ancestor monitors; `ProceduralMapTile.NotifyContentPlacementComplete`
then updates Generated Content layers and calls `ApplyVisibility`. Native
`ProceduralMapTile.ShowContent` separately toggles generated children and Preview
visibility. A settled-load discovery pass and re-generation/reveal handling must
follow these real paths, including inactive unrevealed rooms. A fixed early walk
or an enabled-only snapshot is insufficient. Mask renderer presentation only;
do not disable native GameObjects, colliders, actors or gameplay callbacks.

The maintainer explicitly authorizes a minimal scenery profile in the latest
2026-10-01 report. That permits broader decorative reduction. It remains a
configurable presentation compromise available on PC and reversible on Frame;
cards, multiplayer visuals, figures, pickups, doors, floors and functional
obstacles are not covered by a blanket removal permission.

## Remaining costs and evidence from Build 600 changes

The final selected native-callback capture at line 3123 sums to 2.457 ms/frame
across 15 inclusive targets; nested intervals must not be added as exclusive
CPU time. PlatformLayer.Update is 0.901 ms/frame and ApparanceEngine.Update 0.308.
This capture does not explain the 61.27 ms whole-window logic span. The aligned
logic seams report Update 42.71 ms, interphase 2.50 ms and LateUpdate 19.23 ms
over the first 118 aligned frames, a different sampling population.

SCENE's companion SIM census counts 8,480 active MonoBehaviours, 7,580 enabled;
418 callback-eligible Update instances and 63 LateUpdate instances. It finds 123
animators and 197 particle systems, 143 of which use AlwaysSimulate. Those counts
are not cost measurements, and effect completion can gate gameplay, so globally
disabling animators/particles is not justified by this evidence.

The grass driver's measured Update cost in the first full loaded window is
0.224 ms average, worst 1.89 ms (line 2588). Its existing bounded processing is
not the main sustained bottleneck. A broader scan must still remain bounded and
settle primarily during loading rather than rescanning thousands of nodes in
every playable frame.

WallFade.Late remains 11.32 ms/frame in the final complete window, including
6.17 ms/frame in Tick.Decide. Later wall-table commits still hitch: line 3358
records 246.38 ms total, of which WallCache is 98.31 ms. The read-cache source
change avoids repeated queries, but this run has no cache-off A/B and therefore
does not quantify its gain. The light-write optimization similarly has no
controlled off comparison. Reducing detailed dressing may also reduce the wall
presentation workload, but that secondary benefit needs measurement after the
broader budget actually admits a substantial renderer population.

Both ScenarioCamera and UI Camera still have timed passes (6.45 and 3.05 ms/frame
in the final window), in addition to two head-eye passes and visible options/pause
panel captures. They are separate potential levers; camera passes may feed
offscreen textures, UI and gameplay presentation, so names alone do not prove
they can be disabled safely.

No mod Error/Fatal entry or unhandled exception stack is found. Native Hydra DNS
errors recur without a timed link to the sustained slowdown. Presentation latch
warnings remain separate from the primary renderer-volume failure.

## Collider and interaction audit

`Renderer.forceRenderingOff` does not remove a collider. Hiding collider-bearing
decorative meshes can therefore create invisible interaction blockers; retaining
all collider state alone is not a complete interaction-safety proof.

Source paths that matter:

- `Hands/Interact/RayInteractor.cs:811` picks the first native physics hit without
  testing renderer visibility. `Board/BoardDriver.cs:75` supplies the native
  Controller's active selection mask. Before that synchronization, RayInteractor's
  default is `Physics.DefaultRaycastLayers`.
- `Hands/Interact/RayUguiDriver.cs:233` rejects a farther UI window when that
  physics hit is closer. An incidental collider needs no interactable component
  to block the laser or window.
- `Board/BoardPick.cs:334` and `:368` perform independent near/far native-mask
  physics raycasts. `Board/Patches/PickingPatches.cs:49` independently raycasts for
  the patched native `MF.FindInteractableAtMousePosition` call. Filtering the hand
  ray alone would leave these game selection paths inconsistent.
- `Board/FigureGrab/FigureGrabDriver.cs:1132` resolves actors from the hit collider's
  `CInteractableActor` ancestry. A closer hidden decoration can prevent reaching
  that actor. Physical proximity grabbing uses registered grabbables, so a bare
  incidental collider is not itself a new grabbable, but can still affect physics
  pick/occlusion.

The effective physics layer masks were not recorded for every interaction state.
It is not established that every decorative collider in this room intersects the
current mask, nor that a mask which excludes it now will exclude it after a game
interaction-state change. Do not claim a collider is harmless based only on its
renderer layer in the SCENE census.

The conservative admission rule is to preserve a visible leaf when its own active
collider, a MeshCollider using that leaf's mesh, or a collider belonging to a
wholly hidden decorative unit would become an invisible surface. A collider on a
shared wall/floor ancestor may remain when its corresponding structural geometry
also remains visible; a wall ancestor should not automatically exempt all attached
foliage. Ambiguous collider ownership should preserve the affected decoration.
Do not disable native collider/GameObject state to recover the renderer saving.

If a later implementation deliberately skips owned hidden incidental colliders
instead, the ownership check must be specific to the scenery budget, and every
physics pick path listed above must use the same bounded query/filter. A generic
"any force-hidden renderer" test could bypass unrelated visibility systems and
gameplay walls. Such a pointer change needs its own focused tests.

`Core/FigureRendererGuard` protects actors, animators and currently held props;
it does not identify all non-held gameplay props. Retain explicit ProceduralProp,
door, functional UnityGameEditorObject and CInteractable ancestry exclusions,
including props with no ActorBehaviour. The native UnityGameEditorObject type
includes floor/edge/coverage entries, so treating every ancestor of that type as
a prop would also over-exclude generated floor dressing. The native
`ObjectCacheService` prop mapping and `Board/FigureGrab/PropVisualLookup` establish
the actual `CObjectProp.InstanceName` visual provenance when a component alone
does not identify a gameplay object.

## Acceptance for the next candidate

An automated classifier test proves admitted/protected cases, not this room's
actual savings. The next hardware evidence must show a substantial count of
owned hidden decorative renderers after loading, including the initial default
without touching the slider, and a visible reduction in dressing. Compare the
minimal profile with original scenery at the same fixed view and eye target.
Restoring full detail must restore every budget-owned mask without revealing
unexplored content or touching an unrelated owner's mask. New-room reveal and
setting changes must retain functional scenery and all local/remote game UI.
