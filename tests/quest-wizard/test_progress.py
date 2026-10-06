"""Measured progress, durable waits and live owned-tool supervision."""
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import LogTail, ProgressParser, Supervisor
from state import Cancelled, STAGES, Store, WizardError, atomic_json, file_lock, read_json, stage_progress
import wizard


class ProgressFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.store = Store(self.root / "owned")
        self.state = self.store.create(wizard.choices({"gameRoot": str(self.root / "Game")}))
        self.session = self.state["session"]

    def tearDown(self): self.temp.cleanup()

    def row(self, stage="build"):
        return next(row for row in self.store.load(self.session)["stages"] if row["id"] == stage)



class ProgressTests(ProgressFixture):
    def test_pending_and_measured_percentage(self):
        self.assertEqual([row["progress"]["percent"] for row in self.state["stages"]], [0] * len(STAGES))
        self.store.progress(self.session, "build", "copy", 3, 8, "files", "actor.png")
        value = self.row()["progress"]
        self.assertEqual(value["percent"], 37.5)
        self.assertEqual(set(value), {"phase", "done", "total", "unit", "percent", "detail", "updatedAt"})

    def test_unknown_total_and_elapsed_time_never_invent_percentage(self):
        self.store.progress(self.session, "build", "unity", detail="Importing original assets")
        with mock.patch("state.time.time", return_value=time.time() + 3600):
            self.assertIsNone(self.row()["progress"]["percent"])
        self.store.progress(self.session, "build", "download", 123, None, "bytes")
        self.assertIsNone(self.row()["progress"]["percent"])

    def test_invalid_counts_and_overshoot_rejected(self):
        for done, total in ((-1, 2), (3, 2), (True, 2), (1, float("nan")), (float("inf"), 2), ("1", 2)):
            with self.subTest(done=done, total=total), self.assertRaises(WizardError):
                self.store.progress(self.session, "build", "copy", done, total)
        self.assertEqual(self.row()["progress"]["phase"], "pending")

    def test_detail_bounded_and_control_characters_removed(self):
        self.store.progress(self.session, "build", "copy", 0, 1, "files", "\x00" + "x" * 2000)
        self.assertEqual(self.row()["progress"]["detail"], "x" * 1024)

    def test_high_rate_counter_updates_are_throttled_but_final_is_persisted(self):
        with self.store.active(self.state), mock.patch("state.time.monotonic", return_value=100):
            for number in range(100): self.store.progress(self.session, "build", "copy", number, 100, "files")
            self.assertEqual(sum(event["code"] == "stage_progress" for event in self.state["events"]), 1)
            self.store.progress(self.session, "build", "copy", 100, 100, "files")
            self.assertEqual(sum(event["code"] == "stage_progress" for event in self.state["events"]), 2)
        self.assertEqual(self.row()["progress"]["percent"], 100)

    def test_progress_never_clears_explicit_wait_and_nonce_is_stable(self):
        message = {"en": "Sign in to Unity Hub.", "de": "In Unity Hub anmelden."}
        with self.store.active(self.state):
            wait = self.store.waiting(self.session, "unity", "unity_login_required", message)
            again = self.store.waiting(self.session, "unity", "unity_login_required", message)
            self.assertEqual(wait, again)
            self.store.progress(self.session, "unity", "hub", detail="Hub is open")
            self.store.event(self.state, "process_finished", "unity")
        result = self.store.load(self.session)
        self.assertEqual(result["needsActions"][0]["nonce"], wait["nonce"])
        self.assertEqual(self.row("unity")["waiting"]["action"], "unity-open")
        self.store.clear_waiting(self.session, "unity")
        self.assertNotIn("waiting", self.row("unity"))
        self.assertEqual(self.store.load(self.session)["needsActions"], [])

    def test_wait_accepts_only_localized_supported_actions(self):
        for code, message, action in (("bad code", {"en": "a", "de": "b"}, "unity-open"),
                                      ("okay", {"en": "a"}, "unity-open"),
                                      ("okay", {"en": "a", "de": "b"}, "shell")):
            with self.subTest(code=code, action=action), self.assertRaises(WizardError):
                self.store.waiting(self.session, "unity", code, message, action)

    def test_old_session_progress_defaults_are_compatible(self):
        self.state["stages"][0]["status"] = "complete"
        for row in self.state["stages"]: row.pop("progress")
        atomic_json(self.store.session_dir(self.session) / "state.json", self.state)
        rows = self.store.load(self.session)["stages"]
        self.assertEqual(rows[0]["progress"]["percent"], 100)
        self.assertEqual(rows[1]["progress"]["percent"], 0)

    def test_concurrent_status_reads_and_live_updates_are_atomic(self):
        errors = []
        self.state["status"] = "running"
        def read():
            try:
                for _ in range(80):
                    state = self.store.load(self.session)
                    self.assertEqual(state["status"], "running")
                    self.assertEqual(state["needsActions"][0]["action"], "unity-open")
            except BaseException as error: errors.append(error)
        with file_lock(self.store.root / "run.lock"), self.store.active(self.state):
            self.store.waiting(self.session, "unity", "unity_login_required", {"en": "Sign in", "de": "Anmelden"})
            threads = [threading.Thread(target=read) for _ in range(3)]
            for thread in threads: thread.start()
            for number in range(100): self.store.progress(self.session, "unity", "download", number, 100, "bytes")
            for thread in threads: thread.join()
        self.assertEqual(errors, [])

    def test_output_and_reuse_hashes_report_real_byte_counts(self):
        output = self.store.session_dir(self.session) / "large.bin"; output.write_bytes(b"z" * (2 * 1048576 + 9))
        with self.store.active(self.state):
            receipt = self.store.publish(self.session, "build", "key", [output], {})
            self.assertEqual(self.state["stages"][5]["progress"]["done"], output.stat().st_size)
            self.assertEqual(self.store.valid(self.session, "build", "key", report_progress=True), receipt)
            self.assertEqual(self.state["stages"][5]["progress"]["phase"], "receipt-verify")
        self.assertEqual(self.row()["progress"]["total"], output.stat().st_size)

    def test_cancel_during_receipt_hash_does_not_publish_complete(self):
        output = self.store.session_dir(self.session) / "large.bin"; output.write_bytes(b"z" * 1048576)
        self.store.cancel(self.session)
        with self.assertRaises(Cancelled): self.store.publish(self.session, "build", "key", [output], {})
        self.assertFalse(self.store.receipt(self.session, "build").exists())

    def test_engine_resume_records_duration_complete_and_reuse(self):
        calls = []
        def action(stage):
            def run(state, supervisor):
                calls.append(stage)
                self.store.progress(self.session, stage, "actual-work", 1, 2, "files", "first")
                output = self.store.session_dir(self.session) / (stage + ".txt"); output.write_text(stage)
                return [output], {"name": stage}
            return run
        actions = {stage: action(stage) for stage in STAGES}
        wizard.Engine(self.store, actions=actions).run(self.session)
        resumed = wizard.Engine(self.store, actions=actions).run(self.session)
        self.assertEqual(len(calls), len(STAGES))
        self.assertTrue(all(row["progress"]["percent"] == 100 for row in resumed["stages"]))
        self.assertTrue(all(row["durationSeconds"] >= 0 for row in resumed["stages"]))
        lines = (self.store.session_dir(self.session) / "logs/progress.log").read_text().splitlines()
        self.assertEqual(sum(json.loads(line)["code"] == "stage_reused" for line in lines), len(STAGES))

    def test_timed_out_stage_keeps_retry_wait_without_event_parameter_collision(self):
        def blocked(state, supervisor):
            self.store.waiting(self.session, "tools", "unity_login_required", {"en": "Sign in", "de": "Anmelden"})
            raise WizardError("child_timeout", "Timed out", durationSeconds=2)
        result = wizard.Engine(self.store, actions={"tools": blocked}).run(self.session)
        self.assertEqual(result["status"], "blocked")
        self.assertEqual(result["needsActions"][0]["action"], "unity-open")
        self.assertEqual(result["events"][-1]["code"], "child_timeout")

    def test_progress_log_rotation_is_bounded(self):
        log = self.store.session_dir(self.session) / "logs/progress.log"; log.parent.mkdir()
        log.write_bytes(b"x" * 32)
        with mock.patch("state.PROGRESS_LOG_BYTES", 32): self.store.record(self.session, "test")
        self.assertEqual(log.with_name("progress.previous.log").stat().st_size, 32)
        self.assertEqual(json.loads(log.read_text())["code"], "test")


class ParserTests(unittest.TestCase):
    def test_structured_actual_counts(self):
        parsed = ProgressParser().parse('GHVRQ_PROGRESS {"schema":1,"phase":"assets","done":7,"total":14,"unit":"files","detail":"rat"}')
        self.assertEqual(stage_progress(**parsed)["percent"], 50)

    def test_false_counters_malformed_events_and_overshoot_are_ignored(self):
        parser = ProgressParser()
        for line in ('text 42%', '[1/2] arbitrary', '[3/2 0s] Clang x.cpp', '[1/2 0s] Unrecognized x',
                     'GHVRQ_PROGRESS []', 'GHVRQ_PROGRESS {"schema":1,"phase":"assets","done":2,"total":1}',
                     'GHVRQ_PROGRESS {"schema":1,"phase":"assets","done":NaN,"total":10}'):
            with self.subTest(line=line): self.assertIsNone(parser.parse(line))

    def test_native_bee_counters_and_new_dag_reset(self):
        parser = ProgressParser()
        first = parser.parse('[340/344    0s] Csc Library/Bee/A.dll', 'unity-build.log')
        self.assertEqual(first["done"], 340); self.assertEqual(first["total"], 344)
        other = parser.parse('[1/978  2.2s] Clang GH.Runtime_7.cpp', 'unity-build.log')
        self.assertNotEqual(first["phase"], other["phase"])
        last = parser.parse('[977/978  0s] Clang GH.Runtime_7.cpp', 'unity-build.log')
        reset = parser.parse('[1/978  0s] Clang GH.Runtime_1.cpp', 'unity-build.log')
        self.assertNotEqual(last["phase"], reset["phase"])

    def test_git_and_installer_real_counts(self):
        parser = ProgressParser()
        self.assertEqual(parser.parse('Receiving objects: 50% (4/8), 2 KiB')['done'], 4)
        self.assertEqual(parser.parse('Installed content files: 7/8.')['total'], 8)
        self.assertIsNone(parser.parse('Installed content files: 9/8.'))

    def test_tool_phase_is_explicitly_unknown(self):
        parsed = ProgressParser().parse('graphics: validating original shaders')
        self.assertIsNone(stage_progress(**parsed)["percent"])

    def test_log_tail_handles_partial_cr_and_truncation(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "log"; path.write_bytes(b"one\rtw")
            tail = LogTail(path); self.assertEqual(tail.read(), ["one"])
            with path.open("ab") as stream: stream.write(b"o\nlast")
            self.assertEqual(tail.read(final=True), ["two", "last"])
            path.write_bytes(b"new\n"); self.assertEqual(tail.read(), ["new"])


class ProcessTests(ProgressFixture):
    def test_live_child_output_updates_progress_before_exit(self):
        supervisor = Supervisor(self.store, self.session, poll=.01); supervisor.set_stage("build")
        script = 'import time;print(\'GHVRQ_PROGRESS {"schema":1,"phase":"assets","done":3,"total":4,"unit":"files"}\',flush=True);time.sleep(.15)'
        with self.store.active(self.state):
            supervisor.run([sys.executable, "-I", "-c", script], self.store.session_dir(self.session) / "logs/build.log")
        self.assertEqual(self.row()["progress"]["percent"], 75)
        self.assertFalse((self.store.session_dir(self.session) / "child.json").exists())

    def test_current_native_log_is_observed_but_previous_attempt_is_ignored(self):
        folder = self.store.root / "build/logs"; folder.mkdir(parents=True)
        old = folder / "unity-build-old.log"; old.write_text('[99/100 0s] Clang old.cpp\n'); os.utime(old, (1, 1))
        current = folder / "unity-build-new.log"
        script = 'from pathlib import Path;import sys,time;Path(sys.argv[1]).write_text("[2/8 0s] Clang current.cpp\\n");time.sleep(.15)'
        supervisor = Supervisor(self.store, self.session, poll=.01); supervisor.set_stage("build")
        with self.store.active(self.state):
            supervisor.run([sys.executable, "-I", "-c", script, str(current)], self.store.session_dir(self.session) / "logs/build.log")
        self.assertEqual(self.row()["progress"]["percent"], 25)
        self.assertIn("current.cpp", self.row()["progress"]["detail"])

    def test_timeout_stops_owned_child_and_retains_log(self):
        supervisor = Supervisor(self.store, self.session, poll=.01, grace=.1)
        log = self.store.session_dir(self.session) / "logs/unity.log"
        with self.assertRaises(WizardError) as caught:
            supervisor.run([sys.executable, "-I", "-c", 'import time;print("open",flush=True);time.sleep(30)'], log, timeout=.1)
        self.assertEqual(caught.exception.code, "child_timeout")
        self.assertIn("open", log.read_text())
        self.assertFalse((self.store.session_dir(self.session) / "child.json").exists())

    def test_gui_started_and_explicit_check_can_finish_owned_wait(self):
        supervisor = Supervisor(self.store, self.session, poll=.01, grace=.1)
        started = []
        result = supervisor.run([sys.executable, "-I", "-c", "import time;time.sleep(30)"],
                                self.store.session_dir(self.session) / "logs/unity.log",
                                on_started=lambda process: started.append(process.pid), on_poll=lambda process: False)
        self.assertEqual(result, 0); self.assertEqual(len(started), 1)
        self.assertFalse((self.store.session_dir(self.session) / "child.json").exists())

    def test_acceptable_exit_code_and_invalid_timeout(self):
        supervisor = Supervisor(self.store, self.session)
        log = self.store.session_dir(self.session) / "logs/unity.log"
        self.assertEqual(supervisor.run([sys.executable, "-I", "-c", "raise SystemExit(2)"], log, acceptable_codes=(0, 2)), 2)
        with self.assertRaises(WizardError): supervisor.run([sys.executable], log, timeout=float("nan"))


if __name__ == "__main__": unittest.main()
