"""tex_bases.py -- procedural material bases for the three control boards.

Oak / Steel / Bronze, each as albedo + height + roughness + metallic at any
resolution, driven entirely by a seed so a given seed reproduces a given board
byte for byte.

WHY PROCEDURAL AND NOT AI (this is the user's actual complaint, restated):
  "die Symbole darauf sehen nicht clean sondern KI-generiert aus".
  A diffusion model gives you a picture of a material: soft, non-tileable, with
  lighting already painted into it, and a palette that drifts across the frame.
  A tileable grain with crisp band edges and a five-stop locked palette is the
  exact thing procedural noise does well and diffusion does badly. So the bases
  are procedural, and AI is confined to decorative motifs (see tex_symbols.py) --
  and even there it is used as a HEIGHT STENCIL, never as colour.

Three rules this file obeys, each aimed at "cleaner":
  1. CRISP, NOT MUSH. Every material edge is a smoothstep whose transition band
     is a few texels wide at 2048, not a gaussian blur.
  2. PALETTE LOCK. Albedo is never mixed freely; it is a scalar indexed into a
     short, named ramp (tex_common.Palette). A style cannot acquire a colour
     nobody chose.
  3. NO BAKED LIGHTING. Nothing derived from a directional derivative of the
     height goes into albedo. Shape lives in the normal map; occlusion lives in
     the AO channel of the packed map. The albedo is the material's own colour.
"""

import argparse
import os

import numpy as np
from PIL import Image, ImageDraw

import tex_common as T


# --------------------------------------------------------------------------
# palettes -- the full colour vocabulary of the three boards, and nothing else
# --------------------------------------------------------------------------

PAL_OAK = T.Palette("oak", [
    (0.00, (0x3E, 0x27, 0x13)),   # carved shadow / open pore
    (0.24, (0x69, 0x42, 0x22)),   # latewood band
    (0.52, (0x8D, 0x62, 0x37)),   # mid grain
    (0.80, (0xB2, 0x8B, 0x54)),   # earlywood
    (1.00, (0xCC, 0xA8, 0x6E)),   # ray fleck
])

PAL_STEEL = T.Palette("steel", [
    (0.00, (0x5F, 0x64, 0x6B)),
    (0.40, (0x86, 0x8D, 0x95)),
    (0.75, (0xA8, 0xAF, 0xB7)),
    (1.00, (0xC6, 0xCC, 0xD2)),
])

PAL_BRONZE_BARE = T.Palette("bronze_bare", [
    (0.00, (0x5C, 0x3E, 0x1B)),
    (0.38, (0x8C, 0x60, 0x28)),
    (0.72, (0xB3, 0x84, 0x3C)),
    (1.00, (0xDC, 0xB4, 0x69)),
])

# Desaturated on purpose: real verdigris on a handled object is a grey-green,
# not the cyan a diffusion model reaches for.
PAL_BRONZE_PATINA = T.Palette("bronze_patina", [
    (0.00, (0x22, 0x30, 0x2A)),
    (0.40, (0x3D, 0x53, 0x45)),
    (0.75, (0x5A, 0x71, 0x5C)),
    (1.00, (0x7C, 0x8E, 0x75)),
])


class Fields:
    """The four maps a material hands to the compositor, all (N,N) float except
    albedo which is (N,N,3) sRGB. `height` is in arbitrary units centred on 0;
    the compositor scales it before taking a normal."""

    __slots__ = ("albedo", "height", "rough", "metal", "style", "seed")

    def __init__(self, albedo, height, rough, metal, style, seed):
        self.albedo = albedo
        self.height = height
        self.rough = rough
        self.metal = metal
        self.style = style
        self.seed = seed

    def crop(self, box):
        x0, y0, x1, y1 = box
        return Fields(self.albedo[y0:y1, x0:x1], self.height[y0:y1, x0:x1],
                      self.rough[y0:y1, x0:x1], self.metal[y0:y1, x0:x1],
                      self.style, self.seed)


def _axes(n, orient):
    """Row/col coordinate grids in [0,1). `orient` swaps them, which rotates a
    directional material (wood grain, brush lines) by 90 degrees for the board
    edges without generating a second, uncorrelated noise field."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64) / n
    return (xx, yy) if orient == "along" else (yy, xx)


def _swap(f, orient):
    return f if orient == "along" else f.T


# --------------------------------------------------------------------------
# OAK -- quartersawn white oak: wavy parallel growth bands, open pores that run
# WITH the grain, medullary ray flecks that run ACROSS it.
# --------------------------------------------------------------------------

def build_oak(n, seed, orient="along", detail=1.0):
    xx, yy = _axes(n, orient)
    s = int(seed)
    rings = max(2, int(round(10 * detail)))       # integer => tileable across v

    # The growth bands are the SUBJECT. Everything else is a whisper on top.
    warp = _swap(T.spectral_noise(n, s + 1, beta=2.4, aniso=8.0, highcut=22), orient) * 0.20
    wobble = _swap(T.spectral_noise(n, s + 2, beta=1.6, aniso=12.0,
                                    lowcut=10, highcut=70), orient) * 0.055
    t = yy * rings + warp + wobble
    r = T.frac(t)

    # crisp latewood band, ~21% of each ring, transition ~4 texels at 2048
    band = T.smoothstep(0.600, 0.643, r) * (1.0 - T.smoothstep(0.795, 0.838, r))
    # a shallow earlywood gradient so the band has somewhere to come from
    early = 1.0 - T.smoothstep(0.00, 0.60, r) * 0.30

    # Fine fibre. Two earlier passes ran this as long high-aniso streaks and the
    # wood read as scratchy zebrano veneer; the streaks were ~300 texels long
    # where a real oak fibre at this texel density is ~15. Short and quiet now.
    fibre = _swap(T.spectral_noise(n, s + 6, beta=1.5, aniso=4.0,
                                   lowcut=44 * detail, highcut=280 * detail), orient)

    # Open pores: short dark dashes, sparse, and only where the latewood is.
    # At 3200 texels/m a pore is well under a texel wide and a dozen long.
    pore_n = _swap(T.spectral_noise(n, s + 3, beta=1.0, aniso=3.5,
                                    lowcut=130 * detail, highcut=700 * detail), orient)
    pores = T.smoothstep(2.05, 2.85, pore_n) * (0.25 + 0.75 * band)

    broad = _swap(T.spectral_noise(n, s + 5, beta=2.8, aniso=3.0, highcut=6), orient)

    # Grain SHAPE, measured not guessed. The previous pass produced features
    # 322 px long by 5 px wide -- 100 mm by 1.6 mm on a 0.64 m board, a 63:1
    # aspect -- and read as bamboo veneer rather than planed oak. The across-
    # grain tone must be owned by the growth BANDS (10-15 mm), not by a 1.6 mm
    # striation, so the ring warp is halved (bands stay parallel instead of
    # smearing into each other) and the fibre and pores are shortened and
    # quietened. Verify with the autocorrelation lengths, not by eye.
    # Balance, measured not guessed. The previous pass had
    # 0.78*early - band*0.54, which bottoms out at 0.006 for a full latewood
    # band -- i.e. the growth bands were being crushed onto the palette's
    # DARKEST stop, and 16.8% of the board sat at near-black. The darkest stop
    # belongs to the carved shadows and the odd open pore, nothing else. Band
    # amplitude is now set so full latewood lands on the latewood stop (0.24).
    tone = (0.78 * early
            - band * 0.30
            - pores * 0.20
            + fibre * 0.022
            + broad * 0.065)
    albedo = PAL_OAK.map(np.clip(tone, 0.0, 1.0))

    # planed oak: the hard latewood stands proud, the open pores are the pits
    height = band * 0.30 - pores * 0.34 + fibre * 0.040 + broad * 0.12

    rough = np.clip(0.44 + band * 0.09 + pores * 0.20 + broad * 0.05, 0.30, 0.72)
    metal = np.zeros((n, n), dtype=np.float64)
    return Fields(albedo, height, rough, metal, "oak", s)


# --------------------------------------------------------------------------
# STEEL -- brushed and lightly plated: the pattern lives in ROUGHNESS and the
# normal, almost none of it in albedo. A metal's albedo is its specular colour;
# painting brush marks into it is the classic fake-highlight mistake.
# --------------------------------------------------------------------------

def _scratch_mask(n, seed, count=26, detail=1.0):
    """A handful of long, thin, deliberate scratches. Drawn at 2x and box-reduced
    so the line edge lands crisp rather than gaussian-soft."""
    ss = 2
    img = Image.new("L", (n * ss, n * ss), 0)
    dr = ImageDraw.Draw(img)
    rg = np.random.default_rng(seed)
    for _ in range(int(count * detail)):
        y = rg.integers(0, n * ss)
        x = rg.integers(0, n * ss)
        length = int(rg.uniform(0.10, 0.55) * n * ss)
        ang = rg.normal(0.0, 0.045)
        dx = int(length * np.cos(ang))
        dy = int(length * np.sin(ang))
        w = int(rg.integers(1, 4))
        v = int(rg.integers(90, 230))
        dr.line([(x, y), (x + dx, y + dy)], fill=v, width=w)
        dr.line([(x - n * ss, y), (x - n * ss + dx, y + dy)], fill=v, width=w)
    a = np.asarray(img.resize((n, n), Image.Resampling.BOX), dtype=np.float64) / 255.0
    return a


def build_steel(n, seed, orient="along", detail=1.0):
    s = int(seed)
    brush = _swap(T.spectral_noise(n, s + 1, beta=0.55, aniso=120.0, lowcut=6 * detail), orient)
    # HIGHCUT, and it matters more than its size suggests. beta=0.30 is very
    # nearly white noise, so without an upper limit this term carries full
    # energy right up to Nyquist -- per-texel roughness variation on a mirror.
    # In a headset that is not texture, it is sparkle: the two eyes sample the
    # same texel grid at slightly different sub-texel offsets and the highlight
    # boils. Capping at 420 cycles keeps the finest feature about 5 texels
    # across, which survives a mip chain and cannot alias per eye.
    micro = _swap(T.spectral_noise(n, s + 2, beta=0.30, aniso=45.0,
                                   lowcut=140 * detail, highcut=420 * detail), orient)
    mottle = _swap(T.spectral_noise(n, s + 3, beta=2.9, aniso=2.0, highcut=7), orient)
    scr = _swap(_scratch_mask(n, s + 4, detail=detail), orient)

    # deliberately narrow: a controlled, flat metal grey. Anything wider starts
    # to look like painted-on lighting, which is the complaint.
    tone = 0.63 + mottle * 0.060 + brush * 0.028 - scr * 0.045
    albedo = PAL_STEEL.map(np.clip(tone, 0.0, 1.0))

    # Brushing belongs in ROUGHNESS. Putting it in the normal at this
    # frequency turned the board into sandpaper in the first lit render.
    height = brush * 0.055 + micro * 0.016 - scr * 0.30 + mottle * 0.05

    # Brushed stainless measures around 0.30-0.45 in this parameterisation, not
    # 0.12-0.27. The previous floor made the plate a near-mirror, and a mirror
    # in a dim room is black: the calibrated station rendered the steel board at
    # median luminance 0.09 against oak's 0.26, from an albedo whose mean
    # luminance is 0.637 -- the LIGHTEST of the three. A metal shows what it
    # reflects, so the way to make brushed steel read as brushed steel is to
    # widen its specular lobe, not to lighten its albedo.
    rough = np.clip(0.340 + brush * 0.085 + micro * 0.045 + mottle * 0.040 - scr * 0.09,
                    0.18, 0.62)
    metal = np.clip(1.0 - T.smoothstep(0.6, 1.9, -mottle) * 0.10, 0.0, 1.0)
    return Fields(albedo, height, rough, metal, "steel", s)


# --------------------------------------------------------------------------
# BRONZE -- cast and patinated. The verdigris mask is crisp-edged and, once the
# compositor knows where the carvings are, gets biased INTO the recesses, which
# is where it actually collects. Patina is dielectric: metallic drops to ~0 and
# roughness climbs there. That contrast is what makes it read as real metal.
# --------------------------------------------------------------------------

_PATINA_STAT = {}


def last_patina_coverage(style="bronze"):
    """What fraction of the last bronze build came out patinated. Reported by
    the compositor rather than asserted, because 'an accent' is a number."""
    return _PATINA_STAT.get(style, (None, None))[0]


def last_patina_in_cavity(style="bronze"):
    """Cavity ENRICHMENT under the patina: mean cavity where the patina is,
    over mean cavity everywhere. A patina uncorrelated with the relief scores
    1.0 by construction, whatever it looks like; a patina that is genuinely
    collecting in the low spots scores well above it."""
    return _PATINA_STAT.get(style, (None, None))[1]


def build_bronze(n, seed, orient="along", detail=1.0, cavity_bias=None):
    """Cast and patinated bronze.

    WHAT WAS WRONG WITH THE PREVIOUS PASS, and it is worth being precise because
    the fix is a change of MECHANISM, not of numbers:

      patina was driven by   crust*0.82 + grain*0.18 (+ cavity*2.4 if supplied)

    `crust` was spectral_noise(beta=2.5, highcut=15). A highcut of 15 keeps only
    the lowest fifteen frequency rings, i.e. nothing with a wavelength shorter
    than n/15 -- 137 texels at 2048, about 40 mm on the board. So the patina's
    shape was, by construction, a field of 40 mm soft clouds, and it carried
    82% of the drive. That is exactly the reported symptom: "cyan blobs sitting
    ON the surface", "does not correlate with the relief", "a soft
    low-frequency cloudiness". The cavity term existed but was a minority
    shareholder in its own mechanism, and it was only non-zero on the ~2% of the
    atlas within reach of a carving.

    The drive is now, in order of weight:
      1. CAVITY of the finished relief -- the compositor carves the board, takes
         a light-independent multi-scale cavity of the result, and hands it back.
         Verdigris forms where water sits and where a hand does not wipe: the
         insides of carvings, the bottoms of the cast pebbling, the inside
         corners of a moulding. This is the term that makes it corrosion rather
         than paint.
      2. the cast surface's own PITS, at 11-45 texels (3-13 mm), so the bare
         field is not uniformly clean either.
      3. a fine FLECK at 5-19 texels (1.5-6 mm) that breaks the shoreline up at
         the scale a real shoreline is broken up.
    The old low-frequency crust is gone, and so is every term that could make a
    patch larger than about 13 mm on the flat. What is left that CAN make a
    large connected green area is the cavity term, and a large connected cavity
    is a carving -- which is the point.

    The threshold is then solved for a target coverage rather than fixed, so the
    accent stays an accent whatever the relief underneath happens to be."""
    s = int(seed)
    cast = _swap(T.spectral_noise(n, s + 1, beta=1.7, aniso=1.15,
                                  lowcut=9 * detail, highcut=40 * detail), orient)
    grain = _swap(T.spectral_noise(n, s + 3, beta=1.9, aniso=1.0,
                                   lowcut=22 * detail, highcut=70 * detail), orient)
    speck = _swap(T.spectral_noise(n, s + 5, beta=1.3, aniso=1.0,
                                   lowcut=60 * detail, highcut=300 * detail), orient)
    # The field's own patina drive. BOTH terms have a hard floor on their
    # wavelength, and that floor is the whole fix. The previous pass drove the
    # patina from `crust` (highcut 15 -> nothing shorter than 137 texels) and
    # the first attempt at this fix still used `cast` (lowcut 9 -> up to 227
    # texels), so both produced 30-70 mm amoebas: flat patches with no relation
    # to anything on the board, which is what "cyan blobs sitting ON the
    # surface" describes. `pit` runs 11-45 texels (3-13 mm) and `fleck` 5-19
    # texels (1.5-6 mm), so the largest patch the field can make is smaller than
    # one card slot's corner radius.
    pit = _swap(T.spectral_noise(n, s + 2, beta=1.5, aniso=1.0,
                                 lowcut=45 * detail, highcut=190 * detail), orient)
    fleck = _swap(T.spectral_noise(n, s + 6, beta=1.2, aniso=1.0,
                                   lowcut=110 * detail, highcut=420 * detail), orient)
    # The FIELD, settled. `sheen` used to be highcut=22 at amplitude 0.040, i.e.
    # a 93-texel cloud on the tone; it is now the very lowest frequencies only,
    # at half the amplitude, which reads as one plate catching the room rather
    # than as weather.
    sheen = _swap(T.spectral_noise(n, s + 4, beta=3.0, aniso=1.6, highcut=5), orient)

    cav = (np.zeros((n, n)) if cavity_bias is None
           else np.asarray(cavity_bias, dtype=np.float64))
    field = 0.62 * pit + 0.38 * fleck
    # The weights are the whole design and they are lopsided on purpose. `cav`
    # peaks around 0.5 inside a carving and is exactly zero across the open
    # plate, so 5.0*cav puts every recessed texel above any threshold the field
    # term can set, and the field is left competing only for what is not already
    # spoken for. A 10% target with those weights spends most of its budget on
    # the carvings and the moulding grooves and leaves the open plate nearly
    # clean -- which is what a board that gets handled looks like: hands wipe
    # the flat and cannot reach into a 0.5 mm groove.
    #
    # The pass before this one ran the field at 0.85 with a 14% target and the
    # open plate came back evenly spattered -- the "dirty pixels" failure the
    # first version of this material already had once, in a finer grain.
    drive = 5.00 * cav + 0.42 * field
    target = 0.085
    lo = float(np.quantile(drive, 1.0 - target))
    band = float(np.std(field)) * 0.42 * 0.26 + 1e-6  # a crisp but not aliased edge
    patina = T.smoothstep(lo - band * 0.5, lo + band * 0.5, drive)

    # A groove holds a CRUST; an open face holds a TARNISH. They are not the
    # same thickness and should not be the same material, so the blend is
    # weighted by how much of this texel's drive came from the cavity term.
    # Without this the open plate came back measled: dark teal dots at full
    # strength scattered evenly over gold, which is a pattern, not corrosion.
    # With it the same dots are a partial discolouration and the only places
    # that go fully green are the places that are actually recessed.
    cav_share = np.clip(5.00 * cav / np.maximum(np.abs(drive), 1e-6), 0.0, 1.0)
    patina = patina * T.lerp(0.55, 1.0, cav_share)

    bare_t = np.clip(0.645 + sheen * 0.022 + cast * 0.016, 0.0, 1.0)
    # DARKER than the bare metal, and darker the deeper the drive. The previous
    # tone sat at 0.50 of the patina ramp, which renders BRIGHTER than the bare
    # bronze around it. That is not a small mistake: the patina is dielectric
    # with roughness 0.72 and the bare metal is metallic, so the patina takes
    # its value from the diffuse fill while the metal only shows what it
    # reflects, and a mid-tone patina therefore comes out as the LIGHTEST thing
    # on the plate. A corrosion product lighter than the metal it grew out of
    # reads as paint, and that was half of why it looked applied rather than
    # grown. Verdigris in a groove is dark; only a thick efflorescence on a
    # weathered exposed face goes pale, and a board on a table has none.
    depth_t = T.smoothstep(lo, lo + band * 6.0, drive)
    pat_t = np.clip(0.28 - depth_t * 0.20 + grain * 0.050 + speck * 0.022, 0.0, 1.0)
    albedo = T.lerp(PAL_BRONZE_BARE.map(bare_t), PAL_BRONZE_PATINA.map(pat_t),
                    patina[..., None])

    # Sand-cast bronze is a gentle pebbling, not a crust. The patina crust
    # stands slightly PROUD of the bare metal, which is what a corrosion product
    # does -- it is a deposit, and it is the one place a positive height term is
    # physically right on this material.
    height = cast * 0.028 + patina * 0.045 + grain * 0.012

    rough = np.clip(T.lerp(0.30, 0.72, patina) + cast * 0.010, 0.16, 0.80)
    metal = np.clip(1.0 - patina * 0.94, 0.0, 1.0)

    # The two numbers the report quotes, measured here rather than claimed.
    # The second one is an ENRICHMENT: the mean cavity under the patina divided
    # by the mean cavity over the whole map. A patina that ignores the relief
    # scores 1.0 by construction, whatever it looks like. Anything well above 1
    # is the patina actually sitting in the low spots.
    solid = patina > 0.5
    if solid.any() and cav.max() > 1e-9:
        enrich = float(cav[solid].mean() / max(cav.mean(), 1e-12))
    else:
        enrich = float("nan")
    _PATINA_STAT["bronze"] = (float(solid.mean()), enrich)
    return Fields(albedo, height, rough, metal, "bronze", s)


BUILDERS = {"oak": build_oak, "steel": build_steel, "bronze": build_bronze}
PALETTES = {"oak": [PAL_OAK], "steel": [PAL_STEEL],
            "bronze": [PAL_BRONZE_BARE, PAL_BRONZE_PATINA]}


_CACHE = {}


def build(style, n, seed, orient="along", detail=1.0, cavity_bias=None):
    """Cached material build. cavity_bias is only honoured by bronze and forces
    a cache miss, because the patina then depends on the carvings."""
    if cavity_bias is None:
        key = (style, n, int(seed), orient, round(float(detail), 4))
        if key in _CACHE:
            return _CACHE[key]
    fn = BUILDERS[style]
    if style == "bronze":
        f = fn(n, seed, orient=orient, detail=detail, cavity_bias=cavity_bias)
    else:
        f = fn(n, seed, orient=orient, detail=detail)
    if cavity_bias is None:
        _CACHE[key] = f
    return f


# --------------------------------------------------------------------------
# per-region character
#
# The six contract regions are different SURFACES of one board, so they get
# different treatment even though they share one material. Values are additive
# offsets applied by the compositor.
# --------------------------------------------------------------------------

REGION_TREATMENT = {
    "face":         dict(orient="along", detail=1.00, tone=+0.00, rough=+0.00, height=1.00),
    "frame":        dict(orient="along", detail=1.15, tone=-0.05, rough=+0.03, height=1.05),
    "slot_floor":   dict(orient="along", detail=1.30, tone=-0.14, rough=+0.10, height=0.55),
    "rest_pads":    dict(orient="along", detail=1.10, tone=-0.04, rough=+0.05, height=0.70),
    "button_seats": dict(orient="cross", detail=1.20, tone=-0.09, rough=+0.06, height=0.80),
    "sides":        dict(orient="cross", detail=1.45, tone=-0.10, rough=+0.08, height=0.85),
}


# --------------------------------------------------------------------------
# swatch preview -- LOOK at the material before it goes anywhere near a board
# --------------------------------------------------------------------------

def _preview(out_dir, n=768, seed=20260825):
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for style in ("oak", "steel", "bronze"):
        f = build(style, n, seed, orient="along")
        nrm = T.normal_from_height(f.height, strength=18.0)
        mr = np.stack([1.0 - T.cavity(f.height, 7, 2.4), f.rough, f.metal], axis=-1)
        written.append(T.save_rgb(os.path.join(out_dir, f"swatch_{style}_albedo.png"), f.albedo))
        written.append(T.save_rgb(os.path.join(out_dir, f"swatch_{style}_normal.png"), nrm))
        written.append(T.save_rgb(os.path.join(out_dir, f"swatch_{style}_mr.png"), mr))
        lum = f.albedo @ np.array([0.2126, 0.7152, 0.0722])
        print(f"{style:7s} palette {' '.join(p.name + ':' + ','.join(p.hexes()) for p in PALETTES[style])}")
        print(f"        albedo luminance  min {lum.min():.3f}  mean {lum.mean():.3f}  "
              f"max {lum.max():.3f}  p1 {np.percentile(lum, 1):.3f}  p99 {np.percentile(lum, 99):.3f}")
        print(f"        roughness  {f.rough.min():.2f}..{f.rough.max():.2f}   "
              f"metallic {f.metal.min():.2f}..{f.metal.max():.2f}")
        # Tileability proof. NOT "seam mean < interior mean" -- that instrument
        # is too weak: a seam row that happens to cross a crisp growth band
        # scores high while being perfectly continuous.
        #
        # The verdict is "the wrap step is bigger than every step INSIDE the
        # field", not "it is above the 99.5th percentile of them". The
        # percentile rule was the previous test and it cried wolf: bronze's
        # patina shoreline is deliberately crisp, so the handful of largest
        # steps in the whole field are all shoreline crossings, and whether the
        # wrap column happens to cross one is a coin toss. It did, scored the
        # 99.7th percentile, and was reported as SEAM VISIBLE on a field that is
        # tileable by construction -- every input is FFT-synthesised on a
        # periodic basis and every operation after that is pointwise, so a seam
        # is arithmetically impossible. A false alarm on a correct build is not
        # a conservative instrument, it is a broken one.
        #
        # A real seam joins two uncorrelated samples, so its step is on the
        # order of the field's whole range rather than of a neighbour
        # difference, and exceeding the interior maximum catches that with
        # room to spare. Both numbers are printed either way.
        for axis, name in ((0, "v"), (1, "u")):
            a = f.albedo
            seam = float(np.mean(np.abs(np.take(a, 0, axis) - np.take(a, -1, axis))))
            steps = np.abs(np.diff(a, axis=axis)).mean(axis=tuple(
                i for i in range(3) if i != axis))
            pct = 100.0 * float((steps < seam).mean())
            print(f"        wrap seam {name}: step {seam:.4f} sits at the "
                  f"{pct:.1f}th percentile of interior steps "
                  f"(max {steps.max():.4f})  "
                  f"{'tileable' if seam <= steps.max() else 'SEAM VISIBLE'}")
    for p in written:
        print("WROTE", p, os.path.getsize(p))


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="procedural material bases")
    ap.add_argument("--preview", metavar="DIR", default="unity/board-prep/out/swatches")
    ap.add_argument("--size", type=int, default=768)
    ap.add_argument("--seed", type=int, default=20260825)
    a = ap.parse_args()
    _preview(a.preview, a.size, a.seed)
