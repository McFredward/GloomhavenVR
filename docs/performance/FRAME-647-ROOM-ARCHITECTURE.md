# Build647: broader simplified 3D rooms

**Historical647 experiment; withdrawn in648 after a measured Frame regression.**
The active renderer and asset bank are restored to646. New647 controls are INERT
saved-value storage. See [rollback review](../../.planning/FRAME-648-REVIEW.md). The following records647's experiment and proof limits.

The maintainer approved proposal2 on 2026-10-08: expand the existing simplified
room presentation across native floors and architecture in the whole game and
DLCs, with substantial geometry/detail compromises that remain independently
adjustable. Persistent whole-room rendering state and single-pass stereo were
not selected. The same PC/Frame binary implements every choice.

## Why expand coverage

The immutable Build645 Steam Frame capture remains slow after loading completes.
Seven closed-options windows contain 1,068 frames over approximately100.4 printed
seconds: weighted frame time94.107ms, named mod parent scopes58.847ms. These are
both-eye totals, not GPU busy time. World materials23.928ms, wall late work7.813ms,
scenery6.579ms, terrain5.637ms and environment1.351ms are measured parent costs.

Discovery visited5,058 mesh renderers but masked238 decorative sources. Terrain
prepared115 sources, explicitly refused2,418 floors and substituted approximately
three sources per eye in the last window. Environment combined22 sources into
eight groups. Prepared membership is not simultaneous visibility or draw calls.
Existing geometry controls therefore reached only a small portion of the room.
The archive and exact input hashes are in the main checkout's gitignored
`.planning/debug/frame645-radical-audit/`; no new headset capture accompanies647.

## Independent quality choices

| Optimize setting | Fresh PC | Fresh Frame / Standalone | Visible trade |
| --- | --- | --- | --- |
| `ScenarioRoomArchitecture` | Off | On | Enables wider verified room geometry and optional architectural dressing |
| `ScenarioRoomFloorDetailPercent` |100 |0 | Less fine floor relief; retained 3D elevation, footprint and boundaries |
| `ScenarioRoomArchitectureDensityPercent` |100 |0 | Fewer positively admitted noninteractive complete ornaments |
| `ScenarioRoomFloorBatching` |Off |On | Private local floor groups; different group bounds/per-object lighting selection |
| `ScenarioRoomFloorCameraSourceLimitCount` |0 |0 | Independent CPU/geometry budget for individual floors;0 means unlimited |

Existing wall/pillar near and distant detail percentages also apply to newly
verified structural families when the room mode is on. Five bank-proven genuine
floor-outline/support families share floor detail and its source budget while
remaining outside floor-core grouping; furnishing shelves keep native fades. The old wall-substitution
switch retains its legacy scope independently. The existing per-eye wall/pillar
limit remains64 for fresh low profiles; floors have a separate budget and cannot
consume it. Grouped floors do not consume the individual floor budget.

Saved configuration wins. A graphics profile writes values once; later individual
edits remain authoritative. All controls are present in both languages in VR
Options. Turning off the room master retains the subordinate saved values. No
resolution, stereo path, gameplay rules or multiplayer packet layout changes.

## Geometry and ownership

The offline catalog audits every original MeshFilter use in every PCG source
bundle, including unsafe reuse. Exact source metadata plus native bundle hashes
authorize only unambiguous floor, structure or ornament identities. Native
actors, doors, interactables, props, unknown controllers/effects and ambiguous
families retain original rendering. No broad runtime name matching authorizes
the new role.

Prepared derivatives retain original vertex indexing, channels, submesh slots,
actual 3D bounds and open seams. Floors additionally check coverage,
holes and heights on a sampled grid, including three intermediate morph positions.
Fine relief changes within the original tolerance; unsampled tiny features are
not established by those certificates. Unsafe or
ineffective derivatives retain the exact original. This is still native 3D room
geometry with original textures; it does not flatten the board or erase rooms.
Geometry quality transitions reuse the continuous native-facing morph path.

Native meshes, colliders, transforms, material arrays and Unity static-batch
metadata remain authoritative. Individual terrain proxies copy current native
renderer state and renderer-wide/indexed property blocks. Never-fade floors stay
separate from the wall fade system, including admitted floor-outline supports.
Remaining structural surfaces retain continuous native wall/pillar/shelf fading.

Settled floors can hand their private bank mesh to small environment groups.
Groups live outside native cloning roots. Every source retains its original
renderer and collider. Unknown property blocks, additional vertex streams, LOD,
custom lighting, native command-buffer consumers and current scope/held-object
changes refuse grouping. Visibility, pose, material/effect or morph endpoint
changes restore native rendering before culling. An original floor material with
a live fade channel requires an owned floor-aware world variant; its private
group receives floor-only never-fade markers. Shared source material properties
are never changed to disable walls globally.

## Universal work removal and diagnostics

Repeated exact mesh-role/ancestry/material reads share one synchronous invocation
only. Nested calls, native writers and later eyes invalidate the mutable read
scope. No persistent cross-frame room-state rewrite was introduced. Settled
floors release their otherwise unused private morph mesh and three vertex arrays;
a later quality change allocates a new continuous transition. These work/memory
reductions have no player options.

Debug counters distinguish surviving individual floor and structural leases from
prepared membership. Environment counters separately report completed floor
source/group counts and original/submitted triangle totals. These counts include
both-eye camera invocations and do not prove frustum-visible GPU execution,
draw-call reduction or FPS.

## Verification and next hardware comparison

The integration review records exact native catalog coverage, asset-bank hash,
causal controls, complete local gate and compiled change scope. Automated Unity
fixtures verify actual geometry submission, masks, native cloning, shader pixels,
material/MPB fallback and ownership boundaries; native gameplay scene controllers
and OpenXR headset images remain outside those fixtures.

Use the complete Build647 archive, including its rebuilt environment bank, on
both VR peers. Compare the same fully loaded multi-room view with VR Options
closed. The room master supplies a reversible loaded-view comparison; individual
floor detail, grouping, dressing and source limits isolate their respective
costs. Check room outlines/elevations, floors through active wall masks, animated
pillars/shelves, interactive props and camera/view changes. Hardware frame time
and rendering acceptance remain open; asset triangle reductions are not an FPS
claim.
