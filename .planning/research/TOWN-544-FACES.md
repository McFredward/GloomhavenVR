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
- Use the separate connected-garment support helper for sleeve/cape transitions. It diffuses support over actual mesh connectivity with coincident-UV seam agreement and protects hands/digits; the rejected hard per-vertex colour reassignment is removed.

## Review method

Rapid consecutive `Camera.Render` calls can show stale GPU skinning within one editor update. The focused review explicitly snapshots the current `BakeMesh(mesh, true)` result before each capture. Front, full orbit, above, below and head extrema are checked alongside exact procedural writing/prayer/casting bone poses. Edge-stretch measurements compare the posed costume with the same neutral Idle sample.

The exploratory review batches are not release candidates. They document rejected widened neck skins, artificial collar covers and abrupt cape-weight transitions. Final artifact hashes, source hashes and test outcomes must be added only after integrated visual approval and bundle validation. Hardware quality remains unverified until the maintainer's next headset test.
