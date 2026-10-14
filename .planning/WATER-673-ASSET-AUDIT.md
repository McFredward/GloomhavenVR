# Build673: complete installed water-surface audit

Base: `dev` at `d6306e146c4d94c431ddadb6b4d1a8d59a177913`, ModBuild672.
The maintainer reported difficult-terrain water still using the broken native
shader and requested coverage throughout the game. This audit is read-only;
no native game data, textures, meshes or shader programs were changed or exported.

## Actual source coverage

The existing complete material census contains 3,286 native sources and 9,197
materials. The focused water audit reparsed all **380 renderer-bearing sources**,
checking their full source hashes and original renderer counts: **57,915 mesh /
skinned renderers**, across 5,313,374,825 source bytes. This includes the native
root scenes and installed base/DLC Addressables content, rather than a list of
previously tested scenarios. The water-material family is fully resolved even
though the broader census also contains 55 deliberately null shader pointers.

All four actual native water shaders are accounted for:

| Shader | Material variants | Bound prefab slots |
| --- | ---: | ---: |
| `VFX/Water_Shd` | 2 | 6 |
| `VFX/Water_Shd_Low` | 2 | 0 authored; corresponding LCM assets exist |
| `VFX/Water_Shd_Trans` | 5 | 39 |
| `VFX/Water_Shd_Trans_Low` | 5 | 0 authored; corresponding LCM assets exist |

The 14 materials are CaveWater, ForestWater, generic/Crypt/Sewer
`TERRAIN_GEN_WaterPlane`, `DLC_SC11_WaterPlane` and `DLC_SC11_WaterTorus`, each
with its high and `_LCM` variant. All 14 original material raw hashes, names and
shader pointers were independently reparsed; all four shader raw hashes match
their exact native source objects. No `_Shr_Low` shader exists: that spelling in
the older driver comment was a typo.

## The missed lifetime is real

There are **zero direct serialized renderer material assignments** to these
films. The actual prefabs serialize empty `MeshRenderer.m_Materials` arrays.
Their native `MaterialLoader` components supply original material AssetGUIDs
later, through Addressables. The audit therefore joins each actual
`MaterialLoader.LoadersData.Renderer` pointer to its native GameObject ancestry
and each AssetGUID to the exact original catalog Material location and bundle
dependency closure. A renderer-slot-only scan would incorrectly report no water.

Example: in `pcg_crypt.asset.bundle`, `TERRAIN_Water_Plane` is initially empty;
its loader's GUID `88b35496a8cf1bb499871ada85658393` resolves exactly to
`Assets/Content/Props/HazardousTerrain/Materials/TERRAIN_GEN_WaterPlane_Crypt_Mat.mat`.
The complete scan finds **7,527 native MaterialLoader components and 45 water-film
assignments**. All 36 terrain-film renderer names already match the old
`TERRAIN_Water` check. There is no evidence that a shipped `TERRAIN_GEN` renderer
name caused this particular regression: that is a material name, not its renderer.

`MaterialLoaderData.CheckAllMaterialLoaded` assigns `Renderer.sharedMaterials`
and re-enables the renderer after its asynchronous loads finish. A previously
examined empty renderer must therefore become discoverable at this seam; checking
only already-owned instances cannot adopt a renderer that never entered the set.
Procedural placement/replacement and late room revelation retain the same issue.

## Entire water-film scope

| Actual native family | Assignments | Representative ancestry |
| --- | ---: | --- |
| Generic, Crypt, wood, Sewer terrain | 36 | `TERRAIN_Water_*_PR/TERRAIN_Water_Plane` |
| Cave and forest decorative pools | 6 | `PCG_*_Bay_*_PR/FR_SW_Pond_*` |
| DLC sluice plane, cube and torus | 3 | `DLC_TH_SC08_SluiceGate_BAY/{DLC_SC11_WaterPlane, water_Torus}` |

The maintainer's current all-scenario request covers all **45 actual films**.
The older decorative-water exclusion was not permission to retain the broken
shader. Preserve water visibility, original geometry and the existing fountain /
pond exemption from wall fading. Expanding film replacement does not authorize
retuning decorative solid surroundings. Existing narrow solid basin ownership
remains a separate rule.

Do not classify by "water" in a material, texture or object name alone. The census
also contains underwater stone/wood/floor materials on Amp shaders, underwater
skyboxes, map decals and `WaterCircle_Mat` on `VFX/ShoutCircle_Shd`; these are not
water films and must keep their native materials. Exact native water shader
identity also covers unnamed film renderer children and mixed material slots.

## Property compatibility

All 14 materials and four shader declarations use the existing driver's spelling
for `_Color_Tint`, `_Normal_Map`, `_NormalTilings` and `_Smoothness`. Opaque
Cave/Forest shaders omit `_DetailOpacityBaseNormalStr`. Trans high declares it;
Trans low does not, despite some retained saved values. The existing live
`HasProperty` fallback must remain authoritative; stale serialized values are not
proof that a shader declares a property.

Queues vary: generic terrain is 2900; Cave/Forest and the high DLC films are 2000;
DLC plane LCM is 3000 and torus LCM is 2000. Seven of the fourteen variants,
covering nine of the forty-five authored bindings, use 2000. Preserve every
native material's queue, tint and normal input, including exact restoration.
The private replacement has straight-alpha blending and ZWrite off: its native
opaque queues at or below 2500 must enter the transparent phase at 3000.
Already-transparent queues, including terrain 2900 and DLC plane LCM 3000,
remain authored. This is a blend/depth compatibility conversion on owned film
instances, not a change to the immutable catalog or surrounding native geometry.

Actual Unity `Camera.Render` proves the distinction. Water at queue 2000 remains
visible above opaque ground also at 2000, so queue membership alone was not a
failure. With later opaque/cutout-phase ground at 2450, the same physically
frontmost green film at 2000 is erased to a pure red ground pixel. Moving only
that owned film to 3000 retains its visible contribution. The focused final case
passes seventeen assertions across both ground queues and authored water queues
2000/2900/3000; a compiled old-queue control reproduces the red pixel. These are
software-rendered fixture pixels, not a shipped-scene or headset outcome. See
[WATER-673-REVIEW.md](WATER-673-REVIEW.md) for the exact-source integration evidence.

## Reproduction and evidence

`tools/material-audit/water_surfaces.py` repeats the read-only exact-source census.
Its optional `--coverage-output` exports metadata suitable for runtime tests.
`tools/water-audit/water-surface-coverage.json` retains all 14 exact material
identities and 45 original prefab/material bindings, including source hashes,
GUIDs, renderer path IDs and ancestry. Its per-material fields also retain the
actual custom queue, saved tint/tilings/smoothness/optional normal strength and
the separately verified list of properties the native shader really declares.
This permits quality-variant runtime fixtures to use the actual authored numbers
and distinguish stale saved values from live property availability. It is a
coverage fixture, not a runtime
allowlist. The runtime must support arbitrary newly loaded instances of the
native shader family rather than consult this offline file.

Private receipts live in `.planning/debug/water673-assets/`: the first direct-slot
negative census, complete loader census, material controls, shader/property
compatibility and private clean-dev compiled baseline manifest. The material
validation has **18 assertions / four causal refusals**: a changed raw object,
wrong external shader index, wrong shader object and changed exact material name.
The full renderer census and later bounded material/property checks are composed
evidence, not another complete local runtime gate. Syntax and compact-fixture
counts were also checked. No headset picture or FPS improvement is claimed.

Only this worker's generated build caches were removed after completed processes
were reaped; the clean private baseline, logs, source manifests and native
metadata receipts are retained. No shared baseline or hardware input was modified.
