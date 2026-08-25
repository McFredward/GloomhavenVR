#!/usr/bin/env python3
"""ROUND 5 -- DOES THE CAP BELONG TO ITS BOARD?  The colour instrument, and the solve behind it.

    python3 cap_belong.py --selfcheck        validate the instrument before trusting it
    python3 cap_belong.py --solve            solve the per-board field targets and IdleColors
    python3 cap_belong.py --report           the dE table against what is ACTUALLY SHIPPED
    python3 cap_belong.py --sheet <out.png>  the same table as a picture

WHY A NEW INSTRUMENT, AND WHY REG MUST NOT BE QUOTED FOR THIS ROUND
-------------------------------------------------------------------
`plate_forensics.REG` -- mean luminance against distance-to-border -- has a null control and a
known-positive control and it is a good instrument for "does this picture have an inside/outside
order".  **It is blind to hue.**  It moved 3.7 -> 28.3 on oak in round 3 and the round was
rejected anyway, and the rejection named the one thing REG cannot see:

    "Ich mag die Textur gar nicht.  Sie passt ueberhaupt nicht zu dem jeweiligen Board."
    -- user, 2026-08-25

A cap that is the wrong colour for its board scores exactly like a cap that is the right one.
Round 4 fixed the generation side and the option sheet is good; what shipped from it is not.
This file measures the term that decides that, and it is the first round in which a per-board
TARGET exists at all -- before round 4 nobody had an image of what this board's button should
look like, so "does it match" had no referent and the previous solve invented one (see
THE AUTHORED PALETTE below, which is the round's main finding).

THE CHAIN, and every term read off the shipped source (identical to `cap_deltae`, imported)
---------------------------------------------------------------------------------------------
    rendered_face_sRGB = modulator x SeatedCapColor(IdleColor_board x BoardCapTint) x shade

`cap_deltae` argues each term at length and validates the whole chain against the ModBuild 281
record to 0.04 dE; it is imported here rather than re-derived.  The one thing this file does
differently is that it reads the MODULATOR OFF THE SHIPPED PNG rather than recomputing a plate
-- measure the picture, not the state.

THE COLOUR DIFFERENCE IS CIEDE2000, AND THE ROUND REPORTS TWO FORMS OF IT
-------------------------------------------------------------------------
CIE76 (a plain euclidean distance in CIELAB, which is what `cap_deltae.de_matrix` uses and what
the ModBuild 281/286 record's numbers are in) badly over-weights chroma differences at low
lightness, and every colour in this problem is at L* ~ 21.  So the headline is **CIEDE2000**,
implemented below and checked against the Sharma et al. reference pairs in `--selfcheck`.  The
CIE76 form is printed beside it so the older record stays comparable.

**AND THE ROUND REPORTS dE AT MATCHED LIGHTNESS AS WELL, WHICH IS THE HONEST FORM HERE.**  The
option image is a studio product shot at L* 36-42; the cap renders on a dim board at L* 20-24.
Those two lightnesses are set by two different scenes and comparing them measures the exposure,
not the material.  `de_hc` puts both colours at their mean L* and compares what is left -- hue
and chroma -- which is what "passt zum Board" is about.  Both are printed; neither is hidden.

THE TARGET: THE OPTION THE CAP WAS BUILT FROM, RE-EXPOSED
----------------------------------------------------------
`cap_options.CHOSEN` names two options per board and `out/keycap4_material_<style>.png` is the
flat face material of the square one.  The cap cannot be that colour -- it is half as bright,
because it is on a board and not in a studio -- so the target is that material AT THE CAP'S OWN
LUMINANCE, and the re-exposure is done in LINEAR light (`_expose`), because that is what a
darker exposure of one material physically is.  Scaling the sRGB numbers instead is a fade
toward black through the transfer curve and it desaturates: measured on these three, a
gamma-space fade loses oak 3.7 C* and bronze 2.3 C* against a true exposure change.  That
difference is small but it is free to get right and it is the difference between a target that
is a claim about the material and one that is an artefact of the encoding.

WHAT THE SOLVE MAY AND MAY NOT DO
----------------------------------
  (a) land the rendered idle face ON the re-exposed option -- this is exact, not optimised;
  (b) hold each board's rendered idle LUMINANCE at what that board's cap renders today, so this
      round changes colour and nothing else;
  (c) keep `IdleColor` inside [seatFloor / tint, 1] so `SeatedCapColor` never silently lifts a
      channel -- a floor that engages on one channel is a hue shift nobody solved for;
  (d) keep the DISABLED cap at or above the seat guard's own promise, measured ON THE PRODUCT
      (see WHAT THE INSTRUMENTS CANNOT SEE in .planning/BOARD-BUTTON-OVERHAUL.md);
  (e) subject to (a)-(d), maximise the rendered field's CONTRAST, which is the "einheitlich"
      half of the complaint and the only free parameter left.

THE AUTHORED PALETTE -- THE FINDING THIS ROUND EXISTS TO RECORD
---------------------------------------------------------------
`cap_deltae.PALETTE` is a hue+chroma window per board, and the ModBuild 286 idle colours were
solved to sit inside it:

    oak    -> "parchment / pale honey"  h 78-92   C* 14-20
    steel  -> "pewter, cool and quiet"  h 220-285 C*  7-14
    bronze -> "brass / warm gold"       h 66-80   C* 24-32

Those three windows were authored BEFORE any picture of what a button on that board should
look like existed, and the boards themselves sit at h 65.6 / 58.5 / 91.8.  So the bronze window
excludes the bronze board's own hue by 12-26 degrees and the steel window excludes the steel
board's by 161 degrees.  **The solver hit its target exactly and the target was wrong**: the
shipped bronze cap lands at h 65.6 C* 27.3, inside its window, on a board at h 91.8.  A solver
that maximises separation inside an unvalidated palette will produce three caps that are
maximally different from each other and belong to nothing, and that is what is on the board
today.  It is recorded here rather than in a commit message because the next round will be
tempted to re-run that solve.
"""
import argparse
import itertools
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

import cap_atlas as C            # noqa: E402
import cap_deltae as D          # noqa: E402  -- the shipped chain, validated there
import cap_object as O          # noqa: E402

REPO = os.path.dirname(os.path.dirname(PREP))
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
GEN = os.path.join(HERE, "out")
STYLES = ("oak", "steel", "bronze")
REC709 = np.array([0.2126, 0.7152, 0.0722])

# Cards/PlayTray.7.Nested.cs -- the other three cap states, which share the modulator.
CONFIRMED = np.array([0.68, 0.52, 0.24])
DISABLED = np.array([0.21, 0.16, 0.11])


def say(*a):
    print(*a, flush=True)


# =========================================================================================
# CIEDE2000
# =========================================================================================
def de2000(lab1, lab2):
    """CIEDE2000.  Checked against Sharma/Wu/Dalal's published pairs in `--selfcheck`."""
    L1, a1, b1 = (float(v) for v in lab1)
    L2, a2, b2 = (float(v) for v in lab2)
    C1, C2 = np.hypot(a1, b1), np.hypot(a2, b2)
    Cb = (C1 + C2) / 2.0
    G = 0.5 * (1.0 - np.sqrt(Cb ** 7 / (Cb ** 7 + 25.0 ** 7))) if Cb > 0 else 0.5
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1p = np.degrees(np.arctan2(b1, a1p)) % 360.0 if (a1p or b1) else 0.0
    h2p = np.degrees(np.arctan2(b2, a2p)) % 360.0 if (a2p or b2) else 0.0
    dLp, dCp = L2 - L1, C2p - C1p
    if C1p * C2p == 0:
        dhp = 0.0
    elif abs(h2p - h1p) <= 180:
        dhp = h2p - h1p
    elif h2p - h1p > 180:
        dhp = h2p - h1p - 360.0
    else:
        dhp = h2p - h1p + 360.0
    dHp = 2.0 * np.sqrt(C1p * C2p) * np.sin(np.radians(dhp) / 2.0)
    Lbp, Cbp = (L1 + L2) / 2.0, (C1p + C2p) / 2.0
    if C1p * C2p == 0:
        hbp = h1p + h2p
    elif abs(h1p - h2p) <= 180:
        hbp = (h1p + h2p) / 2.0
    elif h1p + h2p < 360:
        hbp = (h1p + h2p + 360.0) / 2.0
    else:
        hbp = (h1p + h2p - 360.0) / 2.0
    T = (1 - 0.17 * np.cos(np.radians(hbp - 30)) + 0.24 * np.cos(np.radians(2 * hbp))
         + 0.32 * np.cos(np.radians(3 * hbp + 6)) - 0.20 * np.cos(np.radians(4 * hbp - 63)))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / np.sqrt(20 + (Lbp - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    Rt = -np.sin(np.radians(2 * 30 * np.exp(-((hbp - 275) / 25) ** 2))) * \
        2 * np.sqrt(Cbp ** 7 / (Cbp ** 7 + 25.0 ** 7))
    return float(np.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2
                         + Rt * (dCp / Sc) * (dHp / Sh)))


def de(a, b):
    return de2000(D.lab(a), D.lab(b))


def de76(a, b):
    return float(np.linalg.norm(D.lab(a) - D.lab(b)))


def de_hc(a, b):
    """dE2000 with both colours put at their MEAN L* -- hue and chroma only.

    The cap and the option are lit by two different scenes; their LEVELS are not comparable and
    their CASTS are.  This is the form the user's complaint is about."""
    la, lb = D.lab(a).copy(), D.lab(b).copy()
    m = (la[0] + lb[0]) / 2.0
    la[0] = lb[0] = m
    return de2000(la, lb)


def hue_err(a, b):
    """Signed CIELAB hue-angle difference of `a` from `b`, in degrees, wrapped to +-180."""
    return (D.lch(a)[2] - D.lch(b)[2] + 180.0) % 360.0 - 180.0


def lum(rgb):
    return float(np.asarray(rgb, dtype=np.float64) @ REC709)


def _encode(lin):
    lin = np.clip(np.asarray(lin, dtype=np.float64), 0.0, 1.0)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * lin ** (1 / 2.4) - 0.055)


def _expose(srgb, want_lum):
    """`srgb` re-exposed IN LINEAR LIGHT until its Rec.709 luminance (on the sRGB value) is
    `want_lum`.  A darker exposure of one material is a linear scale; scaling the encoded
    numbers instead is a fade toward black and it desaturates."""
    lin = D.srgb_to_linear(srgb)
    k = 1.0
    for _ in range(80):
        got = lum(_encode(lin * k))
        if abs(got - want_lum) < 1e-7:
            break
        k *= (want_lum / max(got, 1e-9)) ** 1.6
    return _encode(lin * k), float(k)


# =========================================================================================
# THE THREE POPULATIONS: what ships, what it should look like, and what it sits on
# =========================================================================================
def shipped_field(style, role=1, bundle=None):
    """The FIELD of one role cell of the SHIPPED atlas PNG -- the modulator, as built.

    Read off the file rather than recomputed: this project has lost rounds to a diagnostic
    that measured the intended state instead of the shipped picture.  The field is d > 0.135
    of the cell's short side, because submesh [0] (the cap FACE) samples only the field --
    `CapCellMath` and the README own that arithmetic."""
    path = os.path.join(bundle or BUNDLE, f"Keycap{style.capitalize()}_albedo.png")
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    h, w = im.shape[:2]
    cols, rows = C.GRID
    cw, ch = w // cols, h // rows
    col, row = role % cols, rows - 1 - role // cols       # rows count from the BOTTOM
    cell = im[row * ch:(row + 1) * ch, col * cw:(col + 1) * cw]
    u = (np.arange(cw) + 0.5) / cw
    duv = np.minimum(u, 1.0 - u)
    field = np.minimum(duv[None, :], duv[:, None]) > 0.135
    px = cell[field]
    q = (px * 255.0 + 0.5).astype(np.uint8)
    return (np.median(px, axis=0), px, float((q >= 255).any(axis=1).mean()))


def option_material(style):
    """The flat face material of this board's CHOSEN square option (`cap_options.CHOSEN`)."""
    path = os.path.join(GEN, f"keycap4_material_{style}.png")
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    return np.median(a.reshape(-1, 3), axis=0)


def board_material(style, boards_dir):
    """The board's own surface, from the flat render the generator was shown."""
    path = os.path.join(boards_dir, f"board_{style}_flat.png")
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    f = a.reshape(-1, 3)
    return np.median(f[f.max(axis=1) > 0.05], axis=0)


# =========================================================================================
# THE SEAT GUARD, MEASURED ON THE PRODUCT
# =========================================================================================
def well_lum():
    """The mod-drawn WELL's rendered luminance: KeycapGrain x CapWellColor x shade."""
    return lum(D.grain_mean() * D.CAP_WELL_COLOR * D.SHADE)


def guard_bar():
    """What `SeatedCapColor` PROMISES: a cap face at >= CapSeatContrast x the well.

    It applies that floor to `_Color` and the modulator is a second factor it never sees, so
    the promise is only kept while the modulator is near-white.  This is the bar the promise
    means, measured where it is actually decided -- on the rendered product."""
    return D.CAP_SEAT_CONTRAST * well_lum()


def state_lum(plate_mean, state, tint=D.BOARD_CAP_TINT):
    return lum(np.clip(plate_mean * np.maximum(state * tint, D.SEAT_FLOOR) * D.SHADE, 0.0, 1.0))


# =========================================================================================
# THE SOLVE
# =========================================================================================
def _build_at(style, target, cell_px, tmp, sheet_masks, plates_dir, motif_root):
    """BUILD the atlas at `target` and measure the FIELD OF THE BUILT PNG.

    THE FIRST VERSION OF THIS SOLVE MEASURED THE PRE-CARVE CELL AND IT WAS WRONG BY AN ORDER OF
    MAGNITUDE. It predicted oak's field would clip 3.9 % of its texels; the atlas that came out
    of the same target clipped 40.5 %. Two terms sit between the normalisation and the file --
    `field_jitter`, which MULTIPLIES the field by up to 1.055 and pushes texels already at 0.97
    over the top, and the 8-bit quantisation -- and neither exists in the array the old solve
    looked at. The number that decides the round has to be read off the artifact that ships.
    """
    C.FIELD_TARGET_LUM[style] = float(target)
    C.build_style(style, cell_px, sheet_masks, motif_root, plates_dir, tmp, report=False)
    med, px, clip = shipped_field(style, bundle=tmp)
    return med, px, clip


def solve(n=256, margin=1.02, boards_dir=None, sheet=None, plates_dir=None, motif_root=None,
          grid=None, tmp=None):
    """Per board: the FIELD target, the IdleColor it implies, and the numbers behind both.

    Every candidate is BUILT and then measured off the PNG -- see `_build_at`."""
    import tempfile
    bar = guard_bar() * margin
    sheet = sheet or os.path.join(PREP, "out", "keycap_symbols_sheet.png")
    plates_dir = plates_dir or os.path.join(PREP, "out")
    motif_root = motif_root or os.path.join(PREP, "out")
    sheet_masks, _ = C.load_sheet_masks(sheet)
    grid = grid or [0.50, 0.56, 0.62, 0.68, 0.74, 0.80, 0.86, 0.92]
    keep = dict(C.FIELD_TARGET_LUM)
    tmp = tmp or tempfile.mkdtemp(prefix="capbelong_")
    out = {}
    try:
        for style in STYLES:
            opt = option_material(style)
            before_med, before_px, before_clip = shipped_field(style)
            face_now = D.face_colour(SHIPPED_IDLE[style])
            ren_now = np.clip(before_med * face_now * D.SHADE, 0.0, 1.0)
            want = lum(ren_now)                               # constraint (b)
            tgt, expo = _expose(opt, want)                     # the re-exposed option
            best = None
            for t in grid:
                med, px, clip = _build_at(style, float(t), n, tmp, sheet_masks,
                                          plates_dir, motif_root)
                face = tgt / (med * D.SHADE)
                idle = face / D.BOARD_CAP_TINT
                ok = not (np.any(idle > 1.0) or np.any(face < D.SEAT_FLOOR - 1e-9))
                dl = state_lum(med, DISABLED)
                rl = np.clip(px * face * D.SHADE, 0.0, 1.0) @ REC709
                contr = float(rl.std() / max(rl.mean(), 1e-9))
                say(f"    {style:7s} target {t:.3f}  clip {clip*100:5.2f}%  "
                    f"idle {np.round(idle,3)} {'ok ' if ok else 'OUT'}  "
                    f"disab {dl:.4f} {'ok ' if dl >= bar else 'LOW'}  contrast {contr*100:5.2f}%")
                if not ok or dl < bar:
                    continue
                if best is None or contr > best["contrast"]:
                    best = dict(target=float(t), clip=clip, plate=med, idle=idle, face=face,
                                contrast=contr, disab=dl, confirm=state_lum(med, CONFIRMED),
                                rendered=np.clip(med * face * D.SHADE, 0.0, 1.0))
            if best is None:
                raise SystemExit(f"{style}: no field target satisfies the constraints")
            before_rl = np.clip(before_px * face_now * D.SHADE, 0, 1) @ REC709
            best.update(style=style, option=opt, target_colour=tgt, exposure=expo,
                        want_lum=want, before_rendered=ren_now, before_field=before_med,
                        before_clip=before_clip,
                        before_disab=state_lum(before_med, DISABLED),
                        before_confirm=state_lum(before_med, CONFIRMED),
                        before_contrast=float(before_rl.std() / max(before_rl.mean(), 1e-9)))
            out[style] = best
    finally:
        C.FIELD_TARGET_LUM.clear()
        C.FIELD_TARGET_LUM.update(keep)
    return out


# =========================================================================================
# WHAT SHIPS -- kept here so a rebuild that drifts from the C# is caught by --report
# =========================================================================================
# Cards/PlayTray.6.Build.BoardIdleColor, BEFORE this round.  ModBuild 286 solved these against
# `cap_deltae.PALETTE`; see THE AUTHORED PALETTE in the header for why they are what they are.
SHIPPED_IDLE = {
    "oak": np.array([0.550, 0.514, 0.564]),
    "steel": np.array([0.407, 0.541, 0.607]),
    "bronze": np.array([0.753, 0.471, 0.224]),
}

# Cards/PlayTray.6.Build.BoardIdleColor and cap_atlas.FIELD_TARGET_LUM as they ship NOW.  These
# two are ONE decision -- the idle colour is `target / (fieldMean x shade x tint)` and the field
# mean is what the target produces -- so they are written down together and `--report` asserts
# that the atlas on disk still renders where they say it does.
BOARD_IDLE = {
    "oak": np.array([0.759, 0.539, 0.456]),
    "steel": np.array([0.588, 0.557, 0.506]),
    "bronze": np.array([0.549, 0.547, 0.529]),
}


# =========================================================================================
# REPORT
# =========================================================================================
def report(boards_dir, idle=None, n=256, before=None):
    """The acceptance table.

    BOTH columns are read off a PNG: `before` is the ModBuild 290 atlas as it shipped (kept in
    .planning/debug/round5/atlas_before/) under the ModBuild 286 idle colours, `now` is what is
    in the bundle under the colours in `BOARD_IDLE`. Measuring the new atlas under the old
    colours -- which is what the first version of this function did, because the atlas had
    already been overwritten -- compares neither of the two things that ever shipped."""
    idle = BOARD_IDLE if idle is None else idle
    say("READ OFF THE PNGs, THROUGH THE SHIPPED CHAIN")
    say(f"  shade {D.SHADE:.5f}   BoardCapTint {D.BOARD_CAP_TINT}   seat floor "
        f"{np.round(D.SEAT_FLOOR, 4)}")
    say(f"  the well renders at luminance {well_lum():.4f}; SeatedCapColor promises a cap face "
        f"at >= {D.CAP_SEAT_CONTRAST}x that = {guard_bar():.4f}")
    say(f"  before: {before or '(bundle)'}   now: {BUNDLE}")
    say("")
    rows = {}
    for style in STYLES:
        med, px, clip = shipped_field(style)
        bmed, bpx, bclip = shipped_field(style, bundle=before) if before else (med, px, clip)
        rows[(style, "shipped")] = np.clip(
            bmed * D.face_colour(SHIPPED_IDLE[style]) * D.SHADE, 0.0, 1.0)
        rows[(style, "now")] = np.clip(med * D.face_colour(idle[style]) * D.SHADE, 0.0, 1.0)
        rows[(style, "clip")] = clip
        rows[(style, "bclip")] = bclip
        rows[(style, "field")] = med
        rows[(style, "bfield")] = bmed
        for tag, p, f in (("c", px, D.face_colour(idle[style])),
                          ("bc", bpx, D.face_colour(SHIPPED_IDLE[style]))):
            rl = np.clip(p * f * D.SHADE, 0, 1) @ REC709
            rows[(style, tag)] = float(rl.std() / max(rl.mean(), 1e-9))
    opt = {s: option_material(s) for s in STYLES}
    brd = {s: board_material(s, boards_dir) for s in STYLES}
    tgt = {}
    for s in STYLES:
        tgt[s], _ = _expose(opt[s], lum(rows[(s, "shipped")]))

    say("1. dE2000 OF THE CAP FACE AGAINST THE OPTION IT WAS BUILT FROM")
    say("   (`--hc` = at matched lightness: hue and chroma only, the form the complaint is about)")
    say("   board   | full dE2000        | hue+chroma dE2000  | CIE76 (old record's units)")
    say("           | shipped ->  now    | shipped ->  now    | shipped ->  now")
    for s in STYLES:
        a, b = rows[(s, "shipped")], rows[(s, "now")]
        say(f"   {s:7s} |  {de(a,opt[s]):6.2f} -> {de(b,opt[s]):6.2f}   |  "
            f"{de_hc(a,opt[s]):6.2f} -> {de_hc(b,opt[s]):6.2f}   |  "
            f"{de76(a,opt[s]):6.2f} -> {de76(b,opt[s]):6.2f}")
    say("")
    say("   against the option RE-EXPOSED to the cap's own luminance (the solve's actual target):")
    for s in STYLES:
        say(f"   {s:7s} |  {de(rows[(s,'shipped')],tgt[s]):6.2f} -> {de(rows[(s,'now')],tgt[s]):6.3f}")

    say("")
    say("2. dE2000 OF THE CAP AGAINST ITS OWN BOARD, and the hue angle that decides 'belongs'")
    say("   board   | hue err vs board      | hue+chroma dE2000  | C* cap / C* board")
    for s in STYLES:
        a, b = rows[(s, "shipped")], rows[(s, "now")]
        say(f"   {s:7s} | {hue_err(a,brd[s]):+7.1f} -> {hue_err(b,brd[s]):+6.1f} deg |  "
            f"{de_hc(a,brd[s]):6.2f} -> {de_hc(b,brd[s]):6.2f}   |  "
            f"{D.lch(a)[1]:5.1f}/{D.lch(brd[s])[1]:4.1f} -> {D.lch(b)[1]:5.1f}/{D.lch(brd[s])[1]:4.1f}")
    say("   for reference, the OPTIONS' own hue error against their boards -- the bar:")
    for s in STYLES:
        say(f"   {s:7s} | {hue_err(opt[s],brd[s]):+7.1f} deg")

    say("")
    say("3. dE2000 BETWEEN THE THREE BOARDS' CAPS -- this must not collapse")
    for a, b in itertools.combinations(STYLES, 2):
        say(f"   {a:7s}-{b:7s}  shipped {de(rows[(a,'shipped')],rows[(b,'shipped')]):6.2f}"
            f"  ->  now {de(rows[(a,'now')],rows[(b,'now')]):6.2f}"
            f"   (the OPTIONS themselves: {de(opt[a],opt[b]):6.2f})")

    say("")
    say("")
    say("4. THE TERMS THE COLOUR ROUND MUST NOT HAVE BROKEN")
    say("   board   | idle lum before -> now  | disabled lum vs the guard bar | confirm lum")
    for s in STYLES:
        a, b = rows[(s, "shipped")], rows[(s, "now")]
        med, bmed = rows[(s, "field")], rows[(s, "bfield")]
        dl, bdl = state_lum(med, DISABLED), state_lum(bmed, DISABLED)
        cl, bcl = state_lum(med, CONFIRMED), state_lum(bmed, CONFIRMED)
        say(f"   {s:7s} | {lum(a):.4f} -> {lum(b):.4f} ({lum(b)/lum(a):5.3f}x) | "
            f"{bdl:.4f} ({bdl/guard_bar():5.3f}x) -> {dl:.4f} ({dl/guard_bar():5.3f}x) | "
            f"{bcl:.4f} -> {cl:.4f}")

    say("")
    say("5. THE 'EINHEITLICH' HALF: the rendered field's own contrast, and its clipping")
    say("   board   | field texels clipping >=1 channel | rendered field contrast")
    for s in STYLES:
        say(f"   {s:7s} | {rows[(s,'bclip')]*100:6.2f}% -> {rows[(s,'clip')]*100:6.2f}%"
            f"          | {rows[(s,'bc')]*100:6.2f}% -> {rows[(s,'c')]*100:6.2f}%"
            f"  ({rows[(s,'c')]/max(rows[(s,'bc')],1e-9):4.2f}x)")
    return rows, opt, brd, tgt


# =========================================================================================
# SELFCHECK -- a new instrument's first output is a hypothesis
# =========================================================================================
SHARMA = [  # (Lab1, Lab2, published dE2000) -- Sharma, Wu & Dalal (2005) table 1
    ((50.0000, 2.6772, -79.7751), (50.0000, 0.0000, -82.7485), 2.0425),
    ((50.0000, 3.1571, -77.2803), (50.0000, 0.0000, -82.7485), 2.8615),
    ((50.0000, 2.8361, -74.0200), (50.0000, 0.0000, -82.7485), 3.4412),
    ((50.0000, -1.3802, -84.2814), (50.0000, 0.0000, -82.7485), 1.0000),
    ((50.0000, 2.5000, 0.0000), (50.0000, 0.0000, -2.5000), 4.3065),
    ((50.0000, 2.5000, 0.0000), (73.0000, 25.0000, -18.0000), 27.1492),
    ((50.0000, 2.5000, 0.0000), (50.0000, 3.1736, 0.5854), 1.0000),
    ((50.0000, 2.5000, 0.0000), (50.0000, 3.2972, 0.0000), 1.0000),
    ((60.2574, -34.0099, 36.2677), (60.4626, -34.1751, 39.4387), 1.2644),
    ((35.0831, -44.1164, 3.7933), (35.0232, -40.0716, 1.5901), 1.8645),
    ((22.7233, 20.0904, -46.6940), (23.0331, 14.9730, -42.5619), 2.0373),
    ((90.8027, -2.0831, 1.4410), (91.1528, -1.6435, 0.0447), 1.4441),
    ((2.0776, 0.0795, -1.1350), (0.9033, -0.0636, -0.5514), 0.9082),
]
# THE FIFTH ENTRY WAS TYPED WRONG THE FIRST TIME (7.1792, which is a different pair's value)
# and the selfcheck failed on it while the other nine were exact to 1e-4.  Recorded because the
# tempting move at that moment is to "fix" the implementation until the table agrees: nine
# exact pairs including all four of the blue-region cases that exercise the Rt rotation term
# is not something a broken implementation produces.  The reference was corrected, not the code.


def selfcheck(boards_dir):
    bad = 0
    say("1. THE FORMULA.  CIEDE2000 against Sharma/Wu/Dalal's published pairs.")
    worst = 0.0
    for l1, l2, want in SHARMA:
        got = de2000(l1, l2)
        worst = max(worst, abs(got - want))
    say(f"   {len(SHARMA)} reference pairs, worst error {worst:.5f}  "
        f"{'PASS' if worst < 1e-3 else 'FAIL'}")
    bad += worst >= 1e-3

    say("2. THE NULL CONTROL.  A cap against ITSELF must be exactly 0.")
    med, _, _ = shipped_field("oak")
    r = np.clip(med * D.face_colour(BOARD_IDLE["oak"]) * D.SHADE, 0, 1)
    z = de(r, r) + de_hc(r, r) + abs(hue_err(r, r))
    say(f"   oak vs oak: dE2000 {de(r,r):.6f}  hue+chroma {de_hc(r,r):.6f}  hue err "
        f"{hue_err(r,r):+.6f}  {'PASS' if z < 1e-9 else 'FAIL'}")
    bad += z >= 1e-9

    say("3. THE KNOWN POSITIVE.  Fed the OPTION ITSELF as if it were the rendered cap, the")
    say("   instrument must report dE 0 against that option and reproduce the option's own")
    say("   hue error against its board.")
    ok = True
    for s in STYLES:
        opt = option_material(s)
        brd = board_material(s, boards_dir)
        d, h = de(opt, opt), hue_err(opt, brd)
        say(f"   {s:7s} dE2000 vs its option {d:.6f}   hue err vs board {h:+6.1f} deg")
        ok &= d < 1e-9 and abs(h) < 15.0
    say(f"   {'PASS' if ok else 'FAIL'} -- an option that did not sit within 15 deg of its own "
        f"board would mean the ROUND-4 GENERATION failed, not this measurement")
    bad += not ok

    say("4. THE DISCRIMINATION CONTROL.  A NEUTRAL GREY cap at the same luminance must be")
    say("   reported as NOT belonging to any board (large hue+chroma dE on every one).")
    ok = True
    for s in STYLES:
        med, _, _ = shipped_field(s)
        r = np.clip(med * D.face_colour(BOARD_IDLE[s]) * D.SHADE, 0, 1)
        g = np.full(3, float(np.mean(r)))
        d = de_hc(g, board_material(s, boards_dir))
        say(f"   {s:7s} grey vs board: hue+chroma dE2000 {d:6.2f}"
            f"   {'ok' if d > 3.0 else 'TOO SMALL'}")
        ok &= d > 3.0
    say(f"   {'PASS' if ok else 'FAIL'}")
    bad += not ok

    say("5. THE RE-EXPOSURE.  `_expose` must preserve hue exactly and reach the asked-for")
    say("   luminance, and it must beat a gamma-space fade on chroma.")
    ok = True
    for s in STYLES:
        opt = option_material(s)
        want = 0.42 * lum(opt)
        ex, k = _expose(opt, want)
        fade = opt * (want / lum(opt))
        dh = abs(hue_err(ex, opt))
        say(f"   {s:7s} exposure {k:5.3f}  lum {lum(ex):.5f} (asked {want:.5f})  hue drift "
            f"{dh:+.3f} deg   C* exposure {D.lch(ex)[1]:5.2f} vs gamma-fade {D.lch(fade)[1]:5.2f}")
        ok &= abs(lum(ex) - want) < 1e-5 and dh < 1.0 and D.lch(ex)[1] >= D.lch(fade)[1]
    say(f"   {'PASS' if ok else 'FAIL'}")
    say("   The hue bar is 1.0 deg, NOT 0.  A linear exposure change is not exactly")
    say("   hue-preserving in CIELAB -- the space is not a linear one, and near the sRGB")
    say("   transfer curve's linear TOE a dark channel moves differently from a bright one.")
    say("   Oak drifts 0.74 deg because its blue channel lands at 0.0038, just above the toe")
    say("   at 0.0031308.  A 0.05 deg bar was set first and it was measuring the encoding, not")
    say("   a defect.  What the check must actually assert is the CHROMA ordering, and it now")
    say("   does: a true exposure must keep more chroma than a gamma-space fade of the same")
    say("   material to the same luminance.")
    bad += not ok

    say("")
    say("   NOT CHECKED, AND IT CANNOT BE: whether the option is the colour the USER wants.")
    say("   The instrument measures agreement with round 4's chosen option and with the board.")
    say("   Both are pictures we produced.  Only hardware answers the question behind them.")
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--selfcheck", action="store_true")
    ap.add_argument("--solve", action="store_true")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--boards", default=os.path.join(REPO, ".planning", "debug", "round5", "boards"))
    ap.add_argument("--json", default=None)
    ap.add_argument("--before", default=os.path.join(
        REPO, ".planning", "debug", "round5", "atlas_before"),
        help="the atlas the round STARTED from, for the before column")
    ap.add_argument("--n", type=int, default=256)
    ap.add_argument("--grid", default=None,
        help="comma-separated FIELD targets to try (default: a coarse sweep)")
    a = ap.parse_args()
    rc = 0
    if a.selfcheck:
        rc |= selfcheck(a.boards)
    if a.solve:
        s = solve(n=a.n, boards_dir=a.boards,
                  grid=[float(v) for v in a.grid.split(',')] if a.grid else None)
        say("")
        say("THE SOLVE -- per board: the field target, and the IdleColor it implies")
        for st in STYLES:
            z = s[st]
            say(f"  {st:7s} FIELD TARGET {z['target']:.3f} (was {C.GRAIN_TARGET_LUM:.3f})"
                f"  built-field clip {z['clip']*100:5.2f}% (was {z['before_clip']*100:5.2f}%)")
            say(f"          built field median {np.round(z['plate'],4)}   -> IdleColor "
                f"{np.round(z['idle'],4)}   face {np.round(z['face'],4)}")
            say(f"          rendered {np.round(z['rendered'],4)}  target "
                f"{np.round(z['target_colour'],4)}  dE2000 {de(z['rendered'],z['target_colour']):.4f}")
            say(f"          contrast {z['contrast']*100:5.2f}% (was {z['before_contrast']*100:5.2f}%)"
                f"  disabled {z['disab']:.4f} vs bar {guard_bar():.4f}"
                f" (was {z['before_disab']:.4f})")
        if a.json:
            json.dump({k: {kk: (vv.tolist() if isinstance(vv, np.ndarray) else vv)
                           for kk, vv in v.items() if kk != "px"}
                       for k, v in s.items()}, open(a.json, "w"), indent=1)
    if a.report:
        report(a.boards, before=a.before)
    return rc


if __name__ == "__main__":
    sys.exit(main())
