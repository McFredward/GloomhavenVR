# Native scenario figure derivatives

Build 606 adds real topology reduction behind the existing player/enemy detail
percentages. This complements native LOD caps: Wind/Sun demons and Berserker have
no original coarser body, while the far Drake LOD remains expensive.

The bank covers 409 unique original non-cloth body/LOD meshes. It contains 1,191
usable derivatives in 49 independently loadable parts (294,017,137 bytes on disk;
largest part 7,280,055 bytes). The external immutable index maps a mesh signature
and tier to a part. Only the requested tier and the parts required by discovered
scenario actors load. Original 100% settings do not open the index or banks.

| Detail range | Target triangle fraction |
| --- | --- |
| 67–99% | 75% |
| 34–66% | 45% |
| 0–33% | 20% |
| 100% | Exact original mesh and LOD table |

These are targets, not promises to remove a fixed fraction: UV/material seams,
open boundaries, manifold topology, small closed limbs, bone-weight discontinuities
and face/error limits take precedence. Every surviving position, normal, tangent,
color, UV coordinate, bone influence and blendshape delta comes from its exact
original vertex. Bindposes, material slots and original conservative bounds remain.
Native Cloth meshes, weapons and small ornaments are excluded. Runtime code changes
only renderer mesh slots and existing native LOD references; bones, animations,
action/events, colliders, gameplay and global hand skin quality remain untouched.

Representative lowest-tier results:

| Actual native model | Vertices before / after | Triangles before / after |
| --- | --- | --- |
| Wind demon | 7,652 / 4,755 | 11,120 / 5,326 |
| Sun demon (`MO_NightDemon_Mesh` in the native Sun bundle) | 7,652 / 4,756 | 11,120 / 5,328 |
| Berserker | 10,325 / 5,875 | 15,738 / 6,838 |
| Far Spitting Drake LOD | 5,079 / 3,999 | 6,745 / 4,585 |

`ScenarioFigureMeshBank` prepares immutable derivatives during actor/loading/config
admission. Applying a cached detail or grabbing a figure never performs file I/O or
simplification. Existing local/remote home and highlight twins follow source mesh
changes and exact restoration. Bank meshes remain owned for the process lifetime;
restoring a setting never destroys a mesh still referenced by another visual twin.
The catalog, part-attempt set and source identity cache are bounded.

## Regeneration and verification

Licensed originals under `ressources/GH_Data` remain read-only. UnityPy exports
complete original streams into ignored local evidence; the separate Unity 2021.3.5
project builds Windows bundles compatible with the shipped game. Generated `.asset`
intermediates, raw exports and Unity Library files are ignored and regenerable.
`manifest.json` pins source bundle hashes and produced bank hashes/counts.

```bash
python3 scripts/generate-figure-meshes.py
python3 scripts/generate-figure-meshes.py --validate
python3 scripts/check-figure-mesh-runtime.py
python3 scripts/check-scenario-figure-detail-runtime.py
python3 scripts/check-scenario-figure-detail-budget.py
```

`--repack` rebuilds parts from existing generated assets; `--skip-extract` reuses
existing exports. The validator checked all 1,191 serialized derivatives with
87,818,972 survivor/channel/bindpose/index/material/boundary assertions. The runtime
fixture loads original Wind/Sun/Berserker/Drake prefab assets directly from their
licensed bundles without instantiating native gameplay controllers. It checks real
bank matching, unreadable Drake metadata, moving-bone skinning, preserved geometry,
local/remote ghost restoration, foreign-slot ownership, and saves original/coarse
Wind/Sun/Berserker silhouettes from real Unity graphics. Negative controls exercise
unsafe topology removal, lost weights, stale ghosts and foreign replacement. The
quality driver separately proves that a large native Cloth mesh and its coefficients
are never admitted, whether the solver is ON or OFF.

These are source and actual Unity runtime results. Headset appearance and frame-time
gains require hardware validation; no measured FPS improvement is claimed yet.

## Optional distance and NPC detail banks

`python3 scripts/generate-distance-figure-meshes.py` appends separate distance parts
and an index without rebuilding the existing 75/45/20 scenario banks. The original
endpoint-derived banks remain byte-for-byte unchanged. Far bodies use the MIT
UnityMeshSimplifier SmartLink implementation vendored under `UnityMeshSimplifier/`
(pinned upstream revision and license are included). It is an offline tool, never
loaded by the plugin. Its 5% triangle target is a request; boundary constraints can
retain more geometry. `distance-manifest.json` records actual counts and hashes.

NPC bodies retain their original facial triangles, positions, normals, UVs, weights
and expression deltas. Only the remaining body is simplified, with its open cut
boundary fixed, and then rejoined under the original material slots and skeleton.
The lossless GHFM2 stream contains the original sparse blendshape frames. No native
bundle, animation, material, bone transform or gameplay controller is modified.

Runtime quality selection uses eye distance divided by the renderer's world-bound
radius, so world scaling retains the same projected-size policy. Separate entry and
exit thresholds avoid mesh chatter. Near/held figures obey the saved mesh-detail
cap; distant figures can use the stronger bank. Missing far records fall back to a
verified 20% derivative. Off and VR teardown restore owned original mesh slots.
A foreign replacement is never overwritten. Skinned influence caps also restore the
original renderer/global quality and respect native quality-level changes.

`python3 scripts/check-figure-distance-runtime.py` compiles the production helpers
and tests original hero/drake/demon bodies and all three shipped NPC prefabs in real
Unity, with mutation controls, actual triangle counts and mesh/rig/material restoration.
Linux preview renders use one common Standard shader for original Windows-only shader
programs; the original material objects and textures remain identical across tiers.
The preview runner yields real player frames between swaps: Unity GPU skin buffers
are prepared once per frame, so capturing multiple incompatible mesh sizes within one
frame can produce a false blank-body result. Two actual frames at each tier and the
far-to-original return must each contain visible skinned-body pixels. Native animator
updates run between those samples; no baked replacement renderer is used.
Those previews establish geometry comparisons, not headset performance or production
shader parity. Hardware logs remain the evidence for frame-time improvement.

The append currently contains 415 derivatives in 29 parts (83,005,400 bytes), with
all existing 1,191 derivatives and their 49 parts unchanged. Representative far
triangle counts are Berserker 1,189 (old lowest 6,838), Sun demon 1,038 (5,328), and
far Spitting Drake 2,007 (4,585). Protected NPC faces impose a much higher floor;
actual shipped renderer triangle counts are:

| NPC | Original | Mid (45 target) | Far (5 target) |
| --- | --- | --- | --- |
| Merchant | 136,798 | 82,840 | 55,569 |
| Priestess | 135,910 | 82,352 | 54,755 |
| Enchantress | 141,114 | 86,072 | 58,909 |

The far target never means that an NPC reached 5%: its untouched facial surface
remains. A focused `--only` or `--npc-only` generation requires `--no-package` so
partial numbered parts cannot overwrite a complete published bank.
