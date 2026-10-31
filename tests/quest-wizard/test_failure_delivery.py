"""The current APK host command supplies bounded, private-safe failure context."""
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
import wizard

CAUSE = "Unable to open 'GloomhavenVR-Quest.capable.apk.rdltsgc1.apk' as zip archive."


class DeliveryFailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / 'owned'
        Store(self.root)
        self.logs = self.root / 'build/logs'; self.logs.mkdir(parents=True)
        self.log = self.root / 'sessions/current/logs/build.log'; self.log.parent.mkdir(parents=True)
        self.started = time.time() - 1

    def tearDown(self): self.temp.cleanup()

    def record(self, name='update-align.log', *, named=None, cause=CAUSE, appendix=''):
        path = self.logs / name
        message = 'zipalign.exe exited with 1; inspect ' + str(named or path) + appendix
        path.write_text(cause)
        self.log.write_text('Quest builder: ' + message)
        atomic_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'build', 'key': 'a' * 64, 'message': message})
        return path, message

    def failure(self): return tool_failure(self.root, 'build', self.log, self.started, 'python.exe', 1)

    def test_actual_zipalign_refusal_replaces_python_wrapper_and_names_retained_android_build(self):
        path, message = self.record()
        error = self.failure()
        self.assertEqual(error.parameters['cause'], CAUSE)
        self.assertEqual(error.parameters['builderError'], message)
        self.assertEqual(error.parameters['logs'], [str(self.log), str(path)])
        self.assertTrue(error.parameters['completedWorkRetained'])
        self.assertIn('fertige Android-Build', error.message['de'])
        self.assertNotIn('python.exe', error.message['de'])

    def test_sign_and_certificate_commands_use_same_exact_boundary(self):
        for name in ('update-sign.log', 'update-signature.log', 'update-base-signature.log', 'update-key-certificate.log'):
            with self.subTest(name=name):
                path, _ = self.record(name, cause='Exception: signer rejected certificate')
                error = self.failure()
                self.assertEqual(error.parameters['cause'], 'Exception: signer rejected certificate')
                self.assertEqual(error.parameters['logs'], [str(self.log), str(path)])

    def test_child_cause_is_private_safe_and_bounded_even_after_secret_lines(self):
        text = ('PRIVATE PREFIX\n' + 'x' * 16000 + '\n' + CAUSE + '\n'
                'password: PRIVATE PASSWORD\nGHVR_QUEST_SIGNING_PASSWORD=PRIVATE ENV\n'
                'Authorization: Bearer PRIVATE BEARER\n'
                '-----BEGIN PRIVATE KEY-----\nPRIVATE KEY BYTES\n-----END PRIVATE KEY-----\n')
        self.record(cause=text)
        error = self.failure()
        self.assertEqual(error.parameters['cause'], CAUSE)
        self.assertNotIn('PRIVATE', json.dumps(error.parameters))
        self.assertLessEqual(len(error.parameters['cause']), 2048)

    def test_stale_log_or_stale_failure_record_cannot_supply_current_cause(self):
        path, message = self.record()
        os.utime(path, (1, 1))
        self.assertEqual(self.failure().parameters['cause'], 'Quest builder: ' + message)
        self.record()
        os.utime(self.root / 'build/last-failure.json', (1, 1))
        self.assertEqual(self.failure().parameters['logs'], [str(self.log)])

    def test_outside_traversal_and_unknown_logs_cannot_select_native_cause(self):
        for named in (self.root / 'private/update-align.log', self.logs / '../private/update-align.log'):
            with self.subTest(named=named):
                self.record(named=named)
                self.assertEqual(self.failure().parameters['logs'], [str(self.log)])
                self.assertNotIn('as zip archive', self.failure().parameters['cause'])
        self.record('update-private-key.log')
        self.assertEqual(self.failure().parameters['logs'], [str(self.log)])

    @unittest.skipIf(os.name == 'nt', 'link privileges vary')
    def test_linked_log_is_not_followed(self):
        path, _ = self.record()
        private = Path(self.temp.name) / 'private.log'; private.write_text(CAUSE)
        path.unlink(); path.symlink_to(private)
        self.assertEqual(self.failure().parameters['logs'], [str(self.log)])
        path.unlink(); os.link(private, path)
        self.assertEqual(self.failure().parameters['logs'], [str(self.log)])

    def test_first_line_names_log_when_builder_also_reports_bounded_native_tail(self):
        path, _ = self.record(appendix='\nNative tool output: ' + CAUSE)
        error = self.failure()
        self.assertEqual(error.parameters['cause'], CAUSE)
        self.assertEqual(error.parameters['logs'], [str(self.log), str(path)])

    def test_real_failed_supervised_child_retains_actual_cause_in_wizard_action(self):
        store = Store(self.root)
        state = store.create(wizard.choices({'gameRoot': str(Path(self.temp.name) / 'Game')}))
        session = state['session']
        log = store.session_dir(session) / 'logs/build.log'
        script = ("import json,pathlib,sys;root=pathlib.Path(sys.argv[1]);"
                  "p=root/'build/logs/update-align.log';p.write_text(sys.argv[2]);"
                  "message='zipalign.exe exited with 1; inspect '+str(p);"
                  "(root/'build/last-failure.json').write_text(json.dumps(dict(schema=1,stage='build',message=message)));"
                  "print('Quest builder: '+message);sys.exit(1)")
        actions = {}
        for name in wizard.STAGES:
            def action(saved, supervisor, name=name):
                if name == 'build': supervisor.run([sys.executable, '-c', script, str(self.root), CAUSE], log)
                output = store.session_dir(session) / (name + '.txt'); output.write_text(name)
                return [output], {}
            actions[name] = action
        result = wizard.Engine(store, actions=actions).run(session)
        self.assertEqual(result['status'], 'failed')
        self.assertEqual(result['needsActions'][0]['parameters']['cause'], CAUSE)
        self.assertIn('APK', result['needsActions'][0]['message']['de'])
        self.assertIn(CAUSE, (log.parent / 'progress.log').read_text())
        self.assertFalse(store.receipt(session, 'build').exists())


if __name__ == '__main__': unittest.main()
