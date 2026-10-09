"""Measured native scheduling, retained retry work and source-scope controls."""
import contextlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import host_resources
import native_admission
import preparation_identity
import progress
import storage

GIB = 1024 ** 3


def captured_host():
    return {"platform": "win32", "totalMemoryBytes": 68438433792,
        "availableMemoryBytes": 38776823808, "commitHeadroomBytes": 29501243392,
        "effectiveCpus": 16, "logicalCpus": 16, "affinityCpus": 16,
        "diskFreeBytes": 301015887872, "warnings": []}


class NativeAdmissionTests(unittest.TestCase):
    def test_captured_host_uses_parallel_measured_profile_without_refusal(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(host_resources, "detect_host", return_value=captured_host()), contextlib.redirect_stdout(io.StringIO()):
            policy = native_admission.require_capacity(host_resources, progress, Path(temporary), target="game")
        self.assertEqual(policy["jobs"], 9)
        self.assertTrue(policy["nativeLaunchAllowed"])
        self.assertLessEqual(policy["largestWorkerReserveBytes"], 2 * GIB)

    def test_small_or_unknown_host_still_selects_a_single_worker(self):
        for available, commit in ((3 * GIB, 2 * GIB), (None, None)):
            host = {**captured_host(), "totalMemoryBytes": 8 * GIB,
                "availableMemoryBytes": available, "commitHeadroomBytes": commit}
            with tempfile.TemporaryDirectory() as temporary, patch.object(host_resources, "detect_host", return_value=host), contextlib.redirect_stdout(io.StringIO()):
                policy = native_admission.require_capacity(host_resources, progress, Path(temporary), target="game")
            self.assertEqual(policy["jobs"], 1)
            self.assertTrue(policy["nativeLaunchAllowed"])

    def test_actual_cli_continues_to_retained_preparation_on_captured_machine(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); output = root / "owned-output"
            original = root / "owned-game"; original.mkdir()
            (original / "original.bin").write_bytes(b"owned original bytes")
            dependencies = types.SimpleNamespace(python_environment=lambda *a, **k: Path(sys.executable))
            with patch.dict(sys.modules, dependencies=dependencies), patch.object(builder, "game_data", return_value=original), \
                patch.object(builder, "inspect_inputs", return_value={"inputKey": "a" * 64}), \
                patch.object(builder.host_resources, "detect_host", return_value=captured_host()), \
                patch.object(builder, "snapshot_inputs", return_value=(root / "source", original)) as snapshot, \
                patch.object(builder, "prepare", return_value=root / "project") as prepare, \
                patch.object(builder, "build") as build, contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                result = builder.main(["build", "--game-root", str(original), "--output-root", str(output)])
            self.assertEqual(result, 0)
            snapshot.assert_called_once(); prepare.assert_called_once(); build.assert_called_once()
            self.assertEqual((original / "original.bin").read_bytes(), b"owned original bytes")
            self.assertFalse((output / "last-failure.json").exists())

    def test_process_local_flags_keep_depth_optimization_and_existing_linker(self):
        base = {"CUSTOM": "preserved", "IL2CPP_ADDITIONAL_ARGS": "--compiler-flags=-g"}
        value = native_admission.player_environment(base, target="game")
        self.assertEqual(value["IL2CPP_ADDITIONAL_ARGS"], '--compiler-flags="-fbracket-depth=1024 -gline-tables-only"')
        self.assertEqual(value["CUSTOM"], "preserved")
        self.assertEqual(base["IL2CPP_ADDITIONAL_ARGS"], "--compiler-flags=-g")
        self.assertEqual(native_admission.player_environment(base, target="diagnostic"), base)
        self.assertNotIn("-O0", value["IL2CPP_ADDITIONAL_ARGS"])

    def test_real_failed_child_then_resume_preserves_native_outputs_and_logs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "unity-launch-a.log"; compiler = root / "unity-build-a.log"
            saved = root / "Library/Bee/already-built.o"; saved.parent.mkdir(parents=True); saved.write_bytes(b"completed native object")
            stamp = saved.stat().st_mtime_ns
            script = root / "pending_native.py"
            script.write_text("import os,pathlib,sys\np=pathlib.Path('attempt')\nn=int(p.read_text())+1 if p.exists() else 1\np.write_text(str(n))\npathlib.Path('threads-'+str(n)).write_text(os.environ['GHVRQ_BEE_THREADS'])\nprint('LLVM ERROR: out of memory' if n==1 else 'compiled pending unit')\nsys.exit(1 if n==1 else 0)\n")
            def execute(argv, path, *, env):
                with path.open("w") as stream:
                    result = subprocess.run(argv, env=env, cwd=root, stdout=stream, stderr=subprocess.STDOUT)
                if result.returncode: raise storage.BuildError("native process exited")
                return path.read_text()
            events = []
            observer = types.SimpleNamespace(event=lambda phase, **kw: events.append({"phase": phase, **kw}))
            resources = types.SimpleNamespace(detect_host=lambda _: captured_host(), phase_budget=lambda *_: {"jobs": 15}, emit_resource_evidence=lambda *a: None)
            result = native_admission.run_player(resources, observer, root, {"jobs": 8}, execute, [sys.executable, str(script)], log,
                env={**__import__('os').environ}, compiler_log=compiler)
            self.assertIn("compiled pending unit", result)
            self.assertEqual([(root / ('threads-'+str(n))).read_text() for n in (1, 2)], ["8", "4"])
            self.assertEqual((saved.read_bytes(), saved.stat().st_mtime_ns), (b"completed native object", stamp))
            self.assertIn("out of memory", (root / "unity-launch-a.memory-attempt-1.log").read_text())
            self.assertEqual([e["status"] for e in events], ["start", "complete"])

    def test_single_worker_waits_for_real_headroom_then_resumes_without_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "launch.log"; compiler = root / "compiler.log"
            events, sleeps = [], []
            capacities = iter([1, 1, 3, 3, 3])
            resources = types.SimpleNamespace(detect_host=lambda _: {"availableMemoryBytes": next(capacities) * GIB},
                phase_budget=lambda *_: {"jobs": 1}, emit_resource_evidence=lambda *a: None)
            observer = types.SimpleNamespace(event=lambda phase, **kw: events.append({"phase": phase, **kw}))
            attempts = 0
            def execute(*_args, **_kwargs):
                nonlocal attempts
                attempts += 1
                if attempts == 1:
                    compiler.write_text("System.OutOfMemoryException")
                    raise storage.BuildError("allocation failed")
                return "done"
            result = native_admission.run_player(resources, observer, root, {"jobs": 1}, execute, [], log,
                env={}, compiler_log=compiler, sleep=lambda seconds: sleeps.append(seconds))
            self.assertEqual(result, "done"); self.assertEqual(attempts, 2); self.assertEqual(sleeps, [5, 5])
            self.assertTrue(any(e["phase"] == "native-memory-wait" for e in events))
            self.assertFalse(any(e["status"] == "failed" for e in events))

    def test_nonmemory_errors_are_not_hidden_by_retries(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "launch.log"; log.write_text("error: no matching function for call")
            attempts = []
            def execute(*_args, **_kwargs):
                attempts.append(1); raise storage.BuildError("real compiler defect")
            resources = types.SimpleNamespace(detect_host=lambda _: {})
            with self.assertRaisesRegex(storage.BuildError, "real compiler defect"):
                native_admission.run_player(resources, progress, root, {"jobs": 1}, execute, [], log, env={}, compiler_log=root / "absent.log")
            self.assertEqual(len(attempts), 1)
            self.assertFalse(native_admission.memory_failure(log, error=storage.BuildError("native exited with 137; inspect log")))
            self.assertTrue(native_admission.memory_failure(log, error=storage.BuildError("Unity.exe exited with -1073741801; inspect log")))
            log.write_text("A pool allocation failed; using fallback\nerror: no matching function for call")
            self.assertFalse(native_admission.memory_failure(log))
            log.write_text("Could not allocate memory: System out of memory!\nTrying to allocate: 1024B")
            self.assertTrue(native_admission.memory_failure(log))
            error = storage.BuildError("Cannot execute Unity; verify selected tool")
            error.__cause__ = OSError(__import__('errno').ENOMEM, "out of memory")
            self.assertTrue(native_admission.memory_failure(root / "missing", error=error))

    def test_windows_shared_log_does_not_abort_memory_recovery(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "launch.log"; log.write_text("LLVM ERROR: out of memory")
            with patch.object(Path, "replace", side_effect=PermissionError("Windows observer denies delete")):
                native_admission._retain_logs((log,), 1)
            self.assertEqual((root / "launch.memory-attempt-1.log").read_text(), log.read_text())
            with patch.object(Path, "replace", side_effect=PermissionError), patch.object(Path, "write_bytes", side_effect=PermissionError), contextlib.redirect_stdout(io.StringIO()):
                native_admission._retain_logs((log,), 1)

    def test_single_worker_on_16gib_can_resume_with_unchanged_initial_capacity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "launch.log"
            host = {"totalMemoryBytes": 16 * GIB, "availableMemoryBytes": 14 * GIB, "commitHeadroomBytes": 14 * GIB}
            resources = types.SimpleNamespace(detect_host=lambda _: host, phase_budget=lambda *_: {"jobs": 1}, emit_resource_evidence=lambda *a: None)
            calls, waits = [], []
            def execute(argv, *_args, **kwargs):
                calls.append(argv)
                if len(calls) == 1:
                    log.write_text("LLVM ERROR: out of memory"); raise storage.BuildError("allocation failed")
                return "retained native work finished"
            def wait(seconds):
                waits.append(seconds)
                self.assertLessEqual(len(waits), 1, "No unattainable free-before-launch +25% threshold")
            result = native_admission.run_player(resources, progress, root, {"jobs": 1}, execute, ["Unity"], log,
                env={}, compiler_log=root / "absent", sleep=wait)
            self.assertIn("finished", result)
            self.assertEqual(calls[1][-2:], ["-job-worker-count", "1"])

    def test_fatal_native_retry_restores_parked_content_inside_outer_transaction(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("native_retry_content_fixture", ROOT / "tests/quest-builder/test_player_exclusion_recovery.py")
        fixtures = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixtures)
        fixture = fixtures.PlayerExclusionRecovery("test_without_outer_transaction_native_directory_zip_and_meta_restore")
        fixture.setUp(); self.addCleanup(fixture.tearDown)
        cached = fixture.project / "Library/Bee/cached.o"; cached.parent.mkdir(parents=True); cached.write_bytes(b"retain original native object")
        log = fixture.output / "launch.log"; calls, deliveries = [], []
        pending = fixture.output / "cache/project-content-transactions" / fixture.project.name / "pending.json"
        def execute(*_args, **_kwargs):
            calls.append(1)
            if len(calls) == 1:
                fixture.exclude()
                log.write_text("Could not allocate memory: System out of memory!")
                raise storage.BuildError("fatal native allocation")
            self.assertTrue(pending.is_file(), "Outer original content backup remains owned until final build acceptance")
            self.assertEqual(fixture.archive.read_bytes(), b"owned archive entry bytes")
            self.assertTrue(fixture.native.is_dir())
            self.assertEqual(cached.read_bytes(), b"retain original native object")
            return "completed pending native work"
        resources = types.SimpleNamespace(detect_host=lambda _: captured_host(), phase_budget=lambda *_: {"jobs": 4}, emit_resource_evidence=lambda *a: None)
        with storage.project_content_transaction(fixture.output, fixture.project, fixture.key):
            result = native_admission.run_player(resources, progress, fixture.output, {"jobs": 8}, execute, ["Unity"], log,
                env={}, compiler_log=fixture.output / "absent", project=fixture.project,
                recover_delivery=lambda: deliveries.append(1))
        self.assertIn("completed", result); self.assertEqual(deliveries, [1]); self.assertFalse(pending.exists())

    def test_wait_is_cancellable_and_retains_all_prior_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); log = root / "launch.log"; saved = root / "cached.o"; saved.write_bytes(b"keep")
            def execute(*_args, **_kwargs):
                log.write_text("LLVM ERROR: out of memory"); raise storage.BuildError("allocation failed")
            def cancel(_seconds): raise KeyboardInterrupt
            resources = types.SimpleNamespace(detect_host=lambda _: {"availableMemoryBytes": GIB},
                phase_budget=lambda *_: {"jobs": 1}, emit_resource_evidence=lambda *a: None)
            with self.assertRaises(KeyboardInterrupt):
                native_admission.run_player(resources, progress, root, {"jobs": 1}, execute, [], log, env={}, compiler_log=root / "absent", sleep=cancel)
            self.assertEqual(saved.read_bytes(), b"keep")

    def test_reviewed_late_compiler_changes_do_not_invalidate_preparation(self):
        previous = subprocess.check_output(["git", "show", "a1fec28b3:" + preparation_identity.BUILDER], cwd=ROOT)
        current = (ROOT / preparation_identity.BUILDER).read_bytes()
        self.assertEqual(preparation_identity.builder_producer_digest(previous), preparation_identity.builder_producer_digest(current))
        for before, after in (
            (b"target=args.target)", b"target='unqualified')"),
            (b"compiler_log=output /", b"compiler_log=other_output /"),
            (b"    def generate_files(resume):", b"    def generate_files(resume):\n        native_admission.player_environment({}, target=args.target)"),
        ):
            changed = current.replace(before, after)
            self.assertNotEqual(current, changed)
            self.assertNotEqual(preparation_identity.builder_producer_digest(current), preparation_identity.builder_producer_digest(changed))


if __name__ == "__main__": unittest.main()
