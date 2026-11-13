"""Actual file/process controls for APK-only update checksum reuse."""
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import apk_input_cache
import storage

CHILD = """
import json,os,sys
from pathlib import Path
sys.path.insert(0,sys.argv[1])
import apk_input_cache,storage
actual=storage.digest
reads=[]
def digest(path):
    reads.append(str(path)); result=actual(path)
    if sys.argv[4]=='die': os._exit(17)
    if sys.argv[4]=='change': path.write_bytes(b'Q'*path.stat().st_size)
    return result
storage.digest=digest
print(json.dumps({'sha':apk_input_cache.digest_apk(Path(sys.argv[2]),Path(sys.argv[3])),'reads':reads}))
"""


class ApkInputCacheTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='quest-apk-input-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.output = self.root / 'output'
        self.output.mkdir()
        self.apk = self.root / 'external APK.apk'
        self.payload = b'Actual APK checksum fixture\n' * 4096
        self.apk.write_bytes(self.payload)
        self.sha = hashlib.sha256(self.payload).hexdigest()
        self.database = self.output / 'cache/apk-input-witnesses.sqlite3'
        storage._invocation_file_proofs.clear()
        self.addCleanup(storage._invocation_file_proofs.clear)

    def process(self, path=None, *, mode='normal', success=True):
        result = subprocess.run([sys.executable, '-I', '-B', '-c', CHILD,
                                 str(ROOT / 'tools/quest-builder'), str(path or self.apk),
                                 str(self.output), mode], capture_output=True, text=True, timeout=10)
        if not success:
            self.assertNotEqual(result.returncode, 0, result.stdout)
            return result
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def owned_build(self):
        path = self.output / 'builds' / ('a' * 64) / 'GloomhavenVR-Quest.apk'
        path.parent.mkdir(parents=True)
        path.write_bytes(self.payload)
        bank = path.parent / 'game-content.zip'
        bank.write_bytes(b'Unrelated game-bank fixture')
        details = {'package': 'dev.gloomhavenvr.quest', 'isDiagnostic': False,
                   'apkSha256': self.sha, 'certificateSha256': 'b' * 64, 'inputKey': 'c' * 64}
        storage.Stages(self.output).run('build', 'a' * 64, lambda: ([path, bank], details))
        storage._invocation_file_proofs.clear()
        return path, bank

    def test_unknown_external_apk_is_read_once_then_reused_in_another_process(self):
        cold = self.process()
        self.assertEqual(cold, {'sha': self.sha, 'reads': [str(self.apk)]})
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': []})

    def test_same_size_edit_with_restored_mtime_requires_one_new_read(self):
        self.process()
        before = self.apk.stat()
        self.apk.write_bytes(b'X' * len(self.payload))
        os.utime(self.apk, ns=(before.st_atime_ns, before.st_mtime_ns))
        expected = hashlib.sha256(self.apk.read_bytes()).hexdigest()
        self.assertNotEqual(expected, self.sha)
        self.assertEqual(self.process(), {'sha': expected, 'reads': [str(self.apk)]})
        self.assertEqual(self.process(), {'sha': expected, 'reads': []})

    def test_replacement_identity_invalidates_a_same_size_same_mtime_input(self):
        self.process()
        before = self.apk.stat()
        replacement = self.apk.with_suffix('.replacement')
        replacement.write_bytes(b'Y' * len(self.payload))
        os.utime(replacement, ns=(before.st_atime_ns, before.st_mtime_ns))
        replacement.replace(self.apk)
        self.assertEqual(self.process()['reads'], [str(self.apk)])

    def test_tampered_cached_digest_requires_actual_bytes(self):
        self.process()
        with sqlite3.connect(self.database) as connection:
            connection.execute("UPDATE validated_file_witnesses SET sha=?", ('0' * 64,))
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': [str(self.apk)]})
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': []})

    def test_damaged_observation_database_is_repaired_without_changing_apk(self):
        self.process()
        self.database.write_bytes(b'damaged cache metadata')
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': [str(self.apk)]})
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': []})
        self.assertEqual(self.apk.read_bytes(), self.payload)
        self.assertEqual(len(list(self.database.parent.glob('*.damaged-*'))), 1)

    def test_closed_build_reuses_only_selected_apk_witness_without_other_bank_reads(self):
        path, bank = self.owned_build()
        bank.write_bytes(b'The separate bank changed; APK input qualification must not inspect it.')
        self.assertEqual(self.process(path), {'sha': self.sha, 'reads': []})
        self.assertFalse(self.database.exists(), 'the existing stage witness already proves this APK')

    def test_changed_closed_build_apk_is_read_once_and_next_resume_uses_actual_digest(self):
        path, _ = self.owned_build()
        path.write_bytes(b'Z' * len(self.payload))
        expected = hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertEqual(self.process(path), {'sha': expected, 'reads': [str(path)]})
        self.assertEqual(self.process(path), {'sha': expected, 'reads': []})

    def test_sidecar_claim_alone_never_seeds_a_digest(self):
        path = self.output / 'builds' / ('a' * 64) / 'GloomhavenVR-Quest.apk'
        path.parent.mkdir(parents=True)
        path.write_bytes(self.payload)
        Path(str(path) + '.build.json').write_text(json.dumps({'apkSha256': '0' * 64}))
        storage.write_json(self.output / 'latest-build.json', {'schema': 1, 'apk': str(path),
                                                             'details': {'apkSha256': '0' * 64}})
        self.assertEqual(self.process(path), {'sha': self.sha, 'reads': [str(path)]})

    def test_closed_receipt_without_actual_witness_hashes_before_reuse(self):
        path, _ = self.owned_build()
        with sqlite3.connect(self.output / 'receipts/.file-witnesses.sqlite3') as connection:
            connection.execute('DELETE FROM validated_file_witnesses')
        self.assertEqual(self.process(path), {'sha': self.sha, 'reads': [str(path)]})
        self.assertEqual(self.process(path), {'sha': self.sha, 'reads': []})

    def test_linked_input_or_parent_is_rejected_before_payload_read(self):
        leaf = self.root / 'alias.apk'
        parent = self.root / 'linked-parent'
        try:
            leaf.symlink_to(self.apk)
            parent.symlink_to(self.root, target_is_directory=True)
        except (OSError, NotImplementedError): self.skipTest('Symlinks unavailable')
        for path in [leaf, parent / self.apk.name]:
            with self.subTest(path=path):
                result = self.process(path, success=False)
                self.assertIn('cannot be links', result.stderr)
        self.assertFalse(self.database.exists())

    def test_linked_cache_is_never_followed_and_cannot_block_an_ordinary_hash(self):
        target = self.root / 'unrelated.bin'
        target.write_bytes(b'private unrelated data')
        self.database.parent.mkdir()
        try: self.database.symlink_to(target)
        except (OSError, NotImplementedError): self.skipTest('Symlinks unavailable')
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': [str(self.apk)]})
        self.assertEqual(target.read_bytes(), b'private unrelated data')

    def test_hardlinked_input_is_read_conservatively_in_each_process(self):
        sibling = self.root / 'another APK.apk'
        try: os.link(self.apk, sibling)
        except (OSError, NotImplementedError): self.skipTest('Hardlinks unavailable')
        self.assertEqual(self.process()['reads'], [str(self.apk)])
        self.assertEqual(self.process()['reads'], [str(self.apk)])

    def test_unknown_change_metadata_disables_persistent_reuse(self):
        actual = storage.digest
        with mock.patch.object(storage, '_file_witness_stamp', return_value=None), \
                mock.patch.object(storage, 'digest', wraps=actual) as hasher:
            self.assertEqual(apk_input_cache.digest_apk(self.apk, self.output), self.sha)
            self.assertEqual(apk_input_cache.digest_apk(self.apk, self.output), self.sha)
            self.assertEqual(hasher.call_count, 2)

    def test_process_death_before_hash_publication_does_not_create_a_false_hit(self):
        self.assertEqual(self.process(mode='die', success=False).returncode, 17)
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': [str(self.apk)]})
        self.assertEqual(self.process(), {'sha': self.sha, 'reads': []})

    def test_changed_input_during_hash_cannot_publish_the_old_digest(self):
        result = self.process(mode='change', success=False)
        self.assertIn('changed while its bytes were read', result.stderr)
        expected = hashlib.sha256(self.apk.read_bytes()).hexdigest()
        self.assertNotEqual(expected, self.sha)
        self.assertEqual(self.process(), {'sha': expected, 'reads': [str(self.apk)]})

    def test_edit_during_warm_lookup_cannot_return_a_stale_digest(self):
        self.process()
        actual = storage.ValidatedFileWitnesses._observe
        def change_after_lookup(witness, path, **kwargs):
            result = actual(witness, path, **kwargs)
            path.write_bytes(b'S' * len(self.payload))
            return result
        with mock.patch.object(storage.ValidatedFileWitnesses, '_observe', change_after_lookup):
            with self.assertRaisesRegex(storage.BuildError, 'changed during update inspection'):
                apk_input_cache.digest_apk(self.apk, self.output)
        expected = hashlib.sha256(self.apk.read_bytes()).hexdigest()
        self.assertEqual(self.process(), {'sha': expected, 'reads': [str(self.apk)]})


if __name__ == '__main__': unittest.main()
