#!/usr/bin/env python3
"""Does the board engraving read as a CUT, or as pale text lying on the board?

WHY THIS EXISTS, AND WHAT IT REPLACES
-------------------------------------
`src/GloomhavenVR/Cards/BoardEngraving.cs` cuts localized text into the board with a TMP
distance-field material: a dark FACE at the board material's colour in shadow, a darker
KEYLINE, and a soft LIGHT UNDERLAY offset down-and-left for the lit lip of the incision.
Nothing in this repository can render that: TextMeshPro is not in the companion Unity
project's package manifest, so `Assets/Editor/PreviewKeycaps.cs` draws a STAND-IN out of
legacy `TextMesh` -- and its stand-in for the underlay is a FULL SECOND COPY of the glyph
rather than the thin sliver TMP actually leaves visible. That picture came back showing
pale ghost text on the plate, which is exactly what a full bright copy behind a dark glyph
looks like and tells you nothing about the real material.

**A picture that cannot show the thing is not evidence either way**, and this project has
lost rounds to reading one as if it were. So this file models the ONE thing the stand-in
gets wrong: TMP's layer compositing, from the same numbers the C# writes.

WHAT IT MODELS, AND WHAT IT DOES NOT
------------------------------------
MODELLED, and these are the terms the question turns on:
  * the three layers in TMP's own order -- underlay behind, outline ring, face on top --
    alpha-composited onto the board's real material;
  * the FACE / KEYLINE / LIT-LIP colours, verbatim from BoardEngraving.cs;
  * the underlay's OFFSET and SOFTNESS in SDF-spread units, so the lit lip comes out as the
    sliver it is rather than as a second glyph;
  * the outline's width as a fraction of the spread, taken inward from the edge the way
    TMP's does.

NOT MODELLED, and stated so nobody mistakes this for the shipped picture:
  * the glyph SHAPES. The board uses the game's harvested MarcellusSC face; this uses
    whatever serif the machine has. Letterforms are not what is in question.
  * TMP's exact smoothstep width per glyph (it scales the softness by a per-glyph gradient
    scale). A fixed 1.4-texel edge is used instead, which is the right order.
  * `BoardLit`'s shading of the board UNDER the text. The plate crop is the material's own
    albedo, i.e. the flat-lit case. A real board is shaded on top of this, which scales
    every term together and cannot change which of them is darker.

THE OUTCOME IT REPORTS is the one a player experiences: is the glyph body DARKER than the
board around it (a cut), and is the lit lip a thin bright edge rather than a second glyph?
Both are printed as numbers next to the picture, so the answer is not left to the eye.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import tex_common as T          # noqa: E402

# ---- verbatim from src/GloomhavenVR/Cards/BoardEngraving.cs ---------------------------
GROOVE = {"oak": (0.260, 0.192, 0.129),
          "steel": (0.188, 0.178, 0.174),
          "bronze": (0.201, 0.177, 0.118)}
LIT_LIP = {"oak": (0.780, 0.577, 0.387),
           "steel": (0.565, 0.535, 0.522),
           "bronze": (0.604, 0.531, 0.353)}
KEYLINE = {"oak": (0.127, 0.094, 0.063),
           "steel": (0.092, 0.087, 0.085),
           "bronze": (0.098, 0.087, 0.057)}
KEYLINE_WIDTH = 0.09       # fraction of the SDF spread, taken inward from the edge
LIP_OFFSET = (-0.28, -0.28)  # SDF-spread units; negative Y = DOWN in TMP's frame
LIP_SOFTNESS = 0.14
LIP_ALPHA = 0.85

# The SDF spread the compositing is expressed in, in texels of this preview. TMP's own
# spread is a font-asset property; what matters is the RATIO of the offsets and widths to
# it, and those are the numbers above.
SPREAD_PX = 26.0
EDGE_SOFT_PX = 1.4         # the face edge's own antialiasing


def font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf",
              "/usr/share/fonts/truetype/liberation/LiberationSerif-Bold.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.isfile(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def glyph_sdf(text, w, h, size):
    """Signed distance to the text's outline, in texels: positive INSIDE."""
    img = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(img)
    f = font(size)
    bbox = d.textbbox((0, 0), text, font=f)
    d.text(((w - (bbox[2] - bbox[0])) // 2 - bbox[0],
            (h - (bbox[3] - bbox[1])) // 2 - bbox[1]), text, font=f, fill=255)
    m = np.asarray(img) > 127
    if not m.any():
        return np.full((h, w), -1e3)
    return T.edt(~m) - T.edt(m)


def over(dst, src_rgb, src_a):
    """Straight alpha compositing, src over dst."""
    a = src_a[..., None]
    return dst * (1.0 - a) + np.asarray(src_rgb, dtype=np.float64) * a


def composite(plate, sdf, style):
    """The three TMP layers onto the plate, in TMP's own order."""
    def cover(d, soft=EDGE_SOFT_PX):
        return np.clip(d / max(soft, 1e-6) * 0.5 + 0.5, 0.0, 1.0)

    # UNDERLAY: the face's own shape, translated and blurred. TMP draws it BEHIND
    # everything, so only the part the glyph does not cover ends up visible -- which is
    # exactly why it reads as a lip and not as a second letter.
    dx = int(round(LIP_OFFSET[0] * SPREAD_PX))
    dy = int(round(-LIP_OFFSET[1] * SPREAD_PX))   # image y grows DOWN; TMP's +Y is up
    lip = np.roll(np.roll(cover(sdf), dy, axis=0), dx, axis=1)
    blur = max(1, int(round(LIP_SOFTNESS * SPREAD_PX)))
    lip = T.box_blur(lip, blur) * LIP_ALPHA

    # OUTLINE: a ring taken INWARD from the edge, the way TMP's _OutlineWidth is.
    inner = KEYLINE_WIDTH * SPREAD_PX
    ring = cover(sdf) * (1.0 - cover(sdf - inner))

    # FACE: everything inside the outline.
    face = cover(sdf - inner)

    out = plate.copy()
    out = over(out, LIT_LIP[style], lip)
    out = over(out, KEYLINE[style], ring)
    out = over(out, GROOVE[style], face)
    return out, face, lip, ring


def measure(plate, out, face, lip):
    """The two numbers the question turns on."""
    body = face > 0.6
    ring_area = (lip > 0.05) & ~body
    if not body.any():
        return None
    lum_in = out.mean(axis=2)[body].mean()
    # The board immediately AROUND the text, not the whole plate: what the eye compares to.
    near = (~body) & (lip <= 0.05)
    lum_out = plate.mean(axis=2)[near].mean()
    lip_lum = out.mean(axis=2)[ring_area].mean() if ring_area.any() else lum_out
    return dict(body=lum_in, board=lum_out, drop=(lum_out - lum_in) / max(lum_out, 1e-6),
                lip=lip_lum, lip_gain=lip_lum / max(lum_out, 1e-6),
                lip_area=float(ring_area.mean()), body_area=float(body.mean()))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    # THE SURFACE BEING CUT is the BOARD, not a keycap. gen_capref.py wrote each board face
    # band to buttons/out/ref_face_<style>.png; see BoardEngraving.cs for the factor of 0.68
    # that distinction is worth on steel, whose cap is dark iron and whose board is bright.
    ap.add_argument("--plates", default=os.path.join(HERE, "out"))
    ap.add_argument("--out", default=os.path.join(os.path.dirname(HERE), "out",
                                                  "engrave_preview.png"))
    ap.add_argument("--size", type=int, default=150)
    args = ap.parse_args()

    rows = [("oak", "KURZE RAST", "SHORT REST"),
            ("steel", "LANGE RAST", "LONG REST"),
            ("bronze", "RUNDE 12", "ROUND 12")]
    W, H = 760, 260
    sheet = Image.new("RGB", (W * 2 + 30, H * 3 + 40), (26, 26, 28))
    print("style   text          glyph body   board    drop     lit lip   x board   lip area")
    for r, (style, de, en) in enumerate(rows):
        p = np.asarray(Image.open(os.path.join(args.plates, f"ref_face_{style}.png"))
                       .convert("RGB").resize((W, H), Image.LANCZOS), dtype=np.float64) / 255.0
        for c, text in enumerate((de, en)):
            sdf = glyph_sdf(text, W, H, args.size)
            out, face, lip, _ = composite(p, sdf, style)
            m = measure(p, out, face, lip)
            print(f"{style:<7} {text:<13} {m['body']:.3f}       {m['board']:.3f}   "
                  f"{m['drop']*100:+5.1f}%   {m['lip']:.3f}     {m['lip_gain']:.2f}x    "
                  f"{m['lip_area']*100:.1f}%")
            img = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB")
            sheet.paste(img, (10 + c * (W + 10), 10 + r * (H + 10)))
    sheet.save(args.out)
    print(f"\n{args.out}")
    print("A CUT is: glyph body darker than the board (a positive drop), with the lit lip a")
    print("SMALL bright area beside it. Pale text lying on the board would show a negative")
    print("drop, or a lip area comparable to the body's.")


if __name__ == "__main__":
    main()
