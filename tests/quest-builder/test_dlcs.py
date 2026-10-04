"""Local ownership, native-query cleanup and generated content selection controls."""
from pathlib import Path
import json
import sys
import tempfile
import types
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import dlcs
from storage import BuildError


class Function:
    def __init__(self, call): self.call = call
    def __call__(self, *args): return self.call(*args)


class Sdk:
    def __init__(self, owned=(1809490, 1958560), installed=(1809490, 1958560), app=780290, initialize=True):
        self.closed = 0
        self.SteamAPI_Init = Function(lambda: initialize)
        self.SteamAPI_Shutdown = Function(self.close)
        self.SteamAPI_SteamApps_v008 = Function(lambda: 1)
        self.SteamAPI_SteamUser_v021 = Function(lambda: 2)
        self.SteamAPI_SteamUtils_v010 = Function(lambda: 3)
        self.SteamAPI_ISteamUser_GetSteamID = Function(lambda user: 76561198000000000)
        self.SteamAPI_ISteamUtils_GetAppID = Function(lambda utils: app)
        self.SteamAPI_ISteamApps_BIsSubscribedApp = Function(lambda apps, id: id in (780290, *owned))
        self.SteamAPI_ISteamApps_BIsDlcInstalled = Function(lambda apps, id: id in installed)
    def close(self): self.closed += 1


class DlcTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
    def test_owned_jotl_solo_capture_has_no_skin_grant_and_shuts_down(self):
        sdk = Sdk()
        value = dlcs.native_query(self.root / "unused.dll", sdk)
        self.assertEqual(value["ownedMask"], 3)
        self.assertEqual(value["installedAppIds"], [1809490, 1958560])
        self.assertEqual([row["owned"] for row in value["dlcs"]], [True, True, False])
        self.assertEqual(sdk.closed, 1)
    def test_uninstalled_owned_or_inconsistent_or_wrong_app_fails_and_shuts_down(self):
        for sdk in (Sdk(installed=()), Sdk(owned=(), installed=(1809490,)), Sdk(app=480)):
            with self.assertRaises(BuildError): dlcs.native_query(self.root / "unused.dll", sdk)
            self.assertEqual(sdk.closed, 1)
    def test_uninitialized_sdk_does_not_claim_ownership(self):
        sdk = Sdk(initialize=False)
        with self.assertRaises(BuildError): dlcs.native_query(self.root / "unused.dll", sdk)
        self.assertEqual(sdk.closed, 0)
    def test_missing_sdk_interface_is_rejected_before_init(self):
        sdk = Sdk()
        del sdk.SteamAPI_ISteamApps_BIsDlcInstalled
        with self.assertRaises(BuildError): dlcs.native_query(self.root / "unused.dll", sdk)
        self.assertEqual(sdk.closed, 0)
    def test_invalid_declared_ids_are_not_silently_granted(self):
        for value in ([1809490, 1809490], [780290], [True], [2584171], None, {"token": "fixture"}):
            with self.assertRaises(BuildError): dlcs.manifest("0", value, "test")
    def test_portable_declaration_matches_selected_account_and_rejects_extra_fields(self):
        path = self.root / "ownership.json"
        valid = {"schema": 1, "provider": "steam", "appId": 780290, "steamId": "0", "installedAppIds": [1958560]}
        path.write_text(json.dumps(valid))
        self.assertEqual(dlcs.declaration(path, "0")["ownedMask"], 2)
        for value in ({**valid, "steamId": "1"}, {**valid, "appId": 480}, {**valid, "token": "fixture"}):
            path.write_text(json.dumps(value))
            with self.assertRaises(BuildError): dlcs.declaration(path, "0")
    def test_dummy_defaults_to_base_and_explicit_dlc_declaration_is_labelled(self):
        args = types.SimpleNamespace()
        profile = {"isDummy": True, "steamId": "0"}
        self.assertEqual(dlcs.capture(args, self.root, profile)["ownedMask"], 0)
        args.owned_dlc = ["jotl", "solo"]
        self.assertEqual(dlcs.capture(args, self.root, profile)["source"], "maintainer-declared-test-ownership")
        with self.assertRaises(BuildError): dlcs.capture(args, self.root, {"steamId": "1"})
    def test_generated_unowned_rules_removed_owned_rules_and_all_ads_preserved(self):
        project = self.root / "generated"
        for row in dlcs.CATALOG[:2]:
            directory = project / "Assets/StreamingAssets/Rulebase/DLC" / row[4]
            directory.mkdir(parents=True)
            (directory / (row[4] + "_Global.ruleset")).write_bytes(b"original rules")
        ad = project / "Assets/Resources/Promotion.asset"
        ad.parent.mkdir(parents=True)
        ad.write_bytes(b"original promotion references")
        receipt = dlcs.stage(project, dlcs.manifest("0", [1809490], "test"))
        self.assertEqual(len(receipt["removedUnavailableRuleFiles"]), 1)
        self.assertTrue((project / "Assets/StreamingAssets/Rulebase/DLC/DLC_JoTL/DLC_JoTL_Global.ruleset").is_file())
        self.assertFalse((project / "Assets/StreamingAssets/Rulebase/DLC/DLC_Solo").exists())
        self.assertEqual(ad.read_bytes(), b"original promotion references")
    def test_owned_missing_from_selected_copy_stops_build(self):
        with self.assertRaises(BuildError): dlcs.stage(self.root, dlcs.manifest("0", [1809490], "test"))
    def test_native_query_uses_isolated_temporary_context_and_checks_account(self):
        args = types.SimpleNamespace()
        library = self.root / "Plugins/x86_64/steam_api64.dll"
        library.parent.mkdir(parents=True)
        library.write_bytes(b"fixture SDK, never executed")
        def run(argv, **kwargs):
            context = Path(kwargs["cwd"])
            self.assertEqual((context / "steam_appid.txt").read_text(), "780290")
            self.assertFalse(kwargs["shell"])
            self.assertEqual(kwargs["timeout"], 30)
            self.assertFalse((self.root / "steam_appid.txt").exists())
            output = Path(argv[-1])
            output.write_text(json.dumps(dlcs.manifest("76561198000000000", [1809490, 1958560], "steam-local-client")))
            return types.SimpleNamespace(returncode=0, stderr="")
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=run):
            profile = {"steamId": "76561198000000000"}
            value = dlcs.capture(args, self.root, profile)
            self.assertEqual(value["ownedMask"], 3)
            self.assertIn("librarySha256", value)
            with self.assertRaises(BuildError): dlcs.capture(args, self.root, {"steamId": "76561198000000001"})


if __name__ == "__main__": unittest.main()
