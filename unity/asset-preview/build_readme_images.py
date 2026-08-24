"""Compose every README image that ships: the asset strips, the two logo copies, the video posters.

The logo's history is in logo() below and it is worth reading before touching it — three rounds
were spent looking for a file or an encoding fault that was never there.
"""
import os, subprocess
from PIL import Image, ImageDraw, ImageFont

T = os.environ.get('ASSET_RENDER_DIR', 'render/')
OUT = os.environ.get('README_IMG_DIR', 'docs/img/')
PLATE = (20, 17, 14)             # light-theme backing only — the tone the artwork expects
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


def styles_sheet(rows, out, width=1280, row_h=250):
    """The three asset strips as ONE image.

    Three separate images plus three caption lines is a lot of page for "there are nine of these",
    and the user's standing note on the README is that it must stay short enough to be read. Each
    row is its own strip, trimmed to its ink so the strips' own padding does not stack up, scaled
    to a common row height, with the family name at the left and the three variant names under it.
    """
    lab = _sheet_font(21, bold=True)
    sub = _sheet_font(16)
    pad, col = 16, 235            # col = the left label column, so the art fills what is left
    avail = width - col - pad
    tiles = []
    for src, _, _ in rows:
        im = Image.open(OUT + src).convert('RGB')
        im = im.crop(_ink_box(im))
        f = min(avail / im.width, row_h / im.height)   # fit BOTH, or the art leaves a dead margin
        tiles.append(im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))),
                               Image.LANCZOS))
    hs = [t.height for t in tiles]
    canvas = Image.new('RGB', (width, sum(hs) + pad * (len(rows) + 1)), BACKDROP)
    d = ImageDraw.Draw(canvas)
    y = pad
    for im, (_, name, variants) in zip(tiles, rows):
        canvas.paste(im, (col + (avail - im.width) // 2, y))
        cy = y + im.height // 2 - 24
        d.text((16, cy), name.upper(), font=lab, fill=(238, 230, 216))
        d.text((16, cy + 27), variants, font=sub, fill=(158, 148, 136))
        y += im.height + pad
    canvas.save(OUT + out, quality=92, optimize=True)
    print('wrote', out, canvas.size)


def _ink_box(im, thresh=45):   # > BACKDROP (26,22,19), or nothing is ever trimmed
    import numpy as np
    a = np.asarray(im).max(axis=2)
    ys, xs = np.where(a > thresh)
    return (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1) if len(xs) else (0, 0, im.width, im.height)


def _sheet_font(px, bold=False):
    names = (('DejaVuSans-Bold.ttf',) if bold else ('DejaVuSans.ttf',))
    for dd in ('/usr/share/fonts/truetype/dejavu/', '/usr/share/fonts/truetype/liberation/'):
        for n in names:
            if os.path.exists(dd + n):
                return ImageFont.truetype(dd + n, px)
    return ImageFont.load_default()


def logo():
    """TWO copies, and this time the reason is measured rather than guessed.

    THE WORDMARK IS DRAWN FOR A DARK BACKGROUND. Its letter fill and its outer bevel are a light
    parchment tone: against black they read as lit metal, against white they lose almost all their
    contrast and the letters look hollow. That is the user's "weisse Luecken", and it is the
    artwork, not the file — PROVEN, after he asked a third time why he only ever sees it on GitHub:
    his GitHub screenshot was cropped to its ink box, scaled to ours and differenced against the
    artist's original composited on white. MEDIAN DIFFERENCE 2/255, mean 6.8, only 2.9 % of pixels
    off by more than 40 — i.e. screenshot noise. GitHub renders our file correctly. GitHub's LIGHT
    THEME is simply the only place this wordmark is ever put on white; the game menu and every
    local viewer put it on dark.

    So: `logo.png` is TRANSPARENT, which is what the user asked for and what he actually sees,
    because he reads GitHub in dark mode. `logo-onlight.png` is the same artwork on a dark plate
    and is served ONLY to `prefers-color-scheme: light`, where the transparent one breaks. Neither
    repaints a pixel of the artist's wordmark, which is the one thing that must not happen.
    """
    src = Image.open('src/GloomhavenVR/Assets/GloomhavenVR_logo.png').convert('RGBA')
    W = 1280
    for name, plate, pad_x, pad_y in (('logo.png', None, 0.03, 0.10),
                                      ('logo-onlight.png', PLATE, 0.06, 0.24)):
        inner = int(W * (1 - 2 * pad_x))
        f = inner / src.width
        mark = src.resize((inner, max(1, round(src.height * f))), Image.LANCZOS)
        H = round(mark.height * (1 + 2 * pad_y))
        out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        if plate:
            ImageDraw.Draw(out).rounded_rectangle((0, 0, W - 1, H - 1), radius=round(H * 0.12),
                                                  fill=plate + (255,))
        out.alpha_composite(mark, ((W - mark.width) // 2, (H - mark.height) // 2))
        out.save(OUT + name, optimize=True)
        print('wrote', name, out.size, 'plated' if plate else 'transparent')


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
styles_sheet([('styles-hands.png',  'Hands',  'Leather glove  ·  Plate gauntlet  ·  Arcane glove'),
              ('styles-masks.png',  'Masks',  'Ironwatch  ·  Runeveil  ·  Grimhorn'),
              ('styles-boards.png', 'Boards', 'Oak  ·  Steel  ·  Bronze')], 'styles.png')
logo()
poster('card-fan.mp4', 'card-fan-poster.jpg', '0:05')
poster('figure-grab.mp4', 'figure-grab-poster.jpg', '0:06')
