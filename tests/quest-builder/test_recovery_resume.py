"""Audited recipe migration replays real retained exports and merge journals."""
import copy
import argparse
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

    def inputs(self, game_files, *, legacy=False):
        files = storage.inventory(self.source)
        if legacy:
            for row in files:
                if row["path"] in recovery_resume.LEGACY_MERGE_FILES:
                    row["sha256"] = recovery_resume.LEGACY_MERGE_FILES[row["path"]]
                    row["size"] = LEGACY_MERGE_BYTES[row["path"]]
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


if __name__ == "__main__":
    unittest.main()
