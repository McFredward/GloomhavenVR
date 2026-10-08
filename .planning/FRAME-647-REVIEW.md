# Build647: comprehensive verified 3D room simplification

The maintainer approved proposal2 on 2026-10-08: expand the existing simplified
room presentation across the game and DLCs. This integration starts at the
current Build646 `dev` commit510b1ab6c and retains its NPC and map-entry repairs.
Visible quality compromises remain individually configurable; pure repeated
work and unused memory removal are universal. Developer behavior and the five
controls are described in [the room architecture note](../docs/performance/FRAME-647-ROOM-ARCHITECTURE.md).

## Hardware evidence and implementation scope

The immutable Build645 Steam Frame audit contains1,068 fully loaded frames in
seven closed-options windows. Weighted frame time is94.107ms, with58.847ms of
named mod parent work across both eyes. World materials23.928ms, wall late work
7.813ms, scenery6.579ms, terrain5.637ms and environment1.351ms identify recurring
costs, not independent GPU busy time. Discovery visited5,058 scenery renderers
but masked238. The terrain path refused2,418 floor sources, prepared115 and
substituted approximately three per eye; environment combined22 sources into
eight groups. These are preparation/submission counters, not visible draw calls.

The existing hardware archive is
`.planning/debug/frame645-radical-audit/hardware-inputs.tar.gz`, SHA256
`d89a3daf9d601d67ee7f071194621e90595ba6dec58dc89f2eb08976ec74b397`.
No Build647 headset capture accompanies this implementation. A closed-menu
fully loaded multi-room hardware comparison remains necessary.

The offline census covers all129 original PCG bundles and8,864 native MeshFilter
uses. Safe positives occur in61 bundles, including13 DLC bundles. All uses of a
shared identity are checked; seven ambiguous identities retain original rendering.
The index contains1,434 exact originals:133 floor,531 structure and770 protected
legacy families. The strongest certified structure derivatives cover505 families
and reduce510,018 to60,903 triangles (88.1%). The strongest floor derivatives
cover68 families and reduce14,157 to4,907 triangles (65.3%). Other originals
remain exact when no safe useful derivative exists. These numbers compare each
asset once and do not estimate a scenario's visible total or FPS.

The rebuilt environment bank contains4,040 immutable geometry streams and4,043
loadable assets, occupies70,975,207 bytes and has SHA256
`cfcc2dee4121e7ab1a2881cb033dd5fb50fada00797d527d347b7bd4dc8d3116`.
The independent Unity loader decodes every stream through the production reader:
3,296,386 vertex slots and1,741,930 triangles across all included tiers. This
includes exact originals and multiple derivatives, not one frame's geometry.

## Rendering and presentation contracts

All geometry stays three-dimensional. Floor footprint, holes, height, actual
bounds, open seams, submesh slots and original attribute channels are certified.
Floor morph certificates include three intermediate positions. Native meshes,
colliders, transforms, gameplay controllers, room visibility, doors and actors
remain authoritative. Geometry transitions use the existing continuous morph.
Original floor-outline supports retain their never-fade contract while sharing
the structural geometry budget. No static-batch metadata is rewritten.

Room floors use an independent per-eye source budget;0 means unlimited. They
cannot consume the existing wall/pillar cap. Settled floors can delegate their
private endpoint to small local environment groups outside all native cloning
roots. Grouping restores original rendering for native visibility, pose, material,
property-block, light/probe, supplementary-stream or geometry changes and native
command-buffer consumers. Room-floor, legacy-floor and structural groups have
distinct ownership keys even when they share materials and spatial cells.
Original material arrays never acquire a group-wide never-fade state.

The world material owner audits complete current shader/effect state. Wider floor
admission can use all supported game/DLC shader families without the obsolete
cheap-shader-only gate. Actual native renderer-wide/indexed property blocks are
checked through the existing complete world effect contract; unsupported effects
retain native rendering. Safe tint/texture blocks remain on individual proxies.
Groups conservatively refuse all source property blocks. Saved LOW floor wall
flags require the owned floor-aware material and private never-fade markers.

Optional architecture removal uses a closed21-signature catalog and complete
safe decoration units. It requires a live retained bank-proven floor/structural
core, refuses unknown/gameplay/held/animated/light descendants and never disables
native colliders. Structural walls, pillars and shelves retain their continuous
native fading. Native core witnesses use bounded existing rescue checks rather
than a scene-wide per-frame scan. Actual runtime coverage is reported separately
from the candidate catalog.

Scoped mesh-role, ancestry and material reads share only one synchronous
invocation. Nested cameras, native writers and recovery invalidate mutable reads.
Settled floors release their unused morph mesh and three vertex arrays; later
quality changes start a fresh continuous transition. These reductions have no
options. The five new options describe actual geometry, decoration, grouping or
CPU/geometry budget compromises; PC defaults remain full room detail and Frame/
Standalone defaults enable the stronger room choices in the same binary.

## Verification ledger

Final integrated results and exact compiled scope are appended after the full
local gate. Focused worker and initial integration receipts are retained with
their source hashes; they are not substituted for that gate.

Independent controls have already caught real implementation/fixture defects:
an early floor test inspected restored post-render state instead of the camera
lease; the corrected rendered observation exposed the mutation. Legacy terrain
material retirement wrongly invalidated delegated floors. A mixed-ownership key
could merge legacy and room-floor contracts. These are repaired and covered.

The first private Unity pack appeared successful but omitted its AssetBundle
container because the minimal project lacked the built-in asset-bundle module;
its incremental cache preserved the invalid output. The builder now requires the
module, forces a fresh rebuild and actually reloads every named asset. Only the
independently decoded corrected bank above is eligible for integration. Failed
receipts are preserved alongside final results.

Actual Unity fixtures execute production geometry, shader pixels, masks, cloning,
camera callbacks and ownership. Native floor/structure cases are bound to original
base-game and DLC asset bytes. Reconstructed original decoration graphs and
material/config/world-owner adapters remain explicit boundaries; native gameplay
controllers, Windows shader execution and OpenXR images are not established by
these fixtures. NPC646 production paths are unchanged; their dedicated suites
are included in the final local catalog.

The next hardware run installs the complete647 archive, including the new bank,
on both VR peers. Compare the same fully loaded view with VR Options closed,
then use the room master and separate floor/group/detail controls to isolate
costs. Check floor holes/elevation, room visibility, continuous wall/pillar/shelf
fades, doors, held props and graphics changes. No FPS guarantee follows from
asset reduction or a successful automated gate.
