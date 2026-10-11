# Build 544 town anatomy and costume repair

## Evidence and scope

The six hardware photographs in `npc_probleme/` supersede the build-543 desktop approval. They show dark eyes, a lengthened merchant head, patchy rear scalp sampling and open or intersecting costume/neck boundaries. The current work preserves the separate anatomical eyes, facial channels and original character references. It explicitly includes garment defects previously left for later review.

The Windows eye defect is independently established from compiled D3D shader programs: the built-in vertex-light keyword was present in vertex variants but absent from the fragment programs that calculated eye illumination. The separate eye lane carries the enabled state through a varying and validates the compiled fragment light bindings. This is not diagnosed as deferred rendering or fixed by self-emission.

## Authoring changes

- Fit the merchant's chin/beard and scalp to a shorter, broader original silhouette while keeping the eye landmarks and actual eye sockets registered.
- Restrict portrait sampling to the original foreground, avoiding the grey reference background on the rear scalp.
- Keep the complete anatomical lower neck rings and taper them inside the original blouse. Cutting individual crossing quads was rejected because it created scalloped side boundaries.
- Blend the low neck into Chest while keeping the skull and mandible rigidly supported by Head. The independent skull/jaw marker contract remains.
- Remove the two specifically inventoried obsolete procedural neck covers from build 542, preserving unrelated hair, clasps and costume components.
- Preserve the priestess's original blouse and chain. The continuous fitted neck replaces only the bounded obsolete exposed skin patch beneath it, with its low vertices fitted behind the retained cloth. The rejected atlas-only patch left a visible seam.
- Give the existing garment cuts a thin inner surface. Source-provenance checks bound every added inner vertex to 3 mm, avoiding BMesh's unbounded miters at nearly reversed original folds. Preserve original outer winding rather than globally recalculating overlapping garment shells.
- Preserve a concealed 20 mm overlap beneath the priestess blouse; this prevents oblique combined head/body poses from exposing the hollow interior without enlarging the visible chest.
- Close the merchant's bounded obsolete rear head-cut in the original cape and smooth its adjoining cut tips. Exact source UV probes identify the central shirt remnants removed above the preserved clasp; the actual collar points remain.
- Use the separate connected-garment support helper for sleeve/cape transitions. It diffuses support over actual mesh connectivity with coincident-UV seam agreement and protects hands/digits; the rejected hard per-vertex colour reassignment is removed.

## Review method

Rapid consecutive `Camera.Render` calls can show stale GPU skinning within one editor update. The focused review explicitly snapshots the current `BakeMesh(mesh, true)` result before each capture. Front, full orbit, above, below and head extrema are checked alongside exact procedural writing/prayer/casting bone poses. Edge-stretch measurements compare the posed costume with the same neutral Idle sample.

The exploratory review batches are not release candidates. They document rejected widened neck skins, artificial collar covers and abrupt cape-weight transitions. Final artifact hashes, source hashes and test outcomes must be added only after integrated visual approval and bundle validation. Hardware quality remains unverified until the maintainer's next headset test.

## Final asset evidence

The integrated CPU pose review is `town544-unity/final-evidence` (137 hashed inputs/images/reports), with full orbit, above/below, independent head limits, exact contact-v9 body poses and combined body/head stress poses. Parent visual review approved the final three characters. Packed prototypes, baked heads and final rigs are archived under `npc-authoring544`; each rig retains all four original actions and all five referenced texture dependencies are packed.

The isolated actual-bundle gate is `town544-final-assets/town-assets-mgsr1d_g`: **489 assertions and six visual negative controls passed**, including all three LODs, 11 facial channels, lit eye apertures, blink/speech/gaze, zero costume facial deltas, floor/scale and original-animation terrain-offset preservation. Its final execution log is `unity-native-clips.log`. The equivalent-scale projection check also exercises the production map near cap of 0.5 and worst allowed far/near ratio of 50,000. Leaving the preceding close-up near plane at 0.01 had produced a fixture-only depth-precision failure; the native capped projection passes without changing shipped assets.

An independent compiled Windows guard passes for all four D3D fragment variants of both eye shaders, including every required real point-light input. No new synthetic audio is included. Actual headset appearance, close-range stereo and hardware frame timing remain to be tested; desktop passes are not a hardware claim.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| Windows `ghvr-town.bundle` | 98,325,951 | `2ba6541d88c48ec15fb94eefbdf4a06e15b8bd8e68068247025797f81d1f2ba7` |
| Linux evidence `town-review.bundle` | 98,331,564 | `e0a1357db551bdbab3995f1ee37c3a728e03e164a284cc57096aa7ea55c0a0f3` |

The Windows artifact remains below GitHub's 100 MiB blob limit and is smaller than build 543. Head atlases remain 4K; the actor still uses one skinned renderer per active LOD with body/face submeshes and the existing four eye renderers.

| Actor | LOD0 vertices / triangles | LOD1 vertices / triangles | LOD2 vertices / triangles | Unity mesh memory, all LODs |
| --- | --- | --- | --- | ---: |
| Merchant | 93,321 / 111,763 | 42,312 / 42,839 | 26,002 / 26,294 | 35,782,312 B |
| Priestess | 87,544 / 111,695 | 39,553 / 42,160 | 23,912 / 24,975 | 33,808,432 B |
| Enchantress | 100,373 / 116,552 | 46,350 / 43,865 | 27,663 / 25,629 | 38,306,376 B |

These are Unity mesh allocations reported by the final fixture, not total GPU/process memory. Eye meshes and materials are additional and unchanged in geometry from build 543.
