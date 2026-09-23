# Build 542 NPC face and material authoring

## Evidence and scope

The build-541 local screenshots show malformed merchant brow geometry and low-detail faces. The remote build-500 log is older and does not establish a build-541 rendering result. Inspection of the original approximately 500k-triangle provider meshes confirmed that these defects already exist before runtime LOD reduction. Simply raising the old LOD budget or smoothing the old brows could not restore missing anatomy.

All three replacement neutral heads use separate 4K albedo atlases. Original costumes, body proportions and the existing bounded station rig remain. Each LOD is still one skinned renderer with separate body/head material slots. Dedicated triangle budgets are 60k body + 40k face, 22k + 18k, and 7k + 6k. This is a first headset candidate, not a claim of final photoreal quality or facial-animation-ready topology. There is no mouth interior, eyelid rig, eye tracking, lip sync, or facial blendshape set.

## Acquisition and derived geometry

Three approved head-only front/profile/back references were generated from the character references. One Hunyuan3D v3 Normal/PBR/multiview request per character cost an estimated $0.675 each. The merchant result had a closed but deeply malformed lip cavity, and the enchantress had broken albedo/UV correspondence confirmed with a texture-free clay comparison. No automatic paid retries were made.

A single separately approved Trellis 2 merchant replacement used the same front reference, resolution 1536, texture size 4096, decimation target 500000, seed 542, and the documented default 12 sampling steps. Estimated cost $0.35; total estimated generation cost $2.375. Raw provider files and receipts are retained unchanged in the private authoring evidence. The API credential was accessed only through the process environment and never logged.

The Trellis merchant's open beard shells required a screened-Poisson reconstruction from normalized source vertex positions and normals: depth 10, point weight 4, samples per node 1.5, four threads. The rebuilt neutral outer surface was reduced and smoothed locally before atlas projection. No generated mouth cavity or detached brow shell is used in the final asset.

The women use submillimetre volumetric surface cleanup. Approved reference images are projected through front/profile/back coordinates into a fresh UV atlas, with explicit landmark alignment. This preserves image data rather than synthesizing texture through numerical painting. The raw provider texture/normal maps are not used for the new heads.

`repair-npc-faces.py` removes the old anatomy while retaining original hood, hair and collar surfaces. The merchant has a continuous inner-neck closure behind his actual shirt collar. The priestess has a shaped linen continuation, using her original robe atlas, tucked behind the original rope neckline. The enchantress retains her original hood and purple hair. Generated scalp and lower hair fragments are tucked inside those original garments.

## Lighting, furniture and contact

Town surfaces now use scene spherical-harmonic ambient light, the main light, and Unity vertex point lights. No artificial studio key/fill or minimum ambient floor remains. ForceVertex stand lights therefore work with the game's zero pixel-light budget. The shared visibility dissolve and stereo macros remain.

The native candle/flame presenter uses original game quad geometry and textures. TownFlame supports original tint/alpha, a shared animation clock, 8x8 atlas frames and central-eye billboarding. One loop per second is authored station choreography; the original compiled shader's speed units were not recoverable and are not claimed as exact native playback.

Primitive tabletop coins, candles, books, bottles and the merchant leather mat were removed. Runtime supplies original game decoration. The merchant counter retains physical card rack lips/back supports. Explicit GroundAnchor, DecorAnchor and LightAnchor transforms are included. Workbench legs now touch local floor zero while retaining their previous upper edge.

Ground contact uses explicit CPU skinning (`bone.localToWorld * bindpose * vertex`) in station coordinates. Unity's default BakeMesh path applied the FBX renderer's scale twice when subsequently transformed; the final authoring path avoids that ambiguity and asserts a human-sized result. Runtime terrain correction remains additive to the authored Actor transform.

## Validation boundaries

Blender front, three-quarter, side and rear previews were reviewed during assembly. Rejected intermediate variants remain diagnostic evidence only and are not shipping inputs. Final Unity checks render the actual imported assets and the same shaders compiled for Linux/GL; shipping bundles target Windows/D3D and keep type trees. Automated images do not establish headset stereo quality.

The targeted Unity check verifies actual scene-light response, dissolve, greeting motion, native map scale, flame clock/billboarding, independent negative controls, separate head texture resolution, CPU skinning versus explicitly scaled BakeMesh, terrain-offset retention during every clip, and sampled actor envelopes. Clip start/middle/end pose point clouds are exported as station-local little-endian float32 XYZ triples for environment contact review.

## Final source review

Shipping anatomy candidates: merchant assembly v20, priestess v15, enchantress v21. The actual Unity close view exposed retained nearly-neutral forehead skin falsely classified as enchantress hood material; the classifier now requires visibly blue/purple cloth, and the generated straight side-hair fragments are trimmed behind the original purple hair.

Isolating the new heads with the original body triangles removed proved that the remaining fine beard/eye flecks were not old-face overlays. Constant-colour, unlit-albedo, normal-only and isolated LOD0 comparisons were retained. Disabling the diagnostic VFACE flip did not change the picture; a plain Unity normal recalculation introduced UV-island facets. The merchant has no open facial boundary above the neck cut. Small reconstructed folds and their normal variation remain, so neutral head corner normals receive area-weighted spatial smoothing without an angular rejection gate (3mm merchant, 2.5mm women). This changes no positions, triangle indices, UVs, skin weights, or clothing normals. It softens the eye/brow highlights; it does not justify claiming all micro-detail is final photoreal quality.

The actual original cloth has low-poly folds and some fine contact shadows. Final headset review remains necessary for close viewing, stereo and Windows/D3D lighting. Facial animation remains future work, requiring separate eyelid/jaw/mouth authoring.

Sampled station-local actor envelopes, including all four clips at 0.1-second intervals:

| NPC | Minimum XYZ (m) | Maximum XYZ (m) |
| --- | --- | --- |
| Merchant | -0.5700, 0.0000, 0.3743 | 0.4785, 1.7501, 0.9272 |
| Priestess | -0.4311, 0.0000, 0.3723 | 0.3971, 1.7517, 0.8600 |
| Enchantress | -0.4750, 0.0000, 0.4089 | 0.4757, 1.7577, 0.8912 |

Final imported LOD triangle counts: merchant 99,999 / 40,000 / 14,526; priestess 100,000 / 40,000 / 13,000; enchantress 99,998 / 40,000 / 12,998. Merchant LOD2 does not reach the nominal 13k decimation target; its actual count is reported rather than hidden by a budget claim.

The setting-worker's actual triangle/opaque-canopy comparison found minimum gaps of 78.29mm, 94.66mm and 103.06mm respectively, greater than the measured 28.52mm canopy wind displacement. The final normal-only cleanup does not change that geometry.

The full bundle is self-contained, Windows64, type trees enabled, and below GitHub's 100MiB per-file limit. Exact final hash/size and validation evidence follow below.


- Final Windows bundle: 81639177 bytes; SHA-256 `9fa2c5a8f9f2412644b0cef00772b16cc11603a66da6d8e92ab53a5c7c15b04b`.
- Final actual Unity asset validation: `PASS 78 assertions; 6 visual negative controls`; private evidence `.planning/debug/town542-final-normal-unity/town-assets-r8lhmeve/evidence/`, log `unity-area.log`, exact source manifest `source-hashes.json`.
- The prior v21 pose evidence used bundle `01974118cfff66abf8791009c8dabaa72e34e1c5a33ca38b343e2018f0146d53`; only corner normals changed afterwards. The final check exports final poses again.
- All Python authoring scripts compile; `git diff --check` passes. The automated picture is an initial hardware candidate, not a tested headset result.
- The primary checkout retains the original paid requests, selected prepared heads,
  final assemblies/rigs, Poisson inputs and delivery evidence privately under
  `.planning/debug/npc-authoring542/`. These are not release assets and remain ignored.
