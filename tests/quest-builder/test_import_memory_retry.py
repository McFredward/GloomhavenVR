"""Real child-process controls for retained, bounded initial-import memory retry.

The fixture executable is named Unity but runs a tiny Python program. It emits
synthetic native allocation diagnostics through real current Editor/worker logs;
this tests subprocess/retry ownership, not an actual Unity allocation failure.
"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder

retention_spec = importlib.util.spec_from_file_location("quest_import_retry_retention", ROOT / "scripts/town_run_retention.py")
retention = importlib.util.module_from_spec(retention_spec)
retention_spec.loader.exec_module(retention)

GIB = 1024 ** 3
IMPORT_METHOD = "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk"
MEMORY_DIAGNOSTIC = "Could not allocate memory: System out of memory"


@unittest.skipUnless(os.name == "posix", "Executable-script fixture needs POSIX shebang execution")
class ImportMemoryRetry(unittest.TestCase):
    def setUp(self):
        base = ROOT / ".planning/debug/quest-import-memory"
        base.mkdir(parents=True, exist_ok=True)
        self.run = Path(tempfile.mkdtemp(prefix="run-", dir=base))
        self.project = self.run / "project with spaces"
        for name in ("Assets", "Packages", "ProjectSettings", "Library/Bee", "Temp", "Logs"):
            (self.project / name).mkdir(parents=True)
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
        (self.project / "Packages/manifest.json").write_text('{"dependencies":{}}')
        (self.project / "Assets/retained.txt").write_text("unchanged asset input")
        self.saved = self.project / "Library/Bee/retained.o"
        self.saved.write_bytes(b"already imported original content")
        self.saved_stamp = self.saved.stat().st_mtime_ns
        self.lock = self.project / "Temp/UnityLockfile"
        self.lock.write_bytes(b"inactive fixture lock must not be removed")
        self.lock_stamp = self.lock.stat().st_mtime_ns
        self.editor = self.run / "Unity"
        self.editor.write_text("#!" + sys.executable + "\n" + FIXTURE)
        self.editor.chmod(0o700)
        self.log = self.run / "launch.log"
        self.native = self.run / "editor.log"
        self.counter = self.run / "attempts.json"
        self.harness = self.run / "harness.log"
        self.harness.write_text("evidence: " + str(self.run) + "\n")
        self.events = []
        self.cleanup_exit_code = 1
        self.environment = patch.dict(os.environ, {"GHVRQ_WIZARD_PROGRESS": "1", "GHVRQ_BUILD_JOBS": "9",
            "GHVRQ_RESOURCE_EVIDENCE_ROOT": str(self.run / "resources")})
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def tearDown(self):
        # command uses real subprocess.run, whose completed child is reaped even
        # when a negative control returns nonzero. No foreign process is killed.
        if self.counter.exists():
            attempts = json.loads(self.counter.read_text())
            for attempt in attempts:
                with self.assertRaises(ProcessLookupError): os.kill(attempt["pid"], 0)
            (self.run / "observed-events.json").write_text(json.dumps(self.events))
        cleaned = retention.cleanup_completed_run("quest-import-memory-retry", self.cleanup_exit_code,
            self.harness, ROOT, process_reaped=True)
        (self.run / "cleanup.json").write_text(json.dumps(cleaned))
        self.assertFalse(cleaned["skipped_paths"])

    def execute(self, scenario, *, serial=False, method=IMPORT_METHOD):
        argv = [str(self.editor), "-batchmode", "-quit", "-projectPath", str(self.project),
                "-buildTarget", "Android", "-executeMethod", method, "-logFile", str(self.native)]
        if serial:
            argv += ["-refreshImportMode", "InProcess", "-desiredWorkerCount", "1", "-job-worker-count", "1"]
        env = {**os.environ, "QUEST_FIXTURE_SCENARIO": scenario, "QUEST_FIXTURE_COUNTER": str(self.counter)}
        snapshot = {"schema": 1, "platform": "win32", "logicalCpus": 16, "effectiveCpus": 16,
            "totalMemoryBytes": 64 * GIB, "availableMemoryBytes": 48 * GIB,
            "commitHeadroomBytes": 48 * GIB, "diskFreeBytes": 100 * GIB, "warnings": []}
        original_event = builder.build_progress.event
        def event(*args, **kwargs):
            value = original_event(*args, **kwargs)
            self.events.append(value)
            return value
        with patch.object(builder.host_resources, "detect_host", return_value=snapshot), \
             patch.object(builder.build_progress, "event", side_effect=event), \
             patch.object(builder.subprocess, "run", wraps=subprocess.run) as actual_children, \
             contextlib.redirect_stdout(io.StringIO()):
            try:
                output = builder.command(argv, self.log, cwd=self.project, env=env)
                self.cleanup_exit_code = 0
                return output
            finally:
                self.child_calls = actual_children.call_count

    def attempts(self):
        return json.loads(self.counter.read_text())

    def assert_retained(self):
        self.assertEqual((self.saved.read_bytes(), self.saved.stat().st_mtime_ns),
                         (b"already imported original content", self.saved_stamp))
        self.assertEqual((self.lock.read_bytes(), self.lock.stat().st_mtime_ns),
                         (b"inactive fixture lock must not be removed", self.lock_stamp))
        self.assertEqual((self.project / "Assets/retained.txt").read_text(), "unchanged asset input")

    def assert_one_serial_retry(self):
        rows = self.attempts()
        self.assertEqual(self.child_calls, 2)
        self.assertEqual([row["mode"] for row in rows], ["OutOfProcessPerQueue", "InProcess"])
        self.assertEqual([row["desired"] for row in rows], ["9", "1"])
        self.assertEqual([row["jobs"] for row in rows], ["9", "1"])
        self.assertEqual([row["method"] for row in rows], [IMPORT_METHOD, IMPORT_METHOD])
        self.assertEqual([row["target"] for row in rows], ["Android", "Android"])
        self.assertEqual([row["project"] for row in rows], [str(self.project), str(self.project)])
        self.assertEqual(sum(row["phase"] == "unity-import-memory-retry" for row in self.events), 1)
        self.assertIn("parallel child failed", (self.run / "launch.memory-attempt-1.log").read_text())
        self.assertIn("serial import succeeded", self.log.read_text())
        self.assertTrue((self.run / "editor.memory-attempt-1.log").is_file())
        self.assert_retained()

    def test_real_child_native_log_oom_retries_once_with_library_and_failed_logs_retained(self):
        output = self.execute("native-memory")
        self.assertIn("serial import succeeded", output)
        self.assert_one_serial_retry()
        self.assertIn(MEMORY_DIAGNOSTIC, (self.run / "editor.memory-attempt-1.log").read_text())
        self.assertNotIn(MEMORY_DIAGNOSTIC, (self.run / "launch.memory-attempt-1.log").read_text())

    def test_real_child_current_worker_only_allocation_diagnostic_selects_serial_retry(self):
        self.execute("worker-memory")
        self.assert_one_serial_retry()
        self.assertNotIn(MEMORY_DIAGNOSTIC, (self.run / "editor.memory-attempt-1.log").read_text())
        self.assertIn(MEMORY_DIAGNOSTIC, (self.project / "Logs/AssetImportWorker0.log").read_text())

    def test_stale_worker_diagnostic_cannot_turn_an_ordinary_error_into_a_memory_retry(self):
        worker = self.project / "Logs/AssetImportWorker0.log"
        worker.write_text(MEMORY_DIAGNOSTIC)
        os.utime(worker, (1, 1))
        with self.assertRaises(builder.BuildError): self.execute("ordinary-error")
        self.assertEqual(self.child_calls, 1)
        self.assertEqual(len(self.attempts()), 1)
        self.assertFalse((self.run / "launch.memory-attempt-1.log").exists())
        self.assertFalse(any(row["phase"] == "unity-import-memory-retry" for row in self.events))
        self.assert_retained()

    def test_real_child_ordinary_compile_error_does_not_retry_or_discard_library(self):
        with self.assertRaises(builder.BuildError): self.execute("ordinary-error")
        self.assertEqual((self.child_calls, len(self.attempts())), (1, 1))
        self.assertIn("error CS0117", self.native.read_text())
        self.assertFalse((self.run / "launch.memory-attempt-1.log").exists())
        self.assert_retained()

    def test_already_serial_memory_failure_does_not_duplicate_retry(self):
        with self.assertRaises(builder.BuildError): self.execute("always-memory", serial=True)
        self.assertEqual((self.child_calls, len(self.attempts())), (1, 1))
        self.assertEqual(self.attempts()[0]["mode"], "InProcess")
        self.assertFalse((self.run / "launch.memory-attempt-1.log").exists())
        self.assert_retained()

    def test_second_serial_allocation_failure_stops_without_unbounded_retries(self):
        with self.assertRaises(builder.BuildError): self.execute("always-memory")
        self.assertEqual((self.child_calls, len(self.attempts())), (2, 2))
        self.assertEqual([row["mode"] for row in self.attempts()], ["OutOfProcessPerQueue", "InProcess"])
        self.assertEqual(sum(row["phase"] == "unity-import-memory-retry" for row in self.events), 1)
        self.assertFalse((self.run / "launch.memory-attempt-2.log").exists())
        self.assert_retained()

    def test_player_memory_failure_remains_owned_by_native_supervisor(self):
        with self.assertRaises(builder.BuildError):
            self.execute("always-memory", method="GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build")
        self.assertEqual((self.child_calls, len(self.attempts())), (1, 1))
        self.assertFalse(any(row["phase"] == "unity-import-memory-retry" for row in self.events))
        self.assert_retained()


FIXTURE = r'''
import json, os, sys
from pathlib import Path
args = sys.argv[1:]
def option(name): return args[args.index(name) + 1]
project = Path(option('-projectPath'))
counter = Path(os.environ['QUEST_FIXTURE_COUNTER'])
rows = json.loads(counter.read_text()) if counter.exists() else []
row = dict(pid=os.getpid(), mode=option('-refreshImportMode'), desired=option('-desiredWorkerCount'),
           jobs=option('-job-worker-count'), method=option('-executeMethod'), target=option('-buildTarget'), project=str(project))
rows.append(row)
counter.write_text(json.dumps(rows))
assert (project / 'Library/Bee/retained.o').read_bytes() == b'already imported original content'
scenario = os.environ['QUEST_FIXTURE_SCENARIO']
native = Path(option('-logFile'))
if scenario == 'ordinary-error':
    native.write_text('error CS0117: Missing captured API member\n')
    print('ordinary child failed', flush=True)
    sys.exit(1)
if scenario == 'always-memory' or row['mode'] != 'InProcess':
    diagnostic = 'Could not allocate memory: System out of memory\n'
    if scenario == 'worker-memory':
        (project / 'Logs/AssetImportWorker0.log').write_text(diagnostic)
        native.write_text('Import child exited unsuccessfully\n')
    else:
        native.write_text(diagnostic)
    print('parallel child failed', flush=True)
    sys.exit(1)
native.write_text('SDK compilation succeeded with retained imports\n')
print('serial import succeeded', flush=True)
'''


if __name__ == '__main__': unittest.main()
