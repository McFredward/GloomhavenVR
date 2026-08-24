"""Compose every README image that ships: the asset strips, the two logo copies, the video posters.

Everything written here is OPAQUE. The logo fault the user reported ("weiße Lücken", GitHub only,
correct locally and in game) could not be reproduced from the file — its alpha is clean, its
transparent pixels are black, it composites correctly on white and on #0d1117 — so rather than
assert a cause I cannot observe, this removes the variable: no alpha reaches GitHub at all.
"""
import os, subprocess
from PIL import Image, ImageDraw

T = os.environ.get('ASSET_RENDER_DIR', 'render/')
OUT = os.environ.get('README_IMG_DIR', 'docs/img/')
BACKDROP = (26, 22, 19)          # warm near-black; reads as deliberate on both GitHub themes
LIGHT = (255, 255, 255)          # GitHub light canvas
DARK = (13, 17, 23)              # GitHub dark canvas


def strip(names, out, width=1280, pad=0.06, backdrop=BACKDROP):
    """One row of transparent renders, flattened onto an opaque backdrop at a common height."""
    ims = [Image.open(T + n + '.png').convert('RGBA') for n in names]
    # Trim each render to its own ink so the padding is even, then scale to a common height.
    trimmed = []
    for im in ims:
        bb = im.getbbox()
        trimmed.append(im.crop(bb) if bb else im)
    h = max(i.height for i in trimmed)
    cell = width // len(trimmed)
    inner = int(cell * (1 - 2 * pad))
    scaled = []
    for im in trimmed:
        f = min(inner / im.width, (h * (1 - 2 * pad)) / im.height)
        scaled.append(im.resize((max(1, int(im.width * f)), max(1, int(im.height * f))),
                                Image.LANCZOS))
    ch = max(i.height for i in scaled)
    height = int(ch / (1 - 2 * pad))
    canvas = Image.new('RGBA', (width, height), backdrop + (255,))
    for k, im in enumerate(scaled):
        x = k * cell + (cell - im.width) // 2
        y = (height - im.height) // 2
        canvas.alpha_composite(im, (x, y))
    canvas.convert('RGB').save(OUT + out, optimize=True)
    print('wrote', out, canvas.size)


def logo():
    src = Image.open('src/GloomhavenVR/Assets/GloomhavenVR_logo.png')
    src = src.convert('RGBA')
    for name, bg in (('logo-light.png', LIGHT), ('logo-dark.png', DARK)):
        c = Image.new('RGBA', src.size, bg + (255,))
        c.alpha_composite(src)
        c.convert('RGB').save(OUT + name, optimize=True)
        print('wrote', name, c.size)


def poster(mp4, out, at='0:04'):
    tmp = os.path.join(T, 'poster_raw.png')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', at, '-i', OUT + mp4,
                    '-frames:v', '1', tmp], check=True)
    im = Image.open(tmp).convert('RGB')
    d = ImageDraw.Draw(im, 'RGBA')
    w, h = im.size
    r = int(min(w, h) * 0.11)
    cx, cy = w // 2, h // 2
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(0, 0, 0, 130),
              outline=(255, 255, 255, 220), width=max(2, r // 22))
    t = int(r * 0.52)
    d.polygon([(cx - t * 0.55, cy - t), (cx - t * 0.55, cy + t), (cx + t * 0.95, cy)],
              fill=(255, 255, 255, 235))
    im.save(OUT + out, quality=88, optimize=True)
    print('wrote', out, im.size)


os.makedirs(OUT, exist_ok=True)
strip(['hand_glove', 'hand_plate', 'hand_arcane'], 'styles-hands.png')
strip(['mask_0', 'mask_1', 'mask_2'], 'styles-masks.png')
strip(['board_oak', 'board_steel', 'board_bronze'], 'styles-boards.png', pad=0.045)
logo()
poster('card-fan.mp4', 'card-fan-poster.jpg', '0:05')
poster('figure-grab.mp4', 'figure-grab-poster.jpg', '0:06')
