"""Actual fixed-path journal recovery; no Player/import or large payload I/O."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import shutil

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import storage


class PlayerExclusionRecovery(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.output = Path(self.temp.name)
        self.project = self.output / "projects" / ("a" * 64); self.key = "b" * 64
        storage.project_generation_paths(self.output, self.project, self.key)
        self.archive = self.project / storage.CONTENT_PATHS[0]; self.write(self.archive, b"owned archive entry bytes")
        self.meta = Path(str(self.archive) + ".meta"); self.write(self.meta, b"owned archive importer guid")
        self.manifest = self.project / storage.CONTENT_PATHS[1]
        self.publish_manifest(self.key)
        self.native = self.project / "Library/com.unity.addressables/aa/Android"
        self.link = self.native / "AddressablesLink/link.xml"; self.write(self.link, b"<linker>original native type closure</linker>")
        self.write(self.native / "Android/original.bundle", b"owned native bank bytes")
        self.stable = self.project / "Assets/Quest/CampaignLink/link.xml"; self.write(self.stable, self.link.read_bytes())
        self.journal = self.project / "QuestCampaignEvidence/excluded-payload/journal.json"
        self.value = {"schema": 1, "scope": "quest-campaign-player-content-exclusion", "inputKey": self.key, "state": "planned",
                      "nativeLink": {"source": "Library/com.unity.addressables/aa/Android/AddressablesLink/link.xml", "projectPath": "Assets/Quest/CampaignLink/link.xml", "sha256": storage.digest(self.link), "previousSha256": None},
                      "moves": [{"kind": "file", "source": storage.CONTENT_PATHS[0], "temporary": "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip", "sha256": storage.digest(self.archive), "size": self.archive.stat().st_size},
                                {"kind": "file", "source": storage.CONTENT_PATHS[0] + ".meta", "temporary": "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip.meta", "sha256": storage.digest(self.meta), "size": self.meta.stat().st_size},
                                {"kind": "directory", "source": "Library/com.unity.addressables/aa/Android", "temporary": "QuestCampaignEvidence/excluded-payload/native-addressables-Android", "sha256": storage.digest(self.link), "size": 0}]}

    def tearDown(self): self.temp.cleanup()
    @staticmethod
    def write(path, value): path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(value)
    def publish_manifest(self, key): storage.write_json(self.manifest, {"schema": 1, "inputKey": key, "archive": self.archive.name, "archiveSha256": storage.digest(self.archive)})
    def save(self): storage.write_json(self.journal, self.value)
    def held(self, index): return self.project / self.value["moves"][index]["temporary"]
    def exclude(self, count=3):
        self.save()
        for row in self.value["moves"][:count]:
            target = self.project / row["temporary"]; target.parent.mkdir(parents=True, exist_ok=True); os.replace(self.project / row["source"], target)
        if count == len(self.value["moves"]): self.value["state"] = "excluded"; self.save()
    def recover(self): storage.recover_project_content(self.output, self.project, self.key)
    def assert_unmoved(self): self.assertFalse(self.archive.exists()); self.assertTrue(self.held(0).exists())

    def test_without_outer_transaction_native_directory_zip_and_meta_restore(self):
        self.exclude(); self.recover()
        self.assertEqual(self.archive.read_bytes(), b"owned archive entry bytes")
        self.assertEqual(self.meta.read_bytes(), b"owned archive importer guid")
        self.assertEqual((self.native / "Android/original.bundle").read_bytes(), b"owned native bank bytes")
        self.assertEqual(json.loads(self.journal.read_text())["state"], "restored")
        before = self.journal.read_bytes(); self.recover(); self.assertEqual(self.journal.read_bytes(), before)

    def test_partial_planned_moves_and_missing_stable_copy_recover(self):
        self.value["nativeLink"]["previousSha256"] = ""
        self.exclude(1); self.stable.unlink(); self.recover()
        self.assertTrue(self.archive.exists()); self.assertTrue(self.native.exists()); self.assertFalse(self.stable.exists())

    def test_valid_two_move_journal_without_zip_meta(self):
        self.meta.unlink(); self.value["moves"].pop(1); self.exclude(2); self.recover()
        self.assertTrue(self.archive.exists()); self.assertTrue(self.native.exists()); self.assertFalse(self.meta.exists())

    def test_outer_rollback_restores_native_directory_before_previous_archive_epoch(self):
        old = self.archive.read_bytes()
        with self.assertRaises(KeyboardInterrupt):
            with storage.project_content_transaction(self.output, self.project, self.key):
                changed = self.archive.with_name("next.zip"); changed.write_bytes(b"new repacked native archive")
                os.replace(changed, self.archive)
                self.publish_manifest(self.key)
                self.value["moves"][0].update(size=self.archive.stat().st_size, sha256=storage.digest(self.archive))
                self.exclude(); raise KeyboardInterrupt()
        self.recover(); self.assertEqual(self.archive.read_bytes(), old); self.assertTrue(self.native.exists())
        self.recover(); self.assertEqual(self.archive.read_bytes(), old)

    def test_restored_owner_survives_legitimate_new_profile_native_link_and_sizes(self):
        self.exclude(); self.recover()
        self.archive.write_bytes(b"legitimate next profile/current native payload of different size")
        self.publish_manifest("c" * 64); self.link.write_bytes(b"new real native closure")
        self.meta.write_bytes(b"new legitimate importer metadata")
        storage.recover_project_content(self.output, self.project, "c" * 64)
        self.assertEqual(self.archive.read_bytes(), b"legitimate next profile/current native payload of different size")
        self.assertEqual(self.link.read_bytes(), b"new real native closure")

    def test_restored_owner_allows_retry_during_an_unfinished_native_rebuild(self):
        self.exclude(); self.recover(); self.link.unlink(); self.recover(); self.assertTrue(self.native.exists())

    def test_restored_owner_allows_missing_recreated_native_directory_and_meta(self):
        self.exclude(); self.recover(); shutil.rmtree(self.native); self.meta.unlink(); self.recover()
        self.assertFalse(self.native.exists()); self.assertFalse(self.meta.exists()); self.assertTrue(self.archive.exists())

    def test_restored_owner_does_not_block_outer_missing_archive_manifest_rollback(self):
        self.exclude(); self.recover()
        with self.assertRaises(KeyboardInterrupt):
            with storage.project_content_transaction(self.output, self.project, self.key):
                self.archive.unlink(); self.manifest.unlink(); raise KeyboardInterrupt()
        self.recover(); self.assertTrue(self.archive.exists()); self.assertTrue(self.manifest.exists())

    def test_native_dto_optional_previous_hash_accepts_null_empty_absent_only(self):
        self.save()
        for value in (None, "", "absent"):
            if value == "absent": self.value["nativeLink"].pop("previousSha256", None)
            else: self.value["nativeLink"]["previousSha256"] = value
            self.save(); self.recover()
        for value in (False, 0, 1):
            self.value["nativeLink"]["previousSha256"] = value; self.save()
            with self.assertRaisesRegex(storage.BuildError, "prior linker"): self.recover()

    def test_new_link_plan_accepts_owned_previous_stable_hash_before_atomic_replace(self):
        previous = storage.digest(self.stable); self.link.write_bytes(b"new native closure")
        current = storage.digest(self.link)
        self.value["nativeLink"].update(sha256=current, previousSha256=previous)
        self.value["moves"][-1]["sha256"] = current
        self.exclude(); self.recover(); self.assertEqual(storage.digest(self.stable), previous)

    def test_late_pair_conflict_preflights_before_any_zip_move(self):
        self.exclude(); self.native.mkdir(parents=True)
        with self.assertRaisesRegex(storage.BuildError, "conflict"): self.recover()
        self.assert_unmoved(); self.assertTrue(self.held(2).exists())

    def test_both_missing_directory_preflights_before_any_zip_move(self):
        self.exclude(); os.replace(self.held(2), self.output / "preserved-away")
        with self.assertRaisesRegex(storage.BuildError, "missing"): self.recover()
        self.assert_unmoved(); self.assertTrue((self.output / "preserved-away").exists())

    def test_changed_native_link_or_unowned_stable_link_reject_before_moves(self):
        self.exclude(); (self.held(2) / "AddressablesLink/link.xml").write_bytes(b"changed outside owned build")
        with self.assertRaisesRegex(storage.BuildError, "linker bytes"): self.recover()
        self.assert_unmoved()
        (self.held(2) / "AddressablesLink/link.xml").write_bytes(self.stable.read_bytes()); self.stable.write_bytes(b"unowned stable XML")
        with self.assertRaisesRegex(storage.BuildError, "stable native linker"): self.recover()
        self.assert_unmoved()

    def test_pending_known_metadata_is_cleaned_but_unrelated_files_survive(self):
        self.exclude()
        pending = Path(str(self.journal) + ".quest-content-pending"); self.write(pending, b"interrupted partial JSON")
        unrelated = self.journal.parent / "unrelated.json"; self.write(unrelated, b"preserve")
        self.recover(); self.assertFalse(pending.exists()); self.assertEqual(unrelated.read_bytes(), b"preserve")

    def test_directory_pending_metadata_rejects_before_restore(self):
        self.exclude(); Path(str(self.journal) + ".quest-content-pending").mkdir()
        with self.assertRaisesRegex(storage.BuildError, "pending path"): self.recover()
        self.assert_unmoved()

    def test_wrong_outstanding_input_and_unsupported_paths_are_not_adopted(self):
        self.exclude(); self.value["inputKey"] = "c" * 64; self.save()
        with self.assertRaisesRegex(storage.BuildError, "current content input"): self.recover()
        self.assert_unmoved(); self.value["inputKey"] = self.key
        self.value["moves"][-1]["temporary"] = "../outside"; self.save()
        with self.assertRaisesRegex(storage.BuildError, "unsupported move"): self.recover()
        self.assert_unmoved()

    @unittest.skipIf(os.name == "nt", "Requires fixture symlink permission")
    def test_nested_native_symlink_rejects_before_any_restore(self):
        self.exclude(); (self.held(2) / "outside.bundle").symlink_to(self.manifest)
        with self.assertRaisesRegex(storage.BuildError, "links"): self.recover()
        self.assert_unmoved()


if __name__ == "__main__": unittest.main()
