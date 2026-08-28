#!/usr/bin/env python3
"""Build the four grab-bar albedo strips that ship EMBEDDED IN THE PLUGIN DLL.

    python3 scripts/grabbar-strips.py [--swatches DIR] [--regenerate]

Stage 1 (only with --regenerate) generates six seamless material swatches with gpt-image-2 from
the prompts below. Stage 2 composites them into the four strips at
src/GloomhavenVR/Assets/grabbar_*.png. Stage 2 alone is deterministic and needs no network, so the
normal case — "the layout changed, rebuild the strips" — never re-rolls the materials.

WHY THE MODEL DOES NOT DRAW THE STRIP ITSELF. The strip's two band boundaries have to land on the
exact u the mesh writes; an image model cannot be asked for "the cap band ends at 10.0 %". So the
model is used for the thing it is good at (seamless material) and the LAYOUT is owned here, where
the boundary is read out of the C# rather than re-typed:

    GrabBarMesh.ShaftU0  ->  cap_px

If that constant moves and this script is not re-run, the caps get painted in shaft material. The
script refuses to run at all if it cannot parse the constant, because a guessed boundary is worse
than no strip.

THE PROMPTS ARE PART OF THE ASSET. They are kept here rather than in a chat log so the four strips
can be rebuilt years from now, and so a future change ("Steel is too blue") edits a prompt instead
of starting over. The four rods were designed against the three boards as dev actually ships them,
rendered from Build/Bundles/gloomhavenvr.bundle through Assets/Editor/PreviewBoard.cs's PROJECT
pass — NOT against unity/GloomhavenVR.Assets/board-unity-preview.png, which in a working tree may
have been rendered from a different branch's bundle entirely.

The OpenAI key is read from the gitignored .env at the repo root and is never printed or written.
"""
import argparse
import base64
import io
import json
import math
import os
import re
import sys
import urllib.error
import urllib.request

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'src', 'GloomhavenVR', 'Assets')
CS = os.path.join(ROOT, 'src', 'GloomhavenVR', 'Core', 'GrabBar.cs')

W, H = 1024, 256

SWATCH = ("A seamless tiling MATERIAL SWATCH, photographed flat and straight on, filling the whole "
          "frame edge to edge. Orthographic, no perspective, no object, no background, no shadow, "
          "no vignette, no text, no border, completely even flat lighting with no highlight and no "
          "hotspot. This is an albedo map for a game engine: colour only. ")

SWATCHES = {
    'sw_oak': SWATCH + ("Aged honey-brown oak, straight grain running strictly HORIZONTALLY across "
                        "the frame, softly polished by handling, warm mid-brown with lighter and "
                        "darker grain lines."),
    'sw_steel': SWATCH + ("Cool blue-grey forged steel with a mottled cloudy patina and faint "
                          "hammer marks, desaturated, no rust, no gold, no warm tones at all."),
    'sw_bronze': SWATCH + ("Aged bronze: warm gold-brass base with blue-green verdigris settled "
                           "into it in irregular patches, the way a handled bronze fitting "
                           "patinates."),
    'sw_walnut': SWATCH + ("Dark oiled walnut, fine straight grain running strictly HORIZONTALLY, "
                           "deep neutral brown, quiet and even, nothing bright."),
    'sw_brass': SWATCH + ("Aged tarnished brass, warm and slightly dull, faint fine scratches, "
                          "darker in the micro-pits, not mirror polished."),
    'sw_gold': SWATCH + ("Polished warm gold-brass, bright and clean with only faint wear, the "
                         "colour of a well-kept gilt fitting."),
}

# (output name, shaft swatch, cap swatch, bake_round)
#
# BAKE_ROUND is an asymmetry between the board rods and the window rod, and it is deliberate.
# The three BOARD rods draw through GloomhavenVR/BoardLit, a LIT shader, so their albedo must stay
# pure albedo — baking shading into it would double-shade them. The WINDOW rod cannot use BoardLit:
# it has to draw over a depthless menu canvas, which needs the _ZTest/_ZWrite properties that only
# GloomhavenVR/Overlay exposes (that shader exists because the board occluded these widgets three
# separate times). Overlay is UNLIT, so an untouched strip would make the window rod read FLAT —
# the exact defect this whole redesign removes. So the window rod carries its roundness in v.
STRIPS = [
    ('grabbar_oak',     'sw_oak',    'sw_brass', False),
    ('grabbar_steel',   'sw_steel',  'sw_steel', False),
    ('grabbar_bronze',  'sw_bronze', 'sw_gold',  False),
    ('grabbar_generic', 'sw_walnut', 'sw_brass', True),
]


def api_key():
    path = os.path.join(ROOT, '.env')
    if not os.path.exists(path):
        sys.exit('.env not found at the repo root — cannot regenerate swatches.')
    for line in io.open(path, encoding='utf-8'):
        line = line.strip()
        if line.startswith('OPENAI_API_KEY='):
            return line.split('=', 1)[1].strip().strip('"').strip("'")
    sys.exit('no OPENAI_API_KEY in .env')


def regenerate(swatch_dir):
    key = api_key()
    os.makedirs(swatch_dir, exist_ok=True)
    for name, prompt in SWATCHES.items():
        path = os.path.join(swatch_dir, name + '.png')
        if os.path.exists(path):
            print('keep', name)
            continue
        req = urllib.request.Request(
            'https://api.openai.com/v1/images/generations',
            data=json.dumps({'model': 'gpt-image-2', 'prompt': prompt,
                             'size': '1024x1024', 'n': 1}).encode('utf-8'),
            headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                data = json.load(r)
        except urllib.error.HTTPError as e:
            sys.exit(f'{name}: HTTP {e.code} {e.read().decode("utf-8", "replace")[:300]}')
        open(path, 'wb').write(base64.b64decode(data['data'][0]['b64_json']))
        print('generated', name)


def cap_fraction():
    """Read ShaftU0 out of the C# so the layout cannot drift away from the mesh's UVs."""
    if not os.path.exists(CS):
        return None
    m = re.search(r'ShaftU0\s*=\s*([0-9.]+)f', io.open(CS, encoding='utf-8').read())
    return float(m.group(1)) if m else None


def band(swatch_dir, name, width, height):
    """Centre-crop-and-scale one swatch to a band. Never squashes: a squashed grain reads wrong."""
    im = Image.open(os.path.join(swatch_dir, name + '.png')).convert('RGB')
    sw, sh = im.size
    scale = max(width / sw, height / sh)
    im = im.resize((max(1, int(sw * scale)), max(1, int(sh * scale))), Image.LANCZOS)
    sw, sh = im.size
    left, top = (sw - width) // 2, (sh - height) // 2
    return im.crop((left, top, left + width, top + height))


def bake_round(img):
    """Multiply the strip by a cylindrical shading gradient in v.

    v runs around the circumference — GrabBarMesh.EmitRing writes uv.y = s/RadialSegments with
    angle = 2*pi*s/N and the vertex at +Y for angle 0 — so v = 0 is the TOP of the rod. The
    gradient peaks there and bottoms at v = 0.5, which is viewer-INDEPENDENT: a top-lit rod reads
    round from any horizontal direction, whereas a gradient keyed to the viewer's side would be
    wrong the moment the window is carried around. Unity's v = 0 is the BOTTOM image row, hence
    the 1 - y/H.
    """
    w, h = img.size
    px = img.load()
    for y in range(h):
        v = 1.0 - (y + 0.5) / h
        b = 0.40 + 0.60 * (0.5 + 0.5 * math.cos(2.0 * math.pi * v))
        for x in range(w):
            r, g, bl = px[x, y]
            px[x, y] = (int(r * b), int(g * b), int(bl * b))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--swatches', default=os.path.join(ROOT, 'unity', 'board-prep', 'grabbar'),
                    help='where the six material swatches live (default: unity/board-prep/grabbar)')
    ap.add_argument('--regenerate', action='store_true',
                    help='generate any missing swatch with gpt-image-2 before compositing')
    args = ap.parse_args()

    if args.regenerate:
        regenerate(args.swatches)

    missing = [n for n in SWATCHES if not os.path.exists(os.path.join(args.swatches, n + '.png'))]
    if missing:
        sys.exit(f'missing swatch(es): {", ".join(sorted(missing))} in {args.swatches}\n'
                 f're-run with --regenerate to create them.')

    frac = cap_fraction()
    if frac is None:
        sys.exit('GrabBarMesh.ShaftU0 not parseable — refusing to guess the band boundary.')
    cap_px = int(round(W * frac))
    print(f'ShaftU0={frac} -> cap band {cap_px} px of {W}')

    os.makedirs(OUT, exist_ok=True)
    for name, shaft_sw, cap_sw, round_bake in STRIPS:
        img = Image.new('RGB', (W, H))
        shaft_w = W - 2 * cap_px
        img.paste(band(args.swatches, cap_sw, cap_px, H), (0, 0))
        img.paste(band(args.swatches, shaft_sw, shaft_w, H), (cap_px, 0))
        # The far cap is the near cap MIRRORED, so both ends carry the same material and the wrap
        # at u=1 meets u=0 on identical pixels.
        img.paste(band(args.swatches, cap_sw, cap_px, H).transpose(Image.FLIP_LEFT_RIGHT),
                  (W - cap_px, 0))
        # Feather the two band joins so no mip level shows a hard vertical line where the materials
        # meet.
        for x in (cap_px, W - cap_px):
            box = (max(0, x - 6), 0, min(W, x + 6), H)
            img.paste(img.crop(box).filter(ImageFilter.GaussianBlur(2.0)), box)
        if round_bake:
            img = bake_round(img)
        path = os.path.join(OUT, name + '.png')
        img.save(path, optimize=True)
        print('wrote', os.path.relpath(path, ROOT), os.path.getsize(path), 'bytes')


if __name__ == '__main__':
    main()
