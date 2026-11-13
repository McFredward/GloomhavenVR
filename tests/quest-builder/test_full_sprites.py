"""Check exact authored geometry and native UV reconstruction for ordinary Sprites."""
import json
import contextlib
import hashlib
import io
import os
from pathlib import Path
import struct
import sys
import tempfile
import types
import unittest
from unittest import mock

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
    def staged_sprites(self, *, receipt_failure=False):
        import pointer_recovery
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        root=Path(temporary.name);project=root/'project';game=root/'game'
        (project/'QuestRecovery').mkdir(parents=True);game.mkdir()
        (game/'sprites.bundle').write_bytes(b'owned original Sprite container')
        rows=[];objects=[]
        collection=types.SimpleNamespace(name='cab-sprites',externals=[])
        for index in range(3):
            tree=fields();tree['m_SpriteAtlas']={'m_PathID':0}
            tree['m_RD']['texture']={'m_FileID':0,'m_PathID':99}
            relative=f'Assets/Sprite{index}.asset';path=project/relative;path.parent.mkdir(exist_ok=True);path.write_text(exported(tree))
            rows.append({'path':relative,'guid':f'{index+1:032x}','objects':[{'collection':'cab-sprites','pathId':index+1,'fileId':21300000,'classId':213}]})
            objects.append(types.SimpleNamespace(assets_file=collection,path_id=index+1,read_typetree=lambda tree=tree:tree,
                get_raw_data=lambda index=index:b'exact native Sprite '+bytes([index])))
        # Its already witnessed packed state is retained, not decoded again or
        # credited as another nonpacked codec item.
        packed={'collection':'cab-sprites','pathId':3,'assetPath':'Assets/Sprite2.asset',
                'sha256':hashlib.sha256((project/'Assets/Sprite2.asset').read_bytes()).hexdigest()}
        (project/'QuestRecovery/packed-sprites-fixture.json').write_text(json.dumps({'restoredMembers':[packed]}))
        rows.append({'path':'Assets/Texture.png','guid':'f'*32,'objects':[{'collection':'cab-sprites','pathId':99,'fileId':2800000,'classId':28}]})
        texture_reader=mock.Mock(return_value={'m_Width':2048,'m_Height':2048})
        objects.append(types.SimpleNamespace(assets_file=collection,path_id=99,read_typetree=texture_reader))
        (project/'QuestRecovery/original-asset-identities.json').write_text(json.dumps({'identities':rows}))
        environment=types.SimpleNamespace(objects=objects);stream=io.StringIO()
        with mock.patch.dict(os.environ,{full_sprites.build_progress.ENV:'1'}),contextlib.redirect_stdout(stream), \
             mock.patch.object(full_sprites.build_progress.time,'monotonic',side_effect=range(10000)), \
             mock.patch.object(pointer_recovery,'load_native',return_value=environment):
            if receipt_failure:
                with mock.patch.object(full_sprites,'write_json',side_effect=OSError('Sprite receipt failed')),self.assertRaisesRegex(OSError,'Sprite receipt failed'):
                    full_sprites.stage(project,game,cab_bundles={'cab-sprites':'sprites.bundle'})
                report=None
            else:
                report=full_sprites.stage(project,game,cab_bundles={'cab-sprites':'sprites.bundle'})
                for row in report['assets']:
                    self.assertEqual(row['sha256'],hashlib.sha256((project/row['assetPath']).read_bytes()).hexdigest())
                    self.assertEqual(row['vertices'],[{'x':0,'y':0},{'x':.5,'y':0},{'x':0,'y':.5}])
                self.assertEqual(hashlib.sha256((project/packed['assetPath']).read_bytes()).hexdigest(),packed['sha256'])
        prefix=full_sprites.build_progress.PREFIX
        progress=[json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        self.texture_read_count=texture_reader.call_count
        self.all_progress=progress
        self.identity_bytes=(project/'QuestRecovery/original-asset-identities.json').stat().st_size
        return report,[value for value in progress if value['phase']=='prepare-items:native-sprites']

    def test_sprite_counter_accepts_two_targets_and_preserves_packed_state(self):
        report,progress=self.staged_sprites()
        self.assertEqual((report['restoredNonPackedSpriteCount'],report['preservedPackedSpriteCount']),(2,1))
        self.assertEqual([(row['status'],row['done'],row['total']) for row in progress],[('start',0,2),('progress',1,2),('complete',2,2)])
        self.assertEqual(progress[1]['detail'],'Assets/Sprite0.asset')

    def test_final_sprite_receipt_error_cannot_publish_complete_counter(self):
        _,progress=self.staged_sprites(receipt_failure=True)
        self.assertEqual(progress[-1]['status'],'failed')
        self.assertFalse(any(row['status']=='complete' or row['done']==2 for row in progress))

    def test_planning_counts_real_objects_containers_and_bytes_without_decoding_shared_texture_twice(self):
        self.staged_sprites()
        self.assertEqual(self.texture_read_count,1)
        phases={'identities':self.identity_bytes,'packed':1,'targets':4,'containers':1}
        for name,total in phases.items():
            rows=[row for row in self.all_progress if row['phase']=='prepare-items:native-sprites-'+name]
            self.assertEqual(rows[0]['status'],'start')
            self.assertEqual((rows[-1]['status'],rows[-1]['done'],rows[-1]['total']),('complete',total,total))
            self.assertEqual([row['done'] for row in rows],sorted(row['done'] for row in rows))
        self.staged_sprites(receipt_failure=True)
        containers=[row for row in self.all_progress if row['phase']=='prepare-items:native-sprites-containers']
        self.assertEqual(containers[-1]['status'],'failed')
        self.assertFalse(any(row['status']=='complete' for row in containers))

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
