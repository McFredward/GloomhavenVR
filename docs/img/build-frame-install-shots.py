"""Crop and stack the eight original Steam Frame install screenshots.

Usage: python3 docs/img/build-frame-install-shots.py .planning/debug/frame_anleitung
The source directory is gitignored hardware evidence in the main checkout. Pass its absolute
path when running from a worktree. The output lives beside this script in frame-install/.
"""

import sys
from pathlib import Path
from PIL import Image

if len(sys.argv) != 2:
    raise SystemExit(f'usage: python3 {sys.argv[0]} FRAME_SCREENSHOT_DIRECTORY')
source = Path(sys.argv[1])
output = Path(__file__).resolve().parent / 'frame-install'
output.mkdir(parents=True, exist_ok=True)

plates = {
    '01-desktop.jpg': [
        ('20260930223825_1.jpg', (550, 570, 1330, 990)),
        ('20260930223804_1.jpg', (610, 315, 1390, 800)),
    ],
    '02-open-zip.jpg': [
        ('20260930223949_1.jpg', (610, 475, 1320, 950)),
    ],
    '03-bepinex-copy.jpg': [
        ('20260930224014_1.jpg', (320, 420, 1380, 980)),
        ('20260930224026_1.jpg', (540, 230, 1550, 830)),
    ],
    '04-mod-copy.jpg': [
        ('20260930224048_1.jpg', (445, 375, 1430, 1000)),
        ('20260930224051_1.jpg', (640, 345, 1640, 990)),
    ],
    '05-setup.jpg': [
        ('20260930224110_1.jpg', (685, 495, 1325, 965)),
    ],
}

for name, frames in plates.items():
    crops = [Image.open(source / filename).crop(box).convert('RGB') for filename, box in frames]
    gap = 16
    width = max(crop.width for crop in crops)
    height = sum(crop.height for crop in crops) + gap * (len(crops) - 1)
    result = Image.new('RGB', (width, height), '#ffffff')
    top = 0
    for crop in crops:
        result.paste(crop, ((width - crop.width) // 2, top))
        top += crop.height + gap
    result.save(output / name, quality=90, optimize=True, subsampling=0)
    print(name, result.size, (output / name).stat().st_size)
