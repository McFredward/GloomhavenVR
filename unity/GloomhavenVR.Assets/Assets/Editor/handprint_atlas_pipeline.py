#!/usr/bin/env python3
"""GloomhavenVR — derive Imported/Textures/handprints_alb.png from a CC0 photo.

Editor-only (like fire_atlas_pipeline.py, cobweb_pipeline.py,
polyhaven_pipeline.py and star_catalogue.py): this file never enters the player
build. Only the derived PNG lands in the tree, and the SOURCE PHOTOGRAPH IS
NEVER COMMITTED — the pipeline downloads it, exactly as the other three do.

============================================================================
WHY THIS FILE EXISTS — the user's instruction, verbatim (wave 2):

  "Sie sind keine wirklichen Hände / es schwebt über den Mauern / es ist
   kackbraun statt blutig / die Position ist nicht gut, da ein Teil davon über
   dem Eingang schwebt wo gar keine Mauer ist. Nutze hier irgendwelche
   Texturen aus dem Internet die tatsächlich Horror verursachen könnten."

The prints used to be built from signed-distance capsules — a palm capsule
plus finger capsules, min-unioned and smoothed (BuildEnvironments.HauntHands,
now deleted). That grammar can only produce a mitten with sausages. It is the
same structural failure the haunt atlas's own header already diagnosed for the
SDF faces: "A handful of SDF primitives can only produce a SILHOUETTE WITH
FEATURES DRAWN ON IT, and a silhouette with features drawn on it is exactly
the grammar of a pictogram."

A real photograph carries, for free, everything the capsules could not fake:

  1. THE ARCH OF THE PALM IS MISSING. A flat hand pressed to a wall touches at
     the heel, the thenar and hypothenar pads and the finger pads; the hollow
     of the palm often does not touch at all. Every print in the source has a
     clean hole through the middle of the palm. The capsule version had a
     solid palm, which is the single loudest "this is a pictogram" tell.
  2. THE THUMB IS A DETACHED ISLAND, offset and rotated.
  3. FINGERS BREAK INTO PAD SEGMENTS, with gaps at the joints.
  4. GRAVITY DRIPS that wander, thin as they go, and have a bead at the head.
  5. FOUR DISTINCT HANDS, left and right. Three prints off one spec at three
     scales is a stencil.

============================================================================
SOURCE (see Bundle/Environments/License.md for the licence trail, quoted
verbatim there with the URL and the date it was read).

  Flickr, "handprints 4" by lisafree54 (Lisa Ann Yount) — CC0 1.0
    page      https://www.flickr.com/photos/136594255@N06/26383403281
    original  https://live.staticflickr.com/1679/26383403281_ba8bfae8de_o.jpg
    2730 x 1820, Canon PowerShot SX260 HS, EXIF intact.
    The photo page's own embedded JSON carries
      "license": "https://creativecommons.org/publicdomain/zero/1.0/"
    and Flickr licence id 9 (Public Domain Dedication).

  Measured first-hand from the file by this pipeline's author, and the numbers
  agree with the research lane's to within a threshold's worth:
    ink coverage 7.9-8.4% (flat across thresholds 0.02..0.15, i.e. the key is
    ONE step with essentially no partial band), darkest 5% of ink
    sRGB (142, 12, 5), mean ink sRGB (174, 82, 65), mean saturation 0.62.

  CAVEAT THE SOURCE FORCES ON US, and it is the whole of section "COLOUR"
  below: it is red PAINT, not blood. Its hue is terracotta and its value is
  far too high. The geometry is kept; the colour is re-authored from scratch.

============================================================================
THE OUTPUT, and why it is shaped like this.

  handprints_alb.png — 768 x 256 RGBA, three 256x256 TILES side by side.
  BuildEnvironments.MakeHauntAtlas stamps them into Env_Haunt.png tiles
  8, 10 and 11 and applies the masonry mortar mask (which needs the wall UV,
  which only the bake knows). Nothing at runtime reads this file: it is a
  bake-time input decoded with ImageConversion from its own bytes.

  THREE TILES, NOT THREE PRINTS IN ONE TILE, and this is the resolution fix.
  A haunt tile is 256 px. The shipped card was 1.24 m square with three prints
  in it, so a print got ~80 px and 0.41 x 1.09 m of wall — 5.5x life size. At
  LIFE SIZE (19 cm) three prints spread 40 cm apart along a wall need ~1 m of
  card, which at 256 px is 3.9 mm per texel and 25 texels across a hand: the
  missing palm arch and the pad gaps are then 1-2 texels and simply are not
  there. One tile per print instead gives each print a 0.22-0.38 m square of
  its own, i.e. 0.9-1.5 mm per texel and 100+ texels across a hand, at zero
  cost in atlas memory — tiles 10..15 were empty.

  CHANNELS. EnvHaunt.shader's decal path is FIXED (another lane owns the
  shader this round) and reads
      col = i.color.rgb * T.r + _Fill.rgb * T.b ;  rim *= T.g ;  alpha *= T.a
  so the channels are re-authored to make that composition evaluate a
  THICKNESS RAMP:
      R  weight on the card's own colour K = the THIN-FILM red
      G  wetness / specular mask -> the shader's rim term, a cold gleam
      B  weight on the material's _Fill = F = the DEEP, near-black stop
      A  coverage, hard photographic edges, zero at the tile border
  Both R and B are stored sRGB-ENCODED, because Env_Haunt.png is imported with
  sRGBTexture=1 and the GPU therefore linearises RGB (but never A) on sample.

============================================================================
COLOUR — the one place realism was the trap.

Dried blood really is cocoa-brown; the shipped key (0.105, 0.090, 0.070),
hue 30 deg, saturation 0.33, was a *correct* colour for a mark that has been
there a week. It was rejected because it is mud, and the fix is NOT to make it
browner or redder as a fill: blood has no single colour. Its colour is a
function of OPTICAL DEPTH — near-black in bulk, bright saturated red only
where the film is thin. So thickness is baked per texel and colour is a ramp:

    optical depth          sRGB              linear             what it is
    thinnest 1-2 mm edge   0.62,0.10,0.06    0.3424,0.0100,0.0049  reads RED
    bulk of the print      0.20,0.020,0.014  0.0331,0.0015,0.0011  barely chromatic
    deepest (drip head)    0.06,0.005,0.005  0.0049,0.0004,0.0004  near black

Those three stops are very nearly coplanar with the origin (the ramp is mostly
a value ramp with a modest chroma shift), which is what makes the shader's
two-basis composition able to carry it at all. The basis is chosen so both
weights stay inside [0,1]:

    K = the thin stop, exactly            -> the card's vertex COLOR
    F = (bulk - 0.05 K)                   -> the material's _Fill
    r,b solved per texel by least squares against the ramp, then clamped.

The residual is printed and asserted: it must stay under 0.01 sRGB per
channel, i.e. under a quantisation step of the 8-bit atlas.

WETNESS. The source's baked-in white specular blobs are NOT flattened away —
they are the one cue that separates blood from rust, and they are extracted
rather than kept in place: a greyscale CLOSING of the optical density removes
the specular dips, the difference (closed - raw) IS the specular mask and goes
to G, and the closed density is what the thickness ramp is measured on. So the
highlight stops lying about thickness and starts being a gleam the shader adds
with the room's own rim colour.

============================================================================
Run:
  python3 handprint_atlas_pipeline.py <dir with the source jpg> <out.png>
  (the dir may be empty: the source is fetched into it if missing)
Needs: python3, numpy, Pillow. No scipy — every morphological step below is a
separable shifted-max, which is all this needs and is 20 lines.
"""
import os
import sys
import urllib.request

import numpy as np
from PIL import Image

SRC_URL = "https://live.staticflickr.com/1679/26383403281_ba8bfae8de_o.jpg"
SRC_NAME = "26383403281_ba8bfae8de_o.jpg"
SRC_SIZE = (2730, 1820)

TILE = 256                 # == BuildEnvironments.HauntTile
INK_T = 0.06               # chroma threshold; see the flat-coverage note above

# ---- the three colour stops, sRGB as authored, converted to linear below ----
STOP_THIN = (0.62, 0.10, 0.06)
STOP_BULK = (0.20, 0.020, 0.014)
STOP_DEEP = (0.06, 0.005, 0.005)
K_OF_BULK = 0.05           # how much of the thin stop sits inside the bulk stop
# Headroom between the card's vertex colour and the atlas's R weight. It is 1.0,
# and that was checked rather than assumed: the built haunt mesh's COLOR channel
# is Float32x4 (Env_C_Haunt.asset, m_Channels[3]: format 0, dimension 4), not
# UNorm8, so the card can carry an arbitrarily small colour without quantising
# and the atlas keeps all 255 levels for the ramp. EnvRoomBuilder divides nothing
# by this; if a future round ever makes that stream 8-bit, raise it here and
# multiply EnvRoomBuilder's card colour by the same number.
KEY_SCALE = 1.0


def srgb_to_linear(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, np.float64), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1.0 / 2.4) - 0.055)


# ----------------------------------------------------------------- morphology
def _shift_max(a, r, axis):
    """max over a (2r+1) window along one axis, by log2(r) doublings."""
    out = a
    k, rem = 1, r
    while rem > 0:
        s = min(k, rem)
        out = np.maximum(np.maximum(out, np.roll(out, s, axis=axis)),
                         np.roll(out, -s, axis=axis))
        rem -= s
        k *= 2
    return out


def dilate(a, r):
    return _shift_max(_shift_max(a, r, 0), r, 1)


def erode(a, r):
    return -dilate(-a, r)


def close(a, r):
    return erode(dilate(a, r), r)


def blur(a, r):
    """separable box blur, radius r texels, edge-clamped."""
    out = a.astype(np.float64)
    for axis in (0, 1):
        pad = [(0, 0), (0, 0)]
        pad[axis] = (r, r)
        p = np.pad(out, pad, mode="edge")
        c = np.cumsum(p, axis=axis)
        c = np.concatenate([np.zeros_like(np.take(c, [0], axis=axis)), c], axis=axis)
        lo = np.take(c, range(0, out.shape[axis]), axis=axis)
        hi = np.take(c, range(2 * r + 1, out.shape[axis] + 2 * r + 1), axis=axis)
        out = (hi - lo) / (2.0 * r + 1.0)
    return out


def dist_inside(mask, n):
    """distance to the nearest non-mask texel, saturating at n. Cheap: n
    erosions. Only ever used with n = 3, which is the 1-2 mm thin edge."""
    d = np.zeros(mask.shape, np.float64)
    cur = mask.astype(np.float64)
    for _ in range(n):
        cur = erode(cur, 1)
        d += cur
    return d


# ------------------------------------------------------------- source regions
# WHICH INK BELONGS TO WHICH PRINT. The four prints in the source are widely
# separated but their drips and detached thumbs interleave, so a bounding box
# per print does not separate them. These predicates were derived by inspection
# of THIS file (they are checked below against the coverage and bounding box
# each one must produce, so a different photograph fails loudly rather than
# silently keying half a hand).
#
#   print 1  top-left     adult, TWO long gravity drips, detached thumb,
#                         a palm broken clean through the middle
#   print 2  bottom-left  adult, fingers fanned, FOUR short drips
#   print 3  top-right    the crispest palm arch in the picture — a C
#   print 4  mid-right    unused
def region(x, y, which):
    if which == 1:
        # its two drips run to y~1230 and its lowest detached pad to y~1140;
        # print 2's palm starts at y~1245 under the second drip, so the
        # boundary steps down as x grows
        return (((x < 540) & (y < 1330))
                | ((x >= 540) & (x < 690) & (y < 1205))
                | ((x >= 690) & (x < 810) & (y < 940)))
    if which == 2:
        return (x < 1400) & ~region(x, y, 1)
    if which == 3:
        # print 4's topmost finger reaches y~690 at x~1960; print 3's own
        # thumb island ends at y~588 there. The gap is 100 px wide.
        return (x >= 1930) & (y < 730) & ~((x < 2060) & (y > 640))
    if which == 4:
        return (x >= 1400) & (y >= 800)
    raise ValueError(which)


# print, expected ink coverage of the whole photo, expected ink bbox (x0,x1,y0,y1)
REGION_GATE = {
    1: (0.0312, (257, 770, 264, 1215)),
    2: (0.0196, (578, 1119, 982, 1649)),
    3: (0.0154, (1994, 2471, 177, 715)),
}
GATE_TOL_PX = 24
GATE_TOL_COV = 0.004


# ------------------------------------------------------------------ the marks
# Each entry is one tile of the derived PNG.
#   src        which print of the photograph
#   tile_m     the physical side of the square this tile is drawn on, metres
#   hand_m     the hand's own length (fingertip to heel), metres — THE scale
#   top_frac   where the fingertips sit in the tile, 0 = top edge
#   flip       mirror horizontally (handedness)
#   smear      (dx, dy, length) drag in TEXELS, or None
#
# LIFE SIZE IS NON-NEGOTIABLE. An adult handprint is 185-195 mm long and
# 90-100 mm across. In VR the player has his own hands in the frame as a
# reference; nothing survives 5.5x. hand_m is measured against the print's own
# fingertip-to-heel extent, so it is the real anatomical length and not a
# bounding box that a drip can stretch.
#
# THE ANOMALY IS SCALE, NOT DIGIT COUNT. The old middle print had SIX fingers.
# A wrongness the player has to COUNT is a joke, not a fright — and at 5x life
# size it read as a cartoon. Mark 2 is a CHILD's hand (132 mm) between two
# adults', descending a wall with a smear at the bottom. That is an anomaly of
# scale, which works pre-attentively, and it is the one the brief asked for.
MARKS = [
    dict(name="contact", src=1, tile_m=0.340, hand_m=0.190, top_frac=0.055,
         flip=False, smear=None),
    dict(name="child", src=3, tile_m=0.220, hand_m=0.132, top_frac=0.090,
         flip=True, smear=None),
    dict(name="smear", src=2, tile_m=0.380, hand_m=0.190, top_frac=0.300,
         flip=False, smear=(8.0, -46.0, 1.0)),
]


def fetch(src_dir):
    path = os.path.join(src_dir, SRC_NAME)
    if not os.path.exists(path):
        os.makedirs(src_dir, exist_ok=True)
        print("fetching %s" % SRC_URL)
        urllib.request.urlretrieve(SRC_URL, path)
    im = Image.open(path).convert("RGB")
    if im.size != SRC_SIZE:
        raise SystemExit("expected a %dx%d source, got %s — this is not "
                         "'handprints 4'." % (SRC_SIZE + (im.size,)))
    return np.asarray(im).astype(np.float64) / 255.0


def key_ink(rgb):
    """Key out of white in ONE threshold. The background is paper; the ink is
    the only chromatic thing in the frame, so R - min(G,B) separates them with
    no rotoscoping. Verified: coverage moves only 8.41% -> 7.80% as the
    threshold goes 0.02 -> 0.15, i.e. the transition band is a texel wide, and
    the specular highlights on the wet paint keep chroma >= 0.15 so they never
    key out as holes."""
    return rgb[:, :, 0] - np.minimum(rgb[:, :, 1], rgb[:, :, 2])


def extract(rgb, chroma, which):
    """One print: its alpha, its optical density and its specular mask, at
    source resolution, cropped tight."""
    h, w = chroma.shape
    xx = np.arange(w)[None, :] * np.ones((h, 1), np.int32)
    yy = np.arange(h)[:, None] * np.ones((1, w), np.int32)
    sel = region(xx, yy, which)
    ink = (chroma > INK_T) & sel

    cov = ink.mean()
    ys, xs = np.nonzero(ink)
    bbox = (int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max()))
    want_cov, want_box = REGION_GATE[which]
    if abs(cov - want_cov) > GATE_TOL_COV:
        raise SystemExit("print %d keyed %.4f of the frame, expected %.4f — the "
                         "source photograph is not the one this was derived from."
                         % (which, cov, want_cov))
    if max(abs(a - b) for a, b in zip(bbox, want_box)) > GATE_TOL_PX:
        raise SystemExit("print %d ink bbox %s, expected %s — the source "
                         "photograph is not the one this was derived from."
                         % (which, bbox, want_box))

    # a soft alpha over the same threshold, so the edge is a real ramp and not
    # a staircase; the AREA downsample later is what actually resolves it
    alpha = np.clip((chroma - INK_T) / 0.10, 0.0, 1.0) * sel

    # OPTICAL DENSITY of the paint. Green is the channel a red pigment absorbs
    # most, so it has by far the best dynamic range: paper G = 1.0 -> OD 0, the
    # darkest ink G = 12/255 -> OD 1.33.
    g = np.clip(rgb[:, :, 1], 1.0 / 255.0, 1.0)
    od = -np.log10(g) * ink

    x0, x1, y0, y1 = bbox
    m = 6
    sl = (slice(max(0, y0 - m), min(h, y1 + m + 1)),
          slice(max(0, x0 - m), min(w, x1 + m + 1)))
    return alpha[sl], od[sl], ink[sl]


def to_tile(mark, alpha_s, od_s, ink_s):
    """Scale one print to life size, drop it in its tile, and return the tile's
    alpha, thickness and wetness planes."""
    # ---- the hand's OWN length, excluding drips. Drips are thin, so a row
    # that is part of the hand carries at least 12% of the widest row's ink.
    rows = ink_s.sum(axis=1).astype(np.float64)
    fat = np.nonzero(rows > 0.12 * rows.max())[0]
    hand_px = float(fat.max() - fat.min() + 1)
    if not (0.35 < hand_px / ink_s.shape[0] < 1.02):
        raise SystemExit("mark %s: hand rows are %.0f of %d — the drip/hand "
                         "split failed." % (mark["name"], hand_px, ink_s.shape[0]))

    # ---- scale: metres per texel of the tile, then px of the source per texel
    m_per_texel = mark["tile_m"] / TILE
    target_hand_texels = mark["hand_m"] / m_per_texel
    scale = target_hand_texels / hand_px

    nh = max(1, int(round(alpha_s.shape[0] * scale)))
    nw = max(1, int(round(alpha_s.shape[1] * scale)))
    if nw > TILE - 8 or nh > 2 * TILE:
        raise SystemExit("mark %s scales to %dx%d, which will not fit a %d tile."
                         % (mark["name"], nw, nh, TILE))

    def rs(plane):
        # AREA resample (BOX). Coverage-correct in both directions, which is
        # what an alpha and a density both need; a naive bilinear halves the
        # coverage of a 1 px drip.
        return np.asarray(Image.fromarray(plane.astype(np.float32), "F")
                          .resize((nw, nh), Image.BOX), np.float64)

    a = rs(alpha_s)
    od = rs(od_s)
    ik = rs(ink_s.astype(np.float32))

    if mark["flip"]:
        a, od, ik = a[:, ::-1], od[:, ::-1], ik[:, ::-1]

    # ---- de-highlight: a greyscale CLOSING removes the small BRIGHT (low-OD)
    # specular dips inside the paint. The closed field is honest thickness; the
    # difference is the gleam.
    r = max(2, int(round(0.0035 / m_per_texel)))     # ~3.5 mm
    od_c = close(od, r)
    wet = np.clip((od_c - od) / max(1e-6, np.percentile((od_c - od)[ik > 0.5], 99)), 0.0, 1.0)

    # ---- place in the tile
    tile_a = np.zeros((TILE, TILE), np.float64)
    tile_d = np.zeros((TILE, TILE), np.float64)
    tile_w = np.zeros((TILE, TILE), np.float64)
    ox = (TILE - nw) // 2
    oy = int(round(mark["top_frac"] * TILE))
    hh = min(nh, TILE - oy)
    tile_a[oy:oy + hh, ox:ox + nw] = a[:hh]
    tile_d[oy:oy + hh, ox:ox + nw] = od_c[:hh]
    tile_w[oy:oy + hh, ox:ox + nw] = wet[:hh]

    # ---- THE SMEAR. The hand slid: fluid is left behind ALONG the path, so
    # the streak trails back the way it came (up, and toward +u = the wall
    # direction the body came from) and thins as it goes. It is a THIN film,
    # so it is given the thin end of the ramp, and it is the wettest thing on
    # the tile.
    if mark["smear"] is not None:
        dx, dy, _ = mark["smear"]
        n = int(round(max(abs(dx), abs(dy))))
        sm_a = tile_a.copy()
        sm_w = tile_w.copy()
        for s in range(1, n + 1):
            t = s / float(n)
            sx = int(round(dx * t))
            sy = int(round(dy * t))
            # thins, and wanders: the taper is the fluid running out
            k = (1.0 - t) ** 1.9 * 0.92
            shifted = np.roll(np.roll(tile_a, sy, axis=0), sx, axis=1) * k
            sm_a = np.maximum(sm_a, shifted)
            sm_w = np.maximum(sm_w, np.roll(np.roll(tile_w, sy, axis=0), sx, axis=1) * k)
        streak = np.clip(sm_a - tile_a, 0.0, 1.0)
        tile_a = np.maximum(tile_a, sm_a * 0.78)
        # the streak is thin film: pull its depth to the bright end
        tile_d = np.maximum(tile_d * (1.0 - streak), tile_d)
        tile_d = np.where(streak > 0.05, tile_d * 0.35, tile_d)
        tile_w = np.clip(np.maximum(tile_w, sm_w * 0.75), 0.0, 1.0)

    # ---- the tile border. A print does not fade out towards a rectangle: the
    # OLD tile multiplied everything by HSStep(1.00, 0.92, max(|x|,|y|)), which
    # is a card vignette and is the classic sticker tell. It is DELETED. What
    # replaces it is a hard requirement that the mark simply does not reach the
    # border: alpha is forced to zero in the outer 2 texels and the bake
    # asserts nothing was cut off.
    # FIVE texels, not two: MakeHauntAtlas forces a five-texel guard band to
    # zero round every cell (trilinear at distance averages a neighbourhood
    # several texels wide and an atlas is not self-clamping), so anything inside
    # it is destroyed anyway and the gate has to be measured where the destruction
    # happens.
    edge = np.ones((TILE, TILE), np.float64)
    edge[:5, :] = edge[-5:, :] = edge[:, :5] = edge[:, -5:] = 0.0
    lost = float((tile_a * (1.0 - edge)).sum())
    if lost > 1.0:
        raise SystemExit("mark %s: %.1f texels of alpha are inside the 5-texel "
                         "guard band — the print is clipped by its own tile."
                         % (mark["name"], lost))
    tile_a *= edge
    return tile_a, tile_d, tile_w


def ramp_weights(depth):
    """Thickness -> the two atlas weights, by least squares against the basis
    the shader can compose. Returns (r, b, max sRGB residual)."""
    L1 = srgb_to_linear(STOP_THIN)
    L2 = srgb_to_linear(STOP_BULK)
    L3 = srgb_to_linear(STOP_DEEP)
    K = L1
    F = L2 - K_OF_BULK * K
    if np.any(F <= 0):
        raise SystemExit("the fill basis went negative; K_OF_BULK is too large.")

    d = np.clip(depth, 0.0, 1.0)[..., None]
    want = np.where(d < 0.5,
                    L1 + (L2 - L1) * (d / 0.5),
                    L2 + (L3 - L2) * ((d - 0.5) / 0.5))

    KK, KF, FF = float(K @ K), float(K @ F), float(F @ F)
    Kw = want @ K
    Fw = want @ F
    det = KK * FF - KF * KF
    r = np.clip((Kw * FF - Fw * KF) / det, 0.0, 1.0)
    # RE-SOLVE b AGAINST THE CLAMPED r. Clamping the two unconstrained weights
    # independently is wrong and it is wrong exactly at the deep end, where the
    # ideal r goes slightly negative: leaving b at its unconstrained value then
    # over-shoots the darkest stop by 0.023 sRGB in red, which is 6 quantisation
    # steps and visible as a plum cast on the drip heads.
    b = np.clip((Fw - r * KF) / FF, 0.0, 1.0)
    r = np.clip((Kw - b * KF) / KK, 0.0, 1.0)
    got = r[..., None] * K + b[..., None] * F
    res = float(np.abs(linear_to_srgb(np.clip(got, 0, 1))
                       - linear_to_srgb(np.clip(want, 0, 1))).max())
    return r, b, res, K, F


def main(src_dir, out_path):
    rgb = fetch(src_dir)
    chroma = key_ink(rgb)
    print("source keyed: ink coverage %.2f%%, mean ink sRGB %s, darkest 5%% %s"
          % ((chroma > INK_T).mean() * 100,
             (rgb[chroma > INK_T].mean(axis=0) * 255).round(1),
             (rgb[chroma > INK_T][np.argsort(rgb[chroma > INK_T].mean(axis=1))
                                  [:max(1, int((chroma > INK_T).sum() * 0.05))]]
              .mean(axis=0) * 255).round(1)))

    out = np.zeros((TILE, TILE * len(MARKS), 4), np.uint8)
    worst = 0.0
    for i, mark in enumerate(MARKS):
        a_s, od_s, ik_s = extract(rgb, chroma, mark["src"])
        a, od, wet = to_tile(mark, a_s, od_s, ik_s)

        ink = a > 0.5
        if ink.sum() < 200:
            raise SystemExit("mark %s came out empty." % mark["name"])

        # ---- THICKNESS. Density normalised on its own percentiles inside the
        # ink, then forced thin at the outer 1-2 mm of every mark: a film has
        # to run out at its own edge, and that is where blood is red.
        lo, hi = np.percentile(od[ink], [4, 96])
        d = np.clip((od - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        m_per_texel = mark["tile_m"] / TILE
        n_edge = max(1, int(round(0.0018 / m_per_texel)))   # 1.8 mm
        d = np.minimum(d, dist_inside(a > 0.35, n_edge) / float(n_edge))
        d *= (a > 0.02)

        r, b, res, K, F = ramp_weights(d)
        worst = max(worst, res)
        r *= (a > 0.02)
        b *= (a > 0.02)

        # wetness: only where there IS a film, softened by one texel so the
        # gleam is not a set of single-texel sparkles
        g = np.clip(blur(wet, 1) * (a > 0.2), 0.0, 1.0)

        sl = slice(i * TILE, (i + 1) * TILE)
        out[:, sl, 0] = np.round(linear_to_srgb(r / KEY_SCALE) * 255).astype(np.uint8)
        out[:, sl, 1] = np.round(linear_to_srgb(g) * 255).astype(np.uint8)
        out[:, sl, 2] = np.round(linear_to_srgb(b) * 255).astype(np.uint8)
        out[:, sl, 3] = np.round(np.clip(a, 0, 1) * 255).astype(np.uint8)

        print("  tile %d '%s': %.3f m square (%.2f mm/texel), hand %.0f mm, "
              "coverage %.2f%%, wet peak %.2f, ramp residual %.4f sRGB"
              % (i, mark["name"], mark["tile_m"], m_per_texel * 1000,
                 mark["hand_m"] * 1000, ink.mean() * 100, g.max(), res))

    if worst > 0.01:
        raise SystemExit("the thickness ramp cannot be carried by the shader's "
                         "two-basis composition: worst residual %.4f sRGB." % worst)

    _, _, _, K, F = ramp_weights(np.zeros(1))
    Image.fromarray(out, "RGBA").save(out_path, optimize=True)
    print("wrote %s  %dx%d RGBA" % (out_path, out.shape[1], out.shape[0]))
    print("  K (card vertex COLOR, LINEAR, written raw)  = %s" % np.round(K, 6))
    print("    x KEY_SCALE %.5f, which is what the card carries = %s"
          % (KEY_SCALE, np.round(K * KEY_SCALE, 6)))
    print("  F (material _Fill, set as sRGB because Unity")
    print("     gamma-converts material colours)         = %s"
          % np.round(linear_to_srgb(F), 6))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
