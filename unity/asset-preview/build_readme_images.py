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
    """The wordmark, TRANSPARENT, with a little breathing room. User ruling, after ModBuild 248.

    HISTORY, so nobody re-derives this a fourth time. The "weisse Luecken" report was accurate and
    was NEVER a file defect: the shipped copies were byte-exact against the artist's artwork on
    both canvases, max difference 0 on every channel of every pixel. The letter interiors of
    GLOOMHAVEN are a light PARCHMENT tone — against black they read as lit metal, against white as
    holes. It looked like a GitHub bug only because GitHub's light theme was the one place the
    wordmark was ever put on a white page.

    ModBuild 248 answered that with a dark plate. The user rejected the plate ("ich will es
    transparent, das nur das logo ohne hintergrund sichtbar ist"), so this is the artwork itself
    with an alpha channel and nothing behind it. THE CONSEQUENCE IS ACCEPTED, NOT FORGOTTEN: on
    GitHub's light theme the pale fill will read as gaps again, because that is what this artwork
    does on white. If that ever has to be solved, the answer is a BACKING behind it on that theme
    only — never a repaint of the letter fill.
    """
    src = Image.open('src/GloomhavenVR/Assets/GloomhavenVR_logo.png').convert('RGBA')
    W, pad_x, pad_y = 1280, 0.03, 0.10
    inner = int(W * (1 - 2 * pad_x))
    f = inner / src.width
    mark = src.resize((inner, max(1, round(src.height * f))), Image.LANCZOS)
    H = round(mark.height * (1 + 2 * pad_y))
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    out.alpha_composite(mark, ((W - mark.width) // 2, (H - mark.height) // 2))
    out.save(OUT + 'logo.png', optimize=True)
    print('wrote logo.png', out.size, 'transparent')


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
