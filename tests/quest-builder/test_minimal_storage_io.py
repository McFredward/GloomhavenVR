"""Required copies and exact receipts share proofs, not duplicate payload reads."""
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage


class MinimalStorageIoTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.path = self.root / "content.bin"; self.path.write_bytes(b"actual owned fixture bytes")
        storage._invocation_file_proofs.clear()
        self.addCleanup(storage._invocation_file_proofs.clear)
        self.env = mock.patch.dict(os.environ, {"GHVR_QUEST_VERIFICATION": ""})
        self.env.start(); self.addCleanup(self.env.stop)

    def witness(self, owner):
        connection = sqlite3.connect(self.root / "receipts.sqlite3"); self.addCleanup(connection.close)
        return storage.ValidatedFileWitnesses(connection, self.root, {"owner": owner})

    def no_payload_reads(self, paths):
        original = Path.open
        def opening(path, mode="r", *args, **kwargs):
            if path in paths and "r" in mode:
                raise AssertionError("duplicate payload read: " + path.name)
            return original(path, mode, *args, **kwargs)
        return mock.patch.object(Path, "open", opening)

    def test_exact_digest_proof_avoids_repeat_record_and_verification_reads(self):
        expected = storage.digest(self.path)
        with self.no_payload_reads({self.path}):
            self.assertEqual(storage.record_file(self.path, self.path.name)["sha256"], expected)
            self.assertTrue(storage.verify_files(self.root, [{"path": self.path.name, "size": self.path.stat().st_size, "sha256": expected}]))

    def test_fresh_stage_accepts_producer_full_hash_without_a_second_asset_read(self):
        stages = storage.Stages(self.root); original = Path.open; reads = []
        def opening(path, mode="r", *args, **kwargs):
            if path == self.path and mode == "rb": reads.append(path)
            return original(path, mode, *args, **kwargs)
        def produced():
            self.path.write_bytes(b"actual generated content")
            return [self.path], {"artifact": storage.record_file(self.path, self.path.name)}
        with mock.patch.object(Path, "open", opening): value = stages.run("player", "new-key", produced)
        self.assertEqual(reads, [self.path])
        self.assertEqual(value["outputs"], [value["details"]["artifact"]])

    def test_digest_cache_still_counts_completed_bytes_to_progress(self):
        expected = storage.digest(self.path); seen = []
        with self.no_payload_reads({self.path}):
            self.assertEqual(storage.digest(self.path, progress=seen.append), expected)
        self.assertEqual(seen, [self.path.stat().st_size])

    def test_mutated_same_size_preserved_mtime_does_not_reuse_digest(self):
        expected = storage.digest(self.path); before = self.path.stat()
        self.path.write_bytes(b"x" * before.st_size)
        os.utime(self.path, ns=(before.st_atime_ns, before.st_mtime_ns))
        self.assertNotEqual(storage.digest(self.path), expected)

    def test_replaced_inode_with_same_bytes_requires_new_read(self):
        storage.digest(self.path); replacement = self.path.with_suffix(".new")
        replacement.write_bytes(self.path.read_bytes()); replacement.replace(self.path)
        with self.no_payload_reads({self.path}), self.assertRaisesRegex(AssertionError, "duplicate"):
            storage.digest(self.path)

    def test_unknown_stamp_never_becomes_metadata_trust(self):
        with mock.patch.object(storage, "_file_witness_stamp", return_value=None):
            storage.digest(self.path)
            with self.no_payload_reads({self.path}), self.assertRaisesRegex(AssertionError, "duplicate"):
                storage.digest(self.path)

    def test_hardlinked_digest_does_not_reuse_single_link_proof(self):
        storage.digest(self.path); os.link(self.path, self.root / "preimage")
        with self.no_payload_reads({self.path}), self.assertRaisesRegex(AssertionError, "duplicate"):
            storage.digest(self.path)

    def test_explicit_full_digest_mode_rereads(self):
        storage.digest(self.path)
        with mock.patch.dict(os.environ, {"GHVR_QUEST_VERIFICATION": "full"}), self.no_payload_reads({self.path}):
            with self.assertRaisesRegex(AssertionError, "duplicate"): storage.digest(self.path)

    def test_live_mutation_during_read_never_seeds_closed_proof(self):
        original_open = Path.open
        class ChangingReader:
            def __init__(self, stream): self.stream = stream; self.changed = False
            def __enter__(self): return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def read(self, size):
                raw = self.stream.read(size)
                if raw and not self.changed:
                    self.changed = True
                    with original_open(self_path, "wb") as writer: writer.write(b"z" * len(raw))
                return raw
        self_path = self.path
        def opening(path, mode="r", *args, **kwargs):
            value = original_open(path, mode, *args, **kwargs)
            return ChangingReader(value) if path == self.path and mode == "rb" else value
        with mock.patch.object(Path, "open", opening), self.assertRaisesRegex(storage.BuildError, "changed while"):
            storage.digest(self.path)
        self.assertNotIn(self.path, storage._invocation_file_proofs)

    def test_custom_digest_reuses_observers_pre_and_post_metadata_handles(self):
        witness = self.witness("fixture"); original = storage._file_witness_stamp; observations = []
        def stamped(path, value): observations.append(path); return original(path, value)
        with mock.patch.object(storage, "_file_witness_stamp", stamped):
            expected = witness.observe(self.path, hasher=lambda path: storage.digest(path))
        self.assertEqual(expected, hashlib.sha256(self.path.read_bytes()).hexdigest())
        self.assertEqual(observations, [self.path, self.path])

    def test_custom_digest_scope_is_cleared_after_cancelled_read(self):
        witness = self.witness("fixture")
        with self.assertRaises(KeyboardInterrupt):
            witness.observe(self.path, hasher=mock.Mock(side_effect=KeyboardInterrupt))
        self.assertIsNone(getattr(storage._digest_read_scope, "current", None))
        self.assertNotIn(self.path, storage._invocation_file_proofs)

    def test_namespace_bridge_reuses_exact_persistent_file_without_bytes(self):
        initial = self.witness("profile-before"); expected = initial.observe(self.path); initial.db.commit()
        storage._invocation_file_proofs.clear()
        after = self.witness("profile-after")
        with self.no_payload_reads({self.path}): self.assertEqual(after.observe(self.path), expected)
        self.assertEqual(after.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 1})

    def test_namespace_bridge_never_uses_self_corrupt_digest_record(self):
        initial = self.witness("before"); initial.observe(self.path); initial.db.commit()
        initial.db.execute("UPDATE validated_file_witnesses SET sha=?", ("0" * 64,)); initial.db.commit()
        storage._invocation_file_proofs.clear()
        after = self.witness("after"); expected = hashlib.sha256(self.path.read_bytes()).hexdigest()
        self.assertEqual(after.observe(self.path), expected); self.assertEqual(after.counters["files_read"], 1)

    def test_namespace_bridge_rejects_changed_stamp_and_expected_digest(self):
        initial = self.witness("before"); expected = initial.observe(self.path); initial.db.commit()
        before = self.path.stat(); self.path.write_bytes(b"y" * before.st_size)
        os.utime(self.path, ns=(before.st_atime_ns, before.st_mtime_ns)); storage._invocation_file_proofs.clear()
        after = self.witness("after")
        self.assertFalse(after.qualify(self.path, expected, before.st_size)); self.assertEqual(after.counters["files_read"], 1)

    def test_bridge_cannot_trust_a_different_root_with_same_relative_path(self):
        initial = self.witness("before"); initial.observe(self.path); initial.db.commit()
        other = self.root / "other"; other.mkdir(); member = other / self.path.name; member.write_bytes(b"other owned bytes")
        storage._invocation_file_proofs.clear()
        proof = storage.ValidatedFileWitnesses(initial.db, other, {"owner": "after"})
        self.assertEqual(proof.observe(member), hashlib.sha256(member.read_bytes()).hexdigest())
        self.assertEqual(proof.counters["files_read"], 1)

    def test_full_witness_mode_rereads_cached_and_cross_owner_records(self):
        initial = self.witness("before"); expected = initial.observe(self.path); initial.db.commit()
        with mock.patch.dict(os.environ, {"GHVR_QUEST_VERIFICATION": "full"}):
            self.assertEqual(initial.observe(self.path), expected); initial.db.commit()
            after = self.witness("after"); self.assertEqual(after.observe(self.path), expected)
        self.assertEqual(initial.counters["files_read"], 2); self.assertEqual(after.counters["files_read"], 1)

    def test_full_witness_batch_mode_rechecks_cached_records(self):
        initial = self.witness("before"); expected = initial.observe(self.path); initial.db.commit()
        records = [{"path": self.path.name, "size": self.path.stat().st_size, "sha256": expected}]
        with mock.patch.dict(os.environ, {"GHVR_QUEST_VERIFICATION": "full"}):
            self.assertEqual(list(initial.qualify_many(records)), [(records[0], True)])
        self.assertEqual(initial.counters["files_read"], 2)

    def test_fresh_snapshot_records_transfer_hash_without_destination_reread(self):
        source = self.root / "originals"; source.mkdir(); member = source / "large.bin"
        member.write_bytes(b"q" * (9 * 1048576)); records = storage.inventory(source)
        destination = self.root / "snapshot"; staged = destination.with_name("snapshot.staging") / member.name
        with self.no_payload_reads({staged, destination / member.name}): storage.snapshot(source, records, destination)
        self.assertEqual((destination / member.name).stat().st_size, records[0]["size"])
        storage._invocation_file_proofs.clear()
        with self.no_payload_reads({destination / member.name}): storage.snapshot(source, records, destination)

    def test_snapshot_copy_cannot_modify_external_hardlink_in_fast_or_full_mode(self):
        source = self.root / "source.bin"; source.write_bytes(b"selected original")
        protected = self.root / "other-original.bin"; protected.write_bytes(b"protected original")
        target = self.root / "partial.bin"; os.link(protected, target)
        item = {"path": target.name, "size": source.stat().st_size,
                "sha256": hashlib.sha256(source.read_bytes()).hexdigest()}
        witness = self.witness("fixture")
        for policy in ("", "full"):
            with self.subTest(policy=policy), mock.patch.dict(os.environ, {"GHVR_QUEST_VERIFICATION": policy}):
                with self.assertRaisesRegex(storage.BuildError, "multiple hard links"):
                    storage._copy_snapshot_member(source, target, item, witness)
        self.assertEqual(protected.read_bytes(), b"protected original")
        self.assertEqual(source.read_bytes(), b"selected original")

    def test_snapshot_wrong_source_bytes_never_publish_a_success_receipt(self):
        source = self.root / "originals"; source.mkdir(); member = source / "fixture.bin"
        member.write_bytes(b"original"); records = storage.inventory(source); member.write_bytes(b"replaced")
        destination = self.root / "snapshot"
        with self.assertRaisesRegex(storage.BuildError, "changed while copied"):
            storage.snapshot(source, records, destination)
        self.assertFalse(destination.exists())
        self.assertFalse((destination.with_name("snapshot.staging") / ".snapshot.json").exists())

    def test_snapshot_short_write_never_publish_success(self):
        source = self.root / "originals"; source.mkdir(); member = source / "fixture.bin"
        member.write_bytes(b"original"); records = storage.inventory(source); destination = self.root / "snapshot"
        staged = destination.with_name("snapshot.staging") / member.name; original_open = Path.open
        class ShortWriter:
            def __init__(self, stream): self.stream = stream
            def __enter__(self): return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def write(self, raw): return self.stream.write(raw[:-1])
        def opening(path, mode="r", *args, **kwargs):
            value = original_open(path, mode, *args, **kwargs)
            return ShortWriter(value) if path == staged and mode == "wb" else value
        with mock.patch.object(Path, "open", opening), self.assertRaisesRegex(storage.BuildError, "fewer bytes"):
            storage.snapshot(source, records, destination)
        self.assertFalse(destination.exists())

    def test_archive_pair_after_new_process_uses_persistent_exact_witness(self):
        output = self.root / "workspace"; output.mkdir(); project = output / "projects" / ("a" * 64)
        archive = project / storage.CONTENT_PATHS[0]; archive.parent.mkdir(parents=True)
        archive.write_bytes(b"known converted campaign bank"); key = "b" * 64
        manifest = project / storage.CONTENT_PATHS[1]
        storage.write_json(manifest, {"schema": 1, "inputKey": key, "archive": archive.name, "archiveSha256": storage.digest(archive)})
        storage.publish_project_content(output, project, key); storage._invocation_file_proofs.clear()
        with self.no_payload_reads({archive}): self.assertTrue(storage.project_content_valid(output, project, key))
        before = archive.stat(); archive.write_bytes(b"x" * before.st_size)
        os.utime(archive, ns=(before.st_atime_ns, before.st_mtime_ns))
        self.assertFalse(storage.project_content_valid(output, project, key))

    def test_old_content_pair_without_witness_is_adopted_by_actual_bytes_once(self):
        output = self.root / "workspace"; output.mkdir(); project = output / "projects" / ("a" * 64)
        archive = project / storage.CONTENT_PATHS[0]; archive.parent.mkdir(parents=True); archive.write_bytes(b"legacy bank")
        key = "b" * 64; manifest = project / storage.CONTENT_PATHS[1]
        storage.write_json(manifest, {"schema": 1, "inputKey": key, "archive": archive.name, "archiveSha256": hashlib.sha256(archive.read_bytes()).hexdigest()})
        self.assertTrue(storage.project_content_valid(output, project, key)); storage._invocation_file_proofs.clear()
        with self.no_payload_reads({archive}): self.assertTrue(storage.project_content_valid(output, project, key))
        self.assertFalse(storage.project_content_valid(output, project, "c" * 64))

    def test_ordinary_guard_uses_one_metadata_observation_per_ancestor(self):
        original = Path.lstat; visits = []
        def observed(path, *args, **kwargs): visits.append(path); return original(path, *args, **kwargs)
        with mock.patch.object(Path, "lstat", observed): self.assertEqual(storage._ordinary_owned(self.path), self.path)
        self.assertEqual(len(visits), len(set(visits)))
        self.assertEqual(visits, [self.path, *self.path.parents])


if __name__ == "__main__": unittest.main()
