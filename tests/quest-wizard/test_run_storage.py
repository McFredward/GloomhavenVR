"""Measure actual retained run dependencies once, including archived failures."""
import hashlib
import json
import os
from pathlib import Path
import sys
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import state
import test_run_cleanup as fixtures


class RunStorageTests(unittest.TestCase):
    setUp = fixtures.RunCleanupTests.setUp
    file = fixtures.RunCleanupTests.file
    snapshot = fixtures.RunCleanupTests.snapshot
    manifest = fixtures.RunCleanupTests.manifest
    project = fixtures.RunCleanupTests.project
    raw_recovery = fixtures.RunCleanupTests.raw_recovery
    other_run = fixtures.RunCleanupTests.other_run
    fallback_run = fixtures.RunCleanupTests.fallback_run

    def row(self, result, session=None, *, retired=False):
        return next(row for row in result["retiredRuns" if retired else "runs"] if row["session"] == (session or self.session))

    def test_real_legacy_full_migration_is_attributed_to_its_abandoned_run(self):
        _, _, project, raw, staged, snapshot = self.fallback_run(log_only=True)
        result = cleanup.inventory(self.root); row = self.row(result)
        direct = sum(path.stat().st_size for folder in (project, raw, staged, snapshot)
                     for path in folder.rglob("*") if path.is_file())
        self.assertGreaterEqual(row["intermediateBytes"], direct)
        self.assertGreaterEqual(row["reclaimableBytes"], direct)
        self.assertEqual(row["totalBytes"], row["exclusiveBytes"] + row["sharedBytes"])
        self.assertTrue(row["exact"]); self.assertFalse(row["reclaimableExact"])

    def test_two_normal_runs_share_one_game_project_without_global_double_count(self):
        manifest = self.manifest(); project = self.project(manifest); raw, staged = self.raw_recovery(manifest)
        other = self.other_run(); self.manifest(other)
        result = cleanup.inventory(self.root)
        own, shared = self.row(result), self.row(result, other["session"])
        self.assertGreater(own["sharedBytes"], 0)
        self.assertEqual(own["sharedBytes"], shared["sharedBytes"])
        self.assertEqual(result["sharedBytes"], own["sharedBytes"])
        self.assertEqual(result["totalBytes"], result["exclusiveBytes"] + result["sharedBytes"] + result["unassignedBytes"])
        self.assertEqual(own["reclaimableBytes"], 0); self.assertEqual(shared["reclaimableBytes"], 0)

    def test_retired_runs_remain_storage_only_and_can_reclaim_their_residual_caches(self):
        _, _, project, raw, staged, snapshot = self.fallback_run(archived=True)
        before = cleanup.inventory(self.root)
        self.assertEqual(before["runs"], [])
        retired = self.row(before, retired=True)
        self.assertTrue(retired["retired"]); self.assertGreater(retired["reclaimableBytes"], 0)
        preview = cleanup.plan_session(self.root, self.session, remove_data=True)
        cleanup.execute(self.root, preview)
        after = cleanup.inventory(self.root)
        self.assertEqual(after["runs"], [])
        self.assertEqual(self.row(after, retired=True)["reclaimableBytes"], 0)
        self.assertLess(after["totalBytes"], before["totalBytes"])

    def test_tool_delivery_and_unassigned_buckets_show_storage_outside_session_folder(self):
        self.manifest()
        self.file(self.build / "tool-cache/unity/tool.bin", b"t" * 1000)
        self.file(self.build / "builds" / ("a" * 64) / "Game.apk", b"a" * 2000)
        self.file(self.build / "cache/unbound-owner/large.bin", b"u" * 3000)
        result = cleanup.inventory(self.root)
        self.assertEqual(result["toolBytes"], 1000)
        self.assertEqual(result["deliverableBytes"], 2000)
        self.assertGreaterEqual(result["unassignedBytes"], 6000)
        self.assertGreaterEqual(result["reusableBytes"], 3000)

    def test_completed_build_receipt_assigns_deliverables_but_never_offers_them_for_cleanup(self):
        self.manifest(); apk = self.file(self.build / "builds" / ("a" * 64) / "Game.apk", b"signed APK")
        bank = self.file(self.build / "builds" / ("a" * 64) / "Game-content.zip", b"retained bank")
        details = {"apk": str(apk), "sourcePackage": str(bank)}
        build_stage = next(row for row in self.run["stages"] if row["id"] == "build")
        build_stage.update(status="complete", details=details)
        self.run["status"] = "complete"; self.store.save(self.run)
        result = cleanup.inventory(self.root); row = self.row(result)
        self.assertEqual(row["deliverableBytes"], apk.stat().st_size + bank.stat().st_size)
        self.assertEqual(row["reclaimableBytes"], 0)
        self.assertTrue(apk.is_file() and bank.is_file())

    def test_inventory_does_not_open_payloads_or_hash_game_data(self):
        manifest = self.manifest(); project = self.project(manifest); raw, staged = self.raw_recovery(manifest)
        apk = self.file(self.build / "builds" / ("a" * 64) / "Game.apk", b"APK")
        allowed = {"wizard-owner.json", "state.json", "profile.json", ".snapshot.json", "owner.json",
                   "original-source.json", "stage-owner.json", "progress.log", "progress.previous.log"}
        def metadata(path):
            return (path.name in allowed or cleanup.HEX.fullmatch(path.stem)
                    or path.parent.name == "receipts" and path.name in {name + ".json" for name in state.STAGES})
        original_open = Path.open; original_bytes = Path.read_bytes
        def metadata_open(path, *args, **kwargs):
            mode = args[0] if args else kwargs.get("mode", "r")
            if not any(flag in mode for flag in ("w", "a", "x")):
                if not metadata(path):
                    raise AssertionError("Inventory opened payload: " + str(path))
            return original_open(path, *args, **kwargs)
        def metadata_bytes(path):
            if not metadata(path):
                raise AssertionError("Inventory read payload: " + str(path))
            return original_bytes(path)
        with mock.patch.object(Path, "open", metadata_open), mock.patch.object(Path, "read_bytes", metadata_bytes):
            result = cleanup.inventory(self.root)
        self.assertTrue(result["complete"])

    def test_one_walk_counts_each_nested_resource_directory_once(self):
        manifest = self.manifest(); project = self.project(manifest); raw, staged = self.raw_recovery(manifest)
        other = self.other_run(); self.manifest(other)
        original = cleanup.os.scandir; visited = []
        def scan(path):
            visited.append(Path(path)); return original(path)
        with mock.patch.object(cleanup.os, "scandir", scan): cleanup.inventory(self.root)
        for folder in (project / "Assets", project / "Library", raw / "RecoveredProject", staged / "project"):
            self.assertEqual(visited.count(folder), 1)

    def test_keep_pin_and_snapshot_surplus_do_not_advertise_those_bytes_as_reclaimable(self):
        manifest = self.manifest(); project = self.project(manifest)
        self.file(project / ".keep", b"")
        snapshot = self.build / "inputs/game" / manifest["game"]["key"]
        self.file(snapshot / "unowned.txt", b"user addition")
        result = cleanup.inventory(self.root); row = self.row(result)
        preview = cleanup.plan_session(self.root, self.session, remove_data=True)
        self.assertEqual(row["reclaimableBytes"], preview["bytes"])
        self.assertGreater(row["intermediateBytes"], row["reclaimableBytes"])

    def test_workspace_pin_prevents_any_prospective_reclamation(self):
        manifest = self.manifest(); self.project(manifest); self.file(self.root / ".keep", b"")
        result = cleanup.inventory(self.root)
        self.assertEqual(self.row(result)["reclaimableBytes"], 0)

    def test_support_summary_is_bounded_and_has_same_measured_ownership(self):
        manifest = self.manifest(); self.project(manifest)
        result = cleanup.inventory(self.root)
        path = self.root / "logs/storage-analysis.json"
        self.assertTrue(result["analysisSaved"]); self.assertLess(path.stat().st_size, 2 * 1048576)
        summary = json.loads(path.read_text())
        self.assertEqual(summary["schema"], 1)
        self.assertEqual(summary["runs"], result["runs"])
        self.assertEqual(summary["totalBytes"], result["totalBytes"])
        self.assertEqual(summary["ownershipWarnings"], [])

    def test_incomplete_ownership_stays_unassigned_with_a_visible_reason(self):
        self.manifest(); other = self.other_run()
        state_path = self.store.session_dir(other["session"]) / "state.json"
        state_path.write_text("unreadable owner")
        unknown = self.file(self.build / "cache/not-owned/large.bin", b"x" * 5000)
        result = cleanup.inventory(self.root)
        self.assertFalse(result["ownershipComplete"])
        self.assertGreaterEqual(result["unassignedBytes"], unknown.stat().st_size)
        self.assertEqual(result["ownershipWarnings"][0]["code"], "unreadable_run_owner")
        self.assertEqual(self.row(result)["reclaimableBytes"], 0)


if __name__ == "__main__": unittest.main()
