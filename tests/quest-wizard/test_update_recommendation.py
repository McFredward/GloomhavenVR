"""Homepage update hints read closed metadata, never large payload bytes."""
import copy
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
import state
import update_recommendation as hints


class UpdateRecommendationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / ".ghvrq")
        self.build = self.store.root / "build"
        self.repo = self.root / "builder"
        for name in hints.SOURCE_FILES:
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True); path.write_text("source")
        self.source = {"modBuild": 676, "sourceCommit": "a" * 40}
        self.game = self.root / "Owned PC game"
        self.game.mkdir(); (self.game / "globalgamemanagers").write_bytes(b"owned original")
        def game_data(path):
            if not (path / "globalgamemanagers").is_file(): raise ValueError("PC game missing")
            return path
        self.helper = SimpleNamespace(game_data=game_data)
        self.games = [{"gameRoot": str(self.game)}]
        self.recent = []
        self.input_key = "b" * 64
        self.apk, self.details = self.completed("c" * 64, 675)
        self.selected = discovery.default_apk(self.store)

    def completed(self, key, mod_build, *, base=None):
        relative = ("updates/" if base else "builds/") + key + "/GloomhavenVR-Quest.apk"
        apk = self.build / relative
        apk.parent.mkdir(parents=True, exist_ok=True)
        apk.write_bytes(b"No APK bytes may be read for a homepage hint.")
        details = {"inputKey": self.input_key, "modBuild": mod_build,
                   "package": "dev.gloomhavenvr.quest", "isDiagnostic": False, "apkSha256": "d" * 64}
        rows = [{"path": relative, "sha256": details["apkSha256"], "size": apk.stat().st_size}]
        if base:
            details.update(contentFiles=[], baseApk=str(base), retainedGameContent=True)
        else:
            bank = apk.parent / "GloomhavenVR-Quest-content.zip"
            bank.write_bytes(b"Neither bank bytes nor its directory may be scanned.")
            details["contentFiles"] = [{"path": bank.relative_to(self.build).as_posix(),
                                        "sha256": "e" * 64, "size": bank.stat().st_size}]
            details["installationContract"] = {"schema": 1, "inputKey": self.input_key,
                "gameArchiveSha256": "e" * 64, "modArchiveSha256": "f" * 64,
                "modFileCount": 3, "modEntryBytesVerified": True}
            rows.extend(details["contentFiles"])
        self.publish(apk, details, rows)
        return apk, details

    def publish(self, apk, details, rows=None):
        key = apk.parent.name
        name = apk.relative_to(self.build).as_posix()
        receipt_name = "receipts/build/" + key + ".json"
        if rows is None:
            rows = [{"path": name, "sha256": details["apkSha256"], "size": apk.stat().st_size}]
        state.atomic_json(self.build / receipt_name,
            {"schema": 1, "key": key, "stage": "build", "details": details, "outputs": rows})
        state.atomic_json(self.build / "latest-build.json",
            {"schema": 1, "apk": name, "receipt": receipt_name, "details": details})

    def recommend(self, selected=None):
        return hints.recommend(self.repo, self.store, self.source,
            self.selected if selected is None else selected, self.games, self.recent, self.helper)

    def test_completed_build_has_ready_newer_bundled_runtime(self):
        result = self.recommend()
        self.assertEqual(result["status"], "available")
        self.assertTrue(result["available"]); self.assertTrue(result["inputsReady"])
        self.assertEqual((result["installedBuild"], result["bundledBuild"]), (675, 676))
        self.assertEqual(result["gameRoot"], str(self.game))
        self.assertEqual(result["apkPath"], str(self.apk))
        self.assertEqual(result["missing"], [])
        self.assertEqual(result["validation"], "metadata-only")

    def test_completed_update_follows_owned_base_bank_without_another_copy(self):
        update, _ = self.completed("1" * 64, 675, base=self.apk)
        selected = discovery.default_apk(self.store)
        self.assertEqual(selected["path"], str(update))
        self.assertTrue(self.recommend(selected)["inputsReady"])
        self.assertFalse((update.parent / "GloomhavenVR-Quest-content.zip").exists())

    def retained_chain(self, count):
        versions = [self.apk]
        for index in range(count):
            apk, _ = self.completed(f"{index + 1:064x}", 675, base=versions[-1])
            versions.append(apk)
        return versions

    def test_repeated_updates_keep_highlight_without_opening_payloads(self):
        self.retained_chain(32)
        selected = discovery.default_apk(self.store)
        original = Path.open; opened = []
        def metadata(path, *args, **kwargs):
            self.assertEqual(path.suffix, ".json", "Only bounded JSON may be opened")
            opened.append(path)
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", metadata), \
             patch.object(Path, "glob", side_effect=AssertionError("No directory scan")), \
             patch.object(state, "digest", side_effect=AssertionError("No full hashing")):
            value = self.recommend(selected)
        self.assertTrue(value["inputsReady"])
        self.assertEqual(value["reason"], "local-update-ready")
        self.assertEqual(len(opened), 33, "Each receipt is read once, including the selected one")

    def test_cleaned_previous_apks_do_not_hide_retained_game_content(self):
        versions = self.retained_chain(12)
        for apk in versions[:-1]: apk.unlink()
        selected = discovery.default_apk(self.store)
        self.assertTrue(self.recommend(selected)["inputsReady"])
        bank = self.apk.parent / "GloomhavenVR-Quest-content.zip"
        bank.unlink()
        self.assertEqual(self.recommend(selected)["missing"], ["campaign-content"])

    def test_long_cycle_is_rejected_without_requiring_old_apk_payloads(self):
        versions = self.retained_chain(12)
        receipt = self.build / "receipts/build" / (versions[4].parent.name + ".json")
        value = json.loads(receipt.read_text())
        value["details"]["baseApk"] = str(versions[-1])
        state.atomic_json(receipt, value)
        for apk in versions[:-1]: apk.unlink()
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])

    def test_total_metadata_and_owner_work_remain_bounded(self):
        self.retained_chain(12)
        selected = discovery.default_apk(self.store)
        with patch.object(hints, "MAX_METADATA_BYTES", 1500):
            self.assertFalse(self.recommend(selected)["inputsReady"])
        with patch.object(hints, "MAX_CONTENT_OWNERS", 4):
            self.assertFalse(self.recommend(selected)["inputsReady"])

    def test_missing_or_cross_input_ancestor_receipt_never_claims_ready(self):
        versions = self.retained_chain(12)
        receipt = self.build / "receipts/build" / (versions[4].parent.name + ".json")
        value = json.loads(receipt.read_text()); value["details"]["inputKey"] = "0" * 64
        state.atomic_json(receipt, value)
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])
        receipt.unlink()
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])

    def test_selected_apk_is_still_required_after_ancestor_payload_cleanup(self):
        versions = self.retained_chain(12)
        selected = discovery.default_apk(self.store)
        for apk in versions[:-1]: apk.unlink()
        versions[-1].write_bytes(b"changed selected APK size")
        self.assertFalse(self.recommend(selected)["inputsReady"])

    def test_imported_package_bank_uses_unchanged_descriptor_stamp_only(self):
        imported = self.build / ("imports/portable/" + "1" * 64 + "/GloomhavenVR-Quest.apk")
        imported.parent.mkdir(parents=True); imported.write_bytes(b"retained imported APK")
        package = self.root / "Export with spaces.zip"; package.write_bytes(b"large ZIP is never opened")
        info = package.stat()
        state.atomic_json(imported.parent / "portable-source.json", {"schema": 1,
            "sourcePackage": str(package), "sourceStamp": {"identity": [info.st_dev, info.st_ino,
                info.st_size, info.st_mtime_ns, info.st_ctime_ns]},
            "apk": {"size": imported.stat().st_size, "sha256": "2" * 64},
            "content": {"size": 11000000000, "sha256": "3" * 64, "archive": "quest-startup-content.zip"}})
        update, _ = self.completed("4" * 64, 675, base=imported)
        self.assertTrue(self.recommend(discovery.default_apk(self.store))["inputsReady"])
        package.write_bytes(b"changed source")
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])

    def test_same_build_host_only_commit_never_recommends_runtime_recompile(self):
        self.details["modBuild"] = 676; self.publish(self.apk, self.details)
        self.selected = discovery.default_apk(self.store)
        self.source.update(modBuild=676, sourceCommit="f" * 40)
        with patch.object(hints, "_receipt", side_effect=AssertionError("Current build needs no input lookup")):
            value = self.recommend()
        self.assertEqual(value["status"], "current"); self.assertFalse(value["available"])

    def test_original_full_build_provenance_without_top_level_identity_is_supported(self):
        details = copy.deepcopy(self.details)
        del details["inputKey"]; del details["modBuild"]
        provenance = {"schema": 1, "runtime": {"inputKey": self.input_key, "modBuild": 675}}
        details.update(buildProvenance=provenance, buildReport={"schema": 1,
            "buildResult": "Succeeded", "package": "dev.gloomhavenvr.quest", "target": "game",
            "inputKey": self.input_key, "buildProvenance": provenance})
        self.publish(self.apk, details)
        self.assertTrue(self.recommend(discovery.default_apk(self.store))["inputsReady"])

    def test_discovery_exposes_hint_without_changing_existing_default_apk_contract(self):
        self.helper.discover_steam_root = lambda: None
        with patch.object(discovery, "builder", return_value=self.helper), \
             patch.object(discovery, "unity_paths", return_value=([], [])), \
             patch.object(discovery, "mod_source", return_value=self.source), \
             patch.object(discovery, "recent_sessions", return_value=([{"gameRoot": str(self.game)}], None)):
            value = discovery.discover(self.repo, self.store)
        self.assertEqual(value["defaultApk"], self.selected)
        self.assertTrue(value["updateRecommendation"]["inputsReady"])

    def test_newer_installed_runtime_is_never_downgraded(self):
        self.source["modBuild"] = 674
        value = self.recommend()
        self.assertEqual(value["status"], "newer-installed"); self.assertFalse(value["available"])

    def test_unknown_build_or_no_closed_apk_never_claims_update_available(self):
        self.assertEqual(hints.recommend(self.repo, self.store, self.source, None,
            self.games, [], self.helper)["reason"], "no-completed-apk")
        self.selected["modBuild"] = None
        self.assertEqual(self.recommend()["reason"], "unknown-build")

    def test_missing_or_truncated_bank_keeps_update_available_but_not_ready(self):
        bank = self.apk.parent / "GloomhavenVR-Quest-content.zip"
        for action in (lambda: bank.write_bytes(b"truncated"), lambda: bank.unlink()):
            action(); value = self.recommend()
            self.assertTrue(value["available"]); self.assertFalse(value["inputsReady"])
            self.assertEqual(value["missing"], ["campaign-content"])

    def test_missing_game_does_not_enable_highlight(self):
        (self.game / "globalgamemanagers").unlink()
        value = self.recommend()
        self.assertFalse(value["inputsReady"]); self.assertEqual(value["missing"], ["owned-pc-game"])

    def test_recent_manually_selected_game_works_without_store_discovery(self):
        self.games = []; self.recent = [{"gameRoot": str(self.game)}]
        self.assertTrue(self.recommend()["inputsReady"])

    def test_retained_builder_snapshot_does_not_replace_owned_pc_installation(self):
        snapshot = self.build / "inputs/game" / ("1" * 64)
        snapshot.mkdir(parents=True); (snapshot / "globalgamemanagers").write_bytes(b"copy")
        self.games = []; self.recent = [{"gameRoot": str(snapshot)}]
        self.assertEqual(self.recommend()["missing"], ["owned-pc-game"])

    def test_missing_bundled_update_module_is_reported(self):
        (self.repo / "tools/quest-builder/apk_update.py").unlink()
        self.assertEqual(self.recommend()["missing"], ["bundled-update-source"])

    def test_changed_receipt_build_or_input_identity_never_claims_ready(self):
        original = copy.deepcopy(self.details)
        for details in ({**original, "modBuild": 674},
                        {**original, "inputKey": "0" * 64}):
            self.publish(self.apk, details)
            self.assertFalse(self.recommend()["inputsReady"])

    def test_no_payload_reads_hashes_or_directory_scans_even_for_huge_bank(self):
        bank = self.apk.parent / "GloomhavenVR-Quest-content.zip"
        with bank.open("r+b") as stream: stream.truncate(11 * 1024 ** 3)
        self.details["contentFiles"][0]["size"] = bank.stat().st_size
        self.publish(self.apk, self.details)
        original = Path.open; opened = []
        def metadata(path, *args, **kwargs):
            self.assertEqual(path.suffix, ".json", "Only bounded JSON may be opened")
            opened.append(path)
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", metadata), \
             patch.object(Path, "glob", side_effect=AssertionError("No directory scan")), \
             patch.object(state, "digest", side_effect=AssertionError("No full hashing")):
            self.assertTrue(self.recommend()["inputsReady"])
        self.assertLessEqual(len(opened), 2)

    def test_closed_base_cycles_and_cross_input_receipts_are_bounded(self):
        update, details = self.completed("1" * 64, 675, base=self.apk)
        details["baseApk"] = str(update); self.publish(update, details)
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])
        details["baseApk"] = str(self.apk); details["inputKey"] = "2" * 64
        self.publish(update, details)
        self.assertFalse(self.recommend(discovery.default_apk(self.store))["inputsReady"])

    def test_linked_bank_and_oversized_receipt_are_not_ready(self):
        bank = self.apk.parent / "GloomhavenVR-Quest-content.zip"
        target = self.root / "outside-bank.zip"; bank.rename(target); bank.symlink_to(target)
        self.assertFalse(self.recommend()["inputsReady"])
        receipt = self.build / "receipts/build" / (self.apk.parent.name + ".json")
        receipt.write_bytes(b" " * (2 * 1048576 + 1))
        self.assertFalse(self.recommend()["inputsReady"])


if __name__ == "__main__": unittest.main()
