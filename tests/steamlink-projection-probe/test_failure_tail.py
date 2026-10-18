import importlib.util
from pathlib import Path
import unittest

SPEC=importlib.util.spec_from_file_location('projection_runner',Path(__file__).with_name('run.py'))
RUNNER=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)

class FailureTailTests(unittest.TestCase):
    def test_keeps_actionable_child_failure_and_status(self):
        message=RUNNER.bounded_failure('Fixture',134,Path('/private/evidence'),'Unhandled exception. meaningful assertion\nstack:line42')
        self.assertIn('exit=134',message)
        self.assertIn('/private/evidence',message)
        self.assertIn('meaningful assertion',message)
        self.assertIn('stack:line42',message)

    def test_flood_is_bounded_and_keeps_tail(self):
        message=RUNNER.bounded_failure('Fixture',1,Path('/evidence'),'EARLY_DROPPED'+('x'*100000)+'ACTUAL_FAILURE')
        self.assertNotIn('EARLY_DROPPED',message)
        self.assertTrue(message.endswith('ACTUAL_FAILURE'))
        self.assertLess(len(message),6300)

if __name__=='__main__':unittest.main()
