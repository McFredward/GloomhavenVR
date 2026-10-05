"""Protect complete native Cubemap face/mip serialization and path identity."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-builder'))
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
