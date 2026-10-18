"""Published update history can shrink while exact compiler inputs remain reusable."""
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import state
import update_storage
import test_cleanup as fixtures


class UpdateStorageTests(unittest.TestCase):
    setUp = fixtures.CleanupTests.setUp
    file = fixtures.CleanupTests.file

    def update(self, number, *, latest=False):
        # Match update_driver.inspect's actual key: only local source paths are
        # omitted. A renamed/copy-pasted receipt cannot qualify another APK.
        plan = {"schema": 1, "mode": "update-mod", "baseApk": str(self.temp / "external.apk"),
                "baseApkSha256": str(number % 10) * 64, "gameInputKey": "a" * 64,
                "inputs": {"mod": {"modBuild": 690 + number}}, "sourcePackage": None}
        key = state.value_hash({name: value for name, value in plan.items() if name not in ("baseApk", "sourcePackage")})
        plan["updateKey"] = key
        folder = self.build / "updates" / key
        state.atomic_json(folder / "plan.json", plan)
        apk = self.file("updates/" + key + "/GloomhavenVR-Quest.apk", ("signed published APK " + str(number)).encode())
        details = {"updateKey": key, "inputKey": "a" * 64, "isDiagnostic": False,
                   "apkSha256": apk["sha256"], "contentFiles": [], "retainedGameContent": True}
        report = {"schema": 1, "buildResult": "Succeeded", "scope": "APK update", **details}
        state.atomic_json(self.build / (apk["path"] + ".build.json"), report)
        raw = (self.build / (apk["path"] + ".build.json")).read_bytes()
        row = {"path": apk["path"] + ".build.json", "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
        receipt = {"schema": 1, "stage": "build", "key": key, "outputs": [apk, row], "details": details}
        receipt_name = "receipts/build/" + key + ".json"
        state.atomic_json(self.build / receipt_name, receipt)
        if latest:
            state.atomic_json(self.build / "latest-build.json", {"schema": 1, "apk": apk["path"],
                              "receipt": receipt_name, "details": details})
        return self.build / apk["path"], plan

    def test_only_obsolete_final_apks_are_pruned_and_all_fast_inputs_survive(self):
        old, plan = self.update(1); current, _ = self.update(2, latest=True)
        retained = [self.build / "updates" / plan["updateKey"] / "code-player.apk",
                    self.build / "updates/code-projects" / ("b" * 64) / "Library/Bee/native.o",
                    self.build / "updates/source-inputs" / ("c" * 64) / "src/Plugin.cs",
                    self.build / "updates/game-inputs" / ("d" * 64) / "code/Managed/GH.Runtime.dll",
                    self.build / "builds" / ("e" * 64) / "GloomhavenVR-Quest-content.zip"]
        for path in retained:
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"reusable owned input")
        size = old.stat().st_size
        result = update_storage.maintain(self.root)
        self.assertEqual((result["prunedFiles"], result["prunedBytes"]), (1, size))
        self.assertFalse(old.exists()); self.assertTrue(current.is_file())
        self.assertTrue(old.with_suffix(".apk.build.json").is_file())
        self.assertTrue(all(path.is_file() for path in retained))
        self.assertEqual(result["warnings"], [])
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 0)

    def test_pending_input_or_explicit_keep_prevents_history_deletion(self):
        first, _ = self.update(1); second, _ = self.update(2); self.update(3, latest=True)
        run = self.store.create({"mode": "update-mod", "baseApk": str(first)})
        run["status"] = "blocked"; self.store.save(run)
        result = update_storage.maintain(self.root, keep_paths=[str(second)])
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(first.is_file() and second.is_file())
        run["status"] = "complete"; self.store.save(run)
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 2)

    def test_history_preview_is_explicit_and_retains_fast_cache_with_no_payload_reads(self):
        old, _ = self.update(1); current, _ = self.update(2, latest=True)
        original = Path.read_bytes
        def metadata_only(path):
            if path in (old, current): raise AssertionError("Cleanup must not read/hash signed APK payloads")
            return original(path)
        with mock.patch.object(Path, "read_bytes", metadata_only):
            preview = cleanup.plan(self.root, "update-history")
            self.assertEqual(preview["fastUpdateImpact"]["status"], "preserved")
            self.assertEqual(preview["warnings"], [])
            self.assertTrue(old.exists())
            result = cleanup.execute(self.root, preview)
        self.assertEqual(result["deletedFiles"], 1)
        self.assertFalse(old.exists()); self.assertTrue(current.is_file())

    def test_changed_plan_unclosed_output_or_external_input_is_never_deleted(self):
        changed, _ = self.update(1); partial, _ = self.update(2); self.update(3, latest=True)
        path = changed.parent / "plan.json"; value = json.loads(path.read_text()); value["inputs"]["mod"]["modBuild"] = 999
        state.atomic_json(path, value)
        (self.build / "receipts/build" / (partial.parent.name + ".json")).unlink()
        external = self.temp / "external.apk"; external.write_bytes(b"selected user APK")
        result = update_storage.maintain(self.root)
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(changed.exists() and partial.exists() and external.exists())

    def test_keep_pin_and_borrowed_hardlink_are_not_removed(self):
        pinned, _ = self.update(1); linked, _ = self.update(2); self.update(3, latest=True)
        (pinned.parent / ".keep").write_text("")
        import os
        os.link(linked, self.temp / "borrowed.apk")
        self.assertEqual(update_storage.maintain(self.root)["prunedFiles"], 0)
        self.assertTrue(pinned.is_file() and linked.is_file())

    def test_new_pending_run_after_preview_invalidates_confirmation(self):
        old, _ = self.update(1); self.update(2, latest=True)
        self.store.create({"mode": "update-mod", "baseApk": str(old), "status": "blocked"})
        # Current pending run protects its input; retire it first to preview.
        for folder in (self.root / "sessions").iterdir():
            value = self.store.load(folder.name); value["status"] = "complete"; self.store.save(value)
        preview = cleanup.plan(self.root, "update-history")
        self.store.create({"mode": "update-mod", "baseApk": str(old)})
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(old.exists())

    def test_cache_removal_warns_before_confirmation_and_run_removal_does_not(self):
        self.update(1, latest=True)
        self.file("cache/derived/owned.bin", b"reusable")
        preview = cleanup.plan(self.root, "build-cache")
        self.assertEqual(preview["fastUpdateImpact"]["status"], "slower")
        self.assertEqual(preview["warnings"][0]["code"], "fast_update_cache_removed")
        run = self.store.create({"mode": "update-mod"}); run["status"] = "complete"; self.store.save(run)
        preview = cleanup.plan_session(self.root, run["session"])
        self.assertEqual(preview["fastUpdateImpact"]["status"], "preserved")
        cleanup.execute(self.root, preview)
        self.assertTrue((self.build / "cache/derived/owned.bin").exists())

    def test_native_cache_is_categorized_as_reusable_instead_of_delivered_apk(self):
        self.update(1, latest=True)
        self.file("updates/code-projects/" + "b" * 64 + "/Library/Bee/native.o", b"x" * 1024)
        self.file("updates/game-inputs/" + "c" * 64 + "/code/Managed/GH.Runtime.dll", b"x" * 512)
        inventory = cleanup.inventory(self.root)
        rows = {row["id"]: row for row in inventory["categories"]}
        self.assertEqual(rows["unity-caches"]["bytes"], 1024)
        self.assertEqual(rows["inputs"]["bytes"], 512)
        self.assertEqual(inventory["fastUpdateBytes"], 1536)
        self.assertTrue(inventory["fastUpdatePolicy"]["retainedOnRunRemoval"])

    def test_retained_compiler_owner_attributes_one_shared_project_to_matching_runs(self):
        _, plan = self.update(1, latest=True)
        project = self.build / "updates/code-projects" / ("b" * 64)
        state.atomic_json(project / "QuestCodeUpdate/code-project-owner.json", {
            "state": "ready", "owner": {"codeKey": plan["updateKey"], "gameInputKey": plan["gameInputKey"]}})
        self.file(project.relative_to(self.build).as_posix() + "/Library/Bee/native.o", b"native" * 100)
        for _ in range(2):
            run = self.store.create({"mode": "update-mod"})
            run["status"] = "complete"
            next(row for row in run["stages"] if row["id"] == "build")["details"] = {
                "inputKey": plan["gameInputKey"], "updateKey": plan["updateKey"]}
            self.store.save(run)
        inventory = cleanup.inventory(self.root)
        self.assertEqual(len(inventory["runs"]), 2)
        self.assertGreaterEqual(inventory["sharedBytes"], 600)
        self.assertTrue(all(row["sharedBytes"] >= 600 for row in inventory["runs"]))

    def test_engine_lock_can_be_retained_without_recursive_run_lock_acquisition(self):
        self.update(1); self.update(2, latest=True)
        with state.file_lock(self.root / "run.lock"):
            result = update_storage.maintain(self.root, run_lock_held=True)
        self.assertEqual(result["prunedFiles"], 1)

    def test_optional_maintenance_failure_does_not_fail_an_already_completed_update(self):
        old, _ = self.update(1); current, _ = self.update(2, latest=True)
        with mock.patch.object(cleanup, "_guard", side_effect=cleanup._helper().BuildError("busy")):
            result = update_storage.maintain(self.root)
        self.assertEqual(result["prunedFiles"], 0)
        self.assertTrue(old.is_file() and current.is_file())
        self.assertEqual(result["warnings"][0]["code"], "update_history_retained")


if __name__ == "__main__": unittest.main()
