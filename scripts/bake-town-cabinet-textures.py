#!/usr/bin/env python3
"""Bake bounded VR cabinet and hand-finished mechanism textures."""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--raw-albedo", type=Path, required=True)
parser.add_argument("--packed-metal-roughness", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)

raw = np.asarray(Image.open(args.raw_albedo).convert("RGB"), dtype=np.float32)
tint = np.array([.72, .63, .56], dtype=np.float32)
Image.fromarray(np.clip(raw * tint, 0, 255).astype(np.uint8), "RGB").save(
    args.output / "merchant_cabinet_albedo.png", optimize=True)
packed = np.asarray(Image.open(args.packed_metal_roughness).convert("RGB"))
unity = np.empty((*packed.shape[:2], 4), dtype=np.uint8)
unity[:,:,0] = packed[:,:,2]  # glTF blue: metallic
unity[:,:,1:3] = 0
unity[:,:,3] = 255 - packed[:,:,1]  # glTF green: roughness
Image.fromarray(unity, "RGBA").save(
    args.output / "merchant_cabinet_metallic_smoothness.png", optimize=True)

# Cabinet controls retain their independent moving meshes. This small, tiling
# atlas supplies grain, shallow hammer dents and tarnish without a separate
# large material per button or category icon.
size = 512
rng = np.random.default_rng(571)
def field(width, height):
    small = rng.integers(0, 256, (height, width), dtype=np.uint8)
    image = Image.fromarray(small, "L").resize((size,size), Image.Resampling.BICUBIC)
    return np.asarray(image.filter(ImageFilter.GaussianBlur(1.5)), dtype=np.float32) / 255 - .5

yy,xx = np.mgrid[:size,:size]
grain = .52*field(12,64) + .25*field(60,250) + .10*rng.normal(size=(size,size))
grain += .12*np.sin(2*np.pi*(xx/19 + .012*np.sin(yy/37)))
dent = .65*field(40,40) + .20*field(170,170)
for name, base, variation in (
    ("wood", (87,48,26), grain*48),
    ("brass", (110,78,34), dent*39),
    ("iron", (43,39,34), dent*28)):
    if name != "wood":
        # Sparse forged scratches run across the hammered surface.
        scratches = (rng.random((size,size)) > .998).astype(np.float32)
        variation = variation + scratches * 24
    color = np.clip(np.array(base, dtype=np.float32)[None,None,:] +
                    variation[:,:,None] * np.array([1,.8,.65]), 4, 245).astype(np.uint8)
    Image.fromarray(color,"RGB").save(args.output / f"merchant_hardware_{name}.png", optimize=True)
    height = variation.astype(np.float32)
    gx = np.gradient(height,axis=1)
    gy = np.gradient(height,axis=0)
    normal = np.stack((-gx*.018, -gy*.018, np.ones_like(height)),axis=2)
    normal /= np.maximum(1e-6,np.linalg.norm(normal,axis=2,keepdims=True))
    normal = ((normal*.5+.5)*255).astype(np.uint8)
    Image.fromarray(normal,"RGB").save(args.output / f"merchant_hardware_{name}_normal.png", optimize=True)

print("CABINET_TEXTURES", sorted(p.name for p in args.output.glob("merchant_*")))
