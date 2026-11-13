"""Independent file batches keep byte proofs and SQLite in their parent."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import full_assets
import staging_copy
from staging_resume import Journal
from storage import BuildError


def safe_path(root, relative):
    path = root / relative
    if ".." in Path(relative).parts or path.is_symlink() or root.resolve() not in path.resolve().parents:
        raise BuildError("Copy escaped its fixture root")
    return path


class FileBatchTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / "source"; self.source.mkdir()
        self.output = self.root / "parallel"
        self.identity = {"source": str(self.source), "game": str(self.root / "game"), "recipe": "file-batch-fixture"}
        self.rows = []
        for index in range(73):
            path = self.source / ("Assets/group-" + str(index % 5) + "/file-" + str(index))
            path.parent.mkdir(parents=True, exist_ok=True)
            raw = bytes((index,)) * (513 + index * 7)
            path.write_bytes(raw)
            self.rows.append({"path": path.relative_to(self.source).as_posix(), "bytes": len(raw),
                              "sha256": hashlib.sha256(raw).hexdigest()})

    def journal(self, output=None):
        return Journal(output or self.output, self.identity, full_assets._StageProofs())

    def run_copy(self, journal, jobs=3, rows=None):
        return journal.run("copy", 0, lambda: staging_copy.copy_rows(self.source, journal.output,
            rows or self.rows, journal.proofs, journal, safe_path, jobs=jobs), copy=True)

    def test_real_serial_and_parallel_outputs_match_with_overlapping_workers(self):
        parent = threading.get_ident(); workers = set(); active = 0; peak = 0
        lock = threading.Lock(); gate = threading.Barrier(3)
        writer = full_assets._copy_recovered
        def observed(original, target, *args, **kwargs):
            nonlocal active, peak
            thread = threading.get_ident()
            self.assertNotEqual(thread, parent)
            self.assertIsNone(kwargs["proofs"])
            with lock:
                first = thread not in workers
                workers.add(thread); active += 1; peak = max(peak, active)
            try:
                if first: gate.wait(timeout=10)
                return writer(original, target, *args, **kwargs)
            finally:
                with lock: active -= 1
        with self.journal() as journal, mock.patch.object(staging_copy, "BATCH_FILES", 8), \
             mock.patch.object(full_assets, "_copy_recovered", side_effect=observed):
            record, remember = journal.record, journal.proofs.remember
            def parent_record(path):
                self.assertEqual(threading.get_ident(), parent); return record(path)
            def parent_remember(*args, **kwargs):
                self.assertEqual(threading.get_ident(), parent); return remember(*args, **kwargs)
            with mock.patch.object(journal, "record", side_effect=parent_record), \
                 mock.patch.object(journal.proofs, "remember", side_effect=parent_remember):
                parallel = self.run_copy(journal)
        self.assertEqual(peak, 3)
        self.assertEqual(len(workers), 3)
        serial_output = self.root / "serial"
        with self.journal(serial_output) as journal:
            serial = self.run_copy(journal, jobs=1)
        self.assertEqual(serial, parallel)
        self.assertEqual([row["path"] for row in parallel], [row["path"] for row in self.rows])
        for row in self.rows:
            self.assertEqual((serial_output / row["path"]).read_bytes(), (self.output / row["path"]).read_bytes())
            self.assertEqual(hashlib.sha256((self.output / row["path"]).read_bytes()).hexdigest(), row["sha256"])
        database = self.output.with_name(self.output.name + ".staging-resume") / "journal.sqlite3"
        with sqlite3.connect(database) as connection:
            self.assertEqual(connection.execute("SELECT COUNT(*) FROM files").fetchone()[0], len(self.rows))
            self.assertEqual(connection.execute("SELECT COUNT(*) FROM undo").fetchone()[0], 0)
        self.assertFalse(list(self.output.parent.rglob("backup-*")))

    def test_failed_batch_keeps_closed_files_and_repairs_only_missing_target(self):
        writer = full_assets._copy_recovered
        failed_path = self.rows[3]["path"]
        def broken(original, target, *args, **kwargs):
            if original == self.source / failed_path:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b"killed uncommitted partial bytes")
                raise RuntimeError("interrupted fourth file")
            return writer(original, target, *args, **kwargs)
        with self.journal() as journal, mock.patch.object(staging_copy, "BATCH_FILES", 8), \
             mock.patch.object(full_assets, "_copy_recovered", side_effect=broken):
            with self.assertRaisesRegex(RuntimeError, "interrupted fourth"):
                self.run_copy(journal, jobs=1)
        kept = self.output / self.rows[0]["path"]
        stamp = kept.stat(); opened = []; opening = Path.open
        def observe(path, mode="r", *args, **kwargs):
            if mode == "rb": opened.append(path)
            return opening(path, mode, *args, **kwargs)
        with self.journal() as journal, mock.patch.object(Path, "open", observe):
            result = self.run_copy(journal)
        self.assertEqual(len(result), len(self.rows))
        self.assertEqual(kept.stat().st_ino, stamp.st_ino)
        for row in self.rows[:3]:
            self.assertNotIn(self.source / row["path"], opened, "Closed uncommitted bytes must not be copied from source again")
        self.assertIn(self.source / failed_path, opened)
        self.assertEqual((self.output / failed_path).read_bytes(), (self.source / failed_path).read_bytes())

    def test_publication_rejects_source_tampering_after_closed_writer(self):
        writer = full_assets._copy_recovered
        row = self.rows[0]
        def tampered(original, target, *args, **kwargs):
            receipt = writer(original, target, *args, **kwargs)
            if original == self.source / row["path"]:
                before = original.stat()
                original.write_bytes(b"z" * before.st_size)
                os.utime(original, ns=(before.st_atime_ns, before.st_mtime_ns))
            return receipt
        with self.journal() as journal, mock.patch.object(full_assets, "_copy_recovered", side_effect=tampered):
            with self.assertRaisesRegex(BuildError, "changed before publication"):
                self.run_copy(journal, jobs=1)
        database = self.output.with_name(self.output.name + ".staging-resume") / "journal.sqlite3"
        with sqlite3.connect(database) as connection:
            self.assertEqual(connection.execute("SELECT COUNT(*) FROM phases").fetchone()[0], 0)

    def test_cancellation_during_streamed_write_removes_partial_file(self):
        import asset_jobs
        raw = b"verified source bytes" * 150000
        original, target = self.source / "large", self.root / "cancelled"
        original.write_bytes(raw)
        class CancelAfterFirstChunk:
            calls = 0
            def check(self):
                self.calls += 1
                if self.calls == 3: raise asset_jobs.Cancelled("fixture cancellation")
        cancellation = CancelAfterFirstChunk()
        with self.assertRaisesRegex(asset_jobs.Cancelled, "fixture cancellation"):
            full_assets._copy_recovered(original, target, hashlib.sha256(raw).hexdigest(),
                expected_size=len(raw), cancellation=cancellation, receipt=True)
        self.assertFalse(target.exists())
        self.assertEqual(original.read_bytes(), raw)

    def test_windows_native_change_time_binds_delayed_publication(self):
        writer = full_assets._copy_recovered
        row = self.rows[0]; target = self.output / row["path"]
        native_changes = {}
        def native_stamp(path, value):
            return ("win32-fixture", value.st_dev, value.st_ino, value.st_size,
                    value.st_mtime_ns, native_changes.get(path, 0))
        def tampered(original, destination, *args, **kwargs):
            receipt = writer(original, destination, *args, **kwargs)
            if destination == target:
                before = target.stat(); target.write_bytes(b"z" * before.st_size)
                os.utime(target, ns=(before.st_atime_ns, before.st_mtime_ns))
                native_changes[target] = 1
            return receipt
        # Windows path ctime means CreationTime, so a same-size preserved-mTime
        # rewrite does not change the portable stamp. Actual native ChangeTime
        # must still reject this worker digest, without publishing a false hash.
        portable_creation = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, 0)
        with self.journal() as journal, mock.patch.object(full_assets, "WINDOWS_FILE_TIMES", True), \
             mock.patch.object(full_assets, "_stamp", side_effect=portable_creation), \
             mock.patch.object(staging_copy.storage, "_file_witness_stamp", side_effect=native_stamp), \
             mock.patch.object(full_assets, "_copy_recovered", side_effect=tampered):
            with self.assertRaisesRegex(BuildError, "native change stamp changed"):
                self.run_copy(journal, jobs=1, rows=[row])
            self.assertEqual(journal.db.execute("SELECT COUNT(*) FROM files").fetchone()[0], 0)

    def test_unknown_windows_native_metadata_reads_actual_target_before_accepting(self):
        with self.journal() as journal, mock.patch.object(full_assets, "WINDOWS_FILE_TIMES", True), \
             mock.patch.object(staging_copy.storage, "_file_witness_stamp", return_value=None):
            hasher = full_assets._hash_output
            with mock.patch.object(full_assets, "_hash_output", wraps=hasher) as observed:
                self.run_copy(journal, jobs=1, rows=self.rows[:2])
            self.assertTrue(observed.called, "Unsupported Windows metadata must qualify real bytes after worker closure")

    def test_queued_source_directory_cannot_be_replaced_by_link(self):
        row = self.rows[0]
        qualifier = full_assets._qualify_copy
        outside = self.root / "moved-directory"
        def replaced(original, target, current, proofs, journal):
            result = qualifier(original, target, current, proofs, journal)
            original.parent.rename(outside)
            try: original.parent.symlink_to(outside, target_is_directory=True)
            except OSError: self.skipTest("Symlink creation unavailable")
            return result
        with self.journal() as journal, mock.patch.object(full_assets, "_qualify_copy", side_effect=replaced):
            with self.assertRaisesRegex(BuildError, "linked directory"):
                self.run_copy(journal, jobs=1, rows=[row])
        self.assertFalse((self.output / row["path"]).exists())
        self.assertEqual(hashlib.sha256((outside / Path(row["path"]).name).read_bytes()).hexdigest(), row["sha256"])

    def test_repeated_schedule_target_rejected_before_any_write(self):
        with self.journal() as journal:
            with self.assertRaisesRegex(BuildError, "repeated target"):
                self.run_copy(journal, rows=[self.rows[0], self.rows[0]])
        self.assertFalse(list(self.output.rglob("file-*")))

    def test_symlink_input_and_output_ancestors_cannot_escape(self):
        outside = self.root / "outside"; outside.mkdir()
        original = outside / "asset"; original.write_bytes(b"outside original")
        row = {"path": "linked/asset", "bytes": original.stat().st_size, "sha256": hashlib.sha256(original.read_bytes()).hexdigest()}
        with self.journal() as journal:
            for root in (self.source, self.output):
                link = root / "linked"
                try: link.symlink_to(outside, target_is_directory=True)
                except OSError: self.skipTest("Symlink creation unavailable")
            with self.assertRaisesRegex(BuildError, "escaped"):
                self.run_copy(journal, rows=[row])
        self.assertEqual(original.read_bytes(), b"outside original")

    def test_live_io_policy_is_separate_from_heavy_codec_reservations(self):
        host = {"effectiveCpus": 24, "availableMemoryBytes": 1024 ** 3, "commitHeadroomBytes": 1024 ** 3}
        with mock.patch.object(staging_copy.host_resources, "detect_host", return_value=host), \
             mock.patch.dict(os.environ, {staging_copy.host_resources.JOBS_ENV: "24"}), \
             contextlib.redirect_stdout(io.StringIO()) as stream:
            self.assertEqual(staging_copy.policy(self.output, 100000), 12)
        value = json.loads(stream.getvalue().split("resources: ", 1)[1])
        self.assertEqual(value["totalFiles"], 100000)
        self.assertEqual(value["additionalScratchBytes"], 0)
        with mock.patch.object(staging_copy.host_resources, "detect_host", return_value={**host, "availableMemoryBytes": 1}), \
             contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(staging_copy.policy(self.output, 100000), 1)


if __name__ == "__main__": unittest.main()
