"""Measured graphics passes keep their one actual checkpoint and live owner."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import state
import stage_plan
import wizard


class GraphicsProgressTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.store = state.Store(Path(temporary.name) / 'owned')
        self.session = self.store.create(wizard.choices({'gameRoot': str(Path(temporary.name) / 'Game')}))['session']
        observer = patch('state.PROGRESS_INTERVAL', 0); observer.start(); self.addCleanup(observer.stop)
        self.store.begin_stage(self.session, 'build', 'graphics-fixture')

    def row(self):
        return self.store.load(self.session)['stages'][5]

    def open(self, operation, checkpoint, done, total):
        self.store.operation(self.session, 'build', operation)
        self.store.progress(self.session, 'build', 'prepare-substage:' + checkpoint, done, total,
                            'steps', operation=operation, status='start')

    def items(self, name, done, total, status='progress', unit='items'):
        self.store.progress(self.session, 'build', 'prepare-items:' + name, done, total, unit,
                            detail='Observed native work', status=status)
        return self.row()

    def test_last_decoded_texture_cannot_close_reference_processing_or_the_checkpoint(self):
        self.open('textures', 'native-texture2d', 2, 3)
        previous = self.row()['progress']['stagePercent']
        for name, done, total in (
            ('native-texture2d-backup', 50, 100), ('native-texture2d-backup', 100, 100),
            ('native-texture2d', 32, 33), ('native-texture2d', 33, 33),
            ('native-texture2d-manifests', 4, 7), ('native-texture2d-manifests', 7, 7),
            ('native-texture2d-reference-scan', 10, 100),
            ('native-texture2d-reference-scan', 90, 100),
            ('native-texture2d-reference-receipts', 3, 6),
            ('native-texture2d-contracts', 30, 40),
        ):
            row = self.items(name, done, total, 'complete' if done == total else 'progress')
            self.assertGreater(row['progress']['stagePercent'], previous, name)
            previous = row['progress']['stagePercent']
            self.assertEqual(row['progress']['buildOverview']['active'], 'textures')
            self.assertNotIn('textures', row['progressPlan']['completed'])
            self.assertLess(row['progressPlan']['fractions']['textures'], 1)
        self.store.progress(self.session, 'build', 'prepare-substage:native-texture2d', 3, 3, 'steps',
                            operation='textures', status='complete')
        self.store.operation(self.session, 'build', 'textures', complete=True)
        self.assertIn('textures', self.row()['progressPlan']['completed'])

    def test_live_compute_child_kernels_and_references_both_move_global_progress(self):
        self.open('graphics', 'campaign-compute', 0, 2)
        previous = self.row()['progress']['stagePercent']
        for name, done, total in (
            ('campaign-compute-pointer-owners', 20, 100),
            ('campaign-compute-pointer-owners', 100, 100),
            ('campaign-compute-backup', 100, 100),
            ('campaign-compute-kernels', 1, 36), ('campaign-compute-kernels', 18, 36),
            ('campaign-compute-kernels', 36, 36), ('campaign-compute-manifests', 3, 7),
            ('campaign-compute-reference-scan', 1000, 191415),
            ('campaign-compute-reference-scan', 100000, 191415),
            ('campaign-compute-contracts', 20, 54),
        ):
            row = self.items(name, done, total, 'complete' if done == total else 'progress')
            self.assertGreater(row['progress']['stagePercent'], previous, name)
            previous = row['progress']['stagePercent']
            self.assertEqual(row['progress']['buildOverview']['active'], 'graphics')
            self.assertNotIn('graphics', row['progressPlan']['completed'])
            self.assertLess(row['progressPlan']['fractions']['graphics'], .5)

    def test_texture_counter_cannot_reopen_closed_textures_during_compute(self):
        self.open('textures', 'native-texture2d', 2, 3)
        self.items('native-texture2d', 33, 33, 'complete')
        self.store.operation(self.session, 'build', 'textures', complete=True)
        self.open('graphics', 'campaign-compute', 0, 2)
        old = self.row()['progress']['stagePercent']
        row = self.items('native-texture2d-reference-scan', 50, 100)
        self.assertEqual(row['progress']['stagePercent'], old)
        self.assertEqual(row['progress']['buildOverview']['active'], 'graphics')
        self.assertIn('textures', row['progressPlan']['completed'])

    def test_rollback_before_opening_a_checkpoint_exposes_work_without_creating_credit(self):
        old = self.row()['progress']['stagePercent']
        row = self.items('campaign-compute-rollback-restore', 128, 1024, unit='bytes')
        self.assertEqual(row['progress']['done'], 128)
        self.assertEqual(row['progress']['total'], 1024)
        self.assertEqual(row['progress']['stagePercent'], old)
        self.assertNotIn('graphics', row['progressPlan']['completed'])

    def test_alias_resets_stay_inside_the_current_shader_and_leave_later_tasks_open(self):
        self.open('graphics', 'campaign-shaders', 1, 2)
        previous = self.row()['progress']['stagePercent']
        for name, done, total in (
            ('campaign-shaders-inventory', 0, 3),
            ('campaign-shaders-extract', 100, 100),
            ('campaign-shaders-variants', 50, 100),
            ('campaign-shaders-inventory', 1, 3),
            ('campaign-shaders-extract', 0, 10),
            ('campaign-shaders-extract', 10, 10),
            ('campaign-shaders-variants', 0, 10),
            ('campaign-shaders-variants', 5, 10),
            ('campaign-shaders-inventory', 2, 3),
            ('campaign-shaders-variants', 1, 1),
            ('campaign-shaders-inventory', 3, 3),
            ('campaign-shaders-programs', 10, 20),
            ('campaign-shaders-sources', 2, 3),
        ):
            row = self.items(name, done, total)
            self.assertGreaterEqual(row['progress']['stagePercent'], previous, name)
            if done: self.assertGreater(row['progress']['stagePercent'], previous, name)
            previous = row['progress']['stagePercent']
            self.assertEqual(row['progress']['buildOverview']['active'], 'graphics')
            self.assertNotIn('graphics', row['progressPlan']['completed'])

    def test_foreign_shader_variant_counter_does_not_create_a_shader_parent(self):
        self.open('graphics', 'campaign-compute', 0, 2)
        previous = self.row()['progress']['stagePercent']
        row = self.items('campaign-shaders-variants', 100, 100, 'complete')
        self.assertEqual(row['progress']['stagePercent'], previous)
        self.assertEqual(row['progressPlan']['preparationScopes']['graphics']['name'], 'campaign-compute')

    def test_measured_schedules_reserve_exactly_one_checkpoint(self):
        for name in ('native-cubemaps', 'native-texture2d', 'campaign-compute', 'campaign-shaders'):
            self.assertAlmostEqual(sum(stage_plan.STARTUP_ITEM_SCHEDULES[name].values()), 1, msg=name)

    def test_second_graphics_checkpoint_has_its_own_current_ordinal_and_named_remaining_passes(self):
        self.open('graphics', 'campaign-shaders', 1, 2)
        row = self.items('campaign-shaders-backup', 100, 100, 'complete')
        work = row['progress']['activeWork']
        self.assertEqual((work['done'], work['total'], work['remaining'], work['index']), (1, 2, 1, 2))
        self.assertEqual(work['checkpoint'], 'campaign-shaders')
        self.assertEqual(work['passes']['done'], 1)
        self.assertEqual(work['passes']['total'], len(stage_plan.STARTUP_ITEM_SCHEDULES['campaign-shaders']))
        self.assertFalse(work['passes']['known'], 'The binary-material branch is not observed yet.')
        graphics = next(item for group in row['progress']['buildOverview']['groups'] for item in group['operations'] if item['id'] == 'graphics')
        checkpoints = graphics['preparation']['checkpoints']
        self.assertEqual([item['id'] for item in checkpoints], ['campaign-compute', 'campaign-shaders'])
        self.assertEqual(checkpoints[0]['status'], 'retained')
        self.assertEqual(checkpoints[0]['detail'], 'aggregate')
        self.assertEqual(checkpoints[0]['passes'], [])
        self.assertEqual(checkpoints[1]['status'], 'running')
        self.assertTrue(checkpoints[1]['passes'][0]['closed'])
        self.assertTrue(all(not item['closed'] for item in checkpoints[1]['passes'][1:]))

    def test_optional_shader_passes_do_not_claim_an_exact_remaining_count_before_branch_is_known(self):
        self.open('graphics', 'campaign-shaders', 1, 2)
        row = self.items('campaign-shaders-materials', 100, 100, 'complete')
        self.assertFalse(row['progress']['activeWork']['passes']['known'])
        row = self.items('campaign-shaders-programs', 1, 10)
        passes = row['progress']['activeWork']['passes']
        self.assertTrue(passes['known'])
        self.assertEqual(passes['total'], 11)
        graphics = next(item for group in row['progress']['buildOverview']['groups'] for item in group['operations'] if item['id'] == 'graphics')
        self.assertFalse(any(item['id'] in stage_plan.CONDITIONAL_PREPARATION_PASSES['campaign-shaders']
                             for item in graphics['preparation']['checkpoints'][1]['passes']))

    def test_observed_binary_material_branch_qualifies_its_actual_shader_pass_plan(self):
        self.open('graphics', 'campaign-shaders', 1, 2)
        row = self.items('campaign-shaders-binary-materials', 0, 10)
        self.assertTrue(row['progress']['activeWork']['passes']['known'])
        self.assertEqual(row['progress']['activeWork']['passes']['total'], 13)

    def test_shader_alias_completion_does_not_close_any_named_outer_pass(self):
        self.open('graphics', 'campaign-shaders', 1, 2)
        self.items('campaign-shaders-inventory', 0, 300)
        row = self.items('campaign-shaders-variants', 100, 100, 'complete', unit='variants')
        work = row['progress']['activeWork']
        self.assertEqual((work['done'], work['index'], work['passes']['done']), (1, 2, 0))
        graphics = next(item for group in row['progress']['buildOverview']['groups'] for item in group['operations'] if item['id'] == 'graphics')
        current = graphics['preparation']['checkpoints'][1]
        self.assertEqual(current['nestedCounter']['done'], 100)
        inventory = next(item for item in current['passes'] if item['id'] == 'campaign-shaders-inventory')
        self.assertFalse(inventory['closed'])
        self.assertNotIn('campaign-shaders-variants', [item['id'] for item in current['passes']])

    def test_completed_checkpoint_does_not_expose_unobserved_pending_inner_passes_after_retry(self):
        self.open('graphics', 'campaign-compute', 0, 2)
        self.items('campaign-compute-kernels', 36, 36, 'complete')
        self.store.progress(self.session, 'build', 'prepare-substage:campaign-compute', 1, 2, 'steps',
                            operation='graphics', status='complete')
        self.store.progress(self.session, 'build', 'starting')
        self.open('graphics', 'campaign-shaders', 1, 2)
        row = self.row()
        graphic = next(item for group in row['progress']['buildOverview']['groups'] for item in group['operations'] if item['id'] == 'graphics')
        old = graphic['preparation']['checkpoints'][0]
        self.assertTrue(old['closed'])
        self.assertEqual(old['passes'], [])
        self.assertEqual(old['detail'], 'aggregate')

    def test_unity_shader_current_pass_is_visible_without_closing_bank_or_crediting_import(self):
        self.open('mod-banks', 'mod-resource-banks', 3, 4)
        previous = self.row()['progress']['stagePercent']
        self.store.progress(self.session, 'build', 'unity-shader-compile', 4903, 12288, 'variants',
                            operation='mod-banks', detail='Shader pass #1; completed passes 0')
        row = self.row()
        operation = next(item for group in row['progress']['buildOverview']['groups'] for item in group['operations'] if item['id'] == 'mod-banks')
        self.assertEqual(operation['compiler']['scope'], 'pass')
        self.assertEqual(operation['compiler']['done'], 4903)
        self.assertEqual(row['progress']['buildOverview']['active'], 'mod-banks')
        self.assertEqual(row['progress']['stagePercent'], previous)
        self.store.progress(self.session, 'build', 'unity-shader-compile', 12288, 12288, 'variants',
                            operation='mod-banks', status='complete')
        self.assertNotIn('mod-banks', self.row()['progressPlan']['completed'])
        self.assertNotIn('unity-import', self.row()['progressPlan']['completed'])

    def test_foreign_unity_shader_counter_cannot_reopen_bank_or_supplement_player(self):
        self.store.operation(self.session, 'build', 'player')
        previous = self.row()['progress']['stagePercent']
        self.store.progress(self.session, 'build', 'unity-shader-task', 99, 100, 'steps', operation='mod-banks')
        row = self.row()
        self.assertEqual(row['progress']['buildOverview']['active'], 'player')
        self.assertEqual(row['progress']['stagePercent'], previous)
        self.assertEqual(row['progressPlan'].get('unityCompiler', {}), {})

    def test_public_unity_shader_task_credits_current_bank_share_and_resets_only_child(self):
        self.open('mod-banks', 'mod-resource-banks', 3, 4)
        previous = self.row()['progress']['stagePercent']
        for done in (25, 50, 100):
            self.store.progress(self.session, 'build', 'unity-shader-task', done, 100, 'steps', operation='mod-banks')
            row = self.row()
            self.assertGreater(row['progress']['stagePercent'], previous)
            previous = row['progress']['stagePercent']
            self.assertLessEqual(row['progressPlan']['fractions']['mod-banks'], (3 + .75) / 4)
            self.assertNotIn('mod-banks', row['progressPlan']['completed'])
        self.store.progress(self.session, 'build', 'unity-shader-compile', 0, 10, 'variants', operation='mod-banks')
        self.assertEqual(self.row()['progress']['stagePercent'], previous)

    def test_every_known_preparation_plan_matches_its_real_checkpoint_denominator(self):
        for operation, names in stage_plan.PREPARATION_CHECKPOINTS.items():
            for index, checkpoint in enumerate(names):
                self.open(operation, checkpoint, index, len(names))
                work = self.row()['progress']['activeWork']
                self.assertEqual((work['done'], work['total'], work['index']), (index, len(names), index + 1), operation)
                self.assertEqual(work['remaining'], len(names) - index)
        self.assertEqual(len(stage_plan._checkpoint_names('startup-content', 8)), 8)
        self.assertEqual(len(stage_plan._checkpoint_names('preparation-contracts', 4)), 4)
        self.assertEqual(stage_plan._checkpoint_names('graphics', 3), ())


if __name__ == '__main__': unittest.main()
