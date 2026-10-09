"""Real failing child and exact attempt-scoped diagnostic context."""
import json
import errno
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from failures import disk_full, tool_failure
from processes import Supervisor
from state import Store, WizardError, atomic_json
import wizard


class FailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "owned"
        self.store = Store(self.root)
        self.state = self.store.create(wizard.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))
        self.session = self.state["session"]
        self.log = self.store.session_dir(self.session) / "logs/build.log"
        self.log.parent.mkdir(parents=True)

    def tearDown(self): self.temp.cleanup()

    def test_real_failure_reports_nested_recovery_cause_without_success_receipt(self):
        key = "a" * 64
        child = "import pathlib,json,sys;root=pathlib.Path(sys.argv[1]);key=sys.argv[2];p=root/'build/logs'/('recovery-'+key[:12]+'.log');p.parent.mkdir(parents=True);p.write_text('[Quest full recovery] FAILED: Skipped core contains an unknown original object; re-export core coherently.\\n');(root/'build/last-failure.json').write_text(json.dumps(dict(schema=1,stage='recovery',key=key,error='BuildError',message='python failed; inspect recovery log')));print('Quest builder: python failed');sys.exit(1)"
        actions = {}
        for name in wizard.STAGES:
            def action(state, supervisor, name=name):
                if name == "build":
                    supervisor.run([sys.executable, "-c", child, str(self.root), key], self.log)
                path = self.store.session_dir(self.session) / (name + ".txt")
                path.write_text(name)
                return [path], {}
            actions[name] = action
        result = wizard.Engine(self.store, actions=actions).run(self.session)
        self.assertEqual(result["status"], "failed")
        action = result["needsActions"][0]
        self.assertEqual(action["parameters"]["failureStage"], "recovery")
        self.assertIn("Skipped core", action["parameters"]["cause"])
        self.assertIn("Spielassets", action["message"]["de"])
        self.assertNotIn("build", result["completed"])
        records = (self.log.parent / "progress.log").read_text()
        self.assertIn("Skipped core", records)
        self.assertIn('"exitCode":1', records)
        self.assertFalse((self.store.session_dir(self.session) / "child.json").exists())

    def test_old_failure_or_unsafe_key_cannot_import_another_attempts_log(self):
        self.log.write_text("Quest builder: current failure")
        failure = self.root / "build/last-failure.json"
        atomic_json(failure, {"schema": 1, "stage": "recovery", "key": "../private", "message": "old private failure"})
        os.utime(failure, (1, 1))
        error = tool_failure(self.root, "build", self.log, time.time() - 1, "python.exe", 1)
        self.assertEqual(error.parameters["failureStage"], "build")
        self.assertNotIn("old private", error.parameters["cause"])
        self.assertEqual(error.parameters["logs"], [str(self.log)])

    def test_memory_rejection_is_retryable_and_names_actual_capacity_not_conversion(self):
        cause = ('Available RAM/commit is insufficient or unknown for the large native game compiler; '
                 'close other programs or provide sufficient Windows pagefile commit headroom and retry.')
        resources = {'phase': 'il2cpp', 'availableMemoryBytes': 35246387200,
                     'commitHeadroomBytes': 26303938560, 'totalMemoryBytes': 68438433792,
                     'requiredCommitHeadroomBytes': 47646032691, 'nativeLaunchAllowed': False}
        self.log.write_text('resources: ' + json.dumps(resources) + '\nQuest builder: ' + cause)
        atomic_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': 'build', 'message': cause})
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertEqual(error.code, 'native_memory_unavailable')
        self.assertEqual(error.parameters['failureStage'], 'native-memory-check')
        self.assertTrue(error.parameters['completedWorkRetained'])
        self.assertIn('32.8 GiB', error.message['de'])
        self.assertIn('24.5 GiB', error.message['de'])
        self.assertIn('44.4 GiB', error.message['de'])
        self.assertIn('Auslagerungsdatei', error.message['de'])
        self.assertNotIn('Konvertierung der Spielassets ist fehlgeschlagen', error.message['de'])
        self.assertEqual(error.parameters['resources']['commitHeadroomBytes'], 26303938560)
        self.assertEqual(error.parameters['cause'], 'Quest builder: ' + cause)

    def test_real_child_memory_stop_blocks_retry_without_failed_closed_weave(self):
        capacity = {'phase': 'il2cpp', 'availableMemoryBytes': 35246387200,
                    'commitHeadroomBytes': 26303938560, 'totalMemoryBytes': 68438433792,
                    'requiredCommitHeadroomBytes': 47646032691, 'nativeLaunchAllowed': False}
        child = ('import pathlib,json,sys;root=pathlib.Path(sys.argv[1]);resources=json.loads(sys.argv[2]);'
                 "p=root/'build/last-failure.json';p.parent.mkdir(parents=True,exist_ok=True);"
                 "p.write_text(json.dumps(dict(schema=1,stage='build',code='native_memory_unavailable',"
                 "message='Native compiler capacity rejected',resources=resources)));"
                 "print('resources: '+json.dumps(resources));print('Quest builder: Native compiler capacity rejected');sys.exit(1)")
        actions = {}
        for name in wizard.STAGES:
            def action(saved, supervisor, name=name):
                if name == 'build':
                    self.store.operation(self.session, 'build', 'weave', complete=True)
                    supervisor.run([sys.executable, '-c', child, str(self.root), json.dumps(capacity)], self.log)
                output = self.store.session_dir(self.session) / (name + '.output')
                output.write_text(name)
                return [output], {}
            actions[name] = action
        result = wizard.Engine(self.store, actions=actions).run(self.session)
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual(result['needsActions'][0]['code'], 'native_memory_unavailable')
        self.assertIn('32.8 GiB', result['needsActions'][0]['message']['de'])
        loaded = self.store.load(self.session)
        overview = loaded['stages'][5]['progress']['buildOverview']
        self.assertIsNone(overview['active'])
        operations = [item for group in overview['groups'] for item in group['operations']]
        self.assertTrue(all(item['status'] in ('complete', 'retained', 'reused') for item in operations if item['closed']))
        self.assertEqual(next(item for item in operations if item['id'] == 'weave')['status'], 'complete')
        self.assertFalse(any(item['status'] == 'failed' for item in operations))
        self.assertNotIn('build', result['completed'])
        self.assertFalse(self.store.receipt(self.session, 'build').exists())
        self.assertFalse((self.store.session_dir(self.session) / 'child.json').exists())

    def test_structured_current_memory_rejection_has_bounded_fields_and_no_false_unknown_zero(self):
        atomic_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'build', 'code': 'native_memory_unavailable', 'message': 'Native compile capacity rejected',
            'resources': {'host': {'availableMemoryBytes': 0, 'commitHeadroomBytes': True,
                                  'totalMemoryBytes': 'sensitive arbitrary input'},
                          'policy': {'requiredCommitHeadroomBytes': 12 * 1024 ** 3},
                          'profileName': 'must-not-leak', 'path': 'must-not-leak'}})
        self.log.write_text('Quest builder: Native compile capacity rejected')
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertEqual(error.code, 'native_memory_unavailable')
        self.assertEqual(error.parameters['resources'], {'availableMemoryBytes': 0,
                                                       'requiredCommitHeadroomBytes': 12 * 1024 ** 3})
        self.assertIn('0.0 GiB', error.message['en'])
        self.assertIn('not reported', error.message['en'])
        self.assertNotIn('must-not-leak', str(error.parameters))

    def test_old_capacity_record_cannot_override_actual_current_compiler_failure(self):
        self.log.write_text('Quest builder: error CS1002: ; expected')
        failure = self.root / 'build/last-failure.json'
        atomic_json(failure, {'schema': 1, 'stage': 'build', 'code': 'native_memory_unavailable',
                              'resources': {'availableMemoryBytes': 123}})
        os.utime(failure, (1, 1))
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertEqual(error.code, 'build_tool_failed')
        self.assertNotIn('resources', error.parameters)

    def test_saved_export_reader_failure_names_evidence_and_keeps_exact_size_context(self):
        cause = 'Recovery resume evidence is oversized: core-recovery.json (19000000 bytes; limit 16777216 bytes)'
        self.log.write_text('Quest builder: ' + cause)
        atomic_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'recovery', 'key': 'b' * 64, 'message': cause})
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertIn('Nachweis', error.message['de'])
        self.assertIn('Arbeitsordner behalten', error.message['de'])
        self.assertEqual(error.parameters['builderError'], cause)
        self.assertIn('19000000 bytes', error.parameters['cause'])

    def test_post_export_helper_import_failure_names_builder_and_retains_actual_cause(self):
        self.log.write_text("ModuleNotFoundError: No module named 'recover'")
        atomic_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'recovery', 'key': 'b' * 64, 'message': "No module named 'recover'"})
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertIn('Hilfsmodul', error.message['de'])
        self.assertIn('abgeschlossene Spieleexports bleiben erhalten', error.message['de'])
        self.assertEqual(error.parameters['cause'], "ModuleNotFoundError: No module named 'recover'")

    def test_unexpected_stage_exception_retains_type_and_traceback(self):
        def fail(*_): raise ValueError("Invalid owned asset index")
        result = wizard.Engine(self.store, actions={"tools": fail}).run(self.session)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["needsActions"][0]["stage"], "tools")
        event = result["events"][-1]
        self.assertEqual(event["parameters"]["error"], "ValueError")
        self.assertIn("Invalid owned asset index", event["parameters"]["traceback"])

    def test_current_certificate_failure_names_download_and_retained_work(self):
        causes = ('<urlopen error [SSL: CERTIFICATE_VERIFY_FAILED] certificate verify failed: unable to get local issuer certificate (_ssl.c:1082)>',
                  'Opus source download failed with verified HTTPS: TLS certificate validation failed: unable to get local issuer certificate')
        for cause in causes:
            with self.subTest(cause=cause):
                self.log.write_text('Quest builder: ' + cause)
                atomic_json(self.root / 'build/last-failure.json', {
                    'schema': 1, 'stage': 'prepare', 'error': 'URLError', 'message': cause})
                error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
                self.assertIn('HTTPS-Download', error.message['de'])
                self.assertIn('Zertifikatsprüfung', error.message['de'])
                self.assertTrue(error.parameters['completedWorkRetained'])
                self.assertEqual(error.parameters['builderError'], cause)
                self.assertEqual(error.parameters['failureStage'], 'prepare')

    def test_old_certificate_failure_cannot_mislabel_current_compiler_failure(self):
        self.log.write_text('Quest builder: error CS1002: ; expected')
        failure = self.root / 'build/last-failure.json'
        atomic_json(failure, {'schema': 1, 'stage': 'prepare', 'message': 'CERTIFICATE_VERIFY_FAILED'})
        os.utime(failure, (1, 1))
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertNotIn('HTTPS-Download', error.message['de'])
        self.assertIn('CS1002', error.parameters['cause'])

    def test_child_disk_full_has_explicit_space_action_even_without_failure_json(self):
        self.log.write_text('Traceback (most recent call last):\nOSError: [Errno 28] No space left on device\n')
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertEqual(error.code, 'workspace_space_exhausted')
        self.assertIn('Platz freigeben und fortsetzen', error.message['de'])
        self.assertIn(str(self.root), error.message['de'])
        self.assertEqual(error.parameters['workspaceRoot'], str(self.root))
        self.assertIn('Errno 28', error.parameters['cause'])
        self.assertEqual(error.parameters['logs'], [str(self.log)])

    def test_disk_full_in_nested_current_recovery_overrides_generic_parent_error(self):
        key = 'b' * 64
        self.log.write_text('Quest builder: recovery tool failed')
        child = self.root / 'build/logs' / ('recovery-' + key[:12] + '.log')
        child.parent.mkdir(parents=True); child.write_text('OSError: [WinError 112] There is not enough space on the disk')
        atomic_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': 'recovery', 'key': key, 'message': 'recovery tool failed'})
        error = tool_failure(self.root, 'build', self.log, time.time() - 1, 'python.exe', 1)
        self.assertEqual(error.code, 'workspace_space_exhausted')
        self.assertEqual(error.parameters['failureStage'], 'recovery')
        self.assertTrue(error.parameters['completedWorkRetained'])

    def test_native_os_disk_full_is_blocked_with_retained_details_not_opaque_failure(self):
        def fail(*_): raise OSError(errno.ENOSPC, 'No space left on device')
        result = wizard.Engine(self.store, actions={'tools': fail}).run(self.session)
        self.assertEqual(result['status'], 'blocked')
        action = result['needsActions'][0]
        self.assertEqual(action['code'], 'workspace_space_exhausted')
        self.assertEqual(action['parameters']['errorNumber'], errno.ENOSPC)
        self.assertEqual(action['parameters']['workspaceRoot'], str(self.root))
        self.assertFalse(self.store.receipt(self.session, 'tools').exists())
        self.assertIn('workspace_space_exhausted', (self.log.parent / 'progress.log').read_text())

    def test_windows_disk_full_codes_are_distinct_from_other_os_failures(self):
        error = OSError('Windows disk full'); error.winerror = 112
        self.assertTrue(disk_full(error)); error.winerror = 39; self.assertTrue(disk_full(error))
        error.winerror = 5; self.assertFalse(disk_full(error))
        self.assertFalse(disk_full(ValueError('No space left on device')))


if __name__ == "__main__": unittest.main()
