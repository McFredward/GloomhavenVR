# Private terrain runtime fixture

Run `python3 scripts/check-terrain-budget-runtime.py`. The complete focused suite
compiles the actual three `ScenarioTerrainBudget` production source files and
imports the actual `ScenarioCheapTerrain.shader` into an isolated Unity 2021.3.5f1
project. Its CPU-side boundaries are explicit substitutes for native scene types,
configuration, held props/hands, logging, shader loading and bank lookup.
The original synthetic cases remain; five additional wall cases read independently
verified native structural streams through the actual production mesh decoder. Actual Unity meshes, transforms, renderers, materials, MPBs, cloning,
command buffers and `Camera.Render` callbacks execute; there is no simulated
renderer or pixel oracle substituted for the production fragment.

The pillar-retirement production case makes370 assertions. Retired combinatorial pillar selectors are removed rather than counted as current behavior. Independently compiled negative
variants each corrupt a production statement and must fail at a named assertion. Shader compilation failures and unrelated exceptions never count as
passing negative controls. `--production-only` and repeatable `--case NAME` are
partial development runs; selected cases always retain the production baseline.
Use `UNITY_PATH` or `--unity` for an editor override.

## Geometry, scope and ownership

The fixture starts with CPU-unreadable native wall/floor mesh sources, a separate
readable original bank and a prepared same-index coarse 3D derivative. It proves
an actual reduction in submitted endpoint triangles, preserved original topology
during the continuous transition, bounded progress across a hitch, independent
near/distant options and restoration of full geometry near a tracked hand.
Coarse geometry retains its native material when cheap wall shading is disabled.
A prepared tier without any actual triangle saving retains the native renderer.

Floors retain native geometry/materials under the combined controls. FloorHex
names are exercised beneath both tile and wall owners. Bank membership alone
cannot admit anonymous geometry or foundation slabs; a vetted wall name beneath
only a map tile cannot replace explicit native wall ownership. Actors, late
interactables, held sources, disabled renderers, foreign masks and foreign mesh
replacements retain their native route. Native clones retain their original
material slots, lack private proxy children and never acquire internal static
batch state; native colliders remain unchanged.

Actual pre-cull observations distinguish a live source lease from post-render
cleanup. Production triangle/cheap counters now count only surviving paired
post-render leases; a late native write is excluded from those counters. These
remain camera admissions, without a frustum/occlusion or GPU draw claim. A later native renderer write immediately disables an already-prepared
proxy. An actual `host.SetActive(false)` followed by `Camera.Render` retains every
native pixel and acquires no source masks; reactivation resumes paired leases.
The causal guard variant replaces `isActiveAndEnabled` with `enabled`, reproducing
the missing wall pixels because static callbacks survive host deactivation while
the host's private proxies cannot render. Per-slot MPB precedence, live native keyword edits and resolution of an
existing environment material variant back to the genuine native original are
checked. Every native renderer-state value is read fresh for each camera, including
live layer/probe/shadow/sorting/motion edits. Identical private renderer, mesh,
material-array and transform writes are skipped. An explicit invocation observer calls the unchanged actual Unity pose setter;
this observer and a causal always-rewrite variant prove that a parked proxy does
not repeatedly call it. Unity `hasChanged` alone was insufficient because
identical setters leave that flag clear. This measurement boundary is stated
in the compiled harness and is never added to shipped production.
Material route reuse expires with that one camera; a cross-camera-stale variant
must fail the native keyword edit. Per-renderer and per-slot MPBs remain fresh.
Live renderer-wide vertex and per-slot emissive MPB gates immediately
retain the native shader and geometry. Native command-buffer consumers veto substitutions; an explicitly
known mod buffer is recognized by its nonzero native handle plus camera/event,
while an additional foreign buffer retains the native renderer identity.

Temporary bank readiness retains pending discovery without replacing sources.
Terminal failure clears discovery, preserves native continuation and reports
one bounded ordinary-level fallback. A later ready bank reseeds the scene.
A shader-resolver failure restores originals and reports once.

## Detached LOD renderer ownership

The wall material reader is extracted directly from `WallSegmentFade.ReadFacts.cs`.
Real prepared terrain proxies publish no canonical material to the wall collectors:
the source renderer remains their sole decision/fade/visibility owner. Otherwise a
world shader resolving back to `Amp_Basic_WallFade` can enroll those detached
proxies as another shared wall under the mod host, independently of the native
wall that is actually clear. This applies to desktop and standalone alike.

Exact registry membership is tested for a live actual proxy, its native source,
a name/material/layer-identical foreign clone and removal before deferred Unity
destruction. No hierarchy scan or name-based exemption is introduced. The two
`terrain-proxy-ledger-*` controls remove registration or disposal respectively.
The complementary wall-read-facts suite executes the real cold classifier,
shader-adoption input and structural signature; removing the exact-owner guard
must fail its named duplicate-adoption assertion. Ten thousand private reads
perform no native material-list query in that counted fixture. A separate actual
Unity reader microbenchmark records host CPU time without using it as a speed
assertion or a complete LOD/GPU/headset FPS claim.

## Independently verified captured structural definitions

`verify-native-coverage.py` reads the original read-only `pcg_city.asset.bundle`,
checks its committed SHA-256 and mesh metadata, and independently exports every
original channel/index byte for five CR_INT ordinary wall definitions occurring in
the Build627 PC capture. The exact bank streams must match byte for byte; the
coarse streams must match the committed full digests. Authored prefab references
prove structural uses and also reveal identical meshes inside under-wall and
doorway/entrance/exit templates. Those ancestor boundaries remain native, even
under a live ProceduralWall; actual late reparent/name changes execute this veto.
No blanket CR_INT/Wall/Pillar name admission is permitted.

Every definition executes actual unreadable original geometry, multi-slot
materials, independent cheap/geometry switches, continuous morphing, nonempty
3D endpoint pixels, untouched native collider/mesh/slots, per-slot native-effect
veto, late-write counter revocation and native cloning during a camera. Cloned
originals retain their original camera silhouette. New causal variants remove
captured coverage, source-template boundaries or completion-counter revocation
and must fail their named assertions. Native material/art are explicit fixture
boundaries; original geometry/provenance is independently native-source-bound.

The earlier mixed10-definition totals are historical; current admission excludes
all pillar identities. The original audit history is preserved in
[the retirement record](../../.planning/retired-PILLAR-LOD-20261019.md).

## Pixel evidence and independent native samples

The real cheap shader renders into a floating-point target. An actual bilinear
gradient map and eleven cutoff values exercise the native LOW endpoint and
multiple intermediate pictures. The floor safety channel is sampled at a
fully-faded endpoint. Both original HIGH routes are compared pixel by pixel
against a separate explicit native clip-equation boundary with fixed noise,
including enable, foundation, vignette and cutoff inputs. Causal shader mutations
remove each of these required effects and must fail the pixel assertions.

The production simplex is separately executed on the GPU for all twenty literal
vectors in `native-noise-vectors.json`. Expected values were calculated by an
independent scalar float32 evaluation of the original DXBC instruction stream
identified by the stored disassembly SHA-256. They are not generated from the
new shader. The time-zero world samples include the native 6/7/10 frequency scale;
the fixture tolerates small GPU float/FMA differences. Replacing simplex with
zero must fail those assertions.

## Frame636 synchronous reads and property blocks

Actual production primitive-access observers call the unchanged Unity operation
and count it. For 96 prepared sources, one Update reads the current head position
and scale once and each tracked hand position once. The next Update freshly
observes tracking loss and changed hand scale. Bounds and original-mesh guards
remain per-source. A settled empty-block camera retains 96 fresh native presence
guards with no block reads, effect reads or private block writes. Late native
color/texture blocks, removed renderer/index blocks and live vertex/emissive
effects execute through actual Camera.Render callbacks. A separate direct helper
case checks both legacy/world floor never-fade channels without admitting floors
to production geometry substitution. Causal variants corrupt each optimization
or freshness/safety boundary. See
[the terrain CPU follow-up](../../docs/performance/FRAME-636-TERRAIN-CPU.md).

Each run retains the compiled cases, shader copies, source/fixture hashes,
results, editor log, actual exit code, native-coverage/provenance receipts and endpoint PNGs beneath the worktree's
gitignored `.planning/debug/terrain-budget-runtime/`. The native game bundle and
raw original shader bytecode/art are never checked in.

## Evidence limits

Lookup and native procedural ownership remain explicit boundaries. The initial
synthetic cases are supplemented by ten independently verified original bank
definitions; the environment-bank suite separately covers the complete package. This suite
does not execute original procedural controllers, Windows shader bytecode,
original game textures, network state or headset presentation. The software GL
graphics device is appropriate for engine/pixel contracts and cannot establish
Frame GPU costs, multiplayer performance or a correct headset picture. The
integrator must run the affected integration checks required by AGENTS.md and the maintainer must compare
the independently adjustable controls on hardware with every room revealed.

## Retired pillar LOD

`RetiredPillars.cs` runs all10 formerly admitted native pillar definitions through
the actual terrain owner with zero detail caps and cheap-wall shading enabled.
No private proxy may be prepared or acquire a camera lease. Native meshes,
materials, colliders, enabled visibility and foreign masks remain unchanged.
The `retired-pillar-admission-restored` control restores one former allowlist
entry and must fail the actual proxy-count assertion.

`verify-pillar-coverage.py` independently verifies each original stream against
its native read-only bundle and requires that only tier100 remains. That exact
stream is still shared by the independent environment chunk renderer. No pillar
50/0 derivative remains in the prepared bank or actual packaged bank. The older
radial/size-selection tests are retired together with their implementation;
[their recorded proof and failures remain in history](../../.planning/retired-PILLAR-LOD-20261019.md).
