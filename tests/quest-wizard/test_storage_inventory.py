"""Workspace size reporting is read-only, exact by stat, and independent of hashes."""
from pathlib import Path
import hashlib
import json
import os
import sys
import tempfile
import threading
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import state


class StorageInventoryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.temp = Path(temporary.name); self.store = state.Store(self.temp / "owned")
        self.root = self.store.root

    def file(self, name, size):
        path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"a" * size); return path

    def test_measures_every_category_without_reading_file_content_or_hashing(self):
        mapping = {"inputs": "build/inputs/game/asset.bin", "converted-assets": "build/cache/recovery/data.bin",
            "unity-projects": "build/projects/key/Assets/figure.asset", "unity-caches": "build/projects/key/Library/cache.bin",
            "tools": "build/tool-cache/unity/unity.exe", "apk-content": "build/builds/key/owned.apk",
            "signing-backups": "device/signing-recovery/serial/txn/private.tar",
            "logs-captures": "device/captures/quest-capture.zip", "other": "other/result.json"}
        expected = {}
        for number, (category, name) in enumerate(mapping.items(), 1):
            self.file(name, number * 17); expected[category] = number * 17
        owner_size = (self.root / "wizard-owner.json").stat().st_size
        with mock.patch.object(cleanup.hashlib, "sha256", side_effect=AssertionError("no game hash")):
            # _root qualifies only its small ownership JSON. Replace its proof
            # helper to assert that the inventory itself never hashes file data.
            with mock.patch.object(cleanup, "_root", return_value=(self.root, self.root / "build")):
                result = cleanup.inventory(self.root)
        rows = {row["id"]: row for row in result["categories"]}
        for category, size in expected.items():
            self.assertEqual(rows[category]["bytes"], size + (owner_size if category == "other" else 0))
        self.assertEqual(result["totalBytes"], sum(expected.values()) + owner_size)
        self.assertEqual(result["files"], 10); self.assertEqual(result["inaccessible"], 0)

    def test_existing_signing_keys_and_bank_are_reported_not_deleted(self):
        key = self.file("build/signing/user.keystore", 15)
        apk = self.file("build/builds/current/owned.apk", 70)
        bank = self.file("build/builds/current/owned-content.zip", 90)
        result = cleanup.inventory(self.root)
        self.assertTrue(key.is_file() and apk.is_file() and bank.is_file())
        rows = {row["id"]: row["bytes"] for row in result["categories"]}
        self.assertEqual(rows["apk-content"], 160); self.assertEqual(rows["tools"], 15)

    def test_symlink_and_borrowed_tree_are_never_followed(self):
        if sys.platform == "win32": self.skipTest("symlink privilege varies")
        foreign = self.temp / "foreign"; foreign.mkdir(); (foreign / "large.bin").write_bytes(b"x" * 10000)
        (self.root / "borrowed").symlink_to(foreign, target_is_directory=True)
        result = cleanup.inventory(self.root)
        self.assertEqual(result["skippedLinks"], 1)
        self.assertLess(result["totalBytes"], 1000)

    def test_unowned_root_does_not_get_an_inventory(self):
        (self.root / "wizard-owner.json").write_text('{"schema":1,"owner":"foreign"}')
        with self.assertRaises(state.WizardError): cleanup.inventory(self.root)

    def test_progress_finishes_only_after_actual_scan(self):
        self.file("build/cache/data.bin", 8192)
        events = []; result = cleanup.inventory(self.root, events.append)
        self.assertEqual(events[-1]["phase"], "storage-inventory-complete")
        self.assertEqual(events[-1]["done"], result["files"])
        self.assertEqual(events[-1]["total"], result["files"])

    def test_readonly_measurement_does_not_take_mutating_build_lock(self):
        with state.file_lock(self.root / "run.lock"):
            result = cleanup.inventory(self.root)
        self.assertEqual(result["status"], "complete")

    def test_cancel_stops_scan(self):
        cancelled = threading.Event(); cancelled.set()
        with self.assertRaises(state.Cancelled): cleanup.inventory(self.root, cancel=cancelled)

    def test_project_cache_category_preserves_project_assets(self):
        self.assertEqual(cleanup._inventory_category(Path("build/projects/key/Assets/Library/Sound.asset")), "unity-projects")
        self.assertEqual(cleanup._inventory_category(Path("build/projects/key/Assets/Figures/figure.asset")), "unity-projects")
        self.assertEqual(cleanup._inventory_category(Path("build/projects/key/Library/Artifacts/a.bin")), "unity-caches")

    def test_original_apk_rollback_is_reported_separately_from_mutable_appdata(self):
        self.file("device/signing-recovery/quest/txn/original.apk", 700)
        self.file("device/signing-recovery/quest/txn/private-one.tar", 30)
        self.file("device/signing-recovery/quest/txn/external-one.tar", 20)
        self.file("device/signing-recovery/quest/txn/journal.json", 10)
        value = cleanup.inventory(self.root)["signingBackups"]
        self.assertEqual(value, {"originalApkBytes": 700, "appDataBytes": 50, "otherBytes": 10})

    def test_completed_apk_and_bank_enable_cache_cleanup_before_installation(self):
        build = self.root / "build"; key = "a" * 64
        apk = self.file("build/builds/" + key + "/owned.apk", 10)
        bank = self.file("build/builds/" + key + "/owned-content.zip", 20)
        def row(path):
            return {"path": path.relative_to(build).as_posix(), "size": path.stat().st_size,
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        apk_row, bank_row = row(apk), row(bank)
        details = {"isDiagnostic": False, "apkSha256": apk_row["sha256"], "contentFiles": [bank_row]}
        receipt = "receipts/build/" + key + ".json"
        state.atomic_json(build / receipt, {"schema": 1, "stage": "build", "key": key,
            "outputs": [apk_row, bank_row], "details": details})
        state.atomic_json(build / "latest-build.json", {"schema": 1, "apk": apk_row["path"], "receipt": receipt, "details": details})
        self.assertTrue(cleanup.has_completed_build(self.root))
        self.assertTrue(cleanup.inventory(self.root)["canDeleteBuildCache"])
        bank.unlink()
        self.assertFalse(cleanup.has_completed_build(self.root))

    def test_hard_linked_allocations_count_once_but_logical_file_sizes_are_visible(self):
        if sys.platform == "win32": self.skipTest("allocated stat blocks are POSIX observations")
        original = self.file("build/cache/original.bin", 5000)
        linked = self.root / "build/cache/linked.bin"; os.link(original, linked)
        result = cleanup.inventory(self.root)
        logical = sum(path.stat().st_size for path in (original, linked, self.root / "wizard-owner.json"))
        allocated = sum(path.stat().st_blocks * 512 for path in (original, self.root / "wizard-owner.json"))
        self.assertEqual(result["totalBytes"], logical); self.assertEqual(result["allocatedBytes"], allocated)

    def test_inaccessible_folder_marks_snapshot_incomplete_without_claiming_success(self):
        target = self.root / "cannot-read"; target.mkdir()
        real = cleanup.os.scandir
        def inaccessible(folder):
            if Path(folder) == target: raise PermissionError("test unreadable folder")
            return real(folder)
        with mock.patch.object(cleanup.os, "scandir", side_effect=inaccessible):
            result = cleanup.inventory(self.root)
        self.assertEqual(result["status"], "complete"); self.assertFalse(result["complete"])
        self.assertEqual(result["inaccessible"], 1)


if __name__ == "__main__": unittest.main()
