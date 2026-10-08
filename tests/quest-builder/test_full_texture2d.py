"""Protect original floating VAT pixels, rectangular HDR mips and importer scope."""
from pathlib import Path
import contextlib
import hashlib
import io
import json
import os
import struct
import sys
import tempfile
import threading
import time
import types
import unittest
from unittest import mock

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-builder'))
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-recovery'))
import full_texture2d
from storage import BuildError


def native():
    return dict(m_Name='Original VAT',m_Width=4,m_Height=3,m_ImageCount=1,m_TextureDimension=2,m_MipsStripped=0,
                m_MipCount=1,m_IsReadable=False,m_IsPreProcessed=False,m_IgnoreMasterTextureLimit=False,
                m_StreamingMipmaps=True,m_StreamingMipmapsPriority=0,m_ForcedFallbackFormat=4,
                m_DownscaleFallback=False,m_IsAlphaChannelOptional=True,m_ColorSpace=0,m_LightmapFormat=0,
                m_TextureFormat=17,m_PlatformBlob=[],
                m_TextureSettings=dict(m_FilterMode=0,m_Aniso=1,m_MipBias=0,m_WrapU=0,m_WrapV=0,m_WrapW=0))


def importer(texture_type=0):
    return {'TextureImporter':{'textureType':texture_type,'mipmaps':{'sRGBTexture':0,'enableMipMap':0},
        'textureSettings':dict(filterMode=0,aniso=1,mipBias=0,wrapU=0,wrapV=0,wrapW=0),
        'isReadable':0,'streamingMipmaps':1,'streamingMipmapsPriority':0,'maxTextureSize':2048,'platformSettings':[]}}


class OrdinaryTextureTests(unittest.TestCase):
    def bc6h_fixture(self,workers,*,broken_output=False):
        import portable_decoder,pointer_recovery
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        root=Path(temporary.name);project=root/'project';game=root/'game';cache=root/'cache'
        (project/'Assets').mkdir(parents=True);game.mkdir();cache.mkdir()
        container=game/'textures.bundle';container.write_bytes(b'original audited container')
        source_sha=hashlib.sha256(container.read_bytes()).hexdigest();rows=[];objects=[]
        parent=threading.get_ident();reads=[];active,peak=0,0;lock=threading.Lock()
        first_jobs=threading.Barrier(workers)
        for index in range(6):
            path=project/f'Assets/HDR{index}.png';path.write_bytes(b'original exported PNG'+bytes([index]))
            meta=Path(str(path)+'.meta');meta.write_bytes(b'original importer'+bytes([index]))
            fields={**native(),'m_Name':f'HDR{index}','m_TextureFormat':24,'m_StreamData':{'path':'','offset':0,'size':0}}
            original_bytes=b'exact native object'+bytes([index]);raw=bytes([index])*16
            def tree(fields=fields):
                self.assertEqual(threading.get_ident(),parent);reads.append(threading.get_ident());return fields
            def pixels(raw=raw):
                self.assertEqual(threading.get_ident(),parent);return types.SimpleNamespace(get_image_data=lambda:raw)
            objects.append(types.SimpleNamespace(assets_file=types.SimpleNamespace(name='cab-textures'),path_id=index+1,
                read_typetree=tree,get_raw_data=lambda raw=original_bytes:raw,read=pixels))
            rows.append({'assetPath':path.relative_to(project).as_posix(),'guid':f'{index+1:032x}','fileId':2800000,
                'sourceContainer':'textures.bundle','originalCollection':'cab-textures','originalPathId':index+1,
                'originalObjectSha256':hashlib.sha256(original_bytes).hexdigest(),'metaSha256':hashlib.sha256(meta.read_bytes()).hexdigest(),
                'native':full_texture2d.native_contract(fields),'nativeFloatOrHdr':True})
        audit={'schema':1,'mismatchedTextureCount':0,'incompleteMipChainCount':0,'floatOrHdrTextureCount':len(rows),
               'sourceContainers':[{'path':'textures.bundle','sha256':source_sha}],'assets':rows}
        environment=types.SimpleNamespace(objects=objects)
        def decode(command,args,cancellation):
            nonlocal active,peak
            index=Path(args[1]).read_bytes()[0]
            with lock:active+=1;peak=max(peak,active)
            if index<workers:first_jobs.wait(timeout=5)
            time.sleep(.04 if index%2==0 else .01);cancellation.check()
            pixels=struct.pack('<48e',*([float(index),-.125,1.5,1.0]*12))
            Path(args[2]).write_bytes(pixels[:-1] if broken_output else pixels)
            with lock:active-=1
        original_write=Path.write_text
        def write(path,*args,**kwargs):
            if project in path.parents:self.assertEqual(threading.get_ident(),parent)
            return original_write(path,*args,**kwargs)
        policy={'jobs':workers,'memoryBudgetBytes':workers*1024**3}
        with contextlib.redirect_stdout(io.StringIO()),mock.patch.object(pointer_recovery,'load_native',return_value=environment), \
             mock.patch.object(portable_decoder,'build',return_value=['fixture-codec']), \
             mock.patch.object(full_texture2d.host_resources,'phase_budget',return_value=policy), \
             mock.patch.object(full_texture2d.asset_jobs,'run_command',side_effect=decode), \
             mock.patch.object(Path,'write_text',write), \
             mock.patch('full_textures.restore_native_texture_pointer_types',return_value={'fixture':True}):
            if broken_output:
                with self.assertRaisesRegex(BuildError,'retain all native pixels'):
                    full_texture2d.restore_float_textures(project,game,audit,dotnet='unused',tool_cache=cache,cab_bundles={},jobs=workers)
                self.assertFalse((project/'Assets/QuestOriginalCampaign/native-texture2d.json').exists())
                self.assertFalse(any(cache.rglob('accepted.json')))
                self.assertTrue(all((project/row['assetPath']).exists() for row in rows))
                return None
            report=full_texture2d.restore_float_textures(project,game,audit,dotnet='unused',tool_cache=cache,cab_bundles={},jobs=workers)
        self.assertEqual(len(reads),len(rows));self.assertEqual(hashlib.sha256(container.read_bytes()).hexdigest(),source_sha)
        generated={str(path.relative_to(project)):path.read_bytes() for path in project.rglob('*') if path.is_file()}
        self.assertEqual([row['originalPathId'] for row in report['assets']],list(range(1,7)))
        return report,generated,peak

    def test_parallel_bc6h_preserves_serial_bytes_receipts_and_parent_reader_writes(self):
        serial=self.bc6h_fixture(1);parallel=self.bc6h_fixture(3)
        self.assertEqual(serial[:2],parallel[:2]);self.assertEqual(serial[2],1);self.assertEqual(parallel[2],3)

    def test_bad_parallel_pixels_never_acquire_cache_or_project_completion(self):
        self.bc6h_fixture(3,broken_output=True)

    def staged_textures(self, *, broken_audit=False, broken_pixels=False, receipt_failure=False):
        import portable_decoder,pointer_recovery,yaml
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        root=Path(temporary.name);project=root/'project';game=root/'game';cache=root/'cache'
        (project/'QuestRecovery').mkdir(parents=True);game.mkdir();cache.mkdir()
        (game/'textures.bundle').write_bytes(b'owned native textures')
        rows=[];objects=[]
        for index in range(3):
            ordinary=index<2;relative=f'Assets/Texture{index}'+('.png' if ordinary else '.texture2D')
            path=project/relative;path.parent.mkdir(exist_ok=True)
            png=b'\x89PNG\r\n\x1a\n'+struct.pack('>I',13)+b'IHDR'+struct.pack('>II',4,3)+b'\0'*8
            path.write_bytes(b'bad image' if broken_audit and index==1 else png)
            guid=f'{index+1:032x}'
            Path(str(path)+'.meta').write_text(yaml.safe_dump({'fileFormatVersion':2,'guid':guid,**importer()}))
            rows.append({'path':relative,'guid':guid,'objects':[{'collection':'cab-textures','pathId':index+1,'fileId':2800000,'classId':28}]})
            tree={**native(),'m_StreamData':{'path':'','offset':0,'size':0}}
            pixels=struct.pack('<48e',*([-3.25,12.5,0.125,1.0]*12))
            if broken_pixels and index==1:pixels=pixels[:-1]
            objects.append(types.SimpleNamespace(assets_file=types.SimpleNamespace(name='cab-textures'),path_id=index+1,
                read_typetree=lambda tree=tree:tree,get_raw_data=lambda index=index:b'exact native object '+bytes([index]),
                read=lambda pixels=pixels:types.SimpleNamespace(get_image_data=lambda:pixels)))
        (project/'QuestRecovery/original-asset-identities.json').write_text(json.dumps({'identities':rows}))
        environment=types.SimpleNamespace(objects=objects);stream=io.StringIO()
        writer=full_texture2d.write_json
        def write(path,value):
            if receipt_failure and Path(path).name=='native-texture2d.json':raise OSError('native receipt failed')
            return writer(path,value)
        with mock.patch.dict(os.environ,{full_texture2d.build_progress.ENV:'1'}),contextlib.redirect_stdout(stream), \
             mock.patch.object(full_texture2d.build_progress.time,'monotonic',side_effect=range(10000)), \
             mock.patch.object(pointer_recovery,'load_native',return_value=environment), \
             mock.patch.object(portable_decoder,'build',return_value=['fixture-codec']), \
             mock.patch.object(full_texture2d,'write_json',side_effect=write):
            if broken_audit or broken_pixels or receipt_failure:
                with self.assertRaises((BuildError,OSError)):
                    full_texture2d.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-textures':'textures.bundle'})
                report=None
            else:
                report=full_texture2d.stage(project,game,dotnet='unused',tool_cache=cache,cab_bundles={'cab-textures':'textures.bundle'})
                for row in report['assets']:
                    self.assertEqual(row['sha256'],hashlib.sha256((project/row['assetPath']).read_bytes()).hexdigest())
                audit=json.loads((project/'Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json').read_text())
                self.assertEqual((audit['ordinaryTexture2DCount'],audit['embeddedNativeTextureCount']),(2,1))
        prefix=full_texture2d.build_progress.PREFIX
        progress=[json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        return report,progress

    def test_audit_and_float_counters_count_actual_codec_targets(self):
        report,progress=self.staged_textures()
        self.assertEqual(report['nativeTexture2DCount'],2)
        for phase in ('prepare-items:ordinary-texture-audit','prepare-items:native-texture2d'):
            values=[row for row in progress if row['phase']==phase]
            self.assertEqual([(row['status'],row['done'],row['total']) for row in values],[('start',0,2),('progress',1,2),('complete',2,2)])

    def test_audit_failure_does_not_complete_or_start_float_conversion(self):
        _,progress=self.staged_textures(broken_audit=True)
        self.assertEqual(progress[-1]['status'],'failed')
        self.assertFalse(any(row['phase']=='prepare-items:native-texture2d' or row['status']=='complete' for row in progress))

    def test_invalid_native_pixels_never_count_as_accepted_texture(self):
        _,progress=self.staged_textures(broken_pixels=True)
        values=[row for row in progress if row['phase']=='prepare-items:native-texture2d']
        self.assertEqual(values[-1]['status'],'failed')
        self.assertEqual([row['done'] for row in values[:-1]],[0,1])

    def test_final_native_receipt_failure_never_reports_one_hundred_percent(self):
        _,progress=self.staged_textures(receipt_failure=True)
        values=[row for row in progress if row['phase']=='prepare-items:native-texture2d']
        self.assertEqual(values[-1]['status'],'failed')
        self.assertFalse(any(row['status']=='complete' or row['done']==2 for row in values))

    def test_float_vat_signed_out_of_range_components_are_byte_exact(self):
        pixels=struct.pack('<48e',*([-3.25,12.5,0.125,1.0]*12))
        text=full_texture2d.native_yaml(native(),pixels,2800000)
        restored=bytes.fromhex(text.split('_typelessdata: ')[1].split('\n')[0])
        self.assertEqual(restored,pixels)
        self.assertIn('m_TextureFormat: 17\n',text)
        self.assertIn('m_Height: 3\n',text)
        self.assertIn('m_FilterMode: 0\n',text)
        self.assertIn('m_IsReadable: 0\n',text)
        witness=full_texture2d.half_range_witness(pixels)
        self.assertEqual(witness['negativeComponent']['value'],-3.25)
        self.assertEqual(witness['aboveOneComponent']['value'],12.5)
        self.assertEqual(witness['unorm8PrecisionLossComponent']['value'],0.125)

    def test_rectangular_full_mips_and_invalid_partial_layout(self):
        self.assertEqual(full_texture2d.half_mip_sizes(4,2,3),[64,16,8])
        with self.assertRaises(BuildError):full_texture2d.half_mip_sizes(4,2,2)
        with self.assertRaises(BuildError):full_texture2d.native_yaml(native(),b'x'*95,2800000)
        fields=native();fields['m_PlatformBlob']=[1]
        with self.assertRaises(BuildError):full_texture2d.native_yaml(fields,b'x'*96,2800000)

    def test_sprite_importer_is_not_misclassified_as_normal_map_loss(self):
        contract=full_texture2d.native_contract(native())
        self.assertEqual(full_texture2d.importer_differences(contract,importer(8),(4,3)),[])
        fields=native();fields['m_LightmapFormat']=3
        contract=full_texture2d.native_contract(fields)
        self.assertEqual(full_texture2d.importer_differences(contract,importer(1),(4,3)),[])
        self.assertEqual(full_texture2d.importer_differences(contract,importer(8),(4,3))[0]['field'],'normalMapClassification')

    def test_dimensions_clamping_color_and_sampler_changes_are_reported(self):
        contract=full_texture2d.native_contract(native());document=importer()
        document['TextureImporter']['maxTextureSize']=2
        document['TextureImporter']['mipmaps']['sRGBTexture']=1
        document['TextureImporter']['textureSettings']['filterMode']=1
        fields={row['field'] for row in full_texture2d.importer_differences(contract,document,(2,2))}
        self.assertEqual(fields,{'maxTextureSize','srgb','filterMode','imageDimensions'})


if __name__=='__main__':unittest.main()
