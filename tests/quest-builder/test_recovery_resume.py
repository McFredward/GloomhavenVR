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


class ResumeFixture(unittest.TestCase):
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
        for name in (*recovery_resume.LEGACY_MERGE_FILES, "tools/quest-recovery/QuestExportIdentity.cs",
                     "tools/quest-recovery/export_identity.py", "tools/quest-recovery/tool-lock.json",
                     *recovery_resume.DERIVED_FILES):
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

    def inputs(self, game_files, *, legacy=False, prior_progress=False):
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
        def interrupt(*args):
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
    def test_preceding_builder_progress_sources_resume_real_export_and_journal(self):
        old = self.workspace
        self.previous = self.inputs(self.previous["game"]["files"], prior_progress=True)
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
        self.assertEqual(sections, list(plan.RECOVERY_SECTIONS))
        self.assertEqual(observed, sorted(observed))
        self.assertGreater(len(set(observed)), 20)
        self.assertLess(max(observed), 100)

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


class CorruptionControls(ResumeFixture):
    def test_changed_core_identity_refuses_binding_without_mutation(self):
        current = self.updated()
        self.core_identity.write_bytes(b"changed native object identities")
        before = self.records(self.workspace)
        with self.assertRaisesRegex(storage.BuildError, "identity evidence changed"): self.select(current)
        self.assertEqual(self.records(self.workspace), before)
        self.assertFalse((self.output / "cache/raw-recovery-resume").exists())

    def test_changed_core_asset_is_rejected_by_actual_child_before_export_or_rollback(self):
        self.interrupted_batch()
        current = self.updated()
        (self.core / "Assets/core.mat").write_bytes(b"corrupt retained core")
        before = self.records(self.workspace)
        with self.assertRaisesRegex(recover.RecoveryError, "Core recovery output changed"):
            self.finish(self.select(current))
        self.assertEqual(self.records(self.workspace), before)
        self.assertTrue((self.batches / "merge-pending.json").exists())

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
