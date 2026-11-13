"""Safe cleanup removes obsolete owners while keeping exact update/resume work."""
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys
import threading
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import state
from update_storage import FAST_AREAS
import test_cleanup as fixtures
import test_update_storage as update_fixtures


class AutomaticCleanupTests(unittest.TestCase):
    setUp = fixtures.CleanupTests.setUp
    file = fixtures.CleanupTests.file
    success = fixtures.CleanupTests.success
    snapshots = fixtures.CleanupTests.snapshots
    raw_recovery = fixtures.CleanupTests.raw_recovery
    update = update_fixtures.UpdateStorageTests.update

    def snapshot(self, kind, content):
        rows = [{"path": name, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
                for name, raw in sorted(content.items())]
        key = state.value_hash({"files": rows})
        for name, raw in content.items(): self.file("inputs/" + kind + "/" + key + "/" + name, raw)
        state.atomic_json(self.build / "inputs" / kind / key / ".snapshot.json", {"schema": 1, "files": rows})
        return {"key": key, "files": rows}

    def owner(self, number, *, status="complete", game=None, current=False):
        game = game or self.snapshot("game", {"app.info": ("original" + str(number)).encode()})
        mod = self.snapshot("mod", {"src/Plugin.cs": ("mod" + str(number)).encode()})
        manifest = {"schema": 1, "target": "game", "recipe": 46,
                    "game": {**game, "unityVersion": "2021.3.5f1"}, "mod": mod}
        manifest["inputKey"] = state.value_hash(manifest)
        path = self.build / "manifests" / (manifest["inputKey"] + ".json")
        state.atomic_json(path, manifest)
        run = self.store.create({"mode": "build"}); run["status"] = status
        row = next(row for row in run["stages"] if row["id"] == "inspect")
        row.update(status="complete", details={"inputKey": manifest["inputKey"], "gameKey": game["key"]})
        source = self.root / "source" / run["session"]; source.mkdir(parents=True)
        (source / "owned-source.py").write_text("retained or obsolete source")
        state.atomic_json(self.store.session_dir(run["session"]) / "source-attempt.json", {
            "schema": 1, "session": run["session"], "origin": "shipped-release"})
        next(row for row in run["stages"] if row["id"] == "source")["details"] = {"sourceRoot": str(source)}
        self.store.save(run)
        key = cleanup._project_key(manifest); project = self.build / "projects" / key
        cleanup._helper().project_generation_paths(self.build, project, manifest["inputKey"])
        self.file("projects/" + key + "/Library/Imported.bin", b"expensive import")
        self.file("cache/prepare-resume/" + key + "/journal.json", b'{"fixture":"owned project"}')
        if current:
            self.success()
            for pointer in (self.build / "latest-build.json", self.build / "receipts/build" / ("a" * 64 + ".json")):
                value = json.loads(pointer.read_text()); value["details"]["inputKey"] = manifest["inputKey"]
                state.atomic_json(pointer, value)
            state.atomic_json(self.build / "latest-input.json", {"manifest": "manifests/" + path.name,
                              "sourceGameRoot": str(self.original), "sourceRepo": str(source)})
        return run, manifest, project, source

    def test_one_plan_combines_duplicate_outputs_and_owned_export_temp_without_payload_reads(self):
        self.snapshots(); old, _ = self.update(1); current, _ = self.update(2, latest=True)
        temporary = self.root / "exports/install.zip.partial-"; temporary = temporary.with_name(temporary.name + "b" * 32)
        temporary.parent.mkdir(); temporary.write_bytes(b"failed export bytes")
        database = self.build / "cache/portable-witnesses.sqlite3"; database.parent.mkdir()
        with sqlite3.connect(database) as connection:
            connection.execute("CREATE TABLE portable_exports (destination TEXT PRIMARY KEY, value TEXT NOT NULL)")
            destination = str(temporary.parent / "install.zip")
            connection.execute("INSERT INTO portable_exports VALUES (?,?)", (destination, json.dumps({
                "schema": 1, "destination": destination, "temporary": str(temporary)})))
        # An unrelated filename does not gain ownership by resembling a temp.
        unknown = temporary.with_name("unknown.zip.partial-" + "c" * 32); unknown.write_bytes(b"unowned")
        original = Path.read_bytes
        def no_payload(path):
            if path.suffix not in (".json", ".py"): raise AssertionError("Unexpected payload read: " + str(path))
            return original(path)
        with mock.patch.object(Path, "read_bytes", no_payload), mock.patch.object(cleanup, "inventory", side_effect=AssertionError("whole workspace scan")):
            preview = cleanup.plan(self.root, "safe"); result = cleanup.execute(self.root, preview)
        # The imported current game's absent manifest makes source retirement
        # uncertain. Its input snapshot is retained, while closed old outputs
        # and an independently proven failed export can still be reclaimed.
        self.assertFalse(old.exists()); self.assertFalse(temporary.exists())
        self.assertTrue(current.exists() and unknown.exists() and database.exists())
        self.assertEqual(result["freedBytes"], preview["bytes"])
        self.assertEqual(result["deletedFiles"], preview["files"])
        self.assertEqual(result["fastUpdateImpact"]["status"], "preserved")
        self.assertEqual(result["invalidatedStages"], [])

    def test_fresh_owned_workspace_is_an_empty_successful_safe_cleanup(self):
        self.build.rmdir()
        preview = cleanup.plan(self.root, "safe")
        self.assertFalse(preview["eligible"])
        result = cleanup.execute(self.root, preview)
        self.assertEqual(result["freedBytes"], 0)
        self.assertEqual(result["fastUpdateImpact"]["status"], "preserved")

    def test_only_unreferenced_immutable_duplicate_is_removed_and_canonical_is_bound(self):
        _, wrong = self.snapshots(); raw = self.raw_recovery(wrong)
        preview = cleanup.plan(self.root, "safe")
        self.assertEqual(len(preview["paths"]), 2)
        result = cleanup.execute(self.root, preview)
        self.assertFalse(raw.exists())
        self.assertFalse((self.build / "inputs/game" / self.wrong).exists())
        self.assertTrue((self.build / "inputs/game" / self.correct).is_dir())
        self.assertEqual(result["fastUpdateImpact"]["status"], "preserved")

    def test_obsolete_completed_and_retired_owned_trees_are_removed_but_current_and_fast_work_survive(self):
        old, manifest, old_project, old_source = self.owner(1)
        retired, retired_manifest, retired_project, retired_source = self.owner(2)
        cleanup.execute(self.root, cleanup.plan_session(self.root, retired["session"]))
        current, current_manifest, current_project, current_source = self.owner(3, current=True)
        retained = []
        for area in FAST_AREAS:
            relative = area + "/" + "f" * 64 + "/owned-result.bin"
            self.file(relative, b"expensive reusable input"); retained.append(self.build / relative)
        retained.append(self.build / self.file("updates/" + "e" * 64 + "/code-player.apk")["path"])
        retained.append(self.build / self.file("signing/owner.keystore")["path"])
        retained.append(self.root / "device/signing-recovery/save.tar")
        retained[-1].parent.mkdir(parents=True); retained[-1].write_bytes(b"savegame")
        unknown = self.build / "projects" / ("d" * 64) / "unowned.bin"
        unknown.parent.mkdir(); unknown.write_bytes(b"ambiguous project")
        preview = cleanup.plan(self.root, "safe"); result = cleanup.execute(self.root, preview)
        self.assertGreater(result["freedBytes"], 0)
        for path in (old_project, old_source, retired_project, retired_source): self.assertFalse(path.exists(), str(path))
        for value in (manifest, retired_manifest):
            self.assertFalse((self.build / "inputs/game" / value["game"]["key"]).exists())
        self.assertTrue(current_project.is_dir() and current_source.is_dir())
        self.assertTrue((self.build / "inputs/game" / current_manifest["game"]["key"]).is_dir())
        self.assertTrue(all(path.is_file() for path in retained) and unknown.is_file())
        self.assertTrue(self.store.session_dir(old["session"]).is_dir())
        self.assertTrue((self.root / "logs/removed-runs" / retired["session"] / "state.json").is_file())
        self.assertEqual(self.store.load(current["session"])["status"], "complete")
        self.assertEqual(result["sessionsReset"], []); self.assertEqual(result["sessionsRemoved"], [])
        self.assertEqual(result["fastUpdateImpact"]["status"], "preserved")

    def test_unfinished_owner_and_qualified_game_keep_shared_project_and_originals(self):
        old, manifest, project, source = self.owner(1)
        self.owner(2, current=True)
        stopped = self.store.create({"mode": "build"}); stopped["status"] = "interrupted"
        stopped["storageProvenance"] = {"schema": 1, "games": [{"gameKey": manifest["game"]["key"]}]}
        self.store.save(stopped)
        cleanup.execute(self.root, cleanup.plan(self.root, "safe"))
        self.assertTrue(project.is_dir())
        self.assertTrue((self.build / "inputs/game" / manifest["game"]["key"]).is_dir())
        self.assertEqual(self.store.load(stopped["session"])["status"], "interrupted")

    def test_unexpected_file_in_old_mod_snapshot_is_retained(self):
        old, manifest, _, _ = self.owner(1)
        self.owner(2, current=True)
        folder = self.build / "inputs/mod" / manifest["mod"]["key"]
        extra = folder / "unique-local-edit.cs"; extra.write_text("// unrecorded source")
        preview = cleanup.plan(self.root, "safe"); cleanup.execute(self.root, preview)
        self.assertTrue(extra.is_file())
        self.assertTrue((folder / ".snapshot.json").is_file())
        self.assertTrue(any(row["reason"] == "surplus_files" and row["path"] == folder.relative_to(self.root).as_posix()
                            for row in preview["skipped"]))

    def test_pinned_obsolete_project_keeps_its_ownership_and_resume_metadata(self):
        old, _, project, _ = self.owner(1)
        self.owner(2, current=True)
        (project / ".keep").write_text("")
        marker = self.build / "cache/project-lifecycle" / project.name / "owner.json"
        journal = self.build / "cache/prepare-resume" / project.name / "journal.json"
        cleanup.execute(self.root, cleanup.plan(self.root, "safe"))
        self.assertTrue(project.is_dir() and marker.is_file() and journal.is_file())

    def test_unbound_or_corrupt_stopped_run_conservatively_keeps_all_build_inputs(self):
        self.owner(1); self.owner(2, current=True)
        stopped = self.store.create({"mode": "build"}); stopped["status"] = "blocked"; self.store.save(stopped)
        preview = cleanup.plan(self.root, "safe")
        self.assertFalse(preview["eligible"])
        self.assertIn("build/inputs", preview["protectedPaths"])
        (self.store.session_dir(stopped["session"]) / "state.json").write_text("broken")
        self.assertFalse(cleanup.plan(self.root, "safe")["eligible"])

    def test_new_pending_owner_or_changed_canonical_file_invalidates_before_deletion(self):
        self.snapshots()
        preview = cleanup.plan(self.root, "safe")
        self.assertTrue(preview["eligible"])
        canonical = self.build / "inputs/game" / self.correct / "app.info"
        canonical.unlink()
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue((self.build / "inputs/game" / self.wrong).exists())
        # Restore the canonical producer's input then bind the run set.
        canonical.write_bytes(b"app.info")
        self.owner(2, current=True); old, _ = self.update(1); self.update(2, latest=True)
        preview = cleanup.plan(self.root, "safe")
        self.store.create({"mode": "update-mod", "baseApk": str(old)})
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(old.is_file())

    def test_pins_borrowed_links_and_active_locks_retain_data(self):
        pinned, _ = self.update(1); borrowed, _ = self.update(2); self.update(3, latest=True)
        (pinned.parent / ".keep").write_text("")
        os.link(borrowed, self.temp / "borrowed.apk")
        preview = cleanup.plan(self.root, "safe")
        self.assertFalse(preview["eligible"])
        self.assertTrue(pinned.exists() and borrowed.exists())
        with state.file_lock(self.root / "run.lock"):
            with self.assertRaises(state.WizardError): cleanup.plan(self.root, "safe")
        with cleanup._helper().output_lock(self.build):
            with self.assertRaises(state.WizardError): cleanup.plan(self.root, "safe")

    def test_unreadable_manifest_or_raw_ownership_does_not_block_independent_obsolete_apk(self):
        for method in ("_manifests", "_raw_owners"):
            with self.subTest(method=method):
                old, _ = self.update(1); current, _ = self.update(2, latest=True)
                with mock.patch.object(cleanup, method, side_effect=cleanup._error("cleanup_linked_path")):
                    preview = cleanup.plan(self.root, "safe")
                cleanup.execute(self.root, preview)
                self.assertFalse(old.exists()); self.assertTrue(current.exists())
                self.assertTrue(any(row["reason"] == "current_dependency_unknown" for row in preview["skipped"]))

    def test_dirty_source_and_git_working_copy_remain_available(self):
        dirty, _, _, dirty_source = self.owner(1)
        git_run, _, _, git_source = self.owner(2)
        self.owner(3, current=True)
        state.atomic_json(self.store.session_dir(dirty["session"]) / "source.json", {"schema": 1, "dirty": True})
        (git_source / ".git").mkdir(); (git_source / ".git/config").write_text("original local Git copy")
        preview = cleanup.plan(self.root, "safe"); cleanup.execute(self.root, preview)
        self.assertTrue(dirty_source.is_dir() and git_source.is_dir())
        self.assertEqual(sum(row["reason"] == "source_edits_retained" for row in preview["skipped"]), 2)

    def test_cancelled_raw_or_snapshot_cleanup_keeps_owner_receipts_and_retries_residue(self):
        import cleanup_io
        for component in ("raw", "snapshot"):
            with self.subTest(component=component):
                _, wrong = self.snapshots(); raw = self.raw_recovery(wrong)
                snapshot = self.build / "inputs/game" / self.wrong
                cancel = threading.Event(); original = cleanup_io.delete_owned
                def remove(path, *args, **kwargs):
                    original(path, *args, **kwargs)
                    if ((component == "raw" and path.name == "asset.bin") or
                            (component == "snapshot" and snapshot in path.parents and path.name == "app.info")):
                        cancel.set()
                preview = cleanup.plan(self.root, "safe")
                with mock.patch.object(cleanup_io, "delete_owned", side_effect=remove):
                    with self.assertRaises(state.Cancelled): cleanup.execute(self.root, preview, cancel=cancel)
                if component == "raw": self.assertTrue((raw / "original-source.json").is_file())
                self.assertTrue((snapshot / ".snapshot.json").is_file())
                cleanup.execute(self.root, cleanup.plan(self.root, "safe"))
                self.assertFalse(raw.exists()); self.assertFalse(snapshot.exists())
                self.assertTrue((self.build / "inputs/game" / self.correct).is_dir())


if __name__ == "__main__": unittest.main()
