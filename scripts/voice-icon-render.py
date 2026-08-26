#!/usr/bin/env python3
"""Convert the voice-badge frames the WIRE TESTS rasterised into viewable PNGs.

The .ppm inputs come out of tests/GloomhavenVR.WireTests/VoiceVectors.cs, which links
src/GloomhavenVR/Voice/VoiceIcon.cs verbatim -- so these pictures are produced BY THE SHIPPED
RASTERISER and not by a lookalike. This script only scales and labels them; it draws nothing.

The frames are composited over mid grey by the test itself, because the badge is drawn over
whatever the room happens to be and its dark contrast rim would be invisible over black.

Usage:  python3 scripts/voice-icon-render.py [outdir]     (default .planning/voice)
"""
import os
import sys

from PIL import Image, ImageDraw

SCALE = 192


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))), ".planning", "voice")

    frames = []
    for step in range(4):
        src = os.path.join(out, f"voice-icon-step{step}.ppm")
        if not os.path.exists(src):
            print(f"missing {src} -- run scripts/wire-tests.sh first", file=sys.stderr)
            return 1
        im = Image.open(src).convert("RGB").resize((SCALE, SCALE), Image.NEAREST)
        im.save(os.path.join(out, f"voice-icon-step{step}.png"))
        frames.append(im)

    # A contact sheet, plus an "off" column -- the state the user asked to be able to switch to,
    # and the one a render of only the lit frames would quietly omit.
    pad, top = 10, 34
    cols = len(frames) + 1
    sheet = Image.new("RGB", (cols * (SCALE + pad) + pad, SCALE + top + 30), (52, 52, 56))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 10), "GloomhavenVR voice badge -- rasterised by the shipped VoiceIcon, "
                      "shown over mid grey at 3x", fill=(235, 235, 235))

    labels = ["step 0  silent", "step 1  quiet", "step 2  talking", "step 3  loud"]
    for i, im in enumerate(frames):
        x = pad + i * (SCALE + pad)
        sheet.paste(im, (x, top))
        d.text((x + 4, top + SCALE + 8), labels[i], fill=(200, 200, 200))

    # OFF: [Voice] SpeakingBadge = false, or the peer is not talking. Nothing is drawn at all --
    # not a dimmed glyph, not a placeholder. The picture has to show that, or "deaktivierbar"
    # is a claim with no evidence behind it.
    x = pad + len(frames) * (SCALE + pad)
    d.rectangle([x, top, x + SCALE, top + SCALE], fill=(128, 128, 128))
    d.text((x + 4, top + SCALE + 8), "off / not speaking", fill=(200, 200, 200))
    d.text((x + 26, top + SCALE // 2 - 6), "(nothing drawn)", fill=(96, 96, 96))

    path = os.path.join(out, "voice-icon-sheet.png")
    sheet.save(path)
    print(f"wrote {path} and {len(frames)} individual frames")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
