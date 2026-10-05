# Environment budget runtime proof

`python3 scripts/check-environment-budget-runtime.py` reads the current production
driver and simplified environment shader from `--source-root` on every invocation.
It compiles the complete driver, imports the actual shader and runs both in Unity
2021.3.5 with a graphics context. No game assets are copied. Native scene classes,
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
- Exact original texture/tint/UV properties and material references; native in-place
  material completion; foreign replacements; unknown native clones restored before
  disposing an owned material variant.
- Forty-eight surfaces sharing two originals validate each material once per real
  camera invocation. A native keyword/property edit between camera invocations
  restores every affected surface; an individual property block and foreign material
  remain independent. Only a read-entry counter is added to the complete production
  compatibility method. Warmed storage is reused and verdict references clear after
  each invocation; allocation claims additionally require a calibrated live counter.
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

Negative controls mutate actual production source and must compile successfully
before failing the intended runtime assertion. The source SHA-256, generated source,
case assemblies, manifest and logs remain in the selected output directory. Unity
import caches and duplicate API references are not retained.

`--production-only` and repeatable `--case NAME` select focused development runs;
their output explicitly says `PARTIAL PASS`. The final registered local gate uses
the default complete suite. Native game scenes, OpenXR output, multiplayer pictures
and actual headset performance remain hardware evidence.
