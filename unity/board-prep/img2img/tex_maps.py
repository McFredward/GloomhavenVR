#!/usr/bin/env python3
"""Derive a NORMAL map and an MRS map for a GloomhavenVR control board from a new albedo atlas.

    python3 maps.py --albedo <new_albedo.png> --old-normal <shipped_normal.png> \
                    --style <oak|steel|bronze> --out <dir>

WHAT THIS WRITES, AND WHAT READS IT
-----------------------------------
`Assets/Bundle/Table/BoardLit.shader` binds three textures per board material:

    _MainTex  <base>_albedo.png   sRGB colour
    _BumpMap  <base>_normal.png   tangent-space normal, sampled with UnpackNormal
    _MRSMap   <base>_mrs.png      R = METALLIC, G = ROUGHNESS, B unused, LINEAR data

_MRSMap IS NOT glTF ORM.  The board pipeline's own intermediate `<base>_mr.png` IS
glTF ORM (R = occlusion, G = roughness, B = metallic) and the two files differ only in
name.  Feeding the ORM pack to _MRSMap makes the shader read occlusion (~0.97) as
metallic, i.e. every board becomes a mirror-finish conductor, and it does so silently:
the texture samples, a highlight appears, and only the look is wrong.  `verify_packing()`
below exists specifically to make that mistake fail loudly, and it is run on every
invocation against a deliberately mis-packed ORM control.

THE TRAP THIS SCRIPT IS BUILT AROUND
------------------------------------
The user wants the relief to match the new art.  But the new albedo comes out of an
image model, and its FEATURE structure -- recess borders, engraved glyph strokes,
moulding edges, seat rings -- sits several millimetres off where the MESH actually has
those features.  The SHIPPED normal map's feature relief is registered to the mesh,
because the board pipeline authored the height field in board metres against the mesh
surface (`tex_atlas.py`, "placement is done in BOARD METRES").

So the two bands are taken from two different sources:

    FEATURE band (low/mid frequency) <- the SHIPPED normal map.  Mesh-registered.
    MATERIAL band (high frequency)   <- the NEW albedo's high-pass luminance, treated
                                        as a height field.  Grain, brush lines, pitting,
                                        casting porosity, pores.  Stochastic, so it has
                                        no "correct" registration to be off by.

The split is a single Gaussian cutoff, `--sigma`, applied to BOTH sides, so the two
bands are complementary and nothing is counted twice.  Composition is done in SLOPE
space (dh/du, dh/dv), not by averaging unit vectors: two height fields added together
have their slopes added, which is exactly the operation that "keep the mouldings and
carve grain into them" means.

CONVENTION NOTE -- READ THIS BEFORE CHANGING THE SIGN OF RED
------------------------------------------------------------
`board-prep/tex_common.py:normal_from_height` writes  nx = +dh/du  and  ny = +dh/dv.
The standard OpenGL/Unity tangent-space convention (which is what UnpackNormal expects)
is  nx = -dh/du,  ny = +dh/dv.  The pipeline's GREEN is standard; its RED is inverted.
Verified two ways: synthetically (a ramp rising to the right yields nx = +0.298 from
tex_common, where the convention requires a negative) and against the shipped atlases
(the pipeline darkens albedo with a cavity term from the same height field, so
d(luminance)/du tracks dh/du; on both metal boards corr(nx, dLum/du) = +0.33 / +0.37,
i.e. red follows +dh/du).

This script therefore DEFAULTS to `--x-convention pipeline`, matching the maps that
ship.  That is deliberate.  Consistency is worth more than correctness here: a shared
red flip across the whole pack is a global, subtle property (and may even be cancelled
by the FBX tangent handedness), whereas mixing conventions would light the grain from
the opposite side to the mouldings on the SAME board, which is a visible defect.  If
the flip is ever fixed at the source, run this with `--x-convention opengl`.

STEREO SAFETY
-------------
This project has a standing defect class: a narrow specular lobe riding on a
high-frequency normal map produces per-eye sparkle (stereo rivalry).  Two guards:

  * the material band's perturbation angle is CALIBRATED, not tuned -- `--target-p99`
    sets the 99th-percentile tilt of the added component and the strength is solved
    for it analytically, per style;
  * the roughness floor is 0.42, matching the shipped pack.  BoardLit's own floor is
    max(0.08, mr.g), a 2.7 deg half-angle -- a mirror.  0.42 gives a 7.87 deg lobe
    half-angle (half_angle = acos(0.5 ** (1/exp2((1-r)*9+1)))), which clears steel's
    measured p95 single-texel normal step.  Steel is the case that matters: it is
    ~99% metallic, so its f0 is the ALBEDO rather than a dielectric 0.04.
"""

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

Image.MAX_IMAGE_PIXELS = None


# ---------------------------------------------------------------------------
# Style policy.  Every number here is a material claim, so each carries its reason.
# ---------------------------------------------------------------------------

ROUGH_FLOOR = 0.42     # matches the shipped pack; see the stereo note in the docstring
ROUGH_CEIL = 0.95      # 1.0 flattens the lobe to nothing and buys no look

STYLES = {
    # target_p99_deg -- 99th percentile tilt of the ADDED material band, in degrees.
    # Oak is a dielectric (f0 = 0.04), so its grain costs almost no specular risk and
    # it can carry real relief.  The two metals put their micro-structure in ROUGHNESS
    # instead: tex_atlas.py already scales their height to 0.30 / 0.38 of oak's after
    # both lit renders came back as sandpaper (steel salt-and-pepper, bronze hammered
    # gold leaf).  These targets keep that ordering.
    # metallic_ceiling -- the value this style's metallic saturates AT, declared here
    # rather than inferred, because verify_packing() tests the written R against it.
    "oak":    dict(target_p99_deg=12.0, invert_height=False, metallic_ceiling=0.00),
    "steel":  dict(target_p99_deg=6.0,  invert_height=False, metallic_ceiling=0.95),
    "bronze": dict(target_p99_deg=7.0,  invert_height=False, metallic_ceiling=0.95),
}

MAX_TOTAL_TILT_DEG = 70.0   # sanity clamp on the combined normal


# ---------------------------------------------------------------------------
# Small numeric helpers (pure numpy -- scipy is not installed on this machine)
# ---------------------------------------------------------------------------

def srgb_to_linear(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def gauss_lowpass(a, sigma):
    """Exact circular Gaussian low-pass via FFT.

    FFT rather than a stack of box blurs because the cutoff is a claim this script
    has to state in millimetres, and a box-blur approximation would make that claim
    approximate too.  Circular wrap matches tex_common's np.roll convention.
    """
    a = np.asarray(a, np.float64)
    if sigma <= 0:
        return a.copy()
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    # Transfer function of a Gaussian of std `sigma` texels.
    g = np.exp(-2.0 * (np.pi ** 2) * (sigma ** 2) * (fy * fy + fx * fx))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(h, w))


def grad_uv(a):
    """Central differences.  du = +right, dv = +down (image order, as tex_common)."""
    gu = (np.roll(a, -1, axis=1) - np.roll(a, 1, axis=1)) * 0.5
    gv = (np.roll(a, -1, axis=0) - np.roll(a, 1, axis=0)) * 0.5
    return gu, gv


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, np.float64) - e0) / max(e1 - e0, 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def pearson(a, b, mask=None):
    a = np.asarray(a, np.float64).ravel()
    b = np.asarray(b, np.float64).ravel()
    if mask is not None:
        m = np.asarray(mask).ravel()
        a, b = a[m], b[m]
    a = a - a.mean()
    b = b - b.mean()
    d = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / d) if d > 0 else 0.0


def lobe_half_angle_deg(rough):
    """BoardLit's specular half-angle for a roughness value.  power = exp2((1-r)*9+1)."""
    power = 2.0 ** ((1.0 - float(rough)) * 9.0 + 1.0)
    return float(np.degrees(np.arccos(0.5 ** (1.0 / power))))


def srgb_to_lab(rgb01):
    """sRGB [0,1] -> CIELAB (D65).  Vectorised, (H,W,3) in, (H,W,3) out."""
    lin = srgb_to_linear(rgb01)
    m = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ m.T
    wp = np.array([0.95047, 1.00000, 1.08883])
    t = xyz / wp
    d = 6.0 / 29.0
    f = np.where(t > d ** 3, np.cbrt(np.clip(t, 1e-12, None)), t / (3 * d * d) + 4.0 / 29.0)
    L = 116.0 * f[..., 1] - 16.0
    a = 500.0 * (f[..., 0] - f[..., 1])
    b = 200.0 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], axis=-1)


def pct(a, q):
    return float(np.percentile(np.asarray(a, np.float64), q))


# ---------------------------------------------------------------------------
# NORMAL MAP
# ---------------------------------------------------------------------------

def decode_normal(png_rgb01, x_convention):
    """PNG [0,1] -> stored slope field (su, sv) = (nx/nz, ny/nz).

    "Stored" means: in whatever convention the file itself uses.  This script never
    converts between conventions internally; it only makes sure the material band it
    ADDS is written in the SAME convention as the feature band it keeps.
    """
    n = png_rgb01 * 2.0 - 1.0
    nz = np.maximum(n[..., 2], 1e-3)
    return n[..., 0] / nz, n[..., 1] / nz


def encode_normal(su, sv):
    """Stored slope field -> uint8 RGB normal map."""
    nz = 1.0 / np.sqrt(su * su + sv * sv + 1.0)
    nx, ny = su * nz, sv * nz
    out = np.stack([nx, ny, nz], -1) * 0.5 + 0.5
    return np.clip(np.rint(out * 255.0), 0, 255).astype(np.uint8)


def tilt_deg(su, sv):
    return np.degrees(np.arctan(np.sqrt(su * su + sv * sv)))


def build_normal(alb_rgb01, old_nrm_rgb01, style, sigma, target_p99_deg,
                 x_convention, gate_amount, gate_floor, strength_scale,
                 keep_shipped_hf, soft_clip_k):
    """Return (rgb_uint8, info dict).  See the module docstring for the band split."""
    inv = STYLES[style]["invert_height"]

    # ---- feature band: the SHIPPED normal, low-passed in slope space ----------
    su_s, sv_s = decode_normal(old_nrm_rgb01, x_convention)
    su_feat = gauss_lowpass(su_s, sigma)
    sv_feat = gauss_lowpass(sv_s, sigma)

    # ---- material band: the NEW albedo's high-pass luminance as a height field --
    lin = srgb_to_linear(alb_rgb01)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    # Darker = lower.  A pore, a groove between brush strokes and a casting pit are
    # all dark BECAUSE they are recessed, so this is the physical polarity, not a
    # look choice.  (Per-style override lives in STYLES[...]["invert_height"].)
    height = -lum if inv else lum
    hp = height - gauss_lowpass(height, sigma)

    # Soft-clip the high-pass BEFORE differentiating.  An engraved glyph stroke is
    # only 3-4 texels wide, so it survives the high-pass -- and in the new art it is
    # several millimetres off the mesh, which would emboss a SECOND, displaced glyph
    # next to the registered one.  tanh at k standard deviations leaves grain linear
    # and compresses exactly the isolated high-contrast edges that cause that.
    sd = float(hp.std())
    if sd > 0:
        hp = np.tanh(hp / (soft_clip_k * sd)) * (soft_clip_k * sd)

    gu, gv = grad_uv(hp)
    if x_convention == "opengl":
        gu = -gu            # standard: nx = -dh/du.  Green is +dh/dv in both.
    # 'pipeline' leaves gu as +dh/du, matching tex_common.normal_from_height.

    # ---- ORNAMENT WEIGHT: what in the shipped map is carving, and what is grain ---
    # A plain low-pass of the shipped normal is the wrong knife and the preview proves
    # it: a rosette spoke is 3-4 texels wide and the dentils on the frame are about the
    # same, so the FEATURE band throws away exactly the detail that makes carving read as
    # carving. The first build of this script did that and the 100% crop came back visibly
    # softer than the map it was meant to improve.
    #
    # But the shipped map's high frequency is not all carving. It was authored from a
    # height field that is carved ornament PLUS a procedural material micro-relief
    # (tex_atlas.py: BASE_HEIGHT 0.26, STYLE_HEIGHT per style) -- and that micro-relief is
    # the OLD art's grain, which is the one thing that must not survive next to the new
    # art's grain.
    #
    # The two are separable by their spatial statistics, not by their frequency: ornament
    # is SPARSE and locally concentrated, grain is DENSE and near-uniform. So the mask is
    # local high-frequency energy measured against the board's own median energy -- a
    # rosette spikes several times above the field, a grain field sits at it by definition.
    su_hf, sv_hf = su_s - su_feat, sv_s - sv_feat
    energy = np.sqrt(np.maximum(gauss_lowpass(su_hf * su_hf + sv_hf * sv_hf, 4.0), 0.0))
    e_med = float(np.median(energy)) + 1e-9
    ornament = smoothstep(2.0 * e_med, 6.0 * e_med, energy)
    ornament = gauss_lowpass(ornament, 2.0)      # soften, or the mask edge is itself an edge

    # The same mask, used the other way round: where the shipped map is already carrying
    # carved relief, the new albedo's version of that carving is suppressed. Otherwise an
    # engraved glyph stroke -- 3-4 texels wide, so it survives the high-pass, and several
    # millimetres off the mesh in the new art -- gets embossed a SECOND time beside the
    # registered one. Driven only by the shipped map, so it cannot smuggle the new albedo's
    # registration back in. On flat field the gate is 1 and the grain is at full strength.
    gate = np.maximum(1.0 - gate_amount * ornament, gate_floor)

    feat_tilt = tilt_deg(su_feat, sv_feat)

    gu = gu * gate
    gv = gv * gate

    # ---- calibrate strength to the target p99 perturbation angle -----------------
    # Slopes scale linearly with strength (the soft clip is upstream of the gradient),
    # so this is solved, not searched: strength = tan(target) / p99(|unit slope|).
    mag = np.sqrt(gu * gu + gv * gv)
    p99_unit = pct(mag, 99.0)
    strength = (np.tan(np.radians(target_p99_deg)) / p99_unit) if p99_unit > 0 else 0.0
    strength *= strength_scale
    su_mat, sv_mat = gu * strength, gv * strength

    mat_tilt = tilt_deg(su_mat, sv_mat)

    # ---- combine, in SLOPE space -------------------------------------------------
    # Two height fields added have their slopes added; that is what "carve grain into the
    # mouldings" means as an operation. The ornament crossfade keeps the shipped map's own
    # high frequency ONLY where it is carving.
    keep_hf = 1.0 if keep_shipped_hf else ornament
    su = su_feat + keep_hf * su_hf + su_mat
    sv = sv_feat + keep_hf * sv_hf + sv_mat

    tmax = np.tan(np.radians(MAX_TOTAL_TILT_DEG))
    m = np.sqrt(su * su + sv * sv)
    over = m > tmax
    sc = np.where(over, tmax / np.maximum(m, 1e-12), 1.0)
    su, sv = su * sc, sv * sc

    rgb = encode_normal(su, sv)

    # per-texel angular step: the quantity a narrow specular lobe turns into sparkle
    def step_deg(su_, sv_):
        nz = 1.0 / np.sqrt(su_ * su_ + sv_ * sv_ + 1.0)
        n = np.stack([su_ * nz, sv_ * nz, nz], -1)
        out = {}
        for ax, name in ((1, "u"), (0, "v")):
            d = np.sum(n * np.roll(n, -1, axis=ax), axis=-1)
            out[name] = np.degrees(np.arccos(np.clip(d, -1.0, 1.0)))
        return out

    st_mat = step_deg(su_mat, sv_mat)
    st_tot = step_deg(su, sv)
    st_ship = step_deg(su_s, sv_s)
    ship_tilt = tilt_deg(su_s, sv_s)

    info = dict(
        sigma=sigma, strength=float(strength), soft_clip_k=soft_clip_k,
        x_convention=x_convention, keep_shipped_hf=bool(keep_shipped_hf),
        gate_mean=float(gate.mean()), gate_min=float(gate.min()),
        ornament_mean=float(ornament.mean()),
        ornament_coverage=float((ornament > 0.5).mean()),
        material_tilt_median_deg=pct(mat_tilt, 50), material_tilt_p99_deg=pct(mat_tilt, 99),
        material_tilt_mean_deg=float(mat_tilt.mean()),
        feature_tilt_median_deg=pct(feat_tilt, 50), feature_tilt_p99_deg=pct(feat_tilt, 99),
        total_tilt_median_deg=pct(tilt_deg(su, sv), 50), total_tilt_p99_deg=pct(tilt_deg(su, sv), 99),
        material_step_p95_u=pct(st_mat["u"], 95), material_step_p95_v=pct(st_mat["v"], 95),
        total_step_p95_u=pct(st_tot["u"], 95), total_step_p95_v=pct(st_tot["v"], 95),
        shipped_step_p95_u=pct(st_ship["u"], 95), shipped_step_p95_v=pct(st_ship["v"], 95),
        shipped_tilt_median_deg=pct(ship_tilt, 50), shipped_tilt_p95_deg=pct(ship_tilt, 95),
        shipped_tilt_p99_deg=pct(ship_tilt, 99),
        clamped_texels=int(over.sum()),
        _slopes=(su, sv), _feat=(su_feat, sv_feat), _mat=(su_mat, sv_mat), _hp=hp,
    )
    return rgb, info


# ---------------------------------------------------------------------------
# MRS MAP   (R = metallic, G = roughness, B = 0)
# ---------------------------------------------------------------------------

def build_mrs(alb_rgb01, style, rough_floor):
    """Derive metallic and roughness from measurable properties of the new albedo.

    Everything is driven by CIELAB, because the material questions are literally
    colorimetric ones: "is this dark and desaturated" (oxide), "is this green or is it
    gold" (verdigris vs polished bronze), "is this bleached or oiled" (wood).
    """
    lab = srgb_to_lab(alb_rgb01)
    L, a, b = lab[..., 0], lab[..., 1], lab[..., 2]
    C = np.sqrt(a * a + b * b)                      # chroma
    hue = np.degrees(np.arctan2(b, a)) % 360.0      # hue angle

    lin = srgb_to_linear(alb_rgb01)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    hp = lum - gauss_lowpass(lum, 3.0)
    # local RMS of the high-pass = "how broken up is the surface here": open wood
    # pore, casting porosity, a pitted patch.  Rough surfaces are rough at this scale.
    micro = np.sqrt(np.maximum(gauss_lowpass(hp * hp, 6.0), 0.0))
    mref = max(pct(micro, 99.0), 1e-6)
    micro_n = np.clip(micro / mref, 0.0, 1.0)

    extra = {}

    if style == "oak":
        # Wood is a DIELECTRIC.  Metallic is 0 everywhere, with no exceptions and no
        # gradient -- a wooden board with any metallic at all reads as painted metal.
        metallic = np.zeros_like(L)
        # Oiled / handled / dark timber is closed-pored and reflective; bleached and
        # open-grain timber scatters.  L* is the handle for the first, the micro-RMS
        # for the second.
        # Calibrated against the stand-in oak atlas, whose L* runs 26..64 (p1..p99):
        # the ramp has to span the timber's ACTUAL range or it saturates at one end and
        # the map is a constant.
        bright = smoothstep(30.0, 64.0, L)
        rough = 0.44 + 0.16 * bright + 0.12 * micro_n
        extra["oiled_fraction"] = float((bright < 0.35).mean())

    elif style == "steel":
        # Mostly bare metal.  Oxide, soot and tarnish are NOT conductors, so metallic
        # has to come down wherever the albedo says the bare surface has been eaten:
        # dark AND low-chroma (rust would be chromatic; this palette's tarnish is a
        # desaturated grey-black film).
        # The dark ramp is calibrated so that BARE steel is not called oxide. Measured on
        # the stand-in steel atlas, bare brushed plate sits at L* 47..67 (p25..p95) with
        # a floor at p1 = 33; soot and heavy tarnish are the things that go below ~30.
        # An earlier 28..62 ramp scored L* 52 -- ordinary plate -- as 21% oxide and pulled
        # the whole board down to 0.76 metallic against the shipped pack's 0.994.
        dark = 1.0 - smoothstep(22.0, 45.0, L)
        # The chroma term is nearly inert on this palette (steel's C is under 4 everywhere,
        # so desat ~ 1) and that is correct: on a monochrome board darkness IS the oxide
        # signal. It earns its place on art that has RUST, which is strongly chromatic and
        # must not be scored as soot.
        desat = 1.0 - smoothstep(4.0, 14.0, C)
        oxide = np.clip(dark * desat, 0.0, 1.0)
        metallic = 0.95 - 0.72 * oxide
        # Rubbed-bright zones are polished; tarnish and pitting scatter.
        rough = 0.44 + 0.34 * oxide + 0.16 * micro_n - 0.06 * smoothstep(55.0, 75.0, L)
        extra["oxide_fraction"] = float((oxide > 0.5).mean())
        extra["oxide_mean"] = float(oxide.mean())

    elif style == "bronze":
        # THE single most important material fact for bronze: verdigris is a MINERAL
        # CRUST -- basic copper carbonate/acetate -- and it is a DIELECTRIC.  Painting
        # it metallic is the difference between a patinated bronze and a green mirror.
        # The test is a hue test: polished bronze sits warm (hue ~55-95 deg in Lab,
        # b* strongly positive); verdigris sits pale blue-green (hue ~140-210 deg).
        # Chroma gates it so that near-neutral grime is not classified as either.
        # Ramp calibrated on the stand-in bronze atlas, which carries a real, separable
        # verdigris population: 80.3% of texels sit at hue 60-80 (polished bronze, C ~ 34)
        # and 6.5% at hue 120-140 (C ~ 15, L* ~ 45, e.g. sRGB 97,113,89 -- muted green
        # crust). Nothing at all lies between 100 and 120 in bulk, so the two materials are
        # separated by an actual gap rather than by a threshold picked to taste. A first
        # 120..155 ramp only scored that crust 0.35-0.46 verdigris and left it half-metal.
        green = smoothstep(105.0, 132.0, hue) * (1.0 - smoothstep(215.0, 250.0, hue))
        chromatic = smoothstep(3.0, 10.0, C)
        verdigris = np.clip(green * chromatic, 0.0, 1.0)
        gold = smoothstep(40.0, 55.0, hue) * (1.0 - smoothstep(95.0, 112.0, hue)) * chromatic
        metallic = 0.95 * (1.0 - verdigris) + 0.03 * verdigris
        # Polished bronze is near-specular; the crust is matte and granular.
        rough = 0.44 + 0.44 * verdigris + 0.12 * micro_n - 0.02 * gold
        extra["verdigris_fraction"] = float((verdigris > 0.5).mean())
        extra["verdigris_mean"] = float(verdigris.mean())
        extra["gold_fraction"] = float((gold > 0.5).mean())
    else:
        raise SystemExit("unknown style " + style)

    metallic = np.clip(metallic, 0.0, 1.0)
    rough = np.clip(rough, rough_floor, ROUGH_CEIL)

    rgb = np.zeros(alb_rgb01.shape[:2] + (3,), np.uint8)
    rgb[..., 0] = np.clip(np.rint(metallic * 255.0), 0, 255).astype(np.uint8)
    rgb[..., 1] = np.clip(np.rint(rough * 255.0), 0, 255).astype(np.uint8)
    rgb[..., 2] = 0                                  # B is UNUSED by BoardLit
    return rgb, metallic, rough, extra


def build_orm_control(alb_rgb01, metallic, rough):
    """The MISTAKE, built on purpose: the glTF ORM pack (R=occlusion, G=roughness,
    B=metallic) that `<base>_mr.png` uses.  verify_packing() must reject this."""
    lin = srgb_to_linear(alb_rgb01)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    ao = np.clip(0.90 + 0.10 * (lum - lum.mean()) / max(lum.std(), 1e-6) * 0.2, 0.0, 1.0)
    rgb = np.zeros(alb_rgb01.shape[:2] + (3,), np.uint8)
    rgb[..., 0] = np.rint(ao * 255).astype(np.uint8)
    rgb[..., 1] = np.rint(rough * 255).astype(np.uint8)
    rgb[..., 2] = np.rint(metallic * 255).astype(np.uint8)
    return rgb


# ---------------------------------------------------------------------------
# FALSIFIERS
# ---------------------------------------------------------------------------

def verify_packing(mrs_u8, style):
    """FALSIFIER 1 -- MRS vs glTF ORM ordering.

    Three predicates that the MRS pack satisfies and the ORM pack cannot:

      P1  B is identically zero.  BoardLit never reads B; ORM puts METALLIC there, so
          any non-zero B means the pack is ORM (or some third thing).

      P2  For oak, R is identically zero.  Wood is a dielectric, so MRS-R must be 0,
          whereas ORM-R is occlusion and sits near 0.9.  This is the predicate that
          catches ORM on the one style where P1 cannot -- oak's metallic is 0, so an
          ORM pack of oak would ALSO have B = 0.

      P3  R is a policy mask, not a continuous field.  Metallic is a material CLASS:
          it saturates at the style's declared ceiling over most of the surface, so
          its modal 8-bit value IS that ceiling and carries a large share of the
          texels.  Occlusion is a continuous shading term with no policy ceiling and
          no mode worth speaking of.

    P3 replaced an earlier shape test ("R mean > 0.75 and p1 > 0.5 and std < 0.10 looks
    like AO") which FALSELY FAILED steel: a 93%-metallic board really is a near-uniform
    high field and is statistically indistinguishable from an occlusion map by summary
    statistics alone.  A packing test must key on something the packing DECIDES, not on
    a silhouette that both packings can wear.

    Returns (ok, [(name, ok, detail), ...]).
    """
    R8 = mrs_u8[..., 0]
    R = R8.astype(np.float64) / 255.0
    B = mrs_u8[..., 2].astype(np.float64) / 255.0
    ceil_u8 = int(round(STYLES[style]["metallic_ceiling"] * 255.0))
    hist = np.bincount(R8.ravel(), minlength=256)
    mode_val = int(hist.argmax())
    mode_frac = float(hist[mode_val]) / R8.size

    checks = [("P1 B channel identically zero", bool(B.max() == 0.0), f"B max={B.max():.4f}")]
    if style == "oak":
        checks.append(("P2 oak metallic identically zero", bool(R.max() == 0.0),
                       f"R max={R.max():.4f}"))
    else:
        checks.append(("P2 (oak-only) skipped", True, f"style={style}"))
    p3 = (mode_val == ceil_u8) and (mode_frac >= 0.10)
    checks.append(("P3 R saturates at the declared ceiling", bool(p3),
                   f"mode={mode_val}/255 ({mode_frac*100:.1f}% of texels), "
                   f"declared ceiling={ceil_u8}/255"))
    return all(c[1] for c in checks), checks


def feature_edges_from_slopes(su, sv, sigma):
    """Feature-band edge strength of a normal map: |slope| of its low-pass band."""
    return np.hypot(gauss_lowpass(su, sigma), gauss_lowpass(sv, sigma))


def feature_edges_from_albedo(alb_rgb01, sigma):
    """Feature-band edge strength of an albedo: |grad| of its low-passed luminance."""
    lin = srgb_to_linear(alb_rgb01)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    gu, gv = grad_uv(gauss_lowpass(lum, sigma))
    return np.hypot(gu, gv)


def verify_registration(alb, old_nrm, style, args, out_slopes):
    """FALSIFIER 2 -- did the FEATURE relief come from the shipped normal or the albedo?

    The failure being hunted is: someone "simplifies" this script into
    normal-from-albedo and the result looks plausible while every engraved line sits
    several millimetres off the mesh.

    The test is a registration check under a DELIBERATE MISREGISTRATION.  The albedo
    is shifted by `--falsify-shift` texels (the real defect: the model's features are
    several mm off, and ~1500 px/m makes 8 texels ~= 5.3 mm), the map is rebuilt from
    the shifted albedo, and its feature-band edges are correlated against
      (a) the SHIPPED normal's feature edges, and
      (b) the SHIFTED albedo's feature edges.
    A correct build tracks (a).  A naive albedo-derived normal -- built here as the
    NEGATIVE CONTROL, so the test is shown to be capable of failing -- tracks (b).
    """
    n = args.falsify_shift
    alb_shift = np.roll(np.roll(alb, n, axis=0), n, axis=1)

    _, info_s = build_normal(alb_shift, old_nrm, style, args.sigma,
                             STYLES[style]["target_p99_deg"], args.x_convention,
                             args.gate_amount, args.gate_floor, args.strength_scale,
                             args.keep_shipped_hf, args.soft_clip_k)
    su_o, sv_o = info_s["_slopes"]

    # NEGATIVE CONTROL: the whole normal derived from the shifted albedo, all bands.
    lin = srgb_to_linear(alb_shift)
    lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
    gu, gv = grad_uv(lum)
    k = np.tan(np.radians(20.0)) / max(pct(np.hypot(gu, gv), 99), 1e-9)
    su_n, sv_n = gu * k, gv * k

    su_ship, sv_ship = decode_normal(old_nrm, args.x_convention)
    E_ship = feature_edges_from_slopes(su_ship, sv_ship, args.sigma)
    E_alb = feature_edges_from_albedo(alb_shift, args.sigma)
    E_out = feature_edges_from_slopes(su_o, sv_o, args.sigma)
    E_naive = feature_edges_from_slopes(su_n, sv_n, args.sigma)

    # Only where there is a feature to be registered at all; flat gutter texels would
    # dilute both correlations toward each other and make the test look weaker.
    mask = (E_ship > pct(E_ship, 70)) | (E_alb > pct(E_alb, 70))

    r_out_ship = pearson(E_out, E_ship, mask)
    r_out_alb = pearson(E_out, E_alb, mask)
    r_nai_ship = pearson(E_naive, E_ship, mask)
    r_nai_alb = pearson(E_naive, E_alb, mask)

    ok = (r_out_ship > r_out_alb + 0.15) and (r_nai_alb > r_nai_ship)
    return dict(shift_texels=n, r_out_ship=r_out_ship, r_out_alb=r_out_alb,
                r_naive_ship=r_nai_ship, r_naive_alb=r_nai_alb,
                margin=r_out_ship - r_out_alb, ok=bool(ok),
                control_ok=bool(r_nai_alb > r_nai_ship))


def verify_normal_encoding(nrm_u8):
    """The written bytes must decode to a valid tangent-space normal AFTER 8-bit
    quantisation -- which is where a map that was unit-length in float stops being one."""
    n = nrm_u8.astype(np.float64) / 255.0 * 2.0 - 1.0
    ln = np.sqrt((n * n).sum(-1))
    unit = np.abs(ln - 1.0) <= 0.01
    zpos = n[..., 2] > 0.0
    return dict(unit_frac=float(unit.mean()), zpos_frac=float(zpos.mean()),
                both_frac=float((unit & zpos).mean()),
                len_min=float(ln.min()), len_max=float(ln.max()),
                z_min=float(n[..., 2].min()))


def selftest_mrs():
    """The stand-in albedos may contain no verdigris and no soot at all, in which case
    'the classifier reports 0%' is indistinguishable from 'the classifier is dead'.
    So it is fed synthetic patches of each material and asked to name them."""
    patches = [
        # (label, sRGB 0-255, style, expect_metallic_lo, expect_metallic_hi)
        ("bronze: polished gold", (196, 148, 62), "bronze", 0.80, 1.00),
        ("bronze: pale verdigris", (138, 190, 172), "bronze", 0.00, 0.20),
        ("bronze: deep verdigris", (86, 150, 128), "bronze", 0.00, 0.20),
        ("steel: rubbed bright", (198, 200, 203), "steel", 0.85, 1.00),
        ("steel: sooty tarnish", (52, 52, 54), "steel", 0.00, 0.40),
        ("oak: oiled dark", (86, 58, 32), "oak", 0.00, 0.00),
        ("oak: bleached", (196, 168, 126), "oak", 0.00, 0.00),
    ]
    rows = []
    for label, rgb, style, lo, hi in patches:
        img = np.tile(np.array(rgb, np.float64) / 255.0, (64, 64, 1))
        _, met, rgh, _ = build_mrs(img, style, ROUGH_FLOOR)
        m, r = float(met.mean()), float(rgh.mean())
        rows.append((label, m, r, lo <= m <= hi))
    return rows


# ---------------------------------------------------------------------------
# PREVIEWS
# ---------------------------------------------------------------------------

def _panel(arr_u8, label, side):
    im = Image.fromarray(arr_u8).resize((side, side), Image.LANCZOS).convert("RGB")
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, side - 1, 16], fill=(0, 0, 0))
    d.text((4, 4), label, fill=(255, 255, 255))
    return im


def write_previews(out_dir, base, old_nrm_u8, new_nrm_u8, metallic, rough, style, crop_at):
    side = 640
    paths = []

    sheet = Image.new("RGB", (side * 2 + 8, side), (24, 24, 24))
    sheet.paste(_panel(old_nrm_u8, f"{style}  SHIPPED normal", side), (0, 0))
    sheet.paste(_panel(new_nrm_u8, f"{style}  NEW normal (feature=shipped, material=albedo)", side), (side + 8, 0))
    p = os.path.join(out_dir, f"{base}_preview_normal.png")
    sheet.save(p); paths.append(p)

    # 100% crops -- the material band lives at 1-8 texels and a downscaled sheet
    # cannot show it.  A preview that cannot resolve the thing being judged agrees
    # with every build.
    y, x = crop_at
    c = 512
    cs = Image.new("RGB", (c * 2 + 8, c), (24, 24, 24))
    cs.paste(_panel(old_nrm_u8[y:y + c, x:x + c], f"SHIPPED 100% @({x},{y})", c), (0, 0))
    cs.paste(_panel(new_nrm_u8[y:y + c, x:x + c], f"NEW 100% @({x},{y})", c), (c + 8, 0))
    p = os.path.join(out_dir, f"{base}_preview_normal_crop.png")
    cs.save(p); paths.append(p)

    met8 = np.rint(np.clip(metallic, 0, 1) * 255).astype(np.uint8)
    rgh8 = np.rint(np.clip(rough, 0, 1) * 255).astype(np.uint8)
    g = lambda a: np.repeat(a[..., None], 3, axis=2)
    sheet = Image.new("RGB", (side * 2 + 8, side), (24, 24, 24))
    sheet.paste(_panel(g(met8), f"{style}  METALLIC (MRS.R)  mean {metallic.mean():.3f}", side), (0, 0))
    sheet.paste(_panel(g(rgh8), f"{style}  ROUGHNESS (MRS.G)  mean {rough.mean():.3f}", side), (side + 8, 0))
    p = os.path.join(out_dir, f"{base}_preview_mrs.png")
    sheet.save(p); paths.append(p)
    return paths


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--albedo", required=True, help="the NEW albedo atlas (sRGB PNG)")
    ap.add_argument("--old-normal", required=True, help="the SHIPPED normal map (mesh-registered feature relief)")
    ap.add_argument("--style", required=True, choices=sorted(STYLES))
    ap.add_argument("--out", required=True, help="output directory")
    ap.add_argument("--base", default=None,
                    help="output basename; default is derived from --albedo (…_albedo.png -> …)")
    ap.add_argument("--sigma", type=float, default=3.0,
                    help="feature/material cutoff, in TEXELS of Gaussian sigma (default 3.0). "
                         "The half-gain wavelength is 5.34*sigma texels.")
    ap.add_argument("--target-p99", type=float, default=None,
                    help="override the style's target p99 perturbation angle, in degrees")
    ap.add_argument("--x-convention", choices=("pipeline", "opengl"), default="pipeline",
                    help="sign of RED. 'pipeline' (default) matches the shipped maps "
                         "(nx=+dh/du, as tex_common.normal_from_height writes them); "
                         "'opengl' is the strict UnpackNormal convention (nx=-dh/du).")
    ap.add_argument("--gate-amount", type=float, default=0.65)
    ap.add_argument("--gate-floor", type=float, default=0.35)
    ap.add_argument("--strength-scale", type=float, default=1.0,
                    help="manual multiplier on top of the calibrated strength")
    ap.add_argument("--soft-clip-k", type=float, default=2.5,
                    help="tanh knee on the high-pass height, in standard deviations")
    ap.add_argument("--rough-floor", type=float, default=ROUGH_FLOOR)
    ap.add_argument("--keep-shipped-hf", action="store_true",
                    help="keep ALL of the shipped map's high-frequency band, not just the part "
                         "the ornament mask calls carving. That re-admits the OLD art's grain "
                         "alongside the new art's. Off by default -- see build_normal().")
    ap.add_argument("--falsify-shift", type=int, default=8,
                    help="texels of deliberate misregistration for falsifier 2 "
                         "(8 texels ~= 5.3 mm at the boards' ~1500 px/m)")
    ap.add_argument("--no-falsify", action="store_true")
    ap.add_argument("--no-previews", action="store_true")
    a = ap.parse_args(argv)

    if a.rough_floor < 0.35:
        raise SystemExit(f"--rough-floor {a.rough_floor} is below 0.35. BoardLit's own floor "
                         f"is 0.08 (a 2.71 deg lobe half-angle = a mirror) and a mirror lobe on "
                         f"a high-frequency normal map is this project's stereo-rivalry defect.")

    os.makedirs(a.out, exist_ok=True)
    base = a.base
    if base is None:
        base = os.path.basename(a.albedo)
        for suf in ("_albedo.png", "_albedo.PNG", ".png"):
            if base.endswith(suf):
                base = base[:-len(suf)]
                break

    alb_im = Image.open(a.albedo).convert("RGB")
    nrm_im = Image.open(a.old_normal).convert("RGB")
    if alb_im.size != nrm_im.size:
        raise SystemExit(f"albedo {alb_im.size} != shipped normal {nrm_im.size} — "
                         f"the feature band and the material band must share the UVs")
    alb = np.asarray(alb_im, np.float64) / 255.0
    old_nrm_u8 = np.asarray(nrm_im, np.uint8)
    old_nrm = old_nrm_u8.astype(np.float64) / 255.0

    target = a.target_p99 if a.target_p99 is not None else STYLES[a.style]["target_p99_deg"]

    W = alb.shape[1]
    px_per_m = 1516.8      # the boards' frame/field islands, from <style>_uv.json
    lam50_tex = 5.34 * a.sigma
    lam50_mm = lam50_tex / px_per_m * 1000.0

    print("=" * 78)
    print(f"  {base}   style={a.style}   {alb_im.size[0]}x{alb_im.size[1]}")
    print("=" * 78)
    print(f"albedo      {a.albedo}")
    print(f"old normal  {a.old_normal}")
    print()
    print("-- BAND SPLIT ------------------------------------------------------------")
    print(f"cutoff             sigma = {a.sigma:.2f} texels")
    print(f"  half-gain wavelength   {lam50_tex:.1f} texels = {lam50_mm:.2f} mm  (at {px_per_m:.0f} px/m)")
    for lam_mm in (1.0, 2.0, 5.0, 10.0, 24.0, 50.0):
        lam = lam_mm / 1000.0 * px_per_m
        gain = 1.0 - np.exp(-2 * np.pi ** 2 * a.sigma ** 2 / lam ** 2)
        print(f"  high-pass gain at {lam_mm:5.1f} mm ({lam:6.1f} tex): {gain:5.3f}")
    print("  KEPT from the new albedo   : everything the high-pass passes -- wood pore,")
    print("    brush line, casting porosity, hammer mark, pitting. Stochastic, so its")
    print("    registration to the mesh is not a question that has an answer.")
    print("  KEPT from the shipped map  : everything it rejects -- the frame band is")
    print("    24 mm wide and the card recess 152x228 mm (oak_uv.json feature_metrics_m),")
    print("    so every real geometric feature on this board is >= 24 mm and lands in the")
    print("    feature band, where the SHIPPED, mesh-registered map supplies it.")
    print()

    mrs_u8, metallic, rough, extra = build_mrs(alb, a.style, a.rough_floor)
    rough_probe = rough

    nrm_u8, info = build_normal(alb, old_nrm, a.style, a.sigma, target, a.x_convention,
                                a.gate_amount, a.gate_floor, a.strength_scale,
                                a.keep_shipped_hf, a.soft_clip_k)

    print("-- NORMAL ----------------------------------------------------------------")
    print(f"x convention        {info['x_convention']}   (red = "
          f"{'+' if a.x_convention == 'pipeline' else '-'}dh/du)")
    print(f"height->slope gain  {info['strength']:.4f} (solved for the target, not tuned)")
    print(f"ornament mask       mean {info['ornament_mean']:.3f}   "
          f"coverage(>0.5) {info['ornament_coverage']*100:.2f}% of texels")
    print(f"  the shipped map's own high frequency is kept AT this weight (carving) and")
    print(f"  discarded at 1-this (the old art's grain); the new material band is gated by")
    print(f"  its complement, floor {a.gate_floor:.2f}. gate mean {info['gate_mean']:.3f} min {info['gate_min']:.3f}")
    print(f"perturbation angle off the surface normal, degrees:")
    print(f"  MATERIAL band (added)  median {info['material_tilt_median_deg']:6.2f}   "
          f"p99 {info['material_tilt_p99_deg']:6.2f}   (target p99 {target:.1f})")
    print(f"  FEATURE band (shipped) median {info['feature_tilt_median_deg']:6.2f}   "
          f"p99 {info['feature_tilt_p99_deg']:6.2f}")
    print(f"  COMBINED               median {info['total_tilt_median_deg']:6.2f}   "
          f"p99 {info['total_tilt_p99_deg']:6.2f}")
    print(f"  the map this REPLACES   median {info['shipped_tilt_median_deg']:6.2f}   "
          f"p99 {info['shipped_tilt_p99_deg']:6.2f}   "
          f"(p95 {info['shipped_tilt_p95_deg']:.2f} vs ours {pct(tilt_deg(*info['_slopes']),95):.2f})")
    print(f"  -- the shipped maps already put ~1% of their texels past 70 deg (bevel and")
    print(f"     recess walls), so a high COMBINED p99 is inherited, not introduced.")
    print(f"  tilt clamp at {MAX_TOTAL_TILT_DEG:.0f} deg hit by {info['clamped_texels']} texels "
          f"({info['clamped_texels']/alb[...,0].size*100:.4f}%)")
    print(f"single-texel angular step (what a narrow lobe turns into per-eye sparkle):")
    print(f"  MATERIAL p95   u {info['material_step_p95_u']:5.2f}  v {info['material_step_p95_v']:5.2f} deg")
    print(f"  COMBINED p95   u {info['total_step_p95_u']:5.2f}  v {info['total_step_p95_v']:5.2f} deg")
    print(f"  SHIPPED  p95   u {info['shipped_step_p95_u']:5.2f}  v {info['shipped_step_p95_v']:5.2f} deg"
          f"   <- the baseline the stereo-rivalry risk is judged against")
    print(f"  lobe half-angle at this style's median roughness: "
          f"{lobe_half_angle_deg(pct(rough_probe, 50)):.2f} deg")
    print()

    print("-- MRS  (R=metallic  G=roughness  B=unused) -------------------------------")
    print(f"metallic   mean {metallic.mean():.4f}   p1 {pct(metallic,1):.4f}   "
          f"p50 {pct(metallic,50):.4f}   p99 {pct(metallic,99):.4f}")
    print(f"roughness  mean {rough.mean():.4f}   p1 {pct(rough,1):.4f}   "
          f"p50 {pct(rough,50):.4f}   p99 {pct(rough,99):.4f}   floor {a.rough_floor:.2f}")
    print(f"B channel  max {int(mrs_u8[...,2].max())}   (BoardLit never reads it)")
    print(f"lobe half-angle at the roughness floor {a.rough_floor:.2f}: "
          f"{lobe_half_angle_deg(a.rough_floor):.2f} deg "
          f"(BoardLit's own floor 0.08 -> {lobe_half_angle_deg(0.08):.2f} deg, a mirror)")
    for k, v in sorted(extra.items()):
        print(f"classifier  {k:22s} {v:.4f}")
    print()

    # ---- write --------------------------------------------------------------
    pn = os.path.join(a.out, f"{base}_normal.png")
    pm = os.path.join(a.out, f"{base}_mrs.png")
    Image.fromarray(nrm_u8).save(pn, optimize=True)
    Image.fromarray(mrs_u8).save(pm, optimize=True)

    print("-- VERIFY ----------------------------------------------------------------")
    enc = verify_normal_encoding(np.asarray(Image.open(pn).convert("RGB"), np.uint8))
    print(f"normal, decoded from the WRITTEN 8-bit file:")
    print(f"  |n| within 1% of unit : {enc['unit_frac']*100:.3f}%   "
          f"(|n| range {enc['len_min']:.4f} .. {enc['len_max']:.4f})")
    print(f"  z > 0                 : {enc['zpos_frac']*100:.3f}%   (z min {enc['z_min']:.4f})")
    print(f"  both                  : {enc['both_frac']*100:.3f}%")

    ok_pack, checks = verify_packing(np.asarray(Image.open(pm).convert("RGB"), np.uint8), a.style)
    print(f"FALSIFIER 1 -- MRS vs glTF ORM packing:")
    for name, ok, detail in checks:
        print(f"  [{'PASS' if ok else 'FAIL'}] {name:38s} {detail}")
    orm = build_orm_control(alb, metallic, rough)
    ok_orm, checks_orm = verify_packing(orm, a.style)
    print(f"  negative control (the same data packed as glTF ORM) -> "
          f"{'REJECTED (good)' if not ok_orm else 'ACCEPTED -- THE TEST IS BLIND'}")
    for name, ok, detail in checks_orm:
        print(f"    [{'pass' if ok else 'fail'}] {name:36s} {detail}")

    reg = None
    if not a.no_falsify:
        reg = verify_registration(alb, old_nrm, a.style, a, info["_slopes"])
        print(f"FALSIFIER 2 -- feature registration (albedo shifted {reg['shift_texels']} texels "
              f"= {reg['shift_texels']/px_per_m*1000:.1f} mm):")
        print(f"  OUR normal   corr(features, SHIPPED normal) = {reg['r_out_ship']:+.4f}")
        print(f"  OUR normal   corr(features, shifted ALBEDO) = {reg['r_out_alb']:+.4f}"
              f"    margin {reg['margin']:+.4f}")
        print(f"  negative control (normal derived wholly from the shifted albedo):")
        print(f"    corr(features, SHIPPED normal) = {reg['r_naive_ship']:+.4f}")
        print(f"    corr(features, shifted ALBEDO) = {reg['r_naive_alb']:+.4f}"
              f"    -> control tracks the ALBEDO: {reg['control_ok']}")
        print(f"  [{'PASS' if reg['ok'] else 'FAIL'}] feature relief comes from the shipped normal")

    print("MRS classifier self-test (synthetic patches -- proves the hue/oxide tests fire")
    print("even when a stand-in albedo happens to contain none of that material):")
    for label, m, r, ok in selftest_mrs():
        print(f"  [{'PASS' if ok else 'FAIL'}] {label:26s} metallic {m:.3f}  roughness {r:.3f}")

    paths = [pn, pm]
    if not a.no_previews:
        h, w = alb.shape[:2]
        paths += write_previews(a.out, base, old_nrm_u8, nrm_u8, metallic, rough,
                                a.style, (h // 2 - 256, w // 2 - 256))

    print()
    print("-- WROTE -----------------------------------------------------------------")
    for p in paths:
        print("  " + os.path.abspath(p))

    report = dict(base=base, style=a.style, sigma=a.sigma,
                  half_gain_wavelength_texels=lam50_tex, half_gain_wavelength_mm=lam50_mm,
                  normal={k: v for k, v in info.items() if not k.startswith("_")},
                  encoding=enc,
                  metallic=dict(mean=float(metallic.mean()), p1=pct(metallic, 1),
                                p50=pct(metallic, 50), p99=pct(metallic, 99)),
                  roughness=dict(mean=float(rough.mean()), p1=pct(rough, 1),
                                 p50=pct(rough, 50), p99=pct(rough, 99),
                                 floor=a.rough_floor),
                  classifier=extra, packing_ok=bool(ok_pack),
                  orm_control_rejected=bool(not ok_orm), registration=reg)
    with open(os.path.join(a.out, f"{base}_maps_report.json"), "w") as f:
        json.dump(report, f, indent=1)

    hard_fail = (not ok_pack) or ok_orm or (reg is not None and not reg["ok"])
    print()
    print("RESULT: " + ("FAIL -- a verification did not hold, do not ship these maps"
                        if hard_fail else "PASS"))
    return 1 if hard_fail else 0


if __name__ == "__main__":
    sys.exit(main())
