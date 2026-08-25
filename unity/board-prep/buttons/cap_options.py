#!/usr/bin/env python3
"""ROUND 4 -- ask the generator for BUTTON OPTIONS THAT BELONG TO A PARTICULAR BOARD.

WHY ROUND 4 EXISTS, and the one thing rounds 1-3 all had in common
------------------------------------------------------------------
Three rounds of cap art have been rejected, twice with the same word ("einheitlich", "wie
zusammengewuerfelte assets mit standard meshs draufgeklatscht") and the third time even after
`plate_forensics.py` measured a real improvement on border structure (REG 3.7/9.9/3.8 ->
28.3/35.3/23.7).

The user's complaint this round is RELATIONAL and it names the input, not the output:

    "Ich mag die Textur gar nicht. Sie passt ueberhaupt nicht zu dem jeweiligen board. Bitte
     gehe das anders an: Gib gpt-image-2 die boards und lass die Optionen fuer eckige und
     runde Knoepfe generieren die zum Design des Boards passen. Baue die dann nach. Verwirf
     deine aktuellen -- das fuehrt zu nichts."
    -- user, 2026-08-25

**Every previous round showed the generator a picture of a MATERIAL or a picture of a SHAPE,
and never once a picture of the BOARD.**

    round 1   `gen_capref.py` -> `ref_face_<style>.png`: a 1024^2 band cropped out of the
              board's albedo. That is a swatch of the board's SURFACE. It carries the board's
              palette and its grain and none of its construction -- no border, no corner, no
              hardware, no edge, no proportion.
    round 2   the same swatch, with the ask re-worded for feature SIZE.
    round 3   `cap_object.py --init` -> a bare grey render of the signet profile. That is a
              picture of the mesh we already have. It carries construction and no board at all.

You cannot generate something that BELONGS ON an object you never showed the model. Round 3's
prompts are the proof: read `cap_object.PROMPTS` and notice that the word "board" does not
appear in any of them. They ask for an oak plate, a steel plate and a bronze plate -- three
materials, correctly and vividly described, with no reference to the three objects those
plates have to sit in.

AND THE MESH WAS NEVER IN QUESTION EITHER
------------------------------------------
`cap_object.PROMPTS` opens with "The supplied grey image is the exact GEOMETRY of these two
plates and must be followed band for band." So all three rounds asked the model to pour a
material into ONE profile -- `CapFaceLayout.BezelChamfer/BezelRim/BezelStep`, identical on all
three boards. That profile is a good one. It is also, on all three boards at once, literally
the same mesh with a different texture on it, which is a fair description of

    "wie zusammengewuerfelte assets mit standard meshs draufgeklatscht".

Twice this user has volunteered praise for a result, and both times the winning move was the
same one: THE MESH WAS ADAPTED TO THE ART. The board front was rebuilt that way; the board back
and sides were rebuilt that way after "Auf den Texturen sind Schrauben und Halzplatten etc zu
sehen, also eigentlich 3-dimensionale Objekte. Sie werden aber flach nur auf der Textur
dargestellt. Ich moechte, dass du das Mesh an die Textur anpasst." He also said of round 2's
caps, unprompted, "Mir gefaellt dass du die buttons etwas anders vom Mesh her designt hast".

So this module does the FIRST half -- ask for options, against the boards -- and
`cap_profile.py` does the second half, which is that the chosen option's three-dimensional
features become GEOMETRY rather than a painted highlight.

WHAT THE MODEL IS SHOWN
-----------------------
`unity/asset-preview/build_asset_strips.sh`'s own board block, at 1536 instead of 900 and in
two views, rendered from the SHIPPED bundle assets:

    board_<style>.png        yaw 35, pitch 50 -- the strip's own three-quarter view, which is
                             roughly what the player's eye does to a tray lying flat
    board_<style>_flat.png   yaw 12, pitch 72 -- closer to plan, where the border profile, the
                             corner hardware and the seat pockets read cleanly

Every flag in that block is a decision with a reason and they are inherited unchanged: the
normal map is BOUND (without it the carved recesses have no relief and the tray reads as a
painted plank), there is NO --cull (every tray material is _Cull: 0, unlike the hands'
_Cull: 2), and all three are pinned to one ortho width so the strip cannot claim one board is
half again as big as another.

Board identity is taken from the SOURCE and not from the look of the render, because it has
been swapped once before: oak = `PlayTray_prepped.fbx`, steel = `PlayTray_9capjqp6*`,
bronze = `PlayTray_16vm268h*` (`BoardFrame.cs:68-69`, `VRCardFactory.cs:29-30`).

WHAT THE ASK IS FOR
-------------------
Options, plural, differing in CONSTRUCTION -- not one plate per board differing in colour.
Round 3 asked for one object per board and got exactly one object per board, so there was
nothing to choose between and nothing for the user to point at. The sheet this produces is
something he can point at.
"""
import argparse
import base64
import json
import mimetypes
import os
import re
import sys
import time
import urllib.error
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

REPO = os.path.dirname(os.path.dirname(PREP))
OUT = os.path.join(HERE, "out")
DEBUG = os.path.join(REPO, ".planning", "debug", "round4")
BOARDS = os.path.join(DEBUG, "boards")

STYLES = ("oak", "steel", "bronze")

MODEL = "gpt-image-2"
SIZE = "1536x1024"          # 3:2, a NATIVE size for this model, passed EXPLICITLY as pixels.
                            # The aspect-enum rescale trap recorded in ../img2img/README.md (a
                            # 16:9 ask silently resampled into 3:2) cannot fire when the request
                            # names a pixel size; the delivered size is read back with PIL
                            # anyway, because "cannot fire" is a claim and reading is a
                            # measurement.


# =========================================================================================
# THE KEY. It lives in the MAIN checkout's .env, which is gitignored, and it is never printed,
# never echoed, never written into a tracked file and never put in a log.
# =========================================================================================
def _api_key():
    path = os.path.join(REPO, ".env")
    if not os.path.isfile(path):
        # A worktree does not carry .env -- it is in the main checkout only.
        path = "/home/claw/gloomhaven_vr/.env"
    with open(path) as fh:
        m = re.search(r'OPENAI_API_KEY=("?)([^"\n]+)\1', fh.read())
    if not m:
        raise RuntimeError("no OPENAI_API_KEY in .env")
    return m.group(2).strip()


# =========================================================================================
# THE PROMPTS, VERBATIM.
#
# Round 2 shipped two plates whose wording was lost and had to record "substance, not prompts".
# Round 3 corrected that and this round inherits the correction: these are the exact strings
# posted to the API, and `_check_manifest()` runs at import and RAISES if any
# `keycap4_options_*.png` in ./out is unaccounted for or is listed both ways.
#
# WHAT IS DELIBERATELY *NOT* IN THESE PROMPTS: the signet profile. Round 3 said "The supplied
# grey image is the exact GEOMETRY of these two plates and must be followed band for band."
# Saying that again would ask the model to re-skin the mesh we already have, which is the thing
# being discarded. The bands are imposed LATER, per board, by `cap_profile.register` -- against
# the profile the CHOSEN option implies, not against the one round 3 shipped.
#
# WHAT IS IN THEM AND IS LOAD-BEARING: the field is EMPTY. The mod carves its own symbol and
# solves its own caption into that field (`CapFaceLayout`, `CapSymbols`), and a generated glyph
# there would be a second, wrong, permanently-untranslated one. `TextOverflowModes.Truncate` is
# banned mod-wide precisely because a half-word on a cap shipped once ("AUSWAHL BEEN"); a model
# inventing lettering is the same failure with no layout solve behind it at all.
# =========================================================================================
_COMMON = """The two supplied photographs are the SAME OBJECT: a control board that lies flat on
a table in front of a player, about 640 mm wide, seen from above at two angles. Study it. It was
made by one maker, out of one material, with one vocabulary of edge treatments, borders, corner
hardware and carved recesses.

At the right-hand end of that board there is a column of three square seats, each about 40 mm
across, sunk into the surface. Buttons go in those seats. Design them.

Produce ONE DESIGN SHEET, a 3 x 2 grid of six separate buttons on a plain flat neutral grey
studio backdrop, evenly spaced, generous even margins, no grid lines and no frames:
  TOP ROW    three ROUND buttons, circular in plan;
  BOTTOM ROW three SQUARE buttons, square in plan with the corners treated as the board's own
             corners are treated.
All six are the same size in the image and are seen from the same angle -- from above and
slightly toward the viewer, about the angle the second photograph is taken from, so that the
thickness and the edge profile of each button are clearly visible. One soft key light from
above, the same on all six, no coloured rim light, no cast shadows on the backdrop, no
vignette, no depth of field, no perspective distortion.

THE SIX MUST DIFFER IN CONSTRUCTION, NOT IN COLOUR. They are six ways of building the same part
out of the same material, as if laid out on a workbench for someone to choose between. Vary:
  - the EDGE PROFILE -- a single chamfer, a stepped double bezel, a rolled-over lip, a raised
    collar standing proud of a wider base, an undercut skirt, a domed crown;
  - the MOUNTING HARDWARE -- a plain seated plug, a flange with countersunk screws, corner
    rivets, a retaining ring, a keyed or slotted collar, a soldered-on frame;
  - the FACE TREATMENT -- flat, dished, slightly domed, cross-hatched, radially brushed,
    stippled.
Do not make one of them a repeat of another with a different tint.

EVERY BUTTON FACE IS COMPLETELY EMPTY. No letter, no digit, no word, no rune, no icon, no
symbol, no engraved device, no logo, no maker's mark and no ornament in the middle of any face.
Marks of MAKING and HANDLING belong on the edges and the hardware -- tool marks, wear where a
thumb lands, dirt in a groove -- and are welcome there. There is also NO TEXT ANYWHERE IN THE
IMAGE: no captions, no labels, no numbers, no arrows.

THE TEST THESE MUST PASS is that a person shown the board and then shown the sheet says the
buttons came off that same board. Take the material, the colour, the age, the wear, the way the
edges are cut and the kind of hardware from the photographs.
{story}
Fill the frame. The six buttons and the flat grey backdrop are the entire image."""

PROMPTS = {
    "oak": _COMMON.format(story="""
This board is WOOD. Read off the photographs: a square-block dentil border of small raised
rectangles runs all the way round the frame, the corners are square and unrounded, the panels
and seats are recesses cut into the solid with clean chamfered walls, and there is no metal
anywhere on its face -- no rivets, no straps, no bosses. It is joinery, not smithing. So these
buttons are turned or carved wood, and where hardware is needed it is wooden -- a pegged plug, a
turned collar, a wedged tenon, a laid-in inlay ring -- or at most a small dark iron pin. Keep
the dentil idea alive: a border of small repeated blocks is this board's signature and one or
two of the six should carry a version of it."""),
    "steel": _COMMON.format(story="""
This board is SHEET STEEL, blued and heat-mottled, with straw-gold and violet temper bloom and
rust breaking through at the edges. Read off the photographs: the border is a run of flat
rectangular blocks with small round rivet heads set among them, the corners are square, and the
seats are pockets pressed or milled into the plate with a raised inner plateau -- plate stacked
on plate. So these buttons are made the way a plate is made: cut, pressed, filed, and fixed
down with visible rivets, screws or a soldered-on collar. Straight parallel file marks and a
ragged wear line where the blue has been rubbed back to bare metal belong here."""),
    "bronze": _COMMON.format(story="""
This board is CAST BRONZE gone green -- verdigris pooled in every recess, the high points
burnished back to warm gold. Read off the photographs: EVERY corner and every outline is
ROUNDED, the border carries both a run of small square beads AND large round dome-headed bosses
at the corners and midpoints, the panel outlines are fine double incised lines following a
rounded rectangle, and the medallions are ringed with a plaited rope interlace. So these buttons
are cast, not cut: soft rounded arrises, a bead or rope ring where a hard edge would be, dome
heads for hardware, and verdigris caught wherever a form turns down. The rounded corners and the
domed boss are this board's signature and the square options must not have sharp corners."""),
}


# =========================================================================================
# THE CALL
# =========================================================================================
def _multipart(fields, files):
    """Build a multipart/form-data body. Written out rather than pulled in because this repo's
    generation scripts have no third-party HTTP dependency and adding one for six POSTs would
    put a new package between the artist's asset chain and the bundle."""
    boundary = "----gvr" + uuid.uuid4().hex
    out = []
    for k, v in fields:
        out.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n"
                   .encode("utf-8"))
    for k, path in files:
        name = os.path.basename(path)
        ctype = mimetypes.guess_type(name)[0] or "application/octet-stream"
        with open(path, "rb") as fh:
            blob = fh.read()
        out.append((f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"; "
                    f"filename=\"{name}\"\r\nContent-Type: {ctype}\r\n\r\n").encode("utf-8"))
        out.append(blob)
        out.append(b"\r\n")
    out.append(f"--{boundary}--\r\n".encode("utf-8"))
    return b"".join(out), boundary


def generate(style, dst, refs=None, prompt=None, retries=3, say=print):
    """One design sheet for one board. Returns the delivered (w, h), READ BACK from the file.

    The reference images are the board renders themselves. `/v1/images/edits` with several
    reference images is the reference-image path for this model family, and it is the one round
    3's chain used; the difference this round is WHAT is referenced.
    """
    from PIL import Image
    if refs is None:
        refs = [os.path.join(BOARDS, f"board_{style}.png"),
                os.path.join(BOARDS, f"board_{style}_flat.png")]
    for r in refs:
        if not os.path.isfile(r):
            raise SystemExit(f"missing board render {r} -- run the board block of "
                             "unity/asset-preview/build_asset_strips.sh first")
    body, boundary = _multipart(
        [("model", MODEL), ("prompt", prompt or PROMPTS[style]), ("size", SIZE), ("n", "1")],
        [("image[]", r) for r in refs])
    req = urllib.request.Request(
        "https://api.openai.com/v1/images/edits", data=body,
        headers={"Authorization": "Bearer " + _api_key(),
                 "Content-Type": f"multipart/form-data; boundary={boundary}"})
    last = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=900) as fh:
                payload = json.load(fh)
            break
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:500]
            # NEVER let the request headers into this message: they carry the key.
            last = f"HTTP {e.code}: {detail}"
            say(f"  [{style}] {last}")
            if e.code < 500 and e.code != 429:
                raise SystemExit(last)
            time.sleep(5 * (attempt + 1))
        except Exception as e:                                   # noqa: BLE001
            last = f"{type(e).__name__}: {e}"
            say(f"  [{style}] {last}")
            time.sleep(5 * (attempt + 1))
    else:
        raise SystemExit(f"{style}: {last}")

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as fh:
        fh.write(base64.b64decode(payload["data"][0]["b64_json"]))
    with Image.open(dst) as im:
        got = im.size
    say(f"  [{style}] {os.path.basename(dst)}  asked {SIZE}  DELIVERED {got[0]}x{got[1]}"
        + ("" if f"{got[0]}x{got[1]}" == SIZE else "   <-- SIZE DIFFERS FROM THE ASK"))
    return got


# =========================================================================================
# THE MANIFEST -- what was generated, what was kept, what was discarded and WHY.
#
# Same shape and the same import-time assertion as `cap_object.CHOSEN`/`DISCARDED`. The record
# of round 3's discard is what let us find our own pipeline bug; a record that can go stale
# silently is not a record.
# =========================================================================================
GENERATED = {
    "oak":    "keycap4_options_oak.png",
    "steel":  "keycap4_options_steel.png",
    "bronze": "keycap4_options_bronze.png",
}

# WHAT WAS KEPT, PER BOARD. Cells are R1..R3 (round, top row, left to right) and S1..S3
# (square, bottom row); `cap_sheet_options.NOTES` says what each one is and the contact sheet
# shows all eighteen. Three sheets, eighteen options, six kept, twelve discarded.
#
# ROUND AND SQUARE ARE BOTH MANDATORY ("Rund und Viereckig sind Vorgabe"), so every board keeps
# exactly one of each. The criterion is not "which button is nicest" -- it is which one is a
# claim about THAT BOARD, because that is the complaint this round exists to answer.
CHOSEN = {
    "oak":    ("R2", "S3"),
    "steel":  ("R2", "S1"),
    "bronze": ("R2", "S2"),
}

# WHY EACH KEPT ONE WAS KEPT -- against the board, not against the other options.
KEPT_BECAUSE = {
    "oak/S3": "the board's square dentil border, at cap scale. This is the single strongest "
              "image on any of the three sheets: the oak board's one unmistakable signature is "
              "a run of small raised blocks around its frame, and this cap wears it. Nothing "
              "else on the oak sheet is a claim about the oak board rather than about oak.",
    "oak/R2": "the round sibling of a stepped, blocked border, and it keeps the clipped-corner "
              "joinery family. Chosen so the two shapes read as one set; the dentil ring is "
              "then built onto BOTH shapes, which is what 'nachbauen' means here.",
    "steel/S1": "four DOME RIVET HEADS. The steel board's border is dentils with small round "
                "rivet heads set among them -- rivets, not screws. S3's slotted screws are the "
                "better-looking cap and the worse match; the board has no screw slot anywhere "
                "on it.",
    "steel/R2": "plate stacked on plate with visible corner hardware, which is how the steel "
                "board's own seats are built (a pocket with a raised inner plateau). Its "
                "screws are rebuilt as S1's rivets so the pair agrees with the board.",
    "bronze/S2": "a raised inner plateau ringed by four dome bosses, with every corner rounded "
                 "-- which is the bronze board's seat pocket, feature for feature.",
    "bronze/R2": "the cast stepped terrace. Concentric raised rings are the bronze board's own "
                 "language (every panel outline is a rounded double incised line) and it pairs "
                 "with S2's plateau.",
}

# WHY EACH DISCARDED ONE WAS DISCARDED. Twelve entries, one per option not taken. "Nice but not
# of this board" is the commonest reason and it is the whole point of the round -- three rounds
# were lost to art that was judged on its own and not against the object it sits in.
DISCARDED_OPTIONS = {
    "oak/R1":   "a plain rolled rim. Correct for oak and true of any turned wooden disc; says "
                "nothing about THIS board. This is round 1-3's failure shape exactly.",
    "oak/R3":   "rolled-over lip with a DISHED face. The dish is disqualifying, not stylistic: "
                "the recessed field carries a carved symbol and a live caption, and both are "
                "solved on a flat viewer-facing plane (CapFaceLayout). A curved field bends "
                "the text.",
    "oak/S1":   "clipped joinery corners and an incised border, and no dentil. Kept as the "
                "CORNER treatment for all three oak caps; discarded as a cap in its own right "
                "because S3 is it, plus the board's signature.",
    "oak/S2":   "four square pegs at the corners. Good joinery, but the oak board's face "
                "carries no hardware at all -- no rivet, no strap, no boss. Pegs put metal-"
                "shaped fittings on the one board whose whole story is that it has none.",
    "steel/R1": "a plain seated plug with a single thin chamfer. The nearest thing on any sheet "
                "to what ModBuild 289 already ships, and rejected for the same reason.",
    "steel/R3": "a rolled lip on a wider flange. Handsome and it is the OTHER steel board's "
                "vocabulary -- a pressed rolled edge, where this board is cut plate with "
                "discrete fixings.",
    "steel/S2": "a keyed retaining ring. The ring is a large circular device sitting in the "
                "middle of the face, which is exactly where the carved role symbol goes. Two "
                "circles competing for the same 25 % of the cap is unreadable at the "
                "across-the-table 32 px view cap_check.py gates on.",
    "steel/S3": "four slotted corner screws. See S1: the board has rivet heads and no screw "
                "slot anywhere. Discarded on the board, not on the button.",
    "bronze/R1": "a single incised ring. The calm option; carries one of the board's several "
                 "cues and none of the strong ones.",
    "bronze/R3": "a domed crown. Same disqualification as oak/R3 and worse: the crown is the "
                 "whole face, so there is no flat field left for the symbol or the caption.",
    "bronze/S1": "rounded square with four dome bosses -- S2 without the raised plateau. S2 is "
                 "strictly this plus the board's own stepped pocket, so S1 is dominated.",
    "bronze/S3": "a rolled lip with keyed retaining notches. The notches read as damage at cap "
                 "scale rather than as hardware, and the bronze board has no notch on it.",
}

# Sheets discarded WHOLE (a re-roll). None: all three landed first try, as round 3's object
# plates did. The ask names the board and the construction axes, and an ask that names what it
# wants does not need rolling.
DISCARDED = {}


CELLS = ("R1", "R2", "R3", "S1", "S2", "S3")


def _check_manifest(out_dir=OUT):
    """RAISE if the record does not account for every generated sheet and every option on it.

    Round 2 shipped a narrative describing a different set of images from the one its own
    manifest named, and nothing checked. This runs at import. Driven negative before it was
    believed: deleting one entry from DISCARDED_OPTIONS makes it raise by name.
    """
    both = set(GENERATED.values()) & set(DISCARDED)
    if both:
        raise RuntimeError(f"cap_options: sheet(s) both generated and discarded: {sorted(both)}")

    # Every one of the six cells on every sheet is either kept or discarded, with a reason.
    for style in STYLES:
        kept = set(CHOSEN.get(style, ()))
        if len(kept) != 2 or not (kept & {"R1", "R2", "R3"}) or not (kept & {"S1", "S2", "S3"}):
            raise RuntimeError(
                f"cap_options: {style} must keep exactly one ROUND and one SQUARE option "
                f"(both shapes are the user's Vorgabe); got {sorted(kept)}")
        for c in CELLS:
            key = f"{style}/{c}"
            in_kept = c in kept
            has_why = key in (KEPT_BECAUSE if in_kept else DISCARDED_OPTIONS)
            if not has_why:
                raise RuntimeError(
                    f"cap_options: option {key} is {'kept' if in_kept else 'discarded'} with no "
                    f"reason recorded -- add it to "
                    f"{'KEPT_BECAUSE' if in_kept else 'DISCARDED_OPTIONS'}")
        stray = {k for k in DISCARDED_OPTIONS if k.startswith(style + "/")} & {
            f"{style}/{c}" for c in kept}
        if stray:
            raise RuntimeError(f"cap_options: option(s) both kept and discarded: {sorted(stray)}")

    if not os.path.isdir(out_dir):
        return
    named = set(GENERATED.values()) | set(DISCARDED)
    found = {f for f in os.listdir(out_dir)
             if f.startswith("keycap4_options_") and f.endswith(".png")}
    missing = found - named
    if missing:
        raise RuntimeError(
            "cap_options: round-4 sheet(s) in ./out that the record does not account for: "
            f"{sorted(missing)} -- add each to GENERATED or to DISCARDED with its reason")
    absent = named - found - set(DISCARDED)
    if absent:
        raise RuntimeError(
            f"cap_options: the record names sheet(s) not in ./out: {sorted(absent)}")


_check_manifest()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--styles", default=",".join(STYLES))
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--suffix", default="", help="write to keycap4_options_<style><suffix>.png")
    ap.add_argument("--print-prompts", action="store_true")
    a = ap.parse_args()
    if a.print_prompts:
        for k, v in PROMPTS.items():
            print(f"===== {k} =====\n{v}\n")
        return
    for s in [x for x in a.styles.split(",") if x]:
        dst = os.path.join(a.out, f"keycap4_options_{s}{a.suffix}.png")
        generate(s, dst)


if __name__ == "__main__":
    main()
