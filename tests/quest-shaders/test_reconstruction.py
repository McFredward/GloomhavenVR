"""Native binding regressions with meaningful missing-data negative controls."""
import importlib.util
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-shaders'))
import converters
import produce

spec = importlib.util.spec_from_file_location('native_reconstruction', ROOT / 'tools/quest-builder/full_shaders.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def field(name, offset, columns=1):
    return {'name': name, 'byteOffset': offset, 'type': 0, 'rows': 1,
            'columns': columns, 'matrix': False, 'arraySize': 0}


class NativeBindings(unittest.TestCase):
    def instance_interface(self):
        return {'buffers': [{'name': 'UnityInstancing_Fixture', 'bytes': 64, 'fields': [],
            'structures': [{'name': 'FixtureArray', 'stride': 32, 'byteOffset': 0, 'arraySize': 2,
                            'fields': [field('_Tint', 0, 4), field('_Alpha', 16)]}]}],
            'bindings': [{'kind': 'cbuffer', 'slot': 0, 'name': 'UnityInstancing_Fixture'}]}

    def instance_hlsl(self, component='x', stride=2):
        return ('cbuffer cbNative : register(b17) { float4 cb0_0_m0[4] : packoffset(c0); };\n'
            'static uint gl_InstanceIndex; static float4 result;\nvoid vert_main() {\n'
            'uint index = gl_InstanceIndex * ' + str(stride) + 'u + 1u;\n'
            'result = cb0_0_m0[index].' + component + ';\n}\n')

    def test_flexible_instance_reads_preserve_runtime_elements(self):
        source, used = native.restore_uniforms(self.instance_hlsl(), self.instance_interface())
        self.assertIn('FixtureArray[((index) / 2)]._Alpha', source)
        self.assertNotIn('cb0_0_m0[0] =', source)
        self.assertIn('UNITY_DEFINE_INSTANCED_PROP(float, _Alpha)', source)
        self.assertEqual(used[0]['usedScalarIndices'], [4, 12])
        # A native instance after the compiler's minimum two elements addresses
        # that actual GPU instance, not a copied element zero or one.
        self.assertNotIn('FixtureArray[0]', source)

    def test_unexplained_instance_padding_and_wrong_stride_fail(self):
        for component, stride in [('w', 2), ('w', 3)]:
            with self.subTest(component=component, stride=stride):
                with self.assertRaisesRegex(native.ShaderRecoveryError, 'used cbuffer scalars'):
                    native.restore_uniforms(self.instance_hlsl(component, stride), self.instance_interface())

    def test_integer_ssa_keeps_all_possible_native_residues(self):
        body = 'uint index = inputIndex * 8u + 3u;\n'
        self.assertEqual(native.index_residues('index', body, 8), {3})
        self.assertEqual(native.index_residues('unknownIndex', body, 8), set(range(8)))
        self.assertEqual(native.index_residues('index', 'uint index = inputIndex * 3u + 1u;', 8), set(range(8)))

    def test_original_typed_buffer_word_addresses_and_strides(self):
        interface = {'buffers': [], 'bindings': [{'kind': 'buffer', 'slot': 2, 'name': '_NativeWords', 'arraySize': 1}]}
        hlsl = 'Buffer<uint4> t2 : register(t27);\nvoid frag_main() { uint value = t2.Load((index * 4u) + 2u).x; }\n'
        layout = [{'kind': 'structured', 'slot': 2, 'strideBytes': 16}]
        result, _ = native.restore_uniforms(hlsl, interface, resource_layouts=layout)
        self.assertIn('StructuredBuffer<QuestNativeBufferWords4> _NativeWords;', result)
        self.assertIn('_NativeWords[((index * 4u) + 2u) / 4u].words[((index * 4u) + 2u) % 4u]', result)
        self.assertNotIn('register(', result)
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'stride witness'):
            native.restore_uniforms(hlsl, interface)
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'non-scalar'):
            native.restore_uniforms(hlsl.replace('.x;', '.y;'), interface, resource_layouts=layout)

    def test_comparison_sampler_uses_original_register_owner(self):
        interface = {'buffers': [], 'bindings': [{'kind': 'texture', 'slot': 3, 'name': '_ShadowMapTexture', 'samplerSlot': 1, 'dimension': 6}]}
        hlsl = ('TextureCube<float4> t3 : register(t19);\nSamplerComparisonState s1 : register(s9);\n'
                'void frag_main() { float value = t3.SampleCmpLevelZero(s1, direction, depth); }')
        result, _ = native.restore_uniforms(hlsl, interface)
        self.assertIn('SamplerComparisonState sampler_ShadowMapTexture;', result)
        self.assertIn('_ShadowMapTexture.SampleCmpLevelZero(sampler_ShadowMapTexture, direction, depth)', result)
        interface['bindings'].append({'kind': 'texture', 'slot': 4, 'name': '_Other', 'samplerSlot': 1, 'dimension': 2})
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'ambiguous texture ownership'):
            native.restore_uniforms(hlsl, interface)

    def test_unknown_resource_register_fails_instead_of_remaining_remapped(self):
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'unbound original resource'):
            native.restore_uniforms('RWBuffer<uint> mystery : register(u7);\nvoid frag_main() {}', {'buffers': [], 'bindings': []})

    def test_actual_dxbc_structured_declaration_stride_is_witnessed(self):
        instructions = [0x50, 6, 0x040000a2, 0x107000, 2, 16]
        code = struct.pack('<6I', *instructions)
        chunk = b'SHDR' + struct.pack('<I', len(code)) + code
        size = 36 + len(chunk)
        dxbc = b'DXBC' + bytes(16) + struct.pack('<4I', 1, size, 1, 36) + chunk
        self.assertEqual(native.native_buffer_layouts(dxbc), [{'slot': 2, 'kind': 'structured', 'strideBytes': 16}])
        damaged = bytearray(dxbc)
        struct.pack_into('<I', damaged, 56, 0x0)
        with self.assertRaises(native.ShaderRecoveryError):
            native.native_buffer_layouts(bytes(damaged))


class ReconstructionGraph(unittest.TestCase):
    def test_native_hardware_tier_differences_are_retained(self):
        rows = [{'stage': 'vertex', 'keywords': ['DIRECTIONAL'], 'hardwareTier': tier,
                 'originalDxbcSha256': ('a' if tier < 2 else 'b') * 64, 'originalInterfaceSha256': 'c' * 64}
                for tier in range(3)]
        banks = produce._banks(rows, 'vertex')
        self.assertEqual(len(banks), 3)
        self.assertEqual(produce._selected(banks, ['DIRECTIONAL', 'UNITY_HARDWARE_TIER3'])['originalDxbcSha256'], 'b' * 64)
        rows[2]['originalDxbcSha256'] = 'a' * 64
        self.assertEqual(len(produce._banks(rows, 'vertex')), 1)

    def test_native_constant_stage_still_receives_unity_stereo_interface(self):
        source = 'struct SPIRV_Cross_Output {\n float4 gl_Position : SV_Position;\n};\nvoid vert_main() {}\nSPIRV_Cross_Output main() { SPIRV_Cross_Output stage_output; return stage_output; }'
        result = native.stereo_wrapper(source, 'vertex')
        self.assertIn('QuestOriginalVertex(SPIRV_Cross_Input stage_input)', result)
        self.assertIn('UNITY_SETUP_INSTANCE_ID(stage_input)', result)
        self.assertIn('UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(stage_output)', result)

    def test_converter_hash_failure_rejects_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'counterfeit.zip'
            archive.write_bytes(b'not-the-pinned-open-source-package')
            with self.assertRaisesRegex(RuntimeError, 'package bytes differ'):
                converters.ensure(Path(directory) / 'cache', archive, platform='win64')


if __name__ == '__main__':
    unittest.main()
