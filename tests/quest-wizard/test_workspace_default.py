"""Closed real-shaped workspace inputs, without opening game/APK payloads."""
import contextlib
import copy
import http.client
import io
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
import qualification
import server
import state
import wizard


class WorkspaceDefaultTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.store = state.Store(self.root / ".ghvrq")
        self.build = self.store.root / "build"
        self.key = "a" * 64
        self.input_key = "b" * 64
        self.apk_name = "builds/" + self.key + "/GloomhavenVR-Quest.apk"
        self.receipt_name = "receipts/build/" + self.key + ".json"
        self.apk = self.build / self.apk_name
        self.apk.parent.mkdir(parents=True)
        self.apk.write_bytes(b"Default selection must leave these payload bytes unopened.")
        provenance = {"schema": 1, "runtime": {"inputKey": self.input_key, "modBuild": 625,
            "sourceCommit": "e" * 40}, "stagedEditorSources": [{"path": "Editor/Build.cs", "sha256": "f" * 64}],
            "campaignShaderManifest": {"qualifiedSource": "x" * 120000}}
        # B625's actual closed full-game shape has no top-level inputKey or
        # modBuild. Both copies of its provenance make latest/receipt >64KiB.
        self.details = {"apkSha256": "c" * 64, "isDiagnostic": False,
            "package": "dev.gloomhavenvr.quest", "buildProvenance": provenance,
            "buildReport": {"schema": 1, "buildResult": "Succeeded", "target": "game",
                "inputKey": self.input_key, "package": "dev.gloomhavenvr.quest", "buildProvenance": provenance},
            "installationContract": {"schema": 1, "inputKey": self.input_key, "gameFileCount": 42},
            "contentFiles": [{"path": "builds/" + self.key + "/GloomhavenVR-Quest-content.zip",
                "sha256": "d" * 64, "size": 10912755026}]}
        self.publish(self.details)
        self.selected = {"path": str(self.apk), "modBuild": 625, "source": "latest-build"}

    def publish(self, details):
        receipt = {"schema": 1, "key": self.key, "stage": "build", "details": details,
            "outputs": [{"path": self.apk_name, "size": self.apk.stat().st_size, "sha256": details["apkSha256"]}]}
        state.atomic_json(self.build / self.receipt_name, receipt)
        state.atomic_json(self.build / "latest-build.json", {"schema": 1,
            "apk": self.apk_name, "receipt": self.receipt_name, "details": details})

    @contextlib.contextmanager
    def metadata_only(self):
        allowed = {self.build / "latest-build.json", self.build / self.receipt_name}
        original = Path.open
        opened = []
        def bounded(path, *args, **kwargs):
            self.assertIn(path, allowed, "Default selection must not read APK, bank, witness DB or arbitrary input manifests.")
            opened.append(path)
            return original(path, *args, **kwargs)
        with patch.object(Path, "open", bounded), patch.object(Path, "glob", side_effect=AssertionError("No directory scan")):
            yield opened

    def test_actual_closed_full_build_shape_is_selected_using_only_two_bounded_metadata_reads(self):
        for path in (self.build / "latest-build.json", self.build / self.receipt_name):
            self.assertGreater(path.stat().st_size, 65536)
        with self.metadata_only() as opened:
            self.assertEqual(discovery.default_apk(self.store), self.selected)
        self.assertEqual(opened, [self.build / "latest-build.json", self.build / self.receipt_name])

    def test_modern_top_level_build_and_update_identities_remain_supported(self):
        self.publish({"package": "dev.gloomhavenvr.quest", "inputKey": self.input_key,
            "apkSha256": "c" * 64, "isDiagnostic": False, "modBuild": 671})
        with self.metadata_only():
            self.assertEqual(discovery.default_apk(self.store), {**self.selected, "modBuild": 671})

    def test_conflicting_or_incomplete_original_identity_never_becomes_a_default(self):
        for mutation in (lambda d: d.update(inputKey="f" * 64),
                         lambda d: d.update(modBuild=671),
                         lambda d: d["buildReport"].update(inputKey="f" * 64),
                         lambda d: d["buildReport"].update(buildResult="Failed"),
                         lambda d: d["buildReport"].update(target="startup"),
                         lambda d: d["buildReport"].update(package="other.package"),
                         lambda d: d["buildProvenance"]["runtime"].update(modBuild=0),
                         lambda d: d["installationContract"].update(inputKey="f" * 64)):
            with self.subTest(mutation=mutation):
                details = copy.deepcopy(self.details)
                mutation(details)
                self.publish(details)
                self.assertIsNone(discovery.default_apk(self.store))

    def test_plan_freezes_workspace_source_for_both_update_modes_when_input_is_omitted_or_blank(self):
        engine = wizard.Engine(self.store)
        for mode in ("update-mod", "update-profile"):
            for value in ({}, {"baseApk": ""}, {"baseApk": "  "}):
                with self.subTest(mode=mode, value=value):
                    selected = engine.plan({"gameRoot": str(self.root / "Owned PC"), "mode": mode, **value})
                    self.assertEqual(selected["choices"]["baseApk"], str(self.apk))
                    self.assertEqual(selected["choices"]["mode"], mode)
                    self.assertEqual(selected["status"], "ready")
        prior = selected["session"]
        (self.build / "latest-build.json").unlink()
        self.assertEqual(self.store.load(prior)["choices"]["baseApk"], str(self.apk))

    def test_explicit_apk_zip_and_missing_paths_remain_the_selected_override(self):
        for name in ("manual.apk", "manual.zip", "missing.apk"):
            path = self.root / name
            if name != "missing.apk": path.write_bytes(b"explicit local source")
            with patch.object(discovery, "default_apk", side_effect=AssertionError("Never replace explicit input")):
                result = wizard.Engine(self.store).plan({"gameRoot": str(self.root / "Owned PC"),
                    "mode": "update-mod", "baseApk": str(path)})
            self.assertEqual(result["choices"]["baseApk"], str(path))

    def test_invalid_override_or_missing_game_never_infers_a_default(self):
        for override in (None, [], 1, "bad\0path"):
            with self.subTest(override=override), self.assertRaises(state.WizardError), \
                 patch.object(discovery, "default_apk", side_effect=AssertionError("Do not infer malformed input")):
                wizard.Engine(self.store).plan({"gameRoot": str(self.root / "Owned PC"),
                    "mode": "update-profile", "baseApk": override})
        for game in (None, "", "  "):
            with self.subTest(game=game), self.assertRaises(state.WizardError) as raised:
                wizard.Engine(self.store).plan({"gameRoot": game, "mode": "update-mod"})
            self.assertEqual(raised.exception.code, "game_required")

    def test_no_closed_default_fails_before_creating_session_or_starting_tools(self):
        (self.build / self.receipt_name).unlink()
        with self.assertRaises(state.WizardError) as raised, patch.object(self.store, "create", side_effect=AssertionError("No invalid session")):
            wizard.Engine(self.store).plan({"gameRoot": str(self.root / "Owned PC"), "mode": "update-mod"})
        self.assertEqual(raised.exception.code, "base_apk_required")
        self.assertIn("Arbeitsordner", raised.exception.message["de"])

    def test_http_plan_and_qualification_resolve_same_workspace_source_and_preserve_override(self):
        local = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui", promotional=False)
        thread = threading.Thread(target=local.serve_forever, daemon=True)
        thread.start()
        def request(route, body):
            connection = http.client.HTTPConnection(*local.server_address, timeout=5)
            connection.request("POST", route, json.dumps(body), {"X-Quest-Token": local.token,
                "Origin": local.origin, "Content-Type": "application/json"})
            response = connection.getresponse()
            code, result = response.status, json.loads(response.read())
            connection.close()
            return code, result
        try:
            game = str(self.root / "Owned PC")
            for mode in ("update-mod", "update-profile"):
                code, result = request("/api/plan", {"choices": {"gameRoot": game, "mode": mode}})
                self.assertEqual(code, 200, result)
                self.assertEqual(result["state"]["choices"]["baseApk"], str(self.apk))
                with patch.object(qualification, "qualify", return_value={"schema": 1, "event": "qualified"}) as qualify:
                    code, result = request("/api/qualify", {"gameRoot": game, "mode": mode, "baseApk": ""})
                self.assertEqual(code, 200, result)
                self.assertEqual(qualify.call_args.kwargs["base_apk"], str(self.apk))
                self.assertEqual(qualify.call_args.kwargs["game_root"], game)
            explicit = str(self.root / "other.zip")
            with patch.object(qualification, "qualify", return_value={"schema": 1}) as qualify:
                code, result = request("/api/qualify", {"gameRoot": game, "mode": "update-mod", "baseApk": explicit})
            self.assertEqual(code, 200, result)
            self.assertEqual(qualify.call_args.kwargs["base_apk"], explicit)
            (self.build / self.receipt_name).unlink()
            for route, body in (("/api/plan", {"choices": {"gameRoot": game, "mode": "update-mod"}}),
                                ("/api/qualify", {"gameRoot": game, "mode": "update-profile"})):
                code, result = request(route, body)
                self.assertEqual(code, 400, result)
                self.assertEqual(result["code"], "base_apk_required")
        finally:
            local.shutdown()
            local.close_owned()
            thread.join(5)
        self.assertFalse(thread.is_alive())

    def test_cli_plan_uses_the_same_completed_workspace_default(self):
        choice_path = self.root / "choices.json"
        choice_path.write_text(json.dumps({"gameRoot": str(self.root / "Owned PC"), "mode": "update-profile"}))
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            code = wizard.main(["plan", "--state-root", str(self.store.root), "--choices-file", str(choice_path)])
        self.assertEqual(code, 0, capture.getvalue())
        self.assertEqual(json.loads(capture.getvalue())["state"]["choices"]["baseApk"], str(self.apk))


if __name__ == "__main__": unittest.main()
