#!/usr/bin/env python3
"""Read-only original albedo/parameter extraction for appearance regressions.

Converted images stay in private debug evidence. Identity/serialized hashes bind
these real materials; this does not execute their native Windows lighting code.
"""
import argparse, hashlib, json
from pathlib import Path
import UnityPy

def sha(data): return hashlib.sha256(data).hexdigest()

def extract(root, output):
    output.mkdir(parents=True, exist_ok=True)
    contract=json.loads((root/'tests/world-material-shader/appearance-contract.json').read_text())
    player_path=root/'ressources/GH_Data/globalgamemanagers'
    player=next(o for o in UnityPy.load(str(player_path)).objects if o.type.name=='PlayerSettings')
    player_tree=player.read_typetree()
    assert sha(player_path.read_bytes())==contract['playerSettings']['sourceSha256']
    assert sha(player.get_raw_data())==contract['playerSettings']['rawSha256']
    assert player_tree['m_ActiveColorSpace']==contract['playerSettings']['colorSpace']
    samples=[]
    shader_contract=json.loads((root/'tests/world-material-shader/native-contract.json').read_text())
    for entry in contract['materials']:
        path=root/'ressources/GH_Data'/entry['source']
        assert sha(path.read_bytes())==entry['sourceSha256'],entry['name']+' original source changed'
        objects=UnityPy.load(str(path)).objects
        original=next(o for o in objects if o.type.name=='Material' and o.path_id==entry['pathId'])
        assert original.assets_file.name==entry['serializedFile']
        assert sha(original.get_raw_data())==entry['rawSha256']
        material=original.read_typetree()
        assert material['m_Name']==entry['name']
        shader=material['m_Shader'];external=original.assets_file.externals[shader['m_FileID']-1]
        cab=external.path.split('/')[-1]
        addressed=next(o for o in shader_contract['objects']
                       if o['identity'].endswith('::'+cab+':'+str(shader['m_PathID'])))
        assert addressed['identity']==entry['shaderIdentity'] and addressed['route']==entry['route']
        properties=material['m_SavedProperties']
        tex=dict(properties['m_TexEnvs'])['_MainTex']
        assert tex['m_Texture']['m_FileID']==0,'External original texture needs explicit addressed dependency'
        texture=next(o for o in objects if o.type.name=='Texture2D' and o.path_id==tex['m_Texture']['m_PathID'])
        assert texture.path_id==entry['texture']['pathId']
        assert sha(texture.get_raw_data())==entry['texture']['rawSha256']
        decoded=texture.read()
        assert decoded.m_Name==entry['texture']['name']
        assert decoded.m_ColorSpace==entry['texture']['colorSpace']
        image=output/(entry['name']+'.png');decoded.image.save(image)
        sample=dict(name=entry['name'],route=entry['route'],texturePath=str(image),
                    srgb=decoded.m_ColorSpace==1,textureSha256=sha(image.read_bytes()),
                    keywords=material['m_ValidKeywords'],
                    floats=[dict(name=k,value=v)for k,v in properties['m_Floats']],
                    colors=[dict(name=k,value=[v[c]for c in ('r','g','b','a')])for k,v in properties['m_Colors']],
                    textureScale=[tex['m_Scale'][c]for c in ('x','y')],textureOffset=[tex['m_Offset'][c]for c in ('x','y')],
                    texcoordScale=[1,1],texcoordOffset=[0,0])
        hidden=dict(properties['m_TexEnvs']).get('_texcoord')
        if hidden:
            sample['texcoordScale']=[hidden['m_Scale'][c]for c in ('x','y')]
            sample['texcoordOffset']=[hidden['m_Offset'][c]for c in ('x','y')]
        samples.append(sample)
    result=dict(colorSpace=player_tree['m_ActiveColorSpace'],samples=samples,
                limits='Decoded original color images/parameters, native Unity CPU SH reference; original Windows PBR and HMD pixels are not executed.')
    (output/'appearance.json').write_text(json.dumps(result,indent=2)+'\n')
    print('Extracted '+str(len(samples))+' addressed original appearance samples; game ColorSpace='+str(result['colorSpace']))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,required=True)
    parser.add_argument('--output-dir',type=Path,required=True)
    args=parser.parse_args();extract(args.source_root.resolve(),args.output_dir.resolve())
