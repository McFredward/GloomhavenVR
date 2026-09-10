#!/usr/bin/env python3
"""Render the documentation marker from the original glyph and the game's resting blue tint."""
import base64
from pathlib import Path
import re

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SOURCE = ROOT / "src/GloomhavenVR/Assets/net_shared.png"
RUNTIME = ROOT / "src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs"


def build():
    match = re.search(r"Color BadgeTint = new\(([^)]+)\)", RUNTIME.read_text())
    if match is None:
        raise SystemExit("Cannot find the runtime shared-window badge tint.")
    red, green, blue, alpha = [float(value.strip().removesuffix("f"))
                               for value in match.group(1).split(",")]
    if alpha != 1 or blue <= max(red, green):
        raise SystemExit("The runtime badge tint changed; review the documentation marker.")
    with Image.open(SOURCE) as source:
        width, height = source.size
        # The original canvas has faint alpha noise outside the actual two-person glyph.
        # Use visible ink to frame it, while retaining the original pixels and alpha unchanged.
        bounds = source.getchannel("A").point(lambda a: 255 if a >= 16 else 0).getbbox()
    if bounds is None:
        raise SystemExit("The runtime marker has no visible glyph.")
    left, top, right, bottom = bounds
    left, top = max(0, left - 2), max(0, top - 2)
    right, bottom = min(width, right + 2), min(height, bottom + 2)
    encoded = base64.b64encode(SOURCE.read_bytes()).decode("ascii")
    # An SVG wrapper preserves the original artwork. The filter applies the same RGB multiply
    # as the runtime material at the top of its pulse; no replacement glyph is drawn.
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
     width="{right-left}" height="{bottom-top}" viewBox="{left} {top} {right-left} {bottom-top}">
  <title>Blue shared-window marker</title>
  <defs>
    <filter id="runtime-tint" color-interpolation-filters="sRGB">
      <feComponentTransfer>
        <feFuncR type="linear" slope="{red:g}"/>
        <feFuncG type="linear" slope="{green:g}"/>
        <feFuncB type="linear" slope="{blue:g}"/>
      </feComponentTransfer>
    </filter>
  </defs>
  <image width="{width}" height="{height}" filter="url(#runtime-tint)"
         xlink:href="data:image/png;base64,{encoded}"/>
</svg>
'''
    target = HERE / "net-shared.svg"
    target.write_text(svg)
    print(f"Wrote {target.name}: visible frame {right-left} x {bottom-top}, RGB tint {red:g}/{green:g}/{blue:g}")


if __name__ == "__main__":
    build()
