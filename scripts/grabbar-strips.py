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
    # ANTIQUE, not merely darker. The first strips used the bright sw_brass on the oak and walnut
    # caps and they came out a pale yellow-olive next to the design sheet's DARK bronze knobs. That
    # is a different material, not a brightness slider, so it gets its own swatch.
    'sw_antique': SWATCH + ("Dark antique bronze, heavily tarnished to a deep brown-bronze with "
                            "almost black recesses and only a faint warm sheen on the high points, "
                            "the patina of an old door fitting nobody has polished in decades."),
}

# Per-material shading, consumed by the MRS map. R = metallic, G = roughness — the packing
# GloomhavenVR/BoardLit declares. Wood is dielectric and satin; the fittings are metal and
# fairly tight. Without this map _SpecStrength has nothing to read and the rods render matte,
# which is most of why the first set looked flat beside the sheet.
# R = metallic, G = roughness.
#
# THE WOODS CARRY A SMALL METALLIC, AND THAT IS AN AUTHORING CHOICE, NOT A CLAIM ABOUT OAK.
# GloomhavenVR/BoardLit builds f0 = lerp(0.04, albedo, metallic) and has no Fresnel and no energy
# model — it is a stylised term over two baked light directions, not PBR. At metallic 0 a wooden rod
# tops out around a 3 % highlight, which is invisible, and the rods came back looking like raw sawn
# stock next to a design sheet that plainly shows an oiled, handled, POLISHED rod. Roughness alone
# cannot buy that back: it sharpens the lobe without brightening it. A modest metallic does, and in
# THIS shader it is the only dial that does. Kept small so the wood does not take on a metal's
# colour-tinted highlight.
SHADING = {
    'sw_oak':      (0.22, 0.30),
    'sw_walnut':   (0.20, 0.34),
    'sw_steel':    (0.90, 0.45),
    'sw_bronze':   (0.85, 0.40),
    'sw_brass':    (0.95, 0.30),
    'sw_gold':     (0.95, 0.25),
    'sw_antique':  (0.90, 0.42),
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
    ('grabbar_oak',     'sw_oak',    'sw_antique', False),
    ('grabbar_steel',   'sw_steel',  'sw_steel',   False),
    ('grabbar_bronze',  'sw_bronze', 'sw_gold',    False),
    ('grabbar_generic', 'sw_walnut', 'sw_antique', True),
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
    im = grade(Image.open(os.path.join(swatch_dir, name + '.png')).convert('RGB'), name)
    sw, sh = im.size
    scale = max(width / sw, height / sh)
    im = im.resize((max(1, int(sw * scale)), max(1, int(sh * scale))), Image.LANCZOS)
    sw, sh = im.size
    left, top = (sw - width) // 2, (sh - height) // 2
    return im.crop((left, top, left + width, top + height))


# A per-swatch albedo grade: (gain, saturation). The generated oak came back a bright saturated
# orange and the walnut a flat brown; against the design sheet both read as raw stock rather than
# as an oiled, handled rod, AND the brightness swamped the specular lobe — a highlight cannot show
# on an albedo that is already near the top of the range. Pulling the level down and easing the
# saturation is what lets BoardLit's sheen read as sheen. Metals are left alone.
GRADE = {
    'sw_oak':    (0.74, 0.82),
    'sw_walnut': (0.88, 0.90),
    # The generated gold came back a bright primary yellow. The sheet's bronze knob is a WARM,
    # slightly tarnished gilt, and at metallic 0.95 the albedo IS the highlight colour here
    # (BoardLit tints f0 with it), so an over-saturated albedo shows up twice.
    'sw_gold':   (0.84, 0.78),
}


def grade(img, name):
    if name not in GRADE:
        return img
    gain, sat = GRADE[name]
    w, h = img.size
    px = img.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            r = lum + (r - lum) * sat
            g = lum + (g - lum) * sat
            b = lum + (b - lum) * sat
            px[x, y] = (min(255, max(0, int(r * gain))),
                        min(255, max(0, int(g * gain))),
                        min(255, max(0, int(b * gain))))
    return img


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


def luminance(img):
    px = img.load()
    w, h = img.size
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            out[y][x] = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    return out


def normal_map(img, strength=2.2):
    """A tangent-space normal map from the albedo's own luminance, treated as height.

    Not a substitute for a sculpted map, and not pretending to be: it exists so the wood grain and
    the metal's pitting CATCH THE LIGHT. GloomhavenVR/BoardLit's own header says the same thing
    about the board — "the AI-authored albedo already carries baked detail; this adds just enough
    normal-mapped shape so the brass/woodgrain reads".

    Unity's normal maps are +Y up in tangent space and the strip's v runs AROUND the rod, so the
    green channel is inverted relative to image rows.
    """
    w, h = img.size
    lum = luminance(img)
    out = Image.new('RGB', (w, h))
    px = out.load()
    for y in range(h):
        ym, yp = (y - 1) % h, (y + 1) % h          # v wraps around the rod
        for x in range(w):
            xm, xp = max(0, x - 1), min(w - 1, x + 1)
            dx = (lum[y][xp] - lum[y][xm]) * strength
            dy = (lum[yp][x] - lum[ym][x]) * strength
            nx, ny, nz = -dx, dy, 1.0
            inv = 1.0 / math.sqrt(nx * nx + ny * ny + nz * nz)
            px[x, y] = (int((nx * inv * 0.5 + 0.5) * 255),
                        int((ny * inv * 0.5 + 0.5) * 255),
                        int((nz * inv * 0.5 + 0.5) * 255))
    return out


def mrs_map(img, cap_px, shaft_sw, cap_sw):
    """R = metallic, G = roughness — the packing GloomhavenVR/BoardLit declares.

    Roughness is modulated by the albedo's own luminance so that pits and grain read as rougher
    than the crowns, which is what puts the crevice contrast into the render.
    """
    w, h = img.size
    lum = luminance(img)
    out = Image.new('RGB', (w, h))
    px = out.load()
    for y in range(h):
        for x in range(w):
            in_cap = x < cap_px or x >= w - cap_px
            metal, rough = SHADING[cap_sw if in_cap else shaft_sw]
            rough = min(1.0, max(0.0, rough + (0.5 - lum[y][x]) * 0.35))
            px[x, y] = (int(metal * 255), int(rough * 255), 0)
    return out


def bake_specular(img):
    """A narrow bright streak just above the rod's top line, for the UNLIT window rod only.

    The lit board rods get their sheen from _SpecStrength + the MRS map. The window rod draws
    through GloomhavenVR/Overlay, which is unlit and reads no map at all, so every bit of its
    material depth has to be in the albedo. bake_round already gives it the body gradient; this
    adds the highlight that makes it read as polished rather than merely curved.
    """
    w, h = img.size
    px = img.load()
    for y in range(h):
        v = 1.0 - (y + 0.5) / h
        # Distance from the top of the rod, wrapped.
        d = min(abs(v - 0.0), abs(v - 1.0))
        streak = math.exp(-(d / 0.085) ** 2) * 0.45
        if streak < 0.002:
            continue
        for x in range(w):
            r, g, b = px[x, y]
            px[x, y] = (min(255, int(r + 255 * streak * 0.55)),
                        min(255, int(g + 255 * streak * 0.55)),
                        min(255, int(b + 255 * streak * 0.50)))
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
        # The maps are taken from the UNBAKED albedo: a baked-in body gradient is lighting, not
        # surface, and feeding it to the normal generator would emboss a false cylinder onto a
        # surface that already is one.
        if not round_bake:
            for suffix, made in (('_n', normal_map(img)),
                                 ('_mrs', mrs_map(img, cap_px, shaft_sw, cap_sw))):
                mpath = os.path.join(OUT, name + suffix + '.png')
                made.save(mpath, optimize=True)
                print('wrote', os.path.relpath(mpath, ROOT), os.path.getsize(mpath), 'bytes')
        else:
            img = bake_round(img)
            img = bake_specular(img)
        path = os.path.join(OUT, name + '.png')
        img.save(path, optimize=True)
        print('wrote', os.path.relpath(path, ROOT), os.path.getsize(path), 'bytes')


if __name__ == '__main__':
    main()
