"""Native binding regressions with meaningful missing-data negative controls."""
import importlib.util
import copy
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-shaders'))
import converters
import produce
import retained
from manifest import sha256

spec = importlib.util.spec_from_file_location('native_reconstruction', ROOT / 'tools/quest-builder/full_shaders.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def field(name, offset, columns=1):
    return {'name': name, 'byteOffset': offset, 'type': 0, 'rows': 1,
            'columns': columns, 'matrix': False, 'arraySize': 0}


class NativeBindings(unittest.TestCase):
    def test_engine_guids_have_exact_native_extent(self):
        self.assertEqual(native.ENGINE_SHADER_GUIDS, {'0' * 16 + marker + '0' * 15 for marker in 'ef'})
        self.assertTrue(all(len(value) == 32 for value in native.ENGINE_SHADER_GUIDS))

    def test_engine_light_declaration_requires_actual_float4_native_layout(self):
        import json
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'interface.json'
            value = field('_LightColor0', 0, 4)
            path.write_text(json.dumps({'buffers': [{'fields': [value]}]}))
            self.assertTrue(produce._native_light_field(str(path)))
            produce._native_light_field.cache_clear()
            path.write_text(json.dumps({'buffers': [{'fields': [{**value, 'columns': 3}]}]}))
            with self.assertRaisesRegex(produce.ValidationError, 'native field layout'):
                produce._native_light_field(str(path))
            produce._native_light_field.cache_clear()

    def test_packed_stage_semantics_link_independently_of_register_numbers(self):
        rows = [{'register': 1, 'semantic': 'TEXCOORD', 'semanticIndex': 0, 'systemValue': 0, 'mask': 3, 'componentType': 3},
                {'register': 1, 'semantic': 'COLOR', 'semanticIndex': 0, 'systemValue': 0, 'mask': 12, 'componentType': 3}]
        vertex = 'struct SPIRV_Cross_Output { float4 o1 : TEXCOORD1; };\nvoid main(){ stage_output.o1 = o1; }'
        output = native.native_stage_interface(vertex, rows, 'output')
        self.assertIn('float2 questNative_TEXCOORD0 : TEXCOORD0;', output)
        self.assertIn('float2 questNative_COLOR0 : COLOR0;', output)
        self.assertIn('stage_output.questNative_COLOR0 = o1.zw;', output)
        fragment = 'struct SPIRV_Cross_Input { float4 v5 : TEXCOORD5; };\nvoid main(){ v5 = stage_input.v5; }'
        input_source = native.native_stage_interface(fragment, [{**row, 'register': 5} for row in rows], 'input')
        self.assertIn('v5 = float4(stage_input.questNative_TEXCOORD0.x, stage_input.questNative_TEXCOORD0.y, stage_input.questNative_COLOR0.x, stage_input.questNative_COLOR0.y);', input_source)
        # An original declared but unwritten packed lane is absent from the
        # real translated instruction interface, not supplied imaginary data.
        unused = native.native_stage_interface(vertex.replace('float4 o1', 'float2 o1'), rows, 'output')
        self.assertNotIn('questNative_COLOR0', unused)
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'overlap'):
            native.native_stage_interface(vertex, [rows[0], {**rows[1], 'mask': 3}], 'output')

    def test_fragment_keeps_exact_unused_native_declarations_and_user_sv_prefix(self):
        signatures = [{'register': 0, 'semantic': 'SV_POSITION', 'semanticIndex': 0, 'systemValue': 1, 'mask': 15, 'readWriteMask': 0, 'componentType': 3},
                      {'register': 1, 'semantic': 'TEXCOORD', 'semanticIndex': 0, 'systemValue': 0, 'mask': 3, 'readWriteMask': 3, 'componentType': 3},
                      {'register': 2, 'semantic': 'TEXCOORD', 'semanticIndex': 6, 'systemValue': 0, 'mask': 15, 'readWriteMask': 0, 'componentType': 3},
                      {'register': 3, 'semantic': 'SV_InstanceID', 'semanticIndex': 0, 'systemValue': 0, 'mask': 1, 'readWriteMask': 1, 'componentType': 1}]
        source = ('struct SPIRV_Cross_Input { float2 v1 : TEXCOORD0; nointerpolation uint v3 : SV_InstanceID0; };\n'
                  'struct SPIRV_Cross_Output { float4 o0 : SV_Target0; };\n'
                  'SPIRV_Cross_Output main(SPIRV_Cross_Input stage_input) { v1 = stage_input.v1; v3 = stage_input.v3; return stage_output; }')
        result = native.stereo_wrapper(source, 'fragment', input_signature=signatures)
        self.assertIn('questNativeUnused_SV_POSITION0 : SV_POSITION0;', result)
        self.assertIn('questNativeUnused_TEXCOORD6 : TEXCOORD6;', result)
        self.assertIn('v3 = stage_input.questNative_SV_InstanceID0;', result)
        self.assertLess(result.index('questNativeUnused_SV_POSITION0'), result.index('questNative_TEXCOORD0'))
        self.assertLess(result.index('questNativeUnused_TEXCOORD6'), result.index('questNative_SV_InstanceID0'))
        # A real read must have a translated transfer; no missing native value
        # is silently supplied merely to make a stage signature compile.
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'read input is absent'):
            native.stereo_wrapper(source, 'fragment', input_signature=[{**r, 'readWriteMask': 1} if r['semanticIndex'] == 6 else r for r in signatures])

    def test_anonymous_input_retains_original_mixed_register_identity(self):
        signatures = [{'register': 9, 'semantic': 'SV_InstanceID', 'semanticIndex': 0,
                       'systemValue': 0, 'mask': 1, 'readWriteMask': 1, 'componentType': 1},
                      {'register': 9, 'semantic': 'SV_IsFrontFace', 'semanticIndex': 0,
                       'systemValue': 9, 'mask': 2, 'readWriteMask': 2, 'componentType': 1}]
        source = ('struct SPIRV_Cross_Input { nointerpolation uint _87 : TEXCOORD9; '
                  'bool gl_FrontFacing : SV_IsFrontFace; };\n'
                  'struct SPIRV_Cross_Output { float4 o0 : SV_Target0; };\n'
                  'SPIRV_Cross_Output main(SPIRV_Cross_Input stage_input) { '
                  '_87 = stage_input._87; return stage_output; }')
        result = native.stereo_wrapper(source, 'fragment', input_signature=signatures)
        self.assertIn('_87 = stage_input.questNative_SV_InstanceID0;', result)
        self.assertIn('bool gl_FrontFacing : SV_IsFrontFace;', result)
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'register identity'):
            native.stereo_wrapper(source.replace('TEXCOORD9', 'UNKNOWN9'), 'fragment', input_signature=signatures)

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

    def test_cube_comparison_matches_native_unity_platform_sampling(self):
        source = ('TextureCube<float4> _CubeShadow;\nTexture2D<float4> _DepthShadow;\n'
                  'float a = _CubeShadow.SampleCmpLevelZero(sampler_CubeShadow, xyz, depth);\n'
                  'float b = _DepthShadow.SampleCmpLevelZero(sampler_DepthShadow, xy, depth);\n')
        result, adapters = native.portable_sampling_interface(source)
        self.assertIn('_CubeShadow.QUEST_NATIVE_CUBE_SHADOW_COMPARE(sampler_CubeShadow, xyz, depth)', result)
        self.assertIn('_DepthShadow.SampleCmpLevelZero(sampler_DepthShadow, xy, depth)', result)
        self.assertIn('#define QUEST_NATIVE_CUBE_SHADOW_COMPARE SampleCmpLevelZero', result)
        self.assertIn('defined(SHADER_API_GLES3)', result)
        self.assertEqual(adapters[0]['texture'], '_CubeShadow')
        self.assertEqual(adapters[0]['instructionCount'], 1)
        # An untyped/2D native sampler must never be rewritten by a name guess.
        unchanged, none = native.portable_sampling_interface(source.replace('TextureCube<float4>', 'Texture2D<float4>'))
        self.assertEqual(unchanged, source.replace('TextureCube<float4>', 'Texture2D<float4>'))
        self.assertEqual(none, [])

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
    def test_native_exclusive_keyword_choices_have_no_invented_empty_bank(self):
        banks = {('POINT',): {}, ('SPOT',): {}, ('DIRECTIONAL',): {},
                 ('POINT', 'SHADOWS_CUBE'): {}, ('SPOT', 'SHADOWS_DEPTH'): {}}
        keys = {'POINT', 'SPOT', 'DIRECTIONAL', 'SHADOWS_CUBE', 'SHADOWS_DEPTH'}
        rows = produce._keyword_pragmas(banks, banks, keys, set())
        self.assertIn('#pragma multi_compile DIRECTIONAL POINT SPOT', rows)
        self.assertNotIn('#pragma shader_feature POINT', rows)
        # Optional native shadows still retain their genuinely unshadowed bank.
        self.assertIn('#pragma shader_feature SHADOWS_CUBE', rows)
        shadows = {('SHADOWS_CUBE',): {}, ('SHADOWS_DEPTH',): {}}
        self.assertEqual(produce._keyword_pragmas(shadows, shadows, set().union(*shadows), set()),
                         ['#pragma multi_compile SHADOWS_CUBE SHADOWS_DEPTH'])

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

class VulkanNativeInterface(unittest.TestCase):
    def test_full_producer_backend_reaches_shaderlab_wrapper(self):
        import inspect
        self.assertEqual(inspect.signature(produce.shader_source).parameters['graphics_api'].default, 'Vulkan')
        self.assertEqual(inspect.signature(produce.restore_project).parameters['graphics_api'].default, 'Vulkan')


    def test_shipping_manifest_retains_native_keywords_without_synthetic_eye_claims(self):
        from unittest.mock import patch
        from types import SimpleNamespace
        form = {'m_Name': 'NativeFixture', 'm_PropInfo': {'m_Props': []}, 'm_SubShaders': [
            {'m_Tags': {'tags': []}, 'm_Passes': [{'m_Type': 0, 'm_State': {'m_Tags': {'tags': []}}}]}]}
        rows = [dict(stage=stage, subshader=0, **{'pass': 0}, hardwareTier=0, keywords=keys,
                     originalDxbcSha256=stage + str(n), originalInterfaceSha256='interface', fragmentOutput='color')
                for n, keys in enumerate(([], ['NATIVE_ALPHA'])) for stage in ('vertex', 'fragment')]
        includes = {(r['originalDxbcSha256'], r['originalInterfaceSha256']): 'Assets/' + r['originalDxbcSha256'] + '.hlsl' for r in rows}
        native_stub = SimpleNamespace(tags=lambda _: '', render_state=lambda _: 'Cull Off')
        with patch.object(produce, 'recovery_module', return_value=native_stub), patch.object(produce, '_fragment_eye', return_value=False), patch.object(produce, '_instance_layout', return_value=False), patch.object(produce, '_native_light_field', return_value=False):
            source, shipping = produce.shader_source(form, {'guid': 'a'*32, 'variants': rows}, Path('.'), includes, 'Vulkan')
            _, historical_probe = produce.shader_source(form, {'guid': 'a'*32, 'variants': rows}, Path('.'), includes, 'GLES3')
        self.assertEqual([v['keywords'] for v in shipping], [[], ['NATIVE_ALPHA']])
        self.assertTrue(all(v['coverageKind'] == 'original-native' for v in shipping))
        self.assertEqual(len(historical_probe), 4)
        self.assertEqual(sum(v['coverageKind'] == 'quest-synthetic' for v in historical_probe), 2)
        self.assertIn('#pragma multi_compile __ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON', source)
        with patch.object(produce, 'recovery_module', return_value=native_stub), patch.object(produce, '_fragment_eye', return_value=False), patch.object(produce, '_instance_layout', return_value=True), patch.object(produce, '_native_light_field', return_value=False):
            instanced, _ = produce.shader_source(form, {'guid': 'a'*32, 'variants': rows}, Path('.'), includes, 'Vulkan')
        self.assertIn('#pragma skip_optimizations gles3 vulkan', instanced)
        self.assertNotIn('#pragma skip_optimizations', source)

    def test_vulkan_recovers_exact_original_layer_semantic(self):
        source = ('struct SPIRV_Cross_Input { float4 position : POSITION0; };\n'
                  'struct SPIRV_Cross_Output { float4 gl_Position : SV_Position; uint o1 : TEXCOORD3;\n};\n'
                  'SPIRV_Cross_Output main(SPIRV_Cross_Input stage_input) { '
                  'SPIRV_Cross_Output stage_output; stage_output.o1=uint(_DepthSlice); return stage_output; }')
        adapter = {'kind': 'native-vertex-layer-to-unity-framebuffer', 'nativeOutput': 'o1', 'portableLocation': 3}
        signature = [{'semantic': 'SV_RenderTargetArrayIndex', 'register': 1, 'componentType': 1, 'mask': 1, 'systemValue': 4}]
        actual = native.stereo_wrapper(source, 'vertex', [adapter], output_signature=signature, graphics_api='Vulkan')
        self.assertIn('o1 : SV_RenderTargetArrayIndex;', actual)
        self.assertIn('stage_output.o1=uint(_DepthSlice);', actual)
        self.assertIn('UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX', actual)
        self.assertNotIn('stereoTargetEyeIndexAsRTArrayIdx', actual)
        signed_source = 'static int o1;\n' + source.replace('uint o1', 'int o1').replace('stage_output.o1=uint(_DepthSlice);', 'o1=int(uint(_DepthSlice)); stage_output.o1=o1;')
        signed = native.stereo_wrapper(signed_source, 'vertex', [adapter], output_signature=signature, graphics_api='Vulkan')
        self.assertIn('uint o1 : SV_RenderTargetArrayIndex;', signed)
        self.assertIn('static int o1;', signed)
        self.assertIn('o1=int(uint(_DepthSlice));', signed)
        self.assertIn('stage_output.o1 = asuint(o1);', signed)
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'exact native scalar uint'):
            native.stereo_wrapper(source, 'vertex', [adapter], output_signature=[{**signature[0], 'componentType': 2}], graphics_api='Vulkan')
        with self.assertRaisesRegex(native.ShaderRecoveryError, 'witnessed native output'):
            native.stereo_wrapper(source.replace('TEXCOORD3', 'TEXCOORD4'), 'vertex', [adapter], output_signature=signature, graphics_api='Vulkan')

class RetainedImportContract(unittest.TestCase):
    def test_only_exact_original_retained_role_and_receipt_accept_import_upgrade(self):
        with tempfile.TemporaryDirectory() as temp:
            project = Path(temp); receipt = project / 'QuestStartupEvidence/legacy-post-effects.json'
            receipt.parent.mkdir()
            for guid, pin in retained.PINS.items():
                name, path_id, original, upgraded, recipe, replacements = pin
                proof = dict(guid=guid, name=name, assetPath='Assets/Shader/Original.shader', originalPathId=path_id,
                    sourceSha256=original, canonicalRecipeSha256=recipe,
                    importUpgrade=dict(kind='UnityObjectToClipPos', replacements=replacements, sha256=upgraded))
                receipt.write_text(json.dumps(dict(shaders=[proof])))
                row = dict(guid=guid, originalName=name, assetPath=proof['assetPath'], originalPathId=path_id,
                    sourceSha256=original, sourceRestoration='retained-source-contract',
                    retainedSourceContract=dict(sourceSha256=original, originalProvenance=dict(
                        receipt='QuestStartupEvidence/legacy-post-effects.json', receiptSha256=sha256(receipt), shader=proof)))
                self.assertTrue(retained.source_matches(row, upgraded, project))
                self.assertFalse(retained.source_matches(row, 'f' * 64, project))
                for mutation in (dict(guid='f' * 32), dict(originalPathId=path_id + 1),
                                 dict(sourceRestoration='exact-original-dxbc'), dict(retainedSourceContract={})):
                    self.assertFalse(retained.source_matches({**row, **mutation}, upgraded, project))
                changed = copy.deepcopy(row); changed['retainedSourceContract']['originalProvenance']['shader']['canonicalRecipeSha256'] = 'f' * 64
                self.assertFalse(retained.source_matches(changed, upgraded, project))
                receipt.write_text('{}')
                self.assertFalse(retained.source_matches(row, upgraded, project))
