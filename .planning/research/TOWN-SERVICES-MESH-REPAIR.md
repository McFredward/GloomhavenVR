# Hunyuan priestess hand normal seams

## Evidence and bounded correction

The first priestess/Hunyuan GLB has straight rectangular lines across the backs of the
hands, thumb bases and wrists. `scripts/diagnose-npc-materials.py` isolates the input:
the lines disappear in opaque rough clay, unlit basecolor and original PBR with only the
Normal input disconnected. A neutral clay material retaining only the normal map reproduces
them. These particular lines are normal-map shading defects, not cracks in the hand mesh
or basecolor texture. Face controls also retain the modeled facial form without the map.

Ordinary atlas padding and explicit island-edge padding were tested and rejected: neither
reliably removes the hand lines. The first apparent padding improvement did not survive a
closer comparison. An explicit 16-bit round trip matches the in-memory padded result, so
the failure is not established as an exporter precision defect.

The bounded correction attenuates the normal effect smoothly to zero at precise hand UV
seams, retaining basecolor/ORM and the remaining normals.
`scripts/repair-npc-texture-seams.py` implements that separate candidate. The tradeoff is
less normal-map microdetail within those narrow hand/wrist regions. This is an asset
presentation correction, not a claim that the original generation was flawless.

## Reproduce

```bash
/home/claw/blender-4.2/blender -b --factory-startup --python-exit-code 1 \
  --python scripts/repair-npc-texture-seams.py -- \
  --input .planning/debug/npc-meshes/priestess/hunyuan/model.glb \
  --output-dir .planning/debug/npc-meshes/priestess/hunyuan/repaired-candidate-hand-seams

/home/claw/blender-4.2/blender -b --factory-startup --python-exit-code 1 \
  --python scripts/review-npc-mesh.py -- \
  --input .planning/debug/npc-meshes/priestess/hunyuan/repaired-candidate-hand-seams/model.glb \
  --output-dir .planning/debug/npc-meshes/priestess/hunyuan/repaired-candidate-hand-seams/review \
  --views face hand_screen_left hand_screen_right --hand-yaw-deg 65 \
  --resolution 768 --samples 16
```

Blender 4.2 needs NumPy, included in its distribution. `--mask-python` defaults to `python3`
and must have NumPy and Pillow installed locally; it rasterizes the UV masks offline. The
tool never calls a generation service or reads credentials. Its output GLB cannot overwrite
the input path. Use a separate directory under gitignored `.planning/debug/`.

The default A-pose region includes triangle centres between 30% and 67% of the imported
Blender Z height, at least 30% of the total X width away from the figure centre. It is a
position-based hand/wrist selection, not semantic hand detection. Adjust
`--hand-min-height`, `--hand-max-height` and `--hand-min-abs-x` for other poses. Rotated,
folded-arm, nonhumanoid or heavily overlapping-UV assets need a separate selection review.

The mask finds single-use UV edges after rounding UV coordinates to six decimals. It
uses a four-texel neutral core and eight-texel linear falloff (`--seam-width`, `--falloff`).
Attenuation stays inside the selected hand UV coverage plus one exterior texel for bilinear
filtering, protecting unrelated islands that happen to sit nearby in the atlas. Tangent
normal RGB blends toward `(0.5, 0.5, 1)`; alpha and all pixels outside the mask remain
unchanged in memory. The normal image is stored as a raw 16-bit PNG to retain the neutral
vector and smooth falloff. This does not add authored detail to the original 8-bit map.

The tool supports embedded GLB images, direct image-to-tangent-normal bindings and UVs
inside 0..1. It rejects transformed coordinates, unsupported bindings and empty hand
selections. It writes the corrected PNG, UV/mask arrays, `repair-report.json`, a packed
`repaired.blend` and a new `model.glb`.

The GLB writer appends the new normal PNG and changes only its image buffer-view reference.
The original binary buffer prefix, accessors, mesh attributes, transforms, material settings
and other image bytes remain verbatim. Old normal bytes remain unreferenced in the binary
buffer; this favors preservation over file compaction. There is no Blender geometry
re-export, vertex splitting or pose drift in this final path.

## Validation and limits

Controls and failed padding experiments stay under
`.planning/debug/npc-meshes/priestess/hunyuan/seam-diagnosis/`. The earlier
`repaired-candidate/` directory is the rejected padding experiment; use only the explicitly
named `repaired-candidate-hand-seams/` for this correction.

The new GLB is reimported and rendered, including both oblique hands and the face. Source
hash preservation, the original binary prefix, unchanged basecolor/ORM embedded bytes,
unchanged source mesh/accessor records and identical imported geometry counts/bounds are
checked. The source has 298,564 imported vertices, 499,374 triangles, 2,681 indexed
components and zero tiny-area triangles; the final GLB retains those counts exactly.

The final mask affects 496,532 of 16,777,216 normal texels (2.96%); 308,064 texels are fully
neutral. Both oblique hand closeups show substantial reduction of the straight thumb/hand
rectangle. On the screen-left thumb seam (768-square image, X 515..574 / Y 425..449), mean
absolute RGB error against the no-normal control falls from 2.801 to 0.272 on the 8-bit
display scale. The comparison uses the exported/reimported GLB. Faint wrist and robe lines
remain; this evidence does not establish complete removal of every visible seam. Syntax
compilation and Git whitespace checks also pass.

Rendered improvement is evidence for the reviewed Blender material path. Native Unity
normal texture import, mipmaps, compression, distant views and headset rendering remain
unverified. A downstream tool that changes the normal image's precision or sampling needs
its own closeup comparison. The correction does not repair unrelated robe/bag seams,
change likeness or add an animation rig.
