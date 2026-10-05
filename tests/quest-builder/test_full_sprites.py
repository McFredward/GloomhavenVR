"""Check exact authored geometry and native UV reconstruction for ordinary Sprites."""
import json
from pathlib import Path
import struct
import sys
import unittest

root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/quest-builder'));sys.path.insert(0,str(root/'tools/quest-recovery'))
import full_sprites
import yaml


def fields():
    channels=[{'stream':0,'offset':0,'format':0,'dimension':0} for _ in range(5)]
    channels[0]['dimension']=3;channels[4]['dimension']=2;channels[4]['offset']=12
    data=b''.join(struct.pack('<5f',*p,0,0) for p in [(0,0,1),(.5,0,2),(0,.5,3)])+b'\0'*4
    return {'m_Rect':{'x':1506,'y':607,'width':128,'height':128},'m_Offset':{'x':-64,'y':64},
            'm_Border':{'x':1,'y':2,'z':3,'w':4},'m_Pivot':{'x':0,'y':1},'m_PixelsToUnits':100,
            'm_RD':{'m_VertexData':{'m_Channels':channels,'m_VertexCount':3,'m_DataSize':data},
                    'texture':{'fileID':0},'textureRect':{'x':1514,'y':615,'width':112,'height':112},
                    'textureRectOffset':{'x':8,'y':8},'atlasRectOffset':{'x':0,'y':0},
                    'uvTransform':{'x':100,'y':1570,'z':100,'w':671},'settingsRaw':0,'downscaleMultiplier':1}}


def exported(native):
    f=json.loads(json.dumps(native,default=lambda v:len(v)))
    f['m_Rect']={'serializedVersion':2,'x':1514,'y':615,'width':112,'height':112}
    f['m_Pivot']={'x':-.07,'y':1.06}
    f['m_RD']['textureRect']['serializedVersion']=2
    f['m_RD']['m_VertexData']['_typelessdata']='00'*64
    return '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!213 &21300000\n'+yaml.safe_dump({'Sprite':f},sort_keys=False)


class NativeSpriteTests(unittest.TestCase):
    def test_original_rect_pivot_trim_and_vertices_are_retained(self):
        source=fields();before=exported(source)
        after,vertices,uv=full_sprites.restore_fields(before,source,2048,2048,{'guid':'a'*32,'fileId':2800000})
        actual=yaml.safe_load(after.split('\n',3)[3])['Sprite']
        self.assertEqual(actual['m_Rect'],{'serializedVersion':2,**source['m_Rect']})
        self.assertEqual(actual['m_Pivot'],source['m_Pivot'])
        self.assertEqual(actual['m_RD']['textureRectOffset'],source['m_RD']['textureRectOffset'])
        self.assertEqual(vertices,[{'x':0,'y':0},{'x':.5,'y':0},{'x':0,'y':.5}])
        self.assertEqual(uv[0],{'x':1570/2048,'y':671/2048})
        payload=bytes.fromhex(actual['m_RD']['m_VertexData']['_typelessdata'])
        for index in range(3):self.assertEqual(payload[index*20:index*20+12],source['m_RD']['m_VertexData']['m_DataSize'][index*20:index*20+12])
        self.assertEqual(actual['m_RD']['texture']['guid'],'a'*32)

    def test_native_restoration_is_idempotent(self):
        source=fields();texture={'guid':'a'*32,'fileId':2800000}
        once=full_sprites.restore_fields(exported(source),source,2048,2048,texture)[0]
        twice=full_sprites.restore_fields(once,source,2048,2048,texture)[0]
        self.assertEqual(once,twice)


if __name__=='__main__':unittest.main()
