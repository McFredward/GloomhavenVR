"""Whole-stage progress witnesses, retries and Unity task observations."""
import json
import math
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import stage_plan
import state
import wizard
from processes import ProgressParser, Supervisor


class StageProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.store = state.Store(Path(self.temp.name) / 'owned')
        self.saved = self.store.create(wizard.choices({'gameRoot': str(Path(self.temp.name) / 'Game')}))
        self.session = self.saved['session']

    def progress(self, stage='build'):
        return next(row for row in self.store.load(self.session)['stages'] if row['id'] == stage)['progress']

    def test_every_stage_has_one_finite_total_and_100_requires_verified_completion(self):
        for name in state.STAGES:
            self.store.begin_stage(self.session, name, 'same-' + name)
            before = 0
            for operation in stage_plan.PLANS[name]:
                value = self.store.operation(self.session, name, operation, complete=True)
                self.assertTrue(math.isfinite(value['stagePercent']))
                self.assertGreaterEqual(value['stagePercent'], before)
                self.assertLess(value['stagePercent'], 100)
                before = value['stagePercent']
            saved = self.store.load(self.session)
            row = next(row for row in saved['stages'] if row['id'] == name)
            row['status'] = 'complete'; self.store.save(saved)
            self.assertEqual(self.progress(name)['stagePercent'], 100)

    def test_substep_100_never_becomes_whole_stage_100_or_restarts_its_total(self):
        self.store.begin_stage(self.session, 'source', 'immutable')
        self.store.progress(self.session, 'source', 'source-copy', 40, 40, 'files')
        first = self.progress('source')
        self.assertEqual(first['percent'], 100)
        self.assertLess(first['stagePercent'], 100)
        self.store.operation(self.session, 'source', 'xr-sources', detail='Loading XR package sources')
        after = self.progress('source')
        self.assertGreaterEqual(after['stagePercent'], first['stagePercent'])
        self.assertIsNone(after['percent'])
        self.assertEqual(after['stageOperation'], 'xr-sources')

    def test_same_input_retry_and_new_process_restore_high_water_but_new_input_resets(self):
        self.store.begin_stage(self.session, 'build', 'input-a')
        self.store.operation(self.session, 'build', 'content-bank')
        self.store.progress(self.session, 'build', 'unity-progress', 45, 100, 'Unity task units')
        previous = self.progress()['stagePercent']
        reopened = state.Store(self.store.root)
        reopened.begin_stage(self.session, 'build', 'input-a')
        reopened.operation(self.session, 'build', 'game-inputs')
        self.assertEqual(self.progress()['stagePercent'], previous)
        reopened.begin_stage(self.session, 'build', 'input-b')
        self.assertEqual(self.progress()['stagePercent'], 0)

    def test_bee_new_dags_reset_secondary_progress_only(self):
        self.store.operation(self.session, 'build', 'player')
        parser = ProgressParser()
        for line in ('[200/400 0s] Clang a.cpp', '[399/400 0s] Clang b.cpp', '[1/400 0s] Link lib.so'):
            before = self.progress()['stagePercent']
            self.store.progress(self.session, 'build', **parser.parse(line, 'unity-build-current.log'))
            self.assertGreaterEqual(self.progress()['stagePercent'], before)
            self.assertLess(self.progress()['stagePercent'], 100)
        self.assertEqual(self.progress()['percent'], .2)

    def test_nested_one_file_or_version_command_cannot_finish_a_parent_operation(self):
        self.store.operation(self.session, 'build', 'game-inputs')
        for phase in ('file-hash', 'tool:unity-version', 'tool:compiler'):
            self.store.progress(self.session, 'build', phase, 1, 1, 'commands')
            self.assertEqual(self.progress()['stagePercent'], 0)
        self.store.progress(self.session, 'build', 'game-hash', 100, 100, 'bytes', status='complete')
        self.assertGreater(self.progress()['stagePercent'], 0)
        self.assertLess(self.progress()['stagePercent'], 100)

    def test_unknown_unity_phase_keeps_total_and_required_action(self):
        self.store.operation(self.session, 'unity', 'editor')
        first = self.progress('unity')['stagePercent']
        wait = self.store.waiting(self.session, 'unity', 'unity_login_required', {'en': 'Sign in', 'de': 'Anmelden'})
        with mock.patch('state.time.time', return_value=9999999999):
            self.store.progress(self.session, 'unity', 'unity-editor-install', detail='Unity Hub downloads Android SDK and NDK')
        after = self.progress('unity')
        self.assertEqual(after['stagePercent'], first)
        self.assertIsNone(after['percent'])
        self.assertEqual(self.store.load(self.session)['needsActions'][0]['nonce'], wait['nonce'])

    def test_old_state_with_only_raw_substep_100_does_not_inherit_global_100(self):
        row = self.saved['stages'][5]
        row['status'] = 'interrupted'
        row.pop('progressPlan', None)
        row['progress'] = state.stage_progress('unknown-phase', 20, 20, 'files')
        state.atomic_json(self.store.session_dir(self.session) / 'state.json', self.saved)
        result = self.progress()
        self.assertEqual(result['percent'], 100)
        self.assertEqual(result['stagePercent'], 0)

    def test_invalid_operations_do_not_mutate_state(self):
        with self.assertRaises(state.WizardError): self.store.progress(self.session, 'build', 'unknown', operation='delete-everything')
        self.assertEqual(self.progress()['stagePercent'], 0)

    def test_full_engine_wait_cancel_retry_and_receipt_reuse_publish_once(self):
        first = [True]
        def action(name):
            def run(saved, supervisor):
                self.store.operation(self.session, name, stage_plan.PLANS[name][-2], complete=True)
                if name == 'build' and first[0]:
                    first[0] = False
                    raise state.Cancelled()
                output = self.store.session_dir(self.session) / (name + '.txt')
                output.write_text(name)
                return [output], {}
            return run
        engine = wizard.Engine(self.store, actions={name: action(name) for name in state.STAGES})
        cancelled = engine.run(self.session)
        self.assertEqual(cancelled['status'], 'cancelled')
        before = self.progress()['stagePercent']
        self.assertLess(before, 100)
        self.assertFalse(self.store.receipt(self.session, 'build').exists())
        done = engine.run(self.session)
        self.assertTrue(all(row['progress']['stagePercent'] == 100 for row in done['stages']))
        self.assertTrue(all(row['progress']['stagePercent'] == 100 for row in engine.run(self.session)['stages']))


class UnityObservationTests(unittest.TestCase):
    def test_api_counter_and_boundary_preserve_raw_fields(self):
        parser = ProgressParser()
        value = parser.parse('GHVRQ_PROGRESS {"schema":1,"phase":"unity-progress","done":3,"total":10,"unit":"steps","detail":"Unity: Importing","status":"progress"}')
        self.assertEqual((value['done'], value['total']), (3, 10))
        boundary = parser.parse('GHVRQ_PROGRESS {"schema":1,"phase":"operation:content-bank","done":1,"total":1,"operation":"content-bank","status":"complete"}')
        self.assertEqual(boundary['operation'], 'content-bank')
        self.assertEqual(boundary['status'], 'complete')

    def test_unity_unknown_details_do_not_invent_totals(self):
        parser = ProgressParser()
        for line in ('Start importing Assets/Figure.png using Guid abc', 'Begin MonoManager ReloadAssembly',
                     '[Package Manager] Resolving packages', '> Task :launcher:packageDebug', 'Invoking il2cpp with arguments'):
            with self.subTest(line=line):
                value = parser.parse(line)
                self.assertTrue(value['phase'].startswith('unity-'))
                self.assertIsNone(value['total'])
                self.assertEqual(value['detail'], line)

    def test_real_native_log_counter_can_supplement_unknown_api(self):
        parser = ProgressParser()
        self.assertEqual(parser.parse('Importing 75 assets of 100')['done'], 75)
        self.assertIsNone(parser.parse('Importing 101 assets of 100'))
        self.assertIsNone(parser.parse('fake guessed progress 75%'))

    def test_live_child_operation_and_unity_task_counter_reach_durable_state(self):
        with tempfile.TemporaryDirectory() as folder:
            store = state.Store(Path(folder) / 'owned'); saved = store.create(wizard.choices({'gameRoot': folder}))
            supervisor = Supervisor(store, saved['session'], poll=.01); supervisor.set_stage('build')
            lines = ['GHVRQ_PROGRESS ' + json.dumps({'schema': 1, 'phase': 'operation:player', 'operation': 'player', 'status': 'start'}),
                     'GHVRQ_PROGRESS ' + json.dumps({'schema': 1, 'phase': 'unity-progress', 'done': 4, 'total': 10, 'unit': 'steps'})]
            code = 'import sys,time;[print(x,flush=True) for x in sys.argv[1:]];time.sleep(.1)'
            with store.active(saved): supervisor.run([sys.executable, '-I', '-c', code, *lines], store.session_dir(saved['session']) / 'logs/build.log')
            progress = store.load(saved['session'])['stages'][5]['progress']
            self.assertEqual(progress['percent'], 40)
            self.assertGreater(progress['stagePercent'], 0)
            self.assertLess(progress['stagePercent'], 100)

    def test_other_stage_and_future_tool_observations_cannot_abort_real_children(self):
        with tempfile.TemporaryDirectory() as folder:
            store = state.Store(Path(folder) / 'owned'); saved = store.create(wizard.choices({'gameRoot': folder}))
            for stage in ('source', 'build', 'unity', 'tools'):
                with self.subTest(stage=stage):
                    supervisor = Supervisor(store, saved['session'], poll=.01); supervisor.set_stage(stage)
                    lines = ['Preparing original asset template',
                             'Preparing 12 changed files on the PC; 4 files remain installed.',
                             'GHVRQ_PROGRESS ' + json.dumps({'schema': 1, 'phase': 'future-tool', 'operation': 'future-operation', 'status': 'complete',
                                                           'done': 3, 'total': 3, 'unit': 'files', 'detail': 'Future source operation finished'})]
                    code = 'import sys,time;[print(x,flush=True) for x in sys.argv[1:]];time.sleep(.1)'
                    with store.active(saved):
                        self.assertEqual(supervisor.run([sys.executable, '-I', '-c', code, *lines], store.session_dir(saved['session']) / ('logs/' + stage + '.log')), 0)
                    row = next(row for row in store.load(saved['session'])['stages'] if row['id'] == stage)
                    self.assertEqual(row['progress']['detail'], 'Future source operation finished')
                    self.assertEqual(row['progress']['stagePercent'], 0)
                    self.assertFalse(store.receipt(saved['session'], stage).exists())

    def test_only_exact_installer_preparation_line_is_classified_as_content(self):
        parser = ProgressParser()
        self.assertIsNone(parser.parse('Preparing original asset template'))
        value = parser.parse('Preparing 12 changed files on the PC; 4 files remain installed.')
        self.assertEqual(value['operation'], 'content')


if __name__ == '__main__': unittest.main()
