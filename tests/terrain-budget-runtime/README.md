# Private terrain runtime fixture

Run `python3 scripts/check-terrain-budget-runtime.py`. The complete focused suite
compiles the actual two `ScenarioTerrainBudget` production source files and
imports the actual `ScenarioCheapTerrain.shader` into an isolated Unity 2021.3.5f1
project. Its CPU-side boundaries are explicit substitutes for native scene types,
configuration, held props/hands, logging, shader loading and the verified mesh
bank. Actual Unity meshes, transforms, renderers, materials, MPBs, cloning,
command buffers and `Camera.Render` callbacks execute; there is no simulated
renderer or pixel oracle substituted for the production fragment.

The production case makes 100 assertions. Thirty independently compiled
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
cleanup. A later native renderer write immediately disables an already-prepared
proxy. Per-slot MPB precedence, live native keyword edits and resolution of an
existing environment material variant back to the genuine native original are
checked. Live renderer-wide vertex and per-slot emissive MPB gates immediately
retain the native shader and geometry. Native command-buffer consumers veto substitutions; an explicitly
known mod buffer is recognized by its nonzero native handle plus camera/event,
while an additional foreign buffer retains the native renderer identity.

Temporary bank readiness retains pending discovery without replacing sources.
Terminal failure clears discovery, preserves native continuation and reports
one bounded ordinary-level fallback. A later ready bank reseeds the scene.
A shader-resolver failure restores originals and reports once.

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

Each run retains the compiled cases, shader copies, source/fixture hashes,
results, editor log, actual exit code and endpoint PNGs beneath the worktree's
gitignored `.planning/debug/terrain-budget-runtime/`. The native game bundle and
raw original shader bytecode/art are never checked in.

## Evidence limits

The verified-bank boundary in this fixture is synthetic; the environment-bank
suite separately proves the generated asset streams and provenance. This suite
does not execute original procedural controllers, Windows shader bytecode,
original game textures, network state or headset presentation. The software GL
graphics device is appropriate for engine/pixel contracts and cannot establish
Frame GPU costs, multiplayer performance or a correct headset picture. The
parent must run the complete integration gate and the maintainer must compare
the independently adjustable controls on hardware with every room revealed.
