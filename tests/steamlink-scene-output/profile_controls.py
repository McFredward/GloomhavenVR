#!/usr/bin/env python3
"""Bind the production source profile and literal shader to the frozen manifest."""
import hashlib
import json
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[2]


def active_production_profile(source):
    matches=re.findall(r'_pack\s*=\s*_bank\.LoadAsset<Shader>\("([^"]+)"\);',source)
    assert matches==['assets/steamlink/steamlinksceneatlas.shader'],'Only the contained legacy v1 source is currently admitted'
    return 'scene-contribution-atlas-v1'


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
    assert 'f=f*f*(3-2*f)' in shader  # Current spatial kernel, not an alpha threshold.
    assert 'scene(first/2048).a' in shader and 'scene((p-float2(32,352))/2048).rgb,1' in shader
    assert 'ReadPixels' not in (ROOT/'src/GloomhavenVR/Core/SteamLink/SceneEyeOutput.cs').read_text()
    # Historical and incorrect footer/profile literals cannot pass this guard.
    assert codes!=[111,153,155,51,111,82,92,219,102,227,121,189]
    full_path=ROOT/'tests/steamlink-scene-payload/profile-v2.json'
    full_raw=full_path.read_bytes();full=json.loads(full_raw)
    assert strings['FullProfile']==full['profile']
    assert strings['FullJsonSha256']==hashlib.sha256(full_raw).hexdigest()
    assert hashlib.sha256(full['guard']['signature_preimage'].encode()).hexdigest()==full['guard']['signature_sha256']
    assert full['guard']['codes']==list(bytes.fromhex(full['guard']['signature_sha256'])[:12])
    assert full['coverage']['size']==full['scene'][2:]==[2048,2048]
    assert full['canvas']==[3168,3168]
    assert full['coverage']['data_origins']==[[576,32],[2624,576],[544,2624],[32,544]]
    assert full['coverage']['rotation_quarter_turns']==[0,1,0,1]
    full_shader=(ROOT/'unity/Assets/Shaders/SteamLinkSceneFullAtlas.shader').read_text()
    full_codes=[int(v) for v in re.findall(r'if\(i==\d+\)return (\d+);',full_shader)]+[int(re.search(r';return (\d+);\n',full_shader).group(1))]
    assert full_codes==full['guard']['codes']
    assert 'float a=saturate(scene(q).a)' in full_shader
    assert 'coverageCell' not in full_shader and '3-2*f' not in full_shader
    assert 'return float4(scene((p-560)/2048).rgb,1)' in full_shader
    production=(ROOT/'src/GloomhavenVR/Core/SteamLink/ScenePayloadAssets.cs').read_text()
    active=active_production_profile(production)
    # A real source mutation must fail; retained v2 constants/assets are not
    # themselves permission to select its unproven real-stream transport.
    mutant=production.replace('assets/steamlink/steamlinksceneatlas.shader','assets/steamlink/steamlinkscenefullatlas.shader')
    try:active_production_profile(mutant)
    except AssertionError:pass
    else:raise AssertionError('Quarantined v2 selection mutation was not refused')
    print(json.dumps({'passed':True,'profile':p['profile'],'profile_sha256':hashlib.sha256(raw).hexdigest(),'full_profile':full['profile'],
        'active_production_profile':active,'quarantined_v2_selection_mutant_rejected':True,
        'full_profile_sha256':hashlib.sha256(full_raw).hexdigest(),
        'signature_sha256':strings['SignatureSha256'],'guard_y':values['GuardY'],
        'native_frame_association_proven':False,'passthrough_authorized':False}))


if __name__=='__main__':main()
