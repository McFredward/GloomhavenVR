"""Filesystem/provenance defect controls; synthetic compiler records are fixtures."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
import stereo_eye_repair as repair
import build_provenance
from storage import BuildError, digest, value_hash


def write(path, raw):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)


def identity(project, row, guid=False):
    result = {'assetPath': row['assetPath'], 'sha256': digest(project / row['assetPath']),
              'metaSha256': digest(project / (row['assetPath'] + '.meta'))}
    if guid: result['guid'] = row['guid']
    return result


def populate(project, proof, manifest=None):
    """Synthetic three-native-pair closure, with exact witness mode dimensions."""
    manifest = manifest or {'schema': 1, 'scope': 'campaign-compiler', 'graphicsApi': 'Vulkan',
        'shaders': [], 'materials': [], 'programs': [], 'requiredShaderCount': 0,
        'requiredMaterialCount': 0, 'requiredOriginalNativeAliasCount': 0, 'requiredSyntheticAliasCount': 0}
    shader = {'assetPath': 'Assets/Original/FinalPass.shader', 'guid': repair.FINALPASS_GUID,
              'originalName': 'Hidden/PostProcessing/FinalPass', 'variants': []}
    write(project / shader['assetPath'], b'// original shader fixture\n')
    write(project / (shader['assetPath'] + '.meta'), ('guid: ' + shader['guid'] + '\n').encode())
    shader['sourceSha256'] = digest(project / shader['assetPath'])
    for i, (dxbc, interface) in enumerate(sorted(repair.PINS)):
        name = 'Assets/Original/Programs/Layer' + str(i) + '.hlsl'
        raw = b'// original native declarations\nstruct Output {\n' + repair.FIELDS + b'};\nvoid main() {\n' + repair.INITIALIZE + b'    original_math();\n}\n'
        write(project / name, raw)
        write(project / (name + '.meta'), ('guid: ' + format(i + 1, '032x') + '\n').encode())
        manifest['programs'].append({'assetPath': name, 'sourceSha256': digest(project / name),
            'originalDxbcSha256': dxbc, 'originalInterfaceSha256': interface, 'originalInputSignature': [],
            'originalOutputSignature': [{'systemValue': 4, 'semantic': 'SV_RenderTargetArrayIndex', 'componentType': 1, 'mask': 1, 'register': 2}],
            'outputInterfaceAdapters': [{'kind': 'native-vertex-layer-to-unity-framebuffer', 'nativeOutput': 'o2'}]})
    dxs = sorted({pair[0] for pair in repair.PINS})
    for sub, dx in enumerate(dxs):
        for tier in range(3):
            shader['variants'].append({'subshader': sub, 'pass': 0, 'hardwareTier': tier, 'passType': 'Normal', 'keywords': ['STEREO_INSTANCING_ENABLED'],
                'vertexOriginalDxbcSha256': dx, 'fragmentOriginalDxbcSha256': format(sub + 1, '064x')})
    uber = {'assetPath': 'Assets/Original/Uber.shader', 'guid': repair.UBER_GUID,
        'originalName': 'Hidden/PostProcessing/Uber', 'variants': [{**row, 'subshader': 0} for row in shader['variants'] if row['subshader'] == 1]}
    write(project / uber['assetPath'], b'// original Uber shader fixture\n')
    write(project / (uber['assetPath'] + '.meta'), ('guid: ' + uber['guid'] + '\n').encode())
    uber['sourceSha256'] = digest(project / uber['assetPath'])
    for owner, uber_owner in ((shader, False), (uber, True)):
        includes = [row['assetPath'] for row in manifest['programs'] if (row.get('originalInterfaceSha256', '').startswith('741146')) == uber_owner and (row.get('originalDxbcSha256'), row.get('originalInterfaceSha256')) in repair.PINS]
        write(project / owner['assetPath'], ('// original ShaderLab fixture\n' + ''.join('#include "' + name + '"\n' for name in includes)).encode())
        owner['sourceSha256'] = digest(project / owner['assetPath'])
    manifest['shaders'].extend([shader, uber]); manifest['requiredShaderCount'] += 2
    manifest['requiredOriginalNativeAliasCount'] += 9
    write(project / repair.MANIFEST, repair.encoded(manifest))
    rows, _ = repair.target_rows(manifest)
    inputs = {'schema': 1, 'shaderGuid': repair.FINALPASS_GUID, 'shaderPath': shader['assetPath'],
        'shaderSha256': shader['sourceSha256'], 'shaderMetaSha256': digest(project / (shader['assetPath'] + '.meta')),
        'sourceManifestSha256': digest(project / repair.MANIFEST), 'shaders': [identity(project, owner, True) for owner in (shader, uber)], 'rewrites': [], 'variants': []}
    for row in inputs['shaders']: row['sourceSha256'] = row.pop('sha256')
    for row in rows:
        raw = (project / row['assetPath']).read_bytes()
        inputs['rewrites'].append({**{key: row[key] for key in ('assetPath', 'originalDxbcSha256', 'originalInterfaceSha256')},
            'beforeSha256': repair.hash_bytes(raw), 'afterSha256': repair.hash_bytes(repair.rewrite(raw)),
            'metaSha256': digest(project / (row['assetPath'] + '.meta'))})
    banks = []
    for uber_mode, subs in ((False, (0, 1)), (True, (0,))):
        for sub in subs:
            for tier in range(3):
                for mode in ('mono', 'instancing', 'multiview'):
                    variant = {'subshader': sub, 'pass': 0, 'hardwareTier': tier, 'stereo': mode,
                        'vertexOriginalDxbcSha256': dxs[1 if uber_mode else sub], 'fragmentOriginalDxbcSha256': format((1 if uber_mode else sub) + 1, '064x'),
                        'keywords': ['STEREO_INSTANCING_ENABLED'] + {'mono': [], 'instancing': ['STEREO_INSTANCING_ON'], 'multiview': ['STEREO_MULTIVIEW_ON']}[mode]}
                    if uber_mode: variant.update(shaderPath=uber['assetPath'],
                        originalInterfaceSha256='741146a3ced34e312d9020e8dccaa56fbb5f606419ea804f519697b68a5b16be')
                    inputs['variants'].append(variant)
                    bank = {'variant': copy.deepcopy(variant), 'bankSha256': 'a' * 64, 'outputs': [{'stage': 'vertex', 'builtin': 9, 'components': 1, 'bitWidth': 32}],
                        'unchangedBaseline': mode != 'multiview', 'baselineError': 'DEFAULT_UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX' if mode == 'multiview' else ''}
                    for stage in ('vertex', 'fragment'):
                        name = str(len(banks)) + '.' + stage + '.spv'; raw = b'synthetic test fixture ' + stage.encode()
                        write(proof / 'FinalPassWitness' / name, raw)
                        bank[stage + 'File'] = name; bank[stage + 'Sha256'] = repair.hash_bytes(raw)
                    banks.append(bank)
    write(proof / 'WitnessInput.json', repair.encoded(inputs))
    witness = {'schema': 1, 'scope': repair.SCOPE, 'unityVersion': '2021.3.5f1', 'inputSha256': digest(proof / 'WitnessInput.json'),
        'baselineRejectedCount': 9, 'positiveCompilerCount': 27, 'monoInstancingExactStageCount': 18, 'rewriteCount': 3,
        'originalDeclarationsAndMathPreserved': True, 'originalShaderAndMetaPreserved': True,
        'headsetPictureVerified': False, 'originalPixelParityVerified': False, 'banks': banks}
    write(proof / 'FinalPassWitness/results.json', repair.encoded(witness))
    return manifest, inputs, witness


class StereoEyeRepairTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='quest eye & 100% ')
        self.root = Path(self.temp.name); self.project = self.root / 'project'; self.proof = self.root / 'proof'
        self.project.mkdir(); self.proof.mkdir()
        self.manifest, self.inputs, self.witness = populate(self.project, self.proof)
    def tearDown(self): self.temp.cleanup()
    def apply(self): return repair.repair(self.project, self.proof)
    def test_exact_only_three_headers_and_metadata_hash_chain(self):
        originals = {p.relative_to(self.project).as_posix(): p.read_bytes() for p in self.project.rglob('*') if p.is_file()}
        receipt = self.apply(); current, prior = repair.verify_completed(self.project)
        self.assertEqual(receipt, current); self.assertEqual(prior, self.manifest)
        changed = {row['assetPath'] for row in receipt['programs']}
        self.assertEqual(len(changed), 3)
        for name, raw in originals.items():
            actual = (self.project / name).read_bytes()
            if name in changed: self.assertEqual(repair.original_bytes(actual), raw)
            elif name != repair.MANIFEST: self.assertEqual(actual, raw)
        self.assertEqual(receipt, self.apply())
        self.assertFalse(receipt['headsetPictureVerified']); self.assertFalse(receipt['originalPixelParityVerified'])
    def test_original_math_meta_and_native_layer_drift_rejected(self):
        row = self.manifest['programs'][0]
        for path in (self.project / row['assetPath'], self.project / (row['assetPath'] + '.meta')):
            before = path.read_bytes(); path.write_bytes(before + b'// changed\n')
            with self.assertRaises(BuildError): self.apply()
            path.write_bytes(before)
        bad = copy.deepcopy(self.manifest); bad['programs'][0]['originalOutputSignature'][0]['componentType'] = 3
        write(self.project / repair.MANIFEST, repair.encoded(bad))
        with self.assertRaises(BuildError): self.apply()
    def test_duplicate_or_missing_tier_diagnostic_mode_and_native_keyword_rejected(self):
        for change in (lambda w: w['banks'].__setitem__(0, copy.deepcopy(w['banks'][1])),
                       lambda w: w['banks'][-1]['variant'].__setitem__('diagnosticNativeProgramPair', True),
                       lambda w: w['banks'][0]['variant']['keywords'].append('STEREO_MULTIVIEW_ON'),
                       lambda w: w['banks'][0]['outputs'].append(copy.deepcopy(w['banks'][0]['outputs'][0]))):
            bad = copy.deepcopy(self.witness); change(bad)
            with self.assertRaises(BuildError): repair.verify_witness(self.inputs, bad, digest(self.proof / 'WitnessInput.json'))
    def test_actual_compiler_artifact_drift_and_linked_project_rejected(self):
        name = self.witness['banks'][0]['vertexFile']; write(self.proof / 'FinalPassWitness' / name, b'changed')
        with self.assertRaises(BuildError): self.apply()
        linked = self.root / 'linked'; linked.symlink_to(self.project, target_is_directory=True)
        with self.assertRaises(BuildError): repair.repair(linked, self.proof)
    def test_interrupted_transaction_never_silently_adopted(self):
        write(self.project / repair.TRANSACTION, b'{}')
        with self.assertRaises(BuildError): self.apply()
    def test_caught_failure_rolls_back_only_recognized_bytes(self):
        before = (self.project / repair.MANIFEST).read_bytes(); first = self.manifest['programs'][0]['assetPath']
        original = (self.project / first).read_bytes(); real = repair.atomic
        def fail(path, raw):
            if path == self.project / repair.IDENTITIES: raise OSError('simulated disk failure')
            return real(path, raw)
        with patch.object(repair, 'atomic', side_effect=fail):
            with self.assertRaises(OSError): self.apply()
        self.assertEqual((self.project / repair.MANIFEST).read_bytes(), before)
        self.assertEqual((self.project / first).read_bytes(), original)
        self.assertTrue((self.project / repair.TRANSACTION).is_file())
    def test_completed_evidence_and_module_drift_rejected(self):
        self.apply(); drivers = self.root / 'drivers'; drivers.mkdir()
        for name in ('full_shaders.py', 'stereo_eye_repair.py'):
            shutil.copyfile(Path(repair.__file__).with_name(name), drivers / name)
        repair.verify_completed(self.project, driver_dir=drivers)
        write(drivers / 'full_shaders.py', b'changed generator')
        with self.assertRaises(BuildError): repair.verify_completed(self.project, driver_dir=drivers)
        write(self.project / repair.WITNESS, b'{}')
        with self.assertRaises(BuildError): repair.verify_completed(self.project)


class ProvenanceEyeTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('eye_provenance_fixture', Path(__file__).with_name('test_build_provenance.py'))
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        self.fixture = module.BuildProvenanceTests('test_unchanged_sources_are_exact_sorted_portable_records')
        self.fixture.setUp(); self.project = self.fixture.project
        self.proof = self.fixture.root / 'proof'; self.proof.mkdir()
        for name in ('full_shaders.py', 'stereo_eye_repair.py'):
            shutil.copyfile(Path(repair.__file__).with_name(name), self.fixture.driver / name)
    def tearDown(self): self.fixture.tearDown()
    def apply(self, with_prior=False):
        if with_prior:
            old, manifest, ledger = self.fixture.shader_order_receipt()
            old_bytes = (self.project / 'QuestCampaignEvidence/fragment-stereo-input-order.json').read_bytes()
        else: manifest = None
        manifest, _, _ = populate(self.project, self.proof, manifest)
        if with_prior:
            old_paths = {row['assetPath'] for row in old['programs']}
            ledger = {'schema': 1, 'scope': 'native-fragment-stereo-input-order-identities',
                'originalAliasCount': manifest['requiredOriginalNativeAliasCount'],
                **{kind: [identity(self.project, row, kind != 'programs') for row in manifest[kind]
                    if kind != 'programs' or row['assetPath'] not in old_paths] for kind in ('shaders', 'materials', 'programs')}}
            ledger_path = self.project / old['identities']['path']; write(ledger_path, repair.encoded(ledger))
            old['manifest']['afterSha256'] = digest(self.project / repair.MANIFEST)
            old['identities'].update(sha256=digest(ledger_path), shaderCount=len(manifest['shaders']),
                materialCount=len(manifest['materials']), originalAliasCount=manifest['requiredOriginalNativeAliasCount'])
            self.fixture.store_shader_order_receipt(old)
            old_bytes = (self.project / 'QuestCampaignEvidence/fragment-stereo-input-order.json').read_bytes()
        receipt = repair.repair(self.project, self.proof)
        if with_prior:
            self.assertEqual((self.project / 'QuestCampaignEvidence/fragment-stereo-input-order.json').read_bytes(), old_bytes)
        return receipt
    def test_portable_three_program_provenance_and_frozen_identity(self):
        receipt = self.apply(); result = self.fixture.capture()
        proof = result['campaignVertexLayerEyeMacroRepair']
        self.assertEqual(proof['programCount'], 3)
        self.assertEqual(proof['manifest']['beforeSha256'], receipt['manifest']['beforeSha256'])
        self.assertEqual(result['runtime']['sourceCommit'], 'b' * 40)
        self.assertNotIn(str(self.fixture.root), json.dumps(result))
        self.assertNotIn('originalName', json.dumps(proof))
        self.assertEqual(result, self.fixture.capture())
    def test_existing_ninety_program_receipt_phase_and_copy_closure_preserved(self):
        receipt = self.apply(True); result = self.fixture.capture()
        eye = result['campaignVertexLayerEyeMacroRepair']; old = result['campaignFragmentStereoInputOrderRepair']
        self.assertEqual(old['programCount'], 90)
        self.assertEqual(old['manifest']['sha256'], receipt['manifest']['beforeSha256'])
        self.assertEqual(eye['manifest']['sha256'], result['campaignShaderManifest']['sha256'])
        self.assertNotEqual(old['manifest']['sha256'], eye['manifest']['sha256'])
        self.assertEqual(old['identities']['unchangedProgramCount'], 4)
        prior = self.project / 'QuestCampaignEvidence/fragment-stereo-input-order.json'
        before = prior.read_bytes(); write(prior, before + b' ')
        with self.assertRaises(BuildError): self.fixture.capture()
    def test_prior_unchanged_program_cannot_be_hidden_by_successor(self):
        self.apply(True)
        name = self.project / 'Assets/Original/Programs/Unchanged.hlsl'
        write(name, b'changed unrelated native vertex instructions')
        with self.assertRaises(BuildError): self.fixture.capture()
    def test_applied_marker_without_receipt_and_unknown_target_rejected(self):
        self.apply(); path = self.project / repair.RECEIPT; raw = path.read_bytes(); path.unlink()
        with self.assertRaises(BuildError): self.fixture.capture()
        write(path, raw)
        inputs = copy.deepcopy(self.fixture.inputs); inputs['target'] = 'startup'
        with self.assertRaises(BuildError): self.fixture.capture(inputs=inputs)
    def test_source_or_evidence_race_invalidates_provenance(self):
        self.apply(); original = build_provenance._shader_eye_repair
        def race(*args, **kwargs):
            result = original(*args, **kwargs)
            write(self.project / repair.WITNESS, b'changed after verification')
            return result
        with patch.object(build_provenance, '_shader_eye_repair', side_effect=race):
            with self.assertRaises(BuildError): self.fixture.capture()


if __name__ == '__main__': unittest.main()
