# Cinematic opening plates

The approved trailer opens on genuine town residents and then reveals the VR
players as enormous masks above a dungeon. `scripts/trailer/render_opener.py`
stages the original mod geometry and read-only native game exports in Blender
4.2. These are cinematic renders, with adapted PBR and authored lighting; they
are not runtime gameplay captures.

## Render inputs and outputs

Use an existing initialized development worktree. All renders, logs, manifests
and packed Blender scenes belong under the main checkout's gitignored
`.planning/debug/trailer/opener/`, as explicitly requested by the maintainer.
The `--output` directory is required. The script does not make paid API calls.

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_opener.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/opener/plates \
  --shots npc_enchantress --width 1920 --samples 96 --save-blend
```

For the reveal, first prepare native exports using the native-scene extractor.
Its source manifests, original texture/property maps and sampled idle pose must
remain beside the generated OBJ/MTL files. Then render matching keyframes:

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_opener.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/opener/plates \
  --native-scene /home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene \
  --shots dungeon_intro,godmask,godmask_lift,board_reveal \
  --width 1920 --samples 96 --save-blend
```

`--width 960 --samples 24` provides a quick composition preview. `--frames N`
adds locally rendered camera-validation samples for shots with camera keyframes.
Read the render log and verify `PLATE_READY` plus the output manifest: Blender
requires `--python-exit-code 1` to propagate Python failures reliably.

## Source fidelity

Town residents use only LOD0 of the shipped original FBX. The importer restores
neutral bind pose, as the runtime does before attaching eyes, and preserves the
original separate garment, face543, hands545, iris and cornea material slots.
Clearing Blender material slots resets polygon assignments; the script records
and restores each source index before rendering. Combining those slots would
incorrectly put the garment atlas across the face and hands.

Masks and VR hands retain original source mesh, UVs and diffuse textures. The
held-figure endpoint articulates the original glove rig and aligns its actual
thumb/index tips around the source hero's head/neck. The figure keeps the exact
native idle pose and its original sword/shield attachment geometry. Held-figure
staging precedes action; later gameplay must return the figure to its board
position before native movement or combat starts.

Native actors and architecture exports are already Blender Z-up. Import with
`forward_axis='Y', up_axis='Z'` to preserve their coordinates. Blender's
`NEGATIVE_Y` import option introduces a 180-degree turn. Source hero anatomical
facing is toward Blender -Y. Native wall materials are paired with their
original wall bank because PCG template slots are filled by the game's builder.
Native floor geometry must be inspected before tiling: segment P is sloped
trim, not a general flat floor tile. The chosen `CR_TC_Floor_Basic` is the full
original hex with a level plateau, native recess and solid edge. Tiling preserves
that geometry and its source UVs.

Original mod environment props supply rocks, a barrel and a crate. Trailer
lighting, service-table geometry and the distant town masonry are documented
staging. There are no invented player bodies, pseudo-cards or generated text on
cards. The two reveal keyframes share their world, Grimhorn transform and source
figure. The lifted endpoint introduces the second original mask and arcane
hands. `dungeon_intro` keeps the same complete world and uses figure-height
framing to put the mask above the image and glove beyond its right edge. It is
the compatible first image for the upward reveal. Rear candles stand on a solid
wooden counter, with masonry extending below it.

## Review evidence

Each output set contains `opener-manifest.json`, SHA-256 hashes of the script,
source inputs and deliverables, source-instance transforms, grip anchors and
Blender version/render settings. Packed `.blend` files retain their actual
textures independently of worktree lifetime. Final editing adds the persistent
`UNOFFICIAL · FAN-MADE MOD` disclosure and the explicit fan-mod closing card.
Named `opener-manifest-<shots>.json` receipts preserve individual render runs
when another shot is rendered into the same output directory.

Validation consists of Python compilation, actual local Blender rendering,
source/export hash validation and visual inspection of framing, material slots,
eyes, figure pose and hand contact. This does not establish headset appearance
or constitute a new runtime test-gate pass. Preserve source manifests, logs,
current previews and plates; remove only owned regenerable caches after the
render processes have finished.
