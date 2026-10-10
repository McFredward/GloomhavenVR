"""Live log, optional graph and durable overview compose without closing the APK."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser, Supervisor
from state import Store
import wizard
from test_player_bee_progress_231755 import BACKEND, PROGRAM, SOURCE, graph, log, profile, result

spec = importlib.util.spec_from_file_location("composed_player_work", ROOT / "tools/quest-builder/unity_work.py")
work = importlib.util.module_from_spec(spec)
spec.loader.exec_module(work)


class Progress:
    def event(self, *args, **kwargs): pass


def event(phase, **values):
    return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": phase, **values})


class PlayerObserverCompositionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        interval = patch("state.PROGRESS_INTERVAL", 0); interval.start(); self.addCleanup(interval.stop)
        self.store = Store(Path(temporary.name) / "owned")
        self.session = self.store.create(wizard.choices({"gameRoot": str(Path(temporary.name) / "Game")}))["session"]
        self.store.begin_stage(self.session, "build", "same-owned-input")
        self.store.operation(self.session, "build", "player")
        self.project = self.store.root / "build/projects/retained"
        shader = self.project / "Assets/Fixture.shader"; shader.parent.mkdir(parents=True)
        shader.write_text('Shader "Fixture/Player" { SubShader {} }\n')
        self.native = self.store.root / "build/logs" / SOURCE
        launch = self.native.with_name("unity-launch-composed.log")
        self.plan = work.publish_plan(["Unity", "-projectPath", str(self.project), "-logFile", str(self.native), "-buildTarget", "Android"], launch, Progress())
        self.assertIsNotNone(self.plan)
        self.started = self.plan["createdAt"] - .01
        self.parser = ProgressParser(started=self.started); self.addCleanup(self.parser.close_coverage)
        self.supervisor = Supervisor(self.store, self.session, poll=.01); self.supervisor.set_stage("build")
        self.tails = {}
        self.native.write_text("")
        self.append(event("operation:player", operation="player", status="start"),
                    event("unity-work-invocation", done=0, total=1, unit="invocations", operation="player", status="start"),
                    event("unity-work-stage:method", done=0, total=1, unit="tasks", operation="player", status="start"))

    def append(self, *lines):
        with self.native.open("a") as stream: stream.write("\n".join(lines) + "\n")
        return self.poll()

    def poll(self):
        self.supervisor._tail_progress(self.tails, self.parser, self.started)
        value = self.store.load(self.session)["stages"][5]["progress"]
        owner = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "player")
        return value, owner, {row["id"]: row for row in owner["unityWork"]["phases"][1]["parts"]}

    def test_real_tail_order_keeps_completed_pass_and_native_child_separate_from_apk(self):
        self.append(event("unity-player-scenes", operation="player", done=14, total=14, unit="scenes"))
        _, _, parts = self.append('Compiling shader "Fixture/Player" pass "Forward" (vp)',
                                  "6 / 6 variants left after stripping, processed in 0.01 seconds",
                                  "[1s] 6 / 6 variants ready")
        self.assertFalse(parts["shaders"]["closed"])
        _, owner, parts = self.append("finished in 1 seconds. Local cache hits 6 (100%), remote cache hits 0 (0%), compiled 0 variants (0%), skipped 0 variants")
        self.assertEqual((owner["compiler"]["status"], owner["compiler"]["percent"]), ("complete", 100.))
        self.assertFalse(parts["scenes"]["closed"])
        self.assertFalse(parts["shaders"]["closed"])
        _, _, parts = self.append(PROGRAM)
        self.assertTrue(parts["scenes"]["closed"])
        self.assertTrue(parts["shaders"]["closed"])

        graph(self.project)
        self.append(BACKEND, "WorkingDir: " + str(self.project))
        profile(self.project)
        log(self.project, result(index=1, annotation="IL2CPP_CodeGen Library/Bee/convert.traceevents", done=2))
        _, _, parts = self.poll()
        conversion = next(row for row in parts["native"]["activities"] if row["id"] == "il2cpp")
        self.assertTrue(conversion["closed"])
        self.assertFalse(parts["native"]["closed"])
        _, owner, parts = self.append("[5/5 0s] C_Android_arm64 Library/Bee/Game.o",
                                     "ExitCode: 0 Duration: 1s",
                                     "*** Tundra build success (1.00 seconds), 2 items updated, 5 evaluated")
        self.assertEqual((parts["native"]["closed"], parts["native"]["percent"]), (True, 100.))
        self.assertFalse(parts["packaging"]["closed"])
        self.assertFalse(owner["closed"])

    def test_successful_player_owner_cannot_reopen_from_late_graph_or_shader_tail(self):
        graph(self.project)
        self.append(BACKEND, "WorkingDir: " + str(self.project))
        _, owner, _ = self.append(event("operation:player", operation="player", status="complete"))
        self.assertTrue(owner["closed"])
        witness = self.project / "Library/retained-asset.bin"; witness.write_bytes(b"original imported data")
        before = witness.stat().st_mtime_ns
        profile(self.project); log(self.project, result(done=1))
        _, owner, parts = self.append(PROGRAM, "[1/5 0s] C_Android_arm64 Library/Bee/Game.o",
                                      'Compiling shader "Fixture/Player" pass "Later" (vp)')
        self.assertTrue(owner["closed"])
        self.assertTrue(all(part["closed"] and part["percent"] == 100. for part in parts.values()))
        self.assertEqual((witness.read_bytes(), witness.stat().st_mtime_ns), (b"original imported data", before))


if __name__ == "__main__": unittest.main()
