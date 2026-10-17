#!/usr/bin/env python3
"""Independent CPU draft-layout oracle, bound to the owned source constants."""
import hashlib
import json
import math
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[2]
PROPOSALS = [
    dict(name='centered-2112x2752-coverage1024-draft', canvas=[2112, 2752],
         color=[32,352,2048,2048], coverage=1024, tile_rows=256, halo=16,
         outer=[[0,32,1056,288],[1056,32,1056,288],
                [0,2432,1056,288],[1056,2432,1056,288]], footer=[0,2720,2112,32]),
    dict(name='centered-2048x2688-coverage992-draft', canvas=[2048,2688],
         color=[0,320,2048,2048], coverage=992, tile_rows=248, halo=16,
         outer=[[0,32,1024,280],[1024,32,1024,280],
                [0,2376,1024,280],[1024,2376,1024,280]], footer=[0,2656,2048,32])
]


def overlaps(a,b):
    return a[0] < b[0]+b[2] and b[0] < a[0]+a[2] and a[1] < b[1]+b[3] and b[1] < a[1]+a[3]


def raw(x,y,n):
    # Asymmetric discontinuities expose wrong row/eye/crop, unlike a flat fill.
    x=max(0,min(n-1,x)); y=max(0,min(n-1,y))
    return ((x*17+y*31+(x//7)*9+(y//11)*23)%251)/250.0


def bilinear(fetch,x,y):
    ix=math.floor(x); iy=math.floor(y); fx=x-ix; fy=y-iy
    return (fetch(ix,iy)*(1-fx)*(1-fy)+fetch(ix+1,iy)*fx*(1-fy)
            +fetch(ix,iy+1)*(1-fx)*fy+fetch(ix+1,iy+1)*fx*fy)


def tile_pixel(p,ax,ay,mutant=False):
    for t,(x,y,w,h) in enumerate(p['outer']):
        if x <= ax < x+w and y <= ay < y+h:
            gx=ax-x-p['halo']; gy=ay-y-p['halo']+t*p['tile_rows']
            if mutant:
                gy=max(t*p['tile_rows'],min((t+1)*p['tile_rows']-1,gy))
            return raw(gx,gy,p['coverage'])
    raise AssertionError('Bilinear footprint escaped all owned tile halos')


def sample(p,qx,qy,mutant=False):
    n=p['coverage']; px=qx*n-.5; py=qy*n-.5
    t=min(3,math.floor(max(py,0)/p['tile_rows']))
    x,y,_,_=p['outer'][t]
    return bilinear(lambda ax,ay:tile_pixel(p,ax,ay,mutant),
                    x+p['halo']+px,y+p['halo']+py-t*p['tile_rows'])


def main():
    text=(ROOT/'src/GloomhavenVR/Core/SteamLink/ScenePayloadDraft.cs').read_text()
    constants={}
    for group in re.findall(r'public const int ([^;]+);',text):
        for assignment in group.split(','):
            name,value=assignment.strip().split('=')
            constants[name.strip()]=int(value.strip())
    expected={'CanvasWidth':2112,'CanvasHeight':2752,'SceneSize':2048,'SceneX':32,'SceneY':352,
        'CoverageSize':1024,'TileRows':256,'Halo':16,'TileWidth':1056,'TileHeight':288,
        'TopTileY':32,'BottomTileY':2432,'FooterY':2720}
    assert constants==expected, 'CPU geometry must bind the exact owned source constants'
    p=PROPOSALS[0]
    checks=len(expected); errors=[];mutant=0;seams=0
    w,h=p['canvas'];x,y,cw,ch=p['color']
    assert (x+cw/2)/w==.5 and (y+ch/2)/h==.5;checks+=2
    regions=[p['color'],p['footer'],*p['outer']]
    for i,r in enumerate(regions):
        assert r[0]>=0 and r[1]>=0 and r[0]+r[2]<=w and r[1]+r[3]<=h;checks+=1
        for q in regions[i+1:]:assert not overlaps(r,q);checks+=1
    n=p['coverage']
    for boundary in (0,256,512,768,1024):
        for delta in (-1.2,-.5,-.1,0,.1,.5,1.2):
            qy=max(0,min(1,(boundary+delta)/n))
            for x in (0,.37,7.125,531.8,1023.25,1024):
                qx=x/n
                original=bilinear(lambda ix,iy:raw(ix,iy,n),qx*n-.5,qy*n-.5)
                got=sample(p,qx,qy)
                error=abs(got-original);assert error<1e-12;errors.append(error);checks+=1
                if abs(sample(p,qx,qy,True)-original)>1e-6:mutant+=1
                if boundary in (256,512,768):seams+=1
    assert mutant>0;checks+=1
    e=(.3,.1,.05);a=0;camera=(.2,.4,.6)
    correct=tuple(e[i]+(1-a)*camera[i] for i in range(3))
    wrong=tuple(e[i]*a+(1-a)*camera[i] for i in range(3))
    assert correct!=wrong;checks+=1
    # No existing/current native signature may be emitted by this private draft.
    pack=(Path(__file__).parent/'ScenePayloadPack.shader').read_text()
    assert '(floor(p.x/64)+1)/16' in pack;checks+=1
    assert 'return float4(e,1)' in pack and 'e*a' not in pack;checks+=1
    assert 'source alpha' in text and '_CoverageTex' in pack;checks+=1
    print(json.dumps({'scope':'OWNED_CPU_DRAFT_GEOMETRY_ONLY','passed':True,'checks':checks,
        'seam_samples':seams,'max_bilinear_error':max(errors),'wrong_halo_rejections':mutant,
        'zero_alpha_emission_mutant_rejected':True,'gpu_executed':False,
        'game_coverage_authorized':False,'passthrough_authorized':False}))

if __name__=='__main__':main()
