# Frame 606 implementation and verification limits

This build implements the approved follow-up to [Frame 605](FRAME-605-ANALYSIS.md).
The maintainer clarified that discarded flat draws must always be suppressed in VR;
`[WorldUI] DesktopMirrorLeftEye` controls only left-eye presentation versus a black
spectator screen. It is no longer an optimization enable switch. Headset menus and
native capture cameras retain their own reversible rendering path. Fresh Frame entries
default to black; saved choices remain intact.

## Reusable work without a presentation compromise

- Hex selection visuals are recognized from native owner references, not broad names.
  Their creation/removal no longer invalidates an otherwise unchanged wall census. The
  605 log's 322 ms rescan was triggered by exactly that selection-only path.
- Hidden scenery omits unnecessary wall rendering preparation but retains structural
  occlusion facts and restoration. Held local/remote props rescue only their own roots.
- Pure wall section geometry and diagnostic labels are prepared once. Child hierarchies
  may be reused only within the publication frame. Board or room changes invalidate old
  derivations. Genuine scene changes still require the atomic publication transaction;
  this build does not claim to have eliminated its remaining cost.
- UI clipping/projection reuses values only inside the same measurement pass. A bounded
  64-entry signature ring covers the observed 21 panels without the previous eight-slot
  churn. Live content, clipping changes, animation and native continuation remain visible.

## Reversible quality choices on every platform

| Key in `[Optimize]` | PC default | Fresh Frame entry | Behavior |
|---|---:|---:|---|
| `ScenarioStaticBatching` | false | true | Small compatible floor draw chunks; original gameplay/collision objects retained. |
| `ScenarioSimpleEnvironmentShading` | false | true | Original floor albedo, tint and geometry with simpler lighting; omits normal/metallic detail and received realtime shadows. |
| `ScenarioEnvironmentEffectsDensityPercent` | 100 | 0 | Original decorative moth, candle and torch particle families only; lights and combat/condition effects retained. |
| `ScenarioPlayerFigureDetailPercent` | 100 | 0 | Authored LODs plus prepared simplified player body meshes. |
| `ScenarioEnemyFigureDetailPercent` | 100 | 0 | Authored LODs plus prepared simplified enemy body meshes. |

Existing saved values are retained. New keys use their platform defaults when absent.
The quality options are local rendering choices explicitly requested by the maintainer;
native actor animation, room reveal, picking, combat effects and multiplayer state are
not removed. The 2D map and original flat-window mode keep their content/capture paths.

### Floor chunks differ from the removed built-in static batching

The historical rejection in `.planning/static-batching-removed.md` concerned Unity
static-batch state and lost material slots on Apparance source clones. This implementation
uses manually combined render substitutes, never `StaticBatchUtility` or native batch
state. Original meshes and material arrays remain populated. Sources are masked only
between camera pre-cull and post-render; interrupted renders recover before native Update.
A native source can still be instantiated with nonempty materials and normal render flags.
The simpler shader has an explicit original-material ledger, including unannounced native
clones; disabling the option restores their genuine original shader references.

Floor identity is taken from the immediate native mesh/node/material, plus the existing
floor-plate geometry verdict in tile coordinates. An ancestor name alone is insufficient.
Restricting substitutions to floor cores keeps wall-dissolve saved-material ledgers separate.
Both native material completion and successful `MaterialLoaderHeal` direct finishes publish
the same safe preparation edge.

Chunks exclude actors, held props, native interactables, animated geometry, water,
foliage, active wall dissolve, lightmapped/unreadable meshes and per-renderer effects.
Groups preserve layer and render flags. Native visibility, mesh, material, property-block
or transform changes fall back to originals before culling. Stable chunks remain resident;
new normal-play chunks publish at most two per update. Loading completion drains pending
preparation. Unreadable sources remain original and are counted rather than silently
advertised as batched. `EnvironmentBudget.PreCull` measures the validation overhead.

### Actual coarse body meshes

Original authored LODs could not reduce nine of the seventeen 605 actors. Offline constrained
edge collapse now prepares original meshes for three tiers while preserving survivor
attributes, UV seams, material boundaries, skin weights and bindposes. Strong native keys
include bounds/index counts, full bindposes and native readability; ambiguous source
signatures are rejected. A missing derivative retains the original. Native cloth meshes
remain untouched even while cloth simulation is disabled.

The catalog has 1,191 derivatives from 409 original sources in 49 parts (294,017,137 bytes).
Runtime opens only current actor/tier parts during preparation or an explicit setting
change; grabs use cached meshes. Banks are finite immutable caches for process lifetime,
so extant local/remote ghost copies never borrow destroyed assets. At 100% native automatic
LOD and exact source mesh references are restored, including already-existing ghosts.
The packagers and archive header gate verify the entire indexed set.

## Evidence and next hardware comparison

Focused tests execute production code and real Unity camera/mesh operations; deliberately
broken variants must fail. They cover native clone slots, chunk pixel equivalence, reversible
materials, room/state fallback, bounded preparation, source rig/attribute preservation,
rendered native silhouettes, changed-bone deformation and existing ghost restoration.
They establish those tested contracts, not headset performance or compositor pixels.

Use the same scenario/view after loading has finished. For an existing profile, verify the
three new environment keys above; compare enabled/disabled cases without changing eye
resolution or view simultaneously. Test 0/100 player/enemy detail, ordinary/held/ghost
figures, room reveal, vegetation restoration, PC mirror On/Off, VR original windows and a
remote observer. Read prepared environment counts and actor vertex counts alongside frame,
logic and render times. GPU busy time is still unavailable in the supplied 605 recording;
XR wait/interval measurements must not be labeled GPU busy time.

## Final integrated validation

Runtime commit `9f267b98`: 14/14 source gates, 101/101 local suites with eight jobs
(631.5 s), 286,760 wire/golden assertions, every bundle/index header and strict Release
with zero warnings/errors. Runner manifest coverage and log hashes were independently
verified. Actual Unity environment/desktop/native-figure proofs pass 185/32/82 assertions
and 19/11/6 runtime defect controls respectively, plus the material-repair binding control.

The compiled comparison against preserved reviewed605 has 21 intended existing behavior
and config types, eight types with only the inlined build increment, six new helpers and
an intentional managed serialization reference. No type or embedded resource was removed.
The historical guard's summary exits 1 for these reviewed changes; all subordinate gates
passed. The final archive is 446,748,051 bytes; its safe unique paths, DLLs, index, 49 parts
and main banks were verified byte-for-byte against the tested inputs. Windows installer
bank validation also ran under actual PowerShell with deliberately invalid index/parts.

Complete logs, exact source/compiled/archive hashes and compact worker evidence remain in
`.planning/debug/frame606-final-validation/` and `.planning/debug/frame606-worker-evidence/`.
These checks do not establish a headset picture or a measured FPS improvement.
