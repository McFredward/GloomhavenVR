"""The host delivery path cannot re-enter an already successful Unity Player."""
import contextlib
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import storage


class FinishBuildTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="quest finish & spaces ")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.output = self.root / "output"
        self.output.mkdir()

    def test_toolchain_includes_the_selected_sdk_alignment_command(self):
        editor = self.root / "Unity"
        android = editor.parent / "Data/PlaybackEngines/AndroidPlayer"
        sdk = android / "SDK/build-tools/30.0.2"
        for path in (editor, sdk / "aapt", sdk / "zipalign", sdk / "lib/apksigner.jar",
                     android / "OpenJDK/bin/java", android / "OpenJDK/bin/keytool", android / "NDK/source.properties"):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"owned fixture command")
        args = SimpleNamespace(unity_editor=str(editor), android_sdk=None, android_ndk=None, jdk=None)
        with patch.object(builder, "command", return_value="2021.3.5f1\n") as launch:
            tools = builder.toolchain(args, self.output)
            self.assertEqual(tools["zipalign"], str(sdk / "zipalign"))
            self.assertEqual(tools["zipalign"], storage._ordinary_owned(Path(tools["aapt"]).with_name("zipalign")).as_posix())
            launch.assert_called_once()
        (sdk / "zipalign").unlink()
        with patch.object(builder, "command", side_effect=AssertionError("No Unity launch with missing signing tools")):
            with self.assertRaisesRegex(storage.BuildError, "zipalign"):
                builder.toolchain(args, self.output)

    def test_finish_cli_does_not_inspect_snapshot_prepare_weave_or_launch_unity(self):
        import dependencies
        game = self.root / "Game_Data"
        game.mkdir()
        (game / "Managed").mkdir()
        dependency = Mock(return_value=Path(sys.executable))
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(builder, "game_data", return_value=game))
            stack.enter_context(patch.object(builder, "ensure_output", return_value=self.output))
            stack.enter_context(patch.object(dependencies, "python_environment", dependency))
            for name in ("inspect_inputs", "snapshot_inputs", "prepare", "weave", "build", "toolchain", "command"):
                stack.enter_context(patch.object(builder, name, side_effect=AssertionError("Repeated " + name)))
            finish = stack.enter_context(patch.object(builder, "finish_completed_build", return_value=self.output / "existing.apk"))
            result = builder.main(["finish-build", "--game-root", str(game), "--output-root", str(self.output),
                                   "--delivery-input-key", "a" * 64])
        self.assertEqual(result, 0)
        self.assertEqual(finish.call_args.args[0].delivery_input_key, "a" * 64)
        self.assertEqual(finish.call_args.args[1], self.output)
        # Reuse the same ready environment as the original full build; a reduced
        # requirement set would unnecessarily trigger installation again.
        self.assertTrue(dependency.call_args.kwargs["procedural"])

    def test_delivery_receipt_keeps_the_original_runtime_and_build_identity(self):
        key = "c" * 64
        apk = self.output / "builds" / key / "GloomhavenVR-Quest.apk"
        apk.parent.mkdir(parents=True)
        apk.write_bytes(b"existing Player remains the delivery input")
        report = Path(str(apk) + ".build.json")
        storage.write_json(report, {"schema": 1, "buildResult": "Succeeded"})
        tools = {}
        for name in ("aapt", "java", "keytool", "apksigner", "zipalign"):
            path = self.root / name
            path.write_bytes(b"selected tool")
            tools[name] = str(path)
        inputs = {"inputKey": "a" * 64, "mod": {"modBuild": 665}}
        selected = {"inputs": inputs, "key": key, "apk": apk, "tools": tools,
                    "source": self.root / "frozen665", "project": self.root / "retainedProject", "provenance": {"runtime": 665}}
        helper = Mock()
        helper.select.return_value = selected
        actual_local = builder._local_helper
        args = SimpleNamespace(game_root=self.root / "Game", profile_json=None, delivery_input_key=inputs["inputKey"])
        details = {"apkSha256": storage.digest(apk), "inputKey": inputs["inputKey"], "modBuild": 665, "contentFiles": []}
        with patch.object(builder, "_local_helper", side_effect=lambda name: helper if name == "player_delivery" else actual_local(name)), \
             patch.object(builder, "finish_player_delivery", return_value=([apk, report], details)) as finish:
            self.assertEqual(builder.finish_completed_build(args, self.output), apk)
        self.assertEqual(finish.call_args.args[:4], (inputs, self.output, selected["source"], selected["project"]))
        self.assertEqual(finish.call_args.args[5:], (key, selected["provenance"]))
        self.assertTrue(finish.call_args.kwargs["resumed"])
        receipt = storage.Stages(self.output).valid("build", key)
        self.assertEqual(receipt["details"]["modBuild"], 665)
        self.assertEqual(builder.verified_latest_build(self.output)[1]["inputKey"], "a" * 64)
        self.assertEqual(storage.canonical(inputs), storage.canonical(selected["inputs"]))

    def test_no_matching_player_never_falls_back_to_a_full_build(self):
        helper = Mock()
        helper.select.return_value = None
        args = SimpleNamespace(game_root=self.root / "Game", profile_json=None, delivery_input_key="a" * 64)
        with patch.object(builder, "_local_helper", return_value=helper), \
             patch.object(builder, "build", side_effect=AssertionError("No implicit recompilation")):
            with self.assertRaisesRegex(storage.BuildError, "no compilation was started"):
                builder.finish_completed_build(args, self.output)


class CompletedPlayerCompositionTests(unittest.TestCase):
    """Compose real selection/publication with controlled binary audit results."""
    def setUp(self):
        from test_player_delivery_resume import CompletedPlayerTests
        self.fixture = CompletedPlayerTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        f = self.fixture
        for name in ("java", "aapt", "apksigner", "keytool", "zipalign"):
            path = f.root / "signing-tools" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture command; real SDK signing covered separately")
            f.tools[name] = str(path)
        f.publish()
        log = f.output / "logs" / ("unity-build-" + f.key[:12] + ".log")
        log.parent.mkdir()
        log.write_text("Android Player completed\n")
        early = f.project / "QuestCampaignShaderEvidence/native-addressables-validation.json"
        storage.write_json(early, {"schema": 1, "hardwareVerified": False})
        self.args = SimpleNamespace(game_root=f.data, profile_json=None, delivery_input_key=f.inputs["inputKey"])

    def run_finish(self, *, corrupt_bank=False):
        f = self.fixture
        actual_local = builder._local_helper
        actual_update = actual_local("update_driver")
        adapted_update = SimpleNamespace(normalized_signing_tools=actual_update.normalized_signing_tools,
                                         add_update_capability=lambda *args: {"schema": 1, "dynamicProfile": True})
        def validate(*args):
            row = storage.record_file(f.bank, f.bank.relative_to(f.output).as_posix())
            return {"apkSha256": storage.digest(f.apk), "inputKey": f.inputs["inputKey"], "modBuild": 665,
                    "contentFiles": [row], "buildReport": json.loads(f.report.read_text())}
        def graphics(source, project, apk, bank, receipt):
            storage.write_json(receipt, {"schema": 1, "hardwareVerified": False})
            if corrupt_bank: bank.write_bytes(b"unexpected changed content")
            return dict.fromkeys(("shaderCount", "originalNativeAliasCount", "allOriginalAliasesRetained",
                "actualNativeAliasMetadataVerified", "materialSampleCount", "compilerQueries", "hardwarePictureVerified"), 0)
        def compute(source, project, apk, banks, receipt, *, artifact_hash):
            self.assertEqual(artifact_hash(apk), storage.digest(f.apk))
            self.assertEqual(artifact_hash(banks[0]), f.bank_sha)
            storage.write_json(receipt, {"schema": 1, "hardwareVerified": False})
            return {"shaderCount": 13, "kernelCount": 36, "graphicsApi": "Vulkan",
                    "actualExecutableBytesVerified": True, "hardwareVerified": False}
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(builder, "_local_helper", side_effect=lambda name:
                                             adapted_update if name == "update_driver" else actual_local(name)))
            stack.enter_context(patch.object(builder, "validate_apk", side_effect=validate))
            stack.enter_context(patch.dict(sys.modules, {
                "campaign_native_shaders": SimpleNamespace(validate_delivered=graphics),
                "campaign_compute": SimpleNamespace(validate_delivered=compute)}))
            for name in ("inspect_inputs", "snapshot_inputs", "prepare", "weave", "toolchain", "command"):
                stack.enter_context(patch.object(builder, name, side_effect=AssertionError("No previous work: " + name)))
            return builder.finish_completed_build(self.args, f.output)

    def test_actual_retained_selector_and_final_publication_keep_665(self):
        f = self.fixture
        self.assertEqual(self.run_finish(), f.apk)
        self.assertEqual(builder.verified_latest_build(f.output)[1]["modBuild"], 665)
        metadata = json.loads(f.report.read_text())
        self.assertEqual(metadata["buildProvenance"], f.provenance)
        self.assertTrue(metadata["deliveryDriver"]["resumedCompletedPlayer"])
        self.assertTrue((f.output / "latest-player.json").is_file())

    def test_changed_bank_fails_only_delivery_and_preserves_completed_apk(self):
        f = self.fixture
        original = f.apk.read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "changed during final validation"):
            self.run_finish(corrupt_bank=True)
        self.assertEqual(f.apk.read_bytes(), original)
        self.assertTrue((f.output / "latest-player.json").is_file())
        self.assertFalse((f.output / "latest-build.json").exists())


if __name__ == "__main__":
    unittest.main()
