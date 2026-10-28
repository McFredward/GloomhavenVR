"""Actual streaming pinned exporter counts without rebuilding instrumentation."""
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import recover


class ExportProgressTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.path = Path(self.temporary.name) / "export.log"
        self.path.write_bytes(b"ExportProgress : (3/4) Exporting 'previous request'\n")
    def tearDown(self): self.temporary.cleanup()
    def append(self, raw):
        with self.path.open("ab") as stream: stream.write(raw)
    def events(self, output):
        return [json.loads(line.removeprefix(recover.build_progress.PREFIX)) for line in output.getvalue().splitlines()
                if line.startswith(recover.build_progress.PREFIX)]

    def test_partial_lines_and_real_actual_counts_ignore_prior_log_content(self):
        progress = recover.ExportLogProgress(self.path)
        self.append(b"ExportProgress : (1/11349) Exporting 'Portrait_")
        progress.poll(); self.assertIsNone(progress.total)
        output = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(output):
            self.append(b"Jekserah'\r\n")
            progress.poll()
            progress.counter.interval = 0
            self.append(b"ExportProgress : (2761/11349) Exporting 'Last collection'\n")
            progress.poll()
        events = self.events(output)
        self.assertEqual((progress.done, progress.total), (2761, 11349))
        self.assertEqual((events[-1]["phase"], events[-1]["done"], events[-1]["total"], events[-1]["unit"]),
                         ("recovery-asset-export", 2761, 11349, "collections"))
        self.assertNotEqual(events[-1]["status"], "complete")

    def test_regressing_reset_and_invalid_counts_do_not_restart_observed_scope(self):
        progress = recover.ExportLogProgress(self.path)
        self.append(b"ExportProgress : (7/10) Exporting 'observed'\n")
        progress.poll()
        self.append(b"ExportProgress : (2/10) Exporting 'backwards'\n"
                    b"ExportProgress : (8/20) Exporting 'different schedule'\n"
                    b"ExportProgress : (11/10) Exporting 'invalid'\n"
                    b"ExportProgress : (9007199254740992/9007199254740992) Exporting 'unsafe'\n")
        progress.poll()
        self.assertEqual((progress.done, progress.total), (7, 10))
        self.append(b"ExportProgress : (8/10) Exporting 'current'\n")
        progress.poll(); self.assertEqual(progress.done, 8)

    def test_live_observer_publishes_during_synchronous_export_and_stops_afterward(self):
        output, witnessed = io.StringIO(), threading.Event()
        actual_event = recover.build_progress.event
        def event(*args, **kwargs):
            value = actual_event(*args, **kwargs)
            if value["phase"] == "recovery-asset-export" and value["total"] == 10:
                witnessed.set()
            return value
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(output), \
             patch.object(recover.build_progress, "event", side_effect=event):
            with recover.observe_export_log(self.path):
                self.append(b"ExportProgress : (6/10) Exporting 'real file'\n")
                self.assertTrue(witnessed.wait(2), "Progress must arrive before export request returns")
        self.assertEqual(self.events(output)[-1]["done"], 6)
        self.assertFalse(any(thread.name == "quest-export-progress" for thread in threading.enumerate()))

    def test_disabled_cli_has_no_observer_or_log_read(self):
        with patch.dict(os.environ, {recover.build_progress.ENV: "0"}), \
             patch.object(recover, "ExportLogProgress", side_effect=AssertionError("CLI needs no progress observer")):
            with recover.observe_export_log(self.path): pass


if __name__ == "__main__": unittest.main()
