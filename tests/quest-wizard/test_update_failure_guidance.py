"""Current update failures offer finite choices without granting repair consent."""
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
from failures import tool_failure
from state import Store, atomic_json


class UpdateFailureGuidanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name) / 'owned'; Store(self.root)
        self.log = self.root / 'sessions/current/logs/build.log'
        self.log.parent.mkdir(parents=True)
        self.started = time.time() - 1

    def tearDown(self): self.temporary.cleanup()

    def fail(self, cause, stage='update-mod'):
        self.log.write_text('Quest builder: ' + cause)
        atomic_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': stage, 'message': cause})
        return tool_failure(self.root, 'build', self.log, self.started, 'python.exe', 1)

    def test_exact_generated_project_refusals_name_retained_retry_and_diagnostics(self):
        for cause in ('The retained private UGUI package changed; no native update was produced.',
                      'A rewritten original assembly needs one recovered DLL and its retained .meta: UnityEngine.UI.dll',
                      'The code-update package API binding is incomplete or differs from its actual inputs.'):
            with self.subTest(cause=cause):
                error = self.fail(cause)
                self.assertEqual(error.code, 'build_tool_failed')
                self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
                self.assertIn(cause, error.parameters['cause'])
                self.assertIn('gespeicherten Run erneut versuchen', error.parameters['updateGuidance']['nextSteps']['de'])
                self.assertIn('Logs exportieren', error.parameters['updateGuidance']['nextSteps']['de'])
                self.assertTrue(error.parameters['completedWorkRetained'])

    def test_specific_input_signing_and_source_choices_are_not_automatic_authority(self):
        cases = {'project-ownership': 'Code-only project belongs to a different update; retain it and use this update\'s own output folder.',
                 'owned-input': 'The PC game does not match this APK\'s original assembly: Managed/Assembly-CSharp.dll; use the matching PC installation or create a full build.',
                 'source-apk': 'The existing Quest APK could not be read.',
                 'signing': 'Updating an installed APK requires its original signing folder (quest.keystore and local-key.json).'}
        for kind, cause in cases.items():
            with self.subTest(kind=kind):
                error = self.fail(cause)
                guidance = error.parameters['updateGuidance']
                self.assertEqual(guidance['kind'], kind)
                self.assertEqual(set(guidance), {'schema', 'kind', 'nextSteps', 'retryRetained', 'editInputs'})
                self.assertEqual(set(guidance['nextSteps']), {'en', 'de'})
                self.assertTrue(guidance['editInputs']); self.assertTrue(guidance['retryRetained'])
                self.assertNotIn('install', error.parameters)
                self.assertFalse(any(key in error.parameters for key in ('resetAppDataAvailable', 'requiresRestoreChoice', 'delete', 'autoRepair')))

    def test_unknown_current_update_error_explains_options_without_inventing_a_repair(self):
        error = self.fail('Unrecognized compiler failure 123')
        self.assertEqual(error.parameters['updateGuidance']['kind'], 'unknown')
        self.assertIn('separate choice', error.parameters['updateGuidance']['nextSteps']['en'])
        self.assertIn('Unrecognized compiler failure 123', error.parameters['cause'])

    def test_update_inspection_has_choices_but_ordinary_build_and_memory_keep_existing_flow(self):
        self.assertEqual(self.fail('The existing Quest APK could not be read.', 'inspect-update').parameters['updateGuidance']['kind'], 'source-apk')
        self.assertNotIn('updateGuidance', self.fail('The existing Quest APK could not be read.', 'build').parameters)
        error = self.fail('Available RAM/commit is insufficient or unknown for the large native game compiler', 'update-mod')
        self.assertEqual(error.code, 'native_memory_unavailable')
        self.assertNotIn('updateGuidance', error.parameters)
        error = self.fail('CERTIFICATE_VERIFY_FAILED', 'update-mod')
        self.assertIn('Zertifikatsprüfung', error.message['de'])
        self.assertNotIn('updateGuidance', error.parameters)

    def test_old_other_stage_or_linked_failure_cannot_create_current_update_actions(self):
        self.fail('The existing Quest APK could not be read.')
        failure = self.root / 'build/last-failure.json'
        os.utime(failure, (1, 1))
        error = tool_failure(self.root, 'build', self.log, self.started, 'python.exe', 1)
        self.assertNotIn('updateGuidance', error.parameters)
        self.assertNotIn('updateGuidance', self.fail('The existing Quest APK could not be read.', 'recovery').parameters)
        if os.name != 'nt':
            failure.unlink(); target = Path(self.temporary.name) / 'foreign.json'
            target.write_text(json.dumps({'schema': 1, 'stage': 'update-mod', 'message': 'The existing Quest APK could not be read.'}))
            failure.symlink_to(target)
            self.assertNotIn('updateGuidance', tool_failure(self.root, 'build', self.log, self.started, 'python.exe', 1).parameters)


if __name__ == '__main__': unittest.main()
