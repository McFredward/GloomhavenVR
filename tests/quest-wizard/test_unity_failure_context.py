"""Current Editor causes survive wrappers without importing unrelated log tails."""
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from failures import tool_failure
from state import Store, atomic_json
import wizard


SPRITE_CAUSE = ("InvalidOperationException: Original sprite GUID does not match its imported asset: "
                "Assets/Sprite/DLC_Promo_JawsOfTheLion.asset")


class UnityFailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "owned"
        Store(self.root)
        self.started = time.time() - 1
        self.log = self.root / "sessions/current/logs/build.log"
        self.log.parent.mkdir(parents=True)
        self.logs = self.root / "build/logs"
        self.logs.mkdir(parents=True)
        self.key = "ffb3cf92a42f" + "0" * 52

    def tearDown(self): self.temp.cleanup()

    def record(self, launcher="unity-launch-ffb3cf92a42f.log", *, named=None, stage="build", key=None):
        path = self.logs / launcher
        message = "Unity.exe exited with 1; inspect " + str(named or path)
        self.log.write_text("Quest builder: " + message)
        path.write_text("Aborting batchmode due to failure:\nexecuteMethod method QuestBuild.Build threw exception.\n")
        atomic_json(self.root / "build/last-failure.json", {
            "schema": 1, "stage": stage, "message": message, **({"key": key} if key else {})})
        return path, message

    def failure(self): return tool_failure(self.root, "build", self.log, self.started, "python.exe", 1)

    def test_captured_sprite_exception_names_editor_cause_in_failure_card(self):
        launcher, message = self.record()
        editor = self.logs / "unity-build-ffb3cf92a42f.log"
        editor.write_text("[Licensing::Module] Error: Access token is unavailable\n"
                          "[LicensingClient] Licenses updated successfully\n" + SPRITE_CAUSE +
                          "\n  at QuestSpriteGeometryValidation.Read (System.String path)\n"
                          "executeMethod method QuestBuild.Build threw exception.\n"
                          "Exiting without the bug reporter. Application will terminate with return code 1")
        error = self.failure()
        self.assertEqual(error.parameters["cause"], SPRITE_CAUSE)
        self.assertEqual(error.parameters["builderError"], message)
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher), str(editor)])
        self.assertTrue(error.parameters["completedWorkRetained"])
        self.assertIn("Unity", error.message["de"])
        self.assertNotIn("python.exe", error.message["de"])
        self.assertNotIn("Access token", error.parameters["cause"])

    def test_real_supervised_failure_keeps_editor_cause_in_current_run_action(self):
        store = Store(self.root)
        saved = store.create(wizard.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))
        session = saved["session"]
        log = store.session_dir(session) / "logs/build.log"
        script = ("import json,pathlib,sys;root=pathlib.Path(sys.argv[1]);"
                  "logs=root/'build/logs';launcher=logs/'unity-launch-ffb3cf92a42f.log';"
                  "launcher.write_text('executeMethod method QuestBuild.Build threw exception.');"
                  "(logs/'unity-build-ffb3cf92a42f.log').write_text(sys.argv[2]);"
                  "message='Unity.exe exited with 1; inspect '+str(launcher);"
                  "(root/'build/last-failure.json').write_text(json.dumps(dict(schema=1,stage='build',message=message)));"
                  "print('Quest builder: '+message);sys.exit(1)")
        actions = {}
        for name in wizard.STAGES:
            def action(state, supervisor, name=name):
                if name == "build": supervisor.run([sys.executable, "-c", script, str(self.root), SPRITE_CAUSE], log)
                output = store.session_dir(session) / (name + ".txt"); output.write_text(name)
                return [output], {}
            actions[name] = action
        result = wizard.Engine(store, actions=actions).run(session)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["needsActions"][0]["parameters"]["cause"], SPRITE_CAUSE)
        self.assertIn("Unity", result["needsActions"][0]["message"]["de"])
        self.assertIn(SPRITE_CAUSE, (log.parent / "progress.log").read_text())
        self.assertNotIn("build", result["completed"])
        self.assertFalse(store.receipt(session, "build").exists())

    def test_initial_import_editor_error_and_full_matching_key_are_admitted(self):
        launcher, _ = self.record("package-import-launch-ffb3cf92a42f.log", key=self.key)
        editor = self.logs / "package-import-ffb3cf92a42f.log"
        cause = "Assets/Quest/Editor/Startup.cs(5,2): error CS1002: ; expected"
        editor.write_text(cause)
        error = self.failure()
        self.assertEqual(error.parameters["cause"], cause)
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher), str(editor)])

    def test_explicit_update_editor_uses_same_current_attempt_boundary(self):
        for kind in ("code", "sdk"):
            with self.subTest(kind=kind):
                launcher, _ = self.record("update-" + kind + "-launch.log", stage="update-mod")
                editor = self.logs / ("update-" + kind + "-unity.log")
                editor.write_text("InvalidOperationException: Missing update SDK assembly")
                error = self.failure()
                self.assertIn("Missing update SDK", error.parameters["cause"])
                self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher), str(editor)])

    def test_explicit_api_error_retains_actual_compiler_cause(self):
        launcher, _ = self.record("package-api-ffb3cf92a42f.log")
        launcher.write_text("InvalidOperationException: Package ABI differs for UnityEngine.Video")
        error = self.failure()
        self.assertIn("Package ABI differs", error.parameters["cause"])
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher)])
        self.assertNotIn("Unity konnte", error.message["de"])

    def test_stale_editor_cannot_supply_cause_even_with_current_launcher(self):
        launcher, message = self.record()
        editor = self.logs / "unity-build-ffb3cf92a42f.log"
        editor.write_text("InvalidOperationException: stale private failure"); os.utime(editor, (1, 1))
        error = self.failure()
        self.assertEqual(error.parameters["cause"], "Quest builder: " + message)
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher)])

    def test_stale_launcher_prevents_following_a_fresh_editor(self):
        launcher, message = self.record(); os.utime(launcher, (1, 1))
        (self.logs / "unity-build-ffb3cf92a42f.log").write_text(SPRITE_CAUSE)
        error = self.failure()
        self.assertEqual(error.parameters["cause"], "Quest builder: " + message)
        self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_unowned_path_cannot_select_same_named_owned_editor(self):
        outside = Path(self.temp.name) / "private/unity-launch-ffb3cf92a42f.log"
        _, message = self.record(named=outside)
        (self.logs / "unity-build-ffb3cf92a42f.log").write_text(SPRITE_CAUSE)
        error = self.failure()
        self.assertEqual(error.parameters["cause"], "Quest builder: " + message)
        self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_different_explicit_key_cannot_select_editor_from_another_build(self):
        for key in ("a" * 64, "../private", True):
            with self.subTest(key=key):
                self.record(key=key)
                (self.logs / "unity-build-ffb3cf92a42f.log").write_text(SPRITE_CAUSE)
                error = self.failure()
                self.assertNotIn("Original sprite GUID", error.parameters["cause"])
                self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_archives_path_traversal_and_unrelated_fresh_editor_are_ignored(self):
        for name in ("unity-launch-ffb3cf92a42f.memory-attempt-1.log",
                     "unity-launch-not-a-build-key.log", "../unity-launch-ffb3cf92a42f.log"):
            with self.subTest(name=name):
                self.record(name)
                (self.logs / "unity-build-ffb3cf92a42f.log").write_text(SPRITE_CAUSE)
                (self.logs / "unity-build-aaaaaaaaaaaa.log").write_text("InvalidOperationException: other run")
                error = self.failure()
                self.assertNotIn("Original sprite GUID", error.parameters["cause"])
                self.assertNotIn("other run", error.parameters["cause"])
                self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_linked_editor_or_failure_metadata_does_not_mask_the_wrapper_error(self):
        launcher, message = self.record()
        outside = Path(self.temp.name) / "private.log"; outside.write_text(SPRITE_CAUSE)
        editor = self.logs / "unity-build-ffb3cf92a42f.log"; editor.symlink_to(outside)
        error = self.failure()
        self.assertEqual(error.parameters["cause"], "Quest builder: " + message)
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher)])
        metadata = self.root / "build/last-failure.json"; metadata.unlink(); metadata.symlink_to(outside)
        self.assertEqual(self.failure().parameters["logs"], [str(self.log)])

    def test_hardlinked_editor_is_ignored(self):
        launcher, _ = self.record()
        outside = Path(self.temp.name) / "private.log"; outside.write_text(SPRITE_CAUSE)
        os.link(outside, self.logs / "unity-build-ffb3cf92a42f.log")
        error = self.failure()
        self.assertNotIn("Original sprite GUID", error.parameters["cause"])
        self.assertEqual(error.parameters["logs"], [str(self.log), str(launcher)])

    def test_editor_read_is_bounded_but_retains_exception_before_shutdown_trace(self):
        self.record()
        editor = self.logs / "unity-build-ffb3cf92a42f.log"
        editor.write_text("PRIVATE_PREFIX\n" + "x" * 100000 + "\n" + SPRITE_CAUSE +
                          "\n" + "  at Editor shutdown trace\n" * 800)
        error = self.failure()
        self.assertEqual(error.parameters["cause"], SPRITE_CAUSE)
        self.assertNotIn("PRIVATE_PREFIX", json.dumps(error.parameters))

    def test_transient_license_error_alone_does_not_replace_fatal_wrapper(self):
        _, message = self.record()
        editor = self.logs / "unity-build-ffb3cf92a42f.log"
        editor.write_text("[Licensing::Module] Error: Access token is unavailable\n"
                          "[LicensingClient] Licenses updated successfully\n")
        error = self.failure()
        self.assertEqual(error.parameters["cause"], "Quest builder: " + message)
        self.assertNotIn("Unity konnte", error.message["de"])

    def test_nested_disk_failure_remains_retryable_space_action(self):
        self.record()
        (self.logs / "unity-build-ffb3cf92a42f.log").write_text("OSError: [WinError 112] There is not enough space on the disk")
        error = self.failure()
        self.assertEqual(error.code, "workspace_space_exhausted")
        self.assertIn("WinError 112", error.parameters["cause"])


if __name__ == "__main__": unittest.main()
