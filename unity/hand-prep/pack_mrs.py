"""Pack an artist's separate Metallic and Roughness maps into the one texture BoardLit reads.

    python3 unity/hand-prep/pack_mrs.py <metallic.png> <roughness.png> <out.png>

BoardLit's `_MRSMap` is R = metallic, G = roughness, B unused, no alpha. Two greyscale PNGs
delivered separately are two 2048² textures in the bundle for two channels of data; packed, they
are one, and the shader does one sample instead of two in a per-pixel branch.

THE OUTPUT IS LINEAR DATA, NOT COLOUR. Whatever imports it must have sRGB OFF — a metallic map
read through the sRGB curve is wrong everywhere except at 0 and 1, and it is wrong SILENTLY: the
texture samples, the highlight appears, and every intermediate value is off. `BuildHands` forces
that on the importer; this script only writes the pixels.

Both inputs are asserted to be actually greyscale and the same size, because "the artist sent a
coloured roughness map" and "the two maps are different resolutions" are both things that would
otherwise be discovered as a look, not as an error.
"""
import sys
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
met_path, rough_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
met = Image.open(met_path).convert('RGB')
rough = Image.open(rough_path).convert('RGB')

if met.size != rough.size:
    raise SystemExit(f"metallic {met.size} != roughness {rough.size} — they must share the UVs")
for name, im in (('metallic', met), ('roughness', rough)):
    r, g, b = im.split()
    if list(r.getdata()) != list(g.getdata()) or list(g.getdata()) != list(b.getdata()):
        raise SystemExit(f"{name} is not greyscale — R, G and B differ, so which one is the data?")

zero = Image.new('L', met.size, 0)
Image.merge('RGB', (met.split()[0], rough.split()[0], zero)).save(out_path, optimize=True)
mm = sum(met.split()[0].getdata()) / (met.size[0] * met.size[1]) / 255.0
rr = sum(rough.split()[0].getdata()) / (rough.size[0] * rough.size[1]) / 255.0
print(f"wrote {out_path} {met.size}  mean metallic {mm:.3f}  mean roughness {rr:.3f}")
