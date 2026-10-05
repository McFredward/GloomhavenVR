# Environment budget runtime proof

`python3 scripts/check-environment-budget-runtime.py` reads the current production
driver and simplified environment shader from `--source-root` on every invocation.
It compiles the complete driver and private mesh-bank decoder, imports the actual
shader and runs them in Unity 2021.3.5 with a graphics context. Selected read-only
original geometry exports are imported as fixture TextAssets; the original game
bundles remain untouched. Native scene classes,
Harmony registration, settings, registry collection and logging are explicit
boundaries; all meshes, materials, particle solvers, transforms, cloning, rendering
callbacks, renderer flags and pixels are actual Unity objects or operations.

The generated scene covers:

- Native generated scenario scope, including excluded actor/UI/held/interactive
  roots, previews, mod clones, foreign scenes, water, animated foliage, emissive
  surfaces and wall dissolution. Immediate native floor identity must pass the full
  production `WallFloorTile` geometry rule; ancestor names, raised platforms and
  pillar feet cannot qualify. Ordinary opaque trim retains its native shader.
- Native ambient families, combat/condition and one-shot exclusions, zero density,
  paused solvers and preservation of preexisting foreign masks or pauses.
- Both the start and completion of native material loading restore source draw
  leases before native visibility/material writes. Real pre-cull chunk/instance
  submissions are revoked, original material references restored, and the shared
  idle lease-recovery delegate runs before the loader hides its source. Native
  addressable requests are an explicit boundary; renderer writes are actual Unity.
- Exact original texture/tint/UV properties and material references; native in-place
  material completion; foreign replacements; unknown native clones restored before
  disposing an owned material variant.
- Forty-eight surfaces sharing two originals validate each material once per real
  camera invocation. A native keyword/property edit between camera invocations
  restores every affected surface; an individual property block and foreign material
  remain independent. Only a read-entry counter is added to the complete production
  compatibility method. Warmed storage is reused and verdict references clear after
  each invocation; allocation claims additionally require a calibrated live counter.
- The independent shared-read option returns to 48 complete material validations
  when Off, then back to two when On. Verified private meshes preserve actual native
  positions, normals, UVs, indices and bounds. Unknown metadata, stale bounds,
  corrupted private streams and mismatched original/prepared SHA256 reject a substitute.
- Actual unreadable original floor meshes create exact private chunks. The bank option
  Off restores original rendering; the new path never changes native shared meshes,
  native material slots or static-batch metadata.
- Native supplementary vertex streams and existing Unity internal batch geometry
  retain original submissions. Shifted supplementary positions alter real native
  non-instanced camera pixels; admission and a late stream edit both keep them.
  The GL backend's automatic-instancing stream behavior is not a pixel oracle;
  that path proves intact native references/masks and absence of private commands.
- Original lit per-object light/reflection probes retain individual draws. Only
  probe-free renderers may use a combined chunk. The simplified shader still reads
  spherical-harmonic ambient lighting, so it does not bypass this native safeguard;
  its material compromise remains available without combining those draws.
- Real bounded explicit instance commands preserve pixels and multiple material
  submeshes. Independent option Off, lightmaps, motion flags and newly active wall
  channels restore originals. A native renderer write after actual pre-cull submission
  clears and detaches queued commands before releasing native source masks.
- Nested cameras keep separate revocable command buffers. The consumer query compares
  exact native buffer identities because Unity returns new managed wrappers. Original
  source cloning during rendering and after interrupted rendering runs the production
  Apparance prefix and retains the original nonempty mesh/material slots and draw flags.
- Completed-camera counters report two actually substituted sources and one active
  group, while late-revoked instancing reports zero source savings. An actual foreign
  camera command buffer reports the fallback reason and keeps native sources.
- Native floor pixels before/after combining, same-render fallback after visibility,
  mesh, transform, material array, property block and common render-flag changes.
- Render-only leases, real balanced and nested camera callback execution, original
  `Object.Instantiate` slots/mesh/flags between cameras, early interruption recovery
  and actual `MonoBehaviour` disable handling.
- Readability, LOD, baked-lightmap, reflected transforms and foreign-mask skips;
  bounded node traversal and two-chunk normal updates; 55 same-cell sources crossing
  the 24-member portion limit; complete loading-time drain; affected-only material
  invalidation and dead-source ledger cleanup.
- Imported production shader support and actual finite tinted pixels, alpha cutout
  and a shadow-caster pass. Lighting quality on native game artwork is not inferred
  from a surrogate's generated texture.
- The successful native material-repair edge is bound after native material/enabled
  assignment and before continuation; removing that edge is rejected. An injected
  resolver failure executes the production API guard and confirms native continuation
  survives while optional preparation stops and reports its fault once.

Fifty-two negative controls mutate actual production source and must compile successfully
before failing the intended runtime assertion. The source SHA-256, generated source,
case assemblies, manifest and logs remain in the selected output directory. The
fixture is snapshotted and hashed before compiling any variant, so concurrent
fixture edits cannot change later cases within the same run. Unity
import caches and duplicate API references are not retained.

`--production-only` and repeatable `--case NAME` select focused development runs;
their output explicitly says `PARTIAL PASS`. The final registered local gate uses
the default complete suite. Native game scenes, OpenXR output, multiplayer pictures
and actual headset performance remain hardware evidence.
