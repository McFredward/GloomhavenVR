#!/usr/bin/env python3
"""Compose 320x240 board-picker thumbnails from the committed transparent asset renders.

Run build_asset_strips.sh to re-render changed board models/materials first. Its board recipe
uses the shipped FBX/albedo/normal maps, double-sided material and one common camera scale.
This step trims transparent margins and applies one shared resize factor to all three renders;
no geometry, shading, proportions or per-board brightness changes are introduced.

Usage: python3 unity/asset-preview/build_board_tiles.py [render-directory]
Requires Pillow. The default input is the tracked asset-preview/render directory.
"""
from pathlib import Path
import sys

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
RENDERS = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "render"
OUT = ROOT / "src/GloomhavenVR/WorldUI/Options/VariantTiles"
NAMES = ("oak", "steel", "bronze")
SIZE = (320, 240)
PADDING = 12


def main():
    renders = []
    for name in NAMES:
        with Image.open(RENDERS / f"board_{name}.png") as source:
            image = source.convert("RGBA")
        bounds = image.getchannel("A").getbbox()
        if bounds is None:
            raise ValueError(f"Empty board render: {name}")
        renders.append(image.crop(bounds))
    scale = min(min((SIZE[0] - 2 * PADDING) / image.width,
                    (SIZE[1] - 2 * PADDING) / image.height) for image in renders)
    OUT.mkdir(parents=True, exist_ok=True)
    for name, image in zip(NAMES, renders):
        image = image.resize((round(image.width * scale), round(image.height * scale)),
                             Image.Resampling.LANCZOS)
        tile = Image.new("RGBA", SIZE, (18, 17, 15, 255))
        tile.alpha_composite(image, ((SIZE[0] - image.width) // 2, (SIZE[1] - image.height) // 2))
        path = OUT / f"tile_board_{name}.png"
        tile.convert("RGB").save(path, optimize=True)
        print(f"{path.relative_to(ROOT)}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
