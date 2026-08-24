#!/usr/bin/env bash
# Round the corners of the README's header animation WITHOUT paying for them in bytes.
#
# WHY THIS IS A SCRIPT AND NOT A ONE-LINER SOMEBODY REMEMBERS
# -----------------------------------------------------------
# GitHub's markdown sanitiser strips `style`, so a rounded corner cannot be asked for in the
# page — it has to be baked into the image, and a GIF's alpha is ONE BIT. That part is easy.
# The hard part is that the source (an ezgif-optimised export, 7.26 MB for 154 frames of
# 800x571) is already near the floor for its content: about 55 % of pixels change every frame,
# so the only lever any GIF encoder has is "leave the unchanged ones alone", and a transparency
# index is what that lever is made of. Add corner transparency naively and the encoder loses
# the lever. Six routes were measured on 2026-08-24:
#
#     source, untouched                                          7.26 MB
#     gifsicle -O3 on the source (i.e. the floor, confirmed)      7.26 MB
#   > ImageMagick CopyOpacity + OptimizeTransparency (THIS)       7.27 MB   <- 0 defects
#     ffmpeg alphamerge/overlay + palettegen, 96 colours         12.07 MB
#     ffmpeg alphamerge/overlay + palettegen, 255 colours        19.92 MB
#     PIL: per-frame index diff, source palette reused           30.21 MB
#     PIL: null re-encode, pixels UNCHANGED                      32.80 MB
#     ffmpeg alphamerge + reserve_transparent + alpha_threshold  59.48 MB
#     PIL: full frames, disposal=2, one transparency index       65.07 MB
#
# The two numbers that matter are the last-but-one and the PIL NULL: PIL re-encoding the source
# WITHOUT CHANGING A PIXEL costs 32.80 MB, because PIL's GIF decoder hands out RGB frames once
# compositing is involved and `convert('P')` then re-quantises each frame to its own local
# palette. So most of those "the mask is expensive" readings were never about the mask at all —
# they were about the encoder. ImageMagick keeps the palette and its `-layers OptimizeTransparency`
# does exactly the right thing: it writes each frame's unchanged pixels AND the never-painted
# corners as the same transparent index, which is legal because a corner no frame ever paints
# stays transparent on the canvas for the whole loop.
#
# VERIFY, DO NOT TRUST: the check below composites all 154 frames in order and counts pixels
# that are transparent INSIDE the rounded rectangle (holes in the picture) or opaque OUTSIDE it
# (ragged corners). Both must be 0. An earlier ffmpeg route passed a spot check on frame 0 and
# had 500 opaque pixels outside the mask from frame 1 onward, so frame 0 alone proves nothing.
set -euo pipefail

SRC=${1:-.planning/debug/ressources/gloomhavenvr_promo.gif}
OUT=${2:-docs/img/promo.gif}
RADIUS=${3:-24}

W=$(python3 -c "from PIL import Image;print(Image.open('$SRC').size[0])")
H=$(python3 -c "from PIL import Image;print(Image.open('$SRC').size[1])")
MASK=$(mktemp --suffix=.png)
trap 'rm -f "$MASK"' EXIT

# One bit of alpha means the curve is a staircase whatever we do; rasterise at 4x and threshold
# so each step lands on the geometrically nearest pixel instead of wherever a 1x rasteriser
# happens to round.
python3 - "$W" "$H" "$RADIUS" "$MASK" <<'PY'
import sys
from PIL import Image, ImageDraw
W, H, R, out = int(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
S = 4
big = Image.new('L', (W * S, H * S), 0)
ImageDraw.Draw(big).rounded_rectangle((0, 0, W * S - 1, H * S - 1), radius=R * S, fill=255)
big.resize((W, H), Image.LANCZOS).point(lambda v: 255 if v >= 128 else 0).save(out)
PY

convert "$SRC" -coalesce null: "$MASK" -compose CopyOpacity -layers composite \
        -layers OptimizeTransparency "$OUT"
gifsicle -O3 -o "$OUT" "$OUT"

python3 - "$SRC" "$OUT" "$RADIUS" <<'PY'
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageSequence
src, out, R = sys.argv[1], sys.argv[2], int(sys.argv[3])
im = Image.open(out)
W, H = im.size
S = 4
big = Image.new('L', (W * S, H * S), 0)
ImageDraw.Draw(big).rounded_rectangle((0, 0, W * S - 1, H * S - 1), radius=R * S, fill=255)
keep = np.asarray(big.resize((W, H), Image.LANCZOS)) >= 128
canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
holes = leak = 0
for fr in ImageSequence.Iterator(im):
    canvas.alpha_composite(fr.convert('RGBA'))
    a = np.asarray(canvas)[:, :, 3]
    holes += int(((a == 0) & keep).sum())
    leak += int(((a != 0) & ~keep).sum())
import os
print(f'{out}: {im.n_frames} frames, loop={im.info.get("loop")}, '
      f'{os.path.getsize(out) / 1048576:.2f} MB (source {os.path.getsize(src) / 1048576:.2f} MB)')
print(f'  transparent INSIDE the rounded rect: {holes}   opaque OUTSIDE it: {leak}')
raise SystemExit(0 if holes == 0 and leak == 0 else 1)
PY
