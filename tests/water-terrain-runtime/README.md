# Water publication and owned shader runtime

The focused suite compiles complete production `WaterTerrainVR`, water constants,
reflection classification and the actual shipped WaterVR shader/include. It runs
in the game-exact Unity 2021.3.5 editor with real renderers, material instances,
HarmonyX method interception, inactive objects, bounds and probe ownership.

The central publication path is source-bound to the current CoreModule callback,
ScenarioEnvironmentBudget publication method and original ready postfix. The
pinned native `MaterialLoaderData.CheckAllMaterialLoaded` method executes unchanged
against fixture-controlled loader storage. An available read-only decompilation
is compared byte-for-byte; CI uses the checked pinned method and provenance. The
original game assembly, asynchronous Addressables provider and procedural placement
controllers are explicit boundaries, not claimed as executed native controllers.

All 45 exact native water renderer paths and 14 material identities from the
whole-game catalog are exercised, including colors, queues and normal maps.
Cases cover empty initial slots followed by native completion, incomplete native
loads, later-slot and in-place shader overwrites, inactive preview rooms, late
room placement, mixed opaque slots, destroyed renderers, bounded event overflow,
restoration and re-entry. Eight compiled defects must fail their causal assertions.
The suite does not establish headset appearance, FPS or multiplayer acceptance.

Run `python3 scripts/check-water-terrain-runtime.py`. Worker checkouts can select
read-only integration and catalog inputs with `--integration-root` and `--coverage`.
`--case` restricts a rerun to affected variants. Every completed run calls the
shared retention cleanup API after reaping its processes; logs, manifests, source
copies and failure evidence remain available.
