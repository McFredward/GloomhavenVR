# Town-service runtime asset authoring

This work adds three static-location, skinned service actors and authored service furniture.
It is independent of the runtime controller, native menu callbacks and multiplayer protocol.
The first hardware build still needs headset acceptance; editor images do not establish VR quality.

## Runtime contract

Bundle paths are lower-case when looked up through `AssetBundle.LoadAsset`:

- `assets/bundle/townservices/prefabs/townmerchant.prefab`
- `assets/bundle/townservices/prefabs/townpriestess.prefab`
- `assets/bundle/townservices/prefabs/townenchantress.prefab`
- `assets/bundle/townservices/prefabs/townworktray.prefab`

Each station root is at floor zero. `Actor` is 1.75 metres tall in its original bind pose,
stands at `(0, 0, 0.65)` and faces Unity `-Z`. `InteractionAnchor` is `(0, 0.95, -0.42)`;
`HeadAnchor` is `(0, 1.56, 0.65)`; `ServiceSurface` is `(0, 0.94, 0)`.
The counter/altar/workbench occupies approximately 1.50 x 0.96 x 0.72 metres and has one
BoxCollider. The runtime decides station placement and visibility; there are no gameplay
MonoBehaviours, native controllers, callbacks, lights, or native menu clones in these assets.
Candle flames are small emissive meshes, not light sources.

`Actor` has a legacy `Animation` component. Its default clip is `Idle` (4 seconds, looping).
`Greeting` (2.4 seconds), `Gesture` (3 seconds) and `ReturnToIdle` (0.8 seconds) clamp at
completion; the runtime should cross-fade back to `Idle`. These names are stable runtime
contracts. Three `SkinnedMeshRenderer` LODs share the same skeleton and four-influence
linear skinning. The rig has root, hips, spine, chest, neck, head, clavicles, upper arms,
forearms, hands, three phalanges per digit, thighs, shins and feet. This is a small authored
station rig, not a humanoid retargeting or full locomotion system.

`TownWorkTray` is 0.44 x 0.32 metres. Its leather contact surface and `ContactAnchor` lie
at local origin; the wooden base extends downward. The tray has no legs, colliders or
callbacks. It can be scaled/positioned by the runtime without moving its contact plane.

## Reproduction

1. Run `prepare-npc-assets.py` for the selected authored GLB of each NPC into sibling
   `merchant`, `priestess` and `enchantress` directories.
2. Run the rig authoring script for each directory:

```bash
/home/claw/blender-4.2/blender --background --threads 4 --python-exit-code 1 \
  --python scripts/rig-town-npcs.py -- \
  --input-dir /absolute/prepared/merchant \
  --output-dir /absolute/rigged/merchant --name merchant --render
```

3. Create a fresh temporary Unity 2021.3.5 project, copy `BuildTownServices.cs` into
   `Assets/Editor`, and invoke:

```bash
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics \
  -projectPath /absolute/temporary-project \
  -executeMethod GloomhavenVR.TownServicesBuilder.Build \
  -townPreparedRoot /absolute/prepared -townRigRoot /absolute/rigged \
  -townEnvironmentTextures /absolute/repo/unity/GloomhavenVR.Assets/Assets/Bundle/Environments/Imported/Textures \
  -quit -logFile /absolute/town-assets-build.log
```

4. Inspect renders and import evidence, then copy only the generated
   `Assets/Bundle/TownServices` directory and its metadata into the companion Unity project.
   The builder does not rebuild the production bundle. The integrator owns that step.

Source GLBs, 500k source meshes, packed `.blend` files, rig reports and render evidence remain
in gitignored authoring directories. They are not runtime assets. Prepared provenance points
back to the selected source hash; rig provenance hashes the preparation manifest.

## Authored deformation and evidence boundaries

The source meshes have many duplicated UV seam vertices. Coordinate-based weights make
those duplicates deform identically, rather than treating UV islands as separate bodies.
Each NPC has manually chosen shoulder/elbow/wrist heights and widths; depth is fitted from
local source samples. Smooth body/arm capsule boundaries exclude hip bags and the hanging
cape from arm motion. Every vertex is normalized to at most four influences, matching
Unity linear skinning. The script preserves source topology, UVs and texture imagery.

The rest animation lowers upper arms by 17 degrees and bends forearms forward by seven
degrees. Breathing uses small chest and neck rotations. Greeting combines a head nod and
one forearm movement; the service gesture adds a modest arm presentation and at most two
degrees of authored digit motion. No mouth animation, lip sync, eye tracking, facial blend
shapes, arbitrary grasp poses or large finger curls are claimed. Digit landmarks remain
proportional estimates; bounded gesture evidence must be reviewed before widening motion.

The initial merchant render exposed hip-bag deformation and partially fixed hand vertices.
Those defects were corrected before acceptance by fitting bone depth and excluding positions
outside the arm capsules. Subsequent front and oblique merchant idle/gesture renders show
relaxed arms, intact bags, and coherent wrists/hands. Equivalent final-source renders and
Unity import checks for all three actors are recorded after the complete build.

## Furniture material provenance

The wood and stone albedo/normal textures reuse the project's existing Poly Haven CC0
`dark_wooden_planks` and `monastery_stone_floor` sources. Their original acquisition/licensing
record is `Assets/Bundle/Environments/License.md`. Furniture geometry is authored by the
builder; no downloaded model or native game mesh is copied into the stations. Other small
surfaces use explicit Standard PBR values. NPCs use their selected authored base colour,
normal and metallic/smoothness maps. Unity Standard backface culling is retained; the
selected GLB's double-sided flag is not a promise of a custom double-sided runtime shader.
