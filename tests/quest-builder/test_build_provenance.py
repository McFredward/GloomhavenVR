"""Focused controls for truthful mixed-source build provenance, without Unity."""

import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import build_provenance
from storage import BuildError, digest, record_file, value_hash


class BuildProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest provenance & 100% ")
        self.root = Path(self.temp.name)
        self.project, self.source = self.root / "project", self.root / "frozen-source"
        self.driver = self.root / "live/tools/quest-builder"
        self.editor = self.project / "Assets/Quest/Editor"
        self.frozen_editor = self.source / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
        self.frozen_driver = self.source / "tools/quest-builder"
        for folder in (self.editor, self.frozen_editor, self.driver, self.frozen_driver):
            folder.mkdir(parents=True)
        for relative, payload in (("builder.py", b"# original build driver\n"),
                                  ("storage.py", b"# dependency\n")):
            self.write(self.driver / relative, payload)
            self.write(self.frozen_driver / relative, payload)
        for relative, payload in (("QuestBuild.cs", b"// original Editor entry\n"),
                                  ("Nested/QuestOriginalScriptOrders.cs", b"// original script order consumer\n")):
            self.write(self.editor / relative, payload)
            self.write(self.frozen_editor / relative, payload)
        self.inputs = {"inputKey": "a" * 64, "mod": {"commit": "b" * 40, "modBuild": 623},
                       "target": "game", "profile": {"persona": "PRIVATE ACCOUNT", "steamId": "PRIVATE ID"}}
        self.tools = {"unityVersion": "2021.3.5f1", "buildDriverSha256": digest(self.driver / "builder.py"),
                      "key": "c" * 64, "editorSha256": "d" * 64, "javaSha256": "e" * 64,
                      "apksignerSha256": "f" * 64, "ndkPropertiesSha256": "1" * 64,
                      "buildToolsVersion": "30.0.2", "editor": str(self.root / "private/Unity"),
                      "keystorePassword": "PRIVATE SIGNING SECRET", "JAVA_HOME": "PRIVATE ENV"}

    def tearDown(self):
        self.temp.cleanup()

    @staticmethod
    def write(path, payload):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)

    def capture(self, **changes):
        arguments = dict(inputs=self.inputs, project=self.project, source=self.source,
                         driver_dir=self.driver, toolchain=self.tools)
        arguments.update(changes)
        return build_provenance.capture(**arguments)

    def test_unchanged_sources_are_exact_sorted_portable_records(self):
        result = self.capture()
        self.assertEqual(result["schema"], 1)
        self.assertEqual(result["runtime"], {"sourceCommit": "b" * 40, "modBuild": 623, "inputKey": "a" * 64})
        self.assertEqual([row["path"] for row in result["buildDriverModules"]],
                         ["tools/quest-builder/builder.py", "tools/quest-builder/storage.py"])
        self.assertEqual([row["path"] for row in result["stagedEditorSources"]],
                         ["Assets/Quest/Editor/Nested/QuestOriginalScriptOrders.cs", "Assets/Quest/Editor/QuestBuild.cs"])
        for row in result["buildDriverModules"] + result["stagedEditorSources"]:
            self.assertEqual(row["sha256"], row["snapshotSha256"])
            self.assertFalse(row["changedFromSnapshot"])
            self.assertIsInstance(row["size"], int)
            self.assertFalse(Path(row["path"]).is_absolute())
            self.assertTrue((self.source / row["snapshotPath"]).is_file())
        self.assertEqual(result, self.capture())

    def test_actual_editor_override_keeps_the_frozen_runtime_identity(self):
        before = self.capture()
        override = self.editor / "Nested/QuestOriginalScriptOrders.cs"
        self.write(override, b"// corrected full Campaign exclusions\n")
        after = self.capture()
        self.assertEqual(after["runtime"], before["runtime"])
        self.assertNotEqual(value_hash(before), value_hash(after))
        changed = [row for row in after["stagedEditorSources"] if row["changedFromSnapshot"]]
        self.assertEqual(len(changed), 1)
        self.assertEqual(changed[0]["path"], "Assets/Quest/Editor/Nested/QuestOriginalScriptOrders.cs")
        self.assertEqual(changed[0]["sha256"], digest(override))
        self.assertEqual(changed[0]["snapshotSha256"], digest(self.frozen_editor / "Nested/QuestOriginalScriptOrders.cs"))

    def test_actual_driver_dependency_change_invalidates_derivative_identity(self):
        before = self.capture()
        self.write(self.driver / "storage.py", b"# corrected dependency\n")
        after = self.capture()
        self.assertNotEqual(value_hash(before), value_hash(after))
        self.assertEqual(after["runtime"], before["runtime"])
        self.assertFalse(after["buildDriverModules"][0]["changedFromSnapshot"])
        self.assertTrue(after["buildDriverModules"][1]["changedFromSnapshot"])

    def test_new_and_removed_sources_change_identity_without_fabricated_baselines(self):
        before = self.capture()
        self.write(self.driver / "next_module.py", b"# future current builder module\n")
        self.write(self.editor / "New/QuestNextValidation.cs", b"// derived new importer validator\n")
        after = self.capture()
        self.assertNotEqual(value_hash(before), value_hash(after))
        for key in ("buildDriverModules", "stagedEditorSources"):
            new = [row for row in after[key] if row["snapshotSha256"] is None]
            self.assertEqual(len(new), 1)
            self.assertTrue(new[0]["changedFromSnapshot"])
        (self.driver / "storage.py").unlink()
        self.assertNotEqual(value_hash(after), value_hash(self.capture()))

    def test_different_runtime_identity_is_preserved_and_changes_derivative_key(self):
        before = self.capture()
        for field, replacement in (("sourceCommit", "2" * 40), ("modBuild", 624), ("inputKey", "3" * 64)):
            with self.subTest(field=field):
                inputs = copy.deepcopy(self.inputs)
                if field == "inputKey": inputs[field] = replacement
                else: inputs["mod"]["commit" if field == "sourceCommit" else field] = replacement
                after = self.capture(inputs=inputs)
                self.assertEqual(after["runtime"][field], replacement)
                self.assertNotEqual(value_hash(before), value_hash(after))

    def test_no_absolute_paths_profile_environment_or_signing_fields_leak(self):
        result = self.capture()
        serialized = json.dumps(result)
        self.assertNotIn(str(self.root), serialized)
        self.assertNotIn("PRIVATE", serialized)
        self.assertNotIn("profile", serialized)
        self.assertNotIn("JAVA_HOME", serialized)
        self.assertEqual(set(result["toolchain"]), set(build_provenance._TOOL_FIELDS))
        # An unrelated sensitive field cannot invalidate this build input.
        changed = copy.deepcopy(self.tools)
        changed["keystorePassword"] = "ANOTHER PRIVATE SIGNING SECRET"
        self.assertEqual(result, self.capture(toolchain=changed))

    def test_optional_public_tool_fingerprint_changes_derivative_identity(self):
        before = self.capture()
        for field, value in (("editorSha256", "0" * 64), ("javaSha256", "0" * 64),
                             ("apksignerSha256", "0" * 64), ("ndkPropertiesSha256", "0" * 64),
                             ("buildToolsVersion", "31.0.0")):
            with self.subTest(field=field):
                tools = copy.deepcopy(self.tools); tools[field] = value
                self.assertNotEqual(value_hash(before), value_hash(self.capture(toolchain=tools)))
        minimal = {key: self.tools[key] for key in ("unityVersion", "buildDriverSha256", "key")}
        self.assertEqual(set(self.capture(toolchain=minimal)["toolchain"]), set(minimal))

    def test_unknown_missing_or_empty_roots_fail(self):
        for field in ("project", "source", "driver_dir"):
            with self.subTest(root=field):
                with self.assertRaises(BuildError): self.capture(**{field: self.root / "missing"})
                not_directory = self.root / (field + ".txt"); not_directory.write_text("regular file")
                with self.assertRaises(BuildError): self.capture(**{field: not_directory})
        for folder in (self.editor, self.frozen_editor, self.frozen_driver):
            with self.subTest(required=folder.name):
                moved = folder.with_name(folder.name + "-retained"); folder.rename(moved)
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: moved.rename(folder)
        (self.driver / "builder.py").unlink()
        with self.assertRaises(BuildError): self.capture()

    def test_empty_editor_is_not_a_valid_build_source(self):
        shutil.rmtree(self.editor); self.editor.mkdir()
        with self.assertRaises(BuildError): self.capture()

    def test_root_and_nested_source_symlinks_are_rejected(self):
        for path in (self.project, self.source, self.driver, self.editor / "Nested",
                     self.driver / "storage.py", self.frozen_driver / "storage.py",
                     self.frozen_editor / "Nested"):
            with self.subTest(path=path.name):
                moved = path.with_name(path.name + "-retained"); path.rename(moved)
                path.symlink_to(moved, target_is_directory=moved.is_dir())
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: path.unlink(); moved.rename(path)
        broken = self.editor / "Broken"; broken.symlink_to(self.root / "absent", target_is_directory=True)
        with self.assertRaises(BuildError): self.capture()

    def test_wrong_or_mutated_driver_hash_stops_capture(self):
        bad = copy.deepcopy(self.tools); bad["buildDriverSha256"] = "0" * 64
        with self.assertRaisesRegex(BuildError, "recorded toolchain"): self.capture(toolchain=bad)
        self.write(self.driver / "builder.py", b"# changed actual live driver\n")
        with self.assertRaisesRegex(BuildError, "recorded toolchain"): self.capture()

    def test_invalid_runtime_and_tool_fingerprints_fail_without_echoing_secrets(self):
        for mutation in (lambda x: x.pop("inputKey"), lambda x: x.update(inputKey="PRIVATE PATH"),
                         lambda x: x["mod"].update(commit="current HEAD"), lambda x: x["mod"].update(modBuild=True),
                         lambda x: x["mod"].update(modBuild=0)):
            bad = copy.deepcopy(self.inputs); mutation(bad)
            with self.assertRaises(BuildError) as error: self.capture(inputs=bad)
            self.assertNotIn("PRIVATE", str(error.exception))
        for field in build_provenance._TOOL_FIELDS:
            bad = copy.deepcopy(self.tools); bad[field] = "PRIVATE SECRET OR PATH"
            with self.assertRaises(BuildError) as error: self.capture(toolchain=bad)
            self.assertNotIn("PRIVATE", str(error.exception))
        for field in ("unityVersion", "buildDriverSha256", "key"):
            bad = copy.deepcopy(self.tools); bad.pop(field)
            with self.assertRaises(BuildError): self.capture(toolchain=bad)

    def test_source_bytes_modified_after_first_hash_are_rejected(self):
        target = self.driver / "storage.py"
        changed = False
        def concurrent_change(path, relative):
            nonlocal changed
            row = record_file(path, relative)
            if path == target and not changed:
                changed = True
                self.write(target, b"# concurrent dependency edit\n")
            return row
        with patch.object(build_provenance, "record_file", concurrent_change):
            with self.assertRaisesRegex(BuildError, "bytes changed"): self.capture()

    def test_new_file_appearing_during_capture_is_rejected(self):
        original = record_file
        changed = False
        def concurrent_add(path, relative):
            nonlocal changed
            row = original(path, relative)
            if not changed:
                changed = True
                self.write(self.editor / "Unexpected.cs", b"// concurrent new source\n")
            return row
        with patch.object(build_provenance, "record_file", concurrent_add):
            with self.assertRaisesRegex(BuildError, "selection changed"): self.capture()


if __name__ == "__main__":
    unittest.main()
