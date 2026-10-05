"""Exact ancillary desktop imports; never exempt an unknown Android program."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import campaign_compute
from storage import BuildError


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


class RetainedDesktopBanks(unittest.TestCase):
    def setUp(self):
        self.owner = {'name': 'AutoExposure', 'guid': '52e89a7b063f39d47a40ba4708a54eb7',
            'assetPath': 'Assets/Native/AutoExposure.compute', 'classId': 72, 'localFileId': 7200000,
            'sourceSha256': 'a' * 64, 'kernels': [
                {'name': name, 'threadGroups': [16, 8, 1], 'originalDxbcSha256': hex(index)[2:] * 64,
                 'interface': {'bindings': []}, 'outputBindings': []}
                for index, name in enumerate(('FirstOriginalKernel', 'SecondOriginalKernel'), 1)]}
        self.desktop = {'targetRenderer': 17, 'targetLevel': 11, 'constantBuffers': [],
            'kernels': [{'name': row['name'], 'variantMap': [['', {
                'threadGroupSize': row['threadGroups'][:], 'code': list(b'#version 430\nvoid main() {}\n')}]]}
                for row in self.owner['kernels']]}
        self.vulkan = {'targetRenderer': 21, 'targetLevel': 0, 'kernels': ['actual runtime gate receives these unchanged']}
        self.obj = {'m_Name': 'AutoExposure', 'variants': [self.desktop, self.vulkan]}
        self.manifest = {'graphicsApi': 'Vulkan', 'shaders': [self.owner]}
        self.pin = (self.owner['guid'], self.owner['assetPath'], self.owner['sourceSha256'],
            campaign_compute.original_kernel_contract_hash(self.owner), digest(self.desktop))

    def select(self, obj=None, manifest=None, pin=None):
        with patch.object(campaign_compute, '_RETAINED_PP_OPENGL_BANKS', {'AutoExposure': pin or self.pin}):
            return campaign_compute.select_vulkan_runtime_banks(manifest or self.manifest, [obj or self.obj])

    def test_exact_multikernel_original_is_disclosed_without_modifying_raw_object_or_runtime(self):
        before = copy.deepcopy(self.obj)
        selected, evidence = self.select()
        self.assertEqual(before, self.obj)
        self.assertIs(selected[0]['variants'][0], self.vulkan)
        row = evidence['additionalNativeBanks'][0]
        self.assertEqual(row['actualBankSha256'], self.pin[-1])
        self.assertEqual([k['kernelName'] for k in row['kernels']], [k['name'] for k in self.owner['kernels']])
        self.assertFalse(row['selectedForAndroidRuntimeAudit'])
        self.assertNotIn('kernelName', row)

    def test_original_owner_source_dxbc_interface_and_dispatch_changes_are_rejected(self):
        controls = (
            lambda c: c.update(guid='0' * 32), lambda c: c.update(assetPath='Assets/Foreign.compute'),
            lambda c: c.update(classId=114), lambda c: c.update(localFileId=0),
            lambda c: c.update(sourceSha256='0' * 64),
            lambda c: c['kernels'][0].update(originalDxbcSha256='0' * 64),
            lambda c: c['kernels'][0].update(threadGroups=[8, 8, 1]),
            lambda c: c['kernels'][0].update(name='InventedKernel'),
            lambda c: c['kernels'][0]['interface'].update(bindings=[{'name': 'InventedBinding'}]),
            lambda c: c['kernels'][0].update(outputBindings=[{'name': 'InventedOutput'}]),
            lambda c: c['kernels'].reverse(),
        )
        for mutate in controls:
            with self.subTest(control=mutate):
                m = copy.deepcopy(self.manifest); mutate(m['shaders'][0])
                with self.assertRaises(BuildError): self.select(manifest=m)

    def test_unknown_backend_bank_bytes_and_duplicate_imports_are_rejected(self):
        controls = (
            lambda o: o['variants'][0].update(targetRenderer=11),
            lambda o: o['variants'][0].update(targetLevel=12),
            lambda o: o['variants'][0]['kernels'][0]['variantMap'][0][1]['code'].append(42),
            lambda o: o['variants'].append(copy.deepcopy(o['variants'][0])),
        )
        for mutate in controls:
            with self.subTest(control=mutate):
                o = copy.deepcopy(self.obj); mutate(o)
                with self.assertRaises(BuildError): self.select(obj=o)

    def test_kernel_frame_and_executable_checks_still_reject_even_with_matching_fixture_bank_hash(self):
        controls = (
            lambda b: b['kernels'][0].update(name='InventedKernel'),
            lambda b: b['kernels'][0]['variantMap'][0][1].update(threadGroupSize=[1, 1, 1]),
            lambda b: b['kernels'][0]['variantMap'][0].__setitem__(0, 'InventedVariant'),
            lambda b: b['kernels'][0]['variantMap'].append(copy.deepcopy(b['kernels'][0]['variantMap'][0])),
            lambda b: b['kernels'][0]['variantMap'][0][1].update(code=list(b'#version 430\nvoid other() {}')),
            lambda b: b['kernels'][0]['variantMap'][0][1].update(code=list(b'#version 310 es\nvoid main() {}')),
        )
        for mutate in controls:
            with self.subTest(control=mutate):
                o = copy.deepcopy(self.obj); mutate(o['variants'][0]); pin = (*self.pin[:-1], digest(o['variants'][0]))
                with self.assertRaises(BuildError): self.select(obj=o, pin=pin)

    def test_error_reports_bounded_original_owner_and_exact_actual_bank_identity(self):
        o = copy.deepcopy(self.obj); o['variants'][0]['targetRenderer'] = 99
        with self.assertRaisesRegex(BuildError, 'AutoExposure renderer=99 level=11 sha256=' + digest(o['variants'][0])):
            self.select(obj=o)
        unknown = 'Unknown' * 2000; o['m_Name'] = unknown
        m = copy.deepcopy(self.manifest); m['shaders'][0]['name'] = unknown
        with self.assertRaises(BuildError) as caught: self.select(obj=o, manifest=m)
        self.assertLess(len(str(caught.exception)), 250)

    def test_production_pins_are_only_twelve_exact_original_owners(self):
        pins = campaign_compute._RETAINED_PP_OPENGL_BANKS
        self.assertEqual(len(pins), 12)
        self.assertNotIn('EyeHistogram', pins)
        self.assertEqual(len({p[0] for p in pins.values()}), 12)
        for name, pin in pins.items():
            self.assertEqual(len(pin), 5)
            self.assertRegex(pin[0], '^[0-9a-f]{32}$')
            self.assertTrue(pin[1].startswith('Assets/QuestRecoveredBundles/' + pin[0] + '/'))
            for checksum in pin[2:]: self.assertRegex(checksum, '^[0-9a-f]{64}$')


if __name__ == '__main__': unittest.main()
