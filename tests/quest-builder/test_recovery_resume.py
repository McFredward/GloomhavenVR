"""Audited recipe migration replays real retained exports and merge journals."""
import copy
import argparse
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import recovery_resume
import builder
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import recover
import bundle_recovery as bundles
import full_recovery
import full_assets
from staging_resume import Journal

LEGACY_MERGE_BYTES = {
    "tools/quest-recovery/bundle_recovery.py": 23906,
    "tools/quest-recovery/full_recovery.py": 12857,
    "tools/quest-recovery/native_evidence.py": 3587,
}
PREVIOUS_PROGRESS_BYTES = {
    "tools/quest-recovery/bundle_recovery.py": 27139,
    "tools/quest-recovery/full_recovery.py": 13022,
    "tools/quest-recovery/native_evidence.py": 8634,
}
NESTED_PROGRESS_BYTES = {
    "tools/quest-recovery/bundle_recovery.py": 27522,
    "tools/quest-recovery/full_recovery.py": 13647,
    "tools/quest-recovery/native_evidence.py": 9023,
}
TIMED_RECOVERY_BYTES = {
    "tools/quest-recovery/bundle_recovery.py": 32509,
    "tools/quest-recovery/full_recovery.py": 14972,
    "tools/quest-recovery/native_evidence.py": 14123,
    "tools/quest-recovery/recover.py": 42700,
}
MINIMAL_RECOVERY_BYTES = {
    "tools/quest-recovery/bundle_recovery.py": 31883,
    "tools/quest-recovery/full_recovery.py": 14662,
    "tools/quest-recovery/native_evidence.py": 11384,
}


class ResumeFixture(unittest.TestCase):
    native_byte_fixture = False
    observation_fixture = False

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.output = self.root / "output"
        self.source = self.root / "selected-source"
        originals = self.root / "owned/GH_Data"
        for name in ("Managed/GH.Runtime.dll", "globalgamemanagers", "level0", "resources.assets", "ScriptingAssemblies.json", "original.bundle"):
            path = originals / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"UnityFS\0" + name.encode())
        game_files = storage.inventory(originals)
        self.game_key = storage.value_hash({"files": game_files})
        self.game = self.output / "inputs/game" / self.game_key
        storage.snapshot(originals, game_files, self.game)
        for name in (*recovery_resume.ORCHESTRATION_FILES, "tools/quest-recovery/QuestExportIdentity.cs",
                     "tools/quest-recovery/export_identity.py", "tools/quest-recovery/tool-lock.json",
                     *recovery_resume.DERIVED_FILES,
                     *(recovery_resume.NATIVE_BYTES_PREVIOUS if self.native_byte_fixture else ()),
                     *(recovery_resume.OBSERVATION_PREVIOUS if self.observation_fixture else ())):
            target = self.source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, target)
        # Explicit shipped B627 receipts, independent of the current ROOT's
        # post-export implementation after integration. No mock compatibility
        # predicate and no proprietary game/export cache fixture is involved.
        self.previous = self.inputs(game_files, legacy=True)
        self.old_key = recovery_resume.recipe_key(self.previous, 1)
        self.workspace = self.output / "cache/full-original-recovery" / self.old_key
        self.workspace.mkdir(parents=True)
        files, fingerprint = recover.source_inventory(self.game)
        recover.write_json(self.workspace / "original-source.json", {"schema": 1, "sourceFingerprint": fingerprint, "sourceInventory": files})
        self.core = self.workspace / "CoreExport/ExportedProject"
        base = self.asset(self.core, "a" * 32, "core", 1)
        redirects = self.core / "QuestRecovery/native-redirect-identities.jsonl"
        redirects.parent.mkdir()
        redirects.write_text('{"collection":"core","pathId":1}\n')
        self.core_identity = self.workspace / "core-identities.jsonl"
        self.core_identity.write_text(json.dumps(base) + "\n")
        self.proof = {"sourceRevision": "1ac666f47d8e9dedf96afb0b914c70d7656151ea",
                      "instrumentationSha256": storage.digest(self.source / "tools/quest-recovery/QuestExportIdentity.cs")}
        recover.write_json(self.workspace / "core-recovery.json", {"schema": 1, "sourceFingerprint": fingerprint,
            "exporterSource": self.proof, "identitiesSha256": recover.sha256(self.core_identity), "files": self.records(self.core)})
        bundle = self.game / "original.bundle"
        self.plan = {"catalogSha256": "c" * 64, "groups": [{"bytes": bundle.stat().st_size,
            "bundles": [{"path": bundle.name, "bytes": bundle.stat().st_size, "sha256": recover.sha256(bundle)}]}]}
        self.export_calls = 0
        self.raw = self.workspace / "RecoveredProject"
        self.batches = self.workspace / "BundleRecovery"
        self.exporter = self.root / "AssetRipper.dll"
        self.exporter.write_bytes(b"verified exporter fixture")
        self.tools = [str(self.exporter)]

    def tearDown(self):
        self.temp.cleanup()

    def inputs(self, game_files, *, legacy=False, prior_progress=False, nested_progress=False, minimal_recovery=False, timed_recovery=False):
        files = storage.inventory(self.source)
        if legacy:
            for row in files:
                if row["path"] in recovery_resume.LEGACY_MERGE_FILES:
                    row["sha256"] = recovery_resume.LEGACY_MERGE_FILES[row["path"]]
                    row["size"] = LEGACY_MERGE_BYTES[row["path"]]
        if prior_progress:
            for row in files:
                if row["path"] in recovery_resume.PREVIOUS_PROGRESS_FILES:
                    row["sha256"] = recovery_resume.PREVIOUS_PROGRESS_FILES[row["path"]]
                    row["size"] = PREVIOUS_PROGRESS_BYTES[row["path"]]
        if nested_progress:
            for row in files:
                if row["path"] in recovery_resume.NESTED_PROGRESS_FILES:
                    row["sha256"] = recovery_resume.NESTED_PROGRESS_FILES[row["path"]]
                    row["size"] = NESTED_PROGRESS_BYTES[row["path"]]
        if minimal_recovery:
            for row in files:
                if row["path"] in recovery_resume.MINIMAL_RECOVERY_FILES:
                    row["sha256"] = recovery_resume.MINIMAL_RECOVERY_FILES[row["path"]]
                    row["size"] = MINIMAL_RECOVERY_BYTES[row["path"]]
        if timed_recovery:
            for row in files:
                if row["path"] in recovery_resume.TIMED_RECOVERY_FILES:
                    row["sha256"] = recovery_resume.TIMED_RECOVERY_FILES[row["path"]]
                    row["size"] = TIMED_RECOVERY_BYTES[row["path"]]
        if legacy or prior_progress or nested_progress or minimal_recovery:
            for row in files:
                if row["path"] == recovery_resume.OBSERVER_FILE:
                    row["sha256"] = recovery_resume.PREVIOUS_OBSERVER_SHA256
                    row["size"] = 40027
        value = {"schema": 1, "recipe": 1, "target": "game", "game": {"key": self.game_key, "unityVersion": "2021.3.5f1", "files": game_files},
                 "mod": {"key": storage.value_hash({"files": files}), "files": files}}
        value["inputKey"] = storage.value_hash(value)
        storage.write_json(self.output / "manifests" / (value["inputKey"] + ".json"), value)
        return value

    def updated(self):
        # Identical exporter/config/capture inputs; changed post-export steps.
        for name in (*recovery_resume.LEGACY_MERGE_FILES, *recovery_resume.DERIVED_FILES):
            with (self.source / name).open("a") as stream:
                stream.write("\n# Audited post-export progress/merge fixture update.\n")
        return self.inputs(self.previous["game"]["files"])

    def asset(self, project, guid, collection, path_id):
        path = project / "Assets" / (collection + ".mat")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: original\n")
        path.with_name(path.name + ".meta").write_text("guid: " + guid + "\n")
        return {"guid": guid, "path": str(path), "objects": [{"collection": collection, "pathId": path_id,
                "fileId": 2100000, "classId": 21, "className": "Material"}]}

    def records(self, root):
        return [{"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": recover.sha256(path)}
                for path in sorted(root.rglob("*")) if path.is_file()]

    def export(self, command, stage, target, log, evidence, settings, **kwargs):
        self.export_calls += 1
        project = target / "ExportedProject"
        row = self.asset(project, "b" * 32, "Portrait_Jekserah", 2)
        Path(os.environ["QUEST_EXPORT_IDENTITIES"]).write_text(json.dumps(row) + "\n")
        log.write_text("original export completed\n")
        return project, [], []

    def interrupted_batch(self):
        def interrupt(*args, **kwargs):
            index = args[0] / "QuestRecovery/native-redirect-identities.jsonl"
            index.write_text("interrupted native merge")
            raise KeyboardInterrupt()
        with patch.object(bundles, "catalog_bundle_plan", return_value=self.plan), patch.object(bundles, "repair_managed_plugins"), \
             patch.object(bundles, "run_export", side_effect=self.export), patch.object(bundles.native_evidence, "merge", side_effect=interrupt):
            with self.assertRaises(KeyboardInterrupt):
                bundles.run_recovery(self.game, self.core, self.core_identity, self.raw, self.batches, self.tools)
        self.assertEqual(self.export_calls, 1)
        self.assertTrue((self.batches / "batch-000/export-complete.json").is_file())
        self.assertTrue((self.batches / "merge-pending.json").is_file())

    def finish(self, workspace):
        with patch.object(full_recovery, "build_tool", return_value=(self.tools, {"source": self.proof})), \
             patch.object(full_recovery, "run_export", side_effect=AssertionError("No second core export")), \
             patch.object(full_recovery, "catalog_bundle_plan", return_value=self.plan), \
             patch.object(full_recovery, "serialized_members", return_value=[]), \
             patch.object(bundles, "catalog_bundle_plan", return_value=self.plan), \
             patch.object(bundles, "repair_managed_plugins"), \
             patch.object(bundles, "audit_asset_references", return_value={"unexpectedUnresolvedCount": 0}), \
             patch.object(bundles, "run_export", side_effect=AssertionError("No second bundle export")):
            return full_recovery.prepare(self.game, workspace, self.root / "tools", "dotnet")

    def select(self, current):
        return recovery_resume.select_workspace(self.output, current, self.source, self.game, 1)


class CompatibleMigrationTests(ResumeFixture):
    def _check_shipped_progress_profile(self, *, prior_progress=False, nested_progress=False, minimal_recovery=False, timed_recovery=False):
        old = self.workspace
        self.previous = self.inputs(self.previous["game"]["files"], prior_progress=prior_progress, nested_progress=nested_progress,
                                    minimal_recovery=minimal_recovery, timed_recovery=timed_recovery)
        self.old_key = recovery_resume.recipe_key(self.previous, 1)
        self.workspace = old.with_name(self.old_key)
        old.rename(self.workspace)
        self.core = self.workspace / "CoreExport/ExportedProject"
        self.core_identity = self.workspace / "core-identities.jsonl"
        identity = json.loads(self.core_identity.read_text())
        identity["path"] = str(self.core / "Assets/core.mat")
        self.core_identity.write_text(json.dumps(identity) + "\n")
        receipt = self.workspace / "core-recovery.json"
        core_proof = json.loads(receipt.read_text())
        core_proof["identitiesSha256"] = recover.sha256(self.core_identity)
        recover.write_json(receipt, core_proof)
        self.raw = self.workspace / "RecoveredProject"
        self.batches = self.workspace / "BundleRecovery"
        self.interrupted_batch()
        receipt = self.batches / "batch-000/export-complete.json"
        retained = receipt.read_bytes()
        current = self.inputs(self.previous["game"]["files"])
        self.assertNotEqual(recovery_resume.recipe_key(current, 1), self.old_key)
        self.assertEqual(self.select(current), self.workspace)
        stream = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream), \
             patch.object(recover.build_progress.time, "monotonic", side_effect=iter(range(10000))):
            result = self.finish(self.workspace)
        self.assertTrue(result["fullOriginalCatalogRecovered"])
        self.assertEqual(self.export_calls, 1)
        self.assertEqual(receipt.read_bytes(), retained)
        self.assertFalse((self.batches / "merge-pending.json").exists())
        # Replay actual source-owned producer output through the durable plan;
        # counts come from real retained files, collection merge and checkpoint
        # writes, not a hand-maintained synthetic list of matching percentages.
        spec = importlib.util.spec_from_file_location("resume_scoped_stage_plan", ROOT / "tools/quest-wizard/stage_plan.py")
        plan = importlib.util.module_from_spec(spec); spec.loader.exec_module(plan)
        row = {"id": "build", "status": "running"}
        observed, sections = [], []
        for line in stream.getvalue().splitlines():
            if not line.startswith(recover.build_progress.PREFIX): continue
            event = json.loads(line[len(recover.build_progress.PREFIX):])
            done, total = event["done"], event["total"]
            event["percent"] = None if done is None or total is None else (100 if total == 0 else 100 * done / total)
            value = plan.advance(row, event, event.get("operation"), event.get("status"))
            observed.append(value["stagePercent"])
            if event["phase"].startswith("recovery-section:"): sections.append(event["phase"].split(":", 1)[1])
        self.assertEqual(sections, list(plan.RECOVERY_SECTIONS[:-1]))
        self.assertEqual(observed, sorted(observed))
        self.assertGreater(len(set(observed)), 20)
        self.assertLess(max(observed), 100)

    def test_preceding_builder_progress_sources_resume_real_export_and_journal(self):
        self._check_shipped_progress_profile(prior_progress=True)

    def test_nested_progress_builder_sources_resume_real_export_and_journal(self):
        self._check_shipped_progress_profile(nested_progress=True)

    def test_minimal_recovery_shipped_builder_resumes_real_export_and_journal(self):
        self._check_shipped_progress_profile(minimal_recovery=True)

    def test_shipped_migration_keeps_the_complete_real_recovery_source_keyset(self):
        for row in storage.inventory(ROOT / "tools/quest-recovery"):
            original = ROOT / "tools/quest-recovery" / row["path"]
            target = self.source / "tools/quest-recovery" / row["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
        self._check_shipped_progress_profile(minimal_recovery=True)

    def test_timed_builder_update_retains_completed_export_and_reference_audit(self):
        self._check_shipped_progress_profile(timed_recovery=True)
        self.assertEqual(self.export_calls, 1)
        with patch.object(bundles, 'audit_asset_references', side_effect=AssertionError('Audit must stay retained')):
            result = self.finish(self.workspace)
        self.assertTrue(result['fullOriginalCatalogRecovered'])
        self.assertEqual(self.export_calls, 1)

    def test_minimal_recovery_migration_still_rejects_corrupted_retained_game_bytes(self):
        self._check_shipped_progress_profile(minimal_recovery=True)
        progress = json.loads((self.raw / "quest-full-recovery-progress.json").read_text())
        relative = next(row["path"] for row in progress["files"] if row["path"].startswith("Assets/")
                        and row["path"].endswith(".mat"))
        path = self.raw / relative
        path.write_bytes(b"changed retained original fields")
        with self.assertRaisesRegex(recover.RecoveryError, "Full recovered checkpoint file changed"):
            self.finish(self.workspace)
        self.assertTrue(path.is_file())
        self.assertEqual(self.export_calls, 1)

    def test_unknown_log_observer_cannot_claim_a_shipped_whole_source_profile(self):
        previous = copy.deepcopy(self.previous)
        for row in previous["mod"]["files"]:
            if row["path"] == recovery_resume.OBSERVER_FILE: row["sha256"] = "f" * 64
        current = self.inputs(self.previous["game"]["files"])
        self.assertFalse(recovery_resume._compatible(previous, current))

    def test_mixed_reviewed_source_profiles_do_not_claim_a_whole_shipped_recipe(self):
        previous = copy.deepcopy(self.previous)
        for row in previous["mod"]["files"]:
            if row["path"] == "tools/quest-recovery/native_evidence.py":
                row["sha256"] = recovery_resume.PREVIOUS_PROGRESS_FILES[row["path"]]
        current = self.inputs(self.previous["game"]["files"])
        self.assertFalse(recovery_resume._compatible(previous, current))

    def test_large_core_inventory_migrates_and_bound_retry_does_not_hash_assets(self):
        receipt = self.workspace / "core-recovery.json"
        value = json.loads(receipt.read_text())
        # A full export contains an entry per asset/.meta, unlike its small
        # source/binding metadata. This real JSON inventory exceeds the old cap;
        # eligibility validates the records without claiming their bytes are
        # verified (the actual child remains responsible for that hash gate).
        value["files"].extend({"path": "Assets/Textures/fixture-character-" + str(number).zfill(6) + ".png",
                              "bytes": 0, "sha256": "e" * 64} for number in range(130000))
        storage.write_json(receipt, value)
        self.assertGreater(receipt.stat().st_size, recovery_resume.MAX_JSON_BYTES)
        self.assertLess(receipt.stat().st_size, recovery_resume.MAX_CORE_RECEIPT_BYTES)
        current = self.updated()
        with patch.object(recovery_resume, "digest", wraps=storage.digest) as hashed:
            self.assertEqual(self.select(current), self.workspace)
            self.assertEqual(self.select(current), self.workspace)
        self.assertTrue(hashed.call_count)
        self.assertFalse(any(self.core in call.args[0].parents for call in hashed.call_args_list))
        self.assertEqual(receipt.stat().st_size, len(storage.canonical(value)) + 1)
        with self.assertRaisesRegex(recover.RecoveryError, "Core recovery output changed"):
            self.finish(self.workspace)
        self.assertEqual(self.export_calls, 0)

    def test_large_valid_receipt_reaches_real_child_and_replays_without_export(self):
        self.interrupted_batch()
        receipt = self.workspace / "core-recovery.json"
        # JSON whitespace does not alter the actual already-exported file
        # inventory. Use it to exercise >16 MiB through real child verification
        # and journal replay without a huge proprietary/filesystem fixture.
        with receipt.open("ab") as stream:
            stream.write(b" " * recovery_resume.MAX_JSON_BYTES)
        self.assertGreater(receipt.stat().st_size, recovery_resume.MAX_JSON_BYTES)
        result = self.finish(self.select(self.updated()))
        self.assertTrue(result["fullOriginalCatalogRecovered"])
        self.assertEqual(self.export_calls, 1)
        self.assertFalse((self.batches / "merge-pending.json").exists())

    def test_real_completed_export_and_journal_resume_under_new_derived_key(self):
        self.interrupted_batch()
        export_receipt = self.batches / "batch-000/export-complete.json"
        batch_bytes = export_receipt.read_bytes()
        current = self.updated()
        new_key = recovery_resume.recipe_key(current, 1)
        self.assertNotEqual(new_key, self.old_key)
        selected = self.select(current)
        self.assertEqual(selected, self.workspace)
        self.assertEqual(export_receipt.read_bytes(), batch_bytes)
        result = self.finish(selected)
        self.assertTrue(result["fullOriginalCatalogRecovered"])
        progress = json.loads((self.raw / "quest-full-recovery-progress.json").read_text())
        self.assertEqual(progress["completedGroups"], [0])
        self.assertFalse((self.batches / "merge-pending.json").exists())
        self.assertEqual(self.export_calls, 1)
        self.assertFalse((self.output / "cache/recovery" / new_key).exists())
        # An ordinary retry uses the durable binding, even with too many other
        # manifests to permit initial discovery; no old derived stage is copied.
        self.assertEqual(self.select(current), self.workspace)

    def test_actual_builder_reuses_raw_exports_but_stages_new_derived_assets(self):
        self.interrupted_batch()
        current = self.updated()
        new_key = recovery_resume.recipe_key(current, 1)
        old_derived = self.output / "cache/recovery" / self.old_key / "project/Assets/old-derived.asset"
        old_derived.parent.mkdir(parents=True)
        old_derived.write_bytes(b"old Android conversion must remain untouched")
        storage.Stages(self.output).run("recovery", self.old_key, lambda: ([old_derived], {"project": "old-derived"}))
        commands = []
        def command(argv, log):
            workspace = Path(argv[argv.index("--workspace") + 1])
            commands.append(workspace)
            self.finish(workspace)
            return "actual retained fixture recovery completed"
        args = argparse.Namespace(target="game", dotnet=str(self.exporter), recovery_dotnet=None)
        with patch("dependencies.python_environment", return_value=Path(sys.executable)), \
             patch("dependencies.dotnet10", return_value=self.exporter), \
             patch.object(builder, "command", side_effect=command), \
             patch.object(builder, "owned_tmp_source_archive", return_value=self.root / "public-tmp-source.zip"), \
             patch.object(builder.full_assets, "stage", side_effect=RuntimeError("Reached new derived stage")) as derive:
            with self.assertRaisesRegex(RuntimeError, "Reached new derived stage"):
                builder.prepare(args, current, self.output, self.source, self.game)
        self.assertEqual(commands, [self.workspace])
        self.assertEqual(derive.call_args.args[0], self.raw)
        self.assertEqual(derive.call_args.args[2], self.output / "cache/recovery" / new_key / "project")
        self.assertEqual(old_derived.read_bytes(), b"old Android conversion must remain untouched")
        self.assertEqual(self.export_calls, 1)

    def test_bound_resume_keeps_an_unrelated_new_recipe_workspace(self):
        current = self.updated()
        self.assertEqual(self.select(current), self.workspace)
        new = self.output / "cache/full-original-recovery" / recovery_resume.recipe_key(current, 1)
        new.mkdir()
        (new / "unrelated-kept-file").write_bytes(b"do not erase another interrupted attempt")
        self.assertEqual(self.select(current), self.workspace)
        self.assertEqual((new / "unrelated-kept-file").read_bytes(), b"do not erase another interrupted attempt")

    def test_published_observer_binding_keeps_its_older_raw_workspace_and_receipt(self):
        current = self.inputs(self.previous["game"]["files"])
        published = copy.deepcopy(current)
        for index, row in enumerate(published["mod"]["files"]):
            if row["path"] == recovery_resume.OBSERVER_FILE:
                published["mod"]["files"][index] = dict(recovery_resume.CRLF_OBSERVER_PREVIOUS)
        key = recovery_resume.recipe_key(published, 1)
        self.assertEqual(recovery_resume.recipe_key(current, 1), key)
        records = recovery_resume._records(recovery_resume.recipe_files(current), "size")
        contract = storage.value_hash({"schema": 1, "files": [row for name, row in sorted(records.items())
            if name not in recovery_resume.ORCHESTRATION_FILES and name not in recovery_resume.DERIVED_FILES]})
        proof = recovery_resume._qualification(self.workspace, current, self.game)
        marker = self.output / "cache/raw-recovery-resume" / (key + ".json")
        storage.write_json(marker, {"schema": 1, "owner": "Quest raw recovery resume", "currentRecipeKey": key,
            "originalWorkspaceKey": self.old_key, "originalManifestKey": self.previous["inputKey"],
            "gameKey": current["game"]["key"], "exportContract": contract, **proof})
        retained = marker.read_bytes()
        self.assertEqual(self.select(current), self.workspace)
        self.assertEqual(marker.read_bytes(), retained)
        self.assertFalse((self.output / "cache/full-original-recovery" / key).exists())

    def test_migration_discovery_is_bounded_and_bound_retry_does_not_scan(self):
        current = self.updated()
        self.assertEqual(self.select(current), self.workspace)
        manifests = self.output / "manifests"
        for number in range(recovery_resume.MAX_MANIFESTS):
            (manifests / ("unrelated-" + str(number))).write_bytes(b"unrelated kept manifest entry")
        self.assertEqual(self.select(current), self.workspace)
        binding = self.output / "cache/raw-recovery-resume" / (recovery_resume.recipe_key(current, 1) + ".json")
        binding.unlink()
        with self.assertRaisesRegex(storage.BuildError, "bounded recovery migration"): self.select(current)
        self.assertTrue((self.workspace / "core-recovery.json").exists())

    def test_unknown_merge_recipe_is_not_adopted(self):
        # An arbitrary earlier recipe cannot claim this audited migration.
        for name in recovery_resume.LEGACY_MERGE_FILES:
            with (self.source / name).open("a") as stream: stream.write("\n# Unreviewed older exporter orchestration.\n")
        foreign = self.inputs(self.previous["game"]["files"])
        foreign_key = recovery_resume.recipe_key(foreign, 1)
        foreign_workspace = self.output / "cache/full-original-recovery" / foreign_key
        self.workspace.rename(foreign_workspace)
        current = self.updated()
        selected = self.select(current)
        self.assertNotEqual(selected, foreign_workspace)
        self.assertFalse(selected.exists())
        self.assertTrue((foreign_workspace / "core-recovery.json").exists())

    def test_new_instrumentation_or_settings_do_not_adopt_old_exports(self):
        for name in ("QuestExportIdentity.cs", "tool-lock.json", "export_identity.py"):
            with self.subTest(name=name):
                target = self.source / "tools/quest-recovery" / name
                original = target.read_bytes()
                target.write_bytes(original + b"\nchanged raw export contract\n")
                current = self.inputs(self.previous["game"]["files"])
                selected = self.select(current)
                self.assertNotEqual(selected, self.workspace)
                self.assertFalse(selected.exists())
                target.write_bytes(original)

    def test_another_game_inventory_cannot_adopt_the_original_exports(self):
        current = self.updated()
        current["game"]["files"] = copy.deepcopy(current["game"]["files"])
        current["game"]["files"][0]["sha256"] = "f" * 64
        current["game"]["key"] = storage.value_hash({"files": current["game"]["files"]})
        selected = self.select(current)
        self.assertNotEqual(selected, self.workspace)
        self.assertTrue((self.workspace / "core-recovery.json").exists())


class ObserverStageContinuationTests(ResumeFixture):
    """Retry the actual Builder/Journal seam after six committed transformations.

    Exporter receipts contain 16 small owned packages, not game assets. The
    staging consumer uses actual copy proofs, filesystem writers and SQLite
    rollback while stopping at native audit; it does not claim a Unity build.
    """
    def setUp(self):
        super().setUp()
        for index in range(15):
            (self.game / ("owned-" + str(index) + ".bundle")).write_bytes(("original package " + str(index)).encode())
        catalog = self.game / "StreamingAssets/aa/catalog.json"
        catalog.parent.mkdir(parents=True)
        catalog.write_bytes(b'{"owned":true}')
        files = storage.inventory(self.game, paths=[path.relative_to(self.game).as_posix()
                                  for path in self.game.rglob("*") if path.is_file() and path.name != ".snapshot.json"])
        self.game_key = storage.value_hash({"files": files})
        game = self.game.with_name(self.game_key)
        self.game.rename(game); self.game = game
        storage.write_json(self.game / ".snapshot.json", {"schema": 1, "files": files})
        # Every current recipe row is real checked-in source; only the known
        # preceding observer row is supplied by its published exact receipt.
        self.current = self.inputs(files)
        self.previous = copy.deepcopy(self.current)
        for index, row in enumerate(self.previous["mod"]["files"]):
            if row["path"] == recovery_resume.OBSERVER_FILE:
                self.previous["mod"]["files"][index] = dict(recovery_resume.CRLF_OBSERVER_PREVIOUS)
        self.previous["mod"]["key"] = storage.value_hash({"files": self.previous["mod"]["files"]})
        self.previous["inputKey"] = storage.value_hash({name: row for name, row in self.previous.items() if name != "inputKey"})
        storage.write_json(self.output / "manifests" / (self.previous["inputKey"] + ".json"), self.previous)
        self.old_key = recovery_resume.recipe_key(self.previous, 1)
        old = self.workspace
        self.workspace = old.with_name(self.old_key); old.rename(self.workspace)
        self.raw = self.workspace / "RecoveredProject"
        self.core = self.workspace / "CoreExport/ExportedProject"
        self.core_identity = self.workspace / "core-identities.jsonl"
        identity = json.loads(self.core_identity.read_text()); identity["path"] = str(self.core / "Assets/core.mat")
        self.core_identity.write_text(json.dumps(identity) + "\n")
        source, fingerprint = recover.source_inventory(self.game)
        storage.write_json(self.workspace / "original-source.json", {
            "schema": 1, "sourceInventory": source, "sourceFingerprint": fingerprint})
        core = json.loads((self.workspace / "core-recovery.json").read_text())
        core.update(sourceFingerprint=fingerprint, identitiesSha256=storage.digest(self.core_identity))
        storage.write_json(self.workspace / "core-recovery.json", core)
        self.asset(self.raw, "b" * 32, "native", 2)
        self.prefab = self.raw / "Assets/native.mat"
        self.prefab.write_bytes(("%YAML 1.1\r\n%TAG !u! tag:unity3d.com,2011:\r\n--- !u!21 &2100000\r\n"
                                 "Material:\r\n  m_Name: original\r\n  target: {fileID: 2100000, guid: "
                                 + "c" * 32 + ", type: 2}\r\n").encode())
        self.managed = self.raw / "QuestRecovery/managed-types.json"
        storage.write_json(self.managed, {"originalTypes": []})
        originals = {row["path"]: row for row in source}
        packages = [originals[name] for name in sorted(originals) if name.endswith(".bundle")]
        self.plan = {"catalogSha256": storage.digest(self.game / "StreamingAssets/aa/catalog.json"),
                     "groups": [{"bytes": row["bytes"], "bundles": [row]} for row in packages]}
        storage.write_json(self.workspace / "BundleRecovery/bundle-plan.json", self.plan)
        owners = self.workspace / "original-cab-bundles.json"
        storage.write_json(owners, {"CAB-" + str(index): row["path"] for index, row in enumerate(packages)})
        self.checkpoint = {"schema": 1, "sourceInventory": source, "sourceFingerprint": fingerprint,
            "coreIdentitySha256": storage.digest(self.core_identity), "catalogSha256": self.plan["catalogSha256"],
            "completedGroups": list(range(16)), "groups": [{"index": index, "input": row} for index, row in enumerate(self.plan["groups"])],
            "assetsRecovered": True, "assetReferences": {"missingGuidCount": 0, "duplicateGuidCount": 0},
            "files": self.records(self.raw)}
        storage.write_json(self.raw / "quest-full-recovery-progress.json", self.checkpoint)
        storage.write_json(self.workspace / "full-recovery.json", {
            "schema": 1, "fullOriginalCatalogRecovered": True, "sourceFingerprint": fingerprint,
            "recoveryProject": str(self.raw), "coreProject": str(self.core),
            "managedTypes": str(self.managed), "cabBundles": str(owners)})
        self.recovered = self.output / "cache/recovery" / self.old_key / "project"
        self.owner = {"schema": 1, "owner": "Quest recovered Campaign stage", "key": self.old_key, "gameKey": self.game_key}
        storage.write_json(self.recovered.parent / "stage-owner.json", self.owner)
        self.phase_calls = []

    def consumer(self, raw, game, recovered, archive, *, resume_owner, **kwargs):
        identity = {"source": str(raw), "game": str(game), "output": str(recovered),
                    "checkpointSha256": storage.digest(raw / "quest-full-recovery-progress.json"),
                    "catalogSha256": self.plan["catalogSha256"], "managedTypesSha256": storage.digest(self.managed),
                    "cabBundlesSha256": storage.digest(self.workspace / "original-cab-bundles.json"),
                    "tmpArchiveSha256": None, "canonicalProject": None, "canonicalStartup": None, "resumeOwner": resume_owner}
        with Journal(recovered, identity, full_assets._StageProofs(), resume_owner=resume_owner) as journal:
            def action(name):
                self.phase_calls.append(name)
                if name == "copy":
                    for row in self.checkpoint["files"]:
                        full_assets._resume_copy(raw / row["path"], recovered / row["path"], row, journal.proofs, journal)
                elif name == "runtime":
                    full_assets._runtime_file(self.managed, recovered / "QuestRecovery/runtime-types.json", journal.proofs)
                    journal.accept()
                elif name == "guid":
                    (recovered / "Assets/native.mat.meta").write_text("guid: " + "c" * 32 + "\n")
                elif name == "layout":
                    path = recovered / "Assets/native.mat"
                    path.write_bytes(path.read_bytes().replace(b"m_Name: original", b"m_Name: restored-layout"))
                return {"context": name, "ownedPackages": 16}
            for position, name in enumerate(("catalog", "canonical", "copy", "runtime", "guid", "layout")):
                journal.run(name, position, lambda name=name: action(name), copy=name == "copy")
            def native():
                self.phase_calls.append("native")
                (recovered / "Assets/native-pending.asset").write_bytes(b"unfinished native writer")
                # Exercise the corrected CRLF parser at the actual retry seam.
                tokens = list(recover.serialized_pointer_tokens((recovered / "Assets/native.mat").read_bytes().decode()))
                self.assertEqual([row[0] for row in tokens], ["c" * 32])
                raise RuntimeError("Native audit reached; no Player build claimed")
            journal.run("native", 6, native, auxiliary=recovered.with_name(recovered.name + "-native-restoration"))

    def seed_interrupted_native(self):
        with self.assertRaisesRegex(RuntimeError, "Native audit reached"):
            self.consumer(self.raw, self.game, self.recovered, self.root / "official-tmp.zip", resume_owner=self.owner)
        self.assertEqual(self.phase_calls, ["catalog", "canonical", "copy", "runtime", "guid", "layout", "native"])
        self.phase_calls.clear()

    def retry(self, current=None):
        args = builder.parser().parse_args(["prepare", "--target", "game", "--dotnet", sys.executable])
        with patch("dependencies.python_environment", return_value=Path(sys.executable)), \
             patch.object(builder, "command", side_effect=AssertionError("Completed raw exports must not relaunch")), \
             patch.object(builder, "owned_tmp_source_archive", return_value=self.root / "official-tmp.zip"), \
             patch.object(builder.full_assets, "stage", side_effect=self.consumer):
            return builder.prepare(args, current or self.current, self.output, self.source, self.game)

    def test_exact_parser_repair_and_current_mod_keep_six_closed_phases_and_all_16_raw_packages(self):
        actual = next(row for row in self.current["mod"]["files"] if row["path"] == recovery_resume.OBSERVER_FILE)
        self.assertEqual(actual, recovery_resume.CRLF_OBSERVER_FIXED)
        self.seed_interrupted_native()
        path = self.recovered / "Assets/native.mat"
        before = path.stat(); content = path.read_bytes()
        # Normal mod development and a changed Git commit do not invalidate the
        # original asset transformations; they still key later mod compilation.
        changed = self.source / "src/GloomhavenVR/CurrentMod.cs"
        changed.parent.mkdir(parents=True); changed.write_text("// Current dev 642 mod source.\n")
        current = self.inputs(self.current["game"]["files"])
        self.assertNotEqual(current["inputKey"], self.previous["inputKey"])
        self.assertEqual(recovery_resume.recipe_key(current, 1), self.old_key)
        stream = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream):
            with self.assertRaisesRegex(RuntimeError, "Native audit reached"):
                self.retry(current)
        self.assertEqual(self.phase_calls, ["native"])
        self.assertEqual(path.read_bytes(), content)
        self.assertEqual((path.stat().st_ino, path.stat().st_mtime_ns), (before.st_ino, before.st_mtime_ns))
        self.assertFalse((self.recovered / "Assets/native-pending.asset").exists())
        self.assertEqual(recovery_resume.completed_raw(self.workspace, current, self.game)["_completedRaw"]["packages"], 16)
        events = [json.loads(line[len(recover.build_progress.PREFIX):]) for line in stream.getvalue().splitlines()
                  if line.startswith(recover.build_progress.PREFIX)]
        reused = [row["phase"].split(":", 1)[1] for row in events
                  if row["phase"].startswith("staging-section:") and row["status"] == "reuse"]
        self.assertEqual(reused, ["catalog", "canonical", "copy", "runtime", "guid", "layout"])
        self.assertTrue(any(row["phase"] == "recovery-batches" and row["done"] == row["total"] == 16 for row in events))

    def test_unknown_parser_or_transformation_source_never_claims_the_alias(self):
        self.assertTrue(recovery_resume._compatible(self.previous, self.current))
        for name in (recovery_resume.OBSERVER_FILE, "tools/quest-recovery/QuestExportIdentity.cs",
                     "tools/quest-builder/full_assets.py", "tools/quest-builder/staging_resume.py"):
            with self.subTest(name=name):
                current = copy.deepcopy(self.current)
                next(row for row in current["mod"]["files"] if row["path"] == name)["sha256"] = "f" * 64
                self.assertNotEqual(recovery_resume.recipe_key(current, 1), self.old_key)
                if name in (recovery_resume.OBSERVER_FILE, "tools/quest-recovery/QuestExportIdentity.cs"):
                    self.assertFalse(recovery_resume._compatible(self.previous, current))
        current = copy.deepcopy(self.current)
        next(row for row in current["mod"]["files"] if row["path"] == recovery_resume.OBSERVER_FILE)["size"] += 1
        self.assertNotEqual(recovery_resume.recipe_key(current, 1), self.old_key)
        self.assertFalse(recovery_resume._compatible(self.previous, current))
        self.assertNotEqual(recovery_resume.recipe_key(self.current, 2), self.old_key)
        current["game"]["key"] = "f" * 64
        self.assertNotEqual(recovery_resume.recipe_key(current, 1), self.old_key)

    def test_existing_raw_binding_from_same_observer_and_older_derived_recipe_keeps_the_six_phases(self):
        prior = copy.deepcopy(self.previous)
        next(row for row in prior["mod"]["files"] if row["path"] == "tools/quest-builder/full_assets.py")["sha256"] = "f" * 64
        prior["mod"]["key"] = storage.value_hash({"files": prior["mod"]["files"]})
        prior["inputKey"] = storage.value_hash({name: row for name, row in prior.items() if name != "inputKey"})
        storage.write_json(self.output / "manifests" / (prior["inputKey"] + ".json"), prior)
        prior_key = recovery_resume.recipe_key(prior, 1)
        workspace = self.workspace.with_name(prior_key)
        self.workspace.rename(workspace); self.workspace = workspace
        self.raw = workspace / "RecoveredProject"; self.core = workspace / "CoreExport/ExportedProject"
        self.managed = self.raw / "QuestRecovery/managed-types.json"
        self.core_identity = workspace / "core-identities.jsonl"
        row = json.loads(self.core_identity.read_text()); row["path"] = str(self.core / "Assets/core.mat")
        self.core_identity.write_text(json.dumps(row) + "\n")
        core = json.loads((workspace / "core-recovery.json").read_text())
        core["identitiesSha256"] = storage.digest(self.core_identity)
        storage.write_json(workspace / "core-recovery.json", core)
        self.checkpoint["coreIdentitySha256"] = core["identitiesSha256"]
        storage.write_json(self.raw / "quest-full-recovery-progress.json", self.checkpoint)
        result = json.loads((workspace / "full-recovery.json").read_text())
        result.update(recoveryProject=str(self.raw), coreProject=str(self.core), managedTypes=str(self.managed),
                      cabBundles=str(workspace / "original-cab-bundles.json"))
        storage.write_json(workspace / "full-recovery.json", result)
        # Reproduce the shipped binding's historical consumer rows, not the
        # new source's canonicalization implementation.
        records = recovery_resume._records(recovery_resume.recipe_files(self.previous), "size")
        contract = storage.value_hash({"schema": 1, "files": [row for name, row in sorted(records.items())
            if name not in recovery_resume.ORCHESTRATION_FILES and name not in recovery_resume.DERIVED_FILES]})
        proof = recovery_resume._qualification(workspace, self.current, self.game)
        marker = self.output / "cache/raw-recovery-resume" / (self.old_key + ".json")
        storage.write_json(marker, {"schema": 1, "owner": "Quest raw recovery resume", "currentRecipeKey": self.old_key,
            "originalWorkspaceKey": prior_key, "originalManifestKey": prior["inputKey"],
            "gameKey": self.game_key, "exportContract": contract, **proof})
        retained = marker.read_bytes()
        self.seed_interrupted_native()
        with self.assertRaisesRegex(RuntimeError, "Native audit reached"):
            self.retry()
        self.assertEqual(self.phase_calls, ["native"])
        self.assertEqual(marker.read_bytes(), retained)
        self.assertFalse((self.output / "cache/full-original-recovery" / self.old_key).exists())

    def test_retained_guid_layout_bytes_are_still_qualified_on_parser_retry(self):
        self.seed_interrupted_native()
        path = self.recovered / "Assets/native.mat"
        path.write_bytes(b"corrupt prior layout output")
        with self.assertRaisesRegex(storage.BuildError, "Retained staged bytes differ"):
            self.retry()
        self.assertEqual(self.phase_calls, [])
        self.assertEqual(path.read_bytes(), b"corrupt prior layout output")

    def test_alias_does_not_substitute_old_observer_bytes_during_source_qualification(self):
        self.seed_interrupted_native()
        observer = self.source / recovery_resume.OBSERVER_FILE
        observer.write_bytes(observer.read_bytes() + b"\n# Unwitnessed source edit.\n")
        with self.assertRaisesRegex(storage.BuildError, "Current recovery source differs from its immutable manifest"):
            self.retry()
        self.assertEqual(self.phase_calls, [])

    def test_missing_or_changed_owner_does_not_restart_completed_transforms(self):
        self.seed_interrupted_native()
        marker = self.recovered.with_name(self.recovered.name + ".staging-resume") / "owner.json"
        original = marker.read_bytes()
        marker.unlink()
        with self.assertRaisesRegex(storage.BuildError, "owner is missing or corrupt"):
            self.retry()
        self.assertEqual(self.phase_calls, [])
        value = json.loads(original); value["identity"]["resumeOwner"]["gameKey"] = "f" * 64
        marker.write_text(json.dumps(value))
        with self.assertRaisesRegex(storage.BuildError, "inputs/owner differ"):
            self.retry()
        self.assertEqual(self.phase_calls, [])


class NativeByteStageContinuationTests(ObserverStageContinuationTests):
    """Retain the bound raw/derived owners across the exact Windows I/O repair."""
    native_byte_fixture = True

    def setUp(self):
        super().setUp()
        for index, row in enumerate(self.previous["mod"]["files"]):
            if row["path"] in recovery_resume.NATIVE_BYTES_PREVIOUS:
                self.previous["mod"]["files"][index] = dict(recovery_resume.NATIVE_BYTES_PREVIOUS[row["path"]])
        self.previous["mod"]["key"] = storage.value_hash({"files": self.previous["mod"]["files"]})
        self.previous["inputKey"] = storage.value_hash({name: row for name, row in self.previous.items() if name != "inputKey"})
        storage.write_json(self.output / "manifests" / (self.previous["inputKey"] + ".json"), self.previous)
        self.assertEqual(recovery_resume.recipe_key(self.previous, 1), self.old_key)

    def test_actual_fixed_native_sources_match_reviewed_complete_profile(self):
        actual = {row["path"]: row for row in self.current["mod"]["files"]
                  if row["path"] in recovery_resume.NATIVE_BYTES_PREVIOUS}
        self.assertEqual(actual, recovery_resume.NATIVE_BYTES_FIXED)
        self.assertTrue(recovery_resume._compatible(self.previous, self.current))
        self.assertFalse(set(actual) & recovery_resume.DERIVED_FILES)
        # The raw contract retains both consumers; deleting them would silently
        # break a captured binding even when its original/raw inputs are intact.
        records = recovery_resume._records(recovery_resume.recipe_files(self.previous), "size")
        self.assertTrue(set(actual) <= records.keys())

    def test_partial_or_unknown_native_pair_never_claims_closed_transformations(self):
        for name in recovery_resume.NATIVE_BYTES_PREVIOUS:
            for change in ("sha256", "size", "partial"):
                with self.subTest(name=name, change=change):
                    current = copy.deepcopy(self.current)
                    row = next(row for row in current["mod"]["files"] if row["path"] == name)
                    if change == "sha256": row["sha256"] = "f" * 64
                    elif change == "size": row["size"] += 1
                    else: row.update(recovery_resume.NATIVE_BYTES_PREVIOUS[name])
                    self.assertNotEqual(recovery_resume.recipe_key(current, 1), self.old_key)
                    self.assertFalse(recovery_resume._compatible(self.previous, current))

    def test_native_alias_still_qualifies_actual_fixed_source_bytes(self):
        self.seed_interrupted_native()
        for name in recovery_resume.NATIVE_BYTES_PREVIOUS:
            with self.subTest(name=name):
                path = self.source / name
                original = path.read_bytes()
                path.write_bytes(original + b"\n# Unknown native writer edit.\n")
                try:
                    with self.assertRaisesRegex(storage.BuildError, "Current recovery source differs from its immutable manifest"):
                        self.retry()
                    self.assertEqual(self.phase_calls, [])
                finally:
                    path.write_bytes(original)

    def test_closed_native_receipt_stays_byte_qualified_across_consumer_repair(self):
        self.seed_interrupted_native()
        # A completed native operation is acceptable only with its existing
        # file witness. The recipe alias is not an instruction to rerun writers.
        identity = {"source": str(self.raw), "game": str(self.game), "output": str(self.recovered),
                    "checkpointSha256": storage.digest(self.raw / "quest-full-recovery-progress.json"),
                    "catalogSha256": self.plan["catalogSha256"], "managedTypesSha256": storage.digest(self.managed),
                    "cabBundlesSha256": storage.digest(self.workspace / "original-cab-bundles.json"),
                    "tmpArchiveSha256": None, "canonicalProject": None, "canonicalStartup": None, "resumeOwner": self.owner}
        path = self.recovered / "Assets/native.mat"
        def complete_native():
            path.write_bytes(path.read_bytes().replace(b"restored-layout", b"restored-native"))
            return {"restored": "original-native-fields"}
        with Journal(self.recovered, identity, full_assets._StageProofs(), resume_owner=self.owner) as journal:
            result = journal.run("native", 6, complete_native,
                                 auxiliary=self.recovered.with_name(self.recovered.name + "-native-restoration"))
        self.assertEqual(result, {"restored": "original-native-fields"})
        with Journal(self.recovered, identity, full_assets._StageProofs(), resume_owner=self.owner) as journal:
            result = journal.run("native", 6, lambda: self.fail("Closed native writer must remain reused"),
                                 auxiliary=self.recovered.with_name(self.recovered.name + "-native-restoration"))
        self.assertEqual(result, {"restored": "original-native-fields"})
        path.write_bytes(b"corrupt completed native bytes")
        with self.assertRaisesRegex(storage.BuildError, "Retained staged bytes differ"):
            with Journal(self.recovered, identity, full_assets._StageProofs(), resume_owner=self.owner):
                self.fail("Corrupt native file must never be reused")


class ObservationRecipeContinuationTests(unittest.TestCase):
    """Replay fourteen real Journal transactions without any native/Unity claim."""
    PHASES = ("catalog", "canonical", "copy", "runtime", "guid", "layout", "native",
              "catalog-final", "index", "tmp", "bindings", "audit", "scenes", "report")

    def setUp(self):
        fixture_type = type("OwnedObservationFixture", (ObserverStageContinuationTests,),
                            {"observation_fixture": True})
        self.fixture = fixture_type("test_exact_parser_repair_and_current_mod_keep_six_closed_phases_and_all_16_raw_packages")
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        f = self.fixture
        actual = {row["path"]: row for row in f.current["mod"]["files"] if row["path"] in recovery_resume.OBSERVATION_FIXED}
        self.assertEqual(actual, recovery_resume.OBSERVATION_FIXED)
        self.assertTrue(actual)
        for index, row in enumerate(f.previous["mod"]["files"]):
            if row["path"] in recovery_resume.OBSERVATION_PREVIOUS:
                f.previous["mod"]["files"][index] = dict(recovery_resume.OBSERVATION_PREVIOUS[row["path"]])
        self.publish(f.previous)
        self.assertEqual(recovery_resume.recipe_key(f.previous, 1), f.old_key)
        f.consumer = self.consumer
        self.actions = []

    def publish(self, value):
        value["mod"]["key"] = storage.value_hash({"files": value["mod"]["files"]})
        value["inputKey"] = storage.value_hash({key: row for key, row in value.items() if key != "inputKey"})
        storage.write_json(self.fixture.output / "manifests" / (value["inputKey"] + ".json"), value)

    def consumer(self, raw, game, recovered, archive, *, resume_owner, **kwargs):
        f = self.fixture
        identity = {"source": str(raw), "game": str(game), "output": str(recovered),
                    "checkpointSha256": storage.digest(raw / "quest-full-recovery-progress.json"),
                    "catalogSha256": f.plan["catalogSha256"], "managedTypesSha256": storage.digest(f.managed),
                    "cabBundlesSha256": storage.digest(f.workspace / "original-cab-bundles.json"),
                    "tmpArchiveSha256": None, "canonicalProject": None, "canonicalStartup": None, "resumeOwner": resume_owner}
        report = {"fixtureOnly": True, "readiness": {"originalSceneClosureStaged": True,
                  "fullOriginalCatalogRecovered": True, "unityImportVerified": False, "androidPlayerBuilt": False},
                  "missingReferences": {"missingGuidCount": 0, "duplicateGuidCount": 0},
                  "unresolvedAddressables": [], "managedScriptBindings": {"unexpectedUnresolvedCount": 0}}
        with Journal(recovered, identity, full_assets._StageProofs(), resume_owner=resume_owner) as journal:
            for position, name in enumerate(self.PHASES):
                def action(name=name):
                    self.actions.append(name)
                    if name == "copy":
                        for row in f.checkpoint["files"]:
                            full_assets._resume_copy(raw / row["path"], recovered / row["path"], row, journal.proofs, journal)
                    elif name == "native":
                        path = recovered / "Assets/native.mat"
                        path.write_bytes(path.read_bytes().replace(b"m_Name: original", b"m_Name: restored-native"))
                    receipt = recovered / "QuestRecovery" / ("fixture-stage-" + name + ".json")
                    storage.write_json(receipt, {"schema": 1, "phase": name, "fixtureOnly": True})
                    if name == "copy":
                        journal.proofs.digest(receipt)
                        journal.record(receipt)
                    return report if name == "report" else {"phase": name}
                result = journal.run(name, position, action, copy=name == "copy",
                    auxiliary=recovered.with_name(recovered.name + "-native-restoration") if name == "native" else None)
        return result

    def seed(self):
        f = self.fixture
        result = self.consumer(f.raw, f.game, f.recovered, f.root / "tmp.zip", resume_owner=f.owner)
        self.assertTrue(result["fixtureOnly"])
        self.assertEqual(self.actions, list(self.PHASES))
        self.actions.clear()

    def test_exact_observer_profile_keeps_all_fourteen_transactions_and_sixteen_raw_packages(self):
        f = self.fixture
        self.seed()
        path = f.recovered / "Assets/native.mat"
        before, content = path.stat(), path.read_bytes()
        stream = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream), \
             patch.object(builder.prepare_resume, "Preparation", side_effect=RuntimeError("Preparation consumer reached")):
            with self.assertRaisesRegex(RuntimeError, "Preparation consumer reached"):
                f.retry()
        self.assertEqual(self.actions, [])
        self.assertEqual(path.read_bytes(), content)
        self.assertEqual((path.stat().st_ino, path.stat().st_mtime_ns), (before.st_ino, before.st_mtime_ns))
        events = [json.loads(line[len(recover.build_progress.PREFIX):]) for line in stream.getvalue().splitlines()
                  if line.startswith(recover.build_progress.PREFIX)]
        reused = [row["phase"].split(":", 1)[1] for row in events
                  if row["phase"].startswith("staging-section:") and row["status"] == "reuse"]
        self.assertEqual(reused, list(self.PHASES))
        self.assertTrue(any(row["phase"] == "recovery-batches" and row["done"] == row["total"] == 16 for row in events))

    def test_partial_or_unrecognized_observer_profile_never_claims_prior_derived_outputs(self):
        f = self.fixture
        for name in recovery_resume.OBSERVATION_PREVIOUS:
            for change in ("unknown", "previous"):
                with self.subTest(name=name, change=change):
                    current = copy.deepcopy(f.current)
                    row = next(row for row in current["mod"]["files"] if row["path"] == name)
                    if change == "previous": row.update(recovery_resume.OBSERVATION_PREVIOUS[name])
                    else: row["sha256"] = "f" * 64
                    # storage/Preparation are not raw exporters. A mixed
                    # observer set must nevertheless stop recipe aliasing of
                    # the changed full-assets/staging consumers.
                    self.assertNotEqual(recovery_resume.recipe_key(current, 1), f.old_key)

    def test_actual_changed_observer_source_and_completed_transaction_bytes_are_rejected(self):
        f = self.fixture
        self.seed()
        path = f.source / "tools/quest-builder/full_assets.py"
        original = path.read_bytes()
        path.write_bytes(original + b"\n# Unmanifested current observer source.\n")
        with self.assertRaisesRegex(storage.BuildError, "Current recovery source differs"):
            f.retry()
        self.assertEqual(self.actions, [])
        path.write_bytes(original)
        changed = f.recovered / "QuestRecovery/fixture-stage-audit.json"
        changed.write_bytes(b"corrupt completed derived transaction")
        with self.assertRaisesRegex(storage.BuildError, "Retained staged bytes differ"):
            f.retry()
        self.assertEqual(self.actions, [])


class CorruptionControls(ResumeFixture):
    def test_changed_core_identity_refuses_binding_without_mutation(self):
        current = self.updated()
        self.core_identity.write_bytes(b"changed native object identities")
        before = self.records(self.workspace)
        with self.assertRaisesRegex(storage.BuildError, "identity evidence changed"): self.select(current)
        self.assertEqual(self.records(self.workspace), before)
        self.assertFalse((self.output / "cache/raw-recovery-resume").exists())

    def test_changed_unused_core_is_not_read_after_matching_merged_checkpoint_exists(self):
        self.interrupted_batch()
        current = self.updated()
        (self.core / "Assets/core.mat").write_bytes(b"corrupt retained core")
        with patch.object(full_recovery, "_hash_file", side_effect=AssertionError("Unused core assets must not be rehashed")):
            result = self.finish(self.select(current))
        self.assertTrue(result["fullOriginalCatalogRecovered"])
        self.assertEqual((self.core / "Assets/core.mat").read_bytes(), b"corrupt retained core")
        self.assertFalse((self.batches / "merge-pending.json").exists())

    def test_changed_actual_merged_core_is_rejected_before_export_or_rollback(self):
        self.interrupted_batch(); current = self.updated()
        (self.raw / "Assets/core.mat").write_bytes(b"corrupt actual retained core output")
        before = (self.batches / "merge-pending.json").read_bytes()
        with self.assertRaisesRegex(recover.RecoveryError, "checkpoint file changed"):
            self.finish(self.select(current))
        self.assertEqual((self.batches / "merge-pending.json").read_bytes(), before)
        self.assertEqual(self.export_calls, 1)

    def test_core_without_merged_checkpoint_is_still_qualified_before_use(self):
        (self.core / "Assets/core.mat").write_bytes(b"corrupt core that would need copying")
        with self.assertRaisesRegex(recover.RecoveryError, "Core recovery output changed"):
            self.finish(self.select(self.updated()))
        self.assertFalse((self.raw / "quest-full-recovery-progress.json").exists())
        self.assertEqual(self.export_calls, 0)

    def test_changed_exporter_binary_is_rejected_by_real_batch_ownership(self):
        self.interrupted_batch()
        current = self.updated()
        selected = self.select(current)
        self.exporter.write_bytes(b"different executable tool")
        export_before = self.records(self.batches / "batch-000")
        with self.assertRaisesRegex(recover.RecoveryError, "different inputs"):
            self.finish(selected)
        self.assertEqual(self.records(self.batches / "batch-000"), export_before)
        self.assertEqual(self.export_calls, 1)

    def test_changed_completed_batch_is_never_deleted_or_reexported(self):
        self.interrupted_batch()
        current = self.updated()
        selected = self.select(current)
        corrupt = self.batches / "batch-000/Export/ExportedProject/Assets/Portrait_Jekserah.mat"
        corrupt.write_bytes(b"changed completed export")
        export_before = self.records(self.batches / "batch-000")
        with self.assertRaisesRegex(recover.RecoveryError, "checkpoint file changed"):
            self.finish(selected)
        self.assertEqual(self.records(self.batches / "batch-000"), export_before)
        self.assertEqual(self.export_calls, 1)

    def test_changed_merge_backup_refuses_rollback_without_mutation(self):
        self.interrupted_batch()
        current = self.updated()
        (self.batches / "merge-index-0.backup").write_bytes(b"changed retained backup")
        before = self.records(self.workspace)
        with self.assertRaisesRegex(recover.RecoveryError, "index backup changed"):
            self.finish(self.select(current))
        self.assertEqual(self.records(self.workspace), before)

    def test_bound_workspace_or_manifest_identity_cannot_be_redirected(self):
        current = self.updated()
        self.select(current)
        marker = self.output / "cache/raw-recovery-resume" / (recovery_resume.recipe_key(current, 1) + ".json")
        value = json.loads(marker.read_text())
        value["originalWorkspaceKey"] = "../foreign"
        storage.write_json(marker, value)
        with self.assertRaisesRegex(storage.BuildError, "workspace identity changed"): self.select(current)
        self.assertTrue((self.workspace / "core-recovery.json").exists())

    def test_bound_manifest_corruption_preserves_the_raw_workspace(self):
        current = self.updated()
        self.select(current)
        manifest = self.output / "manifests" / (self.previous["inputKey"] + ".json")
        value = json.loads(manifest.read_text())
        value["mod"]["files"][0]["sha256"] = "f" * 64
        storage.write_json(manifest, value)
        before = self.records(self.workspace)
        with self.assertRaisesRegex(storage.BuildError, "manifest identity changed"): self.select(current)
        self.assertEqual(self.records(self.workspace), before)

    def test_unsafe_original_receipt_paths_are_rejected_without_a_binding(self):
        current = self.updated()
        receipt = self.workspace / "original-source.json"
        value = json.loads(receipt.read_text())
        value["sourceInventory"][0]["path"] = "../another-owner/private"
        storage.write_json(receipt, value)
        before = self.records(self.workspace)
        with self.assertRaisesRegex(storage.BuildError, "unsafe or invalid"): self.select(current)
        self.assertEqual(self.records(self.workspace), before)
        self.assertFalse((self.output / "cache/raw-recovery-resume").exists())

    @unittest.skipIf(os.name == "nt", "Requires symlink creation rights")
    def test_raw_workspace_symlink_is_not_followed(self):
        current = self.updated()
        foreign = self.root / "private-kept"
        self.workspace.rename(foreign)
        self.workspace.symlink_to(foreign, target_is_directory=True)
        with self.assertRaises(storage.BuildError): self.select(current)
        self.assertTrue((foreign / "core-recovery.json").exists())


class ReceiptReadControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_missing_receipt_is_distinct_and_reports_the_limit(self):
        with self.assertRaisesRegex(storage.BuildError, r"evidence is missing: core-recovery.json \(limit 16777216 bytes\)"):
            recovery_resume._read(self.root / "core-recovery.json")

    def test_large_control_manifest_retains_small_cap_and_reports_actual_size(self):
        path = self.root / "original-source.json"
        with path.open("wb") as stream:
            stream.truncate(recovery_resume.MAX_JSON_BYTES + 1)
        with patch.object(recovery_resume.json, "loads", side_effect=AssertionError("Oversized control must not parse")):
            with self.assertRaisesRegex(storage.BuildError, r"oversized: original-source.json \(16777217 bytes; limit 16777216 bytes\)"):
                recovery_resume._read(path)
        self.assertTrue(path.is_file())

    def test_core_receipt_retains_a_larger_finite_cap(self):
        path = self.root / "core-recovery.json"
        with path.open("wb") as stream:
            stream.truncate(recovery_resume.MAX_CORE_RECEIPT_BYTES + 1)
        with patch.object(recovery_resume.json, "loads", side_effect=AssertionError("Oversized core must not parse")):
            with self.assertRaisesRegex(storage.BuildError, r"oversized: core-recovery.json \(268435457 bytes; limit 268435456 bytes\)"):
                recovery_resume._read(path, max_bytes=recovery_resume.MAX_CORE_RECEIPT_BYTES)
        self.assertTrue(path.is_file())

    def test_malformed_json_and_encoding_remain_unreadable_with_size_and_cause(self):
        path = self.root / "core-recovery.json"
        for raw, cause in ((b'{"files":', "JSONDecodeError"), (b"\xff", "UnicodeDecodeError")):
            with self.subTest(cause=cause):
                path.write_bytes(raw)
                with self.assertRaisesRegex(storage.BuildError, "unreadable: core-recovery.json.*"
                                            + str(len(raw)) + " bytes; limit .*" + cause):
                    recovery_resume._read(path)
                self.assertEqual(path.read_bytes(), raw)

    def test_io_failure_reports_type_without_leaking_exception_contents(self):
        path = self.root / "core-recovery.json"
        path.write_bytes(b"{}")
        with patch.object(Path, "open", side_effect=PermissionError("private account/token path")):
            with self.assertRaisesRegex(storage.BuildError, "unreadable: core-recovery.json.*2 bytes.*PermissionError") as error:
                recovery_resume._read(path)
        self.assertNotIn("private account/token", str(error.exception))

    def test_nonfile_and_nonobject_are_not_accepted_as_receipts(self):
        path = self.root / "core-recovery.json"
        path.mkdir()
        with self.assertRaisesRegex(storage.BuildError, "not a regular file: core-recovery.json"):
            recovery_resume._read(path)
        path.rmdir()
        path.write_text("[]")
        with self.assertRaisesRegex(storage.BuildError, "not an object: core-recovery.json"):
            recovery_resume._read(path)

    def test_actual_read_is_bounded_if_receipt_grows_after_stat(self):
        path = self.root / "core-recovery.json"
        path.write_bytes(b"{}")
        stream = io.BytesIO(b" " * 100)
        with patch.object(Path, "open", return_value=stream):
            with self.assertRaisesRegex(storage.BuildError, "grew beyond its read limit:.*limit 8 bytes.*at least 9 bytes"):
                recovery_resume._read(path, max_bytes=8)
        self.assertEqual(path.read_bytes(), b"{}")


if __name__ == "__main__":
    unittest.main()
