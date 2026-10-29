"""Small end-to-end checks for generated town test evidence retention."""

import builtins
from contextlib import redirect_stdout
import errno
import importlib.util
from io import StringIO
import json
import os
from pathlib import Path
import signal
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'scripts'))
import town_run_retention as retention  # noqa: E402
from town_run_retention import MARKER, cleanup_completed_run, record_success  # noqa: E402

spec = importlib.util.spec_from_file_location('run_test_suites', ROOT / 'scripts/run-test-suites.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class TownRunRetentionTests(unittest.TestCase):
    def test_windows_import_and_lock_fallback_without_fcntl(self):
        class FakeMsvcrt:
            LK_NBLCK = 1
            LK_UNLCK = 2

            def __init__(self):
                self.busy = 2
                self.calls = []

            def locking(self, fd, mode, length):
                self.calls.append((mode, length))
                if mode == self.LK_NBLCK and self.busy:
                    self.busy -= 1
                    raise OSError(errno.EACCES, 'held by another process')

        fake = FakeMsvcrt()
        original_import = builtins.__import__

        def windows_import(name, *args, **kwargs):
            if name == 'fcntl':
                raise ImportError('Windows has no fcntl')
            if name == 'msvcrt':
                return fake
            return original_import(name, *args, **kwargs)

        win_spec = importlib.util.spec_from_file_location('town_run_retention_windows',
                                                         ROOT / 'scripts/town_run_retention.py')
        win_retention = importlib.util.module_from_spec(win_spec)
        with patch.object(builtins, '__import__', side_effect=windows_import):
            win_spec.loader.exec_module(win_retention)
        self.assertIsNone(win_retention._fcntl)
        self.assertIs(win_retention._msvcrt, fake)
        with tempfile.TemporaryFile(mode='w+b') as stream:
            with patch.object(win_retention.time, 'sleep') as sleep:
                with self.assertRaises(BlockingIOError):
                    win_retention.acquire_file_lock(stream, blocking=False)
                win_retention.acquire_file_lock(stream)
                win_retention.release_file_lock(stream)
                sleep.assert_called_once_with(.05)
            self.assertEqual([(fake.LK_NBLCK, 1)] * 3 + [(fake.LK_UNLCK, 1)], fake.calls)
            self.assertEqual(b'\0', stream.read(1))

        # The hyphenated runner is also imported by source-bound tests. Neither
        # import may require fcntl when the fallback is active.
        runner_spec = importlib.util.spec_from_file_location('run_test_suites_windows_import',
                                                             ROOT / 'scripts/run-test-suites.py')
        win_runner = importlib.util.module_from_spec(runner_spec)
        with patch.dict(sys.modules, town_run_retention=win_retention):
            with patch.object(builtins, '__import__', side_effect=windows_import):
                runner_spec.loader.exec_module(win_runner)
        self.assertIs(win_runner.acquire_file_lock, win_retention.acquire_file_lock)

    def test_success_marking_preserves_every_previous_run(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            output = checkout / '.planning/debug/town-activity'
            output.mkdir(parents=True)
            reports = Path(temp) / 'reports'
            reports.mkdir()

            def run(name, suite, code=0):
                path = output / name
                path.mkdir()
                (path / 'payload').write_text('fixture')
                log = reports / (name + '.log')
                log.write_text(f'PASS: suite completed; evidence: {path}\n')
                record_success(suite, code, log, checkout)
                return path

            old = run('run-aaaaaaaa', 'town-activity')
            failed = run('run-bbbbbbbb', 'town-activity', code=1)
            portable = run('run-cccccccc', 'town-activity-portable')
            pinned = run('run-dddddddd', 'town-activity')
            (pinned / '.keep').symlink_to('missing-pin-target')
            latest = run('run-eeeeeeee', 'town-activity')
            self.assertTrue(old.exists())
            self.assertTrue(failed.exists())
            self.assertFalse((failed / MARKER).exists())
            self.assertTrue(portable.exists())
            self.assertTrue(pinned.exists())
            self.assertTrue(latest.exists())

    def test_new_success_preserves_old_evidence_and_only_completion_cleans_caches(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            output = checkout / '.planning/debug/town-activity'
            borrowed = Path(temp) / 'borrowed'
            borrowed.mkdir()
            (borrowed / 'Test.csproj').write_text('<Project />')
            (borrowed / 'bin').mkdir()
            (borrowed / 'bin/input.dll').write_bytes(b'borrowed input')
            evidence = {'run.log': b'original log', 'results.json': b'{"passed":true}',
                        'Sources/Native.cs': b'// original source',
                        'manifests/inputs.sha256': b'original input hashes',
                        'captures/head.png': b'original capture'}
            runs, logs, marked = [], [], []
            for index, name in enumerate(('run-aaaaaaaa', 'run-bbbbbbbb')):
                run = output / name
                project = run / 'production'
                project.mkdir(parents=True)
                (project / 'Test.csproj').write_text('<Project />')
                for cache in ('bin', 'obj'):
                    (project / cache).mkdir()
                    (project / cache / 'rebuild.dll').write_bytes(b'regenerable cache')
                (project / 'bin/compiler.log').write_bytes(b'exact compiler receipt')
                pinned = run / 'pinned'
                (pinned / 'bin').mkdir(parents=True)
                (pinned / 'Test.csproj').write_text('<Project />')
                (pinned / 'bin/pinned.dll').write_bytes(b'pinned input')
                (pinned / '.keep').symlink_to('missing-pin-target')
                (run / 'borrowed').symlink_to(borrowed, target_is_directory=True)
                for relative, payload in evidence.items():
                    path = run / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(payload)
                log = Path(temp) / f'suite-{index}.log'
                log.write_text(f'PASS; evidence: {run}\n')
                runs.append(run)
                logs.append(log)
                marked.append(record_success('town-activity', 0, log, checkout))

            for run, log, marker in zip(runs, logs, marked):
                self.assertTrue(run.is_dir(), 'A newer success must preserve older test evidence')
                for relative, payload in evidence.items():
                    self.assertEqual(payload, (run / relative).read_bytes())
                self.assertEqual((run, 0), marker)
                self.assertEqual({'schema_version': 1, 'suite': 'town-activity'},
                                 json.loads((run / MARKER).read_text()))
                project = run / 'production'
                self.assertTrue((project / 'bin/rebuild.dll').exists())
                self.assertTrue((project / 'obj/rebuild.dll').exists())
                pending = cleanup_completed_run('town-activity', 0, log, checkout)
                self.assertEqual([], pending['removed_paths'])
                self.assertTrue((project / 'bin/rebuild.dll').exists())
                cleaned = cleanup_completed_run('town-activity', 0, log, checkout, process_reaped=True)
                self.assertEqual({str(project / 'bin'), str(project / 'obj')}, set(cleaned['removed_paths']))
                self.assertEqual([], cleaned['skipped_paths'])
                self.assertFalse((project / 'bin').exists())
                self.assertFalse((project / 'obj').exists())
                self.assertTrue((project / 'Test.csproj').exists())
                self.assertEqual(b'exact compiler receipt',
                                 (run / retention.REPORT_DIRECTORY / 'production/bin/compiler.log').read_bytes())
                for relative, payload in evidence.items():
                    self.assertEqual(payload, (run / relative).read_bytes())
                self.assertTrue((run / 'pinned/.keep').is_symlink())
                self.assertEqual(b'pinned input', (run / 'pinned/bin/pinned.dll').read_bytes())
                self.assertTrue((run / 'borrowed').is_symlink())
                self.assertEqual(b'borrowed input', (borrowed / 'bin/input.dll').read_bytes())

    def test_ignores_unmarked_runs_and_all_paths_outside_known_generated_root(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            output = checkout / '.planning/debug/town-service-interaction'
            output.mkdir(parents=True)
            legacy = output / 'run-aaaaaaaa'
            legacy.mkdir()
            remote = checkout / '.planning/debug/remote/run-bbbbbbbb'
            remote.mkdir(parents=True)
            target = Path(temp) / 'outside'
            target.mkdir()
            (output / 'run-cccccccc').symlink_to(target, target_is_directory=True)
            log = Path(temp) / 'suite.log'
            for path in (remote, output / 'run-cccccccc'):
                log.write_text(f'PASS; evidence: {path}\n')
                self.assertEqual((None, 0), record_success('town-service-interaction', 0, log, checkout))
            valid = output / 'run-dddddddd'
            valid.mkdir()
            log.write_text(f'PASS; evidence: {valid}\n')
            record_success('town-service-interaction', 0, log, checkout)
            self.assertTrue(legacy.exists())
            self.assertTrue(remote.exists())
            self.assertTrue(target.exists())

    def test_does_not_follow_a_worktree_debug_symlink(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            planning = checkout / '.planning'
            planning.mkdir(parents=True)
            shared_debug = Path(temp) / 'shared-debug'
            output = shared_debug / 'town-service-interaction'
            run = output / 'run-aaaaaaaa'
            run.mkdir(parents=True)
            (planning / 'debug').symlink_to(shared_debug, target_is_directory=True)
            log = Path(temp) / 'suite.log'
            log.write_text(f'PASS; evidence: {run}\n')
            self.assertEqual((None, 0), record_success('town-service-interaction', 0, log, checkout))
            self.assertTrue(run.exists())
            self.assertFalse((run / MARKER).exists())

    def test_accepts_native_audio_capitalized_evidence_label(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            run = checkout / '.planning/debug/town-native-audio/run-aaaaaaaa'
            run.mkdir(parents=True)
            log = Path(temp) / 'suite.log'
            log.write_text(f'PASS: production audio\nEvidence: {run}\n')
            self.assertEqual((run, 0), record_success('town-native-audio', 0, log, checkout))
            self.assertTrue((run / MARKER).exists())

    def test_runner_uses_exit_status_even_if_failed_suite_prints_pass(self):
        with tempfile.TemporaryDirectory() as temp:
            checkout = Path(temp) / 'checkout'
            checkout.mkdir()
            emitter = Path(temp) / 'emit.py'
            emitter.write_text(
                'from pathlib import Path\n'
                'import sys, tempfile\n'
                "root = Path.cwd() / '.planning/debug/town-service-interaction'\n"
                'root.mkdir(parents=True, exist_ok=True)\n'
                "run = Path(tempfile.mkdtemp(prefix='run-', dir=root))\n"
                "(run / 'payload').write_text('small fixture')\n"
                "print(f'PASS: deliberately printed for both outcomes; evidence: {run}', flush=True)\n"
                'sys.exit(int(sys.argv[1]))\n'
            )

            def execute(index, exit_code):
                suite = {'id': 'town-service-interaction', 'command': [sys.executable, str(emitter), str(exit_code)]}
                with redirect_stdout(StringIO()) as output:
                    code = runner.execute([suite], 1, Path(temp) / f'report-{index}', checkout,
                                          'test-manifest', 'local', (0, 1))
                self.assertNotIn('older successful runs', output.getvalue())
                return code

            self.assertEqual(0, execute(1, 0))
            output = checkout / '.planning/debug/town-service-interaction'
            first = next(path for path in output.glob('run-*') if (path / MARKER).exists())
            self.assertEqual(1, execute(2, 1))
            failed = next(path for path in output.glob('run-*') if path != first)
            self.assertFalse((failed / MARKER).exists())
            self.assertEqual(0, execute(3, 0))
            self.assertTrue(first.exists())
            self.assertTrue(failed.exists())
            self.assertEqual(2, len([path for path in output.glob('run-*') if (path / MARKER).exists()]))


class CompletedRunCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.checkout = Path(self.temp.name) / 'checkout'
        self.run = self.checkout / '.planning/debug/new-suite/nested/run-aaaaaaaa'
        self.run.mkdir(parents=True)
        self.log = Path(self.temp.name) / 'suite.log'
        self.log.write_text(f'Binding production; Evidence: {self.run}\n')

    def managed(self, project=None):
        project = project or self.run / 'production'
        project.mkdir(parents=True, exist_ok=True)
        (project / 'Test.csproj').write_text('<Project />')
        for name in ('bin', 'obj'):
            (project / name).mkdir()
            (project / name / 'rebuild.dll').write_bytes(b'regenerable cache' * 100)
        return project

    def unity(self, project=None):
        project = project or self.run / 'unity'
        (project / 'Assets').mkdir(parents=True)
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        for name in ('Library', 'Temp', 'Obj', 'Logs'):
            (project / name).mkdir()
            (project / name / 'rebuild.cache').write_bytes(b'unity cache' * 100)
        return project

    def cleanup(self, code=0, **kwargs):
        return cleanup_completed_run('new-unmapped-suite', code, self.log, self.checkout,
                                     process_reaped=kwargs.pop('process_reaped', True), **kwargs)

    def test_unmapped_suite_removes_only_managed_caches_and_retains_all_evidence(self):
        project = self.managed()
        evidence = {'results.json': b'{"passed":true}', 'source.cs': b'// handwritten',
                    'manifest.sha256': b'proof', 'capture.png': b'picture', 'run.log': b'log'}
        for name, payload in evidence.items():
            (self.run / name).write_bytes(payload)
        old = self.run.parent / 'run-bbbbbbbb'
        old.mkdir()
        (old / 'bin').mkdir()
        (old / 'bin/old.cache').write_bytes(b'old run')
        result = self.cleanup(code=1)
        self.assertEqual(str(self.run), result['run'])
        self.assertEqual({str(project / 'bin'), str(project / 'obj')}, set(result['removed_paths']))
        self.assertGreater(result['removed_bytes'], 0)
        self.assertTrue((project / 'Test.csproj').exists())
        self.assertFalse((self.run / MARKER).exists())
        self.assertTrue((old / 'bin/old.cache').exists())
        for name, payload in evidence.items():
            self.assertEqual(payload, (self.run / name).read_bytes())
        self.assertEqual([], self.cleanup()['removed_paths'])  # Monitor/runner retry is harmless.

    def test_unity_requires_real_project_and_keeps_logs_with_byte_exact_cache_receipts(self):
        project = self.unity()
        receipts = {'Library/Editor.log': b'failure details\x00',
                    'Temp/Results/native.proof': b'exact assertion receipt',
                    'Obj/crash.dmp': b'crash bytes', 'Library/preview.png': b'image bytes'}
        for relative, payload in receipts.items():
            path = project / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(payload)
        result = self.cleanup(code=-signal.SIGTERM)
        self.assertEqual({str(project / name) for name in ('Library', 'Temp', 'Obj')}, set(result['removed_paths']))
        self.assertEqual(b'unity cache' * 100, (project / 'Logs/rebuild.cache').read_bytes())
        for relative, payload in receipts.items():
            retained = self.run / retention.REPORT_DIRECTORY / 'unity' / relative
            self.assertEqual(payload, retained.read_bytes())
        self.assertTrue((project / 'Assets').is_dir())
        self.assertTrue((project / 'ProjectSettings/ProjectVersion.txt').exists())

    def test_completion_flag_and_real_exit_code_are_required(self):
        project = self.managed()
        for code, reaped in ((0, False), (None, True), (True, True), (0, 1)):
            result = cleanup_completed_run('new-suite', code, self.log, self.checkout, process_reaped=reaped)
            self.assertEqual([], result['removed_paths'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        self.assertFalse((self.run / '.cache-cleanup.lock').exists())

    def test_missing_ambiguous_or_rejected_evidence_does_not_discover_other_runs(self):
        project = self.managed()
        other = self.run.parent / 'run-bbbbbbbb'
        self.managed(other)
        for text in ('nothing explicit\n', f'Evidence: {self.run}\nEvidence: {other}\n',
                     f'Evidence: {self.run}\nEvidence: /outside/run-cccccccc\n',
                     f'Evidence: {self.run.relative_to(self.checkout)}\n'):
            self.log.write_text(text)
            self.assertEqual([], self.cleanup()['removed_paths'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        self.assertTrue((other / 'bin/rebuild.dll').exists())

    def test_failed_compiler_exact_project_path_and_unity_project_argument_are_supported(self):
        project = self.managed()
        self.log.write_text(f'error CS: original failure [{project / "Test.csproj"}]\n')
        self.assertEqual(2, len(self.cleanup(code=1)['removed_paths']))
        unity = self.unity(self.run / 'project with spaces')
        self.log.write_text(f'Unity -batchmode -projectPath "{unity}" -quit\n')
        self.assertEqual(3, len(self.cleanup(code=1)['removed_paths']))

    def test_protected_hardware_roots_and_nested_project_paths_are_untouched(self):
        for name in ('remote', 'hardware', 'steam_frame', 'Steam-Frame'):
            protected = self.checkout / '.planning/debug' / name / 'run-bbbbbbbb'
            project = self.managed(protected)
            self.log.write_text(f'Evidence: {protected}\n')
            self.assertEqual([], self.cleanup()['removed_paths'])
            self.assertTrue((project / 'bin/rebuild.dll').exists())
        self.log.write_text(f'Evidence: {self.run}\n')
        remote = self.managed(self.run / 'remote')
        local = self.managed()
        self.assertEqual(2, len(self.cleanup()['removed_paths']))
        self.assertTrue((remote / 'bin/rebuild.dll').exists())
        self.assertFalse((local / 'bin').exists())

    def test_borrowed_debug_run_project_marker_and_cache_symlinks_are_protected(self):
        project = self.managed()
        outside = Path(self.temp.name) / 'outside'
        outside.mkdir()
        (outside / 'borrowed.cache').write_bytes(b'input')
        (project / 'bin/borrowed').symlink_to(outside, target_is_directory=True)
        result = self.cleanup()
        self.assertIn(str(project / 'bin'), result['skipped_paths'])
        self.assertTrue((project / 'bin/borrowed').is_symlink())
        self.assertEqual(b'input', (outside / 'borrowed.cache').read_bytes())
        (self.run / 'borrowed-project').symlink_to(outside, target_is_directory=True)
        (outside / 'Test.csproj').write_text('<Project />')
        (outside / 'bin').mkdir()
        (outside / 'bin/input').write_bytes(b'borrowed')
        self.cleanup()
        self.assertTrue((outside / 'bin/input').exists())
        linked = self.checkout / '.planning/debug/new-suite/run-bbbbbbbb'
        linked.symlink_to(self.run, target_is_directory=True)
        self.log.write_text(f'Evidence: {linked}\n')
        self.assertIsNone(self.cleanup()['run'])
        (project / 'Test.csproj').unlink()
        (project / 'Test.csproj').symlink_to(outside / 'Test.csproj')
        self.log.write_text(f'Evidence: {self.run}\n')
        self.assertEqual([], self.cleanup()['removed_paths'])

    def test_symlinked_category_or_debug_is_rejected_even_when_target_is_local(self):
        self.managed()
        linked = self.checkout / '.planning/debug/alias'
        linked.symlink_to(self.run.parent, target_is_directory=True)
        self.log.write_text(f'Evidence: {linked / self.run.name}\n')
        self.assertIsNone(self.cleanup()['run'])
        borrowed = Path(self.temp.name) / 'borrowed-checkout'
        (borrowed / '.planning').mkdir(parents=True)
        (borrowed / '.planning/debug').symlink_to(self.checkout / '.planning/debug', target_is_directory=True)
        evidence = borrowed / '.planning/debug/new-suite/nested/run-aaaaaaaa'
        self.log.write_text(f'Evidence: {evidence}\n')
        result = cleanup_completed_run('new-suite', 0, self.log, borrowed, process_reaped=True)
        self.assertEqual([], result['removed_paths'])

    def test_pins_at_run_project_cache_and_descendant_include_dangling_markers(self):
        for location in ('run', 'project', 'cache', 'descendant'):
            with self.subTest(location=location):
                project = self.managed(self.run / location)
                pin = {'run': self.run, 'project': project, 'cache': project / 'bin',
                       'descendant': project / 'bin/sub'}[location]
                pin.mkdir(parents=True, exist_ok=True)
                (pin / '.keep').symlink_to('missing-pin-target')
                result = self.cleanup()
                self.assertTrue((project / 'bin/rebuild.dll').exists())
                if location == 'run':
                    self.assertIsNone(result['run'])
                (pin / '.keep').unlink()
                self.cleanup()

    def test_cache_names_without_physical_project_markers_are_never_removed(self):
        for name in ('Library', 'Temp', 'Obj', 'bin', 'obj'):
            (self.run / name).mkdir()
            (self.run / name / 'input').write_bytes(b'manual input')
        self.assertEqual([], self.cleanup()['removed_paths'])
        unity = self.unity()
        (unity / 'ProjectSettings/ProjectVersion.txt').write_text('not a Unity project')
        self.assertEqual([], self.cleanup()['removed_paths'])
        (unity / 'ProjectSettings/ProjectVersion.txt').unlink()
        outside = Path(self.temp.name) / 'version'
        outside.write_text('m_EditorVersion: 2021.3.5f1\n')
        (unity / 'ProjectSettings/ProjectVersion.txt').symlink_to(outside)
        self.assertEqual([], self.cleanup()['removed_paths'])
        self.assertTrue((unity / 'Library/rebuild.cache').exists())

    def test_large_unreadable_or_changed_receipt_keeps_its_whole_cache(self):
        project = self.managed()
        report = project / 'bin/result.log'
        report.write_bytes(b'original important evidence')
        with patch.object(retention, 'MAX_RETAINED_REPORT_BYTES', 4):
            result = self.cleanup()
        self.assertIn(str(project / 'bin'), result['skipped_paths'])
        self.assertEqual(b'original important evidence', report.read_bytes())
        with patch.object(retention, '_digest', side_effect=PermissionError('unreadable evidence')):
            self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        archived = self.run / retention.REPORT_DIRECTORY / 'production/bin/result.log'
        archived.write_bytes(b'different bytes')
        self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
        self.assertTrue(report.exists())

    def test_unreadable_copy_does_not_delete_any_file_from_affected_cache(self):
        project = self.managed()
        report = project / 'bin/editor.log'
        report.write_bytes(b'important')
        with patch.object(retention, '_copy_report', side_effect=OSError('copy failed')):
            result = self.cleanup()
        self.assertIn(str(project / 'bin'), result['skipped_paths'])
        self.assertEqual(b'important', report.read_bytes())
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        archived = self.run / retention.REPORT_DIRECTORY / 'production/bin/editor.log'
        self.assertFalse(archived.exists())
        self.assertIn(str(project / 'bin'), self.cleanup()['removed_paths'])
        self.assertEqual(b'important', archived.read_bytes())

    def test_discovery_inventory_and_log_bounds_fail_closed(self):
        project = self.managed()
        with patch.object(retention, 'MAX_PROJECT_DIRECTORIES', 1):
            with self.assertRaises(ValueError):
                self.cleanup()
        self.assertTrue((project / 'bin').exists())
        with patch.object(retention, 'MAX_CACHE_ENTRIES', 0):
            self.assertEqual([], self.cleanup()['removed_paths'])
        with patch.object(retention, 'MAX_LOG_BYTES', 1):
            self.assertIsNone(self.cleanup()['run'])
        with patch.object(retention, 'MAX_PROJECT_ENTRIES', 0):
            with self.assertRaises(ValueError):
                self.cleanup()
        self.assertTrue((project / 'bin/rebuild.dll').exists())

    def test_pin_added_during_copy_retains_the_entire_cache(self):
        project = self.managed()
        report = project / 'bin/editor.log'
        report.write_bytes(b'important')
        original = retention._retain_reports

        def pin_after_copy(cache, run, files):
            copied = original(cache, run, files)
            if cache.name == 'bin':
                (cache / '.keep').touch()
            return copied

        with patch.object(retention, '_retain_reports', side_effect=pin_after_copy):
            self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        self.assertEqual(b'important', report.read_bytes())

    def test_symlinked_cleanup_lock_is_rejected(self):
        project = self.managed()
        outside = Path(self.temp.name) / 'borrowed-lock'
        outside.touch()
        (self.run / '.cache-cleanup.lock').symlink_to(outside)
        self.assertEqual([], self.cleanup()['removed_paths'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())

    def test_native_wrapper_evidence_cleans_only_the_innermost_unity_and_dotnet_run(self):
        outer = self.managed()
        inner = self.run / 'proof/run-_n4sc9uv'
        managed = self.managed(inner / 'production')
        unity = self.unity(inner / 'unity')
        (inner / 'results.json').write_bytes(b'exact proof results')
        (inner / 'manifest.sha256').write_bytes(b'original source hashes')
        (managed / 'bin/compiler.log').write_bytes(b'compiler receipt')
        (unity / 'Library/native.log').write_bytes(b'original native log')
        self.log.write_text(f'Evidence: {self.run}\nEvidence: {inner}\nEvidence: {inner}\n')
        result = self.cleanup()
        self.assertEqual(str(inner), result['run'])
        self.assertEqual({str(managed / name) for name in ('bin', 'obj')}
                         | {str(unity / name) for name in ('Library', 'Temp', 'Obj')},
                         set(result['removed_paths']))
        self.assertTrue((outer / 'bin/rebuild.dll').exists())
        self.assertTrue((outer / 'obj/rebuild.dll').exists())
        self.assertTrue((managed / 'Test.csproj').exists())
        self.assertTrue((unity / 'Assets').is_dir())
        self.assertTrue((unity / 'ProjectSettings/ProjectVersion.txt').exists())
        self.assertTrue((unity / 'Logs/rebuild.cache').exists())
        self.assertEqual(b'exact proof results', (inner / 'results.json').read_bytes())
        self.assertEqual(b'original source hashes', (inner / 'manifest.sha256').read_bytes())
        self.assertEqual(b'compiler receipt', (inner / retention.REPORT_DIRECTORY / 'production/bin/compiler.log').read_bytes())
        self.assertEqual(b'original native log', (inner / retention.REPORT_DIRECTORY / 'unity/Library/native.log').read_bytes())
        self.assertFalse((self.run / retention.REPORT_DIRECTORY).exists())

    def test_sibling_nested_run_references_remain_ambiguous(self):
        outer = self.managed()
        first = self.run / 'proof/run-bbbbbbbb'
        second = self.run / 'proof/run-cccccccc'
        self.managed(first)
        self.managed(second)
        self.log.write_text(f'Evidence: {self.run}\nEvidence: {first}\nEvidence: {second}\n')
        self.assertIsNone(self.cleanup()['run'])
        for project in (outer, first, second):
            self.assertTrue((project / 'bin/rebuild.dll').exists())

    def test_outer_run_pin_protects_explicitly_evidenced_nested_proof(self):
        nested = self.run / 'proof/run-bbbbbbbb'
        self.managed(nested)
        self.log.write_text(f'Evidence: {nested}\n')
        (self.run / '.keep').symlink_to('missing-pin-target')
        self.assertIsNone(self.cleanup()['run'])
        self.assertTrue((nested / 'bin/rebuild.dll').exists())
        (self.run / '.keep').unlink()
        original = retention._retain_reports

        def pin_outer_after_copy(cache, run, files):
            copied = original(cache, run, files)
            (self.run / '.keep').touch()
            return copied

        with patch.object(retention, '_retain_reports', side_effect=pin_outer_after_copy):
            self.assertEqual([], self.cleanup()['removed_paths'])
        self.assertTrue((nested / 'bin/rebuild.dll').exists())
        self.assertTrue((nested / 'obj/rebuild.dll').exists())

    def test_exact_nested_compiler_fallback_needs_its_physical_marker(self):
        outer = self.managed()
        nested = self.run / 'proof/run-bbbbbbbb'
        project = self.managed(nested / 'production')
        self.log.write_text(f'error CS [{project / "missing.csproj"}]\n')
        self.assertIsNone(self.cleanup(code=1)['run'])
        self.assertTrue((project / 'bin/rebuild.dll').exists())
        self.log.write_text(f'error CS [{project / "Test.csproj"}]\n')
        result = self.cleanup(code=1)
        self.assertEqual(str(nested), result['run'])
        self.assertEqual({str(project / name) for name in ('bin', 'obj')}, set(result['removed_paths']))
        self.assertTrue((outer / 'bin/rebuild.dll').exists())

    def test_outer_evidence_does_not_discover_another_generated_child_run(self):
        outer = self.managed()
        nested = self.run / 'proof/run-bbbbbbbb'
        self.managed(nested)
        result = self.cleanup()
        self.assertEqual({str(outer / name) for name in ('bin', 'obj')}, set(result['removed_paths']))
        self.assertTrue((nested / 'bin/rebuild.dll').exists())
        self.assertTrue((nested / 'obj/rebuild.dll').exists())

    def test_retained_evidence_symlink_and_pin_fail_closed(self):
        project = self.managed()
        (project / 'bin/result.log').write_bytes(b'preserve me')
        outside = Path(self.temp.name) / 'outside'
        outside.mkdir()
        archive = self.run / retention.REPORT_DIRECTORY
        archive.symlink_to(outside, target_is_directory=True)
        self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
        self.assertEqual([], list(outside.iterdir()))
        archive.unlink()
        archive.mkdir()
        (archive / '.keep').touch()
        self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
        self.assertTrue((project / 'bin/result.log').exists())

    def test_project_outputs_with_protected_hardware_child_are_skipped(self):
        project = self.managed()
        (project / 'bin/remote').mkdir()
        (project / 'bin/remote/Player.log').write_bytes(b'hardware evidence')
        result = self.cleanup()
        self.assertIn(str(project / 'bin'), result['skipped_paths'])
        self.assertEqual(b'hardware evidence', (project / 'bin/remote/Player.log').read_bytes())

    def test_source_manifest_and_capture_trees_stay_at_their_original_paths(self):
        for name in ('Sources', 'src', 'Manifests', 'Captures'):
            project = self.managed(self.run / ('case-' + name.lower()))
            tree = project / 'bin' / name
            tree.mkdir()
            (tree / 'original.data').write_bytes(b'original input or capture')
            self.assertIn(str(project / 'bin'), self.cleanup()['skipped_paths'])
            self.assertEqual(b'original input or capture', (tree / 'original.data').read_bytes())

    def test_runner_cleans_pass_fail_and_cancel_without_marking_failure_successful(self):
        emitter = Path(self.temp.name) / 'emit.py'
        ready = Path(self.temp.name) / 'ready'
        emitter.write_text(
            'from pathlib import Path\n'
            'import sys, tempfile, time\n'
            "root = Path.cwd() / '.planning/debug/new-runner-suite'\n"
            'root.mkdir(parents=True, exist_ok=True)\n'
            "run = Path(tempfile.mkdtemp(prefix='run-', dir=root))\n"
            "(run / 'Test.csproj').write_text('<Project />')\n"
            "(run / 'bin').mkdir()\n"
            "(run / 'bin/cache.dll').write_bytes(b'regenerable')\n"
            "(run / 'results.json').write_text('exact evidence')\n"
            "print(f'Evidence: {run}', flush=True)\n"
            "if sys.argv[1] == 'cancel':\n"
            '    Path(sys.argv[2]).touch()\n'
            '    time.sleep(30)\n'
            'else:\n'
            '    sys.exit(int(sys.argv[1]))\n'
        )
        suite = {'id': 'new-runner-suite', 'command': [sys.executable, str(emitter)]}
        for index, outcome in enumerate(('0', '1', 'cancel')):
            suite['command'] = [sys.executable, str(emitter), outcome, str(ready)]
            if outcome == 'cancel':
                def cancel_when_ready():
                    deadline = time.monotonic() + 5
                    while not ready.exists() and time.monotonic() < deadline:
                        time.sleep(.01)
                    if ready.exists():
                        os.kill(os.getpid(), signal.SIGTERM)
                thread = threading.Thread(target=cancel_when_ready)
                thread.start()
            report = Path(self.temp.name) / f'report-{index}'
            with redirect_stdout(StringIO()):
                code = runner.execute([suite], 1, report, self.checkout, 'test', 'local', (0, 1))
            if outcome == 'cancel':
                thread.join(timeout=6)
                self.assertFalse(thread.is_alive())
            self.assertEqual({'0': 0, '1': 1, 'cancel': 128 + signal.SIGTERM}[outcome], code)
            result = json.loads((report / 'results.json').read_text())
            self.assertEqual({'0': 'passed', '1': 'failed', 'cancel': 'cancelled'}[outcome], result['results'][0]['status'])
            self.assertEqual(outcome == '0', result['passed'])
        runs = list((self.checkout / '.planning/debug/new-runner-suite').glob('run-*'))
        self.assertEqual(3, len(runs))
        for run in runs:
            self.assertFalse((run / 'bin').exists())
            self.assertEqual('exact evidence', (run / 'results.json').read_text())
            self.assertFalse((run / MARKER).exists())


if __name__ == '__main__':
    unittest.main()
