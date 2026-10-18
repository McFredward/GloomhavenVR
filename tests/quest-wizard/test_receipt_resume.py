"""Outer wizard receipts reuse byte-qualified metadata, never guess old hashes."""
import hashlib
from contextlib import closing
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import state


class ReceiptResumeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.store = state.Store(Path(self.temp.name) / "owned")
        self.session = self.store.create({"gameRoot": "original-game-remains-required"})["session"]
        self.path = self.store.session_dir(self.session) / "bank.bin"
        self.raw = b"original complete payload"
        self.path.write_bytes(self.raw)
        self.helper = state._receipt_storage()
        self.helper._invocation_file_proofs.clear()
        self.addCleanup(self.helper._invocation_file_proofs.clear)

    def publish(self, stage="build", key="same-input"):
        return self.store.publish(self.session, stage, key, [self.path], {"exact": True})

    def legacy(self):
        value = {"schema": 1, "stage": "build", "key": "same-input", "details": {"exact": True},
                 "outputs": [{"path": self.path.relative_to(self.store.root).as_posix(),
                              "size": len(self.raw), "sha256": hashlib.sha256(self.raw).hexdigest()}]}
        state.atomic_json(self.store.receipt(self.session, "build"), value)
        return value

    def database(self):
        return self.store.receipt(self.session, "build").parent / ".file-witnesses.sqlite3"

    def no_payload_reads(self):
        original = Path.open
        root = self.store.root
        def opening(path, mode="r", *args, **kwargs):
            if root in path.parents and path.suffix in (".bin", ".apk", ".bank") and "r" in mode:
                raise AssertionError("Warm receipt reread payload bytes: " + str(path))
            return original(path, mode, *args, **kwargs)
        return mock.patch.object(Path, "open", opening)

    def test_publish_records_real_bytes_once_and_warm_new_store_does_not_read_them(self):
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            receipt = self.publish()
            self.assertEqual(reader.call_count, 1)
        self.assertEqual(receipt["outputs"][0]["sha256"], hashlib.sha256(self.raw).hexdigest())
        self.assertEqual(receipt["schema"], 1)
        self.assertEqual(set(receipt), {"schema", "stage", "key", "outputs", "details"})
        self.helper._invocation_file_proofs.clear()
        with self.no_payload_reads():
            reopened = state.Store(self.store.root)
            self.assertEqual(reopened.valid(self.session, "build", "same-input"), receipt)

    def test_legacy_receipt_reads_once_then_reuses_persistent_witnesses(self):
        receipt = self.legacy()
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertEqual(self.store.valid(self.session, "build", "same-input"), receipt)
            self.assertEqual(reader.call_count, 1)
        self.helper._invocation_file_proofs.clear()
        with self.no_payload_reads():
            self.assertEqual(state.Store(self.store.root).valid(self.session, "build", "same-input"), receipt)
        self.assertEqual(state.read_json(self.store.receipt(self.session, "build")), receipt, "legacy receipt itself needs no migration/reset")

    def test_unqualified_legacy_receipt_with_same_size_and_mtime_still_reads_and_refuses_bad_bytes(self):
        self.legacy(); original = self.path.stat()
        self.path.write_bytes(b"x" * len(self.raw))
        os.utime(self.path, ns=(original.st_atime_ns, original.st_mtime_ns))
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
            self.assertEqual(reader.call_count, 1)

    def test_same_size_rewrite_with_restored_mtime_invalidates_change_stamp(self):
        self.publish(); original = self.path.stat()
        self.path.write_bytes(b"x" * len(self.raw))
        os.utime(self.path, ns=(original.st_atime_ns, original.st_mtime_ns))
        self.helper._invocation_file_proofs.clear()
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
            self.assertEqual(reader.call_count, 1)

    def test_same_path_replacement_with_restored_size_and_mtime_invalidates_identity(self):
        self.publish(); original = self.path.stat()
        replacement = self.path.with_name("replacement.bin")
        replacement.write_bytes(b"x" * len(self.raw))
        os.utime(replacement, ns=(original.st_atime_ns, original.st_mtime_ns)); os.replace(replacement, self.path)
        self.helper._invocation_file_proofs.clear()
        self.assertIsNone(self.store.valid(self.session, "build", "same-input"))

    def test_missing_resized_bad_hash_and_foreign_owner_never_validate(self):
        receipt = self.publish()
        for changes in ({"stage": "tools"}, {"key": "different"}, {"outputs": []},
                        {"outputs": [{**receipt["outputs"][0], "sha256": "invalid"}]},
                        {"outputs": [{**receipt["outputs"][0], "sha256": "0" * 64}]},
                        {"outputs": [{**receipt["outputs"][0], "size": len(self.raw) + 1}]}):
            with self.subTest(changes=changes):
                state.atomic_json(self.store.receipt(self.session, "build"), {**receipt, **changes})
                self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
        state.atomic_json(self.store.receipt(self.session, "build"), receipt)
        self.path.unlink(); self.assertIsNone(self.store.valid(self.session, "build", "same-input"))

    def test_tampered_persistent_hash_and_copied_namespace_require_actual_requalification(self):
        receipt = self.publish()
        with closing(sqlite3.connect(self.database())) as db:
            db.execute("UPDATE validated_file_witnesses SET sha=?", ("0" * 64,))
            db.commit()
        self.helper._invocation_file_proofs.clear()
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertEqual(self.store.valid(self.session, "build", "same-input"), receipt)
            self.assertEqual(reader.call_count, 1)
        # Another stage/key cannot inherit a row merely by relabelling JSON.
        copied = {**receipt, "stage": "tools", "key": "another-input"}
        state.atomic_json(self.store.receipt(self.session, "tools"), copied)
        self.helper._invocation_file_proofs.clear()
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertEqual(self.store.valid(self.session, "tools", "another-input"), copied)
            self.assertEqual(reader.call_count, 1)

    def test_links_reparse_paths_and_receipt_escape_are_rejected(self):
        receipt = self.publish()
        for raw in ("../outside", "/outside", "C:/outside", "\\outside", "bad:stream", "bad//file"):
            with self.subTest(raw=raw):
                value = {**receipt, "outputs": [{**receipt["outputs"][0], "path": raw}]}
                state.atomic_json(self.store.receipt(self.session, "build"), value)
                self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
        state.atomic_json(self.store.receipt(self.session, "build"), receipt)
        linked = self.path.with_name("linked.bin")
        try: linked.symlink_to(self.path)
        except OSError: return
        value = {**receipt, "outputs": [{**receipt["outputs"][0], "path": linked.relative_to(self.store.root).as_posix()}]}
        state.atomic_json(self.store.receipt(self.session, "build"), value)
        self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
        parent = self.store.root / "linked-parent"
        parent.symlink_to(self.path.parent, target_is_directory=True)
        value["outputs"][0]["path"] = "linked-parent/" + self.path.name
        state.atomic_json(self.store.receipt(self.session, "build"), value)
        self.assertIsNone(self.store.valid(self.session, "build", "same-input"))

    def test_hardlinks_are_byte_qualified_each_time_and_changed_bytes_refused(self):
        self.publish(); linked = self.path.with_name("alias.bin")
        os.link(self.path, linked)
        self.helper._invocation_file_proofs.clear()
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertIsNotNone(self.store.valid(self.session, "build", "same-input"))
            self.assertIsNotNone(self.store.valid(self.session, "build", "same-input"))
            self.assertEqual(reader.call_count, 2)
        linked.write_bytes(b"x" * len(self.raw))
        self.assertIsNone(self.store.valid(self.session, "build", "same-input"))

    def test_unknown_metadata_always_reads_without_making_persistent_trust(self):
        with mock.patch.object(self.helper, "_file_witness_stamp", return_value=None):
            self.publish(); self.helper._invocation_file_proofs.clear()
            with mock.patch.object(state, "digest", wraps=state.digest) as reader:
                self.assertIsNotNone(self.store.valid(self.session, "build", "same-input"))
                self.assertIsNotNone(self.store.valid(self.session, "build", "same-input"))
                self.assertEqual(reader.call_count, 2)

    def test_cancelled_publish_and_warm_resume_never_complete(self):
        receipt = self.publish()
        self.store.cancel(self.session)
        with self.no_payload_reads(), self.assertRaises(state.Cancelled):
            self.store.valid(self.session, "build", "same-input", report_progress=True)
        self.assertEqual(state.read_json(self.store.receipt(self.session, "build")), receipt)
        with self.assertRaises(state.Cancelled): self.publish("tools")
        self.assertFalse(self.store.receipt(self.session, "tools").exists())

    def test_mutation_while_publishing_does_not_create_receipt_or_witness(self):
        def changing(path, **_):
            raw = path.read_bytes(); path.write_bytes(b"x" * len(raw))
            return hashlib.sha256(raw).hexdigest()
        with mock.patch.object(state, "digest", side_effect=changing), self.assertRaises(state.WizardError):
            self.publish()
        self.assertFalse(self.store.receipt(self.session, "build").exists())
        with closing(sqlite3.connect(self.database())) as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM validated_file_witnesses").fetchone()[0], 0)

    def test_leaf_reparse_metadata_is_rejected_even_if_all_other_stat_fields_match(self):
        self.publish(); original = Path.lstat
        def changed(path, *args, **kwargs):
            result = original(path, *args, **kwargs)
            if path != self.path: return result
            fields = {name: getattr(result, name) for name in dir(result) if name.startswith("st_")}
            fields["st_file_attributes"] = 0x400
            return SimpleNamespace(**fields)
        with mock.patch.object(Path, "lstat", changed):
            self.assertIsNone(self.store.valid(self.session, "build", "same-input"))

    def test_windows_batch_change_time_is_distinct_from_path_creation_and_restored_mtime(self):
        class WindowsMetadata:
            change = 20
            directories = 0
            def stamp(inner, path):
                return ("win32-v1", 123, "a" * 32, path.lstat().st_size, 10, inner.change)
            def directory(inner, path):
                inner.directories += 1
                return {self.path.name: inner.stamp(self.path)}
        metadata = WindowsMetadata()
        with mock.patch.object(self.helper, "_windows_metadata", metadata), \
             mock.patch.object(self.helper, "_file_witness_stamp", side_effect=lambda path, value: metadata.stamp(path)), \
             mock.patch.object(self.helper.sys, "platform", "win32"):
            receipt = self.publish(); self.helper._invocation_file_proofs.clear()
            with self.no_payload_reads(), mock.patch.object(self.helper.ValidatedFileWitnesses, "qualify", side_effect=AssertionError("warm per-file handle")):
                self.assertEqual(self.store.valid(self.session, "build", "same-input"), receipt)
            self.assertEqual(metadata.directories, 1)
            original = self.path.stat(); self.path.write_bytes(b"x" * len(self.raw))
            os.utime(self.path, ns=(original.st_atime_ns, original.st_mtime_ns)); metadata.change += 1
            self.helper._invocation_file_proofs.clear()
            with mock.patch.object(state, "digest", wraps=state.digest) as reader:
                self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
                self.assertEqual(reader.call_count, 1)

    def test_warm_many_output_receipt_uses_directory_batch_not_individual_qualifiers(self):
        paths = []
        for index in range(200):
            path = self.path.with_name(str(index) + ".bin"); path.write_bytes(str(index).encode()); paths.append(path)
        receipt = self.store.publish(self.session, "tools", "sdk", paths, {})
        self.helper._invocation_file_proofs.clear()
        with self.no_payload_reads(), mock.patch.object(self.helper.ValidatedFileWitnesses, "qualify", side_effect=AssertionError("warm per-file qualifier")):
            self.assertEqual(state.Store(self.store.root).valid(self.session, "tools", "sdk"), receipt)

    def test_corrupt_derived_database_is_reseeded_without_discarding_authoritative_receipt(self):
        receipt = self.publish(); self.helper._invocation_file_proofs.clear()
        self.database().write_bytes(b"invalid derived sqlite cache")
        with mock.patch.object(state, "digest", wraps=state.digest) as reader:
            self.assertEqual(self.store.valid(self.session, "build", "same-input"), receipt)
            self.assertEqual(reader.call_count, 1)
        self.helper._invocation_file_proofs.clear()
        with self.no_payload_reads():
            self.assertEqual(state.Store(self.store.root).valid(self.session, "build", "same-input"), receipt)
        self.assertEqual(state.read_json(self.store.receipt(self.session, "build")), receipt)

    def test_linked_or_hardlinked_witness_database_never_opens_another_owners_bytes(self):
        receipt = self.legacy()
        foreign = Path(self.temp.name) / "foreign.sqlite3"; foreign.write_bytes(b"foreign unchanged")
        database = self.database()
        for kind in ("symlink", "hardlink"):
            with self.subTest(kind=kind):
                try:
                    database.symlink_to(foreign) if kind == "symlink" else os.link(foreign, database)
                except OSError: continue
                try: self.assertIsNone(self.store.valid(self.session, "build", "same-input"))
                finally: database.unlink()
                self.assertEqual(foreign.read_bytes(), b"foreign unchanged")
        self.assertEqual(state.read_json(self.store.receipt(self.session, "build")), receipt)

    def test_legacy_migration_log_names_the_one_time_read_and_warm_log_reports_zero_rechecked_bytes(self):
        self.legacy()
        with mock.patch.object(self.store, "progress", wraps=self.store.progress) as report:
            self.assertIsNotNone(self.store.valid(self.session, "build", "same-input", report_progress=True))
            self.assertTrue(any("legacy receipt" in str(call.args) and "one-time byte read" in str(call.args) for call in report.call_args_list))
            self.assertTrue(any("1 files / " + str(len(self.raw)) + " bytes rechecked" in str(call.args) for call in report.call_args_list))
        self.helper._invocation_file_proofs.clear()
        with self.no_payload_reads(), mock.patch.object(self.store, "progress", wraps=self.store.progress) as report:
            self.assertIsNotNone(self.store.valid(self.session, "build", "same-input", report_progress=True))
            self.assertFalse(any("legacy receipt" in str(call.args) for call in report.call_args_list))
            self.assertTrue(any("1 files reused by metadata; 0 files / 0 bytes rechecked" in str(call.args) for call in report.call_args_list))

    def test_sparse_512mib_payload_has_real_publish_hash_and_no_resume_payload_reads_in_new_process(self):
        large = self.path.with_name("large.apk")
        with large.open("wb") as stream: stream.truncate(512 * 1048576)
        receipt = self.store.publish(self.session, "build", "large", [large], {})
        self.assertEqual(receipt["outputs"][0]["size"], 512 * 1048576)
        script = '''from pathlib import Path
import sys
sys.path.insert(0, sys.argv[1])
import state
from unittest import mock
owned = Path(sys.argv[2]); original = Path.open
def opening(path, mode="r", *args, **kwargs):
    if path.suffix == ".apk" and "r" in mode: raise AssertionError("warm 512 MiB payload read")
    return original(path, mode, *args, **kwargs)
with mock.patch.object(Path, "open", opening), mock.patch.object(state, "digest", side_effect=AssertionError("warm digest")):
    result = state.Store(owned).valid(sys.argv[3], "build", "large")
    assert result and result["outputs"][0]["size"] == 512 * 1048576
print("512 MiB outer wizard receipt: zero payload reads in a fresh process")
'''
        result = subprocess.run([sys.executable, "-I", "-B", "-c", script, str(ROOT / "tools/quest-wizard"),
                                 str(self.store.root), self.session], capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("zero payload reads", result.stdout)

    def test_private_storage_import_preserves_existing_application_aliases(self):
        original = sys.modules.get("storage", object())
        self.assertEqual(state._receipt_storage().__name__, "_ghvrq_wizard_receipt_storage")
        self.assertIs(sys.modules.get("storage", original), original)


if __name__ == "__main__": unittest.main()
