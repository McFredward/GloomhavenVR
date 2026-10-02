# Build 603: independent scenery and native figure/generation compromises

## Evidence and limits

The supplied `steam_frame/LogOutput.log` and `Player.log` identify **Build 601 / 98fba1a8d**.
This run includes CampaignMap followed by ProcGen. It is not the earlier direct-scenario
Build 598 run. There are no new screenshots in this evidence directory.

Eight settled budget reports inspect 10,055 unique mesh renderers across four tiles. At
grass 0%/decoration 0% they classify 1,091 grass, 1,460 vegetation and 906 dressing meshes;
all 3,457 eligible meshes are actually `forceRenderingOff`. The control was therefore
working on those admitted families. It did not establish that all remaining grass/tree
surfaces were classified. The source also applies `min(grass, decoration)`, so changing
grass while decoration remains 0% cannot alter any admitted grass. At grass 0%/decoration 100%
the actual mask count drops to 1,091, proving that other decoration returns separately.

Five complete tracked ProcGen windows at 3408×3408 per eye average 73.81–99.62 ms/frame,
with changing views/settings; they are not a controlled A/B or proof of a time-dependent
leak. Main-thread logic remains 34.03–52.26 ms and the measured render-loop span 19.34–27.04 ms.
Ignore mixed loading windows. The XR GPU statistic is not GPU busy time, and unbracketed
engine time must not be labelled GPU wait. The maintainer's perceived decoration improvement
is compatible with these mask reports but does not establish an exact FPS gain.

## Implemented controls

All controls are available in **VR Options → Graphics** and `[Optimize]` in
`BepInEx/config/dev.gloomhavenvr.perf.cfg`. BepInEx preserves persisted choices.

| Config key | Fresh PC | Fresh Frame | Behavior |
|---|---:|---:|---|
| `ScenarioSceneryDensityPercent` |100|0| Independent decorative grass; live,100 restores original |
| `ScenarioVegetationDensityPercent` |100|0| Independent decorative trees/bushes/vines/leaves; live |
| `ScenarioDecorationDensityPercent` |100|0| Loose stone/crystal/scatter dressing; live |
| `ScenarioPlayerFigureDetailPercent` |100|0| Cap player/summon meshes to original native coarser levels |
| `ScenarioEnemyFigureDetailPercent` |100|0| Independent original native monster mesh cap |
| `ScenarioFigureClothSimulation` |true|false| Optional secondary cloth physics on scenario figures |
| `ReduceScenarioGenerationDetail` |false|true| Native procedural quality 0 at the next scenario load |

An existing Frame grass value 25% is retained rather than silently migrated to 0. To test
the most aggressive scenery profile, explicitly set **all three scenery percentages to 0**.
Frame defaults are choices, never a forced platform branch. Original rendering is available
on Frame with 100% density/detail, cloth on and reduced generation off (next load).

The scenery classifier now identifies dedicated foliage-only anonymous LOD leaves beneath
original structural composites. It preserves their solid/mixed-material cores. Retained
collider representation follows original solid asset/mesh ancestry rather than requiring
each generated renderer GameObject to carry a readable asset name. Original mesh-family
fallback admits anonymous fern/shrub/reed/flower branches. Stable carrier hashes keep density
decisions unchanged across LOD switches. Named rejected candidates are Debug-only and
bounded to eight per scene, at most two per verdict.

Native gameplay props/obstacles, doors, structural floors/walls, native actors, lights,
UI, all local/remote cards and held props remain outside the scenery mask. Own enabled/matching
leaf colliders remain visible rather than becoming invisible laser blockers. Existing reveal,
late placement/material readiness, VR-off and owned-mask restoration paths are retained.

Figure quality uses the game's actual LOD renderer tables, not invented proxy bodies or
randomly hidden limbs. The supplied Frame figures already use LOD 2 at the observed distances;
this cap cannot promise another saving there. Optional cloth solver suspension provides a
separate CPU lever. Held local/remote figures restore original quality and native cloth.
Town NPCs, previews, cards and shared widgets are excluded. No gameplay callback, bone,
material, cloth coefficient or collider is changed.

## Procedural generation scope

The read-only finding in [STEAM-FRAME-NATIVE-DETAIL-601.md](STEAM-FRAME-NATIVE-DETAIL-601.md)
shows that Standalone Fastest still requests high Apparance geometry. Only native
`ProceduralMapTile.WriteExtraParameters` / `ProceduralWall.WriteExtraParameters` receive a
transient copy of the effective per-quest profile with `_qualityLevel=0`. Underground and
every unrelated field remain unchanged. Unscoped callers, including native detail providers,
receive their original objects; no native `SetActive` detail-disable path is invoked.

The choice freezes per scenario scene through all later reveals. Live changes apply on the
next scene load instead of regenerating current gameplay geometry. Scene teardown releases
the transient copies. Prefix/finalizer scopes preserve nesting and propagate native exceptions.
The proven board/gaze Apparance focus remains unchanged. Source proves retained inputs,
not the black-box native graph's exact resulting floors/walls or renderer saving.

## Validation and hardware acceptance

Focused source-linked proof: portable scenery classifier 51 assertions/eight negative
controls; complete actual Unity scenery driver 66 assertions/11 negative controls;
generation override 19 Unity assertions/six negative controls. The complete figure driver
adds42 Unity assertions/eight negative controls, including original-settings idle bypass,
local/remote holds, foreign native tables, orphaned fine meshes, native cloth resets and
bounded fault restoration. Its separate42 source-boundary assertions run in hosted CI.
Full final integrated gate results are recorded in STATE.md after completion.

Hardware checks: load/reveal/retry the large scenario at the same 3408 eye target; compare
all three scenery values 100 versus 0 at the same view. Verify essential floor/wall continuity,
obstacles, doors, pickups, targeting and both clients' local/remote cards. Compare figure
detail 100/0 and cloth on/off, including local and remote grabs. Reduced generation requires
a new scenario load for each A/B. Check 2D-map/original-window mode and immersive3D-map mode;
these scenario compromises must not alter either map UI or NPC presentation. Report actual
mask/LOD/cloth counts and loaded FRAME/SPLIT windows; passing automation is not headset acceptance.

## Storage maintenance

After NPC Build 602 publication todev, the requested obsolete-run cleanup freed 399.61 GB net,
leaving 458.34 GB available before new tests.264 inactive workers were archived/removed and
all 1,494 prior branch refs retained. Hardware logs, final NPC evidence and original paid/reference
assets were protected. Exact actions/hashes and the documented initial editor-sublog filter
correction are in `.planning/debug/storage-audit-20261002/README.md`.
