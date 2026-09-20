# Offline town-service mesh review

`scripts/review-npc-mesh.py` creates comparable evidence from downloaded GLB candidates.
It is an offline Blender tool: it makes no generation requests and does not read credentials.
Source GLBs remain unchanged. No runtime NPC integration or hardware claim is implied.

## Usage

Run from the repository root with Blender 4.2:

```bash
/home/claw/blender-4.2/blender -b --factory-startup --python-exit-code 1 \
  --python scripts/review-npc-mesh.py -- \
  --input .planning/debug/npc-meshes/merchant/trellis/model.glb \
  --output-dir .planning/debug/npc-meshes/merchant/trellis/review \
  --resolution 768 --samples 12 --threads 8
```

The default is 960 square pixels, 32 Cycles CPU samples and eight threads. Review a batch
sequentially when memory is limited; the imported meshes and topology analysis can be large.
Use `--python-exit-code 1` so a script exception also fails the Blender process.
Generated models, reports, PNGs and packed Blender files belong under gitignored
`.planning/debug/`; do not commit assets to the research documentation directory.

After checking the first contact sheet, correct orientation or framing if necessary:

- `--up-axis auto` prefers imported Blender Z-up if its extent is at least 75% of the
  longest extent; otherwise it uses the longest positive axis. glTF's Y-up convention
  normally becomes Blender Z-up on import. `x`, `y`, `z`, `-x`, `-y`, `-z` explicitly
  override the imported up axis. Auto cannot distinguish upside-down figures.
- `--front-yaw-deg 0` views the normalized model from Blender -Y. `90` views from +X;
  `180` reverses front/back. This controls cameras, leaving the normalized asset fixed.
  Front is an assumption, not an automatic facial recognition result.
- `--face-height 0.87` and `--hands-height 0.54` set the detail target heights as fractions
  of the 1.75 m review height. The face field is 0.55 m square; each hand field is 0.44 m.
  `--hands-offset 0.43` places each hand crop 43% of the projected width away from centre.
  These defaults target an A-pose; folded arms, T-poses and asymmetric gestures need
  adjusted values or manual camera adjustment. Full front/back views retain pose context.

## Outputs and measurement scope

`report.json` records the original file hash and size, Blender version, imported bounds,
per-object and total vertex/triangle counts, indexed connected components, boundary and
nonmanifold edges, degenerate triangles, UV layers, material slots, Principled input values
and image bindings, texture dimensions/colorspaces, armatures, bone names, actions, shape
keys and modifiers. Counts use imported base meshes, with object instances counted separately;
they do not claim evaluated animation or export/runtime performance. The area threshold for
degenerate triangles is recorded per object and scales with its world-space bounds.

Indexed components count connectivity before welding; UV/material seams, separate clothing,
hair cards and accessories can produce legitimate boundaries/components. A high count is a
review prompt, not proof of broken geometry. Self-intersection, anatomical quality, reference
likeness, skinning quality and suitability for VR are not automatically scored.

The review transform parents imported root objects under `REVIEW_Normalization_Only`,
rotates the selected up axis to +Z, scales the complete mesh bounds to 1.75 m, centres X/Y
and places the minimum Z at zero. Original mesh data, relative hierarchy, materials and
textures are retained. Props, headdresses and outliers contribute to these bounds; the
result is a reproducible comparison size, not a verified anatomical height. Imported GLB
lights are disabled for rendering so candidate-supplied lighting cannot skew comparison.

Eight PNGs, also presented in a labeled `index.html`, show:

1. Full front.
2. Full side.
3. Full back.
4. Full three-quarter.
5. Face front.
6. Face three-quarter.
7. Hand on screen-left, front detail.
8. Hand on screen-right, front detail.

`contact-sheet.png` uses that order, left to right across the top row, then the bottom row.
Its tiles are at most 480 pixels; use individual PNGs for detail judgments. Original PBR
materials are rendered with broad neutral studio lighting, a matte grey floor and AgX.
The contact sheet keeps the rendered display appearance without applying AgX twice.
`review.blend` packs the imported image assets and retains every review camera, with the
three-quarter camera active. Open it for manual crop inspection, topology/rig examination
or adjusted lighting. Studio objects use a `REVIEW_` name prefix.

## Validation

Validation uses a locally generated GLB, not the read-only game references. The smoke asset
contains a UV sphere and a cube, with a colored material. Evidence stays in the worker's
`.planning/debug/npc-mesh-smoke/`. Verify counts, unchanged input SHA-256, floor placement,
1.75 m height, all eight PNGs, contact sheet and successful packed-file reopening. This
checks the review instrument only; downloaded NPC candidates still need visual review.

Blender 4.2.22 smoke validation passed: 504 imported vertices and 236 triangles independently
matched the source GLB accessors; input hash remained unchanged; minimum Z was zero and
height 1.75 m; eight 256-square PNGs and a 1024 x 512 contact sheet were produced. The
packed scene reopened with eight cameras and the intended active camera. Syntax compilation
and Git whitespace checks passed. The first merchant/Trellis candidate also rendered at
768 pixels / 12 samples and retained its two packed 4096-square textures; visual findings
belong to the candidate evaluation, not the instrument's smoke result.
