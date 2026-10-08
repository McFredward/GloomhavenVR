"""Persistent owned byte proofs reduce reads without hiding changed files."""
import ctypes
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import shutil
import sqlite3
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage


def sha(raw): return hashlib.sha256(raw).hexdigest()


class WindowsOrderedPath(type(Path())):
    """Exercise Windows path ordering with real files on the host platform."""
    def __lt__(self, other):
        return PureWindowsPath(self.as_posix()) < PureWindowsPath(other.as_posix())


class WitnessTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.path = self.root / "assets" / "fixture.bin"
        self.path.parent.mkdir(); self.path.write_bytes(b"known owned bytes")
        self.database = self.root / "witness.sqlite3"
        storage._invocation_file_proofs.clear()
        self.addCleanup(storage._invocation_file_proofs.clear)

    def open(self, owner="fixture-owner"):
        connection = sqlite3.connect(self.database); self.addCleanup(connection.close)
        return storage.ValidatedFileWitnesses(connection, self.root, {"owner": owner})

    def cold(self):
        witness = self.open()
        self.assertTrue(witness.qualify(self.path, sha(b"known owned bytes"), 17))
        witness.db.commit(); storage._invocation_file_proofs.clear()
        return witness

    def test_cold_reads_then_new_instance_qualifies_unchanged_without_bytes(self):
        cold = self.cold()
        self.assertEqual(cold.counters, {"files_read": 1, "bytes_read": 17, "cache_hits": 0})
        warm = self.open()
        with mock.patch.object(Path, "open", side_effect=AssertionError("unexpected byte read")):
            self.assertTrue(warm.qualify(self.path, sha(b"known owned bytes"), 17))
        self.assertEqual(warm.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 1})

    def test_expected_hash_and_size_remain_authoritative(self):
        self.cold(); witness = self.open()
        self.assertFalse(witness.qualify(self.path, "0" * 64, 17))
        self.assertFalse(witness.qualify(self.path, sha(b"known owned bytes"), 18))

    def test_same_size_preserved_mtime_change_reads_and_rejects_old_hash(self):
        self.cold(); before = self.path.stat()
        self.path.write_bytes(b"other owned bytes")
        os.utime(self.path, ns=(before.st_atime_ns, before.st_mtime_ns))
        witness = self.open()
        self.assertFalse(witness.qualify(self.path, sha(b"known owned bytes"), 17))
        self.assertEqual(witness.counters["files_read"], 1)
        self.assertEqual(witness.observe(self.path), sha(b"other owned bytes"))
        self.assertEqual(witness.counters["files_read"], 1)

    def test_replaced_identity_requires_read_even_when_size_and_times_match(self):
        self.cold(); before = self.path.stat()
        replacement = self.path.with_suffix(".new"); replacement.write_bytes(b"known owned bytes")
        os.utime(replacement, ns=(before.st_atime_ns, before.st_mtime_ns)); replacement.replace(self.path)
        witness = self.open()
        self.assertTrue(witness.qualify(self.path, sha(b"known owned bytes"), 17))
        self.assertEqual(witness.counters["files_read"], 1)

    def test_corrupt_stamp_or_hash_checksum_requires_byte_read(self):
        for column, bad in (("stamp", "bad-json"), ("sha", "0" * 64), ("check_hash", "broken"), ("size", 19)):
            with self.subTest(column=column):
                witness = self.cold()
                witness.db.execute("UPDATE validated_file_witnesses SET " + column + "=?", (bad,)); witness.db.commit()
                storage._invocation_file_proofs.clear()
                fresh = self.open()
                self.assertTrue(fresh.qualify(self.path, sha(b"known owned bytes"), 17))
                self.assertEqual(fresh.counters["files_read"], 1)
                fresh.db.commit()

    def test_changed_owner_does_not_inherit_persistent_witness(self):
        self.cold(); changed = self.open("another-owner")
        self.assertTrue(changed.qualify(self.path, sha(b"known owned bytes"), 17))
        self.assertEqual(changed.counters["files_read"], 1)

    def test_unsupported_stamp_always_reads_and_remember_does_not_persist(self):
        with mock.patch.object(storage, "_file_witness_stamp", return_value=None):
            witness = self.open(); witness.remember(self.path, sha(b"known owned bytes"))
            self.assertEqual(witness.db.execute("SELECT COUNT(*) FROM validated_file_witnesses").fetchone()[0], 0)
            self.assertEqual(witness.observe(self.path), sha(b"known owned bytes"))
            self.assertEqual(witness.observe(self.path), sha(b"known owned bytes"))
            self.assertEqual(witness.counters["files_read"], 2)

    def test_linked_leaf_parent_and_outside_paths_are_rejected(self):
        witness = self.open()
        linked = self.root / "linked"; linked.symlink_to(self.path)
        with self.assertRaises(storage.BuildError): witness.observe(linked)
        parent = self.root / "parent"; parent.symlink_to(self.path.parent, target_is_directory=True)
        with self.assertRaises(storage.BuildError): witness.observe(parent / self.path.name)
        with self.assertRaises(storage.BuildError): witness.observe(self.root.parent / "outside")

    def test_hardlinked_content_remains_supported_with_conservative_bytes(self):
        self.cold()
        hard = self.root / "preimage.zip"; os.link(self.path, hard)
        witness = self.open()
        self.assertEqual(witness.observe(self.path), sha(b"known owned bytes"))
        self.assertEqual(witness.observe(self.path), sha(b"known owned bytes"))
        self.assertEqual(witness.counters, {"files_read": 2, "bytes_read": 34, "cache_hits": 0})
        stages = storage.Stages(self.root)
        result = stages.run("hardlinked", "fixture", lambda: ([hard], {}))
        storage._invocation_file_proofs.clear()
        self.assertIsNotNone(stages.valid("hardlinked", "fixture"))
        self.assertEqual(hard.read_bytes(), self.path.read_bytes())

    def test_changed_file_while_hashing_never_records_witness(self):
        witness = self.open()
        def changing(path):
            raw = path.read_bytes(); path.write_bytes(b"other owned bytes")
            return sha(raw)
        with self.assertRaisesRegex(storage.BuildError, "changed while"):
            witness.observe(self.path, hasher=changing)
        self.assertEqual(witness.db.execute("SELECT COUNT(*) FROM validated_file_witnesses").fetchone()[0], 0)

    def test_replaced_owned_root_is_rejected_even_with_original_files_moved(self):
        witness = self.open()
        moved = self.root.with_name(self.root.name + "-moved")
        self.root.rename(moved); self.root.mkdir()
        try:
            with self.assertRaisesRegex(storage.BuildError, "owner root changed"):
                witness._path(self.root / "asset")
        finally:
            self.root.rmdir(); moved.rename(self.root)

    def test_remember_requires_closed_proof_stamp_and_invalidation_is_scoped(self):
        witness = self.open(); before = witness.current(self.path)
        witness.remember(self.path, sha(b"known owned bytes"), stamp=before)
        witness.invalidate(self.path)
        self.assertEqual(witness.db.execute("SELECT COUNT(*) FROM validated_file_witnesses").fetchone()[0], 0)
        self.path.write_bytes(b"other owned bytes")
        with self.assertRaisesRegex(storage.BuildError, "changed before"):
            witness.remember(self.path, sha(b"known owned bytes"), stamp=before)

    def test_explicit_root_reset_drops_only_owner_and_binds_recreated_directory(self):
        project = self.root / "project"; project.mkdir()
        target = project / "asset"; target.write_bytes(b"known owned bytes")
        connection = sqlite3.connect(self.database); self.addCleanup(connection.close)
        witness = storage.ValidatedFileWitnesses(connection, project, {"owner": "fixture-owner"})
        witness.remember(target, sha(b"known owned bytes"))
        witness.db.commit()
        sibling_owner = storage.ValidatedFileWitnesses(connection, project, {"owner": "sibling-owner"})
        sibling_owner.remember(target, sha(b"known owned bytes"))
        sibling_owner.db.commit()
        project.rename(self.root / "retained"); project.mkdir(); target.write_bytes(b"other owned bytes")
        witness.invalidate(project)
        self.assertEqual(witness.db.execute("SELECT COUNT(*) FROM validated_file_witnesses WHERE owner=?", (witness.namespace,)).fetchone()[0], 0)
        self.assertEqual(witness.db.execute("SELECT COUNT(*) FROM validated_file_witnesses WHERE owner=?", (sibling_owner.namespace,)).fetchone()[0], 1)
        self.assertEqual(witness.observe(target), sha(b"other owned bytes"))
        self.assertEqual(witness.counters["files_read"], 1)

    def test_producer_proof_seeds_following_stage_without_duplicate_read(self):
        witness = self.open(); witness.remember(self.path, sha(b"known owned bytes"))
        stages = storage.Stages(self.root)
        # JSON receipt writers are allowed; only the asset byte reader is forbidden.
        original = Path.open
        def opening(path, mode="r", *args, **kwargs):
            if path == self.path and mode == "rb": raise AssertionError("duplicate producer read")
            return original(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening):
            result = stages.run("fixture", "key", lambda: ([self.path], {"known": True}))
        self.assertEqual(result["outputs"][0]["sha256"], sha(b"known owned bytes"))

    def test_stage_legacy_migration_then_warm_continuation_and_change(self):
        stages = storage.Stages(self.root)
        storage.write_json(stages.path("fixture", "key"), {"schema": 1, "stage": "fixture", "key": "key",
            "outputs": [{"path": self.path.relative_to(self.root).as_posix(), "size": 17, "sha256": sha(b"known owned bytes")}], "details": {}})
        self.assertIsNotNone(stages.valid("fixture", "key"))
        storage._invocation_file_proofs.clear()
        original = Path.open
        def opening(path, mode="r", *args, **kwargs):
            if path == self.path and mode == "rb": raise AssertionError("warm byte read")
            return original(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening):
            self.assertIsNotNone(storage.Stages(self.root).valid("fixture", "key"))
        stamp = self.path.stat(); self.path.write_bytes(b"other owned bytes")
        os.utime(self.path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        self.assertIsNone(stages.valid("fixture", "key"))


class FakeKernel:
    def __init__(self):
        self.closed, self.calls = [], []
        self.change, self.write, self.file_id = 20, 10, 123
        self.links, self.attributes, self.pending, self.directory = 1, 0, False, False
        self.filesystem, self.failed = "NTFS", None

    def CreateFileW(self, *args): self.calls.append(args); return 99
    def CloseHandle(self, handle): self.closed.append(handle); return True
    def GetVolumeInformationByHandleW(self, handle, a, b, c, d, e, name, count):
        name.value = self.filesystem; return True
    def GetFileInformationByHandleEx(self, handle, kind, pointer, size):
        if kind == self.failed: return False
        value = pointer._obj
        if kind == 0:
            value.CreationTime, value.ChangeTime, value.LastWriteTime = 1, self.change, self.write
            value.FileAttributes = self.attributes
        elif kind == 1:
            value.EndOfFile, value.NumberOfLinks, value.DeletePending, value.Directory = 17, self.links, self.pending, self.directory
        elif kind == 18:
            value.VolumeSerialNumber = 1234
            value.FileId[:] = self.file_id.to_bytes(16, "little")
        return True


class WindowsMetadataTests(unittest.TestCase):
    def test_win32_abi_attribute_access_share_flags_and_closed_handle(self):
        kernel = FakeKernel(); seam = storage._WindowsFileMetadata(kernel)
        self.assertEqual([ctypes.sizeof(cls) for cls in (seam.Basic, seam.Standard, seam.Identity)], [40, 24, 24])
        stamp = seam.stamp(Path("C:/owned/fixture.bin"))
        self.assertEqual(stamp, ("win32-v1", 1234, (123).to_bytes(16, "little").hex(), 17, 10, 20))
        self.assertEqual(kernel.calls[0][1:], (0x80, 7, None, 3, 0x00200000, None))
        self.assertTrue(kernel.calls[0][0].startswith("\\\\?\\")); self.assertEqual(kernel.closed, [99])

    def test_api_failure_unknown_driver_and_fat_are_conservative(self):
        for failed, filesystem in ((0, "NTFS"), (1, "NTFS"), (18, "NTFS"), (None, "FAT32"), (None, "unknown")):
            with self.subTest(failed=failed, filesystem=filesystem):
                kernel = FakeKernel(); kernel.failed, kernel.filesystem = failed, filesystem
                self.assertIsNone(storage._WindowsFileMetadata(kernel).stamp(Path("C:/owned/file")))
                self.assertEqual(kernel.closed, [99])

    def test_reparse_hardlink_directory_and_delete_pending_rejected(self):
        for field, value in (("attributes", 0x400), ("attributes", 0x10), ("directory", True), ("pending", True)):
            with self.subTest(field=field):
                kernel = FakeKernel(); setattr(kernel, field, value)
                with self.assertRaises(storage.BuildError): storage._WindowsFileMetadata(kernel).stamp(Path("C:/owned/file"))
                self.assertEqual(kernel.closed, [99])

    def test_win32_hardlinked_content_has_no_persistent_stamp(self):
        kernel = FakeKernel(); kernel.links = 2
        self.assertIsNone(storage._WindowsFileMetadata(kernel).stamp(Path("C:/owned/file")))
        self.assertEqual(kernel.closed, [99])

    def test_actual_change_time_invalidates_even_when_path_creation_and_mtime_match(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        root = Path(temporary.name); path = root / "file"; path.write_bytes(b"known owned bytes")
        connection = sqlite3.connect(root / "cache.sqlite3"); self.addCleanup(connection.close)
        kernel = FakeKernel(); seam = storage._WindowsFileMetadata(kernel)
        with mock.patch.object(storage, "_file_witness_stamp", side_effect=lambda p, value: seam.stamp(p)):
            witness = storage.ValidatedFileWitnesses(connection, root, {"owner": "fixture"})
            self.assertEqual(witness.observe(path), sha(b"known owned bytes"))
            before = path.stat(); path.write_bytes(b"other owned bytes")
            os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns)); kernel.change += 1
            self.assertFalse(witness.qualify(path, sha(b"known owned bytes"), 17))
            self.assertEqual(witness.counters["files_read"], 2)


class SnapshotWitnessTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.source = self.root / "source"; self.source.mkdir()
        self.destination = self.root / "snapshot"
        self.raw = (b"known ten-megabyte input\n" * 436907)[:10 * 1048576]
        self.path = self.source / "game.bundle"; self.path.write_bytes(self.raw)
        self.records = [{"path": "game.bundle", "size": len(self.raw), "sha256": sha(self.raw)}]
        storage._invocation_file_proofs.clear(); self.addCleanup(storage._invocation_file_proofs.clear)

    def count_reads(self, action):
        original = Path.open; reads = []
        def opening(path, mode="r", *args, **kwargs):
            if mode == "rb" and path.name == "game.bundle": reads.append(path)
            return original(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening): action()
        return reads

    def test_new_snapshot_seeds_verified_producer_then_warm_reads_zero_bytes(self):
        reads = self.count_reads(lambda: storage.snapshot(self.source, self.records, self.destination))
        self.assertEqual(reads.count(self.path), 0)  # kernel copy; original source verification is separate
        self.assertEqual(reads.count(self.destination.with_name("snapshot.staging") / "game.bundle"), 1)
        storage._invocation_file_proofs.clear()
        self.assertEqual(self.count_reads(lambda: storage.snapshot(self.source, self.records, self.destination)), [])
        database = self.root / ".snapshot.snapshot-witnesses.sqlite3"
        self.assertTrue(database.is_file())
        self.assertFalse((self.root / "snapshot.snapshot-witnesses.sqlite3").exists())

    def test_legacy_receipt_reads_once_then_next_run_skips_bytes_and_detects_change(self):
        self.destination.mkdir(); target = self.destination / "game.bundle"; target.write_bytes(self.raw)
        storage.write_json(self.destination / ".snapshot.json", {"schema": 1, "files": self.records})
        self.assertEqual(self.count_reads(lambda: storage.snapshot(self.source, self.records, self.destination)), [target])
        storage._invocation_file_proofs.clear()
        self.assertEqual(self.count_reads(lambda: storage.snapshot(self.source, self.records, self.destination)), [])
        stamp = target.stat()
        target.write_bytes(b"X" + self.raw[1:]); os.utime(target, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        with self.assertRaisesRegex(storage.BuildError, "Immutable snapshot is corrupt"):
            reads = self.count_reads(lambda: storage.snapshot(self.source, self.records, self.destination))
        self.assertEqual(target.read_bytes(), b"X" + self.raw[1:])
        # Source immutability qualification remains a full byte check.
        self.path.write_bytes(b"X" + self.raw[1:])
        self.assertFalse(storage.verify_files(self.source, self.records))

    def test_completed_snapshot_ownership_hash_change_is_not_reused(self):
        storage.snapshot(self.source, self.records, self.destination)
        changed = [{**self.records[0], "sha256": "0" * 64}]
        with self.assertRaisesRegex(storage.BuildError, "Immutable snapshot is corrupt"):
            storage.snapshot(self.source, changed, self.destination)


class PersistentInventoryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.source = self.root / "original"; self.source.mkdir()
        self.database = self.root / "output" / "original-inventory.sqlite3"
        (self.source / "one.bundle").write_bytes(b"first original bytes")
        (self.source / "two.bundle").write_bytes(b"second original bytes")
        storage._invocation_file_proofs.clear(); self.addCleanup(storage._invocation_file_proofs.clear)

    def inventory(self): return storage.persistent_inventory(self.source, self.database)

    def reads(self, action):
        original = Path.open; reads = []
        def opening(path, mode="r", *args, **kwargs):
            if mode == "rb" and self.source in path.parents: reads.append(path.name)
            return original(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening): result = action()
        return result, reads

    def test_first_actual_inventory_matches_strict_then_two_checks_read_nothing(self):
        expected = storage.inventory(self.source)
        result, reads = self.reads(self.inventory)
        self.assertEqual(result, expected); self.assertEqual(reads, ["one.bundle", "two.bundle"])
        storage._invocation_file_proofs.clear()
        result, reads = self.reads(lambda: (self.inventory(), self.inventory()))
        self.assertEqual(result, (expected, expected)); self.assertEqual(reads, [])
        self.assertEqual(sorted(path.name for path in self.source.iterdir()), ["one.bundle", "two.bundle"])

    def mixed_case_files(self):
        for relative in ("app.info", "Managed/Apparance.Net.dll", "Managed/assembly.dll", "StreamingAssets/Z.asset"):
            path = self.source / relative; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(relative.encode("utf-8"))

    def test_windows_path_order_preserves_legacy_inventory_rows_and_key(self):
        self.mixed_case_files(); expected = storage.inventory(self.source)
        windows_rows = sorted(expected, key=lambda row: PureWindowsPath(row["path"]))
        # A Path sort is case-insensitive on Windows. The old manifest sorts
        # relative POSIX strings, so unchanged bytes must not adopt that order.
        self.assertNotEqual(windows_rows, expected)
        self.assertNotEqual(storage.value_hash({"files": windows_rows}), storage.value_hash({"files": expected}))
        with mock.patch.object(storage, "Path", WindowsOrderedPath):
            result, reads = self.reads(self.inventory)
        self.assertEqual(result, expected)
        self.assertEqual(storage.value_hash({"files": result}), storage.value_hash({"files": expected}))
        self.assertEqual(len(reads), len(expected))
        storage._invocation_file_proofs.clear()
        with mock.patch.object(storage, "Path", WindowsOrderedPath):
            result, reads = self.reads(self.inventory)
        self.assertEqual(result, expected); self.assertEqual(reads, [])

    def test_previous_windows_ordered_database_restores_legacy_key_without_reads(self):
        self.mixed_case_files(); expected = storage.inventory(self.source)
        windows_rows = sorted(expected, key=lambda row: PureWindowsPath(row["path"]))
        self.database.parent.mkdir(parents=True)
        # Seed the exact previous database layout with individually observed
        # original bytes, independently of the fixed enumeration order.
        with sqlite3.connect(self.database) as connection:
            witnesses = storage.ValidatedFileWitnesses(connection, self.source,
                {"schema": 1, "owner": "Quest original input inventory"})
            for row in windows_rows:
                self.assertEqual(witnesses.observe(self.source / row["path"]), row["sha256"])
        storage._invocation_file_proofs.clear()
        with mock.patch.object(storage, "Path", WindowsOrderedPath):
            result, reads = self.reads(self.inventory)
        self.assertEqual(reads, []); self.assertEqual(result, expected)
        self.assertEqual(storage.value_hash({"files": result}), storage.value_hash({"files": expected}))
        with sqlite3.connect(self.database) as connection:
            self.assertEqual(connection.execute("SELECT COUNT(*) FROM validated_file_witnesses").fetchone()[0], len(expected))

    def test_directory_and_filename_prefix_sort_as_relative_strings(self):
        path = self.source / "node" / "A.asset"; path.parent.mkdir(); path.write_bytes(b"nested original")
        (self.source / "node.asset").write_bytes(b"root original")
        expected = storage.inventory(self.source)
        native_rows = sorted(expected, key=lambda row: Path(row["path"]))
        self.assertNotEqual(native_rows, expected)
        self.assertEqual(self.inventory(), expected)

    def test_windows_inventory_selects_existing_legacy_snapshot_without_copying(self):
        self.mixed_case_files(); expected = storage.inventory(self.source)
        original_key = storage.value_hash({"files": expected})
        destination = self.root / "inputs" / "game" / original_key
        storage.snapshot(self.source, expected, destination)
        self.inventory(); storage._invocation_file_proofs.clear()
        original_open = Path.open
        def opening(path, mode="r", *args, **kwargs):
            if mode == "rb" and (self.source in path.parents or destination in path.parents):
                raise AssertionError("unchanged original/snapshot asset reread")
            return original_open(path, mode, *args, **kwargs)
        with mock.patch.object(storage, "Path", WindowsOrderedPath), mock.patch.object(Path, "open", opening), \
                mock.patch.object(storage.shutil, "copyfile", side_effect=AssertionError("duplicate snapshot copy")):
            current = self.inventory()
            current_destination = self.root / "inputs" / "game" / storage.value_hash({"files": current})
            self.assertEqual(current_destination, destination)
            storage.snapshot(self.source, current, current_destination)
        self.assertEqual([path.name for path in destination.parent.iterdir() if path.is_dir()], [original_key])

    def test_only_changed_same_size_preserved_mtime_file_is_rehashed(self):
        expected = self.inventory(); storage._invocation_file_proofs.clear()
        path = self.source / "one.bundle"; before = path.stat()
        path.write_bytes(b"other original bytes")
        os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
        result, reads = self.reads(self.inventory)
        self.assertEqual(reads, ["one.bundle"])
        self.assertEqual(result[1], expected[1]); self.assertNotEqual(result[0]["sha256"], expected[0]["sha256"])
        self.assertEqual(result, storage.inventory(self.source))

    def test_addition_and_removal_update_file_set_without_rehashing_survivors(self):
        self.inventory(); storage._invocation_file_proofs.clear()
        (self.source / "one.bundle").unlink(); (self.source / "three.bundle").write_bytes(b"new original")
        result, reads = self.reads(self.inventory)
        self.assertEqual([row["path"] for row in result], ["three.bundle", "two.bundle"])
        self.assertEqual(reads, ["three.bundle"])

    def test_missing_cache_requires_real_reads_and_wrong_root_never_inherits_rows(self):
        self.inventory(); storage._invocation_file_proofs.clear()
        other = self.root / "other-original"; shutil.copytree(self.source, other)
        original = Path.open; opened = []
        def opening(path, mode="r", *args, **kwargs):
            if mode == "rb" and other in path.parents: opened.append(path.name)
            return original(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening): storage.persistent_inventory(other, self.database)
        self.assertEqual(opened, ["one.bundle", "two.bundle"])

    def test_corrupt_witness_rechecks_only_its_file(self):
        self.inventory(); storage._invocation_file_proofs.clear()
        with sqlite3.connect(self.database) as connection:
            connection.execute("UPDATE validated_file_witnesses SET check_hash='broken' WHERE path='one.bundle'")
        result, reads = self.reads(self.inventory)
        self.assertEqual(reads, ["one.bundle"]); self.assertEqual(result, storage.inventory(self.source))

    def test_interrupted_inventory_keeps_each_completed_large_file_proof(self):
        path = self.source / "one.bundle"; path.write_bytes(b"x" * (8 * 1048576))
        original = Path.open
        def opening(candidate, mode="r", *args, **kwargs):
            if candidate == self.source / "two.bundle" and mode == "rb": raise KeyboardInterrupt()
            return original(candidate, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", opening), self.assertRaises(KeyboardInterrupt): self.inventory()
        storage._invocation_file_proofs.clear()
        result, reads = self.reads(self.inventory)
        self.assertEqual(reads, ["two.bundle"]); self.assertEqual(len(result), 2)

    def test_unsupported_metadata_still_reads_and_never_trusts_unobserved_originals(self):
        with mock.patch.object(storage, "_file_witness_stamp", return_value=None):
            self.inventory(); storage._invocation_file_proofs.clear()
            _, reads = self.reads(self.inventory)
        self.assertEqual(reads, ["one.bundle", "two.bundle"])

    def test_database_in_originals_and_linked_inputs_are_rejected(self):
        with self.assertRaisesRegex(storage.BuildError, "outside the original"):
            storage.persistent_inventory(self.source, self.source / "do-not-write.sqlite3")
        linked = self.source / "linked"; linked.symlink_to(self.source / "one.bundle")
        with self.assertRaisesRegex(storage.BuildError, "unsupported linked"):
            self.inventory()
        linked.unlink()
        linked.symlink_to(self.root / "output", target_is_directory=True)
        with self.assertRaises(storage.BuildError): self.inventory()


if __name__ == "__main__": unittest.main()
