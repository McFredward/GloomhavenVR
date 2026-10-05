"""Protect original floating VAT pixels, rectangular HDR mips and importer scope."""
from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/quest-builder'))
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
