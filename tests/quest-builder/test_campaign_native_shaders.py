"""Authored fixtures for actual native alias, collection, and ownership gates."""
import copy
from contextlib import redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import campaign_native_shaders as gate
from storage import BuildError

GUID = 'a' * 32
SECOND = 'b' * 32
MAT = 'c' * 32
PATH = 'Assets/Shader/Authored.shader'


def expected(guid=GUID, path=PATH):
    return {'guid': guid, 'assetPath': path, 'originalName': 'AuthoredFixture', 'variants': [
        {'subshader': 0, 'pass': 0, 'hardwareTier': 0, 'keywords': ['FEATURE'],
         'passType': 'Normal', 'coverageKind': 'original-native'}]}


def native():
    return {'platforms': [18], 'm_ShaderIsBaked': True, 'm_ParsedForm': {
        'm_Name': 'AuthoredFixture', 'm_KeywordNames': ['FEATURE'], 'm_SubShaders': [
            {'m_Passes': [{'progVertex': {'m_SubPrograms': [{'m_GpuProgramType': 25,
                'm_ShaderHardwareTier': 0, 'm_KeywordIndices': [0], 'm_BlobIndex': 0}]},
                'progFragment': {'m_SubPrograms': []}}]}]}}


def file(name, external=()):
    return types.SimpleNamespace(name=name, externals=[types.SimpleNamespace(path=p) for p in external])


def obj(kind, path_id, tree, owner):
    return types.SimpleNamespace(type=types.SimpleNamespace(name=kind), path_id=path_id,
        read_typetree=lambda: copy.deepcopy(tree), assets_file=owner)


def bundle(objects, roots, owner):
    directory = obj('AssetBundle', 142, {'m_Container': [(path, {'asset': {'m_FileID': 0, 'm_PathID': target}})
        for path, target in roots]}, owner)
    return types.SimpleNamespace(objects=[directory, *objects])


def collection(pointer):
    return {'m_Shaders': [(pointer, {'variants': [{'keywords': 'FEATURE', 'passType': 0}]})]}


def plan(with_material=True):
    return {'shaderRoots': {PATH.lower(): expected()}, 'materialRoots': ({'assets/material/a.mat': {
        'guid': MAT, 'assetPath': 'Assets/Material/a.mat', 'shaderGuid': GUID}} if with_material else {}),
        'publicMaterialCount': int(with_material), 'materialSampleCount': int(with_material)}


def source_mat(row):
    return {'enableInstancing': True, 'keywords': ['FEATURE'], 'invalidKeywords': [], 'sha256': '0' * 64}


def location(index, path, kind, deps=(), provider='UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider'):
    return {'index': index, 'internalId': path, 'resourceType': {'m_ClassName': kind},
        'provider': provider, 'dependencyEntries': list(deps)}


class NativeAliasTests(unittest.TestCase):
    def test_actual_combined_Vulkan_bank_needs_no_separate_fragment(self):
        result = gate.require_closure(native(), expected())
        self.assertTrue(result['allOriginalAliasesRetained'])
        self.assertTrue(result['stagesSerializedTogether'])

    def test_original_keyword_alias_loss_is_rejected(self):
        tree = native(); tree['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms'] = []
        with self.assertRaises(BuildError): gate.require_closure(tree, expected())

    def test_instancing_alias_is_never_silently_waived(self):
        row = expected(); row['variants'][0]['keywords'].append('INSTANCING_ON')
        self.assertEqual(gate.alias_report(native(), row)['missingInstancingAliasCount'], 1)
        with self.assertRaises(BuildError): gate.require_closure(native(), row)

    def test_tier_loss_is_rejected(self):
        row = expected(); row['variants'][0]['hardwareTier'] = 1
        with self.assertRaises(BuildError): gate.require_closure(native(), row)

    def test_invalid_native_keyword_index_rejects(self):
        for indices in ([1], [-1], [0, 0]):
            tree = native(); tree['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms'][0]['m_KeywordIndices'] = indices
            with self.assertRaises(BuildError): gate.require_closure(tree, expected())

    def test_extra_runtime_stereo_aliases_do_not_replace_original_aliases(self):
        tree = native(); parsed = tree['m_ParsedForm']; parsed['m_KeywordNames'].append('STEREO_MULTIVIEW_ON')
        actual = parsed['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms']
        extra = copy.deepcopy(actual[0]); extra['m_KeywordIndices'].append(1); actual.append(extra)
        self.assertTrue(gate.require_closure(tree, expected())['allOriginalAliasesRetained'])
        actual.pop(0)
        with self.assertRaises(BuildError): gate.require_closure(tree, expected())

    def test_nonvulkan_unbaked_or_changed_identity_rejects(self):
        for tree in [dict(native(), platforms=[11]), dict(native(), m_ShaderIsBaked=False)]:
            with self.assertRaises(BuildError): gate.require_closure(tree, expected())
        tree = native(); tree['m_ParsedForm']['m_Name'] = 'Hidden/InternalErrorShader'
        with self.assertRaises(BuildError): gate.require_closure(tree, expected())

    def test_coarse_collection_is_not_a_pass_tier_closure(self):
        row = expected(); alias = copy.deepcopy(row['variants'][0]); alias['hardwareTier'] = 1; row['variants'].append(alias)
        self.assertTrue(gate.svc_sample({GUID: {(0, ('FEATURE',))}}, row)['projectionExactlyPreserved'])
        with self.assertRaises(BuildError): gate.require_closure(native(), row)


class NativeOwnershipTests(unittest.TestCase):
    def context(self):
        shader_file = file('CAB-shader')
        material_file = file('CAB-material', ['archive:/CAB-shader/CAB-shader'])
        svc_file = file('CAB-svc', ['archive:/CAB-shader/CAB-shader'])
        shaders = bundle([obj('Shader', 48, native(), shader_file)], [(PATH, 48)], shader_file)
        materials = bundle([obj('Material', 21, {'m_Shader': {'m_FileID': 1, 'm_PathID': 48},
            'm_EnableInstancingVariants': True, 'm_ValidKeywords': ['FEATURE'], 'm_InvalidKeywords': []}, material_file)],
            [('Assets/Material/a.mat', 21)], material_file)
        svc = bundle([obj('ShaderVariantCollection', 200, collection({'m_FileID': 1, 'm_PathID': 48}), svc_file)],
            [(gate.COLLECTION_PATH, 200)], svc_file)
        return shaders, materials, svc

    def test_collection_and_material_deferred_exact_PPtr_link(self):
        shaders, materials, svc = self.context()
        audit = gate.NativeBundleAudit({GUID: expected()}, plan(), source_mat)
        with patch.object(gate, 'program_sample', return_value=[]):
            for env in (svc, materials, shaders): audit.observe(env, 'authored.bundle', '1' * 64)
        result = audit.finish()
        self.assertTrue(result['materials'][0]['exactNativeShaderPPtrVerified'])
        self.assertEqual(result['nativeVariantCollection']['projectedEntryCount'], 1)
        self.assertFalse(result['all9187MaterialsAudited'])
        self.assertEqual(audit.finish(), result)

    def test_missing_shader_root_rejects_despite_other_shader_same_name(self):
        shaders, materials, svc = self.context()
        shaders.objects[0].read_typetree = lambda: {'m_Container': [('Assets/Shader/Other.shader', {'asset': {'m_FileID': 0, 'm_PathID': 48}})]}
        audit = gate.NativeBundleAudit({GUID: expected()}, plan(), source_mat)
        for env in (shaders, materials, svc): audit.observe(env, 'authored.bundle', '1' * 64)
        with self.assertRaises(BuildError): audit.finish()

    def test_wrong_material_native_shader_pointer_rejects(self):
        shaders, materials, svc = self.context()
        changed = materials.objects[1].read_typetree(); changed['m_Shader']['m_PathID'] = 49
        materials.objects[1].read_typetree = lambda: changed
        audit = gate.NativeBundleAudit({GUID: expected()}, plan(), source_mat)
        with patch.object(gate, 'program_sample', return_value=[]):
            for env in (shaders, materials, svc): audit.observe(env, 'authored.bundle', '1' * 64)
        with self.assertRaises(BuildError): audit.finish()

    def test_unresolved_or_wrong_native_SVC_target_rejects(self):
        for pointer in ({'m_FileID': 1, 'm_PathID': 49}, {'m_FileID': 2, 'm_PathID': 48}, {'m_FileID': 0, 'm_PathID': 48}):
            shaders, materials, svc = self.context(); svc.objects[1].read_typetree = lambda pointer=pointer: collection(pointer)
            audit = gate.NativeBundleAudit({GUID: expected()}, plan(), source_mat)
            with patch.object(gate, 'program_sample', return_value=[]):
                for env in (shaders, materials, svc): audit.observe(env, 'authored.bundle', '1' * 64)
            with self.assertRaises(BuildError): audit.finish()

    def test_material_flag_or_keyword_change_rejects(self):
        for key, value in [('m_EnableInstancingVariants', False), ('m_ValidKeywords', []), ('m_InvalidKeywords', ['FEATURE'])]:
            _, materials, _ = self.context(); tree = materials.objects[1].read_typetree(); tree[key] = value
            materials.objects[1].read_typetree = lambda tree=tree: tree
            audit = gate.NativeBundleAudit({GUID: expected()}, plan(), source_mat)
            with self.assertRaises(BuildError): audit.observe(materials, 'authored.bundle', '1' * 64)

    def test_changed_coarse_collection_projection_rejects(self):
        for values in ({}, {GUID: set()}, {SECOND: {(0, ('FEATURE',))}}):
            with self.assertRaises(BuildError): gate.svc_sample(values, expected())

    def test_duplicate_native_owner_rejects(self):
        shaders, _, _ = self.context(); audit = gate.NativeBundleAudit({GUID: expected()}, plan(False), source_mat)
        with patch.object(gate, 'program_sample', return_value=[]):
            audit.observe(shaders, 'one.bundle', '1' * 64)
            with self.assertRaises(BuildError): audit.observe(shaders, 'two.bundle', '1' * 64)


class CatalogAndContractTests(unittest.TestCase):
    def decoded(self):
        return {'locations': [location(0, PATH, 'UnityEngine.Shader', [2]),
            location(1, gate.COLLECTION_PATH, 'UnityEngine.ShaderVariantCollection', [2]),
            location(2, '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/authored.bundle', 'bundle')]}

    def test_exact_native_catalog_plan_rejects_legacyresources_as_AA_owner(self):
        data = self.decoded(); result = gate.catalog_plan(data, {GUID: expected()}, sample_count=0)
        self.assertEqual(result['bundles'], ['StreamingAssets/aa/Android/authored.bundle'])
        data['locations'][0]['provider'] = 'UnityEngine.ResourceManagement.ResourceProviders.LegacyResourcesProvider'
        with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()})

    def test_missing_or_duplicate_shader_or_collection_catalog_roots_reject(self):
        for index in (0, 1):
            data = self.decoded(); data['locations'][index]['internalId'] += '.other'
            with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()})
            data = self.decoded(); extra = copy.deepcopy(data['locations'][index]); extra['index'] = 3; data['locations'].append(extra)
            with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()})

    def test_catalog_dependency_extent_or_path_escape_rejects(self):
        for bad in ('{UnityEngine.AddressableAssets.Addressables.RuntimePath}/../escape.bundle', 'https://host/native.bundle'):
            data = self.decoded(); data['locations'][2]['internalId'] = bad
            with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()})
        data = self.decoded(); data['locations'][0]['dependencyEntries'] = [99]
        with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()})

    def test_original_manifest_contract_rejects_synthetic_or_ambiguous_guid(self):
        data = {'schema': 1, 'scope': 'campaign-compiler', 'graphicsApi': 'Vulkan', 'compilerPlatform': 'Vulkan', 'shaders': [expected()]}
        self.assertEqual(len(gate.contract_rows(data)[0]), 1)
        with self.assertRaises(BuildError): gate.contract_rows(data, full=True)
        for altered in [dict(data, requiredSyntheticAliasCount=1), dict(data, shaders=[expected(), expected()])]:
            with self.assertRaises(BuildError): gate.contract_rows(altered)

    def test_material_selection_is_bounded_and_one_per_original_shader(self):
        data = self.decoded()
        data['locations'] += [location(3, 'Assets/M1.mat', 'UnityEngine.Material', [2]), location(4, 'Assets/M2.mat', 'UnityEngine.Material', [2])]
        rows = [{'guid': MAT, 'assetPath': 'Assets/M1.mat', 'shaderGuid': GUID}, {'guid': SECOND, 'assetPath': 'Assets/M2.mat', 'shaderGuid': GUID}]
        selected = gate.catalog_plan(data, {GUID: expected()}, rows, sample_count=1)
        self.assertEqual(selected['materialSampleCount'], 1); self.assertEqual(selected['publicMaterialCount'], 2)
        self.assertEqual(gate.catalog_plan(data, {GUID: expected()}, rows, sample_count=0)['materialSampleCount'], 0)
        with self.assertRaises(BuildError): gate.catalog_plan(data, {GUID: expected()}, rows, sample_count=65)

    def test_signed_player_SVC_opaque_guid_pointers_reject_forged_filename(self):
        tree = collection({'m_FileID': 1, 'm_PathID': 1})
        self.assertEqual(gate.svc_map(tree, [GUID]), {GUID: {(0, ('FEATURE',))}})
        with self.assertRaises(BuildError): gate.svc_map(tree, ['same-name.shader'])
        tree['m_Shaders'][0][0]['m_PathID'] = 2
        with self.assertRaises(BuildError): gate.svc_map(tree, [GUID])

    def test_duplicate_archive_members_reject(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'fixture.zip'
            with zipfile.ZipFile(path, 'w') as archive:
                archive.writestr('same', 'a')
                import warnings
                with warnings.catch_warnings():
                    warnings.simplefilter('ignore'); archive.writestr('same', 'b')
            with zipfile.ZipFile(path) as archive:
                with self.assertRaises(BuildError): gate.unique_zip(archive)


class ProgramDirectoryTests(unittest.TestCase):
    @staticmethod
    def code():
        payload = struct.pack('<6I', 0x534d4f4c, 0x01010000, 0, 1, 0, 20) + b'\0' * 4
        value = bytearray(52) + payload + payload
        struct.pack_into('<4I', value, 4, 52, len(payload), 52 + len(payload), len(payload))
        return bytes(value)

    def test_compiled_stage_table_preserves_two_actual_compressed_stages(self):
        self.assertEqual([s['nativeStageIndex'] for s in gate.stage_ranges(self.code())], [0, 1])

    def test_missing_overlapping_truncated_and_wrong_stage_headers_reject(self):
        for defect in ('missing', 'overlap', 'extent', 'magic', 'encoding', 'truncated'):
            value = bytearray(self.code())
            if defect == 'missing': struct.pack_into('<II', value, 12, 0, 0)
            if defect == 'overlap': struct.pack_into('<I', value, 12, 52)
            if defect == 'extent': struct.pack_into('<I', value, 8, len(value))
            if defect == 'magic': value[52] = 0
            if defect == 'encoding': struct.pack_into('<I', value, 56, 0x03010000)
            if defect == 'truncated': value = value[:12]
            with self.assertRaises(BuildError): gate.stage_ranges(value)

    def mocks(self):
        helper = types.ModuleType('UnityPy.helpers'); helper.CompressionHelper = types.SimpleNamespace(decompress_lz4=lambda raw, size: raw)
        converter = types.ModuleType('UnityPy.export.ShaderConverter')
        converter.ShaderSubProgram = lambda reader: types.SimpleNamespace(m_ProgramType=25, m_ProgramCode=self.code())
        streams = types.ModuleType('UnityPy.streams'); streams.EndianBinaryReader = lambda raw, endian: raw
        return {'UnityPy': types.ModuleType('UnityPy'), 'UnityPy.helpers': helper,
            'UnityPy.export': types.ModuleType('UnityPy.export'), 'UnityPy.export.ShaderConverter': converter, 'UnityPy.streams': streams}

    def tree(self):
        # Authored directory/header bytes; fake compression and code-body decoding
        # isolate real binary offset checks. Actual native codec proof is separate.
        body = struct.pack('<2i', 202012090, 25) + b'\0' * 24
        segment = struct.pack('<i3i', 1, 16, len(body), 0) + body
        value = native(); value.update(offsets=[[0]], compressedLengths=[[len(segment)]],
            decompressedLengths=[[len(segment)]], compressedBlob=list(segment))
        return value

    def test_native_program_directory_fixture_positive(self):
        with patch.dict(sys.modules, self.mocks()):
            self.assertEqual(len(gate.program_sample(self.tree(), [0])), 1)

    def test_all_alias_pointers_checked_even_outside_bounded_code_samples(self):
        value = self.tree(); programs = value['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms']
        extra = copy.deepcopy(programs[0]); extra['m_KeywordIndices'] = []; extra['m_BlobIndex'] = 99; programs.append(extra)
        with patch.dict(sys.modules, self.mocks()):
            with self.assertRaises(BuildError): gate.program_sample(value, [0])

    def test_invalid_directory_ranges_and_GPU_body_reject(self):
        for offset, length, segment, gpu in ((0, 32, 0, 25), (16, 999, 0, 25), (16, 32, 1, 25), (16, 32, 0, 17)):
            value = self.tree(); data = bytearray(value['compressedBlob']); struct.pack_into('<3i', data, 4, offset, length, segment); struct.pack_into('<i', data, 20, gpu); value['compressedBlob'] = list(data)
            with patch.dict(sys.modules, self.mocks()):
                with self.assertRaises(BuildError): gate.program_sample(value, [0])


class EarlyNativeDirectoryTests(unittest.TestCase):
    def setUp(self):
        import json
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.source = self.root / 'source'; self.project = self.root / 'project'; self.native = self.root / 'native'
        for folder in (self.source, self.project, self.native): folder.mkdir()
        self.decoder_path = self.source / 'tools/quest-recovery/catalog.py'; self.decoder_path.parent.mkdir(parents=True)
        self.decoder_path.write_text('# authored decoder fixture\n')
        self.manifest = self.project / 'Assets/QuestOriginalCampaign/campaign-shaders.json'; self.manifest.parent.mkdir(parents=True)
        self.manifest.write_text(json.dumps({'schema': 1, 'scope': 'campaign-compiler', 'graphicsApi': 'Vulkan',
            'compilerPlatform': 'Vulkan', 'shaders': [expected()], 'materials': [
                {'guid': MAT, 'assetPath': 'Assets/Material/a.mat', 'shaderGuid': GUID}]}))
        material = self.project / 'Assets/Material/a.mat'; material.parent.mkdir(parents=True); material.write_text('authored material bytes')
        self.catalog = self.native / 'catalog.json'; self.catalog.write_text('{}')
        self.bank = self.native / 'Android/authored.bundle'; self.bank.parent.mkdir(); self.bank.write_bytes(b'authored native bank fixture')
        self.evidence = self.root / 'evidence.json'
        self.decoded = {'locations': [location(0, PATH, 'UnityEngine.Shader', [3]),
            location(1, gate.COLLECTION_PATH, 'UnityEngine.ShaderVariantCollection', [3]),
            location(2, 'Assets/Material/a.mat', 'UnityEngine.Material', [3]),
            location(3, '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/authored.bundle', 'bundle')]}
        owner = file('CAB-authored')
        self.env = bundle([obj('Shader', 48, native(), owner),
            obj('Material', 21, {'m_Shader': {'m_FileID': 0, 'm_PathID': 48},
                'm_EnableInstancingVariants': True, 'm_ValidKeywords': ['FEATURE'], 'm_InvalidKeywords': []}, owner),
            obj('ShaderVariantCollection', 200, collection({'m_FileID': 0, 'm_PathID': 48}), owner)],
            [(PATH, 48), ('Assets/Material/a.mat', 21), (gate.COLLECTION_PATH, 200)], owner)
        # A deliberately small authored fixture, never called a688/full-game
        # PASS. Only the fixed full census is substituted; actual closure and
        # filesystem/public-pointer method bodies still execute unchanged.
        original = gate.contract_rows
        self.contract = patch.object(gate, 'contract_rows', side_effect=lambda manifest, *, full: original(manifest, full=False))
        self.contract_mock = self.contract.start()
        self.unity = patch.dict(sys.modules, {'UnityPy': types.SimpleNamespace(load=lambda _: self.env)}); self.unity.start()
        self.sample = patch.object(gate, 'program_sample', return_value=[]); self.sample.start()
        self.material = patch.object(gate, 'source_material', side_effect=lambda project, row: source_mat(row)); self.material.start()
        import campaign_shaders
        self.loader = patch.object(campaign_shaders, 'load', return_value=types.SimpleNamespace(decode_catalog=lambda _: self.decoded)); self.loader.start()

    def tearDown(self):
        for value in (self.loader, self.material, self.sample, self.unity, self.contract): value.stop()
        self.temp.cleanup()

    def run_gate(self):
        return gate.validate_native_directory(self.source, self.project, self.native, self.evidence, material_sample_count=1)

    def test_directory_mount_uses_same_exact_native_roots_and_truthful_preplayer_receipt(self):
        import json
        result = self.run_gate()
        self.assertEqual(result['scope'], 'actual-native-addressables-before-player')
        self.assertEqual(result['shaderCount'], 1); self.assertEqual(result['materialSampleCount'], 1)
        self.assertEqual(result['selectedNativeBundleCount'], 1)
        self.assertTrue(result['materials'][0]['exactNativeShaderPPtrVerified'])
        self.assertFalse(result['signedApkAuditPerformed']); self.assertFalse(result['hardwarePictureVerified'])
        self.assertFalse(result['allNativeContentFilesAudited']); self.assertEqual(result['compilerQueries'], 0)
        self.assertEqual(json.loads(self.evidence.read_text()), result)
        self.contract_mock.assert_called_once(); self.assertTrue(self.contract_mock.call_args.kwargs['full'])

    def test_modeled_windows_ctime_difference_preserves_entire_native_gate_receipt(self):
        baseline = self.run_gate(); actual_fstat = os.fstat
        def windows(fd):
            value = actual_fstat(fd)
            return types.SimpleNamespace(st_dev=value.st_dev, st_ino=value.st_ino, st_size=value.st_size,
                st_mtime_ns=value.st_mtime_ns, st_ctime_ns=value.st_ctime_ns + 123456789)
        with patch.object(gate.sys, 'platform', 'win32'), patch.object(gate.os, 'fstat', side_effect=windows):
            self.assertEqual(self.run_gate(), baseline)

    def test_progress_has_exact_bytes_bundles_and_public_roots_without_claiming_csharp_completion(self):
        baseline = self.run_gate(); output = io.StringIO()
        with patch.dict(os.environ, {'GHVRQ_WIZARD_PROGRESS': '1'}), redirect_stdout(output):
            self.assertEqual(self.run_gate(), baseline)
        rows = [json.loads(line.removeprefix('GHVRQ_PROGRESS ')) for line in output.getvalue().splitlines()]
        completed = {row['phase']: row for row in rows if row['status'] == 'complete'}
        self.assertEqual((completed['unity-native-shader-audit-read']['done'],
            completed['unity-native-shader-audit-read']['total']), (self.bank.stat().st_size, self.bank.stat().st_size))
        self.assertEqual((completed['unity-native-shader-audit-objects']['done'],
            completed['unity-native-shader-audit-objects']['total']), (3, 3))
        self.assertEqual(completed['unity-native-shader-audit-objects']['unit'], 'objects')
        bundles = [row for row in rows if row['phase'] == 'unity-native-shader-audit']
        self.assertEqual((bundles[-1]['done'], bundles[-1]['total'], bundles[-1]['status']), (1, 1, 'progress'))
        self.assertNotIn('unity-native-shader-audit', completed)
        self.assertTrue(all(row['operation'] == 'content-bank' for row in rows))

    def test_original_alias_loss_cannot_emit_root_completion_or_publish_evidence(self):
        shader = native(); shader['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms'] = []
        self.env.objects[1].read_typetree = lambda: shader
        output = io.StringIO()
        with patch.dict(os.environ, {'GHVRQ_WIZARD_PROGRESS': '1'}), redirect_stdout(output):
            with self.assertRaisesRegex(BuildError, 'strips original'): self.run_gate()
        rows = [json.loads(line.removeprefix('GHVRQ_PROGRESS ')) for line in output.getvalue().splitlines()]
        self.assertFalse(any(row['status'] == 'complete' for row in rows))
        self.assertFalse(self.evidence.exists())

    def test_failed_receipt_publication_does_not_close_native_objects_or_parent(self):
        output = io.StringIO()
        with patch.dict(os.environ, {'GHVRQ_WIZARD_PROGRESS': '1'}), redirect_stdout(output), \
                patch.object(gate, 'write_json', side_effect=OSError('fixture unavailable destination')):
            with self.assertRaisesRegex(OSError, 'unavailable'): self.run_gate()
        rows = [json.loads(line.removeprefix('GHVRQ_PROGRESS ')) for line in output.getvalue().splitlines()]
        self.assertFalse(any(row['status'] == 'complete' and row['phase'] in
            ('unity-native-shader-audit-objects', 'unity-native-shader-audit') for row in rows))
        self.assertFalse(self.evidence.exists())

    def test_missing_bank_symlink_file_ancestor_and_directory_are_rejected(self):
        target = self.root / 'external.bundle'; target.write_bytes(self.bank.read_bytes())
        self.bank.unlink()
        with self.assertRaises(BuildError): self.run_gate()
        self.bank.symlink_to(target)
        with self.assertRaises(BuildError): self.run_gate()
        self.bank.unlink(); self.bank.write_bytes(target.read_bytes())
        actual = self.native / 'Actual'; self.bank.parent.rename(actual)
        (self.native / 'Android').symlink_to(actual, target_is_directory=True)
        with self.assertRaises(BuildError): self.run_gate()
        (self.native / 'Android').unlink(); actual.rename(self.native / 'Android')
        alias = self.root / 'native-link'; alias.symlink_to(self.native, target_is_directory=True)
        with self.assertRaises(BuildError): gate.validate_native_directory(self.source, self.project, alias, self.evidence)
        self.assertFalse(self.evidence.exists())

    def test_junction_ancestor_is_rejected_when_host_exposes_public_path_api(self):
        with patch.object(Path, 'is_junction', lambda path: path == self.bank.parent, create=True):
            with self.assertRaises(BuildError): self.run_gate()
        self.assertFalse(self.evidence.exists())

    def test_unsafe_or_missing_native_catalog_dependencies_fail_before_parse(self):
        for suffix in ('../outside.bundle', 'Android/../authored.bundle', 'C:/outside.bundle', 'Android/missing.bundle'):
            with self.subTest(path=suffix):
                self.decoded['locations'][3]['internalId'] = '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/' + suffix
                with self.assertRaises(BuildError): self.run_gate()
        self.assertFalse(self.evidence.exists())

    def test_file_manifest_and_catalog_mutations_during_decode_are_rejected(self):
        for selected in (self.bank, self.catalog, self.manifest, self.decoder_path):
            before = selected.read_bytes()
            with self.subTest(path=selected.name):
                def changed(_): selected.write_bytes(before + b' '); return self.env
                with patch.dict(sys.modules, {'UnityPy': types.SimpleNamespace(load=changed)}):
                    with self.assertRaises(BuildError): self.run_gate()
                selected.write_bytes(before)
        self.assertFalse(self.evidence.exists())

    def test_original_alias_loss_and_wrong_native_collection_pointer_fail_before_receipt(self):
        shader = self.env.objects[1].read_typetree(); shader['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['progVertex']['m_SubPrograms'] = []
        self.env.objects[1].read_typetree = lambda: shader
        with self.assertRaises(BuildError): self.run_gate()
        self.env.objects[1].read_typetree = native
        self.env.objects[3].read_typetree = lambda: collection({'m_FileID': 0, 'm_PathID': 999})
        with self.assertRaises(BuildError): self.run_gate()
        self.assertFalse(self.evidence.exists())

    def test_cli_isolated_execution_bootstraps_sibling_modules_and_emits_one_error_line(self):
        import shutil
        import subprocess
        cli = self.root / 'cli'; cli.mkdir()
        shutil.copyfile(gate.__file__, cli / 'campaign_native_shaders.py')
        import storage
        shutil.copyfile(storage.__file__, cli / 'storage.py')
        result = subprocess.run([sys.executable, '-I', str(cli / 'campaign_native_shaders.py'),
            '--source', str(self.source), '--project', str(self.project), '--native-root', str(self.native),
            '--evidence', str(self.evidence)], text=True, capture_output=True)
        import json
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(result.stderr.splitlines()), 1)
        self.assertEqual(json.loads(result.stderr)['status'], 'failed')
        self.assertNotIn('No module named \'storage\'', result.stderr)
        self.assertFalse(result.stdout)


if __name__ == '__main__': unittest.main()
