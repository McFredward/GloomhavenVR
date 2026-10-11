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
        storage.write_json(f.output / "signing/certificate.json", {"sha256": "f" * 64})
        for path in ("tools/quest-recovery/catalog.py", "tools/quest-recovery/recover.py", "tools/quest-recovery/md4.py",
                     "tools/quest-builder/progress.py", "tools/quest-compute/compiled.py", "tools/quest-compute/native.py",
                     "tools/quest-compute/vulkan_compiled.py", "tools/quest-compute/spirv.py"):
            target = f.source / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((ROOT / path).read_bytes())
        for path in ("Assets/QuestOriginalCampaign/campaign-shaders.json", "Assets/QuestOriginalCampaign/campaign-computes.json"):
            storage.write_json(f.project / path, {"schema": 1, "authoredFixture": True})
        collection = f.project / "Assets/Resources/QuestCampaignShaderVariants.shadervariants.meta"
        collection.parent.mkdir(parents=True, exist_ok=True)
        collection.write_text("guid: " + "a" * 32 + "\n")
        self.material = f.project / "Assets/Material/Authored.mat"
        self.material.parent.mkdir(parents=True, exist_ok=True)
        self.material.write_bytes(b"authored original material contract")
        self.args = SimpleNamespace(game_root=f.data, profile_json=None, delivery_input_key=f.inputs["inputKey"])

    def run_finish(self, *, corrupt_bank=False, fail_compute=False, fail_publication=False, counters=None):
        f = self.fixture
        actual_local = builder._local_helper
        actual_update = actual_local("update_driver")
        adapted_update = SimpleNamespace(normalized_signing_tools=actual_update.normalized_signing_tools,
                                         add_update_capability=lambda *args: {"schema": 1, "dynamicProfile": True})
        def validate(*args):
            if counters is not None: counters["apk"] += 1
            row = storage.record_file(f.bank, f.bank.relative_to(f.output).as_posix())
            return {"apkSha256": storage.digest(f.apk), "inputKey": f.inputs["inputKey"], "modBuild": 665,
                    "contentFiles": [row], "buildReport": json.loads(f.report.read_text())}
        def graphics(source, project, apk, bank, receipt, *, progress):
            if counters is not None: counters["shaders"] += 1
            progress("shaders", 688, 688, "actual authored root audit")
            progress("bundles", 1, 1, "actual authored closure audit")
            if corrupt_bank: bank.write_bytes(b"unexpected changed content")
            result = dict.fromkeys(("shaderCount", "originalNativeAliasCount", "allOriginalAliasesRetained",
                "actualNativeAliasMetadataVerified", "materialSampleCount", "compilerQueries", "hardwarePictureVerified"), 0)
            result["materialSampleCount"] = 1
            result["materials"] = [{"assetPath": self.material.relative_to(project).as_posix(),
                                    "sourceSha256": storage.digest(self.material)}]
            storage.write_json(receipt, result)
            return result
        def compute(source, project, apk, banks, receipt, *, artifact_hash):
            if counters is not None: counters["compute"] += 1
            self.assertEqual(artifact_hash(apk), storage.digest(f.apk))
            self.assertEqual(artifact_hash(banks[0]), f.bank_sha)
            if fail_compute: raise storage.BuildError("Authored interrupted Compute delivery audit")
            storage.write_json(receipt, {"schema": 1, "hardwareVerified": False})
            return {"shaderCount": 13, "kernelCount": 36, "graphicsApi": "Vulkan",
                    "actualExecutableBytesVerified": True, "hardwareVerified": False}
        with contextlib.ExitStack() as stack:
            if fail_publication:
                actual_write = storage.write_json
                def publish(path, value):
                    if path.parent == f.output / "receipts/build":
                        raise storage.BuildError("Authored interrupted final receipt publication")
                    return actual_write(path, value)
                stack.enter_context(patch.object(storage, "write_json", side_effect=publish))
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

    def test_failed_compute_retry_keeps_apk_and_shader_gates(self):
        f = self.fixture
        original = f.apk.read_bytes()
        counts = dict.fromkeys(("apk", "shaders", "compute"), 0)
        with self.assertRaisesRegex(storage.BuildError, "interrupted Compute"):
            self.run_finish(fail_compute=True, counters=counts)
        self.assertEqual(counts, {"apk": 1, "shaders": 1, "compute": 1})
        self.assertEqual(self.run_finish(counters=counts), f.apk)
        self.assertEqual(counts, {"apk": 1, "shaders": 1, "compute": 2})
        self.assertEqual(f.apk.read_bytes(), original)
        self.assertEqual(builder.verified_latest_build(f.output)[1]["modBuild"], 665)

    def test_corrupt_graphics_evidence_repairs_only_graphics(self):
        f = self.fixture
        counts = dict.fromkeys(("apk", "shaders", "compute"), 0)
        self.run_finish(counters=counts)
        (f.folder / "shaders-delivered-validation.json").write_text("corrupt evidence")
        self.run_finish(counters=counts)
        self.assertEqual(counts, {"apk": 1, "shaders": 2, "compute": 1})
        self.assertEqual(json.loads((f.folder / "shaders-delivered-validation.json").read_text())["materialSampleCount"], 1)

    def test_changed_selected_material_reaudits_only_graphics(self):
        counts = dict.fromkeys(("apk", "shaders", "compute"), 0)
        with self.assertRaisesRegex(storage.BuildError, "final receipt publication"):
            self.run_finish(fail_publication=True, counters=counts)
        self.material.write_bytes(b"a changed authored source material contract")
        self.run_finish(counters=counts)
        self.assertEqual(counts, {"apk": 1, "shaders": 2, "compute": 1})

    def test_changed_frozen_consumer_reaudits_only_its_compute_gate(self):
        counts = dict.fromkeys(("apk", "shaders", "compute"), 0)
        with self.assertRaisesRegex(storage.BuildError, "final receipt publication"):
            self.run_finish(fail_publication=True, counters=counts)
        consumer = self.fixture.source / "tools/quest-compute/spirv.py"
        consumer.write_bytes(consumer.read_bytes() + b"\n# changed authored audit consumer\n")
        self.run_finish(counters=counts)
        self.assertEqual(counts, {"apk": 1, "shaders": 1, "compute": 2})

    def test_retry_driver_annotations_do_not_revalidate_the_same_signed_apk(self):
        f = self.fixture
        counts = dict.fromkeys(("apk", "shaders", "compute"), 0)
        with self.assertRaisesRegex(storage.BuildError, "interrupted Compute"):
            self.run_finish(fail_compute=True, counters=counts)
        metadata = json.loads(f.report.read_text())
        metadata["deliveryDriver"] = {"resumedCompletedPlayer": False, "historicalDriver": True}
        storage.write_json(f.report, metadata)
        self.run_finish(counters=counts)
        self.assertEqual(counts, {"apk": 1, "shaders": 1, "compute": 2})

    def test_persistent_delivery_witnesses_do_not_reread_large_unchanged_artifacts(self):
        f = self.fixture
        with self.assertRaisesRegex(storage.BuildError, "interrupted Compute"):
            self.run_finish(fail_compute=True)
        # Simulate a new process: no invocation-only byte proofs survive.
        storage._invocation_file_proofs.clear()
        observed = []
        actual_digest = builder.digest
        def hashed(path):
            observed.append(Path(path))
            return actual_digest(path)
        with patch.object(builder, "digest", side_effect=hashed):
            self.run_finish()
        self.assertNotIn(f.apk, observed)
        self.assertNotIn(f.bank, observed)

    def test_unsupported_metadata_requires_actual_artifact_byte_reads(self):
        f = self.fixture
        with self.assertRaisesRegex(storage.BuildError, "interrupted Compute"):
            self.run_finish(fail_compute=True)
        storage._invocation_file_proofs.clear()
        observed = []
        actual_digest = builder.digest
        def hashed(path):
            observed.append(Path(path))
            return actual_digest(path)
        with patch.object(storage, "_file_witness_stamp", return_value=None), \
             patch.object(builder, "digest", side_effect=hashed):
            self.run_finish()
        self.assertIn(f.apk, observed)
        self.assertIn(f.bank, observed)


if __name__ == "__main__":
    unittest.main()
