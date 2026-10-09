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


if __name__ == '__main__': unittest.main()
