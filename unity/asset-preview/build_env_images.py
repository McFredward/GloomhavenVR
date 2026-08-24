"""Compose the README's environment images from EnvironmentsPreview frames.

The frames come from the ReadmeC / ReadmeS stations added to
unity/GloomhavenVR.Assets/Assets/Editor/PreviewEnvironments.cs at ModBuild 248 — both shot from
the player's own head height (2.02 m in the cellar, 2.80 m in the wood), so the front page shows
the rooms from where a player really stands rather than from a convenient camera.

    ENV_PREVIEW_OUT=<dir> ENV_PREVIEW_VIEWS=Readme ENV_PREVIEW_NOHAUNT=1 ENV_PREVIEW_NOFIRE=1 \
      xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
      -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
      -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-readme.log
    ENV_RENDER_DIR=<dir> python3 unity/asset-preview/build_env_images.py

NOTHING IS RE-LIT, RE-GRADED OR BRIGHTENED HERE. Both rooms are night scenes with a handful of
small sources and that IS the product; a curve pulled over them for the page would advertise a
picture the player never gets. The only operations are crop, resize and — on the element grid —
the caption bar.
"""
import os
from PIL import Image, ImageDraw, ImageFont

T = os.environ.get('ENV_RENDER_DIR', 'env-previews/')
OUT = os.environ.get('README_IMG_DIR', 'docs/img/')
INK = (232, 224, 210)
BAR = (18, 15, 13)


def _font(px):
    for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',
              '/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf'):
        if os.path.exists(p):
            return ImageFont.truetype(p, px)
    return ImageFont.load_default()


def hero(src, out, width=1100):
    im = Image.open(T + src).convert('RGB')
    f = width / im.width
    im = im.resize((width, max(1, round(im.height * f))), Image.LANCZOS)
    im.save(OUT + out, quality=90, optimize=True)
    print('wrote', out, im.size)


def grid(cells, out, width=1280, cols=2):
    """cells: [(filename, caption), ...] laid out in `cols` columns, each with a caption bar."""
    rows = (len(cells) + cols - 1) // cols
    cw = width // cols
    src0 = Image.open(T + cells[0][0])
    ch = round(cw * src0.height / src0.width)
    bar = max(22, round(cw * 0.052))
    font = _font(round(bar * 0.60))
    canvas = Image.new('RGB', (width, rows * (ch + bar)), BAR)
    d = ImageDraw.Draw(canvas)
    for k, (name, caption) in enumerate(cells):
        x, y = (k % cols) * cw, (k // cols) * (ch + bar)
        im = Image.open(T + name).convert('RGB').resize((cw, ch), Image.LANCZOS)
        canvas.paste(im, (x, y))
        tw = d.textlength(caption, font=font)
        d.text((x + (cw - tw) / 2, y + ch + (bar - font.size) / 2 - 1), caption,
               font=font, fill=INK)
    canvas.save(OUT + out, quality=90, optimize=True)
    print('wrote', out, canvas.size)


os.makedirs(OUT, exist_ok=True)
hero('env_cellar_ReadmeC.png', 'env-cellar.jpg')
hero('env_swamp_ReadmeS.png', 'env-forest.jpg')
# Four frames, two per room, each against its own hero above: the claim is that the ROOM changes
# with the element board, so every cell is the same camera as the hero it is read against.
grid([('env_cellar_ReadmeC_efireS.png', 'Cellar · Fire'),
      ('env_cellar_ReadmeC_eiceS.png',  'Cellar · Ice'),
      ('env_swamp_ReadmeS_elightS.png', 'Forest · Light'),
      ('env_swamp_ReadmeS_eearthS.png', 'Forest · Earth')],
     'env-elements.jpg')
