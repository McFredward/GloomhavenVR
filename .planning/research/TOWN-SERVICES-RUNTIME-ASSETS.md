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
   `Assets/Editor` and `TownNpc.shader` into `Assets/Bundle/TownServices/Shaders`, then invoke:

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
   The builder does not rebuild the production bundle. `GloomhavenVR.TownServicesBuilder.BuildBundle`
   explicitly builds only this folder into `Build/TownServices/ghvr-town.bundle` for
   StandaloneWindows64 with TypeTrees enabled. The resulting bundle must stay below 100 MiB.
   The separate name avoids the legacy loaders' broad `Contains("gloomhavenvr")` selection.
   The integrator excludes TownServices from the original bundle's asset collection.

Source GLBs, 500k source meshes, packed `.blend` files, rig reports and render evidence remain
in gitignored authoring directories. They are not runtime assets. Prepared provenance points
back to the selected source hash; rig provenance hashes the preparation manifest.

## Authored deformation and evidence boundaries

The source meshes have many duplicated UV seam vertices. Coordinate-based weights make
those duplicates deform identically, rather than treating UV islands as separate bodies.
Each NPC has manually chosen shoulder/elbow/wrist heights and widths; depth is fitted from
local source samples. Smooth body/arm capsule boundaries exclude hip bags and the hanging
cape from arm motion. Every vertex is normalized to at most four influences, matching
Unity linear skinning. Before reduction, the derivative source welds coincident vertices within 1e-6 metres.
Face count, corner count, material assignments and UV corner values must remain unchanged;
imported corner normals are explicitly restored. The untouched archive/normalized source
retains its original vertex identities. This avoids independent reduction of disconnected
UV islands, which had caused actual micro-cracks in the first LOD candidates.

The rest animation lowers upper arms by 17 degrees and bends forearms forward by seven
degrees. Breathing uses small chest and neck rotations. Greeting combines a head nod and
one forearm movement; the service gesture adds a modest arm presentation and at most two
degrees of authored digit motion. No mouth animation, lip sync, eye tracking, facial blend
shapes, arbitrary grasp poses or large finger curls are claimed. Digit landmarks remain
proportional estimates; bounded gesture evidence must be reviewed before widening motion.

The initial merchant render exposed hip-bag deformation and partially fixed hand vertices.
Those defects were corrected before acceptance by fitting bone depth and excluding positions
outside the arm capsules. Subsequent front and oblique merchant idle/gesture renders show
relaxed arms, intact bags, and coherent wrists/hands. Final-source renders for all three actors and Unity's 45 pose-time samples (each checked
on all three LODs) pass with stable actor placement/orientation. The final UV-connected
LODs have exactly 80,000 / 30,000 / 10,000 triangles for each character. Unity comparisons
confirmed that welding before reduction removed the micro-cracks; Cull Off alone did not.

## Furniture material provenance

The wood and stone albedo/normal textures reuse the project's existing Poly Haven CC0
`dark_wooden_planks` and `monastery_stone_floor` sources. Their original acquisition/licensing
record is `Assets/Bundle/Environments/License.md`. Furniture geometry is authored by the
builder; no downloaded model or native game mesh is copied into the stations. Other small
surfaces use explicit Standard PBR values. NPCs use their selected authored base colour,
normal and metallic/smoothness maps. The self-contained `GloomhavenVR/TownNpc` surface
shader uses Unity's Standard metallic lighting, Cull Off, and face-oriented normals,
matching the selected GLB's double-sided flag. Furniture and tray materials also use this
bundled shader; they do not depend on resolving Unity's built-in Standard shader at runtime.
NPC textures remain 4096 x 4096. Standalone import explicitly selects BC7 base colour,
BC5 normal XY data and DXT5 metallic/smoothness data; source PNGs remain unchanged.
The four repeated furniture wood/stone maps import at 1024 x 1024.
Animation key reduction uses 0.01 rotation/position/scale error settings. Comparison
against uncompressed clips over 108 LOD0 pose-time samples measured a maximum baked
vertex displacement of 0.0001247331 m (0.125 mm), below the 0.5 mm validation bound.

`_TownVisibility` defaults to one and supports the runtime's shared appearance/disappearance
transition through object-space noise clipping. Zero clips every fragment; one skips noise
sampling. The same surface clip also applies to shadow passes. This adds no scene geometry,
background or lighting override.

Unity's surface compiler supplies stereo-instancing and instance-ID setup automatically,
without custom vertex overrides. Materials enable instancing so the applicable variants
are retained. References: [Unity 2021.3 single-pass instancing](https://docs.unity3d.com/2021.3/Documentation/Manual/SinglePassInstancing.html)
and [surface shader instancing](https://docs.unity3d.com/2021.3/Documentation/Manual/gpu-instancing-shader.html).
A successful compile and mono editor render do not establish headset stereo correctness.

## Final offline verification

- A fresh merchant static-package smoke preserves the 499,470-triangle source and creates
  UV-connected 80,000 / 30,000 / 10,000-triangle derivatives. Both Python scripts compile.
- Unity imports all three final rigs, checks 50 bones and all four correctly named clips,
  and bakes 45 pose-time samples on all three LODs with finite bounds and unchanged actor
  placement/facing. The saved prefab starts in Idle frame zero, avoiding an initial A-pose.
- Mono editor renders include idle, greeting and gesture front/oblique views for all three
  stations, plus face/hand close views. Normal-free, clay and unlit controls isolate remaining
  shading/material transitions. Some fine straight facial/material seams and faint priestess
  wrist seams remain visible in strong close views; these are not claimed repaired.
- `_TownVisibility` rendered 0 visible pixels at zero, 93,051 at 0.5 and 99,640 at one
  in the isolated tray control. All furniture and actor materials use the shared shader.
- These checks establish asset import, bounded deformation and mono rendering evidence,
  not headset comfort, stereoscopic correctness, or complete finger articulation quality.
