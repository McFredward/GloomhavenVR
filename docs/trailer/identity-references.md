# Original character identity references

The maintainer approved the generative film direction and then identified drift
in Brute's face. These private references establish the original appearance for
face correction and consistency between trailer cuts. They are static source
portraits, not new trailer footage or gameplay captures. Rendering makes no
paid inference calls.

## Canonical references

All images, packed Blender scenes, logs, source captures and manifests live in
the main checkout's gitignored directory:

```text
.planning/debug/trailer/full-production/references/
```

| File | Identity evidence |
| --- | --- |
| `brute_front.png` | Original native face, eyes, snout, jaw and complete three-horn arrangement |
| `brute_threequarter.png` | Original cheek volume, ear profile, forward-curving central horn and side-horn placement |
| `grimhorn_glove.png` | Original Mask_2 skull silhouette, two mask horns, paint, exposed fingertips and fingerless leather glove |
| `runeveil_arcane.png` | Original Mask_1 face and both arcane hand backs with their original cyan glyphs |
| `runeveil_arcane_palms.png` | Original mostly plain leather palm surfaces; avoid inventing palm symbols |
| `livingbones_front.png` | Original skull, skeletal anatomy, armor, sword and complete limb silhouette |
| `official-reference-21s.png` | Unmodified 1280×720 official trailer face close-up in warm cinematic lighting |
| `official-reference-21_5s.png` | A second original cinematic face/expression comparison |

Native portraits are 1536×1536 with neutral soft lighting and sharp focus. Brute
uses the original idle frame 0, original mesh order, UVs and diffuse textures.
Its reference materials use a diffuse response to make anatomical features and
painted details legible. This does not reproduce the game's runtime shader.

Brute has a broad, short feline snout, heavy brow, ridged cheeks, pointed ears,
a fanged jaw, amber/orange eyes and pale gray/white facial texture with the
original dark markings. The large central horn curves forward; two side horns
curve outward and up. Grimhorn is a different identity: its original mask has
**two** curved horns and a carved skull face. Preserve this distinction.

Expressions and cinematic lighting may change, but anatomy, horn placement,
facial proportions, painted identity and equipment should remain consistent.
Reuse the same source portraits across cuts instead of treating each shot as a
new character design. Arcane symbols belong primarily to the original hand
backs and finger backs; the palm reference establishes their actual underside.

## Reproduction

Use Blender 4.2 from an initialized current-development worktree. Native exports
and the existing original motion archive must already be present in the private
trailer directory.

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_identity_references.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/full-production/references \
  --width 1536 --samples 48 --save-blend
```

Select a bounded subset with `--shots brute_front,brute_threequarter`. The tool
reuses existing source-only import helpers; it changes no original mesh, UV or
texture files. Living Bones framing is checked after updating Blender's view
layer, then widened uniformly to retain all original feet, sword and skull.

The optional official frames were extracted from the already downloaded
reference trailer at their original 1280×720 resolution, without resizing,
interpolation or new download. They are secondary style/expression evidence;
the native portraits supply complete, unobstructed geometry and horn placement.

## Evidence and verification

Each reference has a SHA-256 source/output manifest and its actual renderer
source capture. `identity-reference-manifest.json` retains records across
bounded render invocations. Packed Blender scenes survive worktree removal.
The official-frame receipt records the source video hash, timestamps and exact
output hashes.

Validation consists of Python compilation, successful local Blender renders,
visual inspection, native source identity/UV/topology comparison and output hash
verification. The failed initial Living Bones projection check is retained in
its log; updating the camera's evaluated transform fixed the check before the
final bounded render. This is media/source validation, not a runtime gate or
headset appearance claim. Preserve evidence and current deliverables; remove
only owned caches and obsolete Blender backups after processes exit.
