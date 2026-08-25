"""Board-texture statistics in CIELAB — the corrected instrument.

WHY IT WAS CHANGED. The first version measured the low-frequency term on LUMINANCE only.
It then reported 7.85 for the new bronze, a plate that visibly runs from pale blue-green
verdigris to warm gold, because that variation is almost entirely CHROMATIC at nearly
constant lightness. An instrument that models one term of what the eye sees agrees with
a picture that is wrong in the other term. The mean-chroma target was wrong the same way:
the SHIPPED bronze scores 29.5 and the shipped oak 30.7 precisely BECAUSE they are
uniformly saturated, which is the defect, while a plate that ranges from grey to gold
scores lower and looks better.

The three numbers that matter, all in CIELAB where a unit is roughly a just-noticeable
difference:
  STORY  - mean distance of the 37 mm low-pass from its own mean, over L*a*b* together.
           "How much does this plate change across a hand's width, in any way the eye sees."
           This is the number that separates a made object from a noise field.
  GRAIN  - the same distance for what the low-pass removed: the micro-material.
  HUEVAR - std of chroma. Variation of saturation, not its average: a uniformly orange
           plate has high mean chroma and near-zero HUEVAR.
"""
import sys, numpy as np
from PIL import Image

def srgb2lin(v):
    v=v/255.0
    return np.where(v<=0.04045, v/12.92, ((v+0.055)/1.055)**2.4)

def to_lab(rgb):
    l=srgb2lin(rgb)
    M=np.array([[0.4124,0.3576,0.1805],[0.2126,0.7152,0.0722],[0.0193,0.1192,0.9505]])
    X=l@M.T; W=np.array([0.95047,1.0,1.08883]); t=X/W
    f=np.where(t>0.008856, np.cbrt(t), 7.787*t+16/116)
    return np.stack([116*f[...,1]-16, 500*(f[...,0]-f[...,1]), 200*(f[...,1]-f[...,2])],-1)

def boxblur(a,k):
    r=k//2; p=np.pad(a,((r,r),(r,r),(0,0)),mode='reflect')
    c=np.cumsum(p,0); c=np.vstack([np.zeros((1,)+c.shape[1:]),c]); a1=(c[k:]-c[:-k])/k
    c=np.cumsum(a1,1); c=np.hstack([np.zeros((c.shape[0],1)+c.shape[2:]),c])
    return (c[:,k:]-c[:,:-k])/k

def measure(rgb, k=129):
    """rgb must be 2048x1024 board space so k=129 texels == 37 mm on the plate."""
    lab=to_lab(rgb)
    lo=boxblur(lab,k); hi=lab-lo
    story=np.linalg.norm(lo-lo.reshape(-1,3).mean(0), axis=-1).mean()
    grain=np.linalg.norm(hi, axis=-1).mean()
    ch=np.hypot(lab[...,1],lab[...,2])
    return dict(L=lab[...,0].mean(), story=story, grain=grain,
                chroma=ch.mean(), huevar=ch.std(),
                p1=np.percentile(rgb.mean(2),1), p99=np.percentile(rgb.mean(2),99))

def show(name, rgb, k=129):
    m=measure(rgb,k)
    print("%-28s L* %5.1f | STORY %5.2f | GRAIN %5.2f | chroma %5.2f huevar %5.2f | span %5.1f"%(
        name, m['L'], m['story'], m['grain'], m['chroma'], m['huevar'], m['p99']-m['p1']))
    return m

def board_rect(path):
    a=np.asarray(Image.open(path).convert('RGB')).astype(np.float32); lum=a.mean(2)
    xs=np.nonzero((lum>25).mean(0)>0.35)[0]; ys=np.nonzero((lum>25).mean(1)>0.35)[0]
    return a[int(ys.min()):int(ys.max())+1, int(xs.min()):int(xs.max())+1]

def to2048(a):
    return np.asarray(Image.fromarray(a.astype(np.uint8)).resize((2048,1024),Image.LANCZOS)).astype(np.float32)

if __name__=='__main__':
    O='/home/claw/gloomhaven_vr/unity/board-prep/out/'
    G={'oak':'board_oak_gen1.png','steel':'board_steel_gen2.png','bronze':'board_bronze_gen1.png'}
    for s in ('oak','steel','bronze'):
        print('---',s)
        show('   SHIPPED', np.asarray(Image.open(f'init/{s}/ambient.png').convert('RGB')).astype(np.float32))
        show('   GENERATED', to2048(board_rect(O+G[s])))
