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
(a + cel) * 0.5, and Unity's v = 0 is the bottom row). ALPHA CARRIES THE
SHAPE and nothing else; all colour comes from GhvrFireRamp's three stops
measured up the whole fire, so one bed sprite is white-blue at the seat of a
big fire and orange at the seat of a small one out of one texture.

============================================================================
ModBuild 148 — RGB IS NO LONGER WHITE. IT IS THE EROSION FIELD.

USER VERDICT, hardware, ModBuild 147, verbatim: "Es flackert überhaupt nicht
natürlich... Bitte höre auf es selber machen zu wollen und nutze feuer fx die
ich dir zur Verfügung gestellt habe."

The ModBuild 147 round of this file already found the answer and then wrote
down, in the paragraph immediately below, why it could not take it:

    "This is also the one place where their SHADER's technique (erosion by a
     noise texture) is borrowed — BAKED AS A SINGLE STATIC INSTANCE, because
     our fragment shader has no second sampler and EnvFlame.shader is another
     lane's file this round."

That is the whole of the fault he is reporting. Vefects' 18 prefabs have
startSpeed 0, no flipbook and no velocity module: NOTHING IN THEIR FIRE MOVES.
Every bit of the life in it is a noise field scrolling upward through the
alpha in their fragment shader. Bake that field ONCE and you get their
silhouette with none of their motion, which is exactly what shipped — torn
shapes that stretch and sway as rigid stencils. It is why "es flackert
überhaupt nicht natürlich" and it is why no amount of work on the four
turbulence BANDS fixed it: those bands move geometry, and a fire's life is not
in its geometry, it is in its mask dissolving.

So this round the field ships, animated. The constraint that blocked it (no
second sampler in a fragment that is already the heaviest overdraw in the
room) is answered by PUTTING THE NOISE IN THE THREE CHANNELS THAT WERE BEING
WASTED ON THE CONSTANT 1.0:

    R  the erosion field, ONE tile over the whole 512 image (period 512 px)
    G  the same field at FOUR tiles (period 128 px) — an octave-and-two above
       R, and because a scroll of dv in UV moves a 4x field through four times
       as many of its own periods, G also BOILS FOUR TIMES FASTER off the same
       scroll. Two octaves of a turbulence cascade, in time as well as in
       space, out of ONE extra tex2D.
    B  1.0. Deliberately left legible rather than packed with a third octave:
       a channel that is visibly constant is how a reader of the PNG can tell
       at a glance that RG are data and not art.

Both are made EXACTLY tileable (R is the source, which was measured seamless —
see the gate; G is a 4x4 tiling of an integer downsample, so its wrap is the
source's own) because EnvFlame samples them with a free-running scrolled UV
that wraps over the whole atlas, with the texture's wrapU/wrapV = Repeat.

RANK-EQUALISED to uniform [0,1], then INVERTED. T_VFX_Noise_07 as delivered
has mean 0.314 and std 0.198 — a dark-biased, long-tailed distribution — and
`saturate(a * W - n * E)` with such an n eats the mask in a few dense patches
and leaves the rest untouched. Under a uniform n the surviving coverage is
linear in the threshold, which is what makes _ErodeParams' numbers mean
something a tuner can reason about. The inversion is in erosion_field() and
is load-bearing rather than cosmetic; read the comment there.

IMPORTED AS DATA, not as colour: fire_atlas_alb.png.meta carries
sRGBTexture: 0 and alphaIsTransparency: 0 for this file's sake. Both are part
of the output; see the note where the RGB is written.

============================================================================
AND THE STATIC HOLES ARE GONE. `bed *= 0.62 + 0.38 * n` and
`t *= 0.70 + 0.30 * nn` carved the same field into the mask at bake time. With
the shader now carving it every frame, keeping them would be the field applied
twice — once frozen and once moving — and the frozen copy is the one that
makes a card legible as a card: a hole that never changes is a landmark, and a
landmark is how an eye finds an individual quad in a stack of thirty. The mask
ships FULLER than it was and the shader takes the difference back out; the
energy bookkeeping for that is the ARTE block at the bottom, which is computed
from the shipped erosion parameters rather than guessed.

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
    holes merge into one mass. This is their SHADER's technique (erosion by a
    noise texture) and as of ModBuild 148 it is borrowed the way they use it —
    ANIMATED, out of the RGB channels of this same image. See the block above.

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

# ---------------------------------------------------------------------------
# THE SHIPPED EROSION CONSTANTS. These are EnvFlame's _ErodeParams as
# EnvRoomBuilder.BuildFireCards writes them, restated here because this file
# has to SIMULATE the erosion in order to report what it does to each cell's
# drawn energy — and a simulation against different numbers would be a lie
# dressed as a measurement. If BuildFireCards changes them, change them here
# and re-run: the ARTE line this script prints is the one that goes into
# EnvRoomBuilder's ArtE table.
#
#   ERODE_W  the pre-boost. The mask is multiplied by this BEFORE the field is
#            subtracted. IT HAS TO STAY NEAR 1: at the 1.72 the first cut of
#            this used, saturate(mask * W) was 1.0 over the whole body of every
#            cell, so `1 - field` was drawn LITERALLY — the card became a
#            picture of the noise with a mask-shaped hole punched round it, the
#            silhouette stopped moving at all, and the only thing that lived
#            was a fine web. Keeping the product a GRADIENT is what lets the
#            field carve the OUTLINE, which is the part of a flame that has to
#            tear. Measured over six scroll offsets, the outline's frame-to-
#            frame change went from nil to visible at 1.08.
#   ERODE_E  how deep the field cuts, at the TIP of a card.
#   ERODE_B  ...and at its FOOT, as a fraction of ERODE_E. A flame is anchored
#            where it is fed: the base of a tongue and the seat of a bed do not
#            dissolve, they are the fuel. 0.25 is "a quarter as much down
#            there", and it is what stops the erosion sawing every card off at
#            the ankles once per second.
#   ERODE_G  the fine octave's share of the field (channel G against R).
#   ERODE_UV the field's tiling across ONE CARD, (u, v). 1.35 across and 1.00
#            up: a card sees a little over one period of the coarse octave and
#            five of the fine one, which puts the coarse structure at the size
#            of the whole tongue and the fine at 6-8 cm on a 35 cm card — the
#            3.6 cm / 8 Hz end of EnvFire.cginc's own frequency table.
# ModBuild 149: E, B and UV RETUNED, and the reason is the one the ModBuild 148
# comment above did not know. The field is sampled in CARD UV, and a card's UV is
# not square in METRES: a tongue card is 0.31-0.49 as wide as it is tall, so at
# (0.85, 0.72) one field cell measured 0.46 x 1.39 card-heights — 3.02:1 VERTICAL.
# The erosion was not carving flame out of the alpha, it was carving THREADS, and
# they are the "Faeden bis ganz weit nach oben" the user photographed in feuer3/4.
# The fix is to raise the V tiling, not to lower the U tiling: an earlier attempt
# squared the cells by dropping tileU to 0.34, which left under a third of a period
# ACROSS a card, so the erosion stopped breaking card EDGES and half a dozen cards
# became legible as straight-edged parallelograms — the slab artefact, bought back
# at the price of the threads. At (0.95, 1.35) a cell is 1.28:1 and both hold.
# ERODE_SCROLL is re-derived with it so a feature still rises at 1 m/s.
ERODE_W = 1.08
ERODE_E = 0.78
ERODE_B = 0.34
ERODE_G = 0.26
ERODE_UV = (0.95, 1.35)

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


def rank_uniform(a):
    """Rank-transform a field to exactly uniform [0,1], shape preserved.

    See the EROSION block in the module docstring for why: `saturate(mask * W
    - field * E)` only has a tunable meaning if the field's distribution is
    known, and T_VFX_Noise_07 as delivered is mean 0.314 / std 0.198 with a
    long dark tail. Under a uniform field the surviving coverage is linear in
    the threshold, so ERODE_W and ERODE_E above are numbers a tuner can reason
    about instead of two knobs that interact through a histogram.
    """
    flat = a.ravel()
    order = np.argsort(flat, kind="stable")
    out = np.empty(flat.shape, np.float32)
    out[order] = np.linspace(0.0, 1.0, flat.size, dtype=np.float32)
    return out.reshape(a.shape)


def erosion_field(noise):
    """The two octaves EnvFlame subtracts from the mask, as R and G.

    R is the source at one tile over the whole 512 image; G is an integer 4x4
    tiling of a 4x downsample, i.e. the same field an octave-and-two above it.
    BOTH ARE EXACTLY SEAMLESS at the image wrap — R because the source is (the
    gate at the bottom of this file measures it), G because a 4x4 tiling of
    anything is periodic by construction — which is load-bearing: EnvFlame
    samples this with a free-running scrolled UV that wraps over the whole
    atlas, and a seam would draw a moving straight line across every card in
    both rooms.
    """
    W = TILE * 2
    # INVERTED — 1 - rank, and this is not a sign convention, it is the whole
    # difference between two looks. EnvFlame computes mask*W - field*E, so the
    # field's DARK parts survive as bright. T_VFX_Noise_07 is a cellular field:
    # broad soft cells divided by THIN dark ridges. Taken as delivered those
    # ridges came through as a web of thin BRIGHT lines across every card —
    # scratches, in an effect whose entire complaint this round is "mehrere
    # sichtbare Striche". Inverted, the same ridges are thin DARK rifts
    # dividing bright sheets, which is what the inside of a flame looks like
    # and is also what splits one card's mass into separate tongues.
    r = 1.0 - rank_uniform(resize(noise, W, W))
    # The fine octave is built from a 4x DOWNSAMPLE tiled 4x4, so it is exactly
    # seamless (any 4x4 tiling is) and inherently soft. It is then boxed once
    # more: rank-equalising a cellular field sharpens its ridges into cracks,
    # and at four times the frequency those read as crazed glass rather than
    # as turbulence. One 3-tap box in each axis is enough to turn them back
    # into mottling and costs nothing at bake time.
    #
    # THE RANK TRANSFORM GOES BEFORE THE TILING, and the order is worth stating
    # because the obvious alternative is subtly wrong rather than obviously so.
    # Ranking the TILED array asks rank_uniform to order sixteen pixels that are
    # exact copies of one another; it breaks those ties by raster index and so
    # assigns them sixteen different values. They come out 6e-5 apart on a
    # 512x512 image — MEASURED, and small enough that the two orderings are
    # visually identical here — but that is an accident of the array size, not a
    # property of the method, and the tie spread grows with the tile count.
    # Ranking the tile and then copying it is exact at any size.
    fine = 1.0 - rank_uniform(resize(noise, W // 4, W // 4))
    g = np.tile(fine, (4, 4))
    for ax in (0, 1):
        g = (np.roll(g, 1, ax) + 2.0 * g + np.roll(g, -1, ax)) * 0.25
    return r, g


def simulate_erosion(cell, r, g):
    """What EnvFlame's fragment does to this cell, over a full scroll cycle.

    Returns the cell's mean alpha AFTER erosion, averaged over 16 evenly
    spaced scroll offsets — i.e. the drawn energy the fire will actually have,
    which is the number EnvRoomBuilder.ArtE has to be derived from. Doing this
    by measuring the MASK and hoping is how a sprite swap silently re-weights
    six rounds of tuning; that mistake is written up in EnvRoomBuilder's own
    ART COMPENSATION block and this function exists so it cannot repeat.

    The card's uv.y is the cell's v, and the erosion is height-weighted by
    (ERODE_B + (1 - ERODE_B) * v) exactly as the shader weights it.

    R AND G ARE ROLLED BY THE SAME NUMBER OF PIXELS, and that is not an
    oversight to be tidied: the shader takes ONE tap at one uv, so a scroll of
    dv shifts both channels by the same distance in uv. G's four-times-finer
    pattern simply travels four times as far in its OWN periods for it, which
    is the whole reason a single tap gives a cascade in time as well as in
    space.
    """
    W = r.shape[0]
    v, u = vgrid(), ugrid()
    hw = ERODE_B + (1.0 - ERODE_B) * v
    boosted = np.clip(cell * ERODE_W, 0.0, 1.0)
    # the field is sampled at the card's own uv times the shipped tiling, and
    # wraps — index arithmetic rather than a crop, so the tiling in this
    # measurement is the tiling that ships
    iu = np.rint(u * ERODE_UV[0] * W).astype(np.int32)
    iv = np.rint(v * ERODE_UV[1] * W).astype(np.int32)
    tot = 0.0
    for k in range(16):
        sh = int(round(k * W / 16.0))
        rr = r[(iv + sh) % W, iu % W]
        gg = g[(iv + sh) % W, iu % W]
        field = (1.0 - ERODE_G) * rr + ERODE_G * gg
        tot += float(np.clip(boosted - field * ERODE_E * hw, 0.0, 1.0).mean())
    return tot / 16.0


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
    # ...and it is fed over the bottom TWELFTH rather than the bottom
    # twenty-fifth. ModBuild 148: at 0.040 the mask went from nothing to its
    # brightest in three texels, so the bottom of every bed card was a bright
    # horizontal bar one pixel high running the full width of a quad up to
    # 2.6x as wide as it is tall — a straight line, in an effect whose whole
    # complaint is straight lines ("es sind mehrere sichtbare Striche auf den
    # assets drauf"). It is visible as such in the shipped atlas. The bed still
    # sits INTO what it burns; it now arrives there over 8 cm of a 35 cm card
    # instead of over 1 cm, and the erosion's base weight (ERODE_B) is what
    # keeps that band alive rather than a hard edge keeping it bright.
    bed *= ss(0.0, 0.085, v)                       # sits INTO what it burns
    bed *= 1.0 - ss(0.16, 0.34 + 0.14 * n, v)      # ragged top, no cut
    # NO STATIC HOLES ANY MORE — see the EROSION block in the docstring. The
    # shader carves this same field every frame now, and a hole that is baked
    # as well is a hole that never moves, i.e. a landmark by which the eye
    # finds one card in a stack of thirty.
    # ...and it goes out sideways before its own quad edge does. The widest
    # fade of the four: a bed card's quad is up to 2.6x as wide as it is tall,
    # so its vertical sides are the longest straight lines in the whole effect.
    bed = feather(bed, 0.19, 0.50)
    bed = knee(norm(bed), 0.95, 0.62)

    # ---- cells 1 and 2: THE TONGUES ----------------------------------
    def tongue(t):
        t = t * ss(0.0, 0.10, v)                   # fed at the base, not cut
        t = t * np.clip(1.0 - 0.80 * ss(0.42, 1.0, v), 0, 1)   # the tip dies
        # ...and NO STATIC HOLES: `t * (0.70 + 0.30 * nn)` used to freeze this
        # very field into the mask, and its `shift` argument (which existed
        # only to give the two tongue cells different frozen holes) went with
        # it. The field is the shader's now, and moving; what still separates
        # the two cells is that they are two different source masks at two
        # different widths, which is a difference in SILHOUETTE and survives
        # being eroded. See the EROSION block.
        # narrower than the bed's, because both masks already taper toward
        # their sides — this only has to kill the last few per cent that would
        # otherwise be cut off square at the cell wall
        t = feather(t, 0.30, 0.49)
        return knee(norm(t), 0.92, 0.70)

    t1 = tongue(resize(ground, TILE, TILE))
    # The second tongue is the OTHER mask, mirrored (a different lean) and
    # pulled in to 0.80 of the cell width: the two masks as delivered are
    # within 3 % of the same width, and two tongues of the same width are one
    # tongue drawn twice. Five fires that read as five copies is a fault this
    # feature has already shipped once.
    t2 = np.zeros((TILE, TILE), np.float32)
    nar = int(TILE * 0.80)
    x0 = (TILE - nar) // 2
    t2[:, x0:x0 + nar] = resize(flame, nar, TILE)[:, ::-1]
    t2 = tongue(t2)

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
    ef_r, ef_g = erosion_field(src["noise"])

    # ---- THE SEAM GATE ---------------------------------------------------
    # EnvFlame samples RG with a free-running scrolled uv that wraps over the
    # whole atlas, so a discontinuity at the image wrap would draw a moving
    # STRAIGHT LINE across every card in both rooms once per scroll period —
    # which is the exact fault class this whole round is about. Measured
    # against the field's own internal neighbour difference, not against a
    # typed tolerance: a seamless field's wrap edge is statistically an
    # interior edge.
    for nm, f in (("R", ef_r), ("G", ef_g)):
        for ax in (0, 1):
            wrap = float(np.abs(np.take(f, 0, ax) - np.take(f, -1, ax)).mean())
            inner = float(np.abs(np.take(f, 10, ax) - np.take(f, 11, ax)).mean())
            if wrap > inner * 2.0 + 1e-4:
                raise SystemExit(
                    f"erosion field {nm} is not seamless on axis {ax}: wrap "
                    f"difference {wrap:.5f} against an interior difference of "
                    f"{inner:.5f}. EnvFlame scrolls this field over the wrap, so "
                    "the seam would be a straight bright line travelling up every "
                    "card in both rooms. Re-derive it from a tileable source.")

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
        eroded = simulate_erosion(c, ef_r, ef_g)
        print(f"  {nm:8s} max={c.max():.3f} mean={c.mean():.4f} "
              f"cover>2%={100 * (c > 0.02).mean():5.1f}%  edge ring={edge:.4f}  "
              f"drawn mass {w:.3f} x {h:.3f}  eroded mean={eroded:.4f} "
              f"({eroded / max(c.mean(), 1e-6):.3f}x)")

    # ---- WHAT ENVROOMBUILDER'S ArtE HAS TO BE ---------------------------
    # Drawn ENERGY per card is (mean alpha) x (quad area), and both halves of
    # that have now moved: the mask ships without its baked holes and the
    # shader takes a moving field back out of it. ArtE exists to hold the
    # PRODUCT constant against exactly this kind of change (read its block in
    # EnvRoomBuilder.FireMesh — it was written for the last sprite swap), so
    # the factor each kind needs is the ratio of the energy the round being
    # replaced drew to the energy this one draws.
    #
    # The reference row is the SHIPPED ModBuild 147 atlas, whose per-cell mean
    # alphas are printed in EnvRoomBuilder's own table and restated here so
    # this script needs no second input to close the loop:
    ref = {"bed": 0.1238, "tongueA": 0.2402, "tongueB": 0.1940, "puff": 0.1512}
    ref_e = {"bed": 1.41, "tongue": 0.62, "puff": 1.15}      # what ships today
    now = {nm: simulate_erosion(c, ef_r, ef_g) for nm, c in zip(names, cells)}
    tongue_ref = 0.5 * (ref["tongueA"] + ref["tongueB"])
    tongue_now = 0.5 * (now["tongueA"] + now["tongueB"])
    arte = (ref_e["bed"] * ref["bed"] / max(now["bed"], 1e-6),
            ref_e["tongue"] * tongue_ref / max(tongue_now, 1e-6),
            ref_e["puff"] * ref["puff"] / max(now["puff"], 1e-6))
    print(f"  ArtE (bed, tongue, puff) = {arte[0]:.3f}, {arte[1]:.3f}, {arte[2]:.3f} "
          f"— copy into EnvRoomBuilder.FireMesh's ArtE table; it holds the drawn "
          f"energy of every fire in both rooms at the ModBuild 147 level through "
          f"the erosion swap.")

    cells = [border(c) for c in cells]

    W = TILE * 2
    alpha = np.zeros((W, W), np.float32)
    for i, c in enumerate(cells):
        cx, cy = i % 2, i // 2
        y0 = W - (cy + 1) * TILE      # cell row 0 is the BOTTOM of the PNG
        alpha[y0:y0 + TILE, cx * TILE:(cx + 1) * TILE] = c

    # RGB IS THE EROSION FIELD. B is a legible constant 1. NOT border()ed and
    # NOT cell-aligned: this is one continuous tiling field over the whole
    # image, which is what lets the shader scroll it forever with a single
    # Repeat-wrapped tap.
    #
    # THREE IMPORTER SETTINGS ARE PART OF THIS FILE'S OUTPUT and are set in
    # fire_atlas_alb.png.meta; any one wrong and the field is silently corrupt
    # rather than absent, which is the worst kind of regression:
    #   textureCompression: 0   UNCOMPRESSED, and this one cost a render. The
    #                       field is rank-equalised, i.e. it has FULL-RANGE
    #                       contrast at every scale by construction — which is
    #                       exactly the signal BC1/BC3 colour compression cannot
    #                       carry: four interpolated colours per 4x4 block. The
    #                       first bake with the field in RGB and the shipped
    #                       "Normal Quality" compression rendered the forest's
    #                       burning log as a staircase of hard 4x4 blocks with a
    #                       two-level dither in them, because the shader
    #                       subtracts this field from the mask and therefore
    #                       draws the compressor's error directly. 512x512 RGBA32
    #                       is 1.0 MB against 0.25 MB, in a 66 MB bundle, for the
    #                       one texture in it that is data rather than art.
    #   sRGBTexture: 0      the field is DATA. With it at 1 (which is what it
    #                       shipped as, when RGB was a constant white and the
    #                       setting could not matter) a linear-colour-space
    #                       project applies sRGB->linear on the way in and the
    #                       shader receives the field's 2.2-power — the
    #                       erosion would be ~40 % too weak through the
    #                       midtones and every constant above meaningless.
    #   alphaIsTransparency: 0
    #                       with it at 1 Unity DILATES rgb outward from the
    #                       opaque texels into the transparent ones, to stop
    #                       bilinear halos on a sprite. That is correct for a
    #                       sprite and catastrophic here: three quarters of
    #                       this image is transparent, and the dilation would
    #                       overwrite the field with smeared copies of itself
    #                       everywhere the mask happens to be zero.
    rgb = np.dstack([ef_r, ef_g, np.ones_like(ef_r)])
    rgb8 = (np.clip(rgb, 0, 1) * 255.0 + 0.5).astype(np.uint8)
    a8 = (np.clip(alpha, 0, 1) * 255.0 + 0.5).astype(np.uint8)
    Image.fromarray(np.dstack([rgb8, a8]), "RGBA").save(out_path)
    print("wrote", out_path)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__.strip().splitlines()[-3])
    main(sys.argv[1], sys.argv[2])
