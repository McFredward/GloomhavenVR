import contextlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-network"))
import smoke


class SmokeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="photon smoke spaces ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "original game"
        self.recovered = self.root / "recovered project"
        (self.game / "Managed").mkdir(parents=True)
        (self.recovered / "Assets/Plugins").mkdir(parents=True)
        (self.recovered / "Assets/Resources").mkdir(parents=True)
        for name in smoke.LIBRARIES:
            (self.game / "Managed" / name).write_bytes(b"fixture assembly " + name.encode())
            (self.recovered / "Assets/Plugins" / (name + ".meta")).write_text("fixture importer " + name)
        for name in ("BoltRuntimeSettings.asset", "BoltRuntimeSettings.asset.meta"):
            (self.recovered / "Assets/Resources" / name).write_text("PRIVATE-CONFIG-FIXTURE " + name)
        self.editor = self.root / "Unity editor"
        self.editor.write_bytes(b"trusted local fixture editor")
        self.output = self.root / "private output"
        self.argv = ["--game-data", str(self.game), "--recovered-project", str(self.recovered),
            "--unity-editor", str(self.editor), "--output-root", str(self.output)]
        self.calls = []
        self.stdout, self.stderr = io.StringIO(), io.StringIO()
        self.receipt = {"schema": 1, "platform": "LinuxEditor", "stage": "joined-original-default-lobby",
            "failure": "", "originalDefaultConfig": True, "connectedToMaster": True,
            "joinedOriginalDefaultLobby": True, "customAuthenticationSupplied": False,
            "eosInitialized": False, "gameRoomJoined": False, "androidConnected": False}
        self.exit_code = 0

    def runner(self, command, **options):
        self.calls.append(command)
        self.assertEqual(options["timeout"], 225)
        self.assertNotIn("-quit", command)
        self.assertEqual(command[command.index("-executeMethod") + 1], "QuestNetworkSmoke.Run")
        if self.receipt is not None:
            Path(options["env"]["GHVR_NETWORK_REPORT"]).write_text(json.dumps(self.receipt))
        return subprocess.CompletedProcess(command, self.exit_code, b"PRIVATE-STDOUT", b"PRIVATE-STDERR")

    def cli(self, runner=None):
        with contextlib.redirect_stdout(self.stdout), contextlib.redirect_stderr(self.stderr):
            return smoke.main(self.argv, runner=runner or self.runner)

    def proof(self):
        return json.loads(next(self.output.glob("unity-photon-*/smoke-report.json")).read_text())

    def test_isolated_copy_and_process_result_are_not_hardware_proof(self):
        before = {p: smoke.digest(p) for p in self.game.rglob("*") if p.is_file()}
        self.assertEqual(self.cli(), 0)
        self.assertEqual(before, {p: smoke.digest(p) for p in before})
        proof = self.proof()
        self.assertFalse(proof["androidConnected"])
        self.assertFalse(proof["gameRoomJoined"])
        self.assertEqual(len(proof["originalInputs"]), len(smoke.LIBRARIES) * 2 + 2)
        self.assertNotIn("PRIVATE-", self.stdout.getvalue() + self.stderr.getvalue())
        project = next(self.output.glob("unity-photon-*/project"))
        self.assertEqual(len(list((project / "Assets/Plugins").glob("*.dll"))), len(smoke.LIBRARIES))
        self.assertFalse(list(project.rglob("*EOS*")))

    def test_exit_zero_without_receipt_is_not_success(self):
        self.receipt = None
        self.assertEqual(self.cli(), 1)
        self.assertIn("No valid runtime receipt", self.proof()["failure"])

    def test_failure_callback_is_not_success_even_with_exit_zero(self):
        self.receipt["failure"] = "Original Photon connect/lobby callback rejected the client"
        self.assertEqual(self.cli(), 1)

    def test_nonzero_unity_exit_is_not_success_even_with_positive_receipt(self):
        self.exit_code = 9
        self.assertEqual(self.cli(), 1)
        self.assertEqual(self.proof()["unityExitCode"], 9)

    def test_eos_custom_auth_room_or_android_flags_cannot_broaden_evidence(self):
        for key in ("eosInitialized", "customAuthenticationSupplied", "gameRoomJoined", "androidConnected"):
            self.receipt[key] = True
            self.assertEqual(self.cli(), 1)
            self.receipt[key] = False

    def test_extra_runtime_fields_are_not_exported(self):
        self.receipt["clientSecret"] = "PRIVATE-RECEIPT-FIELD"
        self.assertEqual(self.cli(), 0)
        self.assertNotIn("PRIVATE-RECEIPT-FIELD", json.dumps(self.proof()))

    def test_missing_original_importer_stops_before_unity(self):
        (self.recovered / "Assets/Plugins/bolt.dll.meta").unlink()
        self.assertEqual(self.cli(), 1)
        self.assertEqual(self.calls, [])
        self.assertIn("is missing", self.stderr.getvalue())

    def test_input_output_overlap_and_timeout_fail_without_unity(self):
        self.argv[-1] = str(self.game / "unsafe-output")
        self.assertEqual(self.cli(), 1)
        self.assertEqual(self.calls, [])
        self.argv[-1] = str(self.output)
        self.argv.extend(["--timeout", "999"])
        self.assertEqual(self.cli(), 1)
        self.assertEqual(self.calls, [])

    def test_process_timeout_does_not_claim_connection(self):
        def timeout(command, **options):
            raise subprocess.TimeoutExpired(command, options["timeout"], b"PRIVATE-PARTIAL-STDOUT")
        self.assertEqual(self.cli(timeout), 1)
        self.assertIn("bounded", self.stderr.getvalue())
        self.assertNotIn("PRIVATE-PARTIAL-STDOUT", self.stdout.getvalue() + self.stderr.getvalue())

    def test_fixture_connects_without_room_operations_or_service_initialization(self):
        source = (ROOT / "tools/quest-network/QuestNetworkSmoke.cs").read_text()
        self.assertNotIn("OpJoinRoom", source)
        self.assertNotIn("OpCreateRoom", source)
        self.assertNotIn("BoltMatchmaking.JoinSession", source)
        self.assertNotIn("EOSManager.Instance", source)
        self.assertIn('GetMethod("Disable")', source)
        self.assertIn("EditorApplication.update -= Pump", source)


if __name__ == "__main__":
    unittest.main()
