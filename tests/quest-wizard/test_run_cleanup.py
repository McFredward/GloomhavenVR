"""Stopped runs retire without sacrificing shared exports or delivered banks."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import discovery
import state


def sha(raw): return hashlib.sha256(raw).hexdigest()


class RunCleanupTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.temp = Path(temporary.name); self.root = self.temp / "owned"
        self.store = state.Store(self.root); self.build = self.root / "build"; self.build.mkdir()
        cleanup._plans.clear(); self.addCleanup(cleanup._plans.clear)
        self.run = self.store.create({"gameRoot": str(self.temp / "OriginalGame"), "mode": "build"})
        self.run["status"] = "cancelled"; self.store.save(self.run)
        self.session = self.run["session"]; self.folder = self.store.session_dir(self.session)
        self.file(self.folder / "logs/progress.log", b"retained diagnosis")

    def file(self, path, raw=b"owned bytes"):
        path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw); return path

    def snapshot(self, kind, files):
        rows = [{"path": name, "size": len(raw), "sha256": sha(raw)} for name, raw in sorted(files.items())]
        key = state.value_hash({"files": rows}); folder = self.build / "inputs" / kind / key
        for name, raw in files.items(): self.file(folder / name, raw)
        state.atomic_json(folder / ".snapshot.json", {"schema": 1, "files": rows})
        return {"key": key, "files": rows}, folder

    def manifest(self, run=None, *, game=None, mod=None, bind=True):
        run = self.run if run is None else run
        game, _ = self.snapshot("game", {"app.info": b"Unity original", "Managed/Apparance.Net.dll": b"Mono owned"}) if game is None else game
        mod, _ = self.snapshot("mod", {"src/GloomhavenVR/Plugin.cs": b"current mod"}) if mod is None else mod
        profile = {"schema": 1, "provider": "steam", "displayName": "Fixture", "steamId": "76561198000000000"}
        value = {"schema": 1, "recipe": 46, "target": "game", "game": {**game, "unityVersion": "2021.3.5f1"},
                 "mod": mod, "profile": profile, "profileKey": state.value_hash(profile)}
        value["inputKey"] = state.value_hash(value)
        path = self.build / "manifests" / (value["inputKey"] + ".json"); state.atomic_json(path, value)
        if bind:
            details = {"inputKey": value["inputKey"], "gameKey": value["game"]["key"]}
            key = state.value_hash({"run": run["session"], "stage": "inspect"})
            receipt = self.store.publish(run["session"], "inspect", key, [path], details)
            row = next(row for row in run["stages"] if row["id"] == "inspect")
            row.update(status="complete", details=details); run.setdefault("completed", {})["inspect"] = receipt
            self.store.save(run)
        return value

    def project(self, manifest):
        # Actual producer metadata owns this mutable imported project. No Unity
        # or asset conversion executes; importing the stable key/owner routines
        # checks that cleanup follows the real producer rather than itself.
        builder = discovery.builder(ROOT)
        key = builder.import_workspace.workspace_key(manifest, "game")
        self.assertEqual(cleanup._project_key(manifest), key)
        project = self.build / "projects" / key
        cleanup._helper().project_generation_paths(self.build, project, manifest["inputKey"])
        self.file(project / "Assets/Original.asset")
        self.file(project / "Library/Imported.bin", b"reproducible import" * 100)
        state.atomic_json(project / "QuestBuilderSettings.json", {"schema": 1, "inputKey": manifest["inputKey"]})
        state.atomic_json(self.build / "cache/prepare-resume" / key / "journal.json", {
            "schema": 1, "owner": "Quest preparation substage journal", "project": "projects/" + key,
            "inputKey": manifest["inputKey"], "target": "game", "recipe": 46, "steps": []})
        return project

    def raw_recovery(self, manifest):
        snapshot = self.build / "inputs/game" / manifest["game"]["key"]
        receipt = snapshot / ".snapshot.json"
        rows = [{"path": row["path"], "bytes": row["size"], "sha256": row["sha256"]} for row in manifest["game"]["files"]]
        rows.append({"path": ".snapshot.json", "bytes": receipt.stat().st_size, "sha256": sha(receipt.read_bytes())})
        folder = self.build / "cache/full-original-recovery" / ("a" * 64)
        state.atomic_json(folder / "original-source.json", {"schema": 1, "sourceInventory": rows,
            "sourceFingerprint": sha(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode())})
        self.file(folder / "RecoveredProject/asset.bin")
        stage = self.build / "cache/recovery" / ("b" * 64)
        state.atomic_json(stage / "stage-owner.json", {"schema": 1, "owner": "Quest recovered Campaign stage", "key": stage.name,
                         "gameKey": manifest["game"]["key"]})
        self.file(stage / "project/asset.bin")
        return folder, stage

    def other_run(self, status="blocked"):
        run = self.store.create({"gameRoot": str(self.temp / "OriginalGame"), "mode": "build"})
        run["status"] = status; self.store.save(run); return run

    def retire(self, **kwargs):
        original_log = (self.folder / "logs/progress.log").read_bytes()
        preview = cleanup.plan_session(self.root, self.session, **kwargs)
        result = cleanup.execute(self.root, preview)
        self.assertEqual(result["sessionsRemoved"], [self.session])
        self.assertFalse(self.folder.exists())
        self.assertTrue((self.root / "logs/removed-runs" / self.session / "state.json").is_file())
        self.assertEqual((self.root / "logs/removed-runs" / self.session / "logs/progress.log").read_bytes(), original_log)
        self.assertEqual(result["freedBytes"], preview["bytes"])
        return preview, result

    def test_session_only_removal_requires_confirmation_and_keeps_all_build_data(self):
        manifest = self.manifest(); project = self.project(manifest)
        preview = cleanup.plan_session(self.root, self.session)
        self.assertTrue(preview["eligible"]); self.assertEqual(preview["paths"], [])
        self.assertTrue(self.folder.is_dir()); self.assertTrue(project.is_dir())
        result = cleanup.execute(self.root, preview["id"])
        self.assertEqual(result["sessionsRemoved"], [self.session]); self.assertEqual(result["freedBytes"], 0)
        self.assertTrue(project.is_dir()); self.assertFalse((self.root / "latest-session.json").exists())

    def test_every_supported_stopped_status_can_be_retired(self):
        for status in ("ready", "failed", "blocked", "cancelled", "interrupted"):
            with self.subTest(status=status):
                run = self.other_run(status)
                selected = cleanup.plan_session(self.root, run["session"])
                cleanup.execute(self.root, selected)
                self.assertFalse(self.store.session_dir(run["session"]).exists())

    def test_running_and_successful_runs_are_not_removed(self):
        for status in ("running", "complete"):
            run = self.other_run(status)
            with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, run["session"], remove_data=True)
            self.assertTrue(self.store.session_dir(run["session"]).is_dir())

    def test_latest_pointer_repoints_to_most_recent_valid_survivor(self):
        other = self.other_run(); other["updated"] = 1; state.atomic_json(self.store.session_dir(other["session"]) / "state.json", other)
        state.atomic_json(self.root / "latest-session.json", {"schema": 1, "session": self.session})
        self.retire()
        self.assertEqual(state.read_json(self.root / "latest-session.json")["session"], other["session"])

    def test_another_latest_pointer_is_unchanged(self):
        other = self.other_run(); self.retire()
        self.assertEqual(state.read_json(self.root / "latest-session.json")["session"], other["session"])

    def test_exclusive_project_recovery_and_snapshots_need_no_completed_apk(self):
        manifest = self.manifest(); project = self.project(manifest); raw, staged = self.raw_recovery(manifest)
        originals = self.file(self.temp / "OriginalGame/Gloomhaven_Data/app.info", b"original PC game")
        keys = self.file(self.build / "signing/owner.keystore", b"private key")
        tool = self.file(self.build / "tool-cache/unity/tool.bin")
        bank = self.file(self.build / "builds" / ("c" * 64) / "GloomhavenVR-Quest-content.zip")
        preview, _ = self.retire(remove_data=True)
        self.assertGreater(preview["bytes"], 0)
        self.assertFalse(project.exists()); self.assertFalse(raw.exists()); self.assertFalse(staged.exists())
        self.assertFalse((self.build / "inputs/game" / manifest["game"]["key"]).exists())
        for retained in (originals, keys, tool, bank, self.build / "manifests" / (manifest["inputKey"] + ".json")):
            self.assertTrue(retained.is_file())

    def test_other_session_retains_shared_originals_and_imported_project(self):
        manifest = self.manifest(); project = self.project(manifest); raw, staged = self.raw_recovery(manifest)
        other = self.other_run(); self.manifest(other)
        preview, _ = self.retire(remove_data=True)
        for retained in (project, raw, staged, self.build / "inputs/game" / manifest["game"]["key"]): self.assertTrue(retained.is_dir())
        self.assertTrue(any(row["reason"] == "shared_or_delivered" for row in preview["skipped"]))
        self.assertTrue(self.store.session_dir(other["session"]).is_dir())

    def test_another_qualified_run_before_inspect_protects_its_game_and_import(self):
        manifest = self.manifest(); project = self.project(manifest)
        other = self.other_run(); other["qualification"] = {"spaceEstimate": {"cacheGameKey": manifest["game"]["key"]}}
        self.store.save(other)
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(project.is_dir()); self.assertTrue((self.build / "inputs/game" / manifest["game"]["key"]).is_dir())
        self.assertTrue(any(row["reason"] == "shared_or_delivered" for row in preview["skipped"]))

    def test_different_reported_game_inventory_removes_only_abandoned_new_project(self):
        old_game = self.snapshot("game", {"app.info": b"Unity original", "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json": b"old"})
        old_run = self.other_run("complete"); old = self.manifest(old_run, game=old_game); old_project = self.project(old)
        new_game = self.snapshot("game", {"app.info": b"Unity original", "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json": b"new"})
        new = self.manifest(game=new_game); new_project = self.project(new)
        self.retire(remove_data=True)
        self.assertTrue(old_project.is_dir()); self.assertTrue(old_game[1].is_dir())
        self.assertFalse(new_project.exists()); self.assertFalse(new_game[1].exists())

    def test_full_update_fallback_manifest_is_bound_to_captured_source_profile_and_game(self):
        old = self.manifest(); self.run["choices"]["mode"] = "update-mod"
        game = self.snapshot("game", {"app.info": b"actual changed game"})
        mod = self.snapshot("mod", {"src/GloomhavenVR/Plugin.cs": b"current source"})
        new = self.manifest(game=game, mod=mod, bind=False); project = self.project(new)
        source = next(row for row in self.run["stages"] if row["id"] == "source")
        source["details"] = {"sourceHash": state.value_hash(new["mod"]["files"])}
        self.run["qualification"] = {"spaceEstimate": {"cacheGameKey": new["game"]["key"]}}
        state.atomic_json(self.folder / "profile.json", new["profile"])
        self.store.save(self.run)
        self.retire(remove_data=True)
        self.assertFalse(project.exists()); self.assertFalse(game[1].exists())

    def test_real_steam_capture_provenance_is_normalized_when_binding_fallback(self):
        self.manifest(); self.run["choices"]["mode"] = "update-mod"
        game = self.snapshot("game", {"app.info": b"actual changed game"})
        new = self.manifest(game=game, bind=False)
        captured = {**new["profile"], "accountId": int(new["profile"]["steamId"]) - 76561197960265728,
                    "source": "windows-active-steam-and-loginusers.vdf"}
        normalized = {**captured, "source": "explicit-local-profile", "logoSha256": "d" * 64,
                      "dlcOwnership": {"schema": 1, "installedAppIds": [1809490]}}
        old_key = new["inputKey"]; new["profile"] = normalized; new["profileKey"] = state.value_hash(normalized)
        new["inputKey"] = state.value_hash({name: value for name, value in new.items() if name != "inputKey"})
        (self.build / "manifests" / (old_key + ".json")).unlink()
        state.atomic_json(self.build / "manifests" / (new["inputKey"] + ".json"), new)
        project = self.project(new)
        next(row for row in self.run["stages"] if row["id"] == "source")["details"] = {"sourceHash": state.value_hash(new["mod"]["files"])}
        self.run["qualification"] = {"spaceEstimate": {"cacheGameKey": new["game"]["key"]}}
        state.atomic_json(self.folder / "profile.json", captured); self.store.save(self.run)
        self.retire(remove_data=True)
        self.assertFalse(project.exists()); self.assertFalse(game[1].exists())

    def test_full_provider_identity_differences_do_not_alias_another_profile(self):
        original = {"schema": 1, "provider": "steam", "steamId": "76561198000000000", "displayName": "Fixture"}
        for changes in ({"steamId": "76561198000000001"}, {"providerId": "76561198000000001"},
                        {"displayName": "Someone else"}, {"accountId": 1}, {"provider": "gog", "providerId": "1234"}):
            with self.subTest(changes=changes): self.assertFalse(cleanup._same_identity(original, {**original, **changes}))

    def test_unrelated_fallback_manifest_is_not_guessed_from_latest_input(self):
        self.manifest(); other = self.manifest(game=self.snapshot("game", {"app.info": b"unowned different game"}), bind=False)
        project = self.project(other)
        state.atomic_json(self.build / "latest-input.json", {"manifest": "manifests/" + other["inputKey"] + ".json"})
        self.retire(remove_data=True); self.assertTrue(project.is_dir())

    def test_new_session_between_preview_and_confirmation_invalidates_preview(self):
        self.manifest(); preview = cleanup.plan_session(self.root, self.session, remove_data=True)
        self.other_run()
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(self.folder.is_dir())

    def test_shared_session_change_between_preview_and_confirmation_invalidates_preview(self):
        self.manifest(); other = self.other_run(); preview = cleanup.plan_session(self.root, self.session, remove_data=True)
        other["choices"]["mode"] = "update-mod"; self.store.save(other)
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(self.folder.is_dir())

    def test_unreadable_other_run_keeps_unproven_data_but_still_retires_selected_run(self):
        manifest = self.manifest(); project = self.project(manifest)
        other = self.other_run(); (self.store.session_dir(other["session"]) / "state.json").write_text("broken")
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(project.is_dir()); self.assertTrue(any(row["reason"] == "dependency_proof_incomplete" for row in preview["skipped"]))

    def test_bank_locator_inside_a_candidate_snapshot_protects_entire_tree_without_reading_bank(self):
        manifest = self.manifest(); snapshot = self.build / "inputs/game" / manifest["game"]["key"]
        bank = self.file(snapshot / "retained-bank.zip", b"large owned bank")
        apk = self.file(self.build / "builds" / ("d" * 64) / "Game.apk", b"APK")
        key = "e" * 64
        state.atomic_json(self.build / "receipts/build" / (key + ".json"), {"schema": 1, "stage": "build", "key": key,
            "outputs": [{"path": apk.relative_to(self.build).as_posix(), "size": 3, "sha256": sha(b"APK")}],
            "details": {"sourcePackage": str(bank), "retainedGameContent": True}})
        original_read = Path.read_bytes
        def bounded(path):
            if path in (bank, apk): raise AssertionError("Cleanup opened a delivered payload")
            return original_read(path)
        with mock.patch.object(Path, "read_bytes", bounded): self.retire(remove_data=True)
        self.assertTrue(bank.is_file()); self.assertTrue(apk.is_file())

    def test_shipped_session_source_copy_is_removed_but_original_source_zip_remains(self):
        self.manifest(); identity = "f" * 64
        checkout = self.root / "source" / (self.session + "-" + identity[:16])
        self.file(checkout / "src/Plugin.cs")
        state.atomic_json(checkout.with_name(checkout.name + ".owner.json"), {"schema": 1, "session": self.session,
                          "releaseHash": identity, "commit": "a" * 40})
        source_zip = self.file(self.temp / "GloomhavenVR-Quest-Builder.zip")
        self.retire(remove_data=True); self.assertFalse(checkout.exists()); self.assertTrue(source_zip.is_file())

    def test_other_session_keeps_shared_source_and_its_companion_owner(self):
        self.manifest(); identity = "f" * 64
        checkout = self.root / "source" / (self.session + "-" + identity[:16])
        self.file(checkout / "src/Plugin.cs")
        owner = checkout.with_name(checkout.name + ".owner.json")
        state.atomic_json(owner, {"schema": 1, "session": self.session, "releaseHash": identity, "commit": "a" * 40})
        other = self.other_run()
        next(row for row in other["stages"] if row["id"] == "source")["details"] = {"sourceRoot": str(checkout)}
        self.store.save(other)
        self.retire(remove_data=True); self.assertTrue(checkout.is_dir()); self.assertTrue(owner.is_file())

    def test_unowned_snapshot_surplus_is_retained_with_reason(self):
        manifest = self.manifest(); snapshot = self.build / "inputs/game" / manifest["game"]["key"]
        self.file(snapshot / "user-added-not-owned.txt")
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(snapshot.is_dir()); self.assertTrue(any(row["reason"] == "cleanup_surplus_files" for row in preview["skipped"]))

    def test_keep_pin_preserves_optional_data(self):
        manifest = self.manifest(); project = self.project(manifest); self.file(project / ".keep")
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(project.is_dir()); self.assertTrue(any(row["reason"] == "cleanup_pinned" for row in preview["skipped"]))
        self.assertTrue((self.build / "cache/project-lifecycle" / project.name / "owner.json").is_file())
        self.assertTrue((self.build / "cache/prepare-resume" / project.name / "journal.json").is_file())

    def test_keep_pin_in_run_prevents_retirement(self):
        self.file(self.folder / ".keep")
        with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, self.session)
        self.assertTrue(self.folder.is_dir())

    def test_workspace_keep_pin_prevents_retirement(self):
        self.file(self.root / ".keep")
        with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, self.session)
        self.assertTrue(self.folder.is_dir())

    def test_unproven_source_checkout_is_reported_and_preserved(self):
        checkout = self.root / "source" / self.session
        self.file(checkout / "src/Plugin.cs")
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(checkout.is_dir())
        self.assertTrue(any(row["path"] == checkout.relative_to(self.root).as_posix() and row["reason"] == "unproven_owner"
                            for row in preview["skipped"]))

    @unittest.skipIf(os.name == "nt", "POSIX borrowed-link control")
    def test_borrowed_project_symlink_is_not_traversed_or_deleted(self):
        manifest = self.manifest(); project = self.project(manifest)
        original = self.file(self.temp / "borrowed/asset.bin")
        (project / "Assets/Borrowed").symlink_to(original.parent, target_is_directory=True)
        preview, _ = self.retire(remove_data=True)
        self.assertTrue(original.is_file()); self.assertTrue(project.is_dir())
        self.assertTrue(any(row["reason"] == "linked_path" for row in preview["skipped"]))

    def test_live_editor_blocks_project_deletion_even_when_builder_is_stopped(self):
        manifest = self.manifest(); project = self.project(manifest)
        with mock.patch.object(cleanup, "_editor_busy", return_value=True):
            with self.assertRaises(state.WizardError) as error: cleanup.plan_session(self.root, self.session, remove_data=True)
            self.assertEqual(error.exception.code, "cleanup_editor_busy")
            self.assertIn("Close the Unity Editor", error.exception.message["en"])
            self.assertIn("Unity-Editor", error.exception.message["de"])
        self.assertTrue(project.is_dir()); self.assertTrue(self.folder.is_dir())

    def test_editor_started_after_preview_blocks_execution(self):
        manifest = self.manifest(); project = self.project(manifest)
        preview = cleanup.plan_session(self.root, self.session, remove_data=True)
        with mock.patch.object(cleanup, "_editor_busy", return_value=True):
            with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)
        self.assertTrue(project.is_dir()); self.assertTrue(self.folder.is_dir())

    def test_live_editor_on_raw_recovery_is_protected_independently_of_generated_project(self):
        manifest = self.manifest(); raw, _ = self.raw_recovery(manifest)
        project = raw / "RecoveredProject"
        self.file(project / "ProjectSettings/ProjectVersion.txt", b"m_EditorVersion: 2021.3.5f1\n")
        with mock.patch.object(cleanup, "_editor_busy", side_effect=lambda path: path == project):
            with self.assertRaises(state.WizardError) as error: cleanup.plan_session(self.root, self.session, remove_data=True)
            self.assertEqual(error.exception.code, "cleanup_editor_busy")
        self.assertTrue(raw.is_dir()); self.assertTrue(self.folder.is_dir())

    def test_real_kernel_run_lock_blocks_retirement(self):
        with state.file_lock(self.root / "run.lock"):
            with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, self.session)
        self.assertTrue(self.folder.is_dir())

    def test_client_paths_and_mode_cannot_expand_retained_confirmation(self):
        self.manifest(); preview = cleanup.plan_session(self.root, self.session)
        tampered = {**preview, "removeData": True, "paths": [{"path": "../OriginalGame"}]}
        result = cleanup.execute(self.root, tampered)
        self.assertEqual(result["freedBytes"], 0)
        self.assertTrue((self.build / "inputs").is_dir())
        with self.assertRaises(state.WizardError): cleanup.execute(self.root, preview)

    def test_archive_collision_never_overwrites_diagnostic_evidence(self):
        preserved = self.file(self.root / "logs/removed-runs" / self.session / "state.json", b"prior evidence")
        with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, self.session)
        self.assertEqual(preserved.read_bytes(), b"prior evidence"); self.assertTrue(self.folder.is_dir())

    def test_session_identifiers_and_remove_data_are_strict(self):
        for session in ("../outside", "A" * 32, "f" * 64, None):
            with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, session)
        with self.assertRaises(state.WizardError): cleanup.plan_session(self.root, self.session, remove_data="yes")


if __name__ == "__main__": unittest.main()
