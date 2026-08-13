#!/usr/bin/env python3
"""GloomhavenVR — derive Imported/Textures/cobweb_alb.png from its CC0 source.

Editor-only (like polyhaven_pipeline.py and star_catalogue.py): this file never
enters the player build or the asset bundle. Only the derived PNG ships.

SOURCE (see Bundle/Environments/License.md for the licence trail):
    TextureCan "Spider Web / Cobweb", asset id others_0015, CC0 1.0
    https://www.texturecan.com/details/237/
    4k zip -> others_0015_opacity_4k.jpg  (4096x4096, 8-bit grayscale)
             others_0015_color_4k.jpg     (4096x4096, RGB)

WHY EACH STEP EXISTS
  * The source opacity map PEAKS AT 114/255, not 255 — used raw the web is
    nearly invisible. Everything is normalised by that peak first.
  * It is a JPEG and the threads are ~1 px, so there is ringing around every
    thread. A 5.5% floor is subtracted and the rest re-normalised; without it
    the alpha test speckles.
  * 4096 -> 1024 by AREA MEAN, which is exactly coverage, then a x3.0 gain to
    put the threads back at full opacity. A naive resize halves the coverage at
    the cutoff and the web thins out; max-pooling instead aliases it.
  * RGB is NOT the source colour map: at this size its detail is invisible and
    it only fights the material tint. It is the colour map's own luminance,
    blurred to 14 px and mapped to 0.72..1.00 — large-scale "this part of the
    web is dustier" variation and nothing else.

Run:  python3 cobweb_pipeline.py <dir with the two source jpgs> <out.png>
Needs: python3, numpy, Pillow.
"""
import sys
import os
import numpy as np
from PIL import Image, ImageFilter

N = 1024          # shipped size; see EnvRoomBuilder.AlbSize["cobweb"]
PEAK = 114.0      # measured maximum of the source opacity map
FLOOR = 0.055     # JPEG ringing floor
GAIN = 3.0        # coverage restoration after the 4x area downsample


def main(src_dir: str, out_path: str) -> None:
    op = Image.open(os.path.join(src_dir, "others_0015_opacity_4k.jpg")).convert("L")
    if op.size != (4 * N, 4 * N):
        raise SystemExit(f"expected {4 * N}x{4 * N} opacity map, got {op.size}")
    a = np.asarray(op).astype(np.float32) / PEAK
    a = np.clip((a - FLOOR) / (1.0 - FLOOR), 0.0, 1.0)
    a = np.clip(a.reshape(N, 4, N, 4).mean(axis=(1, 3)) * GAIN, 0.0, 1.0)

    col = Image.open(os.path.join(src_dir, "others_0015_color_4k.jpg")).convert("L")
    col = col.resize((N, N), Image.BOX).filter(ImageFilter.GaussianBlur(14))
    c = np.asarray(col).astype(np.float32)
    c = (c - c.min()) / max(c.max() - c.min(), 1e-6)
    rgb = 0.72 + 0.28 * c

    out = np.zeros((N, N, 4), np.uint8)
    for k in range(3):
        out[:, :, k] = np.round(np.clip(rgb, 0, 1) * 255).astype(np.uint8)
    out[:, :, 3] = np.round(a * 255).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(out_path, optimize=True)
    cov = float((a > 0.12).mean())   # 0.12 == EnvironmentsBuilder.WebCutoff
    print(f"wrote {out_path} {N}x{N} RGBA; coverage above the alpha-test "
          f"cutoff = {cov * 100:.1f}% (source was 11.4%)")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
