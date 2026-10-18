"""Owned incomplete Campaign delivery recovery; real bounded filesystem fixtures."""

import argparse
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parents[2] / "tools/quest-builder"
sys.path.insert(0, str(HERE))
import builder
import storage


class DeliveryRestartTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.output = Path(self.temp.name) / "output"
        self.output.mkdir()
        self.provenance = {"schema": 1, "runtime": {"sourceCommit": "a" * 40, "modBuild": 623, "inputKey": "b" * 64},
                           "toolchain": {"key": "c" * 64}, "drivers": [{"path": "tools/quest-builder/builder.py", "sha256": "d" * 64}]}
        self.key = storage.value_hash({"input": "b" * 64, "toolchain": "c" * 64, "recipe": builder.RECIPE,
                                       "buildProvenance": storage.value_hash(self.provenance), "validateCampaignShaders": False})
        self.folder = self.output / "builds" / self.key
        self.folder.mkdir(parents=True)
        self.prior = self.folder / "build-provenance.json"
        storage.write_json(self.prior, self.provenance)
        self.pending = self.folder / "GloomhavenVR-Quest-content.zip.quest-content-pending"
        self.pending.write_bytes(b"incomplete owned Campaign copy")

    def tearDown(self):
        self.temp.cleanup()

    def recover(self, provenance=None):
        with storage.output_lock(self.output):
            return builder.recover_delivery_pending(self.output, self.key, self.provenance if provenance is None else provenance)

    def test_exact_recovery_preserves_complete_content_and_unrelated_pending_files(self):
        finalized = self.folder / "GloomhavenVR-Quest-content.zip"
        finalized.write_bytes(b"previous complete validated content")
        unrelated = self.folder / "unknown.quest-content-pending"
        unrelated.write_bytes(b"must remain")
        prior_bytes = self.prior.read_bytes()
        self.assertTrue(self.recover())
        self.assertFalse(self.pending.exists())
        self.assertEqual(finalized.read_bytes(), b"previous complete validated content")
        self.assertEqual(unrelated.read_bytes(), b"must remain")
        self.assertEqual(self.prior.read_bytes(), prior_bytes)
        self.assertFalse(self.recover())

    def test_real_hard_death_reacquires_output_guard_before_retry(self):
        self.pending.unlink()
        code = "import os,sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from storage import output_lock;\nwith output_lock(Path(sys.argv[2])):\n Path(sys.argv[3]).write_bytes(b'killed delivery copy');os._exit(0)"
        subprocess.run([sys.executable, "-I", "-B", "-c", code, str(HERE), str(self.output), str(self.pending)], check=True)
        self.assertTrue((self.output / ".builder.lock").exists())
        self.assertTrue(self.recover())
        self.assertFalse(self.pending.exists())
        self.assertFalse((self.output / ".builder.lock").exists())

    def test_missing_malformed_or_different_provenance_never_deletes_pending(self):
        original = self.prior.read_bytes()
        different = copy.deepcopy(self.provenance)
        different["runtime"]["inputKey"] = "f" * 64
        driver_changed = copy.deepcopy(self.provenance)
        driver_changed["drivers"][0]["sha256"] = "e" * 64
        wrong_type = copy.deepcopy(self.provenance)
        wrong_type["schema"] = True
        for raw in (None, b"not JSON", json.dumps(different).encode(), json.dumps(driver_changed).encode(), json.dumps(wrong_type).encode()):
            with self.subTest(raw=raw):
                if raw is None:
                    self.prior.unlink(missing_ok=True)
                else:
                    self.prior.write_bytes(raw)
                with self.assertRaisesRegex(storage.BuildError, "retain the pending"):
                    self.recover()
                self.assertEqual(self.pending.read_bytes(), b"incomplete owned Campaign copy")
        self.prior.write_bytes(original)

    def test_output_without_current_lock_cannot_authorize_removal(self):
        with self.assertRaisesRegex(storage.BuildError, "retain the pending"):
            builder.recover_delivery_pending(self.output, self.key, self.provenance)
        self.assertTrue(self.pending.exists())
        storage.write_json(self.output / ".builder.lock", {"schema": 1, "lock": "kernel-guard", "pid": -1, "nonce": "a" * 32})
        with self.assertRaisesRegex(storage.BuildError, "retain the pending"):
            builder.recover_delivery_pending(self.output, self.key, self.provenance)
        self.assertTrue(self.pending.exists())

    def test_conflicting_file_types_are_retained(self):
        self.pending.unlink()
        self.pending.mkdir()
        with self.assertRaises(storage.BuildError):
            self.recover()
        self.assertTrue(self.pending.is_dir())
        self.pending.rmdir()
        self.pending.write_bytes(b"owned partial")
        finalized = self.folder / "GloomhavenVR-Quest-content.zip"
        finalized.mkdir()
        with self.assertRaises(storage.BuildError):
            self.recover()
        self.assertEqual(self.pending.read_bytes(), b"owned partial")
        self.assertTrue(finalized.is_dir())

    @unittest.skipIf(os.name == "nt", "Requires local symlink fixture permission")
    def test_linked_pending_provenance_build_folder_and_finalized_bank_are_rejected(self):
        original = self.pending.read_bytes()
        external = self.output.parent / "external"
        external.write_bytes(b"foreign bytes")
        for path in (self.pending, self.prior, self.folder / "GloomhavenVR-Quest-content.zip"):
            with self.subTest(path=path.name):
                backup = path.read_bytes() if path.exists() else None
                path.unlink(missing_ok=True)
                path.symlink_to(external)
                with self.assertRaisesRegex(storage.BuildError, "links"):
                    self.recover()
                self.assertTrue(path.is_symlink())
                self.assertEqual(external.read_bytes(), b"foreign bytes")
                path.unlink()
                if backup is not None:
                    path.write_bytes(backup)
        renamed = self.folder.with_name("foreign-folder")
        self.folder.rename(renamed)
        self.folder.symlink_to(renamed, target_is_directory=True)
        with self.assertRaisesRegex(storage.BuildError, "links"):
            self.recover()
        self.assertEqual((renamed / self.pending.name).read_bytes(), original)

    def test_shared_or_changing_pending_file_is_not_removed(self):
        second = self.folder / "shared-partial"
        os.link(self.pending, second)
        with self.assertRaises(storage.BuildError):
            self.recover()
        self.assertTrue(self.pending.exists())
        second.unlink()
        actual_loads = builder.json.loads
        def changing(raw):
            result = actual_loads(raw)
            if result == self.provenance:
                with self.pending.open("ab") as stream:
                    stream.write(b"still being written")
            return result
        with patch.object(builder.json, "loads", side_effect=changing), self.assertRaises(storage.BuildError):
            self.recover()
        self.assertTrue(self.pending.exists())

    def test_actual_player_entry_checks_prior_before_rewriting_or_launching_child(self):
        args = argparse.Namespace(target="probe", validate_campaign_shaders=False)
        inputs = {"inputKey": "b" * 64}
        source = self.output / "source"
        project = self.output / "project"
        stale = copy.deepcopy(self.provenance)
        stale["runtime"]["sourceCommit"] = "f" * 40
        storage.write_json(self.prior, stale)
        prior_bytes = self.prior.read_bytes()
        apk = self.folder / "GloomhavenVR-Quest.apk"
        apk.write_bytes(b"prior APK is still reviewable")
        with storage.output_lock(self.output), patch.object(builder, "toolchain", return_value={"key": "c" * 64}), \
                patch.object(builder, "weave"), patch.object(builder, "signing", return_value=(Path("unused"), {})), \
                patch.object(builder.build_provenance, "capture", return_value=self.provenance), \
                patch.object(builder, "command", side_effect=AssertionError("must not launch a child")):
            with self.assertRaisesRegex(storage.BuildError, "retain the pending"):
                builder.build(args, inputs, self.output, source, self.output / "game", project)
        self.assertEqual(self.prior.read_bytes(), prior_bytes)
        self.assertEqual(apk.read_bytes(), b"prior APK is still reviewable")
        self.assertTrue(self.pending.exists())
        self.assertFalse(storage.Stages(self.output).path("build", self.key).exists())


if __name__ == "__main__":
    unittest.main()
