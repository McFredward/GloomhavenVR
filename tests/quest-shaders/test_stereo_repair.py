"""Fail-closed byte/provenance controls; native witness runs separately in Unity."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-shaders'))
import produce
import stereo_repair as repair
from test_stereo_order import FragmentStereoOrder


class RepairProvenance(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.project = self.directory / 'project'
        self.witness_root = self.directory / 'proof/unity'
        self.manifest_path = 'Assets/QuestOriginalCampaign/campaign-shaders.json'
        self.program_path = 'Assets/Native/front-face.hlsl'
        self.shader_path = 'Assets/Native/foliage.shader'
        self.guid = '1' * 32
        self.source = FragmentStereoOrder.source.encode()
        self.corrected, _ = produce.fragment_stereo_input_order(self.source.decode(), 'fragment', FragmentStereoOrder.signature)
        self.corrected = self.corrected.encode()
        self.write(self.project, self.program_path, self.source)
        self.write(self.project, self.program_path + '.meta', b'guid: ' + b'2' * 32 + b'\n')
        self.write(self.project, 'Assets/Native/unchanged.hlsl', b'original_other_program\n')
        self.write(self.project, 'Assets/Native/unchanged.hlsl.meta', b'guid: ' + b'3' * 32 + b'\n')
        self.write(self.project, self.shader_path, b'Shader "Original" {}\n')
        self.write(self.project, self.shader_path + '.meta', ('guid: ' + self.guid + '\n').encode())
        self.write(self.project, 'Assets/Native/original.mat', b'Material:\n original:true\n')
        self.write(self.project, 'Assets/Native/original.mat.meta', b'guid: ' + b'4' * 32 + b'\n')
        self.failure = dict(id='original-negative', subshader=0, passType='ForwardBase', hardwareTier=0, keywords=['STEREO_MULTIVIEW_ON'])
        self.failure['pass'] = 0
        self.mono = {**self.failure, 'id': 'mono-native', 'keywords': []}
        program = dict(assetPath=self.program_path, originalDxbcSha256='a' * 64, originalInterfaceSha256='b' * 64,
                       sourceSha256=repair.digest(self.source), originalInputSignature=copy.deepcopy(FragmentStereoOrder.signature))
        self.manifest = dict(schema=1, scope='campaign-compiler', graphicsApi='Vulkan', requiredShaderCount=1,
            requiredMaterialCount=1, requiredOriginalNativeAliasCount=2, programs=[program, dict(assetPath='Assets/Native/unchanged.hlsl',
            originalDxbcSha256='c' * 64, originalInterfaceSha256='d' * 64, originalInputSignature=[], sourceSha256=repair.digest(b'original_other_program\n'))],
            shaders=[dict(guid=self.guid, assetPath=self.shader_path, sourceSha256=repair.sha256(self.project / self.shader_path),
                          variants=[dict(coverageKind='original-native', fragmentOriginalDxbcSha256='a' * 64, **row) for row in (self.failure, self.mono)])],
            materials=[dict(assetPath='Assets/Native/original.mat', guid='4' * 32)])
        self.input = dict(shaderPath=self.shader_path, shaderGuid=self.guid,
            failures=[self.failure], nativeControls=[self.mono], rewrites=[dict(assetPath=self.program_path,
            beforeSha256=repair.digest(self.source), afterSha256=repair.digest(self.corrected),
            metaSha256=repair.sha256(self.project / (self.program_path + '.meta')), originalDxbcSha256='a' * 64, originalInterfaceSha256='b' * 64)])
        banks = []
        for number, row in enumerate((self.failure, self.mono)):
            bank = dict(variant=row)
            for stage in ('vertex', 'fragment'):
                content = ('synthetic-unit-provenance-' + str(number) + stage).encode()
                name = str(number) + '.' + stage + '.spv'
                self.write(self.witness_root, 'FoliageWitness/' + name, content)
                bank[stage + 'File'], bank[stage + 'Sha256'] = name, repair.digest(content)
            banks.append(bank)
        self.witness = dict(schema=1, unityVersion='2021.3.5f1', shaderGuid=self.guid,
            shaderSha256=repair.sha256(self.project / self.shader_path), shaderMetaSha256=repair.sha256(self.project / (self.shader_path + '.meta')),
            originalDeclarationsAndMathPreserved=True, originalShaderAndMetaPreserved=True, actualNativeBundleBuilt=True,
            originalInputRewriteCount=1, baselineRejectedCount=1, positiveCompilerCount=2, monoExactStageCount=1, banks=banks,
            headsetPictureVerified=False, originalPixelParityVerified=False)
        self.driver = dict(schema=1, graphicsApi='Vulkan', actualNativePipelineAliasCount=2, actualDistinctNativePipelineCount=2,
            missingNativeEntryRejected=True, headsetPictureVerified=False, originalWindowsPixelParityVerified=False,
            aliases=[{k: row[k] for k in ('variant', 'vertexSha256', 'fragmentSha256')} for row in banks])
        self.persist()

    @staticmethod
    def write(root, path, value):
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(value)

    def persist(self):
        self.write(self.project, self.manifest_path, repair.encoded(self.manifest))
        self.write(self.witness_root, 'WitnessInput.json', repair.encoded(self.input))
        self.witness['inputSha256'] = repair.digest(repair.encoded(self.input))
        self.write(self.witness_root, 'FoliageWitness/results.json', repair.encoded(self.witness))
        self.driver['sourceWitnessSha256'] = repair.digest(repair.encoded(self.witness))
        self.write(self.witness_root.parent, 'actual-driver/actual-vulkan-pipelines.json', repair.encoded(self.driver))

    def run_repair(self):
        return produce.repair_fragment_stereo_inputs(self.project, compiler_witness=self.witness_root)

    def test_exact_generated_line_delta_and_complete_unchanged_ledger(self):
        original = copy.deepcopy(self.manifest)
        receipt = self.run_repair()
        after = json.loads((self.project / self.manifest_path).read_bytes())
        self.assertEqual((self.project / self.program_path).read_bytes(), self.corrected)
        self.assertEqual(receipt['programs'][0]['restBytesSha256'], repair.digest(self.source.replace(repair.MACRO, b'')))
        self.assertEqual(after['shaders'], original['shaders'])
        self.assertEqual(after['materials'], original['materials'])
        for before, changed in zip(original['programs'], after['programs']):
            self.assertEqual({k: v for k, v in changed.items() if k not in ('sourceSha256', 'fragmentStereoInputOrderProofs')},
                             {k: v for k, v in before.items() if k not in ('sourceSha256', 'fragmentStereoInputOrderProofs')})
        ledger = json.loads((self.project / receipt['identities']['path']).read_bytes())
        self.assertEqual((len(ledger['shaders']), len(ledger['materials']), len(ledger['programs'])), (1, 1, 1))
        self.assertEqual(receipt, self.run_repair())
        self.assertFalse(receipt['headsetPictureVerified'])
        self.assertFalse(receipt['originalPixelParityVerified'])

    def test_unknown_source_or_meta_drift_is_not_repaired(self):
        for name in (self.program_path, self.program_path + '.meta', self.shader_path, self.shader_path + '.meta', 'Assets/Native/original.mat.meta'):
            path = self.project / name
            before = path.read_bytes()
            try:
                path.write_bytes(before.replace(b'guid: ' + b'4' * 32, b'guid: ' + b'5' * 32) if name == 'Assets/Native/original.mat.meta' else before + b'changed')
                with self.subTest(path=name), self.assertRaises(repair.ValidationError):
                    self.run_repair()
                self.assertFalse((self.project / repair.RECEIPT).exists())
            finally:
                path.write_bytes(before)

    def test_native_identity_witness_and_census_drift_rejected(self):
        controls = [('requiredOriginalNativeAliasCount', 3), ('requiredShaderCount', 2), ('graphicsApi', 'GLES3')]
        for key, value in controls:
            old = self.manifest[key]; self.manifest[key] = value; self.persist()
            with self.subTest(key=key), self.assertRaises(repair.ValidationError): self.run_repair()
            self.manifest[key] = old; self.persist()
        for key, value in [('originalDxbcSha256', 'e' * 64), ('originalInterfaceSha256', 'f' * 64), ('afterSha256', '0' * 64), ('beforeSha256', '0' * 64)]:
            old = self.input['rewrites'][0][key]; self.input['rewrites'][0][key] = value; self.persist()
            with self.subTest(key=key), self.assertRaises(repair.ValidationError): self.run_repair()
            self.input['rewrites'][0][key] = old; self.persist()

    def test_incomplete_native_actual_evidence_is_rejected(self):
        for key, value in [('baselineRejectedCount', 0), ('positiveCompilerCount', 1), ('monoExactStageCount', 0),
                           ('originalInputRewriteCount', 2), ('actualNativeBundleBuilt', False), ('headsetPictureVerified', True), ('unityVersion', '2021.3.11f1')]:
            old = self.witness[key]; self.witness[key] = value; self.persist()
            with self.subTest(key=key), self.assertRaises(repair.ValidationError): self.run_repair()
            self.witness[key] = old; self.persist()
        for key, value in [('missingNativeEntryRejected', False), ('actualNativePipelineAliasCount', 1), ('actualDistinctNativePipelineCount', 1)]:
            old = self.driver[key]; self.driver[key] = value; self.persist()
            with self.subTest(key=key), self.assertRaises(repair.ValidationError): self.run_repair()
            self.driver[key] = old; self.persist()

    def test_corrupt_actual_cooked_outputs_are_rejected(self):
        path = self.witness_root / 'FoliageWitness/0.fragment.spv'
        path.write_bytes(path.read_bytes() + b'changed')
        with self.assertRaises(repair.ValidationError): self.run_repair()

    def test_missing_partial_or_unknown_provenance_is_rejected(self):
        with self.assertRaises(repair.ValidationError): produce.repair_fragment_stereo_inputs(self.project)
        self.write(self.project, repair.DIRECTORY + '/fragment-stereo-input-order.transaction', b'interrupted')
        with self.assertRaises(repair.ValidationError): self.run_repair()

    def test_completed_receipt_output_manifest_and_ledger_corruptions_rejected(self):
        receipt = self.run_repair()
        for relative in (self.program_path, self.program_path + '.meta', self.shader_path, 'Assets/Native/original.mat',
                         'Assets/Native/unchanged.hlsl', self.manifest_path, receipt['identities']['path'], receipt['nativeCompilerWitness']['path'], receipt['nativeDriverWitness']['path']):
            path = self.project / relative; before = path.read_bytes()
            try:
                path.write_bytes(before + b'\nchanged')
                with self.subTest(path=relative), self.assertRaises((repair.ValidationError, json.JSONDecodeError)):
                    self.run_repair()
            finally: path.write_bytes(before)
        (self.project / repair.RECEIPT).unlink()
        with self.assertRaises(FileNotFoundError): self.run_repair()

    def test_paths_and_symlink_outputs_fail_closed(self):
        for value in ('../escape', 'C:\\outside', '/absolute', 'Assets/../escape', 'Assets//empty'):
            with self.subTest(path=value), self.assertRaises(repair.ValidationError): repair.asset(self.project.resolve(), value)
        link = self.project / 'link'
        link.symlink_to(self.witness_root, target_is_directory=True)
        with self.assertRaises(repair.ValidationError): repair.asset(self.project.resolve(), 'link/WitnessInput.json')

    def test_caught_atomic_error_restores_original_without_complete_receipt(self):
        original_atomic = repair.atomic
        def fail_after_include(path, data):
            if str(path).endswith('identities.json'): raise OSError('controlled write failure')
            return original_atomic(path, data)
        with patch.object(repair, 'atomic', side_effect=fail_after_include), self.assertRaises(OSError): self.run_repair()
        self.assertEqual((self.project / self.program_path).read_bytes(), self.source)
        self.assertEqual(json.loads((self.project / self.manifest_path).read_bytes()), self.manifest)
        self.assertFalse((self.project / repair.RECEIPT).exists())
        with self.assertRaises(repair.ValidationError): self.run_repair()


if __name__ == '__main__': unittest.main()
