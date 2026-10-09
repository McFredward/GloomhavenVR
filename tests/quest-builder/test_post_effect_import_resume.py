"""Resume only audited Editor upgrades; preserve all completed owners and Library.

The journal/witness implementation is real. Small synthetic shader pairs replace
the private copyrighted source fingerprints in this portable protocol fixture;
native identities, receipt shape and negative qualification controls stay real.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import preparation_identity
import prepare_resume as resume
import shaders
import storage

SCHEDULE = [
    ("base-project", "project-files"),
    *[(name, "startup-content") for name in (
        "post-effects", "loading-resources", "startup-movies", "native-sprites", "loading-sprite",
        "startup-audio", "ui-recipes", "startup-ui", "startup-blur", "dlc-selection", "file-extras")],
    ("native-runtime", "native-runtime"), ("bundled-audio", "audio"),
    *[(name, "textures") for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")],
    *[(name, "graphics") for name in ("campaign-compute", "campaign-shaders")],
    *[(name, "mod-banks") for name in (
        "startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")],
    *[(name, "preparation-contracts") for name in (
        "compiler-contracts", "case-paths", "script-orders", "final-settings")],
]


class PostEffectImportResumeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-import-upgrade-")
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name)
        self.project = self.output / "projects" / ("b" * 64)
        self.original, self.upgraded = {}, {}
        pins = copy.deepcopy(shaders.SHADERS)
        for name, spec in pins.items():
            self.original[name] = ("original official " + name + "\n").encode()
            self.upgraded[name] = ("exact audited Unity upgrade " + name + "\n").encode()
            spec.update(sourceBytes=len(self.original[name]),
                        sourceSha256=hashlib.sha256(self.original[name]).hexdigest(),
                        importUpgradeSha256=hashlib.sha256(self.upgraded[name]).hexdigest())
        self.pin_patch = patch.object(shaders, "SHADERS", pins)
        self.pin_patch.start(); self.addCleanup(self.pin_patch.stop)
        self.seed()

    def put(self, relative, raw):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return path

    @staticmethod
    def shader_path(name): return "Assets/Shader/Hidden_" + name + ".shader"

    def seed(self):
        first = self.open()
        entries, effect_paths = [], []
        for name, spec in shaders.SHADERS.items():
            relative = self.shader_path(name)
            self.put(relative, self.original[name])
            metadata = ("fileFormatVersion: 2\nguid: " + spec["guid"] + "\nShaderImporter:\n  userData:\n").encode()
            self.put(relative + ".meta", metadata)
            entries.append(shaders._entry(name, spec, metadata))
            effect_paths.extend((relative, relative + ".meta"))
        receipt = {"schema": 1, "target": "startup", "changeset": shaders.CHANGESET,
                   "installerSha256": shaders.SOURCE_SHA256,
                   "effectsPackageSha256": shaders.PACKAGE_SHA256, "shaders": entries}
        self.put(shaders.RECEIPT, (json.dumps(receipt) + "\n").encode())
        self.value = first.value
        for name, operation in SCHEDULE:
            sentinel = "QuestFixture/closed-" + name
            self.put(sentinel, ("owned " + name).encode())
            paths = [sentinel]
            if name == "post-effects": paths += effect_paths + [shaders.RECEIPT]
            if name == "case-paths": paths += effect_paths
            self.value["steps"].append({"name": name, "operation": operation,
                                       "outputs": [first._observe(path) for path in paths]})
        storage.write_json(first.journal, self.value)
        self.journal = first.journal
        first.close()
        self.library = self.put("Library/imported-artifact", b"retain expensive real imports")
        self.unchanged = {path: (path.read_bytes(), path.stat().st_mtime_ns)
                          for path in [self.library, self.project / shaders.RECEIPT,
                              *[self.project / (self.shader_path(name) + ".meta") for name in shaders.SHADERS]]}
        self.assertTrue(preparation_identity._completed_game_preparation(self.value, "game"))

    def open(self, **kwargs):
        return resume.Preparation(self.output, self.project, input_key="a" * 64,
                                  target="game", recipe=1, **kwargs)

    def import_sources(self):
        for name in shaders.SHADERS: self.put(self.shader_path(name), self.upgraded[name])

    def assert_retained(self):
        current = json.loads(self.journal.read_text())
        self.assertEqual([(step["name"], step["operation"]) for step in current["steps"]], SCHEDULE)
        self.assertEqual(current["pending"], None)
        for name in shaders.SHADERS:
            path = self.shader_path(name)
            latest = [row for step in current["steps"] for row in step["outputs"] if row["path"] == path][-1]
            self.assertEqual(latest, storage.record_file(self.project / path, path))
        for path, expected in self.unchanged.items():
            self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), expected)

    def test_three_exact_upgrades_retain_all_owners_and_warm_restart_reads_no_payload(self):
        self.import_sources()
        second = self.open(); second.close(); self.assert_retained()
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("Warm upgraded files must retain their byte witnesses")):
            third = self.open(); third.close()
        self.assert_retained()

    def test_unchanged_originals_do_not_enter_upgrade_reader(self):
        with patch.object(resume.Preparation, "_accept_post_effect_import", side_effect=AssertionError("No changed row")):
            second = self.open(); second.close()
        self.assert_retained()

    def test_partial_native_import_adopts_only_the_completed_upgrade(self):
        name = "BlendForBloom"
        self.put(self.shader_path(name), self.upgraded[name])
        second = self.open(); second.close(); self.assert_retained()
        for other in ("BrightPassFilter2", "BlurAndFlares"):
            self.assertEqual((self.project / self.shader_path(other)).read_bytes(), self.original[other])

    def test_unknown_shader_edit_is_rejected_without_rewriting_source(self):
        self.import_sources()
        path = self.put(self.shader_path("BlendForBloom"), b"unreviewed arbitrary source")
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed in case-paths"):
            self.open()
        self.assertEqual(path.read_bytes(), b"unreviewed arbitrary source")
        self.assertEqual(json.loads(self.journal.read_text())["steps"], self.value["steps"])

    def test_another_shader_cannot_use_the_post_effect_upgrade_exception(self):
        relative = "Assets/Shader/Unrelated.shader"
        path = self.put(relative, b"owned other shader")
        value = json.loads(self.journal.read_text())
        value["steps"][24]["outputs"].append(storage.record_file(path, relative))
        storage.write_json(self.journal, value); path.write_bytes(self.upgraded["BlendForBloom"])
        with self.assertRaisesRegex(storage.BuildError, "Unrelated.shader"):
            self.open()

    def test_changed_meta_guid_or_source_receipt_is_rejected(self):
        for kind in ("meta", "receipt"):
            with self.subTest(kind=kind):
                self.import_sources()
                relative = self.shader_path("BlendForBloom") + ".meta" if kind == "meta" else shaders.RECEIPT
                path = self.project / relative; before = path.read_bytes()
                path.write_bytes(before + b"changed provenance")
                with self.assertRaises(storage.BuildError): self.open()
                path.write_bytes(before)

    def test_wrong_latest_owner_or_incomplete_schedule_cannot_adopt(self):
        self.import_sources()
        for kind in ("owner", "incomplete"):
            with self.subTest(kind=kind):
                value = copy.deepcopy(self.value)
                if kind == "owner": value["steps"][24]["name"] = "unknown-owner"
                else: value["steps"].pop()
                storage.write_json(self.journal, value)
                with self.assertRaises(storage.BuildError): self.open()

    def test_original_row_hash_cannot_be_self_declared(self):
        self.import_sources()
        value = copy.deepcopy(self.value)
        for row in value["steps"][24]["outputs"]:
            if row["path"] == self.shader_path("BlendForBloom"): row["sha256"] = "f" * 64
        storage.write_json(self.journal, value)
        with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
            self.open()

    def test_duplicate_receipt_target_and_wrong_guid_are_rejected_even_with_new_receipt_hash(self):
        self.import_sources()
        receipt_path = self.project / shaders.RECEIPT
        original = json.loads(receipt_path.read_text())
        for kind in ("duplicate", "guid"):
            with self.subTest(kind=kind):
                receipt = copy.deepcopy(original)
                if kind == "duplicate": receipt["shaders"].append(copy.deepcopy(receipt["shaders"][0]))
                else: receipt["shaders"][0]["guid"] = "f" * 32
                receipt_path.write_text(json.dumps(receipt))
                value = copy.deepcopy(self.value)
                for row in value["steps"][1]["outputs"]:
                    if row["path"] == shaders.RECEIPT:
                        row.update(storage.record_file(receipt_path, shaders.RECEIPT))
                storage.write_json(self.journal, value)
                with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
                    self.open()

    def test_linked_shader_does_not_acquire_editor_upgrade_ownership(self):
        self.import_sources()
        path = self.project / self.shader_path("BlendForBloom")
        linked = self.output / "external-shader-owner"
        try: os.link(path, linked)
        except OSError: self.skipTest("Filesystem cannot create a hard link")
        with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
            self.open()
        self.assertEqual(linked.read_bytes(), self.upgraded["BlendForBloom"])

    def test_cut_before_or_after_adoption_publication_retains_work(self):
        self.import_sources()
        actual = resume.write_json
        for after in (False, True):
            with self.subTest(after=after):
                storage.write_json(self.journal, copy.deepcopy(self.value))
                def killed(path, value):
                    adopted = any(row.get("sha256") == shaders.SHADERS["BlendForBloom"]["importUpgradeSha256"]
                                  for step in value.get("steps", []) for row in step["outputs"])
                    if Path(path) == self.journal and adopted:
                        if after: actual(path, value)
                        raise RuntimeError("fixture adoption cut")
                    return actual(path, value)
                with patch.object(resume, "write_json", killed):
                    with self.assertRaisesRegex(RuntimeError, "fixture adoption cut"): self.open()
                with patch.dict(storage._invocation_file_proofs, clear=True):
                    if after:
                        with patch.object(resume, "digest", side_effect=AssertionError("Published adoption must retain byte witnesses")):
                            resumed = self.open(); resumed.close()
                    else:
                        resumed = self.open(); resumed.close()
                self.assert_retained()


if __name__ == "__main__": unittest.main()
