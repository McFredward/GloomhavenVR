"""Retain real late owners and archives across a runtime-only mod update.

The original-file/native/compiler fixture adapters are small; journal, actual
case-path subprocess, plugin metadata, ZIP packing and metadata transaction run
their production code. No Unity/Android asset compiler is invoked by this test.
"""
import copy
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import preparation_identity as identity
import preparation_metadata as metadata
import recovery_resume
import storage

spec = importlib.util.spec_from_file_location("completed_identity_fixture", ROOT / "tests/quest-builder/test_preparation_identity.py")
checks = importlib.util.module_from_spec(spec); spec.loader.exec_module(checks)


class CompletedPreparationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = checks.ActualPreparationMigrationTests("test_runtime_update_preserves_all_closed_original_conversions_before_mod_banks")
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.f = self.fixture.fixture
        self.fixture.stop = False; self.f.failure = None
        self.f.stack.enter_context(patch.object(sys, "dont_write_bytecode", True))
        self.f.stack.enter_context(patch.dict(os.environ, PYTHONDONTWRITEBYTECODE="1"))
        for name in (identity.METADATA_IDENTITY, "scripts/generate-environment-meshes.py",
                     "tools/environment-mesh/geometry.py", "unity/GloomhavenVR.Assets/Assets/Editor/QuestModBundles.cs",
                     "tools/QuestWeaver/CurrentModCompatibility.cs"):
            self.f.write(self.f.source, name, (ROOT / name).read_bytes())
        for name in sorted(identity.PREFIX_PLAYER_REFERENCES):
            self.f.write(self.f.source, name, b"previous owned desktop XR compiler reference")
        self.f.write(self.f.source, identity.MEMORY_POLICY_PREVIOUS["path"],
                     (ROOT / identity.MEMORY_POLICY_PREVIOUS["path"]).read_bytes())
        # The existing fixture's empty authored-bank receipt predates current-key
        # metadata migration. Model the real archive and external manifest roles.
        def mod_bank(project, inputs, *_args):
            self.f.tick("mod-banks")
            path = self.f.write(project, "Assets/StreamingAssets/quest-mod-content.zip", b"closed authored bank archive")
            self.f.document(project, "Assets/Quest/Resources/quest-mod-content.json", {
                "inputKey": inputs["inputKey"], "archive": path.name,
                "archiveSha256": storage.digest(path), "files": []})
            self.f.document(project, "Assets/Quest/Resources/quest-mod-bundles.json", {})
        self.f.stack.enter_context(patch.object(builder, "package_mod_content", mod_bank))
        actual = builder.prepare_resume.Preparation
        fixture = self.f
        class PreparationWithCurrentInputs(actual):
            def __init__(self, *args, **kwargs):
                kwargs.setdefault("current_inputs", fixture.inputs)
                super().__init__(*args, **kwargs)
        self.f.stack.enter_context(patch.object(builder.prepare_resume, "Preparation", PreparationWithCurrentInputs))
        for name, raw in getattr(self, "editor_sources", {}).items():
            self.f.write(self.f.source, name, raw)
        self.before = self.fixture.inputs()
        self.old_source = self.f.output / "inputs/mod" / self.before["mod"]["key"]
        shutil.copytree(self.f.source, self.old_source, dirs_exist_ok=True)
        self.project = self.f.run_prepare()
        self.journal = self.f.output / "cache/prepare-resume" / self.project.name / "journal.json"
        self.value = json.loads(self.journal.read_text())
        self.assertEqual(len(self.value["steps"]), 27)
        self.assertTrue(identity._completed_game_preparation(self.value, "game"))
        self.library = self.f.write(self.project, "Library/already-imported", b"keep expensive original imports")
        self.originals = {name: ((self.project / name).read_bytes(), (self.project / name).stat().st_mtime_ns)
            for name in (storage.CONTENT_PATHS[0], "Assets/StreamingAssets/quest-mod-content.zip",
                         "Assets/Plugins/GH.Runtime.dll.meta", "QuestStartupEvidence/case-path-migration.json")}
        self.calls = dict(self.f.calls)

    def updated(self, number=1):
        previous = self.f.inputs
        old_source = self.f.output / "inputs/mod" / previous["mod"]["key"]
        if not old_source.exists(): shutil.copytree(self.f.source, old_source)
        self.f.write(self.f.source, "src/GloomhavenVR/Net/UpdatedRuntime.cs",
                     ("// New runtime " + str(number) + ", consumed only by subsequent weaving.\n").encode())
        self.f.write(self.f.source, "tools/QuestWeaver/CurrentModCompatibility.cs",
                     (ROOT / "tools/QuestWeaver/CurrentModCompatibility.cs").read_bytes()
                     + ("\n// Later static runtime adapter " + str(number) + "\n").encode())
        self.f.write(self.f.source, "tools/QuestWeaver/NativeCameraBoundary.cs", b"// Added static adapter runs only after prepare().\n")
        current = self.fixture.inputs()
        current["mod"]["modBuild"] += number
        current.pop("inputKey")
        current["inputKey"] = storage.value_hash(current)
        storage.write_json(self.f.output / "manifests" / (current["inputKey"] + ".json"), current)
        self.f.inputs = current
        return current

    def assert_retained(self, current):
        self.assertEqual(self.f.calls, self.calls)
        value = json.loads(self.journal.read_text())
        self.assertEqual(value["inputKey"], current["inputKey"])
        self.assertEqual(len(value["steps"]), 27)
        self.assertEqual([(s["name"], s["operation"]) for s in value["steps"]],
                         [(s["name"], s["operation"]) for s in self.value["steps"]])
        for name, original in self.originals.items():
            self.assertEqual(((self.project / name).read_bytes(), (self.project / name).stat().st_mtime_ns), original)
        self.assertEqual(self.library.read_bytes(), b"keep expensive original imports")
        for name in metadata.PATHS:
            self.assertEqual(json.loads((self.project / name).read_text())["inputKey"], current["inputKey"])
        self.assertEqual(json.loads((self.project / metadata.INPUTS).read_text()), current)
        self.assertEqual(json.loads((self.project / metadata.SETTINGS).read_text())["modBuild"], current["mod"]["modBuild"])

    def test_runtime_update_keeps_all_twenty_seven_asset_steps_and_later_owners(self):
        current = self.updated()
        self.assertEqual(identity.rebind_key(self.f.output, self.project, current, self.f.source,
                target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.before["inputKey"])
        self.f.run_prepare()
        self.assert_retained(current)
        self.assertFalse((self.journal.parent / "metadata-refresh.json").exists())
        # Force another cold outer receipt check: updated later ownership must
        # remain coherent rather than exposing stale base/case/importer records.
        key = storage.value_hash({"input": current["inputKey"], "recipe": builder.RECIPE})
        storage.Stages(self.f.output).path("prepare", key).unlink()
        with patch.dict(storage._invocation_file_proofs, clear=True):
            self.f.run_prepare()
        self.assert_retained(current)

    def test_changed_desktop_xr_refs_retain_all_asset_steps_without_reading_payloads(self):
        # Capture160059 reset the complete27 frontier solely because these
        # later compile references and their provenance changed in a new release.
        for name in sorted(identity.PREFIX_PLAYER_REFERENCES):
            self.f.write(self.f.source, name, b"new release compiler reference and stable provenance")
        current = self.updated()
        self.assertNotEqual(current["inputKey"], self.before["inputKey"])
        self.assertEqual(identity.rebind_key(self.f.output, self.project, current, self.f.source,
                target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.before["inputKey"])
        actual_digest = builder.prepare_resume.digest
        small_controls = []
        def only_metadata(path):
            if Path(path).is_relative_to(self.f.source):
                return actual_digest(path)  # Current immutable source is still qualified.
            relative = Path(path).relative_to(self.project).as_posix()
            compiler_control = relative == "Assets/csc.rsp" or relative.startswith("Assets/Plugins/") and relative.endswith(".meta")
            if compiler_control:
                small_controls.append(Path(path).stat().st_size)
            if relative not in metadata.PATHS and not compiler_control:
                self.fail("Retained assets must not reopen for changed later XR references: " + str(path))
            return actual_digest(path)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(builder.prepare_resume, "digest", only_metadata):
            self.f.run_prepare()
        self.assert_retained(current)
        self.assertLess(sum(small_controls), 64 * 1024)

    def test_unknown_runtime_dependency_or_earlier_xr_consumer_does_not_claim_reuse(self):
        current = self.updated()
        self.f.write(self.f.source, "libs/RuntimeDeps/NewUnreviewedProducer.dll", b"unknown consumer")
        bad = self.fixture.inputs()
        self.assertIsNone(identity.rebind_key(self.f.output, self.project, bad, self.f.source,
                target="game", recipe=builder.RECIPE, recovery=recovery_resume))
        (self.f.source / "libs/RuntimeDeps/NewUnreviewedProducer.dll").unlink()
        path = self.f.source / identity.BUILDER
        original = path.read_bytes()
        changed = original.replace(b"    def generate_files(resume):", b"    def generate_files(resume):\n        (source / 'libs/RuntimeDeps/Unity.XR.OpenXR.dll').read_bytes()")
        self.assertNotEqual(original, changed)
        path.write_bytes(changed)
        bad = self.fixture.inputs()
        self.assertIsNone(identity.rebind_key(self.f.output, self.project, bad, self.f.source,
                target="game", recipe=builder.RECIPE, recovery=recovery_resume))

    def test_each_interrupted_metadata_publication_replays_only_four_small_documents(self):
        actual = metadata.write_json
        for number, relative in enumerate(metadata.PATHS, 1):
            with self.subTest(relative=relative):
                before_key = json.loads(self.journal.read_text())["inputKey"]
                current = self.updated(number)
                def killed(path, value):
                    actual(path, value)
                    if Path(path) == self.project / relative:
                        raise RuntimeError("fixture kill after JSON publication")
                with patch.object(metadata, "write_json", killed):
                    with self.assertRaisesRegex(RuntimeError, "fixture kill"):
                        self.f.run_prepare()
                self.assertEqual(json.loads(self.journal.read_text())["inputKey"], before_key)
                self.assertTrue((self.journal.parent / "metadata-refresh.json").exists())
                self.assertEqual(self.f.calls, self.calls)
                self.f.run_prepare()
                self.assert_retained(current)
                self.assertFalse((self.journal.parent / "metadata-refresh.json").exists())

    def test_kill_after_journal_publication_retains_transferred_warm_asset_proofs(self):
        current = self.updated()
        actual = metadata.write_json
        def killed(path, value):
            actual(path, value)
            if Path(path) == self.journal: raise RuntimeError("fixture kill after new journal publication")
        with patch.object(metadata, "write_json", killed):
            with self.assertRaisesRegex(RuntimeError, "fixture kill"):
                self.f.run_prepare()
        self.assertEqual(json.loads(self.journal.read_text())["inputKey"], current["inputKey"])
        self.assertTrue((self.journal.parent / "metadata-refresh.json").exists())
        actual_digest = builder.prepare_resume.digest
        def only_small_changed_metadata(path):
            if Path(path).relative_to(self.project).as_posix() not in metadata.PATHS:
                self.fail("A durable transferred original asset proof must not require payload bytes: " + str(path))
            return actual_digest(path)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(builder.prepare_resume, "digest", only_small_changed_metadata):
            self.f.run_prepare()
        self.assert_retained(current)
        self.assertFalse((self.journal.parent / "metadata-refresh.json").exists())

    def test_changed_game_profile_template_graphics_and_bank_producers_refuse_complete_reuse(self):
        current = self.updated()
        for change in ("game", "profile", "template", "graphics", "bank", "environment-script", "authored-link", "metadata-helper", "memory-helper", "memory-policy"):
            with self.subTest(change=change):
                bad = copy.deepcopy(current)
                if change == "game": bad["game"]["files"][0]["sha256"] = "f" * 64
                elif change == "profile": bad["profile"]["isDummy"] = False
                else:
                    selected = {"template": "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/Probe.cs",
                        "graphics": "tools/quest-builder/full_shaders.py",
                        "bank": "unity/GloomhavenVR.Assets/Assets/Editor/QuestModBundles.cs",
                        "environment-script": "scripts/generate-environment-meshes.py",
                        "authored-link": "unity/GloomhavenVR.Assets/Assets/Editor/GrabBarMeshLink.cs",
                        "metadata-helper": identity.METADATA_IDENTITY,
                        "memory-helper": identity.PREFIX_MEMORY_HELPER,
                        "memory-policy": identity.MEMORY_POLICY_PREVIOUS["path"]}[change]
                    found = next((row for row in bad["mod"]["files"] if row["path"] == selected), None)
                    if found is None: bad["mod"]["files"].append({"path": selected, "size": 1, "sha256": "f" * 64})
                    else: found["sha256"] = "f" * 64
                bad["mod"]["key"] = storage.value_hash({"files": bad["mod"]["files"]})
                bad["game"]["key"] = storage.value_hash({"files": bad["game"]["files"]})
                bad.pop("inputKey"); bad["inputKey"] = storage.value_hash(bad)
                storage.write_json(self.f.output / "manifests" / (bad["inputKey"] + ".json"), bad)
                self.assertIsNone(identity.rebind_key(self.f.output, self.project, bad, self.f.source,
                    target="game", recipe=builder.RECIPE, recovery=recovery_resume))
                self.assertEqual(json.loads(self.journal.read_text()), self.value)
        self.assertEqual(self.f.calls, self.calls)

    def test_changed_late_archive_function_body_rejects_fully_completed_preparation(self):
        self.updated()
        path = self.f.source / identity.BUILDER
        original = path.read_bytes()
        changed = original.replace(b"compression=zipfile.ZIP_STORED", b"compression=zipfile.ZIP_DEFLATED")
        self.assertNotEqual(original, changed)
        # This late function body is outside the earlier original-prefix AST.
        self.assertEqual(identity.builder_producer_digest(original, original_prefix=True),
                         identity.builder_producer_digest(changed, original_prefix=True))
        path.write_bytes(changed)
        current = self.fixture.inputs()
        self.assertIsNone(identity.rebind_key(self.f.output, self.project, current, self.f.source,
            target="game", recipe=builder.RECIPE, recovery=recovery_resume))
        self.assertEqual(json.loads(self.journal.read_text()), self.value)
        self.assertEqual(self.f.calls, self.calls)

    def test_changed_completed_schedule_or_pending_owner_never_qualifies_complete_reuse(self):
        current = self.updated()
        for change in ("missing", "reordered", "unknown-owner", "pending"):
            with self.subTest(change=change):
                value = copy.deepcopy(self.value)
                if change == "missing": value["steps"].pop()
                elif change == "reordered": value["steps"][-1], value["steps"][-2] = value["steps"][-2], value["steps"][-1]
                elif change == "unknown-owner": value["steps"][-1]["name"] = "unknown-settings"
                else: value["pending"] = {"name": "final-settings", "operation": "preparation-contracts"}
                storage.write_json(self.journal, value)
                self.assertFalse(identity._completed_game_preparation(value, "game"))
                self.assertIsNone(identity.rebind_key(self.f.output, self.project, current, self.f.source,
                    target="game", recipe=builder.RECIPE, recovery=recovery_resume))
                self.assertEqual(json.loads(self.journal.read_text()), value)
        self.assertEqual(self.f.calls, self.calls)

    def test_transaction_cannot_change_archive_content_or_owner_under_an_identity_refresh(self):
        self.updated()
        actual = metadata.write_json
        def killed(path, value):
            actual(path, value)
            if Path(path) == self.project / metadata.PATHS[0]: raise RuntimeError("fixture kill")
        with patch.object(metadata, "write_json", killed):
            with self.assertRaisesRegex(RuntimeError, "fixture kill"): self.f.run_prepare()
        transaction = self.journal.parent / "metadata-refresh.json"
        original = json.loads(transaction.read_text())
        before = {name: (self.project / name).read_bytes() for name in metadata.PATHS}
        for change in ("archive-bytes", "owner", "nonobject"):
            with self.subTest(change=change):
                bad = copy.deepcopy(original)
                row = bad["files"][0]
                if change == "archive-bytes":
                    row["replacement"]["archiveSha256"] = "f" * 64
                    row["after"] = metadata._record(row["path"], row["replacement"])
                elif change == "owner": row["step"] -= 1
                else: bad["files"][0] = "invalid"
                storage.write_json(transaction, bad)
                with self.assertRaisesRegex(storage.BuildError, "transaction|contract"):
                    self.f.run_prepare()
                self.assertEqual({name: (self.project / name).read_bytes() for name in metadata.PATHS}, before)
                self.assertEqual(json.loads(self.journal.read_text()), self.value)
        self.assertEqual(self.f.calls, self.calls)

    def test_current_metadata_helper_profile_is_exact_and_unknown_edits_remain_consumed(self):
        profile = getattr(recovery_resume, "OBSERVATION_EDITOR_OVERLAY", recovery_resume.OBSERVATION_COMPLETED_METADATA)
        actual = {name: {"path": name, "size": (ROOT / name).stat().st_size,
                         "sha256": hashlib.sha256((ROOT / name).read_bytes()).hexdigest()} for name in profile}
        self.assertEqual(actual, profile)
        normalized = recovery_resume.preparation_source_rows(list(actual.values()))
        self.assertNotIn(identity.METADATA_IDENTITY, {row["path"] for row in normalized})
        bad = copy.deepcopy(actual)
        bad[identity.METADATA_IDENTITY]["sha256"] = "f" * 64
        normalized = recovery_resume.preparation_source_rows(list(bad.values()))
        self.assertIn(bad[identity.METADATA_IDENTITY], normalized)

    def test_exact_current_inputs_coordination_preserves_old_builder_producer_identity(self):
        before = (ROOT / identity.BUILDER).read_bytes()
        if b"current_inputs=inputs" in before:
            before = before.replace(b", current_inputs=inputs", b"")
            before = before.replace(b",\n                                           current_inputs=inputs", b"")
        after = before.replace(b"compatible_input_key=prior_preparation_key,",
                               b"compatible_input_key=prior_preparation_key, current_inputs=inputs,")
        self.assertNotEqual(before, after)
        for prefix in (False, True):
            self.assertEqual(identity.builder_producer_digest(before, original_prefix=prefix),
                             identity.builder_producer_digest(after, original_prefix=prefix))
            self.assertNotEqual(identity.builder_producer_digest(before, original_prefix=prefix),
                identity.builder_producer_digest(after.replace(b"current_inputs=inputs", b"current_inputs=unqualified_inputs"), original_prefix=prefix))

    def test_corrupt_document_during_interrupted_refresh_never_becomes_completed_reuse(self):
        self.updated()
        actual = metadata.write_json
        def killed(path, value):
            actual(path, value)
            if Path(path) == self.project / metadata.INPUTS: raise RuntimeError("fixture kill")
        with patch.object(metadata, "write_json", killed):
            with self.assertRaisesRegex(RuntimeError, "fixture kill"): self.f.run_prepare()
        self.f.write(self.project, metadata.PATHS[0], b'{"inputKey":"foreign"}\n')
        with self.assertRaisesRegex(storage.BuildError, "outside its transaction"):
            self.f.run_prepare()
        self.assertEqual(self.f.calls, self.calls)
        self.assertEqual(json.loads(self.journal.read_text())["inputKey"], self.before["inputKey"])
        self.assertTrue((self.journal.parent / "metadata-refresh.json").exists())


if __name__ == "__main__": unittest.main()
