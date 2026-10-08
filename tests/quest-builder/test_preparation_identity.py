"""Actual preparation retries retain conversions across bounded tool updates."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import preparation_identity as identity
import recovery_resume
import storage

spec = importlib.util.spec_from_file_location("preparation_identity_pipeline", ROOT / "tests/quest-builder/test_prepare_resume.py")
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class BuilderProducerIdentityTests(unittest.TestCase):
    def setUp(self):
        self.original = (ROOT / identity.BUILDER).read_bytes()

    def test_only_explicit_journal_coordination_is_removed_from_complete_builder_ast(self):
        # A preceding Builder has precisely the same producer bodies, without
        # the new module load/rebind/constructor argument. Source comments and
        # formatting do not generate assets.
        previous = self.original.decode()
        previous = previous.replace('preparation_identity = _local_helper("preparation_identity")\n', "")
        previous = previous.replace(
            "    prior_preparation_key = preparation_identity.rebind_key(\n"
            "        output, project, inputs, source, target=args.target, recipe=RECIPE, recovery=recovery_resume)\n\n", "")
        previous = previous.replace(", compatible_input_key=prior_preparation_key", "")
        self.assertNotEqual(previous.encode(), self.original)
        self.assertEqual(identity.builder_producer_digest(previous.encode()), identity.builder_producer_digest(self.original))
        self.assertEqual(identity.builder_producer_digest(b"# Older source comment.\n" + self.original),
                         identity.builder_producer_digest(self.original))

    def test_changed_producer_constant_call_body_or_unknown_identity_argument_stays_distinct(self):
        for before, after in (
            ('PACKAGE = "dev.gloomhavenvr.quest"', 'PACKAGE = "different.quest.package"'),
            ("base_project, base_contracts", "loading_resources, base_contracts"),
            ('"profileSha256": digest(resources / "quest-profile.json")', '"profileSha256": "unwitnessed"'),
            ("compatible_input_key=prior_preparation_key", "compatible_input_key=other_key"),
            ("target=args.target, recipe=RECIPE, recovery=recovery_resume", "target=args.target, recipe=999, recovery=recovery_resume"),
        ):
            with self.subTest(change=after):
                changed = self.original.replace(before.encode(), after.encode())
                self.assertNotEqual(changed, self.original)
                self.assertNotEqual(identity.builder_producer_digest(changed), identity.builder_producer_digest(self.original))

    def test_original_prefix_does_not_depend_on_later_cli_or_player_functions(self):
        changed = self.original.replace(b'"inspect: reading owned game changes and current mod inputs"', b'"inspect: using a persistent original input inventory"')
        self.assertNotEqual(changed, self.original)
        self.assertNotEqual(identity.builder_producer_digest(changed), identity.builder_producer_digest(self.original))
        self.assertEqual(identity.builder_producer_digest(changed, original_prefix=True),
                         identity.builder_producer_digest(self.original, original_prefix=True))
        changed = self.original.replace(b"base_project, base_contracts", b"loading_resources, base_contracts")
        self.assertNotEqual(identity.builder_producer_digest(changed, original_prefix=True),
                            identity.builder_producer_digest(self.original, original_prefix=True))


class ActualPreparationMigrationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = pipeline.PreparationPipelineTests("test_real_prepare_graphics_failure_retains_completed_native_audio_compute_and_library")
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        f = self.fixture
        for name in (identity.BUILDER, identity.IDENTITY, identity.RECIPE_IDENTITY, *recovery_resume.OBSERVATION_PREVIOUS):
            f.write(f.source, name, (ROOT / name).read_bytes())
        # The first process is a real older immutable source snapshot, with
        # only a non-producing comment differing from the updated Builder.
        path = f.source / identity.BUILDER
        path.write_bytes(b"# Previous observer-only source snapshot.\n" + path.read_bytes())
        self.previous = self.inputs()
        self.previous_source = f.output / "inputs/mod" / self.previous["mod"]["key"]
        shutil.copytree(f.source, self.previous_source)
        self.native_attempts = 0
        self.stop = True
        def native_sprites(project, *_args, **_kwargs):
            self.native_attempts += 1
            if self.stop:
                raise storage.BuildError("Native Sprite preparation retry reached")
            return f.evidence(project, "sprites", "Assets/QuestOriginalCampaign/native-sprites.json", {"assets": []})
        f.stack.enter_context(patch.object(sys.modules["full_sprites"], "stage", native_sprites))

    def inputs(self):
        f = self.fixture
        mod, game = storage.inventory(f.source), storage.inventory(f.game)
        selected = copy.deepcopy(f.inputs)
        selected.update(schema=1, recipe=builder.RECIPE, target="game")
        selected["mod"] = {"files": mod, "key": storage.value_hash({"files": mod}), "modBuild": 638,
                           "commit": "fixture-source", "dirty": False}
        selected["game"] = {"files": game, "key": storage.value_hash({"files": game}), "unityVersion": "2021.3.5f1"}
        selected.pop("inputKey", None)
        selected["inputKey"] = storage.value_hash(selected)
        storage.write_json(f.output / "manifests" / (selected["inputKey"] + ".json"), selected)
        f.inputs = selected
        return selected

    def seed(self):
        f = self.fixture
        with self.assertRaisesRegex(storage.BuildError, "Native Sprite preparation retry reached"):
            f.run_prepare()
        project = f.project()
        self.journal = f.output / "cache/prepare-resume" / project.name / "journal.json"
        self.assertEqual([step["name"] for step in json.loads(self.journal.read_text())["steps"]],
                         ["base-project", "post-effects", "loading-resources", "startup-movies"])
        library = f.write(project, "Library/retained-original-import", b"owned native import cache")
        self.library = library
        self.before_calls = dict(f.calls)
        self.probe = project / "Assets/Quest/Runtime/Probe.cs"
        self.probe_bytes, self.probe_stamp = self.probe.read_bytes(), builder.prepare_resume._stamp(self.probe)
        return project

    def updated(self, *, delivery=True):
        f = self.fixture
        (f.source / identity.BUILDER).write_bytes((ROOT / identity.BUILDER).read_bytes())
        if delivery:
            f.write(f.source, "tools/quest-wizard/status-only.js", b"const status = 'updated';")
            f.write(f.source, "tools/quest-wizard-ui/status-only.mjs", b"export const status = 'updated';")
        current = self.inputs()
        self.assertNotEqual(current["inputKey"], self.previous["inputKey"])
        return current

    def test_actual_builder_new_snapshot_retains_four_steps_and_retries_only_native_sprites(self):
        project = self.seed()
        current = self.updated()
        self.assertEqual(identity.rebind_key(self.fixture.output, project, current, self.fixture.source,
                         target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.previous["inputKey"])
        with patch.object(builder.prepare_resume, "copy_changed", side_effect=AssertionError("Completed base copies must not run")):
            with self.assertRaisesRegex(storage.BuildError, "Native Sprite preparation retry reached"):
                self.fixture.run_prepare()
        journal = json.loads(self.journal.read_text())
        self.assertEqual(journal["inputKey"], current["inputKey"])
        self.assertEqual([step["name"] for step in journal["steps"]],
                         ["base-project", "post-effects", "loading-resources", "startup-movies"])
        self.assertEqual(self.fixture.calls, self.before_calls)
        self.assertEqual(self.native_attempts, 2)
        self.assertEqual(self.probe.read_bytes(), self.probe_bytes)
        self.assertEqual(builder.prepare_resume._stamp(self.probe), self.probe_stamp)
        self.assertEqual(self.library.read_bytes(), b"owned native import cache")

    def test_unknown_producer_game_profile_template_and_target_changes_never_claim_compatibility(self):
        project = self.seed()
        self.updated()
        baseline = copy.deepcopy(self.fixture.inputs)
        original_builder = (self.fixture.source / identity.BUILDER).read_bytes()
        for change in ("producer", "template", "profile", "game", "backend", "target"):
            with self.subTest(change=change):
                current = copy.deepcopy(baseline)
                if change == "producer":
                    path = self.fixture.source / identity.BUILDER
                    path.write_bytes(original_builder.replace(b'RECIPE = 1', b'RECIPE = 2'))
                    current = self.inputs()
                elif change == "template":
                    current["mod"]["files"] = copy.deepcopy(current["mod"]["files"])
                    next(row for row in current["mod"]["files"] if row["path"].endswith("Runtime/Probe.cs"))["sha256"] = "f" * 64
                elif change == "profile": current["profile"]["displayName"] = "Another owner"
                elif change == "game": current["game"]["files"][0]["sha256"] = "f" * 64
                elif change == "backend": current["proceduralBackend"] = "box64-wine9"
                elif change == "target": current["target"] = "startup"
                current["mod"]["key"] = storage.value_hash({"files": current["mod"]["files"]})
                current["game"]["key"] = storage.value_hash({"files": current["game"]["files"]})
                current.pop("inputKey", None); current["inputKey"] = storage.value_hash(current)
                storage.write_json(self.fixture.output / "manifests" / (current["inputKey"] + ".json"), current)
                self.assertIsNone(identity.rebind_key(self.fixture.output, project, current, self.fixture.source,
                                  target="game", recipe=builder.RECIPE, recovery=recovery_resume))
                (self.fixture.source / identity.BUILDER).write_bytes(original_builder)

    def test_current_manifest_or_retained_builder_corruption_fails_before_any_project_change(self):
        project = self.seed()
        current = self.updated()
        source = self.previous_source / identity.BUILDER
        source.write_bytes(b"corrupt previous immutable Builder")
        before = self.journal.read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "differs from its immutable source manifest"):
            identity.rebind_key(self.fixture.output, project, current, self.fixture.source,
                                target="game", recipe=builder.RECIPE, recovery=recovery_resume)
        self.assertEqual(self.journal.read_bytes(), before)
        self.assertEqual(self.probe.read_bytes(), self.probe_bytes)
        current["profile"]["displayName"] = "Unmanifested edit"
        with self.assertRaisesRegex(storage.BuildError, "differ from their immutable manifest"):
            identity.rebind_key(self.fixture.output, project, current, self.fixture.source,
                                target="game", recipe=builder.RECIPE, recovery=recovery_resume)
        self.assertEqual(self.journal.read_bytes(), before)

    def test_real_builder_changed_original_is_rejected_before_rebinding_or_replaying_prefix(self):
        self.seed()
        self.updated()
        original = self.fixture.game / "original.bin"
        original.write_bytes(b"changed original game bytes")
        before = self.journal.read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "source changed since its checkpoint"):
            self.fixture.run_prepare()
        self.assertEqual(self.journal.read_bytes(), before)
        self.assertEqual(self.fixture.calls, self.before_calls)
        self.assertEqual(self.native_attempts, 1)
        self.assertEqual(self.probe.read_bytes(), self.probe_bytes)
        self.assertEqual(self.library.read_bytes(), b"owned native import cache")

    def startup_transport_failure(self):
        """Exercise a real ordered preparation, stopped at the captured boundary."""
        f = self.fixture
        self.stop = False
        names = (identity.UI_TRANSPORT_PREVIOUS["path"],
                 "tools/quest-builder/startup.py", "tools/quest-builder/full_sprites.py", "tools/quest-builder/audio.py")
        previous, fixed = {}, {}
        for name in names:
            old = b"# Qualified preceding fixture producer.\n"
            new = b"# Qualified compatible fixture producer.\n"
            f.write(f.source, name, old)
            previous[name] = {"path": name, "size": len(old), "sha256": hashlib.sha256(old).hexdigest()}
            fixed[name] = {"path": name, "size": len(new), "sha256": hashlib.sha256(new).hexdigest()}
        for module in (identity, builder.preparation_identity):
            f.stack.enter_context(patch.object(module, "UI_TRANSPORT_PREVIOUS", previous[names[0]]))
            f.stack.enter_context(patch.object(module, "UI_TRANSPORT_FIXED", fixed[names[0]]))
            f.stack.enter_context(patch.object(module, "STARTUP_PROGRESS_PREVIOUS", {name: previous[name] for name in names[1:]}))
            f.stack.enter_context(patch.object(module, "STARTUP_PROGRESS_FIXED", {name: fixed[name] for name in names[1:]}))
        self.previous = self.inputs()
        self.previous_source = f.output / "inputs/mod" / self.previous["mod"]["key"]
        shutil.copytree(f.source, self.previous_source)
        def fail_ui(_project):
            f.tick("ui-attempt")
            raise storage.BuildError("Original UI recipe transport failure")
        f.stack.enter_context(patch.object(builder.ui_assets, "stage_campaign_recipe_manifest", fail_ui))
        with self.assertRaisesRegex(storage.BuildError, "Original UI recipe transport failure"):
            f.run_prepare()
        project = f.project()
        journal = f.output / "cache/prepare-resume" / project.name / "journal.json"
        self.assertEqual([step["name"] for step in json.loads(journal.read_text())["steps"]],
                         ["base-project", "post-effects", "loading-resources", "startup-movies", "native-sprites", "loading-sprite", "startup-audio"])
        before_calls = dict(f.calls)
        library = f.write(project, "Library/retained-original-import", b"native import cache")
        for name in names: f.write(f.source, name, b"# Qualified compatible fixture producer.\n")
        self.updated()
        return project, journal, before_calls, library

    def test_exact_startup_repair_retains_all_six_closed_content_steps_and_retries_only_ui(self):
        project, journal, calls, library = self.startup_transport_failure()
        f = self.fixture
        self.assertEqual(identity.rebind_key(f.output, project, f.inputs, f.source,
                         target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.previous["inputKey"])
        with patch.object(builder.prepare_resume, "copy_changed", side_effect=AssertionError("Closed base/movie/audio producers must not copy again")):
            with self.assertRaisesRegex(storage.BuildError, "Original UI recipe transport failure"):
                f.run_prepare()
        calls["ui-attempt"] += 1
        self.assertEqual(f.calls, calls)
        self.assertEqual(self.native_attempts, 1)
        self.assertEqual(json.loads(journal.read_text())["inputKey"], f.inputs["inputKey"])
        self.assertEqual(library.read_bytes(), b"native import cache")

    def test_new_mod_runtime_preserves_original_startup_prefix_under_current_input_key(self):
        project, journal, calls, library = self.startup_transport_failure()
        f = self.fixture
        f.write(f.source, "src/GloomhavenVR/Net/NetProtocol.cs", b"// Updated current mod build.\n")
        f.write(f.source, "src/GloomhavenVR/WorldUI/CurrentRuntime.cs", b"// Current runtime consumed after startup.\n")
        f.write(f.source, "scripts/current-runtime-check.py", b"# Unrelated developer verification.\n")
        # Git-free releases carry a changing manifest wrapping all delivered
        # source rows; its metadata is not a preparation producer input.
        f.write(f.source, "quest-builder-release.json", b'{"sourceCommit":"new-release"}\n')
        current = self.inputs()
        current["mod"]["modBuild"] += 1
        current.pop("inputKey"); current["inputKey"] = storage.value_hash(current)
        storage.write_json(f.output / "manifests" / (current["inputKey"] + ".json"), current)
        f.inputs = current
        self.assertEqual(identity.rebind_key(f.output, project, current, f.source,
                         target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.previous["inputKey"])
        with patch.object(builder.prepare_resume, "copy_changed", side_effect=AssertionError("Original startup must not restart for a later runtime update")):
            with self.assertRaisesRegex(storage.BuildError, "Original UI recipe transport failure"):
                f.run_prepare()
        calls["ui-attempt"] += 1
        self.assertEqual(f.calls, calls)
        self.assertEqual(json.loads(journal.read_text())["inputKey"], current["inputKey"])
        self.assertEqual(library.read_bytes(), b"native import cache")

    def test_mod_prefix_exceptions_do_not_cover_loading_inputs_or_later_runtime_owners(self):
        project, journal, _, _ = self.startup_transport_failure()
        f = self.fixture
        baseline = copy.deepcopy(f.inputs)
        for name in identity.PREFIX_MOD_INPUTS:
            with self.subTest(input=name):
                current = copy.deepcopy(baseline)
                next(row for row in current["mod"]["files"] if row["path"] == name).update(size=8, sha256="e" * 64)
                current["mod"]["key"] = storage.value_hash({"files": current["mod"]["files"]})
                current.pop("inputKey"); current["inputKey"] = storage.value_hash(current)
                storage.write_json(f.output / "manifests" / (current["inputKey"] + ".json"), current)
                self.assertIsNone(identity.rebind_key(f.output, project, current, f.source,
                                  target="game", recipe=builder.RECIPE, recovery=recovery_resume))
        value = json.loads(journal.read_text())
        value["steps"].append({"name": "ui-recipes", "operation": "startup-content", "outputs": []})
        value["pending"] = {"name": "startup-ui", "operation": "startup-content", "undo": []}
        storage.write_json(journal, value)
        current = copy.deepcopy(baseline)
        current["mod"]["files"].append({"path": "src/GloomhavenVR/Runtime.cs", "size": 8, "sha256": "e" * 64})
        current["mod"]["modBuild"] += 1
        current["mod"]["key"] = storage.value_hash({"files": current["mod"]["files"]})
        current.pop("inputKey"); current["inputKey"] = storage.value_hash(current)
        storage.write_json(f.output / "manifests" / (current["inputKey"] + ".json"), current)
        self.assertIsNone(identity.rebind_key(f.output, project, current, f.source,
                          target="game", recipe=builder.RECIPE, recovery=recovery_resume))

    def test_runtime_update_preserves_all_closed_original_conversions_before_mod_banks(self):
        f = self.fixture
        self.stop = False
        f.failure = "graphics"
        self.previous = self.inputs()
        self.previous_source = f.output / "inputs/mod" / self.previous["mod"]["key"]
        shutil.copytree(f.source, self.previous_source, dirs_exist_ok=True)
        with self.assertRaisesRegex(storage.BuildError, "fixture graphics failure"):
            f.run_prepare()
        project = f.project()
        journal = f.output / "cache/prepare-resume" / project.name / "journal.json"
        value = json.loads(journal.read_text())
        self.assertEqual(value["steps"][-1]["name"], "campaign-compute")
        self.assertEqual(len(value["steps"]), 18)
        calls = dict(f.calls)
        library = f.write(project, "Library/retained-current-import", b"existing original Unity imports")
        f.write(f.source, "src/GloomhavenVR/Net/NewCurrentRuntime.cs", b"// Current runtime used by later weaving.\n")
        f.write(f.source, "scripts/current-runtime-check.py", b"# Current unrelated check.\n")
        current = self.inputs()
        self.assertEqual(identity.rebind_key(f.output, project, current, f.source,
                         target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.previous["inputKey"])
        real_copy = builder.prepare_resume.copy_changed
        def only_pending_undo(source, target, **kwargs):
            self.assertTrue("undo-campaign-shaders" in Path(source).parts
                            or "undo-campaign-shaders" in Path(target).parts)
            return real_copy(source, target, **kwargs)
        with patch.object(builder.prepare_resume, "copy_changed", side_effect=only_pending_undo):
            with self.assertRaisesRegex(storage.BuildError, "fixture graphics failure"):
                f.run_prepare()
        calls["shaders"] += 1
        self.assertEqual(f.calls, calls)
        self.assertEqual(json.loads(journal.read_text())["inputKey"], current["inputKey"])
        self.assertEqual(library.read_bytes(), b"existing original Unity imports")

    def test_partial_unknown_progress_profile_and_completed_ui_never_receive_transport_alias(self):
        project, journal, _, _ = self.startup_transport_failure()
        f = self.fixture
        baseline = copy.deepcopy(f.inputs)
        for change in ("partial-progress", "unknown-progress", "closed-ui", "wrong-pending"):
            with self.subTest(change=change):
                current, value = copy.deepcopy(baseline), json.loads(journal.read_text())
                if change in ("partial-progress", "unknown-progress"):
                    row = next(row for row in current["mod"]["files"] if row["path"] == "tools/quest-builder/audio.py")
                    row.update(identity.STARTUP_PROGRESS_PREVIOUS[row["path"]] if change == "partial-progress"
                               else {"size": 19, "sha256": "f" * 64})
                    current["mod"]["key"] = storage.value_hash({"files": current["mod"]["files"]})
                    current.pop("inputKey"); current["inputKey"] = storage.value_hash(current)
                    storage.write_json(f.output / "manifests" / (current["inputKey"] + ".json"), current)
                elif change == "closed-ui":
                    value["steps"].append({"name": "ui-recipes", "operation": "startup-content", "outputs": []})
                    value["pending"] = {"name": "startup-ui", "operation": "startup-content", "undo": []}
                else: value["pending"]["name"] = "unreviewed-next-producer"
                storage.write_json(journal, value)
                self.assertIsNone(identity.rebind_key(f.output, project, current, f.source,
                                  target="game", recipe=builder.RECIPE, recovery=recovery_resume))
                if change in ("closed-ui", "wrong-pending"):
                    value["steps"] = value["steps"][:7]
                    value["pending"] = {"name": "ui-recipes", "operation": "startup-content", "undo": []}
                    storage.write_json(journal, value)


if __name__ == "__main__":
    unittest.main()
