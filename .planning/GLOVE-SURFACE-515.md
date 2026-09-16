# Softer Glove finger relief — build 515

## Report and cause

The maintainer reports excessive designed cracks on the Glove fingers and requests a more
natural skin appearance. The source normal atlas contains pronounced authored crease/crack
patterns on the exposed finger regions; the colour atlas also retains baked skin detail.
BoardLit perturbs its lighting normal with this map. Both serialized materials and the actual
published bundle used `_NormalStrength = 0.5`, introduced in build 483 after the earlier
request to halve the original relief. No runtime hand code overrides this property.

The retained logs remain local build 513 / remote build 500, not a new capture of this report.
No new screenshot was supplied. The explanation is supported by texture inspection, shader and
material source, actual bundle inspection and controlled native renders; it does not establish
that every possible skinning defect is absent.

## Change

The hand generator now sets Glove normal strength to 0.25 instead of 0.5. Both glove materials
were regenerated using exactly Unity 2021.3.5f1, and the rebuilt bundle is committed in
`prebuilt/gloomhavenvr.bundle`. This halves the previous normal-map perturbation while preserving
the authored colour texture and skin detail. Plate and Arcane remain at 1.0.

The mod's common hand-prefab path supplies local and remote hands with the same material.
No viewer-local override or new setting is introduced. Meshes, weights, UVs, bone transforms,
attachment anchors and finger animation are unchanged. Version remains 1.0.3 on dev; ModBuild
515 identifies this full install, including the updated bundle. Published 1.0.2 is unchanged.

## Asset verification

- Native editor A/B renders use the real prefab and BoardLit, isolated cloned materials and
  identical framing at strengths 0.50 / 0.25 / 0.00. Twenty-four images cover left/right,
  open/moderately curled and both sides; eight comparison strips are produced. The candidate
  softens the relief while leaving colour-map detail visible. Preview curl arithmetic is
  reproduced and editor lighting is not headset evidence.
- Preview tool: `GloomhavenVR.GloveSurfacePreview.RenderAll`, with
  `GLOVE_SURFACE_PREVIEW_OUT` outside the project and a graphics device (`xvfb-run`).
  Render output for this review: `/tmp/gvr-515-glove-preview`.
- Native `GloveSurfacePreview.VerifyBundle` loads the actual rebuilt bundle and verifies all
  six hand prefabs, 19 anchors each, normal/albedo textures, BoardLit and material strengths.
- Binary-object comparison against the previous bundle retains all 617 asset names and all
  2,768 object IDs/types. Only the two Glove material floats change semantically. A third raw
  object difference is one editor-data hash in Unity's built-in Standard shader; its program
  data and runtime fields are unchanged. All meshes, textures, prefabs and other materials
  are byte-identical.
- New bundle: 74,942,975 bytes, UnityFS format 7, Unity 2021.3.5f1.
- The editor logs also contain the existing project input-system initialization complaint
  (`activeInputHandler=-1`). HandsBuilder exits successfully, all hand materials are generated,
  native renders complete and actual bundle verification passes. No project input setting was
  changed for this material adjustment.

Full integration gate results follow after completion. Headset judgment of the surface remains
with the maintainer; the editor comparison does not prove the perceived strength in VR.
