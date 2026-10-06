"""Real failing child and exact attempt-scoped diagnostic context."""
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
from processes import Supervisor
from state import Store, WizardError, atomic_json
import wizard


class FailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "owned"
        self.store = Store(self.root)
        self.state = self.store.create(wizard.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))
        self.session = self.state["session"]
        self.log = self.store.session_dir(self.session) / "logs/build.log"
        self.log.parent.mkdir(parents=True)

    def tearDown(self): self.temp.cleanup()

    def test_real_failure_reports_nested_recovery_cause_without_success_receipt(self):
        key = "a" * 64
        child = "import pathlib,json,sys;root=pathlib.Path(sys.argv[1]);key=sys.argv[2];p=root/'build/logs'/('recovery-'+key[:12]+'.log');p.parent.mkdir(parents=True);p.write_text('[Quest full recovery] FAILED: Skipped core contains an unknown original object; re-export core coherently.\\n');(root/'build/last-failure.json').write_text(json.dumps(dict(schema=1,stage='recovery',key=key,error='BuildError',message='python failed; inspect recovery log')));print('Quest builder: python failed');sys.exit(1)"
        actions = {}
        for name in wizard.STAGES:
            def action(state, supervisor, name=name):
                if name == "build":
                    supervisor.run([sys.executable, "-c", child, str(self.root), key], self.log)
                path = self.store.session_dir(self.session) / (name + ".txt")
                path.write_text(name)
                return [path], {}
            actions[name] = action
        result = wizard.Engine(self.store, actions=actions).run(self.session)
        self.assertEqual(result["status"], "failed")
        action = result["needsActions"][0]
        self.assertEqual(action["parameters"]["failureStage"], "recovery")
        self.assertIn("Skipped core", action["parameters"]["cause"])
        self.assertIn("Spielassets", action["message"]["de"])
        self.assertNotIn("build", result["completed"])
        records = (self.log.parent / "progress.log").read_text()
        self.assertIn("Skipped core", records)
        self.assertIn('"exitCode":1', records)
        self.assertFalse((self.store.session_dir(self.session) / "child.json").exists())

    def test_old_failure_or_unsafe_key_cannot_import_another_attempts_log(self):
        self.log.write_text("Quest builder: current failure")
        failure = self.root / "build/last-failure.json"
        atomic_json(failure, {"schema": 1, "stage": "recovery", "key": "../private", "message": "old private failure"})
        os.utime(failure, (1, 1))
        error = tool_failure(self.root, "build", self.log, time.time() - 1, "python.exe", 1)
        self.assertEqual(error.parameters["failureStage"], "build")
        self.assertNotIn("old private", error.parameters["cause"])
        self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_saved_export_reader_failure_names_evidence_and_keeps_exact_size_context(self):
        cause = 'Recovery resume evidence is oversized: core-recovery.json (19000000 bytes; limit 16777216 bytes)'
        self.log.write_text('Quest builder: ' + cause)
        atomic_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'recovery', 'key': 'b' * 64, 'message': cause})
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertIn('Nachweis', error.message['de'])
        self.assertIn('Arbeitsordner behalten', error.message['de'])
        self.assertEqual(error.parameters['builderError'], cause)
        self.assertIn('19000000 bytes', error.parameters['cause'])

    def test_unexpected_stage_exception_retains_type_and_traceback(self):
        def fail(*_): raise ValueError("Invalid owned asset index")
        result = wizard.Engine(self.store, actions={"tools": fail}).run(self.session)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["needsActions"][0]["stage"], "tools")
        event = result["events"][-1]
        self.assertEqual(event["parameters"]["error"], "ValueError")
        self.assertIn("Invalid owned asset index", event["parameters"]["traceback"])


if __name__ == "__main__": unittest.main()
