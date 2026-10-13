"""Actual worker log tails share measured asset coverage without stale replay."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import processes
import state

spec = importlib.util.spec_from_file_location("parallel_import_work", ROOT / "tools/quest-builder/unity_work.py")
work = importlib.util.module_from_spec(spec); spec.loader.exec_module(work)


class ParallelImportProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.store = state.Store(self.root / "workspace")
        self.project = self.store.root / "build/projects" / ("a" * 64)
        self.assets = self.project / "Assets"; self.assets.mkdir(parents=True)
        for name in ("first.png", "second.png", "third.png"):
            (self.assets / name).write_bytes(b"Inert import telemetry fixture; never imported into Unity.")
        self.editor = self.root / "Unity.exe"; self.editor.write_bytes(b"Inert executable identity fixture; never executed.")
        self.native = self.store.root / "build/logs/package-import-current.log"
        self.native.parent.mkdir(parents=True, exist_ok=True)
        self.started = time.time()
        self.plan = work.publish_plan([str(self.editor), "-projectPath", str(self.project), "-buildTarget", "Android",
            "-refreshImportMode", "OutOfProcessPerQueue", "-desiredWorkerCount", "2", "-logFile", str(self.native)],
            self.native, SimpleNamespace(enabled=lambda: True, event=Mock()))
        self.native.write_text("Current Editor invocation\n")
        self.logs = [Path(name) for name in self.plan["importWorkerLogs"]]
        self.parser = processes.ProgressParser(started=self.started)
        self.addCleanup(self.parser.close_coverage)

    @staticmethod
    def line(name):
        return ("Start importing Assets/" + name + " using Guid(" + "b" * 32 + ") Importer(1) "
                "-> (artifact id: '" + "c" * 32 + "') in 0.124 seconds\n")

    def test_worker_events_share_unique_finite_total_and_correct_import_owner(self):
        for log in self.logs: log.write_text("Worker ready\n")
        self.assertIsNotNone(self.parser.observe_plan(self.native, started=self.started))
        for log in self.logs: self.assertIsNotNone(self.parser.observe_plan(log, started=self.started))
        first = self.parser.parse(self.line("first.png"), self.logs[0].name)
        second = self.parser.parse(self.line("second.png"), self.logs[1].name)
        duplicate = self.parser.parse(self.line("first.png"), self.logs[1].name)
        self.assertEqual((first["done"], second["done"], duplicate["done"]), (1, 2, 2))
        self.assertEqual(second["total"], 3)
        self.assertEqual(second["operation"], "unity-import")
        self.assertIn("second.png", second["detail"])

    def test_main_supervisor_reads_current_worker_logs_without_waiting_for_editor_stdout(self):
        self.logs[0].write_text(self.line("first.png")); self.logs[1].write_text(self.line("second.png"))
        session = self.store.create({"mode": "build", "install": False})["session"]
        supervisor = processes.Supervisor(self.store, session); supervisor.set_stage("build")
        received = []
        original = self.store.progress
        self.store.progress = lambda *_a, **kwargs: received.append(kwargs)
        try:
            supervisor._tail_progress({}, self.parser, self.started)
        finally: self.store.progress = original
        measured = [row for row in received if row.get("phase") == "unity-asset-import"]
        self.assertTrue(measured)
        self.assertEqual(max(row["done"] for row in measured), 2)
        self.assertTrue(all(row["total"] == 3 and row["operation"] == "unity-import" for row in measured))

    def test_stale_workers_and_unplanned_paths_are_not_admitted(self):
        for log in self.logs: log.write_text("Current worker\n")
        self.assertIsNone(self.parser.observe_plan(self.logs[0], started=self.plan["createdAt"] + 10))
        outside = self.root / "AssetImportWorker9.log"; outside.write_text(self.line("third.png"))
        self.assertIsNone(self.parser.observe_plan(outside, started=self.started))
        self.assertNotIn(outside.name, self.parser.import_worker_owners)

    def test_worker_events_cannot_reopen_closed_import_operation(self):
        self.logs[0].write_text("Current worker\n")
        self.parser.observe_plan(self.logs[0], started=self.started)
        self.parser.closed_operations.add("unity-import")
        self.assertIsNone(self.parser.parse(self.line("first.png"), self.logs[0].name))

    def test_closed_parent_cannot_register_an_old_worker_owner(self):
        self.logs[0].write_text("Old worker completed\n")
        self.parser.closed_operations.add("unity-import")
        self.assertIsNone(self.parser.observe_plan(self.logs[0], started=self.started))
        self.assertNotIn(self.logs[0].name, self.parser.import_worker_owners)

    def test_next_invocation_resets_tail_even_when_overwritten_worker_log_is_larger(self):
        self.logs[0].write_text(self.line("first.png") + "# old filler\n" * 300)
        session = self.store.create({"mode": "build", "install": False})["session"]
        supervisor = processes.Supervisor(self.store, session); supervisor.set_stage("build")
        received = []; tails = {}
        original = self.store.progress
        self.store.progress = lambda *_a, **kwargs: received.append(kwargs)
        try:
            supervisor._tail_progress(tails, self.parser, self.started)
            self.assertEqual(self.parser.import_coverage[self.logs[0].name].done, 1)
            next_plan = work.publish_plan([str(self.editor), "-projectPath", str(self.project), "-buildTarget", "Android",
                "-refreshImportMode", "OutOfProcessPerQueue", "-desiredWorkerCount", "2", "-logFile", str(self.native)],
                self.native, SimpleNamespace(enabled=lambda: True, event=Mock()))
            self.assertNotEqual(next_plan["invocationId"], self.plan["invocationId"])
            self.native.write_text("Next current Editor invocation\n")
            self.logs[0].write_text(self.line("second.png") + self.line("third.png") + "# next filler\n" * 600)
            supervisor._tail_progress(tails, self.parser, self.started)
            self.assertEqual(self.parser.import_coverage[self.logs[0].name].done, 3)
            measured = [row for row in received if row.get("phase") == "unity-asset-import"]
            self.assertTrue(any(row.get("done") == row.get("total") == 3 for row in measured))
        finally:
            self.store.progress = original


if __name__ == "__main__": unittest.main()
