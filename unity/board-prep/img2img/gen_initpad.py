"""Build the img2img init frames: 2:1 board, mildly level-stretched, padded into 16:9.

WHY LEVEL-STRETCH. The ambient render is a picture of an albedo that has almost no dynamic
range, so it comes out as a dim narrow band. A model given a dim, flat init frame tends to
answer with a dim, flat image. The stretch is applied to the INIT FRAME ONLY — nothing here
reaches the shipped atlas; the shipped albedo is rebuilt from what comes back.

WHY 16:9. The tool takes an aspect-ratio enum and 2:1 is not in it. 16:9 (1.778) is the
closest; the 2:1 board fills 88.9% of the height and the rest is plain black, which is
cropped straight back off. Padding rather than stretching keeps the board's proportions
exact, so the return image registers to the init frame with a pure scale.
"""
import numpy as np, sys
from PIL import Image

def stretch(a, lo_p=0.5, hi_p=99.5, target=(28, 232)):
    """Linear level stretch on luminance, applied as a common gain/lift to all channels so
    hue is not disturbed."""
    lum = a.mean(2)
    lo, hi = np.percentile(lum, lo_p), np.percentile(lum, hi_p)
    g = (target[1]-target[0]) / max(hi-lo, 1e-6)
    # GAIN CAP. Bronze's raw range is so narrow that an uncapped stretch asks for 3.4x and
    # returns a neon-orange plate; a model handed a garish init frame answers with a garish
    # image. 2.2x is enough to make every board read as a well-exposed photograph without
    # inventing saturation the material does not have.
    g = min(g, 2.2)
    return np.clip((a - lo) * g + target[0], 0, 255)

for style in ('steel', 'oak', 'bronze'):
    im = Image.open(f'init/{style}/ambient.png')
    a = np.asarray(im.convert('RGB')).astype(np.float32)
    alpha = np.asarray(im.convert('RGBA'))[..., 3].astype(np.float32)/255.0
    print(style, 'board px', im.size, 'covered %.3f' % alpha.mean())
    s = stretch(a)
    s = s * alpha[..., None]                      # keep the outside black
    H, W = s.shape[:2]                            # 1024 x 2048
    CH = int(round(W * 9/16))                     # 1152
    canvas = np.zeros((CH, W, 3), np.float32)
    y0 = (CH - H)//2
    canvas[y0:y0+H] = s
    Image.fromarray(canvas.astype(np.uint8)).save(f'init/init_{style}.png')
    print('   ->', f'init/init_{style}.png', W, 'x', CH, 'board rows', y0, y0+H,
          'mean %.1f std %.1f' % (s[alpha>0.5].mean(), s[alpha>0.5].std()))
