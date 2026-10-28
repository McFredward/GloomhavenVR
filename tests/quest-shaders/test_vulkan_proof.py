"""Planted omissions and native-interface defects in parallel Vulkan evidence."""
import copy
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-shaders'))
from manifest import ValidationError, sha256
import shards
import vulkan
import vulkan_native_reference


def module(image_dimension=1, sampled=1, omit_binding=False):
    words = [0x07230203, 0x10000, 0, 20, 0]
    def op(code, *args): words.extend([((len(args) + 1) << 16) | code, *args])
    op(15, 0, 10, 0x6e69616d, 0)
    op(22, 1, 32)
    op(25, 2, 1, image_dimension, 0, 0, 0, sampled, 0)
    op(32, 3, 0, 2)
    op(59, 3, 4, 0)
    op(71, 4, 34, 1)
    if not omit_binding: op(71, 4, 33, 7)
    return struct.pack('<' + 'I' * len(words), *words)


class NativeVulkanReflection(unittest.TestCase):
    def test_actual_image_and_texel_buffer_descriptor_kinds(self):
        self.assertEqual(vulkan.Module(module()).descriptors(), [dict(set=1, binding=7, kind=2, count=1, stages=1)])
        self.assertEqual(vulkan.Module(module(5)).descriptors()[0]['kind'], 4)
        self.assertEqual(vulkan.Module(module(5, 2)).descriptors()[0]['kind'], 5)
        self.assertEqual(vulkan.Module(module(1, 2)).descriptors()[0]['kind'], 3)

    def test_truncated_native_instructions_and_unknown_descriptors_rejected(self):
        code = module()
        with self.assertRaises(ValidationError): vulkan.Module(code[:-1])
        with self.assertRaises(ValidationError): vulkan.Module(code[:20] + struct.pack('<2I', (3 << 16) | 15, 0))
        with self.assertRaises(ValidationError): vulkan.Module(module(omit_binding=True)).descriptors()
        with self.assertRaises(ValidationError): vulkan.Module(module(sampled=0)).descriptors()


class NativeCompilerPartitions(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        shaders = []
        for index in range(2):
            shaders.append(dict(guid=str(index + 1) * 32, originalName='Original' + str(index), sourceSha256='a' * 64,
                variants=[dict(subshader=0, hardwareTier=0, stereo='mono', keywords=['NATIVE_' + str(index)], **{'pass': index})]))
        self.original = dict(schema=1, graphicsApi='Vulkan', compilerPlatform='Vulkan', programs=[{'native': 'unchanged'}],
            shaders=shaders, materials=[dict(guid='a' * 32, shaderGuid=shaders[0]['guid']), dict(guid='b' * 32, shaderGuid=shaders[1]['guid'])],
            requiredShaderCount=2, requiredMaterialCount=2)
        self.source = self.root / 'original.json'; self.source.write_text(json.dumps(self.original))
        self.parts = shards.partition(self.source, self.root / 'parts', [1, 2])
        self.receipts = []
        for index, part in enumerate(self.parts):
            directory = self.root / ('evidence' + str(index)); directory.mkdir()
            shader = self.original['shaders'][index]
            row = dict(shader['variants'][0], guid=shader['guid'], vertexCompiled=True, fragmentCompiled=True)
            for field, hash_field in (('file', 'bankSha256'), ('vertexFile', 'vertexSha256'), ('fragmentFile', 'fragmentSha256')):
                path = directory / (str(index) + field + '.bin'); path.write_bytes((str(index) + field).encode())
                row[field], row[hash_field] = path.name, sha256(path)
            receipt = dict(schema=1, unityVersion='2021.3.5f1', compilerPlatform='Vulkan', graphicsApi='Vulkan', materialCount=1,
                sourceManifestSha256=sha256(part), originalPixelParityVerified=False, headsetPictureVerified=False, programs=[row])
            path = directory / 'android-compiler.json'; path.write_text(json.dumps(receipt)); self.receipts.append(path)

    def tearDown(self): self.temp.cleanup()

    def test_exact_native_union_and_actual_bank_hashes(self):
        receipt = shards.merge(self.source, self.parts, self.receipts, self.root / 'merged')
        self.assertEqual(len(receipt['programs']), 2); self.assertEqual(receipt['materialCount'], 2)
        self.assertFalse(receipt['headsetPictureVerified'])

    def test_native_partition_omission_duplication_and_bank_mutation_rejected(self):
        with self.assertRaises(ValidationError): shards.merge(self.source, self.parts[:1], self.receipts[:1], self.root / 'omitted')
        with self.assertRaises(ValidationError): shards.merge(self.source, self.parts * 2, self.receipts * 2, self.root / 'duplicate')
        receipt = json.loads(self.receipts[0].read_text()); (self.receipts[0].parent / receipt['programs'][0]['vertexFile']).write_bytes(b'changed')
        with self.assertRaisesRegex(ValidationError, 'bank bytes changed'):
            shards.merge(self.source, self.parts, self.receipts, self.root / 'changed')


class BoundedNativeOracle(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        definition=vulkan_native_reference.MODES['instance-nan']
        self.receipt=dict(unityVersion='2021.3.5f1',backend='Direct3D11',headsetPictureVerified=False,
            originalD3DReadbacksCompared=False,actualDrawMeshInstanced=True,hardwareTier=1,
            cases=[dict(id=name,passed=True)for name in definition['ids']])
        for name in definition['ids']:(self.root/(name+'.f32')).write_bytes(bytes(2*2*4*4))
        self.config=dict(width=2,vulkan=False)
        self.config_path=self.root/definition['configuration'];self.config_path.write_text(json.dumps(self.config))
        self.receipt_path=self.root/definition['receipt'];self.receipt_path.write_text(json.dumps(self.receipt))
        native=self.root/'original.bundle';native.write_bytes(b'actual-original-native-input')
        self.proof=dict(schema=1,mode='instance-nan',backend='Direct3D11',inputs={str(native):sha256(native)},
            originalReadbacks=vulkan_native_reference.validate_receipt('instance-nan',self.receipt,self.root,2,'Direct3D11'),
            configurationSha256=sha256(self.config_path),receiptSha256=sha256(self.receipt_path))
        (self.root/'native-inputs.json').write_text(json.dumps(self.proof))

    def tearDown(self):self.temp.cleanup()

    def test_exact_float_readbacks_and_original_provenance(self):
        config,proof=vulkan_native_reference.original_oracle('instance-nan',self.root)
        self.assertEqual(config,self.config);self.assertEqual(proof,self.proof)

    def test_changed_readback_or_swapped_candidate_cannot_become_native_oracle(self):
        (self.root/'nan.f32').write_bytes(bytes(63)+b'X')
        with self.assertRaisesRegex(ValidationError,'input bytes/readbacks changed'):
            vulkan_native_reference.original_oracle('instance-nan',self.root)
        self.config['vulkan']=True;self.config_path.write_text(json.dumps(self.config))
        with self.assertRaisesRegex(ValidationError,'cannot become'):
            vulkan_native_reference.original_oracle('instance-nan',self.root)

    def test_missing_case_and_non_instanced_execution_fail(self):
        for changes in (dict(cases=self.receipt['cases'][:-1]),dict(actualDrawMeshInstanced=False),dict(backend='Vulkan')):
            with self.subTest(changes=changes),self.assertRaises(ValidationError):
                vulkan_native_reference.validate_receipt('instance-nan',dict(self.receipt,**changes),self.root,2,'Direct3D11')



if __name__ == '__main__': unittest.main()
