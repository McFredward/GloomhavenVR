"""Small end-to-end checks for generated town test evidence retention."""

import builtins
from contextlib import redirect_stdout
import errno
import importlib.util
from io import StringIO
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import MARKER, record_success  # noqa: E402

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

    def test_successful_suite_replaces_only_its_previous_success(self):
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
            (pinned / '.keep').touch()
            latest = run('run-eeeeeeee', 'town-activity')
            self.assertFalse(old.exists())
            self.assertTrue(failed.exists())
            self.assertFalse((failed / MARKER).exists())
            self.assertTrue(portable.exists())
            self.assertTrue(pinned.exists())
            self.assertTrue(latest.exists())

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
                with redirect_stdout(StringIO()):
                    return runner.execute([suite], 1, Path(temp) / f'report-{index}', checkout,
                                          'test-manifest', 'local', (0, 1))

            self.assertEqual(0, execute(1, 0))
            output = checkout / '.planning/debug/town-service-interaction'
            first = next(path for path in output.glob('run-*') if (path / MARKER).exists())
            self.assertEqual(1, execute(2, 1))
            failed = next(path for path in output.glob('run-*') if path != first)
            self.assertFalse((failed / MARKER).exists())
            self.assertEqual(0, execute(3, 0))
            self.assertFalse(first.exists())
            self.assertTrue(failed.exists())
            self.assertEqual(1, len([path for path in output.glob('run-*') if (path / MARKER).exists()]))


if __name__ == '__main__':
    unittest.main()
