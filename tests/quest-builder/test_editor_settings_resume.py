"""Retain the real Editor-owned PlayerSettings without weakening original assets."""
import copy
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import import_workspace
import prepare_resume as resume
import storage
from test_post_effect_import_resume import SCHEDULE

SETTINGS = (b'%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n'
            b'  serializedVersion: 23\n  companyName: Original PC\n  m_ActiveColorSpace: 0\n'
            b'  m_BuildTargetGraphicsAPIs: []\n  activeInputHandler: 0\n')
PATH = "ProjectSettings/ProjectSettings.asset"


class EditorSettingsResumeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-editor-settings-")
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)
        self.project = self.output / "projects" / ("b" * 64)
        self.put(PATH, SETTINGS)
        import_workspace.stage(self.project, "game")
        first = self.open()
        for name, operation in SCHEDULE:
            paths = ["QuestFixture/closed-" + name]
            self.put(paths[0], name.encode())
            if name == "final-settings":
                paths = resume.manifest_contracts(self.project, [import_workspace.RECEIPT])
            first.value["steps"].append({"name": name, "operation": operation,
                                        "outputs": [first._observe(path) for path in paths]})
        storage.write_json(first.journal, first.value)
        self.journal = first.journal
        self.original_journal = copy.deepcopy(first.value)
        first.close()
        self.prepared = (self.project / PATH).read_bytes()
        self.library = self.put("Library/Artifacts/owned-import", b"expensive immutable imports")
        self.imported = self.prepared.replace(b"companyName: Original PC", b"companyName: GloomhavenVR").replace(
            b"activeInputHandler: 0", b"activeInputHandler: 2")

    def put(self, name, raw):
        path = self.project / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return path

    def open(self):
        return resume.Preparation(self.output, self.project, input_key="a" * 64, target="game", recipe=1)

    def test_real_final_settings_contract_tracks_receipt_and_configuration(self):
        self.assertEqual({row["path"] for row in self.original_journal["steps"][-1]["outputs"]},
                         {PATH, import_workspace.RECEIPT})

    def test_actual_editor_configuration_and_subsequent_identity_updates_retain_imports(self):
        self.put(PATH, self.imported)
        second = self.open(); second.close()
        current = json.loads(self.journal.read_text())
        receipt = json.loads((self.project / import_workspace.RECEIPT).read_text())
        self.assertEqual(current["editorSettings"]["preparedSha256"], receipt["sha256"])
        self.assertEqual(current["editorSettings"]["accepted"], storage.record_file(self.project / PATH, PATH))
        self.assertEqual(len(current["steps"]), 27)
        self.put(PATH, self.imported + b"  bundleVersion: 0.1.0.B660.test\n")
        third = self.open(); third.close()
        self.assertEqual(self.library.read_bytes(), b"expensive immutable imports")
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("Warm PlayerSettings need no second payload read")), patch.object(
                resume.Preparation, "_accept_editor_settings", side_effect=AssertionError("Warm settings must not open provenance")):
            fourth = self.open(); fourth.close()

    def test_unchanged_configuration_never_enters_reader(self):
        with patch.object(resume.Preparation, "_accept_editor_settings", side_effect=AssertionError("No changed output")):
            second = self.open(); second.close()

    def test_changed_bootstrap_api_color_or_malformed_settings_are_rejected(self):
        for raw in (self.imported.replace(b"m_APIs: 15000000", b"m_APIs: 0b000000"),
                    self.imported.replace(b"m_ActiveColorSpace: 1", b"m_ActiveColorSpace: 0"),
                    self.imported.replace(b"PlayerSettings:", b"OtherSettings:"),
                    self.imported + b"--- !u!129 &2\nPlayerSettings:\n",
                    self.imported + b"ForeignSettings: true\n",
                    self.imported.replace(b"m_ActiveColorSpace: 1", b"m_ActiveColorSpace: 9")):
            with self.subTest(raw=raw):
                self.put(PATH, raw)
                with self.assertRaisesRegex(storage.BuildError, "audited Editor settings rejected"):
                    self.open()
                self.assertEqual(json.loads(self.journal.read_text()), self.original_journal)

    def test_missing_changed_or_self_declared_receipt_does_not_authorize_configuration(self):
        original_receipt = (self.project / import_workspace.RECEIPT).read_bytes()
        for kind in ("missing", "changed", "contract"):
            with self.subTest(kind=kind):
                storage.write_json(self.journal, self.original_journal)
                path = self.put(import_workspace.RECEIPT, original_receipt)
                self.put(PATH, self.imported)
                if kind == "missing": path.unlink()
                elif kind == "changed": path.write_bytes(original_receipt + b" ")
                else:
                    doc = json.loads(original_receipt); doc["source"] = "foreign settings writer"
                    path.write_text(json.dumps(doc))
                    value = copy.deepcopy(self.original_journal)
                    for row in value["steps"][-1]["outputs"]:
                        if row["path"] == import_workspace.RECEIPT: row.update(storage.record_file(path, row["path"]))
                    storage.write_json(self.journal, value)
                with self.assertRaises(storage.BuildError): self.open()
                self.assertEqual(self.library.read_bytes(), b"expensive immutable imports")

    def test_wrong_first_row_or_durable_marker_is_not_an_original_receipt(self):
        self.put(PATH, self.imported)
        for kind in ("row", "marker"):
            with self.subTest(kind=kind):
                value = copy.deepcopy(self.original_journal)
                if kind == "row":
                    for row in value["steps"][-1]["outputs"]:
                        if row["path"] == PATH: row["sha256"] = "f" * 64
                else: value["editorSettings"] = {"schema": 1, "accepted": {"sha256": "f" * 64}}
                storage.write_json(self.journal, value)
                with self.assertRaisesRegex(storage.BuildError, "audited Editor settings rejected"):
                    self.open()

    def test_incomplete_preparation_or_wrong_latest_owner_cannot_adopt(self):
        self.put(PATH, self.imported)
        for kind in ("incomplete", "owner"):
            with self.subTest(kind=kind):
                value = copy.deepcopy(self.original_journal)
                if kind == "incomplete": value["steps"].pop(0)
                else: value["steps"][-1]["name"] = "foreign-owner"
                storage.write_json(self.journal, value)
                with self.assertRaisesRegex(storage.BuildError, "audited Editor settings rejected"):
                    self.open()

    def test_unrelated_project_settings_still_require_original_bytes(self):
        other = "ProjectSettings/QualitySettings.asset"
        path = self.put(other, b"original quality settings")
        value = copy.deepcopy(self.original_journal)
        value["steps"][-1]["outputs"].append(storage.record_file(path, other))
        storage.write_json(self.journal, value)
        self.put(PATH, self.imported)
        self.put(other, b"foreign quality settings")
        with self.assertRaisesRegex(storage.BuildError, "QualitySettings.asset"):
            self.open()
        self.assertEqual(json.loads(self.journal.read_text()), value)

    def test_linked_configuration_or_mutation_during_closed_read_is_rejected(self):
        self.put(PATH, self.imported)
        path = self.project / PATH
        linked = self.output / "external-settings-owner"
        try: os.link(path, linked)
        except OSError: self.skipTest("Filesystem does not support hard links")
        with self.assertRaisesRegex(storage.BuildError, "missing or linked"):
            self.open()
        linked.unlink()
        actual = import_workspace.patch_settings
        def concurrent(raw, target):
            result = actual(raw, target)
            path.write_bytes(raw + b"  companyName: changed during read\n")
            return result
        with patch.object(import_workspace, "patch_settings", concurrent):
            with self.assertRaisesRegex(storage.BuildError, "changed during the bounded read"):
                self.open()
        self.assertEqual(json.loads(self.journal.read_text()), self.original_journal)

    def test_cut_before_and_after_settings_adoption_keeps_witnesses_and_owner_chain(self):
        self.put(PATH, self.imported)
        actual = resume.write_json
        for after in (False, True):
            with self.subTest(after=after):
                storage.write_json(self.journal, self.original_journal)
                def killed(path, value):
                    if Path(path) == self.journal and value.get("editorSettings"):
                        if after: actual(path, value)
                        raise RuntimeError("settings adoption cut")
                    return actual(path, value)
                with patch.object(resume, "write_json", killed):
                    with self.assertRaisesRegex(RuntimeError, "settings adoption cut"): self.open()
                with patch.dict(storage._invocation_file_proofs, clear=True):
                    if after:
                        with patch.object(resume, "digest", side_effect=AssertionError("Published adoption must be durable")):
                            second = self.open(); second.close()
                    else:
                        second = self.open(); second.close()
                self.assertEqual(len(json.loads(self.journal.read_text())["steps"]), 27)
                self.assertEqual(self.library.read_bytes(), b"expensive immutable imports")


if __name__ == "__main__": unittest.main()
