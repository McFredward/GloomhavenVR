"""Completed compiler receipts remain observable without scanning APK payloads."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support
from storage import write_json


class CompactCompilerSupportTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.build = self.root / 'build'
        self.key = 'c' * 64; self.session = 'b' * 32
        self.route = {'updateStrategy': 'compact-code', 'updateKey': self.key}
        self.folder = self.build / 'updates' / self.key
        self.apk = self.folder / 'code-player.apk'
        self.receipt = self.folder / 'code-player.apk.code-build.json'
        self.value = {'schema': 1, 'scope': 'quest-code-update-v1', 'abi': 'arm64-v8a',
                      'unityVersion': '2021.3.5f1', 'packageAbi': 'd' * 64,
                      'buildResult': 'Succeeded', 'originalAssetsImported': False,
                      'privateName': 'PRIVATE MUST NOT SHIP',
                      'files': [{'path': 'lib/arm64-v8a/libil2cpp.so', 'size': 1024, 'sha256': 'e' * 64},
                                {'path': 'assets/bin/Data/Managed/Metadata/global-metadata.dat', 'size': 512, 'sha256': 'f' * 64}]}
        write_json(self.receipt, self.value)
        self.apk.write_bytes(b'Fixture bytes: observing file size is not native qualification')

    def test_completed_compiler_is_reported_without_opening_or_hashing_the_apk(self):
        real_open = Path.open
        def refuse_apk_read(path, *args, **kwargs):
            if path.suffix == '.apk': raise AssertionError('Diagnostic export must not read APK contents')
            return real_open(path, *args, **kwargs)
        with patch.object(Path, 'open', refuse_apk_read), patch.object(support, 'digest', side_effect=AssertionError('No compiler hashing')):
            result = support.compact_compiler_output_stats(self.build, self.route)
        self.assertEqual(result['buildResult'], 'Succeeded')
        self.assertTrue(result['apkPresent'])
        self.assertEqual(result['apkBytes'], self.apk.stat().st_size)
        self.assertFalse(result['contentVerified'])
        self.assertFalse(result['updatePublicationVerified'])
        self.assertNotIn('PRIVATE', json.dumps(result))

    def test_only_current_qualified_route_exposes_the_small_native_summary(self):
        write_json(self.root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})
        state = {'session': self.session, 'choices': {'mode': 'update-mod'}, 'status': 'failed',
                 'completed': {'inspect': {'details': {'inputKey': 'a' * 64, 'baseApkSha256': 'd' * 64}}}}
        write_json(self.root / 'sessions' / self.session / 'state.json', state)
        plan = {'schema': 1, 'mode': 'update-mod', 'updateKey': self.key, 'updateStrategy': 'compact-code',
                'gameInputKey': 'a' * 64, 'baseApkSha256': 'd' * 64}
        write_json(self.folder / 'plan.json', plan)
        write_json(self.build / 'latest-update-input.json', {'plan': 'updates/' + self.key + '/plan.json'})
        for matching in (True, False):
            if not matching:
                plan['baseApkSha256'] = '0' * 64; write_json(self.folder / 'plan.json', plan)
            result = support.export_support(self.root, self.session)
            with zipfile.ZipFile(result['path']) as archive:
                meta = json.loads(archive.read('diagnostic.json'))
            self.assertEqual('compactCompilerOutput' in meta, matching)
            if matching: self.assertEqual(meta['compactCompilerOutput']['buildResult'], 'Succeeded')

    def test_invalid_incomplete_or_foreign_receipts_remain_optional(self):
        for change in ({'scope': 'foreign'}, {'files': []}, {'originalAssetsImported': True},
                       {'files': [self.value['files'][0], self.value['files'][0]]}, {'packageAbi': 'invalid'}):
            write_json(self.receipt, dict(self.value, **change))
            with self.subTest(change=change):
                self.assertIsNone(support.compact_compiler_output_stats(self.build, self.route))
        for route in (None, {}, {'updateStrategy': 'cached-full-build', 'updateKey': self.key},
                      {'updateStrategy': 'compact-code', 'updateKey': '../foreign'}):
            self.assertIsNone(support.compact_compiler_output_stats(self.build, route))

    @unittest.skipIf(os.name == 'nt', 'POSIX link construction; production ordinary-path checks cover Windows too')
    def test_linked_compiler_receipt_is_not_read(self):
        target = self.root / 'outside.json'; target.write_bytes(self.receipt.read_bytes())
        self.receipt.unlink(); self.receipt.symlink_to(target)
        self.assertIsNone(support.compact_compiler_output_stats(self.build, self.route))


if __name__ == '__main__': unittest.main()
