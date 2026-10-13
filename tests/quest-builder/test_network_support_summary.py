"""Global diagnostics show the current blocked run rather than old build errors."""
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support


class CurrentNetworkSummaryTests(unittest.TestCase):
    def test_blocked_run_retains_current_endpoint_failure_and_redacts_external_secrets(self):
        saved = {'schema': 1, 'status': 'blocked', 'session': 'a' * 32,
                 'needsActions': [{'code': 'network_preflight_failed',
                     'message': {'de': 'Downloadserver antwortet nicht', 'en': 'Download host timed out'},
                     'parameters': {'completedWorkRetained': True, 'unrelatedPrivateField': 'NEVER_EXPORT',
                         'failedDependencies': [{'id': 'adb', 'attempts': [{'url': 'https://user:secret@dl.google.com/file?token=private',
                             'reason': 'The read operation timed out', 'failureKind': 'timeout'}]}]}}]}
        summary = support.run_summary(saved, False, [])
        action = summary['needsActions'][0]
        self.assertEqual(action['code'], 'network_preflight_failed')
        self.assertIn('timed out', action['parameters']['failedDependencies'][0]['attempts'][0]['reason'])
        self.assertTrue(action['parameters']['completedWorkRetained'])
        for secret in ('user:secret', 'token=private', 'NEVER_EXPORT'):
            self.assertNotIn(secret, json.dumps(summary))

    def test_completed_or_resumed_run_cannot_keep_a_stale_blocked_action_in_summary(self):
        for status in ('complete', 'running', 'cancelled'):
            summary = support.run_summary({'status': status, 'needsActions': [{'code': 'old_failure', 'message': {'en': 'Old'}}]}, False, [])
            self.assertNotIn('needsActions', summary)


if __name__ == '__main__': unittest.main()
