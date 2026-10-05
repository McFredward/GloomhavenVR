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
