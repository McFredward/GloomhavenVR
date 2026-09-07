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


def styles_matrix(rows, out, width=1280, cell_h=300, bg=(255, 255, 255), ink=(26, 22, 19)):
    """The nine selectable assets as a 3x3 MATRIX on white — one cell per asset, name underneath.

    Three decisions, all the user's:

    * "mach die Assets so dass sie untereinander stehen, wie eine Matrix" — so the cells line up in
      COLUMNS as well as rows. That is why this builds from the nine individual renders rather than
      from the three styles-*.png strips: a strip centres its three items inside its own width, so
      three strips stacked can never line up with each other.
    * "mach den Hintergrund weiss statt schwarz (es soll trotzdem lesbar bleiben)". White it is,
      and the readability caveat is answered by making the LABELS dark — not by touching the
      renders. The arcane glove and Grimhorn are nearly black and read perfectly well on white;
      that is what a transparent render is for.
    * The order inside each family is his: hands unchanged, masks Grimhorn/Ironwatch/Runeveil,
      boards Steel/Bronze/Oak.

    ONE SCALE PER ROW, never per cell. Auto-fitting each asset to its own cell would normalise away
    the real size differences inside a family and let the picture claim something untrue.
    """
    name_f = _sheet_font(19, bold=True)
    pad, label_h = 18, 34
    cw = width // 3
    tiles = []
    for row in rows:
        ims = []
        for src, _ in row:
            im = Image.open(T + src).convert('RGBA')
            bb = im.getbbox()
            ims.append(im.crop(bb) if bb else im)
        f = min(min((cw - 2 * pad) / i.width, cell_h / i.height) for i in ims)
        tiles.append([i.resize((max(1, round(i.width * f)), max(1, round(i.height * f))),
                               Image.LANCZOS) for i in ims])
    heights = [max(i.height for i in r) for r in tiles]
    canvas = Image.new('RGB', (width, sum(heights) + len(rows) * (label_h + pad) + pad), bg)
    d = ImageDraw.Draw(canvas)
    y = pad
    for tile_row, row, rh in zip(tiles, rows, heights):
        for k, (im, (_, name)) in enumerate(zip(tile_row, row)):
            x = k * cw + (cw - im.width) // 2
            canvas.paste(im, (x, y + rh - im.height), im)      # sit them on a common baseline
            tw = d.textlength(name, font=name_f)
            d.text((k * cw + (cw - tw) / 2, y + rh + 8), name, font=name_f, fill=ink)
        y += rh + label_h + pad
    canvas.save(OUT + out, quality=94, optimize=True)
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

    NO LONGER THE README'S HEADER. The artist delivered `gloomhavenvr_promo.gif` on 2026-08-24 —
    animated key art that CONTAINS the wordmark — and it is the page header now. Both files below
    are still written, deliberately: they are the only record of what the "weisse Luecken" actually
    were, and the promo GIF happens to make that whole class of bug impossible (it is OPAQUE, so
    the light theme never puts this wordmark on white again). If a static wordmark is ever wanted
    back, the answer is already here and already measured. Do not re-derive it.

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
# Order inside each family is the user's. Masks: Mask_0 = Ironwatch, Mask_1 = Runeveil,
# Mask_2 = Grimhorn (Loc.cs:1018-1020). Boards: PlayTray_9capjqp6 = STEEL,
# PlayTray_16vm268h = BRONZE, PlayTray_prepped = OAK (BoardFrame.cs:68-69) — they swap easily,
# and have been swapped once; check the source, not the look of the render.
#
# BOARDS ARE OAK, STEEL, BRONZE (user, 2026-08-25) — and the matrix row disagreed with the
# strip above it, which has always been oak/steel/bronze. Both now read the same way, so the
# two images cannot tell different stories about the same three assets.
styles_matrix([[('hand_glove.png', 'Leather glove'), ('hand_plate.png', 'Plate gauntlet'),
                ('hand_arcane.png', 'Arcane glove')],
               [('mask_2.png', 'Grimhorn'), ('mask_0.png', 'Ironwatch'),
                ('mask_1.png', 'Runeveil')],
               # THE BOARD TILES CARRY THEIR GRAB ROD, at the mount the game uses. They went
               # through two wrong shapes first and both are worth remembering: a separate
               # grabbars.png beside the matrix, and then a fourth ROW of bare rods under the
               # boards. Both made the reader pair a rod with a board themselves. The user's
               # correction — "Statt eine eigene Zeile render die Boards direkt mit dem Greifbalken
               # an der Stelle an der man es auch sieht im Spiel" — is the only version where the
               # picture answers the question instead of posing it.
               #
               # THESE THREE DO NOT COME FROM render_asset.py like every other tile. That script is
               # Blender and takes FBX + albedo, and the rod is a PROCEDURAL mesh built in C# at
               # runtime — there is no FBX to hand it. They come from
               # unity/GloomhavenVR.Assets/Assets/Editor/PreviewBoardAsset.cs, which loads the board
               # prefab through AssetDatabase and so draws it through the REAL BoardLit rather than
               # through render_asset.py's reproduction of BoardLit's arithmetic. It matches this
               # script's framing deliberately: 900 px square, ortho 0.78, yaw 35, pitch 50,
               # transparent clear. See its header for the two conventions that had to be crossed
               # (Blender is right-handed, Unity is left-handed; and PlayTray is a VERTICAL panel
               # in the game, so it has to be laid flat to be photographed like a tray).
               [('board_oak.png', 'Oak'), ('board_steel.png', 'Steel'),
                ('board_bronze.png', 'Bronze')]], 'styles.png')
logo()
# NOTHING IS POSTERED ANY MORE. Every committed clip was deleted on 2026-09-07: a video
# served from a repo path does not play on GitHub, so the docs take `user-attachments`
# URLs and a poster has nothing left to front. poster() is kept for the day a clip is
# committed for some other reason; if that day never comes, delete it.
