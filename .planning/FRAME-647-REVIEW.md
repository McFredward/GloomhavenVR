# Build647: comprehensive verified 3D room simplification

**Historical647 experiment; withdrawn in648 after a measured Frame regression.**
The active renderer and asset bank are restored to646. New647 controls are INERT
saved-value storage. See [FRAME-648-REVIEW.md](FRAME-648-REVIEW.md). The following records647's experiment and proof limits.

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
cover80 families and reduce15,643 to5,855 triangles (62.6%). Across all133
floor families with original fallbacks,53,962 becomes44,174 (18.1%). An independent
representative native16-floor set reduces18,288 to16,768 (8.3%); only two
originals in that set have useful safe floor derivatives. Eight representative
native DLC structures reduce36,255 to6,725 (81.5%). Other originals
remain exact when no safe useful derivative exists. These numbers compare each
asset once and do not estimate a scenario's visible total or FPS.

The rebuilt environment bank contains4,057 immutable geometry streams and4,060
loadable assets, occupies70,999,748 bytes and has SHA256
`c3648422dedaf3197ae1738ccbb4eff50aad0491113bbd263855fe6c04ea1802`.
The independent Unity loader decodes every stream through the production reader:
3,297,619 vertex slots and1,742,924 triangles across all included tiers. This
includes exact originals and multiple derivatives, not one frame's geometry.

## Rendering and presentation contracts

All geometry stays three-dimensional. Actual bounds, open seams, submesh slots and original attribute channels are
certified. Floor certificates additionally verify sampled footprint, holes and
height on the existing18-by-18 grid. Fine relief may change within the unchanged
height tolerance; these samples do not prove every unsampled tiny feature.
Floor morph certificates include three intermediate positions. Every original
projected convex-hull edge slot, including collinear points and all heights,
remains fixed; an independent audit caught94/152 earlier derivatives that missed
diagonal rim vertices. All169 final floor derivatives pass the stronger check. Native meshes,
colliders, transforms, gameplay controllers, room visibility, doors and actors
remain authoritative. Geometry transitions use the existing continuous morph.
Five exact bank-proven floor-outline/support identities retain their never-fade
contract and use the floor detail/budget, without claiming floor-core grouping.
FloorShelf furnishing identities retain normal continuous native fading. No static-batch metadata is rewritten.

Room floors use an independent per-eye source budget;0 means unlimited. They
cannot consume the existing wall/pillar cap. Settled floors can delegate their
private endpoint to small local environment groups outside all native cloning
roots. Grouping restores original rendering for native visibility, pose, material,
property-block, light/probe, supplementary-stream or geometry changes and native
command-buffer consumers. Room-floor, legacy-floor and structural groups have
distinct ownership keys even when they share materials and spatial cells.
Original material arrays never acquire a group-wide never-fade state.

The final board-motion review reproduced another loss of floor grouping: private
Core-host chunks used fixed world-relative coordinates and relinquished every
floor lease after a Tile translation. Floor meshes now use Tile-local coordinates;
only the private chunk follows current positive uniform Tile/scenario/host poses.
Every source's independent local-chain matrix remains checked. The mesh object
stays unchanged, cloning roots remain native-only, and unsupported ancestry or
late source writes still restore originals before culling. This is a universal
correctness repair within the optional grouping, with no additional visual dial.
Source-bound runtime and pixel evidence is in
[the floor-motion note](../docs/performance/FRAME-647-FLOOR-MOTION.md).

The world material owner audits complete current shader/effect state. Wider floor
admission can use all supported game/DLC shader families without the obsolete
cheap-shader-only gate. Actual native renderer-wide/indexed property blocks are
checked through the existing complete world effect contract; unsupported effects
retain native rendering. Safe tint/texture blocks remain on individual proxies.
Groups conservatively refuse all source property blocks. Saved LOW floor wall
flags require the owned floor-aware material and private never-fade markers.

New LODGroup ancestors retain native rendering: private proxies outside the
native LODGroup cannot safely infer which enabled original level Unity selects.
Fifteen positive rock identities have native LODGroup use and therefore retain
their native selection. This correctness rule is universal and has no option.

Optional architecture removal uses a closed21-signature catalog and complete
safe decoration units. It requires a live retained bank-proven floor/structural
core, refuses unknown/gameplay/held/animated/light descendants and never disables
native colliders. Structural walls, pillars and shelves retain their continuous
native fading. Native core witnesses use bounded existing rescue checks rather
than a scene-wide per-frame scan. Actual runtime coverage is reported separately
from the candidate catalog. Reconstructed original graphs cover46 contexts,139
original mesh identities and1,242 uses from14 verified bundles. Of62 closed
ornament uses,27 complete units (20,332 original triangles) are additional
admissions and35 retain native presentation for collider/core/script safety.

Scoped mesh-role, ancestry and material reads share only one synchronous
invocation. Nested cameras, native writers and recovery invalidate mutable reads.
Settled floors release their unused morph mesh and three vertex arrays; later
quality changes start a fresh continuous transition. These reductions have no
options. The five new options describe actual geometry, decoration, grouping or
CPU/geometry budget compromises; PC defaults remain full room detail and Frame/
Standalone defaults enable the stronger room choices in the same binary.

## Verification ledger

The complete local catalog ran all177 entries on frozen integrated8bf477053;
174 passed and three failed. The failed receipts remain evidence, rather than a
claimed green177 invocation. The offered-orientation control had an obsolete
pre646 source anchor; its correction removes only the offered-print exemption
and preserves646's independent live-return exemption. The full affected suite
passes production and all nine causal controls. Five new settings also lacked
the separate compact bilingual player-help table, despite having names and full
descriptions; the corrected lookup passes2,296 assertions and all content/lookup
controls. The NPC639 in-flight replacement exceeded its unchanged1s wall-clock
deadline under four-way concurrent execution; its isolated result is recorded
below. No runtime NPC code or latency requirement is changed for that retry.

The late bounded board-pose repair is validated through the entire affected
environment suite after integration. Passing unchanged areas inherit the
completed catalog's exact-source evidence, as permitted by the maintainer's
2026-10-06 bounded-repair clarification. The final ledger distinguishes this
complete coverage with focused repeats from a fresh single green full invocation.

Final coverage on the bounded repaired source a67963fbe:

| Evidence | Result and exact scope |
| --- | --- |
| Frozen complete catalog |177/177 recorded,174 passes and three preserved failures; `.planning/debug/test-runs/20261008-222654-075ea6cf/results.json` |
| Final affected environment |36,880 production assertions and103 causal variants,104 total; `frame647-environment-board-final/run-bbflmdtm` |
| Offered orientation after binding repair |Production and all nine negative controls; floor-motion worktree `frame647-offered-binding/run-3mu_kvrp` |
| NPC639 in-flight replacement |Isolated unchanged1s deadline passes; `frame647-npc639-isolated/results.json`, rendered proof `npc639/proof/run-cpgczxad`; failed four-way receipt retained |
| Bilingual player help |2,296 production lookups,667 bound keys and all content/lookup controls; floor-motion worktree `frame647-player-help/run-n5azro9b` |
| Final source gates |16/16 complete passes,17.6s; `test-runs/20261008-225954-7c2d6e96/results.json` |
| Wire vectors and runner checks |299,714 assertions pass; runner unittest suite passes; no packet grammar/version change |
| Original/DLC terrain |882 production assertions,103 total production/causal variants; inherited unchanged terrain result from complete catalog |
| World material ownership |746 production assertions,64 total variants plus actual shader suite; inherited unchanged complete-catalog results |
| Whole-source asset catalog |129 bundles,8,864 occurrences,1,434 exact originals,169 floor derivatives and four destructive controls; complete catalog passes with morph checks |
| Surface census |658 to663 keys, exactly the five choices;232 Harmony registrations and4,790 log tokens retained; zero removals |
| Bundle formats and docs |All committed bundles retain Unity2021.3.5f1 format7; rebuilt environment70,999,748 bytes; five EN/DE player-doc pairs pass |

The private guard continuation runs source16, fresh wire-project compilation and
golden vectors, runner checks, bundle formats, surface comparison and the normal
Release snapshot. Only the already completed local-catalog invocation and ROOT
resolution differ from the original script; both scripts/hashes and logs are
archived. It is not presented as a second complete177 run. Its final exit1 is the
normal compiled-difference verdict: zero moved types,20 changed and one addition.
All20 changed types were reviewed against the private original646 snapshot:
12 intended configuration/rendering types and eight consumers differing only at
standalone646-to647 build literals. The only added type is
`ScenarioArchitecturalDetailBudget`. Type count1,238 becomes1,239. No NPC runtime
or wire implementation changes occur outside those literal build checks.

Worker evidence is already copied and independently reverified in the main
checkout's gitignored `.planning/debug/frame647/workers/`:

| Archive | Bytes | SHA256 |
| --- | ---: | --- |
| `assets.tar.gz` |113,537,286 |`c60807cc10e566e5b73fe8a3cf15cb6636dec6cecdbf5338f17180697c40acd6` |
| `terrain.tar.gz` |61,439,766 |`4079a9f9552ca1efe0f4883bd4986feb831d42dd42fab969d24329bf1d26bdf1` |
| `scenery.tar.gz` |5,570,887 |`9a0621f38936c535f464b5a010173bd6e6bce1ccff6232dfe06e1990419a5fca` |

The three archives retain182,6,788 and252 verified payloads respectively. The
primary's remaining complete/focused receipts, failed attempts, actual compiled
assemblies and both646/final snapshots are retained in `frame647/root.tar.gz`
with deduplicated SHA-addressed payloads and `root-verification.json`. Archive
verification precedes removal of the five owned647 worktrees. Read-only game
inputs, shared baselines, unrelated agent worktrees and supplied hardware logs
are outside cleanup scope. Final strict main-checkout Debug/Release build and
push receipts accompany the main handoff.

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
