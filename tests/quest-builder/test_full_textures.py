"""Protect complete native Cubemap face/mip serialization and path identity."""
from pathlib import Path
import contextlib
import io
import json
import os
import sys
import tempfile
import types
import unittest
from unittest import mock

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-builder'))
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-recovery'))
import full_textures
from storage import BuildError


def cube():
    return dict(m_Name='Native reflection',m_Width=4,m_Height=4,m_ImageCount=6,m_MipsStripped=0,
                m_MipCount=3,m_IsReadable=False,m_IsPreProcessed=False,m_IgnoreMasterTextureLimit=False,
                m_StreamingMipmaps=True,m_StreamingMipmapsPriority=0,m_ForcedFallbackFormat=4,
                m_DownscaleFallback=False,m_IsAlphaChannelOptional=True,m_ColorSpace=0,m_LightmapFormat=0,
                m_PlatformBlob=[],m_SourceTextures=[{'m_PathID':0}]*6,
                m_TextureSettings=dict(m_FilterMode=2,m_Aniso=0,m_MipBias=0,m_WrapU=1,m_WrapV=1,m_WrapW=1))


class NativeCubeTests(unittest.TestCase):
    def staged_cubes(self, *, final_reference_failure=False, platform_failure=False):
        import portable_decoder,pointer_recovery
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        root=Path(temporary.name);project=root/'project';game=root/'game';cache=root/'cache'
        (project/'QuestRecovery').mkdir(parents=True);game.mkdir();cache.mkdir()
        (game/'images.bundle').write_bytes(b'owned native image container')
        rows=[];objects=[]
        for index in range(4):
            is_cube=index<2;relative=f'Assets/Image{index}'+('.png' if is_cube else '.renderTexture')
            path=project/relative;path.parent.mkdir(exist_ok=True);path.write_bytes(b'original exported image')
            Path(str(path)+'.meta').write_text(f'fileFormatVersion: 2\nguid: {index+1:032x}\n')
            rows.append({'path':relative,'guid':f'{index+1:032x}','objects':[{'collection':'cab-images','pathId':index+1,'fileId':8900000 if is_cube else 8400000,'classId':89 if is_cube else 84}]})
            tree={**cube(),'m_TextureFormat':5} if is_cube else dict(m_Width=4,m_Height=4,m_ColorFormat=8,m_DepthStencilFormat=90,m_EnableCompatibleFormat=True,m_Dimension=2,m_AntiAliasing=1)
            if platform_failure and index==3:tree['m_ColorFormat']=999
            pixels=b'x'*504
            objects.append(types.SimpleNamespace(assets_file=types.SimpleNamespace(name='cab-images'),path_id=index+1,
                read_typetree=lambda tree=tree:tree,read=lambda pixels=pixels:types.SimpleNamespace(get_image_data=lambda:pixels)))
        (project/'QuestRecovery/original-asset-identities.json').write_text(json.dumps({'identities':rows}))
        environment=types.SimpleNamespace(objects=objects)
        stream=io.StringIO()
        with mock.patch.dict(os.environ,{full_textures.build_progress.ENV:'1'}),contextlib.redirect_stdout(stream), \
             mock.patch.object(full_textures.build_progress.time,'monotonic',side_effect=range(10000)), \
             mock.patch.object(pointer_recovery,'load_native',return_value=environment), \
             mock.patch.object(portable_decoder,'build',return_value=['fixture-codec']):
            if final_reference_failure:
                with mock.patch.object(full_textures,'restore_native_texture_pointer_types',side_effect=OSError('reference receipt failed')),self.assertRaisesRegex(OSError,'reference receipt failed'):
                    full_textures.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-images':'images.bundle'})
                report=None
            elif platform_failure:
                with self.assertRaisesRegex(BuildError,'RenderTexture format'):
                    full_textures.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-images':'images.bundle'})
                report=None
            else:
                report=full_textures.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-images':'images.bundle'})
                for row in report['assets']:
                    self.assertEqual(row['sha256'],__import__('hashlib').sha256((project/row['assetPath']).read_bytes()).hexdigest())
                self.assertEqual(report['platformImageAudit']['nativePlatformImageCount'],2)
        prefix=full_textures.build_progress.PREFIX
        progress=[json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        return report,progress

    def test_native_cube_and_child_image_counters_use_their_actual_target_lists(self):
        report,progress=self.staged_cubes()
        self.assertEqual(report['nativeCubemapCount'],2)
        for phase in ('prepare-items:native-cubemaps','prepare-items:platform-images'):
            values=[row for row in progress if row['phase']==phase]
            self.assertEqual([(row['status'],row['done'],row['total']) for row in values],[('start',0,2),('progress',1,2),('complete',2,2)])
        phases=[row['phase'] for row in progress if row['status']=='complete' and
                row['phase'] in ('prepare-items:platform-images','prepare-items:native-cubemaps')]
        self.assertEqual(phases,['prepare-items:native-cubemaps','prepare-items:platform-images'])

    def test_final_reference_failure_preserves_actual_native_cube_decode_completion(self):
        _,progress=self.staged_cubes(final_reference_failure=True)
        cube_progress=[row for row in progress if row['phase']=='prepare-items:native-cubemaps']
        self.assertEqual((cube_progress[-1]['status'],cube_progress[-1]['done']),('complete',2))
        self.assertFalse(any(row['phase']=='prepare-items:native-cubemaps-reference-receipts' and
                             row['status']=='complete' for row in progress))

    def test_failed_child_image_preserves_decoded_cubes_but_never_completes_reference_restoration(self):
        _,progress=self.staged_cubes(platform_failure=True)
        cubes=[row for row in progress if row['phase']=='prepare-items:native-cubemaps']
        self.assertEqual((cubes[-1]['status'],cubes[-1]['done']),('complete',2))
        images=[row for row in progress if row['phase']=='prepare-items:platform-images']
        self.assertEqual(images[-1]['status'],'failed')
        self.assertFalse(any(row['done']==2 or row['status']=='complete' for row in images))
        self.assertFalse(any(row['phase']=='prepare-items:native-cubemaps-reference-receipts' for row in progress))

    def test_native_texture_reference_types_change_real_nodes_and_refresh_current_hashes(self):
        import hashlib,json
        with tempfile.TemporaryDirectory() as root:
            p=Path(root);folder=p/'Assets/QuestOriginalCampaign';folder.mkdir(parents=True)
            target=p/'Assets/T.texture2D';target.write_text('%YAML 1.1\n--- !u!28 &2800000\nTexture2D:\n  m_Name: Probe\n')
            guid='a'*32
            Path(str(target)+'.meta').write_text('fileFormatVersion: 2\nguid: '+guid+'\nNativeFormatImporter:\n  mainObjectFileID: 2800000\n')
            (folder/'native-texture2d.json').write_text(json.dumps({'assets':[{'assetPath':'Assets/T.texture2D','guid':guid,'fileId':2800000}]}))
            pointer='{fileID: 2800000, guid: '+guid+', type: 3}'
            material=p/'Assets/M.mat'
            material.write_text('%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: "'+pointer+'"\n  first: '+pointer+'\n  second: '+pointer+'\n')
            before=hashlib.sha256(material.read_bytes()).hexdigest()
            native=folder/'native-sprites.json';native.write_text(json.dumps({'assets':[{'assetPath':'Assets/M.mat','sha256':before,'originalObjectSha256':'original-native-source'}]}))
            report=full_textures.restore_native_texture_pointer_types(p)
            self.assertEqual(report['referenceCount'],2)
            self.assertEqual(report['changedReferenceCount'],2)
            self.assertIn('m_Name: "'+pointer+'"',material.read_text())
            self.assertEqual(material.read_text().count('type: 2'),2)
            row=json.loads(native.read_text())['assets'][0]
            self.assertEqual(row['sha256'],hashlib.sha256(material.read_bytes()).hexdigest())
            self.assertEqual(row['originalObjectSha256'],'original-native-source')
            again=full_textures.restore_native_texture_pointer_types(p)
            self.assertEqual(again['referenceCount'],2)
            self.assertEqual(again['changedReferenceCount'],0)

    def test_full_half_mips_faces_and_nonreadability_are_retained(self):
        pixels=bytes(range(168))*6
        text=full_textures.native_yaml(cube(),pixels,17,8900000)
        self.assertIn('m_CompleteImageSize: 168\n',text)
        self.assertIn('image data: 1008\n',text)
        self.assertIn('m_IsReadable: 0\n',text)
        self.assertIn('m_TextureDimension: 4\n',text)
        self.assertEqual(bytes.fromhex(text.split('_typelessdata: ')[1].split('\n')[0]),pixels)

    def test_truncated_pixels_incomplete_mips_and_native_blob_fail(self):
        with self.assertRaises(BuildError): full_textures.native_yaml(cube(),b'x'*1007,17,8900000)
        with self.assertRaises(BuildError): full_textures.mip_sizes(4,2,8)
        with self.assertRaises(BuildError): full_textures.mip_sizes(3,2,8)
        native=cube();native['m_PlatformBlob']=[1]
        with self.assertRaises(BuildError): full_textures.native_yaml(native,b'x'*1008,17,8900000)

    def test_path_mapping_preserves_native_guid_and_original_logical_keys(self):
        with tempfile.TemporaryDirectory() as root:
            p=Path(root);(p/'QuestRecovery').mkdir()
            f=p/'QuestRecovery/original-asset-identities.json'
            f.write_text('{"identities":[{"path":"Assets/Cube.png","guid":"a","originalPath":"Assets/Cube.png"}]}')
            import json
            folder=p/'Assets/QuestOriginalCampaign';folder.mkdir(parents=True)
            catalog=folder/'campaign-addressables.json'
            catalog.write_text(json.dumps({'entries':[{'assetPath':'Assets/Cube.png','originalAssetPath':'Assets/Cube.png',
                           'keys':['Assets/Cube.png'],'nativeKeys':['Assets/Cube.png']}]}))
            bindings=folder/'script-bindings.json'
            bindings.write_text(json.dumps({'assetPaths':['Assets/Cube.png'],'bindings':[]}))
            changed=full_textures.remap_manifests(p,{'Assets/Cube.png':'Assets/Cube.asset'})
            self.assertEqual(len(changed),3)
            self.assertEqual(json.loads(f.read_text())['identities'][0],{'path':'Assets/Cube.asset','guid':'a','originalPath':'Assets/Cube.png'})
            self.assertEqual(json.loads(catalog.read_text())['entries'][0],{'assetPath':'Assets/Cube.asset',
                    'originalAssetPath':'Assets/Cube.png','keys':['Assets/Cube.png'],'nativeKeys':['Assets/Cube.png']})
            self.assertEqual(json.loads(bindings.read_text())['assetPaths'],['Assets/Cube.asset'])


if __name__=='__main__':unittest.main()
