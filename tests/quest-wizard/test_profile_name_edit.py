"""Name-only APK edits preserve full identity and do not require a compiler."""
import importlib.util
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
from state import Store, WizardError
import wizard


class ProfileNameEditTests(unittest.TestCase):
    def setUp(self):
        path = ROOT / "tests/quest-builder/test_apk_update.py"
        spec = importlib.util.spec_from_file_location("quest_name_apk_fixture", path)
        fixture_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(fixture_module)
        self.fixture = fixture_module.ApkUpdateTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.core = discovery.builder(ROOT).apk_updates
        self.root = self.fixture.root
        self.original = {"schema": 1, "provider": "steam", "steamId": "76561198000000000",
                         "accountId": 39734272, "displayName": "Old name", "source": "explicit-local-profile"}
        self.write_profile(self.original)
        self.store = Store(self.root / "workspace")
        self.logo = self.root / "logo.png"
        self.logo.write_bytes(b"fixture logo; read_logo independently tested")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.root / "owned-PC-game"),
              "mode": "update-profile", "baseApk": str(self.fixture.base), "provider": "steam",
              "profileName": "New player", "steamLogo": str(self.logo)}))
        self.saved["completed"] = {"source": {"details": {"sourceRoot": str(ROOT)}}}
        self.engine = wizard.Engine(self.store)

    def write_profile(self, value):
        self.fixture.values[self.core.PROFILE] = json.dumps(value).encode()
        self.fixture.write_zip(self.fixture.base, self.fixture.values)

    def profile(self):
        module = SimpleNamespace(apk_updates=self.core, read_logo=lambda _: (b"fixture", "f" * 64))
        with self.store.active(self.saved), patch.object(discovery, "builder", return_value=module), \
             patch.object(wizard.provision, "download", side_effect=AssertionError("Name edit has a retained logo")):
            paths, details = self.engine.stage_profile(self.saved, Mock())
        return json.loads(paths[0].read_text()), details

    def test_name_only_edit_preserves_original_steam_id_and_account_number(self):
        value, _ = self.profile()
        self.assertEqual(value, {**self.original, "displayName": "New player"})
        args = self.saved["choices"]
        self.assertNotIn("profile", args)
        self.assertEqual(args["profileName"], "New player")

    def test_epic_name_only_edit_preserves_existing_full_provider_id(self):
        identity = "a" * 32
        validator = discovery.builder(ROOT).load_profile.__globals__["validate_identity"]
        profile = validator({"schema": 1, "provider": "epic", "providerId": identity, "displayName": "Original Epic"})
        self.write_profile(profile)
        self.saved["choices"]["provider"] = "epic"
        value, _ = self.profile()
        self.assertEqual(value["providerId"], identity)
        self.assertEqual(value["accountId"], profile["accountId"])
        self.assertEqual(value["displayName"], "New player")

    def test_wrong_provider_is_visible_before_writing_any_replacement_profile(self):
        self.saved["choices"]["provider"] = "gog"
        with self.assertRaisesRegex(WizardError, "provider differs"):
            self.profile()
        self.assertFalse((self.store.session_dir(self.saved["session"]) / "profile.json").exists())

    def test_editing_a_dummy_identity_cannot_silently_make_it_look_like_a_real_account(self):
        self.write_profile({"schema": 1, "provider": "steam", "steamId": "0", "accountId": 0,
                            "displayName": "DUMMY old", "source": "maintainer-authorized-dummy", "isDummy": True})
        with self.assertRaisesRegex(WizardError, "explicitly labelled"):
            self.profile()
        self.saved["choices"]["profileName"] = "DUMMY new"
        value, _ = self.profile()
        self.assertEqual(value["steamId"], "0")
        self.assertTrue(value["isDummy"])

    def test_name_changes_invalidate_only_profile_and_downstream_receipts(self):
        before = {stage: self.engine.key(self.saved, stage) for stage in ("tools", "source", "unity", "profile")}
        self.saved["choices"]["profileName"] = "Another player"
        after = {stage: self.engine.key(self.saved, stage) for stage in before}
        for stage in ("tools", "source", "unity"): self.assertEqual(before[stage], after[stage])
        self.assertNotEqual(before["profile"], after["profile"])

    def test_invalid_or_conflicting_names_do_not_start_an_update(self):
        selected = self.saved["choices"]
        for name in ("", "\ninvalid", "a" * 257, None):
            with self.assertRaises(WizardError): wizard.choices({**selected, "profileName": name})
        for mode in ("build", "update-mod"):
            with self.assertRaises(WizardError): wizard.choices({**selected, "mode": mode})
        with self.assertRaises(WizardError): wizard.choices({**selected, "profile": self.original})


if __name__ == "__main__": unittest.main()
