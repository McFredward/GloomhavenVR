"""Actual quiet-child evidence without guessing conversion failure or timing out."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
CHILD = """
import importlib.util, sys, time
spec = importlib.util.spec_from_file_location('quiet_progress', sys.argv[1])
progress = importlib.util.module_from_spec(spec); spec.loader.exec_module(progress)
mode = sys.argv[2]
def original_conversion_call():
    time.sleep(.6)
with progress.silence_diagnostics(quiet_seconds=.1, limit=2):
    if mode == 'active':
        for number in range(12):
            progress.event('native-recipe', number, 12, 'files')
            time.sleep(.025)
    elif mode == 'exit':
        pass
    else:
        original_conversion_call()
time.sleep(.2)
print('conversion returned successfully')
"""


class QuietChildTests(unittest.TestCase):
    def run_child(self, mode, enabled=True):
        environment = dict(os.environ)
        environment.pop('GHVRQ_WIZARD_PROGRESS', None)
        if enabled: environment['GHVRQ_WIZARD_PROGRESS'] = '1'
        with tempfile.TemporaryDirectory() as directory:
            return subprocess.run([sys.executable, '-I', '-B', '-c', CHILD,
                                   str(ROOT / 'tools/quest-builder/progress.py'), mode],
                                  cwd=directory, env=environment, capture_output=True,
                                  text=True, timeout=10)

    def test_quiet_call_records_bounded_actual_stack_and_returns_success(self):
        result = self.run_child('quiet')
        self.assertEqual(result.returncode, 0, result.stderr)
        reports = [json.loads(line[len('GHVRQ_DIAGNOSTIC '):]) for line in result.stderr.splitlines()
                   if line.startswith('GHVRQ_DIAGNOSTIC ')]
        self.assertEqual(len(reports), 2)
        self.assertTrue(all(row['timeout'] is False for row in reports))
        self.assertIn('original_conversion_call', result.stderr)
        self.assertIn('conversion returned successfully', result.stdout)

    def test_observed_progress_and_scope_exit_do_not_leave_diagnostic_watchers(self):
        for mode in ('active', 'exit'):
            with self.subTest(mode=mode):
                result = self.run_child(mode)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertNotIn('GHVRQ_DIAGNOSTIC', result.stderr)

    def test_normal_cli_has_no_diagnostic_thread_or_output(self):
        result = self.run_child('quiet', enabled=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stderr, '')
        self.assertEqual(result.stdout, 'conversion returned successfully\n')


if __name__ == '__main__': unittest.main()
