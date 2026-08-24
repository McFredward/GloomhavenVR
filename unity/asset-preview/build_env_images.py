"""Compose the README's environment images from EnvironmentsPreview frames.

The frames come from the ReadmeC / ReadmeS / ReadmeMoon stations in
unity/GloomhavenVR.Assets/Assets/Editor/PreviewEnvironments.cs — all three shot from the player's
own head height (2.02 m in the cellar, 2.80 m in the wood), so the front page shows the rooms from
where a player really stands rather than from a convenient camera.

    ENV_PREVIEW_OUT=<dir> ENV_PREVIEW_VIEWS=Readme,FireRoom ENV_PREVIEW_NOHAUNT=1 ENV_PREVIEW_NOFIRE=1 \
      xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
      -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
      -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-readme.log
    ENV_RENDER_DIR=<dir>/ python3 unity/asset-preview/build_env_images.py

NOTHING IS RE-LIT, RE-GRADED OR BRIGHTENED HERE. Both rooms are night scenes with a handful of
small sources and that IS the product; a curve pulled over them for the page would advertise a
picture the player never gets. The only operations are crop, resize and the labels.

THE ELEMENT IMAGE IS A BEFORE/AFTER, and that is the whole point of the second version of it. The
first was four single frames of four different element states, and the user's verdict was exactly
right: "nicht wirklich ersichtlich was da der unterschied ist". Of course not — there was nothing
in the picture to compare each cell against. Every row now shows THE SAME CAMERA with the element
off and on, and the four rows are chosen for the size of that difference rather than for covering
the set: Fire and Ice remake the cellar, Light remakes the wood, and Dark puts the moon into a
total eclipse. Air and Earth are real and are left out here because they are MOTION (leaves
shaking, growth coming up) and a still cannot show them honestly.
"""
import os
from PIL import Image, ImageDraw, ImageFont

T = os.environ.get('ENV_RENDER_DIR', 'env-previews/')
OUT = os.environ.get('README_IMG_DIR', 'docs/img/')
INK = (236, 228, 214)
DIM = (150, 140, 128)
BAR = (18, 15, 13)


def _font(px, bold=False):
    names = (('DejaVuSans-Bold.ttf', 'LiberationSans-Bold.ttf') if bold else
             ('DejaVuSans.ttf', 'LiberationSans-Regular.ttf'))
    for d in ('/usr/share/fonts/truetype/dejavu/', '/usr/share/fonts/truetype/liberation/'):
        for n in names:
            if os.path.exists(d + n):
                return ImageFont.truetype(d + n, px)
    return ImageFont.load_default()


def hero(src, out, width=1100):
    im = Image.open(T + src).convert('RGB')
    f = width / im.width
    im = im.resize((width, max(1, round(im.height * f))), Image.LANCZOS)
    im.save(OUT + out, quality=90, optimize=True)
    print('wrote', out, im.size)


def pairs(rows, out, width=1280, cell_h=340):
    """rows: [(base_png, element_png, caption, crop_box_or_None), ...] — one before/after per row.

    The crop box exists for the moon: at the ReadmeMoon station the disc is a small part of a wide
    frame, and the eclipse — the single most visible thing either element board does — would land
    on a few dozen pixels at README width. Cropping a real frame is not staging it; re-lighting one
    would be.
    """
    cw = width // 2
    bar = 40
    cap = _font(21, bold=True)
    tag = _font(16)
    canvas = Image.new('RGB', (width, len(rows) * (cell_h + bar)), BAR)
    d = ImageDraw.Draw(canvas)
    for k, (base, elem, caption, box) in enumerate(rows):
        y = k * (cell_h + bar)
        # The two corner tags are a PAIR and read as one sentence — "element off" / "element on".
        # The right one used to name the element instead, which made the row's own caption bar
        # below ("Cellar · Fire") the only place the pairing was stated, and left the eye with two
        # unrelated labels to reconcile. Which element it is belongs in exactly one place.
        for col, (name, label) in enumerate(((base, 'element off'), (elem, 'element on'))):
            im = Image.open(T + name).convert('RGB')
            if box:
                im = im.crop(box)
            # cover-fit the cell so both halves show the same framing at the same scale
            f = max(cw / im.width, cell_h / im.height)
            im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.LANCZOS)
            im = im.crop(((im.width - cw) // 2, (im.height - cell_h) // 2,
                          (im.width - cw) // 2 + cw, (im.height - cell_h) // 2 + cell_h))
            canvas.paste(im, (col * cw, y))
            d.text((col * cw + 14, y + 10), label.upper(), font=tag,
                   fill=DIM if col == 0 else INK)
        d.line([(cw, y), (cw, y + cell_h)], fill=BAR, width=3)
        tw = d.textlength(caption, font=cap)
        d.text(((width - tw) / 2, y + cell_h + (bar - 21) / 2 - 2), caption, font=cap, fill=INK)
    canvas.save(OUT + out, quality=90, optimize=True)
    print('wrote', out, canvas.size)


os.makedirs(OUT, exist_ok=True)
hero('env_cellar_ReadmeC.png', 'env-cellar.jpg')
hero('env_swamp_ReadmeS.png', 'env-forest.jpg')
# The moon crop: 360x202 of the 1280x720 ReadmeMoon frame, centred on the disc.
MOON = (470, 180, 830, 382)
pairs([# FIRE IS SHOT FROM FireRoom, NOT FROM THE README STATION, and that is the user's second
       # verdict on this image: "Das Feuer sieht man nicht auf dem Bild." He was right — ReadmeC
       # looks at the candle table, so under Fire it showed the room BRIGHTENING with no fire
       # anywhere in frame, which reads as a light switch. The gated fires are props in fixed
       # places (a crate, a plank, a barrel) and a frame has to point AT them. FireRoom holds
       # three of them at once and its "off" half is the same corner, dark and quiet.
       ('env_cellar_FireRoom_ebase.png',  'env_cellar_FireRoom_efireS.png',  'Cellar · Fire',  None),
       ('env_cellar_ReadmeC_ebase.png',   'env_cellar_ReadmeC_eiceS.png',    'Cellar · Ice',   None),
       ('env_swamp_ReadmeS_ebase.png',    'env_swamp_ReadmeS_elightS.png',   'Forest · Light', None),
       ('env_swamp_ReadmeMoon_ebase.png', 'env_swamp_ReadmeMoon_edarkS.png', 'Forest · Dark',  MOON)],
      'env-elements.jpg')
