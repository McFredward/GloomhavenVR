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

# ---------------------------------------------------------------------------------------------
#  THE STRIPS ARE UNWRAPPED RODS, NOT MATERIAL SWATCHES.
#
#  The first set asked for six seamless material tiles and composited them. The user's verdict was
#  that they were "deutlich besser" on the design sheet and that the rods needed "Details ...
#  Dekorationen, Kratzer etc. ... immersiv und lebendig". A tile cannot carry any of that: it is
#  uniform by definition, and uniform is exactly what reads as plastic.
#
#  So each rod's shaft is now generated as ITS OWN UNWRAP, with the layout stated to the model:
#
#      HORIZONTAL  = along the rod          (grain, scratches, wear run this way)
#      VERTICAL    = around the circumference — so a decorative RING must be drawn as a
#                    VERTICAL BAND, and anything drawn as a horizontal band would spiral
#
#  The aspect is deliberate too. The shaft band is 820 x 256 texels covering ~0.30 m along the rod
#  and pi x 0.028 = 0.088 m around it, i.e. 2733 px/m against 2909 px/m — near enough isotropic. So
#  the generated image is CROPPED to 3.2:1 rather than squashed into it; squashing would stretch
#  every scratch by four.
# ---------------------------------------------------------------------------------------------

UNWRAP = ("A flat orthographic TEXTURE UNWRAP for a game asset, filling the entire frame edge to "
          "edge with no border, no background, no perspective, no shadow, no vignette, no text and "
          "no watermark. Completely even lighting: this is an albedo map, colour only, with no "
          "baked highlight and no baked shadow. It is the surface of a slender turned HANDLE rod "
          "unrolled flat: the HORIZONTAL axis runs ALONG the rod and the VERTICAL axis runs AROUND "
          "its circumference, so any decorative ring must appear as a VERTICAL band spanning the "
          "full height, and the grain must run HORIZONTALLY. ")

WEAR = ("Make it lived-in and specific rather than uniform: fine scratches at shallow angles, a "
        "few deeper nicks and chips, small dents, uneven staining, a subtly worn and darkened band "
        "across the middle third where a hand has held it for years, and cleaner less-touched "
        "surface toward the two ends. No two areas of the frame should look the same. ")

SWATCHES = {
    'sw_oak': UNWRAP + WEAR + (
        "Material: aged honey-brown oak, straight horizontal grain with darker medullary streaks. "
        "Decoration: three narrow VERTICAL incised fillet lines, unevenly spaced, one of them "
        "doubled, cut shallowly into the wood and slightly darkened in the groove. A short vertical "
        "band of tiny chisel facets near one end. The wood is oiled, not painted."),

    'sw_steel': UNWRAP + WEAR + (
        "Material: cool blue-grey forged steel, mottled cloudy patina, faint hammer facets. "
        "Decoration: two narrow VERTICAL bands of shallow diagonal knurling, and one plain raised "
        "vertical collar line. Scattered small rust-brown pinpricks in the deeper pits. "
        "Utilitarian and cold — no gold, no warm tones anywhere."),

    'sw_bronze': UNWRAP + WEAR + (
        "Material: aged bronze, warm gold-brass where handling has polished it and blue-green "
        "verdigris crusted into everything that is not touched. Decoration: a VERTICAL band of "
        "engraved interlaced knotwork, and two narrower vertical beaded lines elsewhere, all "
        "filled with verdigris in the recesses and rubbed bright on the crowns."),

    'sw_walnut': UNWRAP + WEAR + (
        "Material: dark oiled walnut, fine horizontal grain, deep neutral brown, quiet. "
        "Decoration: two narrow VERTICAL inlaid brass pinstripes, slightly tarnished, and a "
        "sparse scatter of tiny brass pins. Restrained — this rod sits against a page of text and "
        "must not shout."),

    'sw_antique': UNWRAP + (
        "Material: dark antique bronze, tarnished to a deep brown-bronze with nearly black "
        "recesses and a faint warm sheen on the high points. Decoration: closely spaced fine "
        "VERTICAL beading across the whole frame, like the knurled collar of an old door fitting, "
        "with the tarnish heaviest between the beads. Small casting pits and a few bright rubbed "
        "spots where a thumb would land."),

    'sw_gold': UNWRAP + (
        "Material: warm gilt bronze, softly polished, with age in it rather than showroom shine. "
        "Decoration: closely spaced fine VERTICAL beading across the whole frame, the hollows "
        "between the beads darkened with old tarnish and a trace of verdigris, the crowns rubbed "
        "bright. A few fine scratches and one small dent."),
}

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
    'sw_oak':      (0.30, 0.27),
    'sw_walnut':   (0.27, 0.31),
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
                             'size': '1536x1024', 'n': 1}).encode('utf-8'),
            headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                data = json.load(r)
        except urllib.error.HTTPError as e:
            sys.exit(f'{name}: HTTP {e.code} {e.read().decode("utf-8", "replace")[:300]}')
        open(path, 'wb').write(base64.b64decode(data['data'][0]['b64_json']))
        print('generated', name)


def cs_const(name):
    """Read one float constant out of GrabBar.cs.

    Every number this script needs about the mesh is READ, never re-typed. The band boundary, the
    knob's arc and the cap's length all live in the C# because the mesh is authored there; a copy
    here would be one more pair of numbers to keep in step, and this project has paid for that
    shape of drift more than once.
    """
    if not os.path.exists(CS):
        return None
    m = re.search(name + r'\s*=\s*([0-9.]+)f', io.open(CS, encoding='utf-8').read())
    return float(m.group(1)) if m else None


def cap_fraction():
    return cs_const('ShaftU0')


def dome_fraction():
    """How much of the CAP band, in u, the smooth knob occupies — the rest is the beaded collar.

    The cap's u runs from the dome's tip to the shoulder across CapLengthInRadii, and the dome
    itself is DomeRadius x (1 - cos(sweep)) long. Without this split the beading generated for the
    collar is painted across the whole cap, and the knob renders as a beehive instead of the smooth
    ball the design sheet shows.
    """
    dome_r = cs_const('DomeRadius')
    sweep = cs_const('DomeSweepDegrees')
    cap_len = cs_const('CapLengthInRadii')
    if None in (dome_r, sweep, cap_len) or cap_len <= 0:
        return None
    return (dome_r * (1.0 - math.cos(math.radians(sweep)))) / cap_len


def band(swatch_dir, name, width, height):
    """Crop one unwrap to the band's aspect, then scale. NEVER squashes.

    The generated unwraps are near-square; a shaft band is 3.2:1. Resizing straight to the band
    would stretch every scratch and every incised line by a factor of four along the rod, which is
    precisely the smeared, characterless look this whole pass exists to remove. So the largest
    region of the SOURCE aspect is cropped first and only then scaled.
    """
    im = grade(Image.open(os.path.join(swatch_dir, name + '.png')).convert('RGB'), name)
    sw, sh = im.size
    want = width / float(height)
    have = sw / float(sh)
    if have > want:                       # source too wide -> trim its sides
        nw = int(round(sh * want))
        left = (sw - nw) // 2
        im = im.crop((left, 0, left + nw, sh))
    else:                                 # source too tall -> trim top and bottom
        nh = int(round(sw / want))
        top = (sh - nh) // 2
        im = im.crop((0, top, sw, top + nh))
    return im.resize((width, height), Image.LANCZOS)


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


def cap_band(swatch_dir, name, width, height, dome_frac):
    """The cap's strip: a SMOOTH knob, then the beaded collar.

    The generated cap unwraps are beaded edge to edge, which is right for the collar and wrong for
    the ball — painted across the dome it renders as a beehive. The dome's share of the band is
    computed from the mesh's own constants, and over that stretch the beading is dissolved away
    with a heavy blur, leaving the material (tarnish, pits, rubbed spots) but not the ridges. A
    short crossfade keeps the join from reading as a hard edge.
    """
    sharp = band(swatch_dir, name, width, height)
    smooth = sharp.filter(ImageFilter.GaussianBlur(max(2.0, width * 0.10)))
    out = sharp.copy()
    px_out = out.load()
    px_s = smooth.load()
    px_h = sharp.load()
    dome_px = dome_frac * width
    fade = max(1.0, width * 0.10)
    for x in range(width):
        # 1 at the tip (all smooth), 0 past the crossfade (all beading).
        t = 1.0 - min(1.0, max(0.0, (x - (dome_px - fade)) / fade))
        if t >= 0.999:
            for y in range(height):
                px_out[x, y] = px_s[x, y]
        elif t > 0.001:
            for y in range(height):
                a, b = px_s[x, y], px_h[x, y]
                px_out[x, y] = (int(a[0] * t + b[0] * (1 - t)),
                                int(a[1] * t + b[1] * (1 - t)),
                                int(a[2] * t + b[2] * (1 - t)))
    return out


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
    dome_frac = dome_fraction()
    if dome_frac is None:
        sys.exit('GrabBarMesh dome constants not parseable — refusing to guess where the knob ends.')
    cap_px = int(round(W * frac))
    print(f'dome occupies {dome_frac:.3f} of the cap band -> smooth knob, beaded collar')
    print(f'ShaftU0={frac} -> cap band {cap_px} px of {W}')

    os.makedirs(OUT, exist_ok=True)
    for name, shaft_sw, cap_sw, round_bake in STRIPS:
        img = Image.new('RGB', (W, H))
        shaft_w = W - 2 * cap_px
        img.paste(cap_band(args.swatches, cap_sw, cap_px, H, dome_frac), (0, 0))
        img.paste(band(args.swatches, shaft_sw, shaft_w, H), (cap_px, 0))
        # The far cap is the near cap MIRRORED, so both ends carry the same material and the wrap
        # at u=1 meets u=0 on identical pixels.
        img.paste(cap_band(args.swatches, cap_sw, cap_px, H, dome_frac)
                  .transpose(Image.FLIP_LEFT_RIGHT), (W - cap_px, 0))
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
