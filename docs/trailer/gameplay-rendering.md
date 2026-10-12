# Local cinematic camera and figure release

`scripts/trailer/render_gameplay.py` produces source-faithful cinematic
reconstructions for the approved trailer. It loads the packed
`godmask_lift.blend` world, retaining the original masks, original native Brute,
original native flat hexes, props, adapted materials and authored lighting.
These clips are local Blender renders, **not runtime gameplay captures**.
The final edit must retain the persistent unofficial fan-mod disclosure.

The release moves the held original Brute down to its original staged board
cell `(0, 0.17, 0.005)`. It does not move the character to a different hex or
issue any native action. The exact original leather glove rig replaces the
packed baked hand and is checked vertex by vertex against that starting mesh.
Thumb/index contact remains unchanged during descent. The source finger bones
open only after landing, and the hand withdraws. Subsequent camera shots keep
the actors in their original idle poses. This represents ordinary idle release,
not the forced immediate return required when a native action begins.

A second original leather hand completes Grimhorn's tracked pair. The existing
two arcane hands complete Runeveil's pair. The source Living Bones model adds
a second native actor. No avatar bodies or invented gameplay widgets are added.

## Render commands

All media belongs under the main checkout's gitignored
`.planning/debug/trailer/cinematic/`. The script enforces that destination and
refuses to overwrite an existing shot directory. Every new render round uses a
new version directory. Neither this script nor its helpers make paid API calls.

First benchmark and inspect a frame before committing to a full render:

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_gameplay.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/cinematic/preview-v1 \
  --shots release,table_orbit,figure_dolly --frames 1,18,last \
  --width 1920 --samples 24 --save-blend
```

Render complete silent clips after checking their compositions:

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_gameplay.py -- \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/cinematic/final-v1 \
  --shots release,table_orbit,figure_dolly --animation \
  --width 1920 --samples 24 --save-blend
```

At 24 fps, the release lasts 1.5 seconds, the shared-table camera orbit lasts
3 seconds, and the figure-height rail shot lasts 2.5 seconds. Every camera
frame has an explicitly evaluated aim and focus distance. H.264 encoding
preserves all rendered frames at 1920×1080 and 24 fps. The movie is silent;
the parent edit supplies original narration, music, source sound effects and
the persistent fan-mod label.

## Evidence and cleanup

Each shot includes an exact-source manifest, hashes of the source packed scene,
original source files, tool and new inputs, original asset placements, release
control poses, measured hand-mesh agreement, render timings, output hashes and
encoded frame-count evidence. `--save-blend` packs the actual live rig, textures
and animation independently of the development worktree. Source banks remain
read-only. Unused FBX materials are purged before packing because their legacy
filenames can refer to textures that the adapted source materials replace.

After a verified encode, the script removes its own regenerable encoding-frame
directory while retaining the movie, packed scene, manifest and selected full
resolution QC frames. Interrupted or failed renders preserve their evidence;
reap the process before cleaning only that run's unused encoding cache. Never
remove the shared native exports, opener scenes or another worker's media.

Validation uses Python compilation, real local renders, source-hash and
vertex-correspondence checks, landing-pose inspection and FFprobe frame-count
verification. It does not establish headset appearance or a new complete
runtime-test gate.
