# Preserved doorway fire attachment, Build 621

## Evidence and cause

The supplied Gaming PC logs identify Build 620. The front doorway braziers in
`flammen_problem.jpg` have no flames while their frame remains visible. The
same frame carries normal fire in `viel_deko2.jpg`. These pictures establish
the visible discrepancy; the native ownership lines establish its cause.

The original `TO_INT_Stone_Doorway_02_FRAME_Split_PR` owns
`CR_St_WallTorch_Fire` and `CR_St_WallTorch_Fire (1)`. Their nested
`fx_sparks_drop`, `distort` and `fx_sparks (1)` appear in the supplied log as
**mounted dressing of Wall 5 at fade 1.00**. The persistent arch rectangle ends
at z=-9.9; the torch emitters sit at z=-9.8. The old emitter-point membership
test therefore misses an actual arch attachment. This is not a missing effect
or a shader-resolution hypothesis.

## Original asset scope

Read-only inspection covers 17 original PCG databases, 53 prefab instances and
33 doorway/effect family identities. The whole-game renderer census from Build
620 located these families; the original databases were then read again to
inspect their complete native hierarchies. The checked-in
`scripts/arch-mounted-runtime/NativeArchMounts.json` records original paths,
mesh identities and database SHA-256 values.

Three physical holder meshes occur in those families: `EN_CR_WallTorch_01`,
`CR_INT_Wall_Candles_01` and `CR_INT_Wall_Candles_02`. There are 29 primitive
doorway families with 49 actual frame mesh pairings. Four wider entrance/exit
families contain the already listed `TO_INT_Wood_Shack_Doorway_01_Split_PR`
primitive; the wider containers do not acquire blanket protection. Vermling's
frame is nested under the original `ST_Vermling_Door_Split` node. Original
`CandleFlame` mesh layers may be siblings of their physical holders rather
than particle descendants.

## Implementation

An effect now inherits the existing arch exception through its actual native
holder/frame ancestry or an inspected primitive's own original frame child.
The live physical holder/frame must still pass the existing persistent arch
rectangle predicate. The rule does not enlarge that rectangle or exempt an
unrelated nearby fire. Larger gatehouse features are not attachment roots.
Ordinary wall-mounted fire keeps its original animated fade.

The mounted admission gate applies this verdict, and sticky carry restitutes
an already adopted effect before carrying it for the neighbouring wall again.
Queries read current parents, meshes and protected-frame geometry at each
ownership decision. They retain no cross-frame Unity references or verdicts.
The existing measured geometry cache remains scoped to one synchronous wall
publication. The ordinary mesh path exits before the new ancestry queries;
the existing classified name is reused there.

No wall material, dissolve shader, fade ramp, light, gameplay callback, network
record or authoritative state changes. Local and remote wall decisions use
the same renderer attachment rule and preserve their existing shared fades.

## Validation and limits

`python3 scripts/check-arch-mounted-runtime.py` compiles the production
attachment helper, original arch membership geometry, original geometry reads
and the actual new sticky-restitution branch. Unity 2021.3.5 executes native
transforms, meshes, renderer bounds, particle modules and light objects.
Gameplay marker components and the restitution write are explicit inert test
boundaries. The measured hardware rectangle/emitter positions are replayed
using bounded stand-in geometry; original game shader bytecode and headset
pixels are not executed by this fixture.

The production replay passed **134 assertions**, including all 49 original
primitive/frame pairings, all four nested composite families, the actual
Vermling route, sibling candle layers, both clone/numeric suffix orders,
ordinary neighbouring walls, missing/foreign holders and frames, actor
ownership, absence of an arch rectangle, reparenting and untouched native
particle/light values. **Nine causal controls** cover missing primitive
recognition, broad frame-name matching, foreign mount/frame acceptance,
ignored live geometry, omitted candle layers, omitted actor veto, omitted
sticky restitution and loss of the original Vermling nested route.

The first fixture build stopped on an unassigned `LastSeen` boundary field;
the next stopped on an unreachable constant mutation. Both raw failures are
retained. One initially broad negative mutation failed too early rather than
at its intended unknown-root case; it was replaced by the actual unsafe
`_FRAME` substring admission mutation. The complete production/eight-control
run then passed; subsequent added family coverage and the ninth control ran
as a bounded production/nested-route resume. No compilation failure is counted
as causal evidence and no previously passing unrelated suites were repeated.

The unchanged wall-read suite passed **19,504 assertions and 28 controls**;
maintenance tracing passed its original helper and four controls. Release
builds passed with zero warnings/errors. Light-stabiliser source checks and
six controls, and all 11 locked frame orderings, passed.

Evidence is in the worker's `.planning/debug/frame621-archflames/`, including
input hashes, bounded hardware ownership snippets, complete native audit,
original failures, source hashes, raw Unity results and focused build logs.
The primary agent retains the final integrated gate separately. The next
hardware check should verify all flame layers on retained doorframes while
ordinary wall-mounted flames still follow their own wall, locally and in
multiplayer. No new headset outcome is claimed.
