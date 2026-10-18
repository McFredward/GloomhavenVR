"""Local ownership, native-query cleanup and generated content selection controls."""
from pathlib import Path
import json
import os
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
    def __init__(self, owned=(1809490, 1958560), installed=(1809490, 1958560), app=780290, initialize=True, base_owned=True):
        self.closed = 0
        self.SteamAPI_Init = Function(lambda: initialize)
        self.SteamAPI_Shutdown = Function(self.close)
        self.SteamAPI_SteamApps_v008 = Function(lambda: 1)
        self.SteamAPI_SteamUser_v021 = Function(lambda: 2)
        self.SteamAPI_SteamUtils_v010 = Function(lambda: 3)
        self.SteamAPI_ISteamUser_GetSteamID = Function(lambda user: 76561198000000000)
        self.SteamAPI_ISteamUtils_GetAppID = Function(lambda utils: app)
        self.SteamAPI_ISteamApps_BIsSubscribedApp = Function(lambda apps, id: id in ((780290,) if base_owned else ()) + tuple(owned))
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
        self.assertEqual(dlcs.capture(args, self.root, {"steamId": "1"})["source"], "explicit-local-ownership")
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

    def library(self):
        library = self.root / "Plugins/x86_64/steam_api64.dll"
        library.parent.mkdir(parents=True, exist_ok=True)
        library.write_bytes(b"fixture SDK; never executed")
        return library

    def probe(self, outcomes):
        """Real capture orchestration; replace only the external native SDK process."""
        calls = []
        def run(argv, **kwargs):
            calls.append((argv, kwargs))
            self.assertEqual(kwargs["env"]["SteamAppId"], str(dlcs.APP_ID))
            self.assertEqual(kwargs["env"]["SteamGameId"], str(dlcs.APP_ID))
            self.assertFalse(kwargs["shell"])
            self.assertFalse((self.root / "steam_appid.txt").exists())
            next_result = outcomes[min(len(calls) - 1, len(outcomes) - 1)]
            if isinstance(next_result, dict):
                Path(argv[-1]).write_text(json.dumps(next_result))
                return types.SimpleNamespace(returncode=0, stdout="fixture SDK stdout", stderr="")
            if isinstance(next_result, BaseException):
                raise next_result
            code, message = next_result
            return types.SimpleNamespace(returncode=code, stdout="fixture SDK stdout", stderr=message)
        return run, calls

    def test_native_init_failure_launches_once_retries_same_query_then_validates(self):
        self.library()
        valid = dlcs.manifest("76561198000000000", [1809490], "steam-local-client")
        external, calls = self.probe([(75, dlcs.CLIENT_UNAVAILABLE + " client missing"), (75, dlcs.CLIENT_UNAVAILABLE + " starting"), valid])
        clock = types.SimpleNamespace(value=0.)
        with mock.patch.object(dlcs.sys, "platform", "win32"), \
             mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened, \
             mock.patch.object(dlcs.time, "monotonic", side_effect=lambda: clock.value), \
             mock.patch.object(dlcs.time, "sleep", side_effect=lambda seconds: setattr(clock, "value", clock.value + seconds)), \
             mock.patch.object(dlcs.build_progress, "event") as progress:
            result = dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
        self.assertEqual(result["ownedMask"], 1)
        opened.assert_called_once_with()
        self.assertEqual(len(calls), 3)
        self.assertEqual({str(kwargs["cwd"]) for argv, kwargs in calls}, {str(calls[0][1]["cwd"])})
        self.assertFalse(Path(calls[0][1]["cwd"]).exists())
        self.assertTrue(any("2.0 s elapsed / 90 s timeout" in call.kwargs["detail"] for call in progress.call_args_list))
        self.assertEqual(progress.call_args.kwargs["status"], "complete")
        self.assertFalse(any("--base-only" in argv for argv, kwargs in calls))

    def test_init_deadline_yields_guided_action_without_relaunch_or_claim(self):
        self.library()
        external, calls = self.probe([(75, dlcs.CLIENT_UNAVAILABLE + " not signed in")])
        clock = types.SimpleNamespace(value=0.)
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs, "CLIENT_READY_SECONDS", 4.), \
             mock.patch.object(dlcs.subprocess, "run", side_effect=external), mock.patch.object(dlcs, "_launch_steam") as opened, \
             mock.patch.object(dlcs.time, "monotonic", side_effect=lambda: clock.value), \
             mock.patch.object(dlcs.time, "sleep", side_effect=lambda seconds: setattr(clock, "value", clock.value + seconds)), \
             self.assertRaises(BuildError) as error:
            dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
        opened.assert_called_once_with()
        self.assertEqual(len(calls), 3)
        self.assertIn("[launcher-action:steam]", str(error.exception))
        self.assertIn("Existing build results are retained", str(error.exception))
        self.assertIn("exit=75", str(error.exception))
        self.assertIn("not signed in", str(error.exception))

    def test_successful_sdk_does_not_launch_or_retry(self):
        self.library()
        external, calls = self.probe([dlcs.manifest("76561198000000000", [], "steam-local-client")])
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened:
            self.assertEqual(dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})["ownedMask"], 0)
        self.assertEqual(len(calls), 1)
        opened.assert_not_called()

    def test_license_mismatch_native_failure_and_foreign_account_never_launch(self):
        self.library()
        for outcome in ((1, "no matching Gloomhaven license/context"), (1, "DLC ownership and installation disagree"),
                        (1, "required local DLC interface missing"), (3221225477, "native exception"),
                        (1, dlcs.CLIENT_UNAVAILABLE), dlcs.manifest("76561198000000001", [], "steam-local-client")):
            external, calls = self.probe([outcome])
            with self.subTest(outcome=outcome), mock.patch.object(dlcs.sys, "platform", "win32"), \
                 mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
                 mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(BuildError):
                dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
            opened.assert_not_called()
            self.assertEqual(len(calls), 1)

    def test_launch_success_never_overrides_missing_base_license_or_wrong_account(self):
        self.library()
        for outcome in ((1, "[launcher-action:steam] The active Steam client has no matching Gloomhaven license/context."),
                        dlcs.manifest("76561198000000001", [], "steam-local-client")):
            external, calls = self.probe([(75, dlcs.CLIENT_UNAVAILABLE), outcome])
            with self.subTest(outcome=outcome), mock.patch.object(dlcs.sys, "platform", "win32"), \
                 mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
                 mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(BuildError) as error:
                dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
            self.assertIn("[launcher-action:steam]", str(error.exception))
            self.assertEqual(len(calls), 2)
            opened.assert_called_once_with()

    def test_native_base_only_requires_license_without_touching_dlc_exports(self):
        for owned in (True, False):
            sdk = Sdk(installed=(), base_owned=owned)
            del sdk.SteamAPI_ISteamApps_BIsDlcInstalled
            if owned:
                value = dlcs.native_query(self.root / "unused.dll", sdk, base_only=True)
                self.assertEqual(value["steamId"], "76561198000000000")
                self.assertEqual(value["installedAppIds"], [])
            else:
                with self.assertRaisesRegex(BuildError, "no matching Gloomhaven license/context"):
                    dlcs.native_query(self.root / "unused.dll", sdk, base_only=True)
            self.assertEqual(sdk.closed, 1)

    def test_real_windows_explicit_dlc_selection_still_validates_base_game_and_account(self):
        self.library()
        profile = {"steamId": "76561198000000000"}
        for selection in ([], ["jotl", "solo"]):
            args = types.SimpleNamespace(owned_dlc=selection)
            for outcome in (dlcs.manifest(profile["steamId"], [], "steam-local-client"),
                            dlcs.manifest("76561198000000001", [], "steam-local-client"),
                            (1, "no matching Gloomhaven license/context")):
                external, calls = self.probe([outcome])
                with self.subTest(selection=selection, outcome=outcome), mock.patch.object(dlcs.sys, "platform", "win32"), \
                     mock.patch.object(dlcs.subprocess, "run", side_effect=external), mock.patch.object(dlcs, "_launch_steam") as opened:
                    if isinstance(outcome, dict) and outcome["steamId"] == profile["steamId"]:
                        value = dlcs.capture(args, self.root, profile)
                        self.assertEqual(value["installedAppIds"], [1809490, 1958560] if selection else [])
                        self.assertEqual(value["source"], "explicit-local-ownership")
                    else:
                        with self.assertRaises(BuildError): dlcs.capture(args, self.root, profile)
                self.assertEqual(len(calls), 1)
                self.assertIn("--base-only", calls[0][0])
                opened.assert_not_called()

    def test_explicit_steam_declaration_uses_recoverable_base_only_query(self):
        self.library()
        path = self.root / "declared.json"
        path.write_text(json.dumps({"schema": 1, "provider": "steam", "appId": 780290, "steamId": "76561198000000000", "installedAppIds": [1958560]}))
        external, calls = self.probe([(75, dlcs.CLIENT_UNAVAILABLE), dlcs.manifest("76561198000000000", [], "steam-local-client")])
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened:
            value = dlcs.capture(types.SimpleNamespace(dlc_ownership_json=path), self.root, {"steamId": "76561198000000000"})
        self.assertEqual(value["installedAppIds"], [1958560])
        self.assertEqual(value["source"], "explicit-local-declaration")
        self.assertTrue(all("--base-only" in argv for argv, kwargs in calls))
        opened.assert_called_once_with()

    def test_linux_explicit_and_dummy_windows_do_not_gain_sdk_or_launcher_dependency(self):
        with mock.patch.object(dlcs.subprocess, "run") as native, mock.patch.object(dlcs, "_launch_steam") as opened:
            with mock.patch.object(dlcs.sys, "platform", "linux"):
                self.assertEqual(dlcs.capture(types.SimpleNamespace(owned_dlc=["solo"]), self.root, {"steamId": "1"})["ownedMask"], 2)
            with mock.patch.object(dlcs.sys, "platform", "win32"):
                self.assertEqual(dlcs.capture(types.SimpleNamespace(owned_dlc=[]), self.root, {"steamId": "0", "isDummy": True})["ownedMask"], 0)
                self.assertEqual(dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "0", "isDummy": True})["ownedMask"], 0)
            native.assert_not_called()
            opened.assert_not_called()

    def test_probe_errors_preserve_exit_and_both_output_streams_without_starting_store(self):
        self.library()
        external, calls = self.probe([(17, "precise fixture stderr")])
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(BuildError) as error:
            dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
        self.assertIn("exit=17", str(error.exception))
        self.assertIn("stdout=fixture SDK stdout", str(error.exception))
        self.assertIn("stderr=precise fixture stderr", str(error.exception))
        opened.assert_not_called()

    def test_timeout_after_launcher_request_retains_guided_recovery(self):
        self.library()
        external, calls = self.probe([(75, dlcs.CLIENT_UNAVAILABLE), dlcs.subprocess.TimeoutExpired("native fixture", 30, output="blocked SDK", stderr="fixture detail")])
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(BuildError) as error:
            dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
        self.assertIn("[launcher-action:steam]", str(error.exception))
        self.assertIn("blocked SDK", str(error.exception))
        self.assertIn("fixture detail", str(error.exception))
        opened.assert_called_once_with()

    def test_initial_native_timeout_does_not_guess_client_unavailable(self):
        self.library()
        external, calls = self.probe([dlcs.subprocess.TimeoutExpired("native fixture", 30, output="blocked SDK", stderr="fixture detail")])
        with mock.patch.object(dlcs.sys, "platform", "win32"), mock.patch.object(dlcs.subprocess, "run", side_effect=external), \
             mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(dlcs.SteamProbeTimeout) as error:
            dlcs.capture(types.SimpleNamespace(), self.root, {"steamId": "76561198000000000"})
        self.assertIn("blocked SDK", str(error.exception))
        self.assertEqual(len(calls), 1)
        opened.assert_not_called()

    def test_probe_rejects_missing_invalid_and_stale_success_payloads(self):
        library = self.library()
        directory = self.root / "isolated"
        directory.mkdir()
        output = directory / "dlcs.json"
        for content in (None, "malformed-json", "[]"):
            output.write_text(json.dumps(dlcs.manifest("76561198000000000", [], "steam-local-client")))
            def run(argv, **kwargs):
                self.assertFalse(output.exists())
                if content is not None: output.write_text(content)
                return types.SimpleNamespace(returncode=0, stdout="precise stdout", stderr="precise stderr")
            with self.subTest(content=content), mock.patch.object(dlcs.subprocess, "run", side_effect=run), \
                 mock.patch.object(dlcs, "_launch_steam") as opened, self.assertRaises(BuildError) as error:
                dlcs._probe(library, directory, output, {})
            self.assertIn("exit=0", str(error.exception))
            self.assertIn("precise stdout", str(error.exception))
            self.assertIn("precise stderr", str(error.exception))
            opened.assert_not_called()

    def test_dynamic_launcher_import_works_without_global_module_alias(self):
        import store_launchers
        # _launch_steam loads its own exact sibling; intercept only desktop
        # execution. This catches regressions to a late `import storage` alias.
        executable = self.root / "Steam/steam.exe"
        executable.parent.mkdir()
        executable.write_bytes(b"fixture; not executed")
        with mock.patch.object(dlcs.sys, "platform", "win32"), \
             mock.patch.dict(os.environ, {"ProgramFiles(x86)": str(self.root)}, clear=True), \
             mock.patch.object(os, "startfile", create=True) as opened:
            self.assertTrue(dlcs._launch_steam()["launched"])
        opened.assert_called_once_with(str(executable))


if __name__ == "__main__": unittest.main()
