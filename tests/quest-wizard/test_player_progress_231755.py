"""Replay scoped B665 Player evidence without importing or building game assets."""
from pathlib import Path
import copy
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
from state import Store
import wizard


class PlayerProgress231755Tests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        patch = mock.patch('state.PROGRESS_INTERVAL', 0); patch.start(); self.addCleanup(patch.stop)
        self.store = Store(Path(temporary.name) / 'owned')
        self.session = self.store.create(wizard.choices({'gameRoot': str(Path(temporary.name) / 'Game')}))['session']
        self.store.begin_stage(self.session, 'build', 'same-owned-input')
        self.store.operation(self.session, 'build', 'player')
        self.report('unity-work-invocation', 0, 1, 'invocations', status='start')
        self.report('unity-work-plan', 3301, 86561, 'assets', '[asset-coverage] Known project/package assets')
        self.report('unity-work-stage:method', 0, 1, status='start')

    def report(self, phase, done, total, unit='tasks', detail='Actual source-backed progress', status='progress'):
        return self.store.progress(self.session, 'build', phase, done, total, unit, detail, operation='player', status=status)

    def player(self, value):
        return next(row for group in value['buildOverview']['groups'] for row in group['operations'] if row['id'] == 'player')

    def parts(self, value): return {row['id']: row for row in self.player(value)['unityWork']['phases'][1]['parts']}

    def test_last_scene_entry_is_open_until_real_platform_postprocessing_boundary(self):
        entry = self.report('unity-player-scenes', 14, 14, 'scenes', 'Unity Player scene: Assets/Scenes/Intro.unity')
        self.assertFalse(self.parts(entry)['scenes']['closed'])
        self.report('unity-shader-compile', 6, 6, 'variants', '[shader-pass:1557] TextCore pass finished', status='complete')
        still_open = self.parts(self.store.load(self.session)['stages'][5]['progress'])['scenes']
        self.assertEqual(still_open['status'], 'running', 'Observed scene processing cannot be labelled unstarted pending work')
        boundary = self.report('unity-player-platform-handoff', 1, 1, status='complete')
        for name in ('scenes', 'shaders'):
            self.assertEqual((self.parts(boundary)[name]['status'], self.parts(boundary)[name]['percent']), ('complete', 100.))
        self.assertFalse(self.player(boundary)['closed'])

    def test_scene_and_shader_preparation_each_include_real_final_handoff_work(self):
        scenes = self.report('unity-player-scenes', 14, 14, 'scenes')
        self.assertAlmostEqual(self.parts(scenes)['scenes']['percent'], 100 * 14 / 15, places=5)
        shader = self.report('unity-player-shaders', 688, 689, 'tasks', '[shader-coverage] 688 known names +1platform handoff')
        self.assertEqual(self.parts(shader)['shaders']['percent'], 80.)
        self.assertFalse(self.parts(shader)['shaders']['closed'])
        final = self.report('unity-player-platform-handoff', 1, 1, status='complete')
        self.assertEqual(self.parts(final)['shaders']['percent'], 100.)

    def test_first_known_shader_pass_advances_source_coverage_without_crediting_repeated_or_builtin_passes(self):
        before = self.report('unity-player-shaders', 0, 250, 'tasks', '[shader-name-coverage] 249 source names and platform handoff')
        first = self.report('unity-shader-compile', 3, 6, 'variants', '[shader-source:unseen] [shader-pass:1] Known Shader READY')
        self.assertAlmostEqual(self.parts(first)['shaders']['percent'], 80 * (3 / 7) / 249, places=6)
        self.assertGreater(first['stagePercent'], before['stagePercent'])
        later = self.report('unity-shader-compile', 6, 6, 'variants', '[shader-source:unseen] [shader-pass:1] Known Shader READY')
        self.assertGreater(later['stagePercent'], first['stagePercent'])
        # The parser queues this actual finished-name count before FINISHED.
        counted = self.report('unity-player-shaders', 1, 250, 'tasks', '[shader-name-coverage] One name observed')
        returned = self.report('unity-shader-compile', 6, 6, 'variants', '[shader-source:unseen] [shader-pass:1] Known Shader FINISHED', status='complete')
        self.assertEqual(returned['stagePercent'], counted['stagePercent'])
        for source in ('observed', 'outside'):
            repeated = self.report('unity-shader-compile', 100, 100, 'variants', '[shader-source:' + source + '] [shader-pass:2] READY')
            self.assertEqual(repeated['stagePercent'], counted['stagePercent'])
            self.assertAlmostEqual(self.parts(repeated)['shaders']['percent'], 80 / 249, places=6)
        self.assertFalse(self.parts(returned)['shaders']['closed'])

    def test_outer_compute_census_remains_available_when_one_kernel_set_completes(self):
        self.report('unity-player-native-result', 1, 1, status='complete')
        self.report('unity-compute-identities', 2, 13, 'shaders')
        kernel = self.report('unity-compute-kernels', 4, 4, 'kernels', status='complete')
        result = self.player(kernel)['unityWork']['phases'][2]
        self.assertEqual((result['counter']['done'], result['counter']['total']), (2, 13))
        self.assertEqual(result['status'], 'running')
        self.assertAlmostEqual(result['percent'], 50 * 2 / 13, places=5)
        self.assertFalse(result['closed'])
        self.assertFalse(self.player(kernel)['closed'])

    def test_player_plan_matches_actual_producer_and_codegen_is_nested_in_dependency_graph(self):
        self.report('unity-player-platform-handoff', 1, 1, status='complete')
        conversion = self.report('unity-il2cpp', 2, 2, detail='[bee-scope:il2cpp] IL2CPP_CodeGen successful output', status='complete')
        parts = self.player(conversion)['unityWork']['phases'][1]['parts']
        self.assertEqual([part['id'] for part in parts], ['scenes', 'shaders', 'native', 'packaging'])
        native = self.parts(conversion)['native']
        self.assertEqual(native['activities'][0]['id'], 'il2cpp')
        self.assertEqual((native['activities'][0]['closed'], native['activities'][0]['percent']), (True, 100.))
        self.assertFalse(native['closed'])
        self.assertEqual(native['percent'], 20., 'Completed conversion advances only its own share, not a future dependency graph')

    def test_copy_only_graph_and_shader_pass_cannot_saturate_native_percentage(self):
        copy = self.report('bee-actions:unity-build:9233:0', 9233, 9233, 'actions', '[bee-scope:staging] CopyFiles', status='complete')
        variants = self.report('unity-shader-compile', 6, 6, 'variants', '[shader-pass:1557] completed variants167610', status='complete')
        native = self.parts(variants)['native']
        self.assertEqual(native['percent'], 0.)
        self.assertEqual(copy['stagePercent'], variants['stagePercent'])
        compiler = self.player(variants)['compiler']
        self.assertEqual((compiler['status'], compiler['percent']), ('complete', 100.))
        self.assertFalse(self.parts(variants)['shaders']['closed'], 'Only the precise last Shader pass completed')

    def test_qualified_native_plan_moves_continuously_and_raw_local_counter_does_not_replace_it(self):
        before = self.report('unity-native-build-plan', 27, 10070, 'tasks', '[bee-native-plan] [bee-graph:Player] reachable nodes +backend result')
        partial = self.report('unity-native-build-plan', 408, 10070, 'tasks', '[bee-native-plan] [bee-graph:Player] reachable nodes +backend result')
        local = self.report('bee-actions:unity-build:10069:0', 407, 10069, 'actions', '[bee-scope:mixed] WriteResponseFile')
        self.assertGreater(partial['stagePercent'], before['stagePercent'])
        native = self.parts(local)['native']
        self.assertEqual((native['activities'][0]['counter']['done'], native['activities'][0]['counter']['total']), (408, 10070))
        self.assertEqual((native['localCounter']['done'], native['localCounter']['total']), (407, 10069))
        self.assertAlmostEqual(native['percent'], 80 * 408 / 10070, places=5)
        self.assertFalse(native['closed'])

    def test_all_graph_nodes_still_reserve_backend_result_then_actual_success_closes_child_only(self):
        nodes = self.report('unity-native-build-plan', 10069, 10070, 'tasks')
        self.assertFalse(self.parts(nodes)['native']['closed'])
        self.assertLess(self.parts(nodes)['native']['percent'], 100.)
        result = self.report('unity-native-build-plan', 10070, 10070, 'tasks', status='complete')
        self.assertEqual((self.parts(result)['native']['status'], self.parts(result)['native']['percent']), ('complete', 100.))
        self.assertFalse(self.player(result)['closed'])
        self.assertFalse(self.player(result)['unityWork']['phases'][1]['closed'])
        self.assertFalse(self.parts(result)['packaging']['closed'])
        delayed = self.report('unity-native-build-plan', 0, 10070, 'tasks')
        graph = next(row for row in self.parts(delayed)['native']['activities'] if row['id'] == 'native-build')
        self.assertEqual((graph['status'], graph['percent'], graph['counter']['done']), ('complete', 100., 10070))

    def test_captured_old99_graph_highwater_resumes_without_reset_or_waiting_to_catch_up(self):
        state = self.store._state(self.session)
        row = next(row for row in state['stages'] if row['id'] == 'build')
        plan = row['progressPlan']; work = plan['unityWork']['player']
        # These exact public B665231755 values result from the old source
        # schedule: scene99%(.05) +localgraph99%(.50), assets ready(.15).
        work.update(parts={'configuration': .05, 'scenes': .05, 'il2cpp': .20, 'native': .50, 'packaging': .20},
            active='method', completed=['assets'], part='native', partFractions={'scenes': .99, 'native': .99},
            fractions={'method': .5445}, counters={
                'scenes': {'phase': 'unity-player-scenes', 'done': 14, 'total': 14, 'unit': 'scenes'},
                'native': {'phase': 'bee-actions:unity-build-3367946efeab.log:10069:0', 'done': 27, 'total': 10069, 'unit': 'actions'}})
        plan.update(workRevision=6, percent=93.6848)
        plan['fractions']['player'] = .5856
        row['progress'].update(stagePercent=93.6848)
        self.store.save(state)
        first = self.report('unity-native-build-plan', 27, 10070, 'tasks', '[bee-native-plan] actual qualified node plan')
        second = self.report('unity-native-build-plan', 28, 10070, 'tasks', '[bee-native-plan] actual qualified node plan')
        self.assertGreater(first['stagePercent'], 93.6848, 'New actual work must use the remaining budget immediately')
        self.assertGreater(second['stagePercent'], first['stagePercent'], 'One real node cannot be hidden behind the old bogus99% curve')
        self.assertLess(self.parts(second)['native']['percent'], 1.)
        self.assertEqual(self.parts(second)['scenes']['status'], 'running')
        self.assertGreater(self.parts(second)['scenes']['percent'], 90.)
        # The finite logical pipeline changed, not completed preparation or
        # the original recovery point. The same map survives a real resume.
        before = second['stagePercent']
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, 'build', 'same-owned-input')
        self.store.operation(self.session, 'build', 'player')
        resumed = self.report('unity-work-invocation', 0, 1, 'invocations', status='start')
        self.assertGreaterEqual(resumed['stagePercent'], before)

    def test_regenerated_plan_uses_actual_new_denominator_while_retained_total_never_resets(self):
        old = self.report('unity-native-build-plan', 10069, 10070, 'tasks', '[bee-native-plan] [bee-graph:before-regeneration]')
        new = self.report('unity-native-build-plan', 10069, 15001, 'tasks', '[bee-native-plan] [bee-graph:after-regeneration]')
        self.assertLess(self.parts(new)['native']['percent'], self.parts(old)['native']['percent'])
        self.assertGreaterEqual(new['stagePercent'], old['stagePercent'])
        self.assertAlmostEqual(self.parts(new)['native']['percent'], 80 * 10069 / 15001, places=5)

    def test_reused_codegen_child_is_visible_as_complete_without_invented_skips(self):
        reused = self.report('unity-il2cpp', 2, 2, status='reuse')
        child = self.parts(reused)['native']['activities'][0]
        self.assertEqual((child['status'], child['closed'], child['percent']), ('reused', True, 100.))
        self.assertEqual(self.parts(reused)['packaging']['status'], 'pending')

    def test_real_native_failure_preserves_completed_conversion_and_earlier_source_boundaries(self):
        self.report('unity-player-platform-handoff', 1, 1, status='complete')
        self.report('unity-il2cpp', 2, 2, status='complete')
        self.report('unity-native-build-plan', 50, 100, 'tasks')
        state = self.store.load(self.session)
        next(row for row in state['stages'] if row['id'] == 'build')['status'] = 'failed'
        self.store.save(state)
        failed = next(row for row in self.store.load(self.session)['stages'] if row['id'] == 'build')['progress']
        self.assertEqual(self.parts(failed)['native']['status'], 'failed')
        self.assertEqual(self.parts(failed)['native']['activities'][0]['status'], 'complete')
        self.assertEqual(self.parts(failed)['scenes']['status'], 'complete')
        self.assertEqual(self.parts(failed)['packaging']['status'], 'pending')


if __name__ == '__main__': unittest.main()
