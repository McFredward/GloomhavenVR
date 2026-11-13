"""Bounded filename/Win32 checks; no shader compiler or Unity launch."""
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-shaders'))
import produce


class NativeProgramPaths(unittest.TestCase):
    def test_exact_pair_is_stable_unordered_and_not_truncated(self):
        keys = [('a' * 64, 'b' * 64), ('a' * 64, 'c' * 64), ('b' * 64, 'a' * 64)]
        actual = produce.program_paths(keys + keys)
        self.assertEqual(actual, produce.program_paths(reversed(keys)))
        self.assertEqual(len(set(actual.values())), 3)
        self.assertEqual(actual[keys[0]], produce.program_paths([keys[0]])[keys[0]])
        identity = b'quest-original-program-v1\0' + bytes.fromhex(keys[0][0]) + bytes.fromhex(keys[0][1])
        self.assertTrue(actual[keys[0]].endswith(hashlib.sha256(identity).hexdigest() + '.hlsl'))
        self.assertTrue(all(len(path) == 113 and len(Path(path).name) == 69 for path in actual.values()))
        for invalid in [('A' * 64, 'b' * 64), ('a' * 63, 'b' * 64), ('a' * 64, '../escape'), ['a' * 64, 'b' * 64]]:
            with self.subTest(invalid=invalid), self.assertRaises(produce.ValidationError):
                produce.program_paths([invalid])

    def test_collision_fails_before_a_program_can_be_replaced(self):
        with patch.object(produce.hashlib, 'sha256', return_value=SimpleNamespace(hexdigest=lambda: 'a' * 64)):
            with self.assertRaisesRegex(produce.ValidationError, 'collision'):
                produce.program_paths([('a' * 64, 'b' * 64), ('a' * 64, 'c' * 64)])

    def test_actual_generated_layout_counts_project_key_and_meta(self):
        key = ('a' * 64, 'b' * 64)
        short = produce.program_paths([key])[key]
        old = produce.PROGRAM_DIRECTORY + '/' + '-'.join(key) + '.hlsl'
        project = 'C:\\q\\projects\\' + 'c' * 64
        overlay = 'C:\\q\\tool-cache\\campaign-shaders\\' + 'd' * 64 + '\\overlay'
        self.assertEqual(produce.windows_path_budget(project, [short])['maximumPathUtf16Units'], 197)
        self.assertEqual(produce.windows_path_budget(overlay, [short])['maximumPathUtf16Units'], 224)
        self.assertEqual(len(old), 178)
        self.assertEqual(produce.windows_path_budget('C:\\', [old])['maximumPathUtf16Units'], 186)
        for root in (project, overlay, 'C:\\' + 'x' * 80 + '\\projects\\' + 'c' * 64):
            with self.subTest(root=root), self.assertRaisesRegex(produce.ValidationError, 'shorter build/cache root'):
                produce.windows_path_budget(root, [old if root in (project, overlay) else short])

    def test_full_utf16_and_component_boundaries_are_checked(self):
        # Supplementary Unicode code points use two UTF-16 units on Windows.
        accepted = produce.windows_path_budget('C:\\', ['x' * 249])
        self.assertEqual(accepted['maximumPathUtf16Units'], 257)
        with self.assertRaisesRegex(produce.ValidationError, 'budget'):
            produce.windows_path_budget('C:\\xx', ['x' * 249])
        with self.assertRaisesRegex(produce.ValidationError, 'component'):
            produce.windows_path_budget('C:\\', ['😀' * 128])
        for invalid in ('Assets/CON.shader', 'Assets/COM¹.hlsl', 'Assets/LPT².hlsl', 'Assets/bad?.hlsl',
                        'Assets/a./b.hlsl', 'Assets/a /b.hlsl', '../outside.hlsl', 'C:/outside.hlsl',
                        '/outside.hlsl', ''):
            with self.subTest(invalid=invalid), self.assertRaises(produce.ValidationError):
                produce.windows_path_budget('C:\\q', [invalid])
        for root in ('relative', 'C:relative', '\\\\?\\C:\\q', '\\\\.\\C:\\q', 'C:\\q\\..\\outside'):
            with self.subTest(root=root), self.assertRaises(produce.ValidationError):
                produce.windows_path_budget(root, ['Assets/a.hlsl'])

    def test_real_wizard_profile_projects_overlay_and_longer_profiles(self):
        program = produce.program_paths([('a' * 64, 'b' * 64)])[('a' * 64, 'b' * 64)]
        longest_shader = 'Assets/QuestRecoveredBundles/' + 'c' * 32 + '/' + 'f' * 58 + '.shader'
        self.assertEqual(len(longest_shader), 127)
        base = 'C:/Users/McFredward/.ghvrq/build'
        project = base + '/projects/' + 'd' * 64
        overlay = base + '/tool-cache/campaign-shaders/' + 'e' * 64 + '/overlay'
        self.assertEqual(produce.windows_path_budget(project, [program])['maximumPathUtf16Units'], 225)
        self.assertEqual(produce.windows_path_budget(project, [program, longest_shader])['maximumPathUtf16Units'], 239)
        self.assertEqual(produce.windows_path_budget(overlay, [program])['maximumPathUtf16Units'], 252)
        with self.assertRaisesRegex(produce.ValidationError, 'shorter build/cache root'):
            produce.windows_path_budget(overlay, [program, longest_shader])
        # A central short-cache layout can fit without renaming original Shader
        # files. The producer still checks whichever real roots the caller uses.
        compact = base + '/tool-cache/cs/' + 'e' * 64 + '/o'
        self.assertLess(produce.windows_path_budget(compact, [program, longest_shader])['maximumPathUtf16Units'], 260)
        longer = project.replace('McFredward', 'a' * 48)
        with self.assertRaisesRegex(produce.ValidationError, 'shorter build/cache root'):
            produce.windows_path_budget(longer, [program, longest_shader])

    def test_shaderlab_only_changes_include_operands(self):
        form = {'m_Name': 'Original/Fixture', 'm_PropInfo': {'m_Props': []}, 'm_SubShaders': [
            {'m_Tags': {'tags': []}, 'm_Passes': [{'m_Type': 0, 'm_State': {'lighting': False, 'm_Tags': {'tags': []}}}]}]}
        rows = [dict(stage=stage, subshader=0, **{'pass': 0}, hardwareTier=0, keywords=keywords,
                     originalDxbcSha256=hashlib.sha256((stage + str(n)).encode()).hexdigest(),
                     originalInterfaceSha256='c' * 64, fragmentOutput='color')
                for n, keywords in enumerate(([], ['ORIGINAL_ALPHA'])) for stage in ('vertex', 'fragment')]
        keys = [(row['originalDxbcSha256'], row['originalInterfaceSha256']) for row in rows]
        before = {key: produce.PROGRAM_DIRECTORY + '/' + '-'.join(key) + '.hlsl' for key in keys}
        after = produce.program_paths(keys)
        source_row = {'guid': 'd' * 32, 'variants': rows}
        original = copy.deepcopy((form, source_row))
        native = SimpleNamespace(tags=lambda _: '', render_state=lambda _: 'Cull Off')
        with patch.object(produce, 'recovery_module', return_value=native), patch.object(produce, '_fragment_eye', return_value=False), patch.object(produce, '_instance_layout', return_value=False), patch.object(produce, '_native_light_field', return_value=False):
            old_source, old_aliases = produce.shader_source(form, source_row, Path('.'), before)
            new_source, new_aliases = produce.shader_source(form, source_row, Path('.'), after)
        expected = old_source
        for key in keys:
            expected = expected.replace('#include "' + before[key] + '"', '#include "' + after[key] + '"')
        self.assertEqual(new_source, expected)
        self.assertEqual(old_aliases, new_aliases)
        self.assertEqual((form, source_row), original)
        self.assertEqual(len(re.findall(r'#include "Assets/QuestOriginalCampaign/ShaderPrograms/', new_source)), 4)

    def test_overlay_preserves_exact_program_bytes_shader_meta_and_material_identity(self):
        # These seams isolate filename production from native math recovery,
        # whose existing receipts remain separate. They do not prove GPU output.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project, cache, output = (root / name for name in ('project', 'cache', 'overlay'))
            (project / 'Assets').mkdir(parents=True)
            for folder in ('translated', 'interfaces', 'forms'):
                (cache / folder).mkdir(parents=True)
            raw = b'exact native instruction fixture'
            dxbc = hashlib.sha256(raw).hexdigest()
            interface = 'b' * 64
            guid = 'c' * 32
            content = 'float4 NativeOriginal(float4 value) { return value * 0.5f; }\n'
            bound = cache / 'bound.hlsl'
            bound.write_text(content)
            (cache / 'translated' / (dxbc + '.dxbc')).write_bytes(raw)
            (cache / 'interfaces' / (interface + '.json')).write_text('{}')
            form = 'd' * 64
            (cache / 'forms' / (form + '.json')).write_text('{}')
            source = project / 'Assets/Original.shader'
            source.write_text('Shader "Original/Fixture" { }\n')
            meta = source.with_suffix('.shader.meta')
            meta_bytes = ('fileFormatVersion: 2\nguid: ' + guid + '\nShaderImporter:\n  externalObjects: {}\n').encode()
            meta.write_bytes(meta_bytes)
            shader = {'guid': guid, 'assetPath': 'Assets/Original.shader', 'originalName': 'Original/Fixture',
                      'originalSerializedFile': 'original-cab', 'originalPathId': 42, 'originalParsedFormSha256': form,
                      'allOriginalInstructionsExtracted': True, 'allOriginalInterfacesBound': True,
                      'variants': [{'stage': 'vertex', 'originalDxbcSha256': dxbc, 'originalInterfaceSha256': interface,
                                    'boundHlslPath': str(bound), 'boundHlslSha256': produce.sha256(bound),
                                    'originalInputSignature': [], 'originalOutputSignature': []}]}
            material = {'guid': 'e' * 32, 'assetPath': 'Assets/Original.mat', 'shaderGuid': guid}
            inventory = root / 'inventory.json'
            inventory.write_text(json.dumps({'shaders': [shader], 'materials': [material]}))
            native = SimpleNamespace(dxbc_container=lambda _: (None, {}), portable_sampling_interface=lambda text: (text, []), stereo_wrapper=lambda text, *args, **kwargs: text)
            def wrapper(form, row, cache, includes, graphics_api):
                return 'Shader "Original/Fixture" {\n#include "' + next(iter(includes.values())) + '"\n}\n', []
            with patch.object(produce, 'recovery_module', return_value=native), patch.object(produce, 'shader_source', side_effect=wrapper), patch.object(produce.load_bounds, 'restore', side_effect=lambda text: (text, [])), patch.object(produce.integer_bits, 'restore', side_effect=lambda text: (text, [])), patch.object(produce.instance_nan, 'restore', side_effect=lambda text, _: (text, [])):
                actual = produce.restore_project(project, inventory, cache, output)
            program = actual['programs'][0]
            self.assertEqual((output / program['assetPath']).read_bytes(), bound.read_bytes())
            self.assertEqual(program['sourceSha256'], produce.sha256(bound))
            self.assertEqual(program['originalDxbcSha256'], dxbc)
            self.assertEqual(program['originalInterfaceSha256'], interface)
            self.assertEqual((output / 'Assets/Original.shader.meta').read_bytes(), meta_bytes)
            self.assertEqual(actual['materials'], [material])
            for field in ('guid', 'assetPath', 'originalName', 'originalSerializedFile', 'originalPathId'):
                self.assertEqual(actual['shaders'][0][field], shader[field])
            self.assertEqual(actual['programPathScheme'], produce.PROGRAM_PATH_SCHEME)
            self.assertEqual(source.read_text(), 'Shader "Original/Fixture" { }\n')

    def test_windows_preflight_rejects_before_creating_overlay(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inventory = root / 'inventory.json'
            inventory.write_text(json.dumps({'shaders': [{'assetPath': 'Assets/Original.shader', 'variants': []}]}))
            output = root / 'not-created'
            with patch.object(produce, 'os', SimpleNamespace(name='nt')), patch.object(produce, 'windows_path_budget', side_effect=produce.ValidationError('budget')) as check:
                with self.assertRaisesRegex(produce.ValidationError, 'budget'):
                    produce.restore_project(root / 'project', inventory, root / 'cache', output)
            self.assertEqual(check.call_count, 1)
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
