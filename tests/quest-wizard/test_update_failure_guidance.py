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
from state import Store, WizardError, atomic_json
from processes import Supervisor
import wizard


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
                      'Compact update repair required: the compiler could not resolve retained script Unity.InputSystem.ForUI.dll|UnityEngine.InputSystem.Plugins.InputForUI|InputSystemProvider.',
                      'The code-update package API binding is incomplete or differs from its actual inputs.'):
            with self.subTest(cause=cause):
                error = self.fail(cause)
                self.assertEqual(error.code, 'build_tool_failed')
                self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
                self.assertIn(cause, error.parameters['cause'])
                self.assertIn('gespeicherten Run erneut versuchen', error.parameters['updateGuidance']['nextSteps']['de'])
                self.assertIn('Logs exportieren', error.parameters['updateGuidance']['nextSteps']['de'])
                self.assertTrue(error.parameters['completedWorkRetained'])
                self.assertNotIn('newBuild', error.parameters['updateGuidance'])

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

    def test_rebuild_decision_is_a_pause_and_never_implicit_repair_or_delete_consent(self):
        cause = 'Compact update needs a rebuild decision: Retained original serialized script is missing: ApparanceAbout'
        error = self.fail(cause)
        self.assertEqual(error.code, 'update_rebuild_decision_required')
        self.assertEqual(error.parameters['updateGuidance']['kind'], 'rebuild-decision')
        self.assertTrue(error.parameters['updateGuidance']['newBuild'])
        self.assertTrue(error.parameters['completedWorkRetained'])
        self.assertIn('benötigt deine ausdrückliche Entscheidung', error.message['de'])
        self.assertIn('does not start conversion or delete', error.parameters['updateGuidance']['nextSteps']['en'])
        self.assertFalse(any(key in error.parameters for key in ('delete', 'autoRepair', 'rebuildApproved')))

    def test_plugin_import_failure_requests_targeted_repair_without_rebuild_choice(self):
        error = self.fail('Compact update plugin import failed: Unity could not load Apparance.Unity.dll')
        self.assertEqual(error.code, 'build_tool_failed')
        self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
        self.assertNotIn('newBuild', error.parameters['updateGuidance'])
        self.assertIn('does not establish', error.message['en'])
        self.assertIn('gezielte Plugin-Reparatur', error.parameters['updateGuidance']['nextSteps']['de'])

    def test_openxr_retry_exhaustion_keeps_the_small_compiler_repair_scope(self):
        error = self.fail('Compact update OpenXR preparation failed: the package settings are still unavailable after one retained compiler retry.')
        self.assertEqual(error.code, 'build_tool_failed')
        self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
        self.assertTrue(error.parameters['completedWorkRetained'])
        self.assertNotIn('newBuild', error.parameters['updateGuidance'])
        self.assertIn('OpenXR', error.message['de'])
        self.assertIn('keine neue Konvertierung', error.parameters['updateGuidance']['nextSteps']['de'])

    def test_registry_conflict_keeps_completed_native_output_and_avoids_a_rebuild_offer(self):
        error = self.fail('APK has conflicting MonoScript identities: Fixture.dll|Example|Script; executionOrder differs.')
        self.assertEqual(error.code, 'build_tool_failed')
        self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
        self.assertTrue(error.parameters['completedWorkRetained'])
        self.assertNotIn('newBuild', error.parameters['updateGuidance'])
        self.assertIn('Kompatibilitätsprüfung', error.message['de'])
        self.assertIn('code-player.apk', error.parameters['updateGuidance']['nextSteps']['de'])

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

    def test_actual_legacy_build_failure_in_real_supervised_update_gets_current_mode_guidance(self):
        cause = 'A rewritten original assembly needs one recovered DLL and its retained .meta: UnityEngine.UI.dll'
        script = ("import json,os,pathlib,sys;root=pathlib.Path(sys.argv[1]);"
                  "failure=root/'build/last-failure.json';failure.parent.mkdir(parents=True,exist_ok=True);"
                  "failure.write_text(json.dumps(dict(schema=1,stage='build',error='BuildError',message=sys.argv[2])));"
                  "stale=sys.argv[3]=='stale';"
                  "os.utime(failure,(1,1)) if stale else None;"
                  "print('Quest builder: '+sys.argv[2]);sys.exit(1)")
        for mode, stale in (('update-mod', False), ('build', False), ('update-mod', True)):
            with self.subTest(mode=mode, stale=stale):
                store = Store(self.root)
                saved = store.create(wizard.choices({'gameRoot': str(self.root / 'Game'), 'mode': mode,
                                                     'baseApk': str(self.root / 'base.apk'), 'install': False}))
                session = saved['session']; log = store.session_dir(session) / 'logs/build.log'
                supervisor = Supervisor(store, session, poll=.01); supervisor.set_stage('build')
                with self.assertRaises(WizardError) as caught:
                    supervisor.run([sys.executable, '-B', '-c', script, str(self.root), cause, 'stale' if stale else 'current'], log)
                error = caught.exception
                self.assertEqual(error.code, 'build_tool_failed')
                self.assertEqual(error.parameters['failureStage'], 'build')
                self.assertEqual(error.parameters['workflowMode'], mode)
                self.assertIn(cause, error.parameters['cause'])
                if mode == 'update-mod' and not stale:
                    self.assertEqual(error.parameters['updateGuidance']['kind'], 'compact-project')
                    self.assertNotIn('could not be repaired', error.message['en'])
                    self.assertNotIn('automatic repair tried', error.message['en'])
                else:
                    self.assertNotIn('updateGuidance', error.parameters)
                self.assertFalse(store.receipt(session, 'build').exists())
                self.assertFalse((store.session_dir(session) / 'child.json').exists())


if __name__ == '__main__': unittest.main()
