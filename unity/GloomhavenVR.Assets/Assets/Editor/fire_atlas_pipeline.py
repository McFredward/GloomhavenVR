#!/usr/bin/env python3
"""GloomhavenVR — derive Imported/Textures/fire_atlas_alb.png from real fire art.

Editor-only (like cobweb_pipeline.py, polyhaven_pipeline.py and
star_catalogue.py): this file never enters the player build or the asset
bundle. Only the derived PNG ships, and the derived PNG is a re-authored
composite — none of the four source images survives in it as delivered.

============================================================================
WHY THIS FILE EXISTS AT ALL — the user's instruction, verbatim (ModBuild 147):

  "Ich hab dir in .debug/ressources zwei Feuer FX System abgelegt. Schau sie
   dir und importiere sie gegenfalls und ersetze damit dein eigens gebautes
   feuer - das sieht nicht wirklich gut aus. Kann es dann trotzdem mit dem
   wind reagieren?"

...and, earlier and more strongly:

  "Bitte benutze irgendein Feuer FX das schon vorgefertigt ist als es selber
   zu bauen ... Ich will lieber das du es mit solchen fx Dingen umsetzt statt
   selber zu machen."

He is overruling "build it yourself". This file is how far that instruction
can be carried, and the limit is not a preference — it is the render
pipeline. Both packages he supplied are for a pipeline this project does not
use:

  * "Fire 001" (N2Studio) ships the Nova Shader library. Its shader is tagged
    RenderPipeline = "UniversalPipeline", its passes are LightMode =
    "UniversalForward", and Particles.hlsl #includes
    Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl.
    URP-only. URP is not in this project and the game is BUILT-IN.
  * "Free Fire VFX - HDRP" (Vefects) — all five shaders are tagged
    RenderPipeline = "HDRenderPipeline". HDRP-only, same verdict.

Neither package's SHADERS can run here. What CAN be taken is the ART, and the
art is the half that was actually wrong: read the two atlases side by side
(the old one is still reproducible from EnvironmentsBuilder.MakeFireAtlas) and
the procedural cells are analytic cones with a sharp apex and a coarse fBm
"cottage cheese" fill, while the imported masks are real turbulent flame with
filaments, wisps and a torn organic outline. A cone with a sharp point IS the
candle silhouette this feature has been rejected for twice.

============================================================================
AND THE OTHER FINDING, which is why the mod's own shader is KEPT.

Every Vefects fire prefab was read (18 of them, all plain Shuriken, not one
VFX Graph). Their emitters:

    startSpeed 0, gravityModifier 0, VelocityModule all zero,
    ShapeModule type 2 (Hemisphere) radius 0.1,
    UVModule (TextureSheetAnimation) DISABLED on every emitter,
    rateOverTime 15 (Medium) / 25 (Big), startLifetime 0.6-1.0 s.

That is: ~26 STATIONARY quads on a 10 cm hemisphere, each living under a
second, growing 0.5x -> 1.0x and fading. Nothing moves. There is no flipbook.
The entire sense of flame comes from a noise-driven erosion in their HDRP
fragment shader — which is exactly the part that cannot come across.

The mod's fire is the same design (stationary crossed cards, all motion in the
shader, from a clock) with MORE structure, not less: a bed/tongue/puff
taxonomy, a three-stop temperature ramp, pieces that detach and cool, and four
turbulence bands assigned to the structure sizes they belong to. So the port
is: keep the emission design, which already matches; take the art, which was
the deficiency; keep the shader, which is the only thing that knows about the
element channel, the fire wash, the shelf riders and the shared clock.

============================================================================
SOURCES (see Bundle/Environments/License.md for the full licence trail).
Both are FREE Unity Asset Store packages under the standard Asset Store EULA,
whose section 2.2.1(b) licenses distribution "as incorporated and embedded in
that Licensed Product". Nothing here is redistributed as delivered.

  A. "Free Fire VFX - HDRP", Vefects, Asset Store id 239742 (category 116
     VFX; customLicense false; not a Restricted Asset). Files used:
       Textures/T_VFX_Fire_Ground_Mask_01.tga   512x512, 8-bit greyscale
       Textures/T_VFX_Fire_Mask_01.tga          256x256, 8-bit greyscale
       Textures/T_VFX_Noise_07.tga              256x256, 8-bit greyscale
  B. "Fire 001", N2Studio. Files used:
       Textures/FireSeq1.png                    1024x1024 RGB, a 2x2 sheet of
                                                four turbulence frames

Both arrive as .unitypackage (a gzipped tar of per-GUID directories, each
holding `asset`, `asset.meta` and `pathname`), which is what this script
reads, so the derivation runs from exactly the files the user supplied.

============================================================================
THE OUTPUT is a 512x512 RGBA PNG laid out as EnvFlame.shader's 2x2 atlas:

    cell 0  BED      the seat: wide, low, ragged-topped, sits ON the object
    cell 1  TONGUE A rises out of the bed
    cell 2  TONGUE B a second, narrower silhouette
    cell 3  PUFF     a piece that has detached and is cooling

Cell c is at (c % 2, c // 2) with row 0 the BOTTOM half of the texture (the
shader computes cel = float2(fmod(ci,2), floor(ci/2)) and samples
(a + cel) * 0.5, and Unity's v = 0 is the bottom row). RGB IS WHITE
THROUGHOUT: all colour comes from GhvrFireRamp's three stops measured up the
whole fire, so one bed sprite is white-blue at the seat of a big fire and
orange at the seat of a small one out of one texture. Alpha carries the shape.

WHY EACH STEP EXISTS
  * NORMALISE on the source's own peak. Two of the four sources top out below
    1.0 (FireSeq1's cells peak at 0.78-0.87) and one is a crop.
  * THE KNEE. As delivered, T_VFX_Fire_Mask_01 is 6.91 % saturated and
    T_VFX_Fire_Ground_Mask_01 5.33 % — measured, not estimated. This pass is
    ADDITIVE and a fire is thirty overlapping cards: a region of alpha 1
    clips to white over an area whose boundary is the CARD EDGE, and the
    third bake of the procedural atlas shipped exactly that (straight white
    slabs and a hard-edged chevron in the crate fire). Every cell is put
    through a soft exponential knee with a ceiling of 0.90-0.95, and the
    check at the bottom of this file FAILS the build if any cell comes out
    with a saturated region again.
  * NO STRAIGHT EDGES ANYWHERE. Same reason. The bed's ragged top is the
    source's own turbulence multiplied into a smoothstep whose edge position
    is itself modulated by the noise map — the first cut of it pasted a band
    and left a hard horizontal line across the top of every bed card.
  * A SIDEWAYS FEATHER, on every cell, to nothing, well inside the cell. This
    is the step the first bake left out and the render caught: see feather().
    The extent numbers cannot see it — the drawn extents were within 5 % of
    the atlas being replaced while the cellar's burning spill rendered as a
    scatter of hard-cornered plates, because what makes a card show its own
    quad boundary is not how big its mass is but whether that mass has
    reached zero by the time it gets there.
  * A HARD ZERO BORDER of 3.5 % on every cell. EnvFlame insets its lookup by
    GHVR_FIRE_INSET (0.013), which is ~3 texels at this size and is enough
    for the base level but not for mip 3, where one texel is eight. The
    border makes cross-cell bleed impossible at every level instead of
    unlikely at the top one.
  * HOLES, from Vefects' own noise map rather than from a new fBm. Two
    overlapping cards with solid interiors show two outlines; two cards with
    holes merge into one mass. This is also the one place where their
    SHADER's technique (erosion by a noise texture) is borrowed — baked as a
    single static instance, because our fragment shader has no second
    sampler and EnvFlame.shader is another lane's file this round.

Run:  python3 fire_atlas_pipeline.py <dir with the two .unitypackage files> <out.png>
Needs: python3, numpy, Pillow.
"""
import io
import os
import sys
import tarfile

import numpy as np
from PIL import Image

TILE = 256                      # per cell; 512x512 atlas, as MakeFireAtlas was
BORDER = 0.035                  # hard-zero margin, in cell fractions

PKG_VEFECTS = "Free Fire VFX - HDRP.unitypackage"
PKG_N2 = "Fire 001.unitypackage"

WANT = {
    "Assets/Vefects/Free Fire HDRP/Textures/T_VFX_Fire_Ground_Mask_01.tga": "ground",
    "Assets/Vefects/Free Fire HDRP/Textures/T_VFX_Fire_Mask_01.tga": "flame",
    "Assets/Vefects/Free Fire HDRP/Textures/T_VFX_Noise_07.tga": "noise",
    "Assets/N2Studio/Textures/FireSeq1.png": "seq",
}


# ---------------------------------------------------------------- unpacking
def read_package(path):
    """Pull the wanted assets out of a .unitypackage without unpacking it all.

    A .unitypackage is a gzipped tar of one directory per asset GUID; the
    directory holds the payload as `asset` and its project path as `pathname`.
    Extracting the whole thing would drop URP/HDRP shaders, ShaderGraphs,
    editor scripts and thirty advertising textures into the project, none of
    which would compile and none of which is used.
    """
    found = {}
    with tarfile.open(path, "r:gz") as t:
        members = {m.name: m for m in t.getmembers()}
        for name, m in members.items():
            if not name.endswith("/pathname"):
                continue
            f = t.extractfile(m)
            if f is None:
                continue
            p = f.read().decode("utf-8").strip().split("\n")[0]
            if p not in WANT:
                continue
            am = members.get(name[: -len("pathname")] + "asset")
            if am is None:
                raise SystemExit(f"{p} has a pathname but no asset payload")
            data = t.extractfile(am).read()
            found[WANT[p]] = np.asarray(
                Image.open(io.BytesIO(data)).convert("L")
            ).astype(np.float32) / 255.0
    return found


# ------------------------------------------------------------------ helpers
def resize(a, w, h):
    """Lanczos at 16 bit — these are single-channel masks and an 8-bit
    intermediate quantises the long soft tails that ARE the improvement."""
    im = Image.fromarray((np.clip(a, 0, 1) * 65535.0 + 0.5).astype(np.uint16))
    return np.asarray(im.resize((w, h), Image.LANCZOS)).astype(np.float32) / 65535.0


def norm(a):
    return np.clip(a / max(a.max(), 1e-6), 0.0, 1.0)


def ss(e0, e1, x):
    """HLSL's smoothstep(edge0, edge1, x) — NOT Unity's Mathf.SmoothStep,
    which is a lerp with a smoothed t and has the same three arguments in the
    same order. That confusion once shipped a fire atlas whose bed had an
    alpha of 3/255 (see MakeFireAtlas's own note); it is worth restating."""
    t = np.clip((x - np.asarray(e0)) / np.maximum(np.asarray(e1) - np.asarray(e0), 1e-6), 0, 1)
    return t * t * (3.0 - 2.0 * t)


def vgrid():
    """v in [0,1] with 0 at the BOTTOM row — Unity's texture v, and the card's
    own v, which EnvFlame uses as height up the card."""
    return np.repeat(np.linspace(1.0, 0.0, TILE)[:, None], TILE, axis=1)


def ugrid():
    return np.repeat(np.linspace(0.0, 1.0, TILE)[None, :], TILE, axis=0)


def knee(a, ceil, k):
    """Soft-compress the top end so no region of any cell sits at alpha 1.

    a' = ceil * (1 - e^(-a/k)) / (1 - e^(-1/k)): monotone, a'(0) = 0,
    a'(1) = ceil, and the derivative falls with a, so the bright interior is
    flattened toward the ceiling while the soft edges keep their full range.
    """
    return ceil * (1.0 - np.exp(-a / k)) / (1.0 - np.exp(-1.0 / k))


def feather(a, e0, e1):
    """Fade the mass out sideways, to nothing, well inside the cell.

    WHY THIS EXISTS, and it is the one thing the RMS extent check below cannot
    see. The first bake of this atlas stretched each source mask across its
    whole cell — the drawn extents came out within 5 % of the atlas being
    replaced, so the measurement said "same size" — and the render showed the
    cellar's burning spill as a scatter of hard-cornered parallelograms and
    pale flat plates lying on the flagstones. Individual CARDS were legible.

    The reason is edge PROFILE, not extent. These masks were authored for
    Vefects' own use, which is ONE quad per particle at 0.35-3 m; nothing in
    that arrangement cares that the mask reaches the edge of its image. Here
    thirty of them overlap inside 30 cm, and any card whose sprite does not
    reach zero before its own quad boundary draws that boundary. The
    procedural cells this replaces had exactly such a fade and said why
    ("a fade over 24 % of the width and 12 % of the height costs nothing and
    there is no card edge left to see"); losing it was the regression.
    """
    u = ugrid()
    return a * (1.0 - ss(e0, e1, np.abs(u - 0.5)))


def border(a):
    """Zero margin: nothing in a cell may be reachable from a neighbour's tap
    at ANY mip level."""
    b = max(int(TILE * BORDER), 2)
    idx = np.arange(TILE)
    ramp = ss(0.0, 1.0, np.clip(np.minimum(idx, TILE - 1 - idx) / (2.0 * b), 0, 1))
    m = ramp[:, None] * ramp[None, :]
    m[: b // 2, :] = 0.0
    m[-(b // 2):, :] = 0.0
    m[:, : b // 2] = 0.0
    m[:, -(b // 2):] = 0.0
    return a * m


# -------------------------------------------------------------------- cells
def build(src):
    ground = norm(src["ground"])         # 512, a real flame column
    flame = norm(src["flame"])           # 256, a fatter rounded mass
    noise = src["noise"]
    seq = src["seq"]                     # 1024, 2x2 turbulence sheet

    n = resize(noise, TILE, TILE)
    n = (n - n.min()) / max(n.max() - n.min(), 1e-6)
    v, u = vgrid(), ugrid()

    # ---- cell 0: THE BED ---------------------------------------------
    # The lower 64 % of the GROUND-fire mask — the part of a real flame that
    # lies ON something — stretched across the cell and squeezed into its
    # bottom band, because the bed card's quad is 1.7-2.6x as wide as it is
    # tall (FireMesh) and a sprite that filled the square cell would render
    # as a slab twice as tall as the bed this room was tuned with.
    bed = np.zeros((TILE, TILE), np.float32)
    band = int(TILE * 0.58)
    bed[TILE - band:, :] = resize(
        ground[int(512 * 0.36):, int(512 * 0.13):int(512 * 0.87)], TILE, band)
    bed *= ss(0.0, 0.040, v)                       # sits INTO what it burns
    bed *= 1.0 - ss(0.16, 0.34 + 0.14 * n, v)      # ragged top, no cut
    bed *= 0.62 + 0.38 * n                         # holes
    # ...and it goes out sideways before its own quad edge does. The widest
    # fade of the four: a bed card's quad is up to 2.6x as wide as it is tall,
    # so its vertical sides are the longest straight lines in the whole effect.
    bed = feather(bed, 0.19, 0.50)
    bed = knee(norm(bed), 0.95, 0.62)

    # ---- cells 1 and 2: THE TONGUES ----------------------------------
    def tongue(t, shift):
        t = t * ss(0.0, 0.10, v)                   # fed at the base, not cut
        t = t * np.clip(1.0 - 0.80 * ss(0.42, 1.0, v), 0, 1)   # the tip dies
        nn = np.roll(np.roll(n, shift, 0), shift * 3, 1)
        t = t * (0.70 + 0.30 * nn)
        # narrower than the bed's, because both masks already taper toward
        # their sides — this only has to kill the last few per cent that would
        # otherwise be cut off square at the cell wall
        t = feather(t, 0.30, 0.49)
        return knee(norm(t), 0.92, 0.70)

    t1 = tongue(resize(ground, TILE, TILE), 0)
    # The second tongue is the OTHER mask, mirrored (a different lean) and
    # pulled in to 0.80 of the cell width: the two masks as delivered are
    # within 3 % of the same width, and two tongues of the same width are one
    # tongue drawn twice. Five fires that read as five copies is a fault this
    # feature has already shipped once.
    t2 = np.zeros((TILE, TILE), np.float32)
    nar = int(TILE * 0.80)
    x0 = (TILE - nar) // 2
    t2[:, x0:x0 + nar] = resize(flame, nar, TILE)[:, ::-1]
    t2 = tongue(t2, 97)

    # ---- cell 3: THE PUFF --------------------------------------------
    # N2Studio's sheet, top-right frame: a turbulent mass with a bright core
    # and filaments running off it, which is what a piece that has left a fire
    # looks like. Vefects' T_VFX_Smoke_01 was the alternative and was rejected
    # here — it is a SMOKE puff with no core, and this cell is also what the
    # Fire+Light smoke and the Fire+Ice steam are drawn with (EnvFlame lerps
    # it to _SmokeCol / _SteamCol), so it has to work hot as well as cold.
    puff = norm(resize(seq[0:512, 512:1024], TILE, TILE))
    r = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2) / 0.5
    puff *= 1.0 - ss(0.55, 0.96, r)                # no edge of its own
    puff = knee(norm(puff), 0.90, 0.80)

    return [bed, t1, t2, puff]


# --------------------------------------------------------------------- gate
def extent(a):
    """Alpha-weighted RMS extent x2 — 'how big is the drawn mass', in cell
    units. This is the number EnvRoomBuilder.FireMesh's ArtScale table is
    derived from; if this file's crops change, that table has to be re-derived
    or the fire silently changes size."""
    s = a.sum()
    if s < 1e-6:
        return 0.0, 0.0
    u, v = ugrid(), vgrid()
    cu, cv = (a * u).sum() / s, (a * v).sum() / s
    return (2.0 * np.sqrt((a * (u - cu) ** 2).sum() / s),
            2.0 * np.sqrt((a * (v - cv) ** 2).sum() / s))


def main(src_dir, out_path):
    src = {}
    for pkg in (PKG_VEFECTS, PKG_N2):
        p = os.path.join(src_dir, pkg)
        if not os.path.exists(p):
            raise SystemExit(f"missing source package: {p}")
        src.update(read_package(p))
    missing = set(WANT.values()) - set(src)
    if missing:
        raise SystemExit(f"packages did not contain: {sorted(missing)}")

    cells = build(src)
    names = ("bed", "tongueA", "tongueB", "puff")

    for nm, c in zip(names, cells):
        sat = float((c > 0.97).mean())
        if sat > 0.0:
            raise SystemExit(
                f"cell '{nm}' has {sat * 100:.3f} % of its area at alpha > 0.97. "
                "This pass is ADDITIVE: a saturated region clips to white over an "
                "area bounded by the CARD EDGE, which is the straight-white-slab "
                "artefact the atlas was rewritten to remove. Lower the knee ceiling.")
        # ...and the OTHER gate, which is the one the extent numbers cannot see.
        # A cell whose mass is still alive at its own wall draws that wall: the
        # first bake put the cellar's burning spill on the flagstones as a
        # scatter of hard-cornered parallelograms and pale flat plates, and the
        # extents were within 5 % of the atlas it replaced. What separates the
        # two is how much alpha survives in the outer ring. The procedural
        # cells kept ~0.2 % there; anything above 1 % is a visible card edge.
        ring = max(int(TILE * 0.08), 4)
        m = np.zeros((TILE, TILE), bool)
        m[:ring, :] = m[-ring:, :] = m[:, :ring] = m[:, -ring:] = True
        edge = float(c[m].mean())
        if edge > 0.010:
            raise SystemExit(
                f"cell '{nm}' still carries {edge:.4f} mean alpha in its outer 8 % ring. "
                "Every card would draw its own quad boundary as a straight edge — the "
                "slab artefact this pipeline's feather() exists to prevent. Widen the "
                "feather.")
        w, h = extent(c)
        print(f"  {nm:8s} max={c.max():.3f} mean={c.mean():.4f} "
              f"cover>2%={100 * (c > 0.02).mean():5.1f}%  edge ring={edge:.4f}  "
              f"drawn mass {w:.3f} x {h:.3f}")

    cells = [border(c) for c in cells]

    W = TILE * 2
    alpha = np.zeros((W, W), np.float32)
    for i, c in enumerate(cells):
        cx, cy = i % 2, i // 2
        y0 = W - (cy + 1) * TILE      # cell row 0 is the BOTTOM of the PNG
        alpha[y0:y0 + TILE, cx * TILE:(cx + 1) * TILE] = c

    rgb = np.full((W, W, 3), 255, np.uint8)
    a8 = (np.clip(alpha, 0, 1) * 255.0 + 0.5).astype(np.uint8)
    Image.fromarray(np.dstack([rgb, a8]), "RGBA").save(out_path)
    print("wrote", out_path)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__.strip().splitlines()[-3])
    main(sys.argv[1], sys.argv[2])
