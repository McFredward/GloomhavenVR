"""Actual immutable container reads are reused; source changes remain failures."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
sys.path.insert(0, str(ROOT / 'tools/quest-recovery'))
import storage
import native_stage

class ImmutableHashTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.original = self.root / 'original.bundle'
        self.original.write_bytes(b'original immutable container bytes')

    def test_repeated_objects_share_one_actual_container_read(self):
        hashes = storage.ImmutableFileHashes()
        with patch.object(storage, 'digest', wraps=storage.digest) as read:
            values = [hashes.digest(self.original) for _ in range(50)]
        self.assertEqual(read.call_count, 1)
        self.assertEqual(set(values), {hashlib.sha256(self.original.read_bytes()).hexdigest()})
        # No persisted marker can silently qualify a new invocation.
        with patch.object(storage, 'digest', wraps=storage.digest) as read:
            storage.ImmutableFileHashes().digest(self.original)
        self.assertEqual(read.call_count, 1)

    def test_source_change_before_reuse_is_not_silently_rehashed(self):
        hashes = storage.ImmutableFileHashes(); hashes.digest(self.original)
        self.original.write_bytes(b'changed immutable source container')
        with self.assertRaisesRegex(storage.BuildError, 'source changed during conversion'):
            hashes.digest(self.original)

    def test_source_change_during_actual_read_cannot_publish_a_proof(self):
        original_digest = storage.digest
        def change(path):
            result = original_digest(path); path.write_bytes(b'changed during read'); return result
        with patch.object(storage, 'digest', side_effect=change):
            with self.assertRaisesRegex(storage.BuildError, 'source changed while read'):
                storage.ImmutableFileHashes().digest(self.original)

    def test_native_restoration_defers_only_the_duplicate_audit_when_requested(self):
        for deferred in (False, True):
            project = self.root / ('deferred' if deferred else 'standalone'); (project / 'QuestRecovery').mkdir(parents=True)
            repair = {'complete': True, 'additionalNativeTargets': [], 'nativePointerCount': 0,
                      'restoredPointerCount': 0, 'remainingMissingPointerCount': 0}
            def prepare(*args, **kwargs):
                workspace = args[4]; workspace.mkdir()
                (workspace / 'native-pointer-repair.json').write_text(json.dumps(repair))
                return repair
            with patch.object(native_stage.native_evidence, 'recipes', return_value=[]), \
                    patch.object(native_stage.pointer_recovery, 'prepare', side_effect=prepare), \
                    patch.object(native_stage.pointer_recovery, 'apply'), \
                    patch.object(native_stage, 'audit_asset_references', wraps=native_stage.audit_asset_references) as audit:
                arguments = {'audit_references': False} if deferred else {}
                result = native_stage.restore(project, self.root, [], {}, unitypy=object(), **arguments)
            self.assertEqual(audit.call_count, 0 if deferred else 1)
            self.assertEqual(result[1]['nativePointerRestoration']['remainingMissingPointerCount'], 0)

if __name__ == '__main__': unittest.main()
