#!/usr/bin/env python3
"""Bind the production source profile and literal shader to the frozen manifest."""
import hashlib
import json
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[2]


def main():
    path=ROOT/'tests/steamlink-scene-payload/profile.json'
    raw=path.read_bytes();p=json.loads(raw)
    assert hashlib.sha256(raw).hexdigest()=='04b47a78192aa883c264b1e04b88d0e17e7d373ab8ed1f1f6b15ee1a20601904'
    constants=(ROOT/'src/GloomhavenVR/Core/SteamLink/SceneAtlasProfile.cs').read_text()
    strings=dict(re.findall(r'public const string (\w+) = "([^"]+)";',constants))
    assert strings['Profile']==p['profile'] and strings['JsonSha256']==hashlib.sha256(raw).hexdigest()
    assert strings['SignatureSha256']==hashlib.sha256(p['guard']['signature_preimage'].encode()).hexdigest()
    assert p['guard']['codes']==list(bytes.fromhex(strings['SignatureSha256'])[:12])
    values={}
    for group in re.findall(r'public const int ([^;]+);',constants):
        for item in group.split(','):
            k,v=item.split('=');values[k.strip()]=int(v)
    assert [values['Width'],values['Height']]==p['canvas']
    assert [values['SceneX'],values['SceneY'],values['SceneSize'],values['SceneSize']]==p['scene']
    assert [values['GuardX'],values['GuardY']]==p['guard']['origin']
    assert values['GuardY']==2400 and values['GuardCount']==len(p['guard']['codes'])
    shader=(ROOT/'unity/Assets/Shaders/SteamLinkSceneAtlas.shader').read_text()
    codes=[int(v) for v in re.findall(r'if\(i==\d+\)return (\d+);',shader)]+[int(re.search(r';return (\d+);\n',shader).group(1))]
    assert codes==p['guard']['codes']
    assert 'p.y>=2400&&p.y<2432' in shader and 'p.x>=64' in shader
    assert 'linear_clamp_sampler' in shader and 'coverageCell(lo+1)' in shader
    assert 'scene(first/2048).a' in shader and 'scene((p-float2(32,352))/2048).rgb,1' in shader
    assert 'ReadPixels' not in (ROOT/'src/GloomhavenVR/Core/SteamLink/SceneEyeOutput.cs').read_text()
    # Historical and incorrect footer/profile literals cannot pass this guard.
    assert codes!=[111,153,155,51,111,82,92,219,102,227,121,189]
    print(json.dumps({'passed':True,'profile':p['profile'],'profile_sha256':hashlib.sha256(raw).hexdigest(),
        'signature_sha256':strings['SignatureSha256'],'guard_y':values['GuardY'],
        'native_frame_association_proven':False,'passthrough_authorized':False}))


if __name__=='__main__':main()
