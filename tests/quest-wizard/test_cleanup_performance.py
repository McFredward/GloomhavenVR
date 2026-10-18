"""Bounded metadata observation, responsive cancellation and retained authority."""
import hashlib
import os
from pathlib import Path
import stat
import sys
import tempfile
import threading
import unittest
from types import SimpleNamespace
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import cleanup_io
import state


class CleanupPerformanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "owned"
        self.store = state.Store(self.root)
        self.build = self.root / "build"; self.build.mkdir()
        cleanup._plans.clear(); self.addCleanup(cleanup._plans.clear)

    def file(self, relative, raw=b"generated"):
        path = self.build / relative; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}

    def success(self):
        key = "a" * 64
        apk = self.file("builds/" + key + "/Quest.apk", b"signed fixture")
        bank = self.file("builds/" + key + "/content.zip", b"fixture bank")
        details = {"isDiagnostic": False, "apkSha256": apk["sha256"], "contentFiles": [bank]}
        receipt = "receipts/build/" + key + ".json"
        state.atomic_json(self.build / receipt, {"schema": 1, "stage": "build", "key": key,
                                               "outputs": [apk, bank], "details": details})
        state.atomic_json(self.build / "latest-build.json", {"schema": 1, "apk": apk["path"],
                                                            "receipt": receipt, "details": details})

    def ticking(self):
        ticks = iter(index / 5 for index in range(100000))
        return mock.patch.object(cleanup.time, "monotonic", side_effect=lambda: next(ticks))

    def test_large_scan_has_one_ordinary_root_guard_and_no_native_leaf_handles(self):
        for folder in range(8):
            for index in range(64): self.file(f"cache/{folder}/asset-{index}.bin", b"x")
        old_stamp = cleanup._stamp; old_ordinary = cleanup.ordinary
        with mock.patch.object(cleanup, "_stamp", wraps=old_stamp) as stamps, \
                mock.patch.object(cleanup, "ordinary", wraps=old_ordinary) as guards, \
                mock.patch.object(cleanup, "value_hash", side_effect=AssertionError("global JSON fingerprint")), \
                mock.patch.object(Path, "read_bytes", side_effect=AssertionError("payload read")):
            scan = cleanup._scan(self.build / "cache")
        self.assertEqual(scan["files"], 512); self.assertEqual(scan["bytes"], 512)
        self.assertEqual(stamps.call_count, 1); self.assertEqual(guards.call_count, 1)
        self.assertEqual(len(scan["nodes"]), 512 + 9)

    def test_directory_enumeration_order_does_not_change_fingerprint(self):
        for name in ("z", "a", "m"):
            self.file("cache/" + name + "/asset.bin")
        expected = cleanup._scan(self.build / "cache")
        real_scandir = os.scandir
        class Reversed:
            def __init__(self, path):
                with real_scandir(path) as entries: self.entries = list(entries)[::-1]
            def __enter__(self): return iter(self.entries)
            def __exit__(self, *unused): pass
        with mock.patch.object(cleanup.os, "scandir", Reversed):
            observed = cleanup._scan(self.build / "cache")
        self.assertEqual(observed["fingerprint"], expected["fingerprint"])

    def test_pending_directory_substituted_by_link_is_rejected_before_enumeration(self):
        self.file("cache/a/first.bin"); self.file("cache/z/last.bin")
        foreign = self.root.parent / "foreign"; foreign.mkdir(); (foreign / "keep.bin").write_bytes(b"owned elsewhere")
        pending = self.build / "cache/z"; shifted = self.build / "moved-z"
        real_scandir = os.scandir; visited = []
        def enumerate_directory(path):
            visited.append(Path(path))
            if Path(path) == self.build / "cache/a":
                pending.rename(shifted); pending.symlink_to(foreign, target_is_directory=True)
            return real_scandir(path)
        with mock.patch.object(cleanup.os, "scandir", side_effect=enumerate_directory):
            with self.assertRaises(state.WizardError): cleanup._scan(self.build / "cache")
        self.assertNotIn(pending, visited); self.assertNotIn(foreign, visited)
        self.assertEqual((foreign / "keep.bin").read_bytes(), b"owned elsewhere")

    def test_windows_cached_zero_ids_require_exactly_one_leaf_stat(self):
        self.file("cache/file.bin")
        leaf = self.build / "cache/file.bin"; real = leaf.lstat()
        fields = {name: getattr(real, name) for name in
                  ("st_dev", "st_ino", "st_mode", "st_size", "st_mtime_ns", "st_ctime_ns", "st_nlink")}
        fields.update(st_dev=0, st_ino=0, st_nlink=0, st_file_attributes=0)
        cached = SimpleNamespace(**fields)
        with mock.patch.object(Path, "lstat", return_value=real) as metadata:
            accepted = cleanup._metadata_stamp(leaf, cached)
        self.assertEqual(metadata.call_count, 1)
        self.assertEqual(accepted[:2], (real.st_dev, real.st_ino))

    def test_zero_cached_link_count_cannot_admit_hardlink(self):
        self.file("cache/file.bin")
        leaf = self.build / "cache/file.bin"
        os.link(leaf, self.root / "external-hardlink.bin")
        real = leaf.lstat()
        cached = SimpleNamespace(st_dev=0, st_ino=0, st_nlink=0, st_mode=real.st_mode,
                                 st_size=real.st_size, st_mtime_ns=real.st_mtime_ns,
                                 st_ctime_ns=real.st_ctime_ns, st_file_attributes=0)
        with self.assertRaises(state.WizardError) as error:
            cleanup._metadata_stamp(leaf, cached)
        self.assertEqual(error.exception.code, "cleanup_linked_path")

    def test_windows_native_witness_is_batched_once_per_directory(self):
        for folder in range(3):
            for index in range(12): self.file(f"cache/{folder}/asset-{index}.bin")
        helper = cleanup._helper()
        calls = []
        def native(path):
            calls.append(path)
            return {leaf.name: ("win32-v1", 7, str(leaf.lstat().st_ino), leaf.stat().st_size, 11, 12)
                    for leaf in path.iterdir() if leaf.is_file()}
        with mock.patch.object(cleanup.sys, "platform", "win32"), \
                mock.patch.object(helper, "_windows_metadata", SimpleNamespace(directory=native)), \
                mock.patch.object(helper, "_file_witness_stamp", side_effect=AssertionError("native leaf open")):
            scan = cleanup._scan(self.build / "cache")
        self.assertEqual(len(calls), 4)
        self.assertEqual(scan["files"], 36)
        self.assertTrue(all(stamp[6][0] == "win32-v1" for _, stamp in scan["nodes"] if stat.S_ISREG(stamp[2])))

    def test_windows_batch_cancel_runs_between_queries_and_closes_handle(self):
        helper = cleanup._helper(); cancel = threading.Event(); closed = []
        def query(*unused): cancel.set(); return True
        kernel = SimpleNamespace(GetFileInformationByHandleEx=query,
                                 CloseHandle=lambda handle: closed.append(handle))
        class Reader:
            def __init__(self, kernel=None): self.kernel = kernel; self.filesystems = {}
            def directory(self, path):
                try: self.kernel.GetFileInformationByHandleEx(123)
                finally: self.kernel.CloseHandle(123)
        shared = Reader(kernel)
        tracker = cleanup._ScanProgress(None, "cleanup-scan", cancel)
        with mock.patch.object(cleanup.sys, "platform", "win32"), \
                mock.patch.object(helper, "_windows_metadata", shared), \
                mock.patch.object(helper, "_WindowsFileMetadata", Reader):
            with self.assertRaises(state.Cancelled): cleanup._directory_metadata(self.build, tracker)
        self.assertEqual(closed, [123])
        self.assertIs(shared.kernel, kernel)
        self.assertEqual(tracker.metadata_queries, 1)

    def test_cancel_during_flat_folder_enumeration_is_observed_before_any_delete(self):
        for index in range(128): self.file(f"cache/asset-{index}.bin")
        cancel = threading.Event(); events = []; observations = 0
        real_scandir = os.scandir
        class Counting:
            def __init__(self, path): self.entries = real_scandir(path)
            def __enter__(self):
                for entry in self.entries:
                    nonlocal observations
                    observations += 1
                    if observations == 20: cancel.set()
                    yield entry
            def __exit__(self, *unused): self.entries.close()
        with mock.patch.object(cleanup.os, "scandir", Counting), self.ticking():
            with self.assertRaises(state.Cancelled):
                cleanup._scan(self.build / "cache", events.append, cancel=cancel)
        self.assertEqual(observations, 20)
        self.assertEqual(len(list((self.build / "cache").iterdir())), 128)
        self.assertGreater(len(events), 2)

    def test_plan_cancel_never_publishes_partial_preview_and_releases_lock(self):
        self.success()
        for index in range(32): self.file(f"cache/asset-{index}.bin")
        cancel = threading.Event()
        def progress(event):
            if event["phase"] == "cleanup-scan" and event.get("files", 0) >= 3: cancel.set()
        with self.ticking(), self.assertRaises(state.Cancelled):
            cleanup.plan(self.root, "build-cache", progress, cancel=cancel)
        self.assertEqual(cleanup._plans, {})
        with state.file_lock(self.root / "run.lock"): pass
        self.assertEqual(len(list((self.build / "cache").iterdir())), 32)

    def test_preview_progress_is_aggregate_across_all_selected_roots(self):
        self.success()
        for area in ("cache", "projects", "inputs"):
            for index in range(4): self.file(f"{area}/asset-{index}.bin", b"x")
        events = []
        with self.ticking(): preview = cleanup.plan(self.root, "build-cache", events.append)
        scan = [event for event in events if event["phase"] == "cleanup-scan"]
        self.assertEqual(scan[-1]["files"], 12); self.assertEqual(scan[-1]["bytes"], 12)
        self.assertEqual(scan[-1]["folders"], 3)
        self.assertEqual([event["files"] for event in scan], sorted(event["files"] for event in scan))
        self.assertEqual(preview["files"], 12)
        self.assertTrue(all("currentPath" in event and "elapsedSeconds" in event for event in scan))
        self.assertEqual(scan[-1]["processedEntries"], 15)
        self.assertTrue(all("metadataQueries" in event and "subphase" in event for event in scan))

    def test_cancel_in_inventory_ownership_is_not_converted_to_warning(self):
        value = self.store.create({"mode": "build"}); value["status"] = "failed"; self.store.save(value)
        with mock.patch.object(cleanup, "_session_scope", side_effect=state.Cancelled()):
            with self.assertRaises(state.Cancelled): cleanup.inventory(self.root)

    def test_cancel_enumerating_owner_runs_is_not_converted_to_warning(self):
        from run_storage import Ownership
        with mock.patch.object(Ownership, "_folders", side_effect=state.Cancelled()):
            with self.assertRaises(state.Cancelled): cleanup.inventory(self.root)

    def test_inventory_reports_inside_a_single_large_directory(self):
        for index in range(32): self.file(f"cache/asset-{index}.bin")
        events = []
        with self.ticking(): result = cleanup.inventory(self.root, events.append)
        updates = [event for event in events if event["phase"] == "storage-inventory"
                   and event.get("currentPath") == "build/cache"]
        self.assertGreater(len(updates), 3)
        self.assertGreater(updates[-1]["files"], updates[0]["files"])
        self.assertTrue(result["complete"])

    def test_legacy_ownership_log_cancel_is_checked_per_line(self):
        value = self.store.create({"mode": "build"})
        folder = self.root / "sessions" / value["session"]
        path = folder / "logs/progress.log"; path.parent.mkdir()
        path.write_text('{"code":"unrelated"}\n' * 1000)
        cancel = mock.Mock(); cancel.is_set.side_effect = [False] * 10 + [True]
        with self.assertRaises(state.Cancelled):
            cleanup._qualified_games(folder, value, {}, cancel=cancel)
        self.assertEqual(cancel.is_set.call_count, 11)

    def test_cancel_after_one_deleted_file_retains_accurate_partial_result(self):
        self.success()
        for index in range(3): self.file(f"cache/asset-{index}.bin", b"abc")
        preview = cleanup.plan(self.root, "build-cache")
        cancel = threading.Event(); events = []
        def progress(event):
            events.append(event)
            if event["phase"] == "cleanup-delete" and event.get("files") == 1: cancel.set()
        with self.ticking():
            with self.assertRaises(state.Cancelled) as error:
                cleanup.execute(self.root, preview, progress, cancel=cancel)
        self.assertEqual(error.exception.parameters["result"]["deletedFiles"], 1)
        self.assertEqual(error.exception.parameters["result"]["freedBytes"], 3)
        self.assertEqual(len(list((self.build / "cache").iterdir())), 2)
        with state.file_lock(self.root / "run.lock"): pass
        deletion = [event for event in events if event["phase"] == "cleanup-delete"]
        self.assertTrue(all(event["unit"] == "entries" for event in deletion))
        self.assertTrue(all(event["done"] < event["total"] for event in deletion))

    def test_repair_suboperation_cannot_replace_global_deletion_counters(self):
        self.success(); self.file("cache/file.bin")
        preview = cleanup.plan(self.root, "build-cache"); events = []
        real_delete = cleanup_io.delete_owned
        def deleting(*args, **kwargs):
            kwargs["progress"]({"phase": "cleanup-readonly-repair", "done": 1, "total": 1, "detail": "leaf"})
            return real_delete(*args, **kwargs)
        with mock.patch.object(cleanup_io, "delete_owned", side_effect=deleting):
            cleanup.execute(self.root, preview, events.append)
        repairs = [event for event in events if event["phase"] == "cleanup-readonly-repair"]
        self.assertEqual(repairs[0]["done"], 0); self.assertEqual(repairs[0]["total"], 2)
        self.assertEqual(repairs[0]["files"], 0)
        self.assertEqual(repairs[0]["action"]["done"], 1)

    def test_cancelled_preview_retains_existing_targets_and_does_not_retire_run(self):
        value = self.store.create({"mode": "build"}); value["status"] = "failed"; self.store.save(value)
        folder = self.root / "sessions" / value["session"]
        cancel = threading.Event()
        def progress(event):
            if event["phase"] == "cleanup-session-scan": cancel.set()
        with self.assertRaises(state.Cancelled):
            cleanup.plan_session(self.root, value["session"], remove_data=True, progress=progress, cancel=cancel)
        self.assertTrue(folder.is_dir()); self.assertEqual(cleanup._plans, {})

    def test_signing_pin_links_and_same_size_mutation_remain_rejected(self):
        for kind in ("pin", "signing", "hardlink", "edit"):
            with self.subTest(kind=kind):
                self.success(); self.file("cache/file.bin", b"original")
                cache = self.build / "cache"
                if kind == "pin": (cache / ".keep").write_bytes(b"")
                elif kind == "signing": (cache / "private.jks").write_bytes(b"key")
                elif kind == "hardlink": os.link(cache / "file.bin", cache / "linked.bin")
                preview = cleanup.plan(self.root, "build-cache")
                if kind == "edit":
                    leaf = cache / "file.bin"; stamp = leaf.stat()
                    leaf.write_bytes(b"modified"); os.utime(leaf, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
                    with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
                else: self.assertFalse(preview["eligible"])
                self.assertTrue((cache / "file.bin").exists())
                for path in cache.iterdir(): path.unlink()
                cache.rmdir()


if __name__ == "__main__": unittest.main()
