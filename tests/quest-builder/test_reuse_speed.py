"""Warm preparation and interrupted copies retain actual accepted byte work."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import prepare_resume
import recovery_resume


class PreparationReuseSpeedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)
        self.project = self.output / "projects/fixture"
        self.asset = self.project / "Assets/input.asset"
        self.asset.parent.mkdir(parents=True)
        self.original = b"original payload" * 150000
        self.asset.write_bytes(self.original)

    def journal(self):
        return prepare_resume.Preparation(self.output, self.project,
            input_key="a" * 64, target="game", recipe=1)

    def test_closed_preparation_does_not_rewrite_unchanged_journal(self):
        library = self.project / "Library/retained-import"
        library.parent.mkdir()
        library.write_bytes(b"completed original asset import")
        first = self.journal()
        with first.operation("project-files", 1):
            first.run("base-project", "project-files", lambda: None, ["Assets/input.asset"])
        first.finish()
        path = first.journal
        original = path.read_bytes()
        stamp = path.stat()
        first.close()
        with patch.object(prepare_resume, "write_json", side_effect=AssertionError("unchanged journal rewritten")):
            second = self.journal()
            with second.operation("project-files", 1):
                second.run("base-project", "project-files",
                           lambda: self.fail("completed action repeated"), ["Assets/input.asset"])
            second.finish()
            self.assertEqual(second.witnesses.counters["bytes_read"], 0)
            second.close()
        self.assertEqual(path.read_bytes(), original)
        self.assertEqual((path.stat().st_ino, path.stat().st_mtime_ns), (stamp.st_ino, stamp.st_mtime_ns))
        self.assertEqual(library.read_bytes(), b"completed original asset import")

    def interrupted_in_fresh_process(self):
        # The second reader must not accidentally reuse invocation-only hashes
        # left by this writer. Both invocations use the real journal and SQLite.
        script = """
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[1])
import prepare_resume, storage
output = Path(sys.argv[2]); project = output / 'projects/fixture'
value = prepare_resume.Preparation(output, project, input_key='a' * 64, target='game', recipe=1)
def interrupt():
    (project / 'Assets/input.asset').write_bytes(b'incomplete')
    raise storage.BuildError('injected phase interruption')
try:
    with value.operation('textures', 1):
        value.run('native-texture2d', 'textures', interrupt, [], mutations=['Assets/input.asset'])
except storage.BuildError as error:
    if str(error) != 'injected phase interruption':
        raise
finally:
    value.close()
"""
        subprocess.run([sys.executable, "-I", "-B", "-c", script,
                        str(ROOT / "tools/quest-builder"), str(self.output)], check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        return self.output / "cache/prepare-resume/fixture/undo-native-texture2d/0"

    def test_new_process_restores_completed_copy_without_whole_backup_precheck(self):
        saved = self.interrupted_in_fresh_process()
        self.assertTrue(saved.is_file())
        actual_digest = prepare_resume.digest
        reads = []
        def digest(path, *args, **kwargs):
            if path == saved: reads.append(path)
            return actual_digest(path, *args, **kwargs)
        with patch.object(prepare_resume, "digest", digest):
            second = self.journal()
            self.assertEqual(second.undo_witnesses.counters["cache_hits"], 1)
            self.assertEqual(second.undo_witnesses.counters["bytes_read"], 0)
            self.assertIsNone(second.value["pending"])
            second.close()
        self.assertEqual(reads, [])
        self.assertEqual(self.asset.read_bytes(), self.original)
        self.assertFalse(saved.parent.exists())

    def test_changed_same_size_time_backup_is_rejected_before_target_restore(self):
        saved = self.interrupted_in_fresh_process()
        before = saved.stat()
        saved.write_bytes(b"x" * len(self.original))
        os.utime(saved, ns=(before.st_atime_ns, before.st_mtime_ns))
        with self.assertRaisesRegex(storage.BuildError, "undo bytes changed"):
            self.journal()
        self.assertEqual(self.asset.read_bytes(), b"incomplete")
        path = self.output / "cache/prepare-resume/fixture/journal.json"
        self.assertIsNotNone(json.loads(path.read_text())["pending"])

    def test_unsupported_live_change_metadata_reads_backup_instead_of_trusting_saved_stats(self):
        saved = self.interrupted_in_fresh_process()
        actual_digest = prepare_resume.digest
        reads = []
        def digest(path, *args, **kwargs):
            if path == saved: reads.append(path)
            return actual_digest(path, *args, **kwargs)
        with patch.object(storage, "_file_witness_stamp", return_value=None), patch.object(prepare_resume, "digest", digest):
            second = self.journal()
            self.assertEqual(second.undo_witnesses.counters["bytes_read"], len(self.original))
            second.close()
        self.assertEqual(reads, [saved])
        self.assertEqual(self.asset.read_bytes(), self.original)


class PreparationObserverIdentityTests(unittest.TestCase):
    def test_only_complete_reviewed_undo_profile_retains_original_producers(self):
        previous = list(recovery_resume.OBSERVATION_UNDO_WITNESSES_PREVIOUS.values())
        current = list(recovery_resume.OBSERVATION_EDITOR_OVERLAY.values())
        before = recovery_resume.preparation_source_rows(previous)
        self.assertEqual(recovery_resume.preparation_source_rows(current), before)
        row = next(item for item in current if item["path"] == "tools/quest-builder/prepare_resume.py")
        actual = ROOT / row["path"]
        self.assertEqual(actual.stat().st_size, row["size"])
        self.assertEqual(storage.digest(actual), row["sha256"])
        changed = [{**item, "sha256": "0" * 64} if item is row else item for item in current]
        self.assertNotEqual(recovery_resume.preparation_source_rows(changed), before)
        mixed = [item for item in current if item["path"] != "tools/quest-builder/project_access.py"]
        self.assertNotEqual(recovery_resume.preparation_source_rows(mixed), before)


if __name__ == "__main__":
    unittest.main()
