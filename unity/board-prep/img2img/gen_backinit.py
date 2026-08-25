#!/usr/bin/env python3
"""Build the INIT FRAME for a board's BACK PLATE, for an img2img pass.

    python3 gen_backinit.py --dump <style.npz> --style <oak|steel|bronze> \
                            --albedo <front_albedo.png> --geo <geobufdir> --out <dir>

WHAT AN INIT FRAME HAS TO DO HERE, AND WHAT IT MUST NOT DO
----------------------------------------------------------
The front lane's init frame was a flat-on AMBIENT render of the real board, and it worked
because the front face is covered in geometry the model had to respect.  The BACK PLATE has
almost none: it is a flat slab inside a rounded-rectangle silhouette.  So the init frame's
whole job is to fix three things and leave everything else to the prompt:

  * the SILHOUETTE, rasterised from the mesh's own back triangles, so the model paints the
    board's real outline and not a rectangle with different corners;
  * the PROPORTION, 2:1, which is what the plate actually is;
  * the BASE COLOUR, taken as the median of the style's own front plate, so the back cannot
    come back a different material from the front of the same object.

It is deliberately AMBIENT and almost featureless.  A baked directional key would be lit a
second time by BoardLit, and any painted structure here would be structure the model then
has to argue with.

THE 3:2 TRAP, inherited from the front lane and still live
----------------------------------------------------------
The tool's aspect enum has no 2:1 and it silently RESCALES a frame it is given into the
nearest supported ratio -- the first steel generation of the previous round came back at
1.809:1, which is exactly 16/9 divided by 3/2.  So the plate is drawn at its true 2:1 and
then PADDED to 3:2 with black, and the pad is cropped off again after generation by
`crop_back()`.  The pad fractions are written to `backinit_<style>.json` so the crop is
arithmetic rather than a search for the board's edges in the model's output.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

# 2:1 is the plate; the generation is done at 3:2 and cropped back (see the docstring).
PLATE_W, PLATE_H = 2048, 1024
GEN_W, GEN_H = 2048, 1365          # 3:2 within the model's own size ladder


def srgb2lin(x):
    x = np.asarray(x, np.float64) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def lin2srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055) * 255.0


def back_silhouette(dump, w=PLATE_W, h=PLATE_H):
    """Rasterise the BACK triangles in BOARD XY -> (coverage mask, extent)."""
    from gen_geobuf import classify
    d = np.load(dump)
    pos, nrm = d["pos"], d["nrm"]
    grp, thin = classify(pos, nrm)
    ax = [i for i in range(3) if i != thin]
    sel = grp == 2
    P = pos[sel][:, :, ax]                       # (T, 3, 2) in board metres
    flat = pos.reshape(-1, 3)[:, ax]
    lo, hi = flat.min(0), flat.max(0)
    span = hi - lo
    Q = np.empty_like(P)
    Q[..., 0] = (P[..., 0] - lo[0]) / span[0] * w
    Q[..., 1] = (1.0 - (P[..., 1] - lo[1]) / span[1]) * h
    mask = np.zeros((h, w), bool)
    for t in Q:
        x0 = max(int(np.floor(t[:, 0].min())), 0); x1 = min(int(np.ceil(t[:, 0].max())) + 1, w)
        y0 = max(int(np.floor(t[:, 1].min())), 0); y1 = min(int(np.ceil(t[:, 1].max())) + 1, h)
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        det = ((t[1, 1] - t[2, 1]) * (t[0, 0] - t[2, 0]) +
               (t[2, 0] - t[1, 0]) * (t[0, 1] - t[2, 1]))
        if abs(det) < 1e-12:
            continue
        l0 = ((t[1, 1] - t[2, 1]) * (X - t[2, 0]) + (t[2, 0] - t[1, 0]) * (Y - t[2, 1])) / det
        l1 = ((t[2, 1] - t[0, 1]) * (X - t[2, 0]) + (t[0, 0] - t[2, 0]) * (Y - t[2, 1])) / det
        l2 = 1.0 - l0 - l1
        mask[y0:y1, x0:x1] |= (l0 >= -0.002) & (l1 >= -0.002) & (l2 >= -0.002)
    return mask, (lo, hi)


def plate_colour(albedo_path, geodir):
    """Median linear colour of the style's own FRONT plate.

    Median over the FRONT group only, and in LINEAR light: the mean of an sRGB atlas is
    biased by the dark carved shadows, and those are not what the plate is made of."""
    grp = np.load(os.path.join(geodir, "grp.npy"))
    a = np.asarray(Image.open(albedo_path).convert("RGB"))
    lin = srgb2lin(a)
    sel = grp == 1
    return np.median(lin[sel], axis=0)


def build(style, dump, albedo, geodir, out):
    os.makedirs(out, exist_ok=True)
    mask, _ = back_silhouette(dump)
    base = plate_colour(albedo, geodir)

    img = np.zeros((PLATE_H, PLATE_W, 3), np.float64)
    img[mask] = base

    # A soft inward falloff so the plate is not a perfectly flat fill: it gives the model
    # the plate's own edge to hold on to without dictating any structure inside it.
    m = Image.fromarray((mask * 255).astype(np.uint8))
    near = np.asarray(m.filter(ImageFilter.GaussianBlur(26)), np.float64) / 255.0
    img *= (0.80 + 0.20 * near)[..., None]

    rgb = np.clip(lin2srgb(img), 0, 255).astype(np.uint8)
    plate = Image.fromarray(rgb)
    plate.save(os.path.join(out, f"backplate_{style}.png"))

    pad = Image.new("RGB", (GEN_W, GEN_H), (0, 0, 0))
    oy = (GEN_H - PLATE_H) // 2
    pad.paste(plate, (0, oy))
    pad.save(os.path.join(out, f"backinit_{style}.png"))

    meta = dict(style=style, plate=[PLATE_W, PLATE_H], gen=[GEN_W, GEN_H],
                offset=[0, oy], coverage=float(mask.mean()),
                base_srgb=[int(v) for v in np.clip(lin2srgb(base), 0, 255).round()])
    with open(os.path.join(out, f"backinit_{style}.json"), "w") as fh:
        json.dump(meta, fh, indent=1)
    print(f"{style}: silhouette covers {mask.mean()*100:.1f}% of the 2:1 plate, "
          f"base sRGB {meta['base_srgb']}, padded to {GEN_W}x{GEN_H} at y={oy}")
    return meta


def crop_back(gen_path, meta_path):
    """Undo the 3:2 pad: return the generated art at the plate's own 2:1."""
    with open(meta_path) as fh:
        meta = json.load(fh)
    im = Image.open(gen_path).convert("RGB")
    gw, gh = meta["gen"]
    if im.size != (gw, gh):
        im = im.resize((gw, gh), Image.LANCZOS)
    ox, oy = meta["offset"]
    pw, ph = meta["plate"]
    return im.crop((ox, oy, ox + pw, oy + ph))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--style", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    build(a.style, a.dump, a.albedo, a.geo, a.out)


if __name__ == "__main__":
    main()
