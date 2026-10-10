# Private terrain runtime fixture

Run `python3 scripts/check-terrain-budget-runtime.py`. The complete focused suite
compiles the actual three `ScenarioTerrainBudget` production source files and
imports the actual `ScenarioCheapTerrain.shader` into an isolated Unity 2021.3.5f1
project. Its CPU-side boundaries are explicit substitutes for native scene types,
configuration, held props/hands, logging, shader loading and bank lookup.
The original synthetic cases remain; ten additional cases read independently
verified native structural streams through the actual production mesh decoder. Actual Unity meshes, transforms, renderers, materials, MPBs, cloning,
command buffers and `Camera.Render` callbacks execute; there is no simulated
renderer or pixel oracle substituted for the production fragment.

The production case makes 7,021 assertions. One hundred six independently compiled
negative variants each corrupt a production statement and must fail at a named
assertion. Shader compilation failures and unrelated exceptions never count as
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

## Independently verified captured structural definitions

`verify-native-coverage.py` reads the original read-only `pcg_city.asset.bundle`,
checks its committed SHA-256 and mesh metadata, and independently exports every
original channel/index byte for ten CR_INT wall/pillar definitions occurring in
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

The new ten asset definitions total 3,472/2,088/1,180 triangles at 100/50/0.
These are asset-definition counts, not scene-instance coverage or FPS. The
allowlist grows from fifteen identities/twelve present bank definitions to
twenty-five identities/twenty-two present definitions, without changing assets.

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

## Radial pillar proximity

All ten positively admitted crypt/cave/city pillar definitions are independently
read from their native bundles by `verify-pillar-coverage.py`, in addition to the
original ten captured city structural cases. Every original channel/index byte
and complete exact/coarse digest is checked. Eight orbit angles per pillar
execute the actual private owner and compare exact endpoint topology and rendered
silhouette pixels. Synthetic cases separately reproduce the former square-AABB
distance defect, retained near/far settings, head yaw, offset bounds, tilted and
nonuniform source parents, late cap-deferred native fallback and foreign masks.
Existing wall-hide/recovery, animation and camera-lifecycle cases remain.

The correction reads a pillar's current source matrix during the existing Update.
Without tracked hands this replaces the native bounds getter; tracked hands keep
their unchanged native bounds touch guard as well. Actual primitive observers
verify these counts. A warm paired primitive benchmark binds the actual proximity
method once outside timing and compares 21 alternating batches of 512 calls with
the former Unity bounds/closest-point path. Delegate/observer overhead is included
in this diagnostic. The separate two-source complete Update timer is diagnostic
only. Neither timer establishes Steam Frame frame rate or GPU cost. Native LOD
controllers and far source-cap/frustum selection are unchanged.

## Independent size-based pillar LOD

Each admitted pillar uses its own original full enclosing-cylinder radius and
its own radial VR-camera distance. The fixture additionally compiles the pure
production `FigureDistanceLodPolicy.FirstReductionDistances` and the unchanged
`Select`, alongside the source-extracted actual figure mesh-bank tier selector.
All ten native crypt/cave/city pillar definitions compare actual settled topology
with the same effective tier bands at caps 0, 66, 67 and 100. Probes immediately
inside/outside the entry and exit edges distinguish even modest differences
between original sizes; eight radial orbit positions, head yaw and two board/head
scales retain their own size-derived result. Nonempty native/private shader
readbacks preserve the selected silhouette, and distant endpoints must save
actual original triangles. A fixed common radius is rejected independently of
the former fixed .75m distance and horizontal-thickness controls.

No actual figure, player configuration or party-published reference supplies a
pillar threshold. Runtime cases enable/disable figure LOD and add/remove an
unrelated actor without changing the selected pillar detail. The complete
CoreModule source is bound to the receipt and checked for the absence of the
former proposed provider bridge. Ordinary walls and native ownership remain
unchanged. Manual mode retains the saved viewing radius, with live mode and cap
edits. Original-size rotation/nonuniform scale, exact binary entry/exit equality,
invalid captured original bounds, and fully hidden/native-disabled sources are
covered. Invalid original radii preserve exact geometry and clear the far latch.

Actual primitive observers establish one current source matrix during Update,
zero extra native bounds reads for an untracked pillar and zero geometry/radius
reads during either camera callback. The full radius uses that same existing
matrix; the manual path skips its optional scalar square root. Original geometry
and the existing shader/bank lookup are explicit fixture boundaries; headset
presentation and GPU/FPS effects remain unverified. The twelve added controls
must fail their exact named runtime assertions, retaining all 94 prior controls.
`--figure-source-root` and `--integration-root` support frozen worker input;
their exact bytes are hashed and checked for stability with every other input.

The initial 106-variant independent run passed production and 104 of 105 controls.
Its new figure-toggle coupling control escaped because the selected far position
was coarse under both manual and independent modes. The final positive compares
inside the pillar's own near band and outside the manual radius, distinguishing
the two policies. That control is rerun alongside the new zero-height fallback
control and the existing invalid-size control, with complete final production.
The original stable receipt is retained; combined coverage is composed, not a
claim of an uninterrupted final 107-variant run. Zero native height retains the
existing manual distance fallback but supplies no size radius, preserving exact
geometry in independent mode instead of borrowing an angle-dependent world AABB.
