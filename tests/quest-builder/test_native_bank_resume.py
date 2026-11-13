"""Closed legacy cooking evidence cannot turn an unrelated cache into a bank."""
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import import_workspace
import native_bank_resume as resume
import native_content_pack
import preparation_identity
import recovery_resume
import storage


class NativeBankResumeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.output = self.root / "build"
        names = ["tools/quest-builder/builder.py", "src/GloomhavenVR/Net/NetProtocol.cs",
                 "tools/QuestWeaver/Program.cs", "tools/quest-procedural-runtime/proton.lock.json",
                 "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs"]
        rows = [storage.record_file(ROOT / name, name) for name in names]
        value = {"schema": 1, "target": "game", "game": {"key": storage.value_hash({"files": []}), "files": [], "unityVersion": "2021.3.5f1"},
                 "profile": {"displayName": "Fixture owner", "steamId": "76561197960265729"},
                 "proceduralBackend": "proton-arm64ec-fex", "recipe": 77,
                 "mod": {"files": rows, "key": storage.value_hash({"files": rows}), "commit": "d" * 40, "modBuild": 672}}
        self.inputs = {**value, "inputKey": storage.value_hash(value)}
        self.project = self.output / "projects" / import_workspace.workspace_key(self.inputs, "game")
        self.project.mkdir(parents=True)
        for name in resume.CONTROLS:
            path = self.project / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('{"fixture":"small compiler/control metadata, not native content"}\n')
        storage.write_json(self.project / resume.CONTROLS[-1],
            {"schema": 1, "target": "Android", "backend": "IL2CPP", "compilation": "Player",
             "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"})
        schedule = [("base-project", "project-files")]
        schedule += [(name, "startup-content") for name in ("post-effects", "loading-resources", "startup-movies",
            "native-sprites", "loading-sprite", "startup-audio", "ui-recipes", "startup-ui", "startup-blur", "dlc-selection", "file-extras")]
        schedule += [("native-runtime", "native-runtime"), ("bundled-audio", "audio")]
        schedule += [(name, "textures") for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")]
        schedule += [(name, "graphics") for name in ("campaign-compute", "campaign-shaders")]
        schedule += [(name, "mod-banks") for name in ("startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")]
        schedule += [(name, "preparation-contracts") for name in ("compiler-contracts", "case-paths", "script-orders", "final-settings")]
        steps = [{"name": name, "operation": operation, "outputs": []} for name, operation in schedule]
        steps[0]["outputs"] = [storage.record_file(self.project / name, name) for name in resume.CONTROLS[:-1]]
        self.journal = self.output / "cache/prepare-resume" / self.project.name / "journal.json"
        storage.write_json(self.journal, {"schema": 1, "owner": "Quest preparation substage journal",
            "project": self.project.relative_to(self.output).as_posix(), "pending": None, "steps": steps})
        self.source = self.output / "inputs/mod" / self.inputs["mod"]["key"]
        for name in names:
            path = self.source / name; path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, path)
        storage.write_json(self.output / "manifests" / (self.inputs["inputKey"] + ".json"), self.inputs)
        self.tools = {"unityVersion": "2021.3.5f1", "editorSha256": "e" * 64,
                      "ndkPropertiesSha256": "f" * 64, "key": "9" * 64, "buildDriverSha256": rows[0]["sha256"]}
        self.provenance = {"schema": 1, "runtime": {"inputKey": self.inputs["inputKey"],
                           "sourceCommit": "d" * 40, "modBuild": 672}, "toolchain": self.tools}
        for field, selected in (("buildDriverModules", rows[:1]), ("stagedEditorSources", rows[-1:])):
            self.provenance[field] = [{**row, "path": row["path"].removeprefix("unity/GloomhavenVR.Quest/"),
                "snapshotPath": row["path"], "snapshotSha256": row["sha256"], "changedFromSnapshot": False}
                for row in selected]
        self.key = storage.value_hash({"input": self.inputs["inputKey"], "toolchain": self.tools["key"],
                                      "recipe": 77, "buildProvenance": storage.value_hash(self.provenance),
                                      "validateCampaignShaders": False})
        self.folder = self.output / "builds" / self.key
        storage.write_json(self.folder / "build-provenance.json", self.provenance)
        self.log = self.output / "logs" / ("unity-build-" + self.key[:12] + ".log")
        self.log.parent.mkdir(parents=True)
        self.completed = ("Addressable content successfully built (duration : 1:09:50.641)\n"
                          "InvalidDataException: Native content packer failed (1): Native content packer stopped: No module named 'storage'\n"
                          "executeMethod method GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build threw exception.\n")
        self.log.write_text(self.completed)

    def adopt(self, *, inputs=None, tools=None, project=None):
        return resume.legacy_adoption(self.output, project or self.project, inputs or self.inputs,
                                     self.source, tools or self.tools, preparation_identity, recovery_resume, recipe=77)

    def test_exact_completed_invocation_qualifies_without_reading_native_banks(self):
        observed = self.adopt()
        self.assertEqual(observed["previousBuildKey"], self.key)
        self.assertEqual(observed["previousInputKey"], self.inputs["inputKey"])
        self.assertFalse(observed["hardwareVerified"])
        self.assertEqual(observed["compilerKey"], storage.value_hash(resume.identity(
            self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)))

    def test_available_directory_or_unrelated_successful_log_cannot_authorize_adoption(self):
        for text in ("Addressable content successfully built (duration : 1:09:50.641)\n",
                     self.completed.replace("No module named 'storage'", "unknown native compiler error"),
                     self.completed.replace("Addressable content successfully built", "Addressable content failed"),
                     self.completed + "Shader error in retained bank\n"):
            self.log.write_text(text)
            self.assertIsNone(self.adopt())
        self.log.write_text(self.completed)
        self.folder.rename(self.output / "builds" / ("0" * 64))
        self.assertIsNone(self.adopt())

    def test_game_profile_runtime_code_or_compiler_drift_prevents_adoption(self):
        for field in ("game", "profile", "runtime"):
            changed = copy.deepcopy(self.inputs)
            if field == "game": changed["game"]["key"] = "0" * 64
            elif field == "profile": changed["profile"]["displayName"] = "different owner"
            else: changed["mod"]["files"][1]["sha256"] = "0" * 64
            with self.subTest(field=field): self.assertIsNone(self.adopt(inputs=changed))
        self.assertIsNone(self.adopt(tools={**self.tools, "editorSha256": "0" * 64}))

    def test_frozen_builder_tamper_and_runtime_provenance_drift_are_rejected(self):
        path = self.source / "tools/quest-builder/builder.py"
        path.write_bytes(path.read_bytes() + b"\n# changed after source qualification\n")
        self.assertIsNone(self.adopt())
        shutil.copyfile(ROOT / "tools/quest-builder/builder.py", path)
        broken = copy.deepcopy(self.provenance); broken["runtime"]["sourceCommit"] = "0" * 40
        storage.write_json(self.folder / "build-provenance.json", broken)
        self.assertIsNone(self.adopt())

    def test_actual_compiler_source_drift_missing_rows_or_unknown_provenance_cannot_adopt(self):
        for field in ("buildDriverModules", "stagedEditorSources"):
            for mutation in ("missing", "changed", "sha256", "snapshotSha256", "path"):
                broken = copy.deepcopy(self.provenance)
                if mutation == "missing": broken[field] = []
                elif mutation == "changed": broken[field][0]["changedFromSnapshot"] = True
                else: broken[field][0][mutation] = "unexpected actual producer"
                # Even a valid folder hash of arbitrary provenance is insufficient.
                key = storage.value_hash({"input": self.inputs["inputKey"], "toolchain": self.tools["key"],
                    "recipe": 77, "buildProvenance": storage.value_hash(broken), "validateCampaignShaders": False})
                self.folder.rename(self.output / "builds" / key); self.folder = self.output / "builds" / key
                storage.write_json(self.folder / "build-provenance.json", broken)
                (self.output / "logs" / ("unity-build-" + key[:12] + ".log")).write_text(self.completed)
                with self.subTest(field=field, mutation=mutation): self.assertIsNone(self.adopt())

    def test_small_native_controls_change_the_modern_compiler_key(self):
        before = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        path = self.project / resume.CONTROLS[0]
        path.write_text('{"fixture":"changed actual catalog associations"}\n')
        after = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        self.assertNotEqual(storage.value_hash(before), storage.value_hash(after))
        self.assertIsNone(self.adopt())

    def test_admissible_controls_keep_the_exact_preceding_identity_contract(self):
        previous = {"schema": 1, "scope": "quest-native-bank-cooking-inputs",
            "preparation": storage.value_hash(preparation_identity._scope(
                self.inputs, self.source, recovery_resume, original_prefix=True, completed=True)),
            "runtimeSources": resume._runtime_rows(self.inputs, preparation_identity),
            "controls": [storage.record_file(self.project / name, name) for name in resume.CONTROLS],
            "target": "Android", "graphicsApi": "Vulkan",
            "compiler": {name: self.tools[name] for name in (
                "unityVersion", "editorSha256", "ndkPropertiesSha256")}}
        actual = resume.identity(self.inputs, self.source, self.project, self.tools,
                                 preparation_identity, recovery_resume)
        self.assertEqual(actual, previous)
        self.assertEqual(storage.value_hash(actual), storage.value_hash(previous))

    def test_real_large_shader_manifest_streams_and_keeps_modern_checkpoint(self):
        path = self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json"
        size = 74621329  # Exact legitimate control size in capture20261013T191013.
        opening = b'{"schema":1,"shaders":[]}'
        block = b" " * (1024 * 1024)
        expected = hashlib.sha256(opening)
        with path.open("wb") as stream:
            stream.write(opening)
            remaining = size - len(opening)
            while remaining:
                chunk = block[:min(remaining, len(block))]
                stream.write(chunk); expected.update(chunk); remaining -= len(chunk)
        with patch.object(resume, "_read", side_effect=AssertionError("Controls must not be parsed as JSON")):
            selected = resume.identity(self.inputs, self.source, self.project, self.tools,
                                       preparation_identity, recovery_resume)
        row = next(row for row in selected["controls"] if row["path"] == path.relative_to(self.project).as_posix())
        self.assertEqual(row["size"], size)
        self.assertEqual(row["sha256"], expected.hexdigest())
        compiler_key = storage.value_hash(selected)
        receipt = {"schema": 1, "scope": "closed-native-addressables", "compilerCompleted": True,
                   "owner": {"compilerKey": compiler_key, "projectRoot": str(self.project.resolve())}, "files": []}
        receipt["checkpointSha256"] = native_content_pack.checkpoint_hash(receipt)
        storage.write_json(self.project / native_content_pack.NATIVE_CHECKPOINT, receipt)
        real_open = Path.open
        def guarded_open(candidate, *args, **kwargs):
            if candidate == path:
                raise AssertionError("Unchanged large control already has an exact native-stamp byte proof")
            return real_open(candidate, *args, **kwargs)
        with patch.object(Path, "open", guarded_open), patch.object(resume, "legacy_adoption",
                side_effect=AssertionError("Modern checkpoint must bypass the legacy journal")):
            result = resume.environment({}, self.inputs, self.source, self.project, self.output, self.tools,
                preparation_identity, recovery_resume, recipe=77, progress=SimpleNamespace(event=Mock()))
        self.assertEqual(result["GHVR_QUEST_NATIVE_BANK_KEY"], compiler_key)
        self.assertNotIn("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", result)

    def test_missing_or_non_file_control_is_not_reported_as_a_size_failure(self):
        path = self.project / resume.CONTROLS[1]
        path.unlink()
        with self.assertRaisesRegex(storage.BuildError, "control is missing: .*campaign-shaders.json"):
            resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        path.mkdir()
        with self.assertRaisesRegex(storage.BuildError, "control is not a regular file: .*campaign-shaders.json"):
            resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)

    def test_same_size_restored_mtime_rewrite_cannot_keep_a_control_proof(self):
        path = self.project / resume.CONTROLS[0]
        before = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        stamp = path.stat(); original = path.read_bytes()
        path.write_bytes(original.replace(b"small", b"other"))
        os.utime(path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        after = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        self.assertEqual(path.stat().st_size, stamp.st_size)
        self.assertNotEqual(storage.value_hash(before), storage.value_hash(after))

    def test_unknown_native_stamps_reread_controls(self):
        path = self.project / resume.CONTROLS[1]
        real_open = Path.open; reads = []
        def observed_open(candidate, *args, **kwargs):
            if candidate == path and args and args[0] == "rb": reads.append(candidate)
            return real_open(candidate, *args, **kwargs)
        with patch.object(storage, "_file_witness_stamp", return_value=None), patch.object(Path, "open", observed_open):
            before = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
            after = resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)
        self.assertEqual(len(reads), 2)
        self.assertEqual(before, after)

    def test_control_read_permission_failure_remains_a_real_filesystem_error(self):
        path = self.project / resume.CONTROLS[1]
        real_record = resume.record_file
        def denied(candidate, name, *args, **kwargs):
            if candidate == path: raise PermissionError("Fixture denied control read")
            return real_record(candidate, name, *args, **kwargs)
        with patch.object(resume, "record_file", denied), self.assertRaisesRegex(PermissionError, "denied control read"):
            resume.identity(self.inputs, self.source, self.project, self.tools, preparation_identity, recovery_resume)

    def test_incomplete_preparation_or_unknown_sdk_cannot_adopt_legacy_outputs(self):
        self.journal.unlink()
        self.assertIsNone(self.adopt())

    def test_arbitrary_inherited_adoption_environment_is_cleared(self):
        progress = SimpleNamespace(event=Mock())
        env = resume.environment({"GHVR_QUEST_NATIVE_BANK_KEY": "untrusted", "GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "1"},
            self.inputs, self.source, self.project, self.output, self.tools,
            preparation_identity, recovery_resume, recipe=77, progress=progress)
        self.assertRegex(env["GHVR_QUEST_NATIVE_BANK_KEY"], r"^[0-9a-f]{64}$")
        self.assertEqual(env["GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT"], "1")
        self.log.write_text("unfinished native build\n")
        rejected = resume.environment(env, self.inputs, self.source, self.project, self.output, self.tools,
            preparation_identity, recovery_resume, recipe=77, progress=progress)
        self.assertNotIn("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", rejected)

    def test_modern_checkpoint_bypasses_legacy_journal_without_authorizing_unproven_bank(self):
        compiler_key = storage.value_hash(resume.identity(self.inputs, self.source, self.project, self.tools,
                                                         preparation_identity, recovery_resume))
        receipt = {"schema": 1, "scope": "closed-native-addressables", "compilerCompleted": True,
                   "owner": {"compilerKey": compiler_key, "projectRoot": str(self.project.resolve())},
                   "files": [], "fixtureLabel": "Ünicode"}
        receipt["checkpointSha256"] = native_content_pack.checkpoint_hash(receipt)
        path = self.project / native_content_pack.NATIVE_CHECKPOINT
        storage.write_json(path, receipt)
        progress = SimpleNamespace(event=Mock())
        with patch.object(resume, "legacy_adoption", side_effect=AssertionError("No legacy journal scan needed")):
            result = resume.environment({}, self.inputs, self.source, self.project, self.output, self.tools,
                                        preparation_identity, recovery_resume, recipe=77, progress=progress)
        self.assertEqual(result["GHVR_QUEST_NATIVE_BANK_KEY"], compiler_key)
        self.assertNotIn("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", result)
        receipt["checkpointSha256"] = "0" * 64; storage.write_json(path, receipt)
        with patch.object(resume, "legacy_adoption", return_value=None) as legacy:
            resume.environment({}, self.inputs, self.source, self.project, self.output, self.tools,
                               preparation_identity, recovery_resume, recipe=77, progress=progress)
        legacy.assert_called_once()

    def test_malformed_optional_checkpoints_fall_back_without_inheriting_adoption(self):
        path = self.project / native_content_pack.NATIVE_CHECKPOINT
        path.parent.mkdir(parents=True, exist_ok=True)
        invalid = [b"not JSON", b"[]", b"null", b'{"owner":[]}', b'{"owner":"invalid"}',
                   b'{"schema":1,"scope":"closed-native-addressables","compilerCompleted":true,"owner":[]}',
                   b'[' * 2048 + b']' * 2048, b'"\xff"']
        # A correct checksum cannot turn a malformed owner into valid metadata.
        for owner in ([], "invalid", None):
            receipt = {"schema": 1, "scope": "closed-native-addressables",
                       "compilerCompleted": True, "owner": owner}
            receipt["checkpointSha256"] = native_content_pack.checkpoint_hash(receipt)
            invalid.append(storage.canonical(receipt))
        for raw in invalid:
            path.write_bytes(raw)
            with self.subTest(receipt=raw[:80]), patch.object(resume, "legacy_adoption", return_value=None) as legacy:
                result = resume.environment({"GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "1"},
                    self.inputs, self.source, self.project, self.output, self.tools,
                    preparation_identity, recovery_resume, recipe=77, progress=SimpleNamespace(event=Mock()))
                self.assertNotIn("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", result)
                legacy.assert_called_once()
        with path.open("wb") as stream: stream.truncate(resume.NATIVE_RECEIPT_BYTES + 1)
        with patch.object(resume, "legacy_adoption", return_value=None) as legacy:
            result = resume.environment({}, self.inputs, self.source, self.project, self.output, self.tools,
                preparation_identity, recovery_resume, recipe=77, progress=SimpleNamespace(event=Mock()))
        self.assertNotIn("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", result)
        legacy.assert_called_once()

    def test_modern_receipt_uses_the_isolated_reader_metadata_bound(self):
        compiler_key = storage.value_hash(resume.identity(self.inputs, self.source, self.project, self.tools,
            preparation_identity, recovery_resume))
        receipt = {"schema": 1, "scope": "closed-native-addressables", "compilerCompleted": True,
            "owner": {"compilerKey": compiler_key, "projectRoot": str(self.project.resolve())},
            "files": [], "fixtureLabel": "x" * (4 * 1024 * 1024)}
        receipt["checkpointSha256"] = native_content_pack.checkpoint_hash(receipt)
        path = self.project / native_content_pack.NATIVE_CHECKPOINT
        storage.write_json(path, receipt)
        self.assertGreater(path.stat().st_size, 4 * 1024 * 1024)
        with patch.object(resume, "legacy_adoption", side_effect=AssertionError("Known modern receipt needs no legacy scan")):
            result = resume.environment({}, self.inputs, self.source, self.project, self.output, self.tools,
                preparation_identity, recovery_resume, recipe=77, progress=SimpleNamespace(event=Mock()))
        self.assertEqual(result["GHVR_QUEST_NATIVE_BANK_KEY"], compiler_key)

    def test_malformed_optional_legacy_evidence_cannot_authorize_adoption(self):
        broken_toolchain = copy.deepcopy(self.provenance); broken_toolchain["toolchain"] = []
        broken_runtime = copy.deepcopy(self.provenance); broken_runtime["runtime"] = []
        for value in ([], broken_toolchain, broken_runtime):
            storage.write_json(self.folder / "build-provenance.json", value)
            with self.subTest(provenance=value): self.assertIsNone(self.adopt())
        journal = json.loads(self.journal.read_text())
        journal["steps"][0]["outputs"] = ["invalid row"]
        storage.write_json(self.journal, journal)
        self.assertIsNone(self.adopt())

    def test_unreadable_or_racing_optional_build_folders_fall_back(self):
        real_iterdir, real_stat = Path.iterdir, Path.stat
        candidates = self.output / "builds"
        def denied(candidate):
            if candidate == candidates: raise PermissionError("Fixture denied old build list")
            return real_iterdir(candidate)
        with patch.object(Path, "iterdir", denied): self.assertIsNone(self.adopt())
        def raced(candidate, *args, **kwargs):
            if candidate == self.folder and kwargs.get("follow_symlinks", True):
                raise FileNotFoundError("Fixture old folder removed during inspection")
            return real_stat(candidate, *args, **kwargs)
        with patch.object(Path, "stat", raced): self.assertIsNone(self.adopt())


if __name__ == "__main__": unittest.main()
