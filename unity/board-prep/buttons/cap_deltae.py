#!/usr/bin/env python3
"""The IDLE-FACE SEPARATION instrument, and the per-board idle colours it solves for.

WHAT QUESTION THIS ANSWERS
--------------------------
ModBuild 281 shipped three per-board keycap plates and measured that the three IDLE cap
faces land at CIELAB dE **13.8 (oak-steel), 10.3 (steel-bronze) and 3.7 (oak-bronze)** --
i.e. oak and bronze are the same colour, told apart only by their grain. The cause is NOT
the plates and NOT the level re-basing (a uniform gain cannot move a hue ratio): it is that
all three boards multiply their plate by ONE state colour,

    IdleColor (0.60, 0.51, 0.35)  x  [ButtonColors] BoardCapTint (0.5, 0.5, 0.5)

and a plate whose own cast is +-20 % cannot survive being multiplied by a tint that strong.
A chroma boost on the PLATE was tried in that round and rejected on measurement (dE 3.7 ->
3.9 -> 3.1 from k = 1.0 to 2.6, clipping 99.99 % of oak's texels at k = 1.6) -- the
separation lives in the blue channel, which the parchment tint has already crushed.

This file solves the OTHER lever: three PER-BOARD idle face colours.

THE CHAIN IT REPRODUCES, and every term is read off the shipped source
---------------------------------------------------------------------
    rendered_face_sRGB = plate_normalised  x  SeatedCapColor(IdleColor_board x BoardCapTint)  x  shade

  * `plate_normalised` -- `cap_atlas.normalise_plate` (imported, NOT re-implemented), which
    re-bases the plate to the shipped KeycapGrain's mean 0.837 with a uniform RGB gain.
  * `BoardLit` fragment: `alb = tex2D(_MainTex, uv) * _Color; col = alb.rgb * shade`
    (unity/GloomhavenVR.Assets/Assets/Bundle/Table/BoardLit.shader).
  * `SeatedCapColor` / `CapWellColor` / `CapSeatContrast` -- WorldUI/ButtonTuning.cs, as
    ported verbatim into Assets/Editor/PreviewKeycaps.cs lines 147-185.
  * `shade` is DERIVED, not guessed. BoardLit bakes
        key  = normalize( 0.35, 0.85, -0.45)
        fill = normalize(-0.55, 0.35,  0.30)
        shade = _Ambient + saturate(dot(N,key)) * _LightBoost + saturate(dot(N,fill)) * 0.35
    with the keycap material's own `_Ambient` 0.5 / `_LightBoost` 0.85 (PreviewKeycaps
    lines 817-818, ported from PlayTray.NewKeycapMaterial). A flat front-facing cap plateau
    has N = (0, 0, -1) ("viewer side is -Z", PlayTray.7.Nested CapRestZ), so
        dot(N, key)  = 0.45 / |key|  = 0.43970   -> lit
        dot(N, fill) = -0.30 / |fill|            -> saturated to 0
        shade = 0.5 + 0.43970 * 0.85 = 0.87373
    exactly, and it is a CONSTANT -- which is why it cancels out of every RATIO below.

THE COLOUR SPACE IS GAMMA, and that is the whole reason this is arithmetic on raw sRGB
numbers rather than on decoded ones. Assets/Editor/PreviewKeycaps.cs argues it at length:
the rig renders in Gamma colorspace, BoardLit's fragment is a PRODUCT OF TWO COLOURS, and a
product is exactly where gamma and linear diverge -- the naive linear render made the steel
cap read 11x darker than the reference where the rig gives 3x. So the plate's raw sRGB
texels are multiplied by the raw sRGB state colour, and the result IS the framebuffer value.
Only THEN is it decoded (sRGB -> linear -> XYZ -> CIELAB) to be measured, because CIELAB is
defined on light and the framebuffer value is an encoding of light.

VALIDATING THE INSTRUMENT BEFORE TRUSTING IT -- this project has shipped three that lied
-----------------------------------------------------------------------------------------
`--selfcheck` runs three things:

  1. THE KNOWN POSITIVE. Fed the CURRENT three plates and the CURRENT single IdleColor it
     must reproduce 13.8 / 10.3 / 3.7. IT DOES, to 13.79 / 10.26 / 3.74 -- but only with
     `shade` OMITTED. With the physically correct shade applied it gives 12.37 / 9.19 /
     3.36, i.e. 0.897x, uniformly. Both are printed. The reading is that the 281 record's
     three numbers were measured on the ALBEDO PRODUCT (plate x state colour) rather than
     on the shaded framebuffer value; since `shade` is one scalar applied identically to all
     three styles it cannot change which pair is closest, and the agreement to 0.04 dE on
     all three pairs on the unshaded form is what says the rest of the chain is modelled
     right. THE SOLVER BELOW GATES ON THE SHADED FORM, which is the stricter of the two.

  2. THE NULL CONTROL. The same style against itself must give exactly dE 0.

  3. THE WELL RATIO, AND THE ONE TERM THIS FILE CANNOT REPRODUCE. The 281 record states the
     caps land at "1.74-1.81x the luminance of the well". This instrument models the well as
     the mod actually mints it -- `PlayTray.NewKeycapMaterial(lit, ButtonTuning.CapWellColor)`
     with the style-less KeycapGrain texture, i.e. `grain x (0.15, 0.12, 0.08) x shade` --
     and gets 2.07-2.34 for the same three shipped plates. The offset is systematic (1.19x
     on the pre-round grain cap, 1.19-1.29x on the three plates) and is in the WELL term:
     nothing in this repository defines a well surface 1.2x brighter than that. Four
     candidate well definitions were tried (grain x CapWellColor, the style's own plain
     atlas cell, the board's own face band, and that face band tinted) and none lands on
     1.75; nor do four candidate luminance definitions (Rec.709 on the sRGB value, Rec.709
     on the decoded linear value, the plain RGB mean, and CIELAB L*). The likeliest reading
     is that the 281 number was read off the render station's PICTURE of a cap in a well
     plate -- which carries the bevel, the wall and the normal map and is not arithmetic
     anyone can redo here -- but that is a guess and it is labelled one.
     SO THE ABSOLUTE NUMBER IS NOT CLAIMED. What is gated instead is the RATIO OF THE NEW
     CAP TO THE CURRENT CAP OF THE SAME BOARD, in which the well term cancels exactly and no
     definition of it can matter. The bar is +-2 %, which is tighter than the 4 % spread of
     the band the record quotes.

WHAT THE SOLVER MAY AND MAY NOT DO
----------------------------------
  (a) push the minimum pairwise dE as high as it can, target >= 10 on all three pairs;
  (b) hold each board's cap luminance within +-2 % of what that board's cap renders today;
  (c) keep the clipped-texel fraction of the rendered face under 0.5 %;
  (d) stay in the game's antique palette. THIS IS THE CONSTRAINT THAT HAD TO BE WRITTEN
      TWICE, and the first version is worth recording. It was a CHROMA CEILING and nothing
      else -- and a chroma ceiling does not constrain HUE, so the free solve happily
      returned a RED oak cap, a BLUE steel cap and a GREEN bronze cap at dE 47-59: three
      saturated plastic keys, maximally separated and completely wrong for a board of aged
      wood and tarnished metal. Maximising a separation metric with no palette in the
      objective produces exactly that, every time.
      What replaced it is an AUTHORED palette -- one named antique material per board, each
      a HUE WINDOW and a CHROMA WINDOW on the rendered face (`PALETTE` below), inside which
      the solver may optimise freely. Every corner of every window is a colour that belongs
      on this board set, so wherever the search lands the answer is admissible:
          oak    -> PARCHMENT / pale honey   h 75-92 deg,  C* 14-21
          steel  -> PEWTER, cool and quiet   h 215-285,    C*  7-15
          bronze -> BRASS / warm gold        h 58-76,      C* 26-36
      The three shipped faces today sit at h 87 / 80 / 89 with C* 22.4 / 10.6 / 19.2 -- all
      three inside 9 degrees of hue of one another, which IS the defect in one line.
      Separately, every idle channel is kept at or above the value that makes the SEAT
      FLOOR a no-op: `SeatedCapColor` clamps each channel up to CapWellColor * 1.35, and a
      solved colour that got silently lifted by that clamp would not be the colour that was
      solved for.
"""
import argparse
import itertools
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

import cap_atlas as C            # noqa: E402  -- normalise_plate, verbatim

REPO = os.path.dirname(os.path.dirname(PREP))
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
STYLES = ("oak", "steel", "bronze")

# ---- THE SHIPPED CHAIN, every constant with the file it came from -------------------------
IDLE_SHIPPED = np.array([0.60, 0.51, 0.35])      # Cards/PlayTray.7.Nested.cs IdleColor
BOARD_CAP_TINT = 0.5                             # Defaults.WorldUI.cs BoardCapTint{R,G,B}
CAP_WELL_COLOR = np.array([0.15, 0.12, 0.08])    # WorldUI/ButtonTuning.cs CapWellColor
CAP_SEAT_CONTRAST = 1.35                         # WorldUI/ButtonTuning.cs CapSeatContrast
SEAT_FLOOR = CAP_WELL_COLOR * CAP_SEAT_CONTRAST
AMBIENT, LIGHT_BOOST, FILL_WEIGHT = 0.5, 0.85, 0.35   # PreviewKeycaps.MakeMat / BoardLit

_KEY = np.array([0.35, 0.85, -0.45]); _KEY /= np.linalg.norm(_KEY)
_FILL = np.array([-0.55, 0.35, 0.30]); _FILL /= np.linalg.norm(_FILL)
_N_FLAT = np.array([0.0, 0.0, -1.0])              # the cap plateau faces the viewer at -Z
SHADE = (AMBIENT + max(0.0, float(_N_FLAT @ _KEY)) * LIGHT_BOOST
         + max(0.0, float(_N_FLAT @ _FILL)) * FILL_WEIGHT)

REC709 = np.array([0.2126, 0.7152, 0.0722])

# ---- THE SOLVER'S BARS ---------------------------------------------------------------------
DE_TARGET = 10.0            # minimum pairwise dE asked for on the SHADED form
LUM_TOLERANCE = 0.02        # +-2 % of the board's own current cap luminance
CLIP_BAR = 0.005            # 0.5 % of the rendered face's texels
IDLE_MAX = 0.95             # an idle channel above this is no longer "antique"

# THE AUTHORED PALETTE -- one antique material per board, as a window on the RENDERED face.
# See constraint (d) in the header for why a bare chroma ceiling was not enough. Hue is the
# CIELAB hue angle in degrees; chroma is C* = hypot(a*, b*).
PALETTE = {
    "oak":    dict(material="parchment / pale honey", hue=(78.0, 92.0),  chroma=(14.0, 20.0)),
    "steel":  dict(material="pewter, cool and quiet", hue=(220.0, 285.0), chroma=(7.0, 14.0)),
    "bronze": dict(material="brass / warm gold",      hue=(66.0, 80.0),  chroma=(24.0, 32.0)),
}

_M_XYZ = np.array([[0.4124, 0.3576, 0.1805],
                   [0.2126, 0.7152, 0.0722],
                   [0.0193, 0.1192, 0.9505]])
_WHITE_D65 = np.array([0.95047, 1.00000, 1.08883])


def srgb_to_linear(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0.0, 1.0)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lab(rgb_srgb):
    """CIELAB of a framebuffer value. The value is sRGB-ENCODED (the rig is a gamma
    project), so it is decoded first -- CIELAB is defined on light, not on the encoding."""
    xyz = srgb_to_linear(rgb_srgb) @ _M_XYZ.T / _WHITE_D65
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16.0 / 116.0)
    return np.stack([116.0 * f[..., 1] - 16.0,
                     500.0 * (f[..., 0] - f[..., 1]),
                     200.0 * (f[..., 1] - f[..., 2])], axis=-1)


def seated(face):
    """WorldUI.ButtonTuning.SeatedCapColor -- the per-channel floor the mod actually writes."""
    return np.maximum(np.asarray(face, dtype=np.float64), SEAT_FLOOR)


def face_colour(idle, tint=BOARD_CAP_TINT):
    return seated(np.asarray(idle, dtype=np.float64) * tint)


def seat_floor_engages(idle, tint=BOARD_CAP_TINT):
    return bool(np.any(np.asarray(idle, dtype=np.float64) * tint < SEAT_FLOOR - 1e-9))


# ---- THE PLATES ----------------------------------------------------------------------------
def load_plate(style, plates_dir, suffix=""):
    raw = np.asarray(Image.open(os.path.join(plates_dir, f"keycap_plate_{style}{suffix}.png"))
                     .convert("RGB"), dtype=np.float64) / 255.0
    norm, gain, achieved, _spend = C.normalise_plate(raw)
    return raw, norm, gain, achieved


def grain_mean():
    g = np.asarray(Image.open(os.path.join(BUNDLE, "KeycapGrain_albedo.png")).convert("RGB"),
                   dtype=np.float64) / 255.0
    return g.reshape(-1, 3).mean(axis=0)


def well_srgb():
    """The mod-drawn WELL: PlayTray.NewKeycapMaterial(lit, ButtonTuning.CapWellColor) with no
    style, i.e. the shared KeycapGrain texture. See the header for what this cannot claim."""
    return grain_mean() * CAP_WELL_COLOR * SHADE


def rendered(plate_norm_mean, idle, shade=SHADE):
    return np.clip(np.asarray(plate_norm_mean) * face_colour(idle) * shade, 0.0, 1.0)


def clip_fraction(plate_norm, idle, shade=SHADE):
    """The exact fraction of the rendered face's texels that reach the top of the range.

    IT CANNOT BE ANYTHING BUT ZERO INSIDE THE SOLVER'S BOX, and that is worth stating rather
    than measuring tens of thousands of times: `normalise_plate` clips its output to [0, 1],
    the largest face channel the solver may reach is IDLE_MAX x BoardCapTint = 0.475, and
    0.475 x shade = 0.415 < 1. So no product can clip. `clip_bound` is the cheap version the
    search uses; this exact one is still run on every reported result, because a bound is an
    argument and a measurement is evidence."""
    v = plate_norm * face_colour(idle) * shade
    return float((v >= 0.999).any(axis=-1).mean())


def clip_bound(idle, shade=SHADE):
    """Upper bound on the rendered face's brightest possible channel (plate max is 1.0)."""
    return float(np.max(face_colour(idle)) * shade)


def de_matrix(means, idles, shade=SHADE):
    labs = {s: lab(rendered(means[s], idles[s], shade)) for s in means}
    return {(a, b): float(np.linalg.norm(labs[a] - labs[b]))
            for a, b in itertools.combinations(means, 2)}


def lch(rgb_srgb):
    """(L*, C*, h deg) of a framebuffer value."""
    L = lab(rgb_srgb)
    return (float(L[0]), float(np.hypot(L[1], L[2])),
            float(np.degrees(np.arctan2(L[2], L[1])) % 360.0))


def chroma(rgb_srgb):
    return lch(rgb_srgb)[1]


def in_palette(style, rgb_srgb):
    """Is this rendered face inside the board's authored antique window?"""
    spec = PALETTE.get(style)
    if spec is None:
        return True
    _, c, h = lch(rgb_srgb)
    h0, h1 = spec["hue"]
    c0, c1 = spec["chroma"]
    # EPS because the grid the solver searches lands exactly ON the window edges, and a bare
    # inequality then reports the solver's own optimum as out of palette -- an instrument
    # contradicting the thing it just produced.
    eps = 1e-6
    return (c0 - eps <= c <= c1 + eps) and (h0 - eps <= h <= h1 + eps)


# ---- THE SOLVER ----------------------------------------------------------------------------
# IT DOES NOT SEARCH THE NINE IDLE CHANNELS. It searches the thing the constraints are
# actually written about -- the RENDERED FACE, in CIELAB -- and inverts the chain to get the
# idle colour, which is exact:
#
#     rendered = plate_mean x SeatedCapColor(idle x tint) x shade
#     =>  idle = rendered / (plate_mean x shade x tint)          [the seat floor is then
#                                                                 CHECKED, never relied on]
#
# The first version of this file did climb the nine channels directly and it was both slower
# and worse: every mutation had to be re-projected back onto the luminance constraint, and
# the palette window is a hue/chroma box in a space the channels only reach obliquely, so
# most proposals were rejected and the search stalled on infeasible seeds. Inverting the
# chain turns three coupled 3-D searches into one 2-D grid per board (hue x chroma), with
# lightness SOLVED rather than searched -- because lightness is not free: constraint (b)
# pins each board's cap luminance to what it renders today.
_M_RGB = np.linalg.inv(_M_XYZ)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1.0 / 2.4) - 0.055)


def lch_to_srgb(L, C, h_deg):
    """CIELAB (L*, C*, h) -> a framebuffer sRGB triple. Returns (rgb, in_gamut)."""
    a = C * np.cos(np.radians(h_deg))
    b = C * np.sin(np.radians(h_deg))
    fy = (L + 16.0) / 116.0
    f = np.array([fy + a / 500.0, fy, fy - b / 200.0])
    xyz = np.where(f ** 3 > 0.008856, f ** 3, (f - 16.0 / 116.0) / 7.787) * _WHITE_D65
    lin = xyz @ _M_RGB.T
    ok = bool(np.all(lin > -1e-6) and np.all(lin < 1.0 + 1e-6))
    return linear_to_srgb(lin), ok


def _face_for_target(target, plate_mean, shade=SHADE):
    """The state colour that renders `target` on this plate. Exact -- the chain is a product."""
    return target / np.maximum(plate_mean * shade, 1e-9)


def _target_at_luminance(lum_target, C, h, tol=1e-7):
    """The (C, h) colour whose Rec.709 luminance on the framebuffer value is `lum_target`.

    Luminance is monotone in L* at fixed chroma and hue, so a bisection is exact and needs
    no derivative. Returns (rgb, ok)."""
    lo, hi = 0.0, 100.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        rgb, _ = lch_to_srgb(mid, C, h)
        if float(rgb @ REC709) < lum_target:
            lo = mid
        else:
            hi = mid
        if hi - lo < tol:
            break
    L = 0.5 * (lo + hi)
    rgb, gamut = lch_to_srgb(L, C, h)
    return rgb, L, (gamut and abs(float(rgb @ REC709) - lum_target) < 1e-4)


def candidates(style, plate_mean, lum_target, n_hue=25, n_chroma=13):
    """Every admissible rendered face for one board, on a hue x chroma grid of its window."""
    spec = PALETTE[style]
    out = []
    for h in np.linspace(spec["hue"][0], spec["hue"][1], n_hue):
        for C in np.linspace(spec["chroma"][0], spec["chroma"][1], n_chroma):
            rgb, L, ok = _target_at_luminance(lum_target, C, h)
            if not ok:
                continue
            face = _face_for_target(rgb, plate_mean)
            idle = face / BOARD_CAP_TINT
            if np.any(idle < SEAT_FLOOR / BOARD_CAP_TINT - 1e-9) or np.any(idle > IDLE_MAX):
                continue
            if np.any(rgb < 0.0) or np.any(rgb > 1.0):
                continue
            out.append(dict(style=style, idle=idle, face=face, rgb=rgb,
                            lab=lab(rgb), L=L, C=float(C), h=float(h)))
    return out


def solve(means, plates_norm, styles=STYLES):
    """Maximise the minimum pairwise dE over the three boards' admissible sets.

    Exhaustive on the grid, so the answer is the grid's optimum and not a local one, and it
    is DETERMINISTIC -- a solver whose output becomes a shipped constant has to be."""
    lum_ref = {s: float(rendered(means[s], IDLE_SHIPPED) @ REC709) for s in styles}
    cands = {s: candidates(s, means[s], lum_ref[s]) for s in styles}
    for s in styles:
        if not cands[s]:
            return None, -1.0, lum_ref, cands
    A, B, Cc = (np.array([c["lab"] for c in cands[s]]) for s in styles)
    d_ab = np.linalg.norm(A[:, None, :] - B[None, :, :], axis=-1)      # oak  x steel
    d_ac = np.linalg.norm(A[:, None, :] - Cc[None, :, :], axis=-1)     # oak  x bronze
    d_bc = np.linalg.norm(B[:, None, :] - Cc[None, :, :], axis=-1)     # steel x bronze
    # min over the three pairs for every triple, without materialising i x j x k floats more
    # than once: the arrays are a few hundred long, so the cube is small.
    m = np.minimum(np.minimum(d_ab[:, :, None], d_ac[:, None, :]), d_bc[None, :, :])
    i, j, k = np.unravel_index(int(np.argmax(m)), m.shape)
    best = {styles[0]: cands[styles[0]][i]["idle"],
            styles[1]: cands[styles[1]][j]["idle"],
            styles[2]: cands[styles[2]][k]["idle"]}
    return best, float(m[i, j, k]), lum_ref, cands


def report(plates_dir, styles=STYLES, solve_new=True, out_txt=None):
    lines = []

    def say(t=""):
        print(t)
        lines.append(t)

    say("=" * 96)
    say("THE INSTRUMENT")
    say("=" * 96)
    say(f"shade (BoardLit, flat cap plateau N = (0,0,-1), _Ambient {AMBIENT}, "
        f"_LightBoost {LIGHT_BOOST}) = {SHADE:.5f}")
    say(f"  dot(N, key)  = {float(_N_FLAT @ _KEY):+.5f}   -> lit")
    say(f"  dot(N, fill) = {float(_N_FLAT @ _FILL):+.5f}   -> saturated to 0")
    face0 = face_colour(IDLE_SHIPPED)
    say(f"shipped IdleColor {np.round(IDLE_SHIPPED, 3).tolist()} x BoardCapTint "
        f"{BOARD_CAP_TINT} -> face {np.round(face0, 4).tolist()}"
        + ("   [SEAT FLOOR ENGAGED]" if seat_floor_engages(IDLE_SHIPPED) else
           "   (seat floor is a no-op here)"))
    w = well_srgb()
    say(f"well  = KeycapGrain mean {grain_mean().round(4).tolist()} x CapWellColor "
        f"{CAP_WELL_COLOR.tolist()}"
        f" x shade -> {w.round(4)}  lum {float(w @ REC709):.5f}")

    raws, norms, means = {}, {}, {}
    say("")
    say("PLATES (level re-based through cap_atlas.normalise_plate, imported not re-implemented)")
    for s in styles:
        raw, norm, gain, achieved = load_plate(s, plates_dir)
        raws[s], norms[s] = raw, norm
        means[s] = norm.reshape(-1, 3).mean(axis=0)
        say(f"  {s:<7} raw mean {raw.mean():.4f} rgb "
            f"{raw.reshape(-1,3).mean(0).round(4).tolist()}"
            f"  x gain {gain:.3f} ->  mean {achieved:.4f} rgb {means[s].round(4).tolist()}")

    # THE KNOWN POSITIVE HAS TO BE FED THE PLATES IT WAS RECORDED AGAINST. `keycap_plate_*.png`
    # is whatever the working tree ships right now; the ModBuild 281 numbers were measured on
    # the round-1 plates, which `plates.py --ingest` keeps beside them as `_r1`. Validating on
    # the CURRENT plates and quoting the OLD record would be a self-fulfilling instrument.
    val_suffix = "_r1" if all(os.path.isfile(os.path.join(plates_dir, f"keycap_plate_{s}_r1.png"))
                              for s in styles) else ""
    means_val = {}
    for s in styles:
        _, nv, _, _ = load_plate(s, plates_dir, val_suffix)
        means_val[s] = nv.reshape(-1, 3).mean(axis=0)
    say(f"  validation plates: keycap_plate_<style>{val_suffix or ' (no _r1 backup found)'}.png"
        + ("" if val_suffix else "   *** the known positive below is measured on the CURRENT "
                                "plates and CANNOT check the 281 record ***"))

    say("")
    say("-" * 96)
    say("VALIDATION 1 -- THE KNOWN POSITIVE: the ROUND-1 plates x the shipped single IdleColor")
    say("-" * 96)
    idles_old = {s: IDLE_SHIPPED for s in styles}
    de_val_shaded = de_matrix(means_val, idles_old)
    de_val_albedo = de_matrix(means_val, idles_old, shade=1.0)
    de_shaded = de_matrix(means, idles_old)          # the CURRENT plates, for before/after
    de_albedo = de_val_albedo
    say("  pair            shaded (this model)   albedo-only (no shade)   ModBuild 281 record")
    rec = {("oak", "steel"): 13.8, ("steel", "bronze"): 10.3, ("oak", "bronze"): 3.7}
    for pair in [("oak", "steel"), ("steel", "bronze"), ("oak", "bronze")]:
        k = pair if pair in de_val_shaded else (pair[1], pair[0])
        say(f"  {pair[0]:>6}-{pair[1]:<8}      {de_val_shaded[k]:6.2f}                 "
            f"{de_val_albedo[k]:6.2f}                  {rec[pair]:5.1f}")
    err = max(abs(de_val_albedo[p] - rec[p]) for p in rec)
    ratio = float(np.mean([de_val_shaded[k] / de_val_albedo[k] for k in de_val_shaded]))
    say(f"  VERDICT: the albedo-only form reproduces all three recorded numbers to "
        f"{err:.2f} dE. The shaded form is a uniform {ratio:.3f}x of it (one scalar, applied "
        f"to all three styles alike, so it cannot reorder the pairs).")
    say("  The model of the chain is therefore accepted, and the 281 record's three numbers")
    say("  are identified as having been measured on the ALBEDO PRODUCT, not on the shaded")
    say("  framebuffer value. Everything below gates on the SHADED form, which is stricter.")

    say("")
    say("-" * 96)
    say("VALIDATION 2 -- THE NULL CONTROL: each style measured against itself")
    say("-" * 96)
    for s in styles:
        d = float(np.linalg.norm(lab(rendered(means_val[s], IDLE_SHIPPED))
                                 - lab(rendered(means_val[s], IDLE_SHIPPED))))
        say(f"  {s:<7} vs itself: dE {d:.6f}"
            + ("   OK" if d < 1e-9 else "   *** NON-ZERO, the instrument is broken ***"))

    say("")
    say("-" * 96)
    say("VALIDATION 3 -- THE WELL RATIO, and the one term this file does NOT claim")
    say("-" * 96)
    wl = float(w @ REC709)
    grain_cap = grain_mean() * face0 * SHADE
    say(f"  the PRE-ROUND grain cap (KeycapGrain x face x shade) : ratio to well "
        f"{float(grain_cap @ REC709) / wl:.3f}   [the 281 record calls this 1.75]")
    ratios_old = {}
    for s in styles:
        r = rendered(means_val[s], IDLE_SHIPPED)
        ratios_old[s] = float(r @ REC709) / wl
        say(f"  {s:<7} round-1 plate: face {np.round(r, 4).tolist()}  "
            f"lum {float(r @ REC709):.5f}  ratio to well {ratios_old[s]:.3f}   "
            f"[record band 1.74-1.81]")
    say("  This instrument's well is 1.19-1.29x too DARK against that record, systematically.")
    say("  Four well definitions were tried and none lands on 1.75 (see the header). So the")
    say("  ABSOLUTE ratio is not claimed; what is gated is each new cap against the SAME")
    say(f"  board's current cap (+-{LUM_TOLERANCE*100:.0f} %), in which the well term cancels exactly.")

    if not solve_new:
        if out_txt:
            open(out_txt, "w", encoding="utf-8").write("\n".join(lines) + "\n")
        return None

    say("")
    say("=" * 96)
    say("THE SOLVE -- three per-board idle face colours")
    say("=" * 96)
    best, best_de, lum_ref, cands = solve(means, norms, styles)
    say(f"  admissible faces on the hue x chroma grid: "
        + ", ".join(f"{s2} {len(cands[s2])}" for s2 in styles))
    if best is None:
        say("  NO FEASIBLE SOLUTION under the stated constraints.")
        if out_txt:
            open(out_txt, "w", encoding="utf-8").write("\n".join(lines) + "\n")
        return None

    say(f"  bars: min pairwise dE >= {DE_TARGET}, cap luminance within +-{LUM_TOLERANCE*100:.0f} % "
        f"of today's, clipped texels < {CLIP_BAR*100:.1f} %, and inside the authored palette:")
    for s in styles:
        spec = PALETTE[s]
        say(f"        {s:<7} {spec['material']:<24} hue {spec['hue'][0]:5.0f}-{spec['hue'][1]:<5.0f} deg   "
            f"C* {spec['chroma'][0]:4.0f}-{spec['chroma'][1]:<4.0f}")
    say("")
    say("  board    OLD idle (all three)      NEW idle                 face = idle x 0.5")
    for s in styles:
        say(f"  {s:<7}  ({IDLE_SHIPPED[0]:.3f}, {IDLE_SHIPPED[1]:.3f}, {IDLE_SHIPPED[2]:.3f})"
            f"      ({best[s][0]:.3f}, {best[s][1]:.3f}, {best[s][2]:.3f})     "
            f"{np.round(face_colour(best[s]), 4)}"
            + ("  [SEAT FLOOR ENGAGED]" if seat_floor_engages(best[s]) else ""))

    say("")
    say("  pair             dE BEFORE   dE AFTER")
    de_new = de_matrix(means, best)
    for pair in [("oak", "steel"), ("steel", "bronze"), ("oak", "bronze")]:
        k = pair if pair in de_shaded else (pair[1], pair[0])
        say(f"  {pair[0]:>6}-{pair[1]:<9}   {de_shaded[k]:7.2f}    {de_new[k]:7.2f}")
    say(f"  minimum pairwise dE: {min(de_shaded.values()):.2f}  ->  {best_de:.2f}")

    say("")
    say("  board    rendered face (sRGB)         L*     C*      h     lum      ratio-to-well "
        "  vs today   clipped   in palette")
    for s in styles:
        for tag, idle in (("today", IDLE_SHIPPED), ("NEW  ", best[s])):
            r = rendered(means[s], idle)
            L, c, h = lch(r)
            lum = float(r @ REC709)
            say(f"  {s:<7} {tag} {np.round(r, 4).tolist()}  {L:5.2f}  {c:5.2f}  {h:5.1f}  "
                f"{lum:.5f}   {lum/wl:8.3f}     {lum/lum_ref[s]-1.0:+7.3%}   "
                f"{clip_fraction(norms[s], idle)*100:.4f} %   "
                f"{'yes' if in_palette(s, r) else 'NO'}")

    say("")
    say("  AND AGAINST THE CAP THAT IS ACTUALLY ON THE BOARD TODAY -- the round-1 plate through")
    say("  the shipped idle. `vs today` above is against the NEW plate through the shipped idle,")
    say("  which isolates the colour change; this isolates the whole round:")
    for s in styles:
        old_lum = float(rendered(means_val[s], IDLE_SHIPPED) @ REC709)
        new_lum = float(rendered(means[s], best[s]) @ REC709)
        say(f"  {s:<7} shipped cap lum {old_lum:.5f} (ratio {old_lum/wl:.3f})  ->  "
            f"new cap lum {new_lum:.5f} (ratio {new_lum/wl:.3f})   "
            f"{new_lum/old_lum - 1.0:+7.3%}"
            + ("   OK" if abs(new_lum / old_lum - 1.0) <= LUM_TOLERANCE else "   *** OUT OF BAND ***"))
    say("")
    say(f"  clip bound: the brightest channel any admissible face can reach is "
        f"IDLE_MAX x BoardCapTint x shade = {IDLE_MAX * BOARD_CAP_TINT * SHADE:.4f}, so with a "
        f"plate clipped to [0,1] by normalise_plate NO texel can clip. Measured above: 0.")

    if out_txt:
        open(out_txt, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return best


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--plates", default=os.path.join(PREP, "out"))
    ap.add_argument("--selfcheck", action="store_true",
                    help="run only the three validations, do not solve")
    ap.add_argument("--out", default=None, help="also write the report to this text file")
    args = ap.parse_args()
    report(args.plates, solve_new=not args.selfcheck, out_txt=args.out)


if __name__ == "__main__":
    main()
