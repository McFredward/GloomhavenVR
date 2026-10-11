# Build619 original asset detail coverage

## Hardware evidence and source defect

Both supplied Frame sinks identify Build618. The later scenario reports ten bound
actors and `Ambient FX density=0%`, but zero admitted optional effects. Native
Living Spirit identities occur in the log and the renderer census still submits
`VFX/BendyGlowPlane_Shd`. The previous implementation admitted only Wind, Sun,
Night and Flame demon idle branches. It could not admit a Living Spirit's
`P_Living_Spirit_Idle (1)` or its three mesh-rendered eye bands. This is a source
coverage defect, independently of the unmeasured FPS cost of those effects.

## Complete original census

The read-only UnityPy census traversed all 188 shipped `hero_`/`npc_` addressable
bundles and 190 exported character model hierarchies. That includes ordinary and
elite enemies, bosses, playable classes and summons. It found 818 resident
particle systems and resident mesh/trail effects in the original model trees;
standalone attack, loot and condition prefabs elsewhere in those same bundles
do not acquire model ownership.

The scenery census also read all 129 **nested** PCG database bundles and the
material bundles: 2,144 bundles in total, with 9,438 distinct mesh/object
identities. A top-level-only bundle glob misses the actual PCG databases and
would misleadingly report only 28 names. Original source hashes and names are
retained in `NativeDetailProvenance.json`; no artwork or mesh vertices are copied
into the tracked fixture.

Reproduce the original census with the game's read-only references available:

```bash
/home/claw/unitypy-venv/bin/python scripts/audit-scenario-detail-assets.py \
  --output .planning/debug/frame619-details/original-assets.json \
  --provenance-output tests/GloomhavenVR.ScenarioSceneryBudgetTests/NativeDetailProvenance.json
```

## Reversible implementation

The figure cosmetic classifier now has 58 exact original model identities and
144 original model/effect pairs. It covers heroes, summons, bosses, ordinary and
elite monsters rather than a particular hardware scenario. Seventeen exact
mesh/material pairs add resident eye bands and summon decorative meshes; original
drake trails use the same resident effect ownership. Living Spirit has 13 native
particles and three eye-band meshes in each ordinary/elite model.

The classifier does not infer optional geometry from a shader name, transparency
or `loop=true`. Original model ownership, an authored resident branch, and exact
mesh/material identity are required. Independent opaque bodies, cloth, horns,
weapons, lights, colliders, card/UI descendants and separately attached native
combat/condition effects retain their native presentation. One explicitly ranged
release child in the Sightless Eye prefab is conservatively retained rather than
classified as idle cosmetics. The source census is not a promise to remove all
rendering at zero: gameplay presentation and core silhouettes remain.

Five original resident insect/summon systems have collision modules. Their
optional renderers can be masked while their native simulations and callbacks
continue. Safe resident systems are paused with their clocks/buffers preserved;
none is stopped or cleared. Full density, VR teardown and scene exit restore
only rendering/simulation writes actually owned by the budget. Originally
stopped/paused systems and foreign masks are preserved.

All captured native actors receive the policy, including inactive children,
material completion, newly bound actors and local/remote held originals. No
camera, network record or game state is changed. A bounded Debug-only
`FIGURE FX COVERAGE` receipt names actual admitted models and distinguishes owned
renderer masks from paused solvers (maximum twelve per scene).

The scenery classifier now recognizes the original PCG biome/DLC families and
previously missed long grass, foliage, moss and geranium leaves. Loose clutter,
scatter, debris, small stones, containers, candles/books and banners still pass
the existing full structural, gameplay identity, generation and collision checks.
Moss names do not make a solid wall optional. Native props and interactive trees
remain protected. Pure optional placement can use the existing creation deferral;
mixed floor/wall composites keep their structural members and mask only admitted
decoration. Normal discovery, async material completion and inactive room reveal
all use the same classifier. Every budget at 100 restores original rendering.

`ScenarioSceneryBudget.IsPreparingPresentation` and
`ScenarioFigureDetailBudget.IsPreparingPresentation` expose actual queued
presentation work to the loading coordinator. Permanent ancestry monitoring,
delayed diagnostics and settled idle actors are not considered loading.

## Verification and limits

- Independent original-asset metadata and ownership source guard: 412 assertions.
- Portable complete scenery classifier: 115 assertions and 18 negative controls.
- Real Unity 2021.3.5 scenery driver: 208 assertions, all 40 production/negative
  variants pass. This includes actual all-biome masking/restoration, native prop
  and solid wall protection, and loading readiness draining.
- Real Unity figure driver: 142 assertions. Original-identity Living Spirit mesh
  cosmetics are drawn by an actual graphics device and leave zero cosmetic
  pixels at zero density; ordinary/elite variants, eleven additional figure
  families, core bodies/lights, late materials and full-quality restoration are
  checked. Four additional negative controls remove the Spirit catalog, ignore
  mesh cosmetics, pause native callbacks, or accept an unknown mesh material.
- The complete figure attempt executes all 28 variants. One legacy expected
  failure string encountered the new, earlier Living Spirit restoration check;
  its mutation correctly failed restoration. Only that expectation was updated
  and only that negative case rerun; it passes its bounded resume. Production
  source and passing cases were not rerun for this fixture-only correction.

Receipts (including the first failure and resume, source hashes, native census,
assemblies, logs and pixel captures) are retained under
`.planning/debug/frame619/worker-details/` in the main checkout. The integrated
primary gate is separate. These establish admission, ownership and rendered
pixels in the harness; they do not establish headset picture quality or an FPS
gain in a new hardware run.
