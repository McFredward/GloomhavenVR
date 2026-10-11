# Frame 615: wall publication reads and structural render submissions

This addresses ranked items 1 and 5 in [FRAME-612-ANALYSIS.md](FRAME-612-ANALYSIS.md).
It does not claim an isolated headset frame-time gain. The 612 evidence has twelve
loaded-play wall commits averaging 158.45ms; ten follow scene signature movement,
two the safety age ceiling. Bounds and mounted rebuilding dominate those commits.

## Actor particle lifetimes

Only an actual `ParticleSystemRenderer` with its own `ParticleSystem`, underneath
an active native `ActorBehaviour`, is exempt from the three wall signature folds.
The exact original `FootstepSound.MakeFootstepSound` calls `ObjectPool.Spawn` with
an authored foot transform as parent and recycles after its lifetime. This is a
native actor effect, not scenery. Actor-owned particle creation, activation and
recycling need not publish a new identical wall table.

Water protection and real wall-fade shader renderers retain their existing folds.
Detached world/projectile particles, inactive actor owners, unknown effects and
ordinary actor meshes stay conservative. Ownership is queried live to respect
pool reparenting. Destroyed rows preserve the exemption captured while alive.
This changes signatures only; it does not hide, pause or replace native effects.

## Exact reads during synchronous publication

`WallCommitGeometryReads` memoizes only `MeshRenderer.bounds` during the existing
synchronous `RescanCore` transaction. The same exact answer feeds wall bounds,
mounted ownership and the other commit consumers. The transaction writes material
properties and visibility, but does not change original transforms, meshes or
local bounds; native Update cannot interleave it. Skinned and particle bounds
remain live. Prepare, drift and per-frame fade reads remain live outside the
transaction. A finally clears every retained reference even after failure.

The existing shared-wall-read setting controls this cache. The bounded Debug
BUDGET report now distinguishes actual native mesh reads from repeated same-commit
answers, including when the cache is disabled. The Unity fixture demonstrates
500 identical mesh queries crossing the native boundary once instead of 500
times. This is not a measurement of the hardware commit reduction. Atomic
publication and reveal/drift/safety checks remain intact; no interval was raised.

## Structural chunks: an explicit optional compromise

`ConfigureStructuralBatching(Func<bool>)` configures the new structural option
before `ScenarioEnvironmentBudget.Install`. It is separate from the existing
floor-batching option and requires simpler environment shading. PC defaults are
Off, fresh Frame defaults On. The cheaper shader remains the explicit quality
compromise: original albedo UV/tint/world projection survive, native normal/MRAO
and detail work do not. Native object-space shader effects are never interpreted
on a combined structural mesh.

Only an enumerated authored masonry mesh identity qualifies. A native scenario
and generated tile must own it; one opaque N_MRAO material, readable one-submesh
geometry, a positive transform, no MPB, no LOD/lightmap, and matching render flags
are required. Actors, hands, gameplay props, doors, Canvas, animators, rigidbodies,
preview/mod children, water, foliage, active wall channels and unknown identities
stay native. Ancestor text and generic `Wall` labels never grant admission.

Small groups stay within the same native tile, material, layer, render/probe flags
and four-metre xyz cell. They contain at most 24 originals and 48,000 vertices.
Original GameObjects, MeshFilters, exact original mesh references, colliders,
transforms and native cull/door identity remain unchanged. A separate chunk is a
render substitute; no original acquires Unity's internal static-batch state.
Original sources are masked only inside paired real camera rendering callbacks,
with early interrupted-lease recovery before native content creation.

Every camera revalidates native visibility, transform, mesh, material, MPB and
render flags. Cameras with command buffers retain ordinary original sources.
An actual colored `CommandBuffer.DrawRenderer` proof targets the original native
renderer identity. A negative control that masks these sources is rejected.
Before all 22 wall MPB writes, both material swaps and both enable primitives,
synchronous release drops a structural substitute and restores its owned material.
Native material-loader and `ShowContent` prefixes release before their writes.
An optimization failure restores originals and cannot gate quest continuation.
Off/shutdown restore only owned values and preserve foreign changes.

The original bundle audit finds 13 admitted readable masonry mesh records in
`pcg_crypt`, 13 in `pcg_ancientcaverns`, and 11 matching cave records of which ten
are unreadable. Cave unreadable originals deliberately remain unbatched. These
are asset metadata counts, not runtime eligibility or visible draw counts.
Native material loaders fill deferred prefab slots; the live driver still checks
the completed actual material, effects and scope. No broad cave saving is promised.

The engine fixture extracts both original native `EN_CR_Pillar_Thin` (1440
vertices) and `EN_CR_Pillar_Large` (1595) from the shipped crypt bundle. Two
*different* native mesh identities combine into one 3035-vertex substitute;
original mesh/collider identity and rendered pixels remain exact under the same
explicit simpler material. This avoids an already-instanced identical-mesh test.
It establishes feasible submission reduction for eligible groups, not a GPU
counter or measured Frame gain. Runtime unreadable/MPB/cull exclusions may leave
few groups in a particular scenario.

## Native GPU instancing flag experiment

`ScenarioStructuralInstancing.Install(host, enabled)` is also available as an
independent optional experiment. It requires exact native shader support, an
eligible generated native scope and repeated mesh/material pairs. It changes only
an originally disabled material instancing flag; already enabled and foreign
flags are preserved and owned writes restore on Off/shutdown. It never creates a
renderer substitute. Unity's ordinary per-renderer/MPB draw fallback stays intact.

Original compiled shader metadata proves `INSTANCING_ON` programs in the high/low
N_MRAO and WallFade variants. The actual Frame scene already reports 4194 material
slots enabled, so this flag is Off by default on both PC and Frame. Its benefit
may be zero. Non-instanced MPB properties can also prevent draw aggregation;
see Unity's [GPU instancing shader documentation](https://docs.unity3d.com/2021.3/Manual/gpu-instancing-shader.html).
The GL shader fixture is an explicit API surrogate, not original game shading.

## Focused evidence and remaining hardware measurement

- Wall read-facts production: 19,486 assertions, 21 existing mutation controls.
- Actual Unity overlay/ownership/geometry production: 693 assertions; all 22
  controls passed across bounded runs. The original SpittingDrake rig and sleeping
  ghost pixel proof remain part of this fixture.
- Environment whole-driver production: 204 assertions, including original native
  masonry geometry, identical pixels, render leases and colored native command
  draws. All 22 causal controls passed; only failed bounded cases were resumed.
- Native instancing whole-driver production: 23 assertions and four controls;
  separate original compiled-shader provenance is retained.
- Strict Release: zero warnings/errors. The final integrated gate also passes;
  see [the complete validation record](FRAME-615-IMPLEMENTATION.md#integrated-validation).

Small receipts, failed-run explanations, native producer source/hash, original
bundle/shader provenance and final results are preserved in the main checkout's
`.planning/debug/frame615-review/walls/`. Disposable Unity Library/Temp caches
are removed by the runners. Headset commit read counts, effective chunk counts,
actual eye draw calls and frame time still require the next hardware run.
