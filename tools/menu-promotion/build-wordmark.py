#!/usr/bin/env python3
"""Build the FrosthavenVR volume outline from the supplied transparent wordmark."""
import argparse
from itertools import groupby
from pathlib import Path
from struct import pack

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src/GloomhavenVR/Assets/MenuPromotion"
WIDTH = 512
CORE_ALPHA = 190


def segments(values):
    cursor = 0
    for value, group in groupby(values):
        end = cursor + sum(1 for _ in group)
        if value:
            yield cursor, end, value
        cursor = end


def rear_rectangles(rows):
    # Merge equal neighboring scanline runs, keeping letter counters empty.
    active = {}
    result = []
    for y, row in enumerate([*rows, [False] * len(rows[0])]):
        spans = {(left, right) for left, right, _ in segments(row)}
        for left, right in sorted(active.keys() - spans):
            result.append((left, active.pop((left, right)), right, y))
        for span in sorted(spans):
            active.setdefault(span, y)
    return result


def boundary_edges(rows):
    result = []
    for transpose in (False, True):
        grid = list(zip(*rows)) if transpose else rows
        empty = (False,) * len(grid[0])
        for plane, (before, after) in enumerate(zip([empty, *grid], [*grid, empty])):
            differences = (int(a) - int(b) for a, b in zip(after, before))
            for start, end, sign in segments(differences):
                # Orient the edges so the solid lies to their left in logo space.
                a, b = ((plane, start), (plane, end)) if transpose else ((start, plane), (end, plane))
                if (sign > 0) == transpose:
                    a, b = b, a
                result.append((*a, *b))
    return result


def build(source):
    with Image.open(source) as image:
        height = round(image.height * WIDTH / image.width)
        alpha = image.getchannel("A").resize((WIDTH, height), Image.Resampling.LANCZOS)
    alpha = alpha.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    pixels = alpha.tobytes()
    rows = [[value >= CORE_ALPHA for value in pixels[start:start + WIDTH]]
            for start in range(0, len(pixels), WIDTH)]
    rear, sides = rear_rectangles(rows), boundary_edges(rows)
    # Each record is exactly four uint16 coordinates in the shared grid.
    header = pack("<4sHHHH", b"FWM1", WIDTH, height, len(rear), len(sides))
    payload = header + b"".join(pack("<HHHH", *quad) for quad in [*rear, *sides])
    return payload, (WIDTH, height), len(rear), len(sides)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ASSETS / "FrosthavenVR_logo.png")
    parser.add_argument("--output", type=Path, default=ASSETS / "FrosthavenVR_logo.fwm")
    args = parser.parse_args()
    payload, grid, rear, sides = build(args.source)
    args.output.write_bytes(payload)
    print(f"{grid[0]}×{grid[1]} grid, {rear} rear quads, {sides} wall quads, {len(payload)} bytes")


if __name__ == "__main__":
    main()
