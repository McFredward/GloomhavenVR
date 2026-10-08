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
        interval = mock.patch('state.PROGRESS_INTERVAL', 0)
        interval.start(); self.addCleanup(interval.stop)
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

    def closed_preparation(self, *, work_key=None):
        self.store.begin_stage(self.session, 'build', 'old-release-key', work_key=work_key)
        self.store.operation(self.session, 'build', 'startup-content')
        self.store.progress(self.session, 'build', 'prepare-substage:native-sprites', 3, 6, 'checkpoints',
                            operation='startup-content', status='start')
        closed = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-items:native-sprites', 80, 100, 'items')
        self.assertGreater(self.progress()['stagePercent'], closed)
        return closed

    def test_release_key_change_preserves_closed_work_but_drops_uncommitted_items(self):
        closed = self.closed_preparation(work_key='owned-game-target-profile')
        old = self.store.load(self.session)
        old['stages'][5]['status'] = 'failed'
        old['needsActions'] = [{'code': 'build_tool_failed', 'stage': 'build'}]
        self.store.save(old)
        reopened = state.Store(self.store.root)
        reopened.begin_run(self.session)
        reopened.begin_stage(self.session, 'build', 'repaired-release-key', work_key='owned-game-target-profile')
        current = reopened.load(self.session)['stages'][5]
        self.assertEqual(current['status'], 'pending')
        self.assertEqual(current['progress']['stagePercent'], closed)
        self.assertEqual(current['progress']['phase'], 'pending')
        self.assertIsNone(current['progress']['buildOverview']['active'])
        self.assertEqual(current['progressPlan']['preparationCompleted']['startup-content'], {'done': 3, 'total': 6})
        self.assertEqual(current['progressPlan']['preparationScopes'], {})
        self.assertNotIn('startup-content', current['progressPlan']['completed'])
        operations = [item for group in current['progress']['buildOverview']['groups'] for item in group['operations']]
        self.assertTrue(all(item['status'] == 'retained' for item in operations if item['closed']))
        self.assertFalse(any(item['status'] in ('running', 'checking', 'failed', 'reused') for item in operations))
        reopened.progress(self.session, 'build', 'starting')
        reopened.operation(self.session, 'build', 'game-inputs')
        progress = reopened.load(self.session)['stages'][5]['progress']
        self.assertEqual(progress['stagePercent'], closed)
        self.assertEqual(progress['buildOverview']['active'], 'game-inputs')
        self.assertEqual(progress['activeWork']['operation'], 'game-inputs')

    def test_legacy_scope_adoption_requires_matching_engine_qualification(self):
        for previous, expected in [('owned-scope', True), ('different-game', False), (None, False)]:
            with self.subTest(previous=previous):
                self.store = state.Store(Path(self.temp.name) / ('legacy-' + str(previous)))
                self.session = self.store.create({'gameRoot': 'owned'})['session']
                closed = self.closed_preparation()
                self.store.begin_run(self.session)
                self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned-scope', previous_work_key=previous)
                current = self.store.load(self.session)['stages'][5]
                self.assertEqual(current['progress']['stagePercent'], closed if expected else 0)
                self.assertEqual(current['progressWorkKey'], 'owned-scope')
                self.assertEqual(current['progressKey'], 'new-release-key')

    def test_legacy_terminal_failure_restores_only_closed_checkpoint_history(self):
        closed = self.closed_preparation()
        self.store.progress(self.session, 'build', 'prepare-substage:native-sprites', 3, 6, 'checkpoints',
                            operation='startup-content', status='failed')
        self.store.progress(self.session, 'build', 'operation:startup-content', operation='startup-content', status='failed')
        saved = self.store.load(self.session)
        saved['status'] = saved['stages'][5]['status'] = 'failed'
        plan = saved['stages'][5]['progressPlan']
        plan.pop('preparationCompleted', None)
        self.assertEqual(plan['preparationScopes'], {}, 'old terminal operation discarded the live scope')
        self.store.save(saved)
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned', previous_work_key='owned')
        progress = self.progress()
        self.assertEqual(progress['stagePercent'], closed)
        current = self.store.load(self.session)['stages'][5]
        self.assertEqual(current['progressPlan']['preparationCompleted']['startup-content'], {'done': 3, 'total': 6})
        self.assertIsNone(progress['buildOverview']['active'])

    def test_legacy_checkpoint_history_before_choice_change_cannot_add_credit(self):
        self.closed_preparation()
        saved = self.store.load(self.session)
        plan = saved['stages'][5]['progressPlan']
        plan.pop('preparationCompleted', None); plan['preparationScopes'] = {}
        saved['events'].append({'code': 'choices_updated'})
        self.store.save(saved)
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned', previous_work_key='owned')
        self.assertEqual(self.progress()['stagePercent'], 48)
        self.assertEqual(self.store.load(self.session)['stages'][5]['progressPlan']['preparationCompleted'], {})

    def test_changed_game_target_profile_or_workflow_cannot_adopt_retained_work(self):
        for scope in ('changed-game', 'changed-target', 'changed-profile', 'update-mod', 'update-profile'):
            with self.subTest(scope=scope):
                self.store = state.Store(Path(self.temp.name) / scope)
                self.session = self.store.create({'gameRoot': 'owned'})['session']
                self.closed_preparation(work_key='old-owned-scope')
                # Even an unchanged receipt key cannot override a genuinely
                # changed work scope. A legacy hint cannot replace a saved one.
                self.store.begin_stage(self.session, 'build', 'old-release-key', work_key=scope, previous_work_key=scope)
                current = self.store.load(self.session)['stages'][5]
                self.assertEqual(current['progress']['stagePercent'], 0)
                self.assertEqual(current['progressPlan']['completed'], [])
                self.assertEqual(current['progressWorkKey'], scope)

    def test_retained_work_scope_does_not_qualify_old_stage_receipts(self):
        self.closed_preparation(work_key='owned-scope')
        output = self.store.session_dir(self.session) / 'old-build-result.txt'
        output.write_text('old APK receipt fixture')
        self.store.publish(self.session, 'build', 'old-release-key', [output], {})
        self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned-scope')
        self.assertGreater(self.progress()['stagePercent'], 0)
        self.assertIsNone(self.store.valid(self.session, 'build', 'new-release-key'))
        self.assertIsNotNone(self.store.valid(self.session, 'build', 'old-release-key'))

    def test_changed_release_retains_closed_raw_packages_and_staging_receipts(self):
        self.store.begin_stage(self.session, 'build', 'old-release-key', work_key='owned-scope')
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 16, 16, 'batches', status='reuse')
        for section in stage_plan.RECOVERY_SECTIONS[:-1]:
            self.store.progress(self.session, 'build', 'recovery-section:' + section, 1, 1, 'sections', status='reuse')
            if section == 'batches': self.store.progress(self.session, 'build', 'recovery-batches', 16, 16, 'batches', status='reuse')
        self.store.progress(self.session, 'build', 'recovery-section:staging', status='start')
        for step in stage_plan.STAGING_STEPS[:6]:
            self.store.progress(self.session, 'build', 'staging-section:' + step, 1, 1, 'steps', status='reuse')
        closed = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'staging-section:native', 0, 1, 'steps', status='start')
        self.store.progress(self.session, 'build', 'recovery-asset-references', 95, 100, 'files')
        self.assertGreater(self.progress()['stagePercent'], closed)
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned-scope')
        progress = self.progress()
        self.assertEqual(progress['stagePercent'], closed)
        overview = progress['buildOverview']
        self.assertEqual(overview['recovery']['batches'], {'done': 16, 'total': 16})
        self.assertEqual([item['id'] for item in overview['recovery']['staging'] if item['closed']], list(stage_plan.STAGING_STEPS[:6]))
        self.assertTrue(all(item['status'] == 'retained' for item in overview['recovery']['staging'][:6]))
        self.assertIsNone(overview['recovery']['stagingCounter'])
        self.assertIsNone(overview['active'])

    def test_invalid_work_scope_does_not_mutate_progress(self):
        self.closed_preparation(work_key='owned-scope')
        before = self.progress()['stagePercent']
        for parameters in ({'work_key': ''}, {'work_key': True}, {'work_key': 'x' * 161}, {'previous_work_key': 'old'}):
            with self.subTest(parameters=parameters), self.assertRaises(state.WizardError):
                self.store.begin_stage(self.session, 'build', 'new', **parameters)
        with self.assertRaises(state.WizardError): self.store.begin_stage(self.session, 'source', 'new', work_key='owned')
        self.assertEqual(self.progress()['stagePercent'], before)

    def test_new_artifact_reopens_source_dependent_code_import_and_player_work(self):
        self.store.begin_stage(self.session, 'build', 'old-release-key', work_key='owned-scope')
        self.store.operation(self.session, 'build', 'output-verify', complete=True)
        self.assertGreater(self.progress()['stagePercent'], 99)
        self.store.begin_stage(self.session, 'build', 'new-release-key', work_key='owned-scope')
        row = self.store.load(self.session)['stages'][5]
        names = stage_plan.PLANS['build'][:stage_plan.PLANS['build'].index('weave')]
        self.assertEqual(row['progressPlan']['completed'], list(names))
        self.assertEqual(row['progress']['stagePercent'], sum(stage_plan.BUILD_SHARES[name] for name in names))
        operations = [item for group in row['progress']['buildOverview']['groups'] for item in group['operations']]
        self.assertTrue(all(item['status'] == 'pending' for item in operations if item['id'] not in names))
        self.assertNotIn('player', row['progressPlan']['completed'])
        self.assertNotIn('output-verify', row['progressPlan']['completed'])

    def test_update_workflows_use_only_their_real_operations_and_output_verification(self):
        for mode, operations in stage_plan.UPDATE_PLANS.items():
            with self.subTest(mode=mode):
                self.store = state.Store(Path(self.temp.name) / mode)
                self.session = self.store.create({'gameRoot': 'owned', 'mode': mode})['session']
                fresh = self.progress()
                self.assertEqual(fresh['stageTotal'], len(operations))
                self.assertEqual(fresh['buildOverview']['total'], len(operations))
                self.store.begin_run(self.session)
                self.store.begin_stage(self.session, 'build', 'update-input', work_key=mode + '-owned-scope')
                self.store.progress(self.session, 'build', 'game-hash', 50, 100, 'bytes')
                self.assertEqual(self.progress()['stageOperation'], 'update-owned-game')
                previous = self.progress()['stagePercent']
                self.assertGreater(previous, 0)
                for name in operations[:-1]:
                    progress = self.store.operation(self.session, 'build', name, complete=True)
                    self.assertGreaterEqual(progress['stagePercent'], previous)
                    self.assertLess(progress['stagePercent'], 100)
                    previous = progress['stagePercent']
                self.store.progress(self.session, 'build', 'output-verify', 50, 100, 'bytes')
                final = self.progress()
                self.assertEqual(final['stageOperation'], 'update-verify')
                self.assertGreater(final['stagePercent'], previous)
                self.assertEqual([row['id'] for group in final['buildOverview']['groups'] for row in group['operations']], list(operations))
                self.assertEqual(final['buildOverview']['recovery']['sections'], [])
                self.assertEqual(final['buildOverview']['recovery']['staging'], [])
                self.assertIsNone(final['buildOverview']['recovery']['batches'])
                with self.assertRaises(state.WizardError): self.store.operation(self.session, 'build', 'recovery')
                with self.assertRaises(state.WizardError): self.store.operation(self.session, 'build', 'unity-import')
                saved = self.store.load(self.session)
                saved['stages'][5]['status'] = 'complete'; self.store.save(saved)
                self.assertEqual(self.progress()['stagePercent'], 100)

    def test_update_recipe_change_reopens_code_or_repack_without_global_asset_credit(self):
        for mode, expected in [('update-mod', ['update-owned-game']), ('update-profile', ['update-owned-game', 'update-profile'])]:
            with self.subTest(mode=mode):
                self.store = state.Store(Path(self.temp.name) / mode)
                self.session = self.store.create({'gameRoot': 'owned', 'mode': mode})['session']
                self.store.begin_stage(self.session, 'build', 'old-update-key', work_key='same-' + mode)
                self.store.operation(self.session, 'build', 'update-verify', complete=True)
                self.store.begin_stage(self.session, 'build', 'new-update-key', work_key='same-' + mode)
                current = self.store.load(self.session)['stages'][5]
                self.assertEqual(current['progressPlan']['completed'], expected)
                self.assertEqual(current['progress']['stagePercent'], sum(stage_plan.UPDATE_SHARES[mode][name] for name in expected))
                self.assertNotIn('recovery', current['progressPlan']['completed'])
                self.assertNotIn('update-verify', current['progressPlan']['completed'])

    def test_update_helper_aliases_contribute_counters_without_completing_the_parent(self):
        self.store = state.Store(Path(self.temp.name) / 'update-helpers')
        self.session = self.store.create({'gameRoot': 'owned', 'mode': 'update-mod'})['session']
        self.store.begin_stage(self.session, 'build', 'update-input')
        for parent, aliases in [('update-mod-source', ('source-snapshot', 'game-snapshot', 'snapshot-check')),
                                ('update-code', ('weave', 'package-api', 'unity-import', 'unity-validation', 'unity-build')),
                                ('update-art', ('mod-banks', 'mod-resource-banks', 'unity-import', 'unity-build')),
                                ('update-repack', ('delivery',))]:
            self.store.operation(self.session, 'build', parent)
            for alias in aliases:
                with self.subTest(parent=parent, alias=alias):
                    progress = self.store.progress(self.session, 'build', 'unity-progress', 50, 100, 'tasks', operation=alias, status='complete')
                    self.assertEqual(progress['stageOperation'], parent)
                    self.assertEqual(progress['childOperation'], alias)
                    current = self.store.load(self.session)['stages'][5]
                    self.assertNotIn(parent, current['progressPlan']['completed'])
                    self.assertEqual(progress['buildOverview']['active'], parent)
            self.store.operation(self.session, 'build', parent, complete=True)
            self.assertIn(parent, self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        self.store = state.Store(Path(self.temp.name) / 'profile-no-code')
        self.session = self.store.create({'gameRoot': 'owned', 'mode': 'update-profile'})['session']
        self.store.begin_stage(self.session, 'build', 'profile-input')
        for alias in ('weave', 'package-api', 'unity-import', 'mod-banks', 'delivery', 'unknown-future-op'):
            with self.subTest(alias=alias), self.assertRaises(state.WizardError):
                self.store.progress(self.session, 'build', 'tool:child', 1, 1, 'commands', operation=alias, status='complete')
        self.assertEqual(self.progress()['stagePercent'], 0)

    def test_mode_change_cannot_inherit_full_asset_progress_or_update_repack_progress(self):
        self.closed_preparation(work_key='build-scope')
        saved = self.store.load(self.session)
        saved['choices']['mode'] = 'update-mod'; self.store.save(saved)
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, 'build', 'update-key', work_key='update-scope')
        current = self.store.load(self.session)['stages'][5]
        self.assertEqual(current['workMode'], 'update-mod')
        self.assertEqual(current['progress']['stagePercent'], 0)
        self.assertEqual(current['progress']['stageTotal'], 6)
        self.store.operation(self.session, 'build', 'update-repack', complete=True)
        self.assertGreater(self.progress()['stagePercent'], 0)
        saved = self.store.load(self.session)
        saved['choices']['mode'] = 'update-profile'; self.store.save(saved)
        self.store.begin_run(self.session)
        self.assertEqual(self.progress()['stagePercent'], 0)
        self.assertEqual(self.progress()['stageTotal'], 4)

    def test_build_overview_lists_real_grouped_operations_with_no_duplicate_or_missing_work(self):
        self.assertEqual(tuple(name for names in stage_plan.BUILD_GROUPS.values() for name in names), stage_plan.PLANS['build'])
        self.assertEqual(sum(stage_plan.BUILD_SHARES.values()), 100)
        self.store.operation(self.session, 'build', 'startup-content')
        overview = self.progress()['buildOverview']
        self.assertEqual(overview['total'], 23)
        self.assertEqual(overview['active'], 'startup-content')
        self.assertEqual([group['id'] for group in overview['groups']], ['inputs', 'recovery', 'project', 'code', 'import', 'export'])
        remaining = [item['id'] for group in overview['groups'] for item in group['operations'] if not item['closed']]
        self.assertIn('unity-import', remaining)
        self.assertIn('player', remaining)
        self.assertEqual(remaining[0], 'startup-content')

    def test_preparation_copy_counts_advance_total_without_publishing_parent_success(self):
        self.store.operation(self.session, 'build', 'project-files')
        first = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-project-copy', 25, 100, 'files')
        middle = self.progress()['stagePercent']
        self.assertGreater(middle, first)
        self.store.progress(self.session, 'build', 'prepare-project-copy', 80, 100, 'files')
        self.assertGreater(self.progress()['stagePercent'], middle)
        row = {'id': 'build', 'status': 'running'}
        value = stage_plan.advance(row, {'phase': 'prepare-project-copy', 'done': 100, 'total': 100,
                                        'unit': 'files', 'percent': 100, 'updatedAt': 1}, status='complete')
        self.assertNotIn('project-files', row['progressPlan']['completed'])
        self.assertEqual(value['buildOverview']['active'], 'project-files')
        stage_plan.advance(row, {'phase': 'operation:project-files', 'done': 1, 'total': 1,
                                'unit': 'operations', 'percent': 100, 'updatedAt': 2}, 'project-files', 'complete')
        self.assertIn('project-files', row['progressPlan']['completed'])

    def test_raw_section_reuse_cannot_complete_recovery_before_staging(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 16, 16, 'batches', status='reuse')
        for section in stage_plan.RECOVERY_SECTIONS[:-1]:
            self.store.progress(self.session, 'build', 'recovery-section:' + section, 1, 1, 'sections', status='reuse')
            if section == 'batches':
                self.store.progress(self.session, 'build', 'recovery-batches', 16, 16, 'batches', status='reuse')
        self.store.progress(self.session, 'build', 'recovery-section:staging', status='start')
        self.store.progress(self.session, 'build', 'staging-section:copy', 0, 1, 'steps', status='start')
        self.store.progress(self.session, 'build', 'staging-copy', 0, 189701, 'files')
        overview = self.progress()['buildOverview']
        self.assertEqual(overview['recovery']['batches'], {'done': 16, 'total': 16})
        self.assertEqual(sum(item['closed'] for item in overview['recovery']['sections']), 7)
        self.assertEqual(sum(item['closed'] for item in overview['recovery']['staging']), 2)
        self.assertTrue(all(item['status'] == 'reused' for item in overview['recovery']['sections'][:-1]))
        self.assertNotIn('recovery', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'staging-copy', 75000, 189701, 'files')
        self.assertGreater(self.progress()['stagePercent'], before)

    def test_retry_overview_names_live_earlier_check_without_losing_retained_completion(self):
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        self.store.progress(self.session, 'build', 'staging-section:copy', 0, 1, 'steps')
        saved = self.store.load(self.session); saved['stages'][5]['status'] = 'failed'; self.store.save(saved)
        previous = self.progress()['stagePercent']
        self.store = state.Store(self.store.root)
        self.store.begin_stage(self.session, 'build', 'same-input')
        saved = self.store.load(self.session); saved['stages'][5]['status'] = 'running'; self.store.save(saved)
        self.store.progress(self.session, 'build', 'starting')
        self.store.operation(self.session, 'build', 'game-inputs')
        current = self.progress()
        self.assertEqual(current['stagePercent'], previous)
        overview = current['buildOverview']
        self.assertEqual(overview['active'], 'game-inputs')
        self.assertEqual(overview['groups'][0]['operations'][0]['status'], 'checking')
        self.assertEqual(overview['groups'][0]['operations'][1]['status'], 'retained')
        self.assertEqual(current['activeWork']['operation'], 'game-inputs')
        self.assertEqual(overview['done'], 6)

    def test_preparation_child_receipt_advances_parent_without_falsely_completing_it(self):
        self.store.operation(self.session, 'build', 'startup-content')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-substage:movies', 2, 6, 'checkpoints',
                            operation='startup-content', status='complete')
        current = self.progress()
        self.assertGreater(current['stagePercent'], before)
        self.assertNotIn('startup-content', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        self.assertEqual(current['buildOverview']['active'], 'startup-content')
        self.store.progress(self.session, 'build', 'operation:startup-content', 1, 1, 'operations',
                            operation='startup-content', status='reuse')
        row = self.progress()['buildOverview']['groups'][2]['operations'][1]
        self.assertEqual(row['id'], 'startup-content'); self.assertEqual(row['status'], 'reused')

    def test_earlier_recovery_replay_cannot_credit_a_later_incomplete_player(self):
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.operation(self.session, 'build', 'player')
        self.store.progress(self.session, 'build', 'unity-progress', 20, 100, 'tasks')
        previous = self.progress()['stagePercent']
        reopened = state.Store(self.store.root)
        reopened.begin_stage(self.session, 'build', 'same-input')
        reopened.progress(self.session, 'build', 'starting')
        reopened.operation(self.session, 'build', 'recovery')
        reopened.progress(self.session, 'build', 'recovery-plan', 16, 16, 'batches', status='reuse')
        reopened.progress(self.session, 'build', 'recovery-section:batches', 1, 1, 'sections', status='reuse')
        reopened.progress(self.session, 'build', 'recovery-batches', 16, 16, 'batches', status='reuse')
        current = reopened.load(self.session)['stages'][5]
        self.assertEqual(current['progressPlan']['fractions']['player'], .2)
        self.assertEqual(current['progress']['stagePercent'], previous)
        self.assertEqual(current['progress']['buildOverview']['active'], 'recovery')
        self.assertNotIn('player', current['progressPlan']['completed'])

    def test_preparation_items_share_only_the_opened_real_checkpoint(self):
        self.store.operation(self.session, 'build', 'textures')
        self.store.progress(self.session, 'build', 'prepare-substage:native-texture2d', 2, 3, 'checkpoints',
                            operation='textures', status='start')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-items:native-texture2d', 250, 1000, 'items', status='complete')
        row = self.store.load(self.session)['stages'][5]
        self.assertEqual(row['progressPlan']['fractions']['textures'], 2.25 / 3)
        self.assertGreater(row['progress']['stagePercent'], before)
        scope = row['progressPlan']['preparationScopes']['textures']
        self.assertEqual(scope['itemFraction'], .25)
        self.assertEqual(scope['items'], {'done': 250, 'total': 1000})
        self.assertEqual(row['progress']['activeWork'], {'operation': 'textures', 'done': 2, 'total': 3,
                                                       'unit': 'steps', 'percent': 75.0})
        self.assertNotIn('textures', row['progressPlan']['completed'])
        self.store.progress(self.session, 'build', 'prepare-items:native-texture2d', 1000, 1000, 'items', status='complete')
        self.assertNotIn('textures', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        self.store.progress(self.session, 'build', 'prepare-substage:native-texture2d', 3, 3, 'checkpoints',
                            operation='textures', status='complete')
        self.assertNotIn('textures', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        self.store.operation(self.session, 'build', 'textures', complete=True)
        self.assertIn('textures', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])

    def test_preparation_next_checkpoint_resets_only_its_secondary_counter(self):
        self.store.operation(self.session, 'build', 'textures')
        self.store.progress(self.session, 'build', 'prepare-substage:native-cubemaps', 0, 3, 'checkpoints',
                            operation='textures', status='start')
        self.store.progress(self.session, 'build', 'prepare-items:native-cubemaps', 90, 100, 'items')
        previous = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-substage:native-cubemaps', 1, 3, 'checkpoints',
                            operation='textures', status='complete')
        self.store.progress(self.session, 'build', 'prepare-substage:ordinary-texture-audit', 1, 3, 'checkpoints',
                            operation='textures', status='start')
        self.store.progress(self.session, 'build', 'prepare-items:ordinary-texture-audit', 0, 1000, 'items')
        self.assertEqual(self.progress()['percent'], 0)
        self.assertGreaterEqual(self.progress()['stagePercent'], previous)
        self.assertEqual(self.store.load(self.session)['stages'][5]['progressPlan']['fractions']['textures'], 1 / 3)
        self.store.progress(self.session, 'build', 'prepare-items:ordinary-texture-audit', 250, 1000, 'items')
        self.assertEqual(self.store.load(self.session)['stages'][5]['progressPlan']['fractions']['textures'], 1.25 / 3)

    def open_startup_checkpoint(self, checkpoint, done=2):
        self.store.operation(self.session, 'build', 'startup-content')
        self.store.progress(self.session, 'build', 'prepare-substage:' + checkpoint, done, 11, 'checkpoints',
                            operation='startup-content', status='start')

    def startup_fraction(self):
        return self.store.load(self.session)['stages'][5]['progressPlan']['fractions']['startup-content']

    def test_movie_passes_advance_the_same_main_and_global_progress_without_closing_parent(self):
        self.open_startup_checkpoint('startup-movies')
        before=self.progress()['stagePercent']; share=0.
        for name,weight in stage_plan.STARTUP_ITEM_SCHEDULES['startup-movies'].items():
            self.store.progress(self.session,'build','prepare-items:'+name,0,100,'bytes',status='start')
            self.store.progress(self.session,'build','prepare-items:'+name,50,100,'bytes')
            self.assertAlmostEqual(self.startup_fraction(),(2+share+weight*.5)/11)
            self.assertGreater(self.progress()['stagePercent'],before)
            self.assertAlmostEqual(self.progress()['activeWork']['percent'],100*(2+share+weight*.5)/11,places=5)
            self.store.progress(self.session,'build','prepare-items:'+name,100,100,'bytes',status='complete')
            share+=weight;before=self.progress()['stagePercent']
        plan=self.store.load(self.session)['stages'][5]['progressPlan']
        self.assertNotIn('startup-content',plan['completed'])
        self.assertEqual(self.progress()['activeWork']['done'],2)
        self.assertAlmostEqual(self.startup_fraction(),2.99/11)
        self.store.progress(self.session,'build','prepare-substage:startup-movies',3,11,'checkpoints',
                            operation='startup-content',status='complete')
        self.assertAlmostEqual(self.startup_fraction(),3/11)

    def test_movie_nested_hash_advances_at_most_one_open_media_file(self):
        self.open_startup_checkpoint('startup-movies')
        self.store.progress(self.session,'build','prepare-items:startup-movies-media',1,4,'files')
        self.store.progress(self.session,'build','file-hash',50,100,'bytes')
        self.assertAlmostEqual(self.startup_fraction(),(2+.10*1.5/4)/11)
        for _ in range(3):self.store.progress(self.session,'build','file-hash',100,100,'bytes',status='complete')
        self.assertAlmostEqual(self.startup_fraction(),(2+.10*1.99/4)/11)
        self.assertNotIn('startup-content',self.store.load(self.session)['stages'][5]['progressPlan']['completed'])
        self.store.progress(self.session,'build','prepare-items:startup-movies-media',2,4,'files')
        self.assertAlmostEqual(self.startup_fraction(),(2+.10*.5)/11)

    def test_interleaved_sprite_container_and_item_counts_sum_only_observed_shares(self):
        self.open_startup_checkpoint('native-sprites',3)
        for name in ('identities','packed','targets'):
            self.store.progress(self.session,'build','prepare-items:native-sprites-'+name,10,10,'items',status='complete')
        self.store.progress(self.session,'build','prepare-items:native-sprites-containers',1,4,'containers')
        self.store.progress(self.session,'build','prepare-items:native-sprites',1,100,'items')
        self.assertAlmostEqual(self.startup_fraction(),(3+.2+.1*.25+.7*.01)/11)
        self.store.progress(self.session,'build','prepare-items:native-sprites',25,100,'items')
        self.assertAlmostEqual(self.startup_fraction(),(3+.2+.1*.25+.7*.25)/11)
        self.assertNotIn('startup-content',self.store.load(self.session)['stages'][5]['progressPlan']['completed'])

    def test_audio_decode_blocks_belong_only_to_current_clip_pass_and_empty_writes_are_valid(self):
        self.open_startup_checkpoint('startup-audio',5)
        self.store.progress(self.session,'build','prepare-items:startup-audio-metadata',100,100,'objects',status='complete')
        self.store.progress(self.session,'build','prepare-items:startup-audio-preflight',1,4,'clips')
        self.store.progress(self.session,'build','prepare-items:startup-audio-decode',50,100,'blocks')
        self.assertAlmostEqual(self.startup_fraction(),(5+.05+.65*1.5/4)/11)
        for _ in range(3):self.store.progress(self.session,'build','prepare-items:startup-audio-decode',100,100,'blocks',status='complete')
        self.assertAlmostEqual(self.startup_fraction(),(5+.05+.65*1.99/4)/11)
        self.store.progress(self.session,'build','prepare-items:startup-audio-preflight',4,4,'clips',status='complete')
        self.store.progress(self.session,'build','prepare-items:startup-audio-write',0,0,'clips',status='complete')
        self.assertAlmostEqual(self.startup_fraction(),5.99/11)
        self.assertNotIn('startup-content',self.store.load(self.session)['stages'][5]['progressPlan']['completed'])

    def test_new_startup_counters_cannot_use_foreign_closed_or_previous_attempt_scopes(self):
        self.store.begin_stage(self.session,'build','same-input')
        self.open_startup_checkpoint('startup-movies')
        before=self.progress()['stagePercent']
        for phase in ('prepare-items:startup-audio-metadata','prepare-items:native-sprites-containers'):
            self.store.progress(self.session,'build',phase,100,100,'items',status='complete')
            self.assertEqual(self.progress()['stagePercent'],before)
        self.store.progress(self.session,'build','prepare-items:startup-movies-assets',100,100,'bytes',operation='player',status='complete')
        self.assertEqual(self.startup_fraction(),2/11)
        self.store.begin_stage(self.session,'build','same-input')
        self.store.progress(self.session,'build','starting')
        self.store.progress(self.session,'build','prepare-items:startup-movies-assets',100,100,'bytes',status='complete')
        self.assertEqual(self.startup_fraction(),2/11)
        self.open_startup_checkpoint('startup-movies')
        self.store.progress(self.session,'build','prepare-substage:startup-movies',3,11,'checkpoints',operation='startup-content',status='complete')
        self.store.progress(self.session,'build','prepare-items:startup-movies-assets',100,100,'bytes',status='complete')
        self.assertEqual(self.startup_fraction(),3/11)

    def test_preparation_unopened_mismatched_and_nested_platform_items_cannot_add_credit(self):
        self.store.operation(self.session, 'build', 'textures')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-items:native-cubemaps', 99, 100, 'items', status='complete')
        self.assertEqual(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'prepare-substage:native-cubemaps', 0, 3, 'checkpoints',
                            operation='textures', status='start')
        for phase in ('prepare-items:platform-images', 'prepare-items:native-texture2d', 'arbitrary-nested'):
            self.store.progress(self.session, 'build', phase, 100, 100, 'items', operation='textures',
                                status='complete' if phase.startswith('prepare-items:') else None)
            self.assertEqual(self.progress()['stagePercent'], before, phase)
        self.assertNotIn('textures', self.store.load(self.session)['stages'][5]['progressPlan']['completed'])

    def test_earlier_preparation_replay_never_credits_later_player_frontier(self):
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.operation(self.session, 'build', 'player')
        self.store.progress(self.session, 'build', 'unity-progress', 20, 100, 'tasks')
        previous = self.progress()['stagePercent']
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.progress(self.session, 'build', 'starting')
        self.store.operation(self.session, 'build', 'textures')
        self.store.progress(self.session, 'build', 'prepare-substage:native-texture2d', 2, 3, 'checkpoints',
                            operation='textures', status='start')
        self.store.progress(self.session, 'build', 'prepare-items:native-texture2d', 250, 1000, 'items')
        row = self.store.load(self.session)['stages'][5]
        self.assertEqual(row['progressPlan']['fractions']['player'], .2)
        self.assertEqual(row['progressPlan']['fractions']['textures'], .75)
        self.assertEqual(row['progress']['stagePercent'], previous)
        self.assertNotIn('player', row['progressPlan']['completed'])

    def test_preparation_closed_checkpoint_and_new_attempt_drop_stale_item_ownership(self):
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.operation(self.session, 'build', 'audio')
        self.store.progress(self.session, 'build', 'prepare-substage:bundled-audio', 0, 1, 'checkpoints',
                            operation='audio', status='start')
        self.store.progress(self.session, 'build', 'prepare-items:bundled-audio', 20, 100, 'items')
        before = self.progress()['stagePercent']
        self.store.begin_stage(self.session, 'build', 'same-input')
        self.store.progress(self.session, 'build', 'starting')
        self.store.progress(self.session, 'build', 'prepare-items:bundled-audio', 99, 100, 'items')
        self.assertEqual(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'prepare-substage:bundled-audio', 1, 2, 'checkpoints',
                            operation='audio', status='complete')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'prepare-items:bundled-audio', 99, 100, 'items')
        self.assertEqual(self.progress()['stagePercent'], before)

    def test_bee_new_dags_reset_secondary_progress_only(self):
        self.store.operation(self.session, 'build', 'player')
        parser = ProgressParser()
        for line in ('[200/400 0s] Clang a.cpp', '[399/400 0s] Clang b.cpp', '[1/400 0s] Link lib.so'):
            before = self.progress()['stagePercent']
            self.store.progress(self.session, 'build', **parser.parse(line, 'unity-build-current.log'))
            self.assertGreaterEqual(self.progress()['stagePercent'], before)
            self.assertLess(self.progress()['stagePercent'], 100)
        self.assertEqual(self.progress()['percent'], .2)

    def test_recovery_requires_batch_context_then_includes_its_measured_substeps(self):
        self.store.operation(self.session, 'build', 'recovery')
        initial = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 2761, 2761, 'collections')
        self.assertEqual(self.progress()['stagePercent'], initial)
        self.store.progress(self.session, 'build', 'recovery-batches', 1, 4, 'batches')
        measured = self.progress()['stagePercent']
        self.assertGreater(measured, initial)
        self.store.progress(self.session, 'build', 'recovery-batch', 1, 4, 'batches')
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 1, 100, 'collections')
        partial = self.progress()['stagePercent']
        self.assertGreater(partial, measured)
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 70, 100, 'collections')
        self.assertGreater(self.progress()['stagePercent'], partial)
        self.assertLess(self.progress()['stagePercent'], 100)
        self.store.progress(self.session, 'build', 'recovery-batches', 4, 4, 'batches')
        self.assertLess(self.progress()['stagePercent'], 100)
        self.store.operation(self.session, 'build', 'project-files')
        self.assertGreaterEqual(self.progress()['stagePercent'], measured)

    def recovery_batch(self, done=0, total=4):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-section:batches')
        self.store.progress(self.session, 'build', 'recovery-batches', done, total, 'batches')
        self.store.progress(self.session, 'build', 'recovery-batch', done, total, 'batches')

    def test_recovery_context_identifies_distinct_batches_and_clears_after_section(self):
        self.recovery_batch()
        self.store.progress(self.session, 'build', 'recovery-batch-export-verify', 1, 10, 'files')
        first = self.progress()
        self.assertEqual((first['recoveryBatchIndex'], first['recoveryBatchTotal']), (1, 4))
        reopened = state.Store(self.store.root)
        self.assertEqual(reopened.load(self.session)['stages'][5]['progress']['recoveryBatchIndex'], 1)
        self.store.progress(self.session, 'build', 'recovery-batches', 1, 4, 'batches')
        self.assertNotIn('recoveryBatchIndex', self.progress())
        self.store.progress(self.session, 'build', 'recovery-batch', 1, 4, 'batches')
        self.assertEqual(self.progress()['recoveryBatchIndex'], 2)
        self.store.progress(self.session, 'build', 'recovery-section:references')
        self.assertNotIn('recoveryBatchTotal', self.progress())
        self.assertEqual(self.progress()['recoverySection'], 'references')
        self.store.operation(self.session, 'build', 'project-files')
        self.assertNotIn('recoverySection', self.progress())

    def test_scoped_files_collections_and_checkpoints_move_total_before_completion(self):
        self.recovery_batch()
        for phase, unit in (('recovery-batch-bundle-copy', 'bundles'), ('recovery-export-receipt-hash', 'files'),
                            ('recovery-original-collection-merge', 'collections'), ('recovery-checkpoint-write', 'records')):
            with self.subTest(phase=phase):
                self.store.progress(self.session, 'build', phase, 1, 10, unit)
                small = self.progress()['stagePercent']
                self.store.progress(self.session, 'build', phase, 7, 10, unit)
                self.assertGreater(self.progress()['stagePercent'], small)
                self.assertLess(self.progress()['stagePercent'], 100)

    def test_large_file_bytes_are_fraction_of_one_parent_file(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-section:source')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 4, 'batches')
        self.store.progress(self.session, 'build', 'recovery-source-hash', 2, 4, 'files')
        initial = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-source-file-hash', 1, 10, 'bytes')
        small = self.progress()['stagePercent']
        self.assertGreater(small, initial)
        self.store.progress(self.session, 'build', 'recovery-source-file-hash', 8, 10, 'bytes')
        partial = self.progress()['stagePercent']
        self.assertGreater(partial, small)
        # A second file's resetting byte counter cannot reset the total or
        # count an unwitnessed extra file. Only the outer file count accepts it.
        self.store.progress(self.session, 'build', 'recovery-source-file-hash', 1, 100, 'bytes')
        self.assertEqual(self.progress()['stagePercent'], partial)
        self.store.progress(self.session, 'build', 'recovery-source-hash', 3, 4, 'files')
        self.assertGreater(self.progress()['stagePercent'], partial)
        self.assertLess(self.progress()['stagePercent'], 100)

    def test_native_index_scope_and_recipe_bytes_stay_inside_current_batch(self):
        self.recovery_batch(total=2)
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-native-index-read', 5, 5, 'bytes')
        self.assertEqual(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 5, 5, 'collections')
        self.store.progress(self.session, 'build', 'recovery-native-index-set', 1, 2, 'indexes')
        self.store.progress(self.session, 'build', 'recovery-native-index-read', 1, 10, 'bytes')
        small = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-native-index-read', 8, 10, 'bytes')
        self.assertGreater(self.progress()['stagePercent'], small)
        # The index stays within one read of one index of one package, even
        # though conversion now has a useful share of the whole-stage bar.
        self.assertLess(self.progress()['stagePercent'] - small,
                        stage_plan.BUILD_SHARES['recovery'] / (8 * len(stage_plan.BATCH_STEPS) * 2 * 3))
        reopened = state.Store(self.store.root)
        self.assertEqual(reopened.load(self.session)['stages'][5]['progress']['stagePercent'], self.progress()['stagePercent'])
        self.store.progress(self.session, 'build', 'recovery-native-recipe-merge', 2, 4, 'files')
        small = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-native-recipe-copy', 1, 10, 'bytes')
        self.store.progress(self.session, 'build', 'recovery-native-recipe-copy', 8, 10, 'bytes')
        self.assertGreater(self.progress()['stagePercent'], small)
        self.store.progress(self.session, 'build', 'recovery-native-index-write', 9, 10, 'rows')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-batches', 1, 2, 'batches')
        self.assertGreater(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'recovery-batch', 1, 2, 'batches')
        self.store.progress(self.session, 'build', 'recovery-batch-bundle-copy', 1, 100, 'bundles')
        self.assertGreater(self.progress()['stagePercent'], before)

    def test_recovery_source_recheck_has_distinct_scope_and_resume_keeps_nested_high_water(self):
        self.store.begin_stage(self.session, 'build', 'same')
        self.recovery_batch()
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 7, 10, 'collections')
        before = self.progress()['stagePercent']
        self.store = state.Store(self.store.root)
        self.store.begin_stage(self.session, 'build', 'same')
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 1, 10, 'collections')
        self.assertEqual(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 9, 10, 'collections')
        self.assertGreater(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'recovery-section:source-recheck')
        self.store.progress(self.session, 'build', 'recovery-source-hash', 1, 10, 'files')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-source-hash', 8, 10, 'files')
        self.assertGreater(self.progress()['stagePercent'], before)
        self.store.begin_stage(self.session, 'build', 'new')
        self.assertEqual(self.progress()['stagePercent'], 0)

    def test_conversion_counts_actual_packages_and_each_package_moves_whole_stage(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-section:source')
        self.assertIsNone(self.progress()['activeWork']['total'])
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.assertEqual(self.progress()['activeWork']['total'], 23)
        self.recovery_batch(done=4, total=16)
        self.assertEqual(self.progress()['activeWork']['done'], 6)
        self.assertEqual(self.progress()['activeWork']['operation'], 'recovery')
        previous = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-asset-export', 1000, 11000, 'collections')
        small = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-asset-export', 9000, 11000, 'collections')
        self.assertGreater(self.progress()['stagePercent'], small)
        self.assertEqual(self.progress()['activeWork']['done'], 6)
        self.store.progress(self.session, 'build', 'recovery-batches', 5, 16, 'batches')
        self.assertEqual(self.progress()['activeWork']['done'], 7)
        self.assertGreater(self.progress()['stagePercent'], previous + 1)
        self.store.operation(self.session, 'build', 'project-files')
        self.assertEqual(self.progress()['activeWork']['operation'], 'project-files')
        self.assertEqual(self.progress()['activeWork']['total'], 1)
        self.assertNotIn('recoveryBatchIndex', self.progress())

    def test_previous_distribution_keeps_its_percent_and_moves_on_first_measured_work(self):
        self.recovery_batch(done=4, total=16)
        self.store.progress(self.session, 'build', 'recovery-native-index-set', 1, 2, 'indexes')
        self.store.progress(self.session, 'build', 'recovery-native-recipe-merge', 100, 62880, 'files')
        saved = self.store.load(self.session)
        row = saved['stages'][5]
        row['progressPlan'].pop('workRevision')
        row['progressPlan']['percent'] = 29.97
        state.atomic_json(self.store.session_dir(self.session) / 'state.json', saved)
        reopened = state.Store(self.store.root)
        migrated = reopened.load(self.session)['stages'][5]['progress']
        self.assertEqual(migrated['stagePercent'], 29.97)
        reopened.progress(self.session, 'build', 'recovery-native-recipe-merge', 5000, 62880, 'files')
        self.assertGreater(self.progress()['stagePercent'], 29.97)
        self.assertLess(self.progress()['stagePercent'], 100)
        self.assertEqual(self.progress()['activeWork']['done'], 6)
        self.assertEqual(self.progress()['activeWork']['total'], 23)
        self.assertEqual((self.progress()['recoveryNativeIndex'], self.progress()['recoveryNativeTotal']), (2, 2))

    def test_reference_and_staging_counters_continue_after_all_raw_batches(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-batches', 16, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-section:references')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'recovery-asset-references', 50, 100, 'files')
        self.assertGreater(self.progress()['stagePercent'], before)
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        self.store.progress(self.session, 'build', 'staging-section:copy', 0, 1, 'steps')
        self.store.progress(self.session, 'build', 'staging-copy', 1, 100, 'files')
        before = self.progress()['stagePercent']
        self.store.progress(self.session, 'build', 'staging-copy', 80, 100, 'files')
        self.assertGreater(self.progress()['stagePercent'], before)
        self.assertEqual(self.progress()['activeWork']['done'], 22)
        self.assertEqual(self.progress()['activeWork']['total'], 23)
        self.assertLess(self.progress()['stagePercent'], 100)

    def test_native_reference_pass_and_container_reads_do_not_close_later_staging(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        for step in stage_plan.STAGING_STEPS[:6]:
            self.store.progress(self.session, 'build', 'staging-section:' + step, 1, 1, 'steps', status='reuse')
        self.store.progress(self.session, 'build', 'staging-section:native', 0, 1, 'steps', status='start')
        prior = self.progress()['stagePercent']
        for phase, done in [('recovery-asset-references', 50), ('recovery-asset-references', 100),
                            ('recovery-pointer-containers', 20), ('recovery-pointer-containers', 100)]:
            self.store.progress(self.session, 'build', phase, done, 100, 'files')
            progress = self.progress()
            self.assertGreater(progress['stagePercent'], prior)
            prior = progress['stagePercent']
            rows = progress['buildOverview']['recovery']['staging']
            self.assertEqual([item['id'] for item in rows if item['closed']], list(stage_plan.STAGING_STEPS[:6]))
            native = next(item for item in rows if item['id'] == 'native')
            self.assertEqual(native['status'], 'running')
            self.assertLess(native['percent'], 100)
            self.assertTrue(all(item['status'] == 'pending' for item in rows[7:]))
        self.store.progress(self.session, 'build', 'staging-section:native', status='failed')
        saved = self.store.load(self.session)
        saved['stages'][5]['status'] = 'failed'; self.store.save(saved)
        rows = self.progress()['buildOverview']['recovery']['staging']
        self.assertFalse(next(item for item in rows if item['id'] == 'native')['closed'])
        self.assertEqual(next(item for item in rows if item['id'] == 'native')['status'], 'failed')
        self.store.progress(self.session, 'build', 'staging-section:native', 1, 1, 'steps', status='complete')
        self.assertTrue(next(item for item in self.progress()['buildOverview']['recovery']['staging']
                             if item['id'] == 'native')['closed'])

    def test_final_reference_audit_remains_owned_by_its_later_phase(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        self.store.progress(self.session, 'build', 'staging-section:audit', 0, 1, 'steps', status='start')
        self.store.progress(self.session, 'build', 'recovery-asset-references', 50, 100, 'files')
        rows = self.progress()['buildOverview']['recovery']['staging']
        audit = next(item for item in rows if item['id'] == 'audit')
        self.assertEqual(audit['percent'], 50)
        self.assertFalse(audit['closed'])
        self.assertEqual(audit['status'], 'running')

    def test_old_native_precheck_frontier_retains_only_receipt_proven_phases(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        for step in stage_plan.STAGING_STEPS[:6]:
            self.store.progress(self.session, 'build', 'staging-section:' + step, 1, 1, 'steps', status='reuse')
        saved = self.store.load(self.session)
        row = saved['stages'][5]
        row['progressPlan']['workRevision'] = stage_plan.WORK_REVISION - 1
        row['progressPlan']['percent'] = 46.750311
        staging = row['progressPlan']['recovery']['sections']['staging']
        staging.update(completed=list(stage_plan.STAGING_STEPS[:11]), current='audit', live='native',
                       fractions={'audit': .99}, liveStatus='failed')
        row['progressPlan']['recovery']['work']['fractions']['staging'] = 11.99 / 14
        state.atomic_json(self.store.session_dir(self.session) / 'state.json', saved)
        reopened = state.Store(self.store.root)
        reopened.progress(self.session, 'build', 'staging-section:native', 0, 1, 'steps', status='start')
        progress = self.progress()
        self.assertGreaterEqual(progress['stagePercent'], 46.750311)
        rows = progress['buildOverview']['recovery']['staging']
        self.assertEqual([item['id'] for item in rows if item['closed']], list(stage_plan.STAGING_STEPS[:6]))
        self.assertEqual(next(item for item in rows if item['id'] == 'native')['status'], 'running')
        before = progress['stagePercent']
        reopened.progress(self.session, 'build', 'recovery-asset-references', 50, 100, 'files')
        self.assertGreater(self.progress()['stagePercent'], before)

    def test_staging_runtime_and_report_file_bytes_supplement_known_parent_work(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-plan', 0, 16, 'batches')
        self.store.progress(self.session, 'build', 'recovery-section:staging')
        for step, parent, child in [('runtime', 'staging-runtime-copy', 'staging-runtime-file'),
                                    ('report', 'staging-report-files', 'staging-report-file')]:
            self.store.progress(self.session, 'build', 'staging-section:' + step, 0, 1, 'steps')
            self.store.progress(self.session, 'build', parent, 0, 100, 'files')
            before = self.progress()['stagePercent']
            self.store.progress(self.session, 'build', child, 500, 1000, 'bytes')
            self.assertGreater(self.progress()['stagePercent'], before)
            self.store.progress(self.session, 'build', parent, 99, 100, 'files')
            self.store.progress(self.session, 'build', 'staging-section:' + step, 1, 1, 'steps')
        self.assertEqual(self.progress()['activeWork']['done'], 22)
        # The final staging section still awaits its recovery receipt boundary.
        self.assertLess(self.progress()['stagePercent'], 100)

    def test_unknown_schedule_never_counts_clock_time_as_conversion_progress(self):
        self.store.operation(self.session, 'build', 'recovery')
        self.store.progress(self.session, 'build', 'recovery-section:source')
        before = self.progress()['stagePercent']
        with mock.patch('state.time.time', return_value=9999999999):
            self.store.progress(self.session, 'build', 'recovery-native-index-input', detail='Retained index')
        self.assertEqual(self.progress()['stagePercent'], before)
        self.assertIsNone(self.progress()['activeWork']['done'])
        self.assertIsNone(self.progress()['activeWork']['total'])

    def test_previous_saved_plan_migrates_without_resetting_observed_total(self):
        saved = self.store.load(self.session)
        row = saved['stages'][5]
        row.update(progressKey='same', status='interrupted', progress=state.stage_progress('recovery-batches', 1, 4, 'batches'))
        row['progressPlan'] = {'version': 1, 'current': 'recovery', 'completed': list(stage_plan.PLANS['build'][:6]),
                               'fractions': {'recovery': .25}, 'percent': 29.8}
        state.atomic_json(self.store.session_dir(self.session) / 'state.json', saved)
        self.store.begin_stage(self.session, 'build', 'same')
        migrated = self.store.load(self.session)['stages'][5]
        self.assertEqual(migrated['progressPlan']['version'], 2)
        before = migrated['progress']['stagePercent']
        self.assertGreaterEqual(before, 29.8)
        self.store.progress(self.session, 'build', 'recovery-batch', 1, 4, 'batches')
        self.store.progress(self.session, 'build', 'recovery-original-collection-merge', 7, 10, 'collections')
        self.assertGreater(self.progress()['stagePercent'], before)

    def test_unsupported_nested_scope_and_incidental_counter_never_skip_work(self):
        self.store.operation(self.session, 'build', 'game-inputs')
        for phase in ('recovery-section:future', 'recovery-native-future', 'file-hash'):
            self.store.progress(self.session, 'build', phase, 1, 1, 'commands')
            self.assertEqual(self.progress()['stagePercent'], 0)
        self.recovery_batch()
        before = self.progress()['stagePercent']
        for phase in ('recovery-section:future', 'recovery-native-future', 'file-hash', 'tool:unity-version'):
            self.store.progress(self.session, 'build', phase, 1, 1, 'commands')
            self.assertEqual(self.progress()['stagePercent'], before)

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

    def test_replacing_default_release_uses_new_source_and_keeps_tool_setup(self):
        release = Path(self.temp.name) / 'Builder'
        release.mkdir()
        manifest = release / 'quest-builder-release.json'
        state.atomic_json(manifest, {'schema': 1, 'sourceCommit': 'a' * 40})
        calls = []
        def action(stage):
            def run(saved, supervisor):
                calls.append(stage)
                path = self.store.session_dir(self.session) / (stage + '.txt')
                path.write_text(manifest.read_text() if stage == 'source' else stage)
                return [path], {}
            return run
        engine = wizard.Engine(self.store, release, actions={name: action(name) for name in state.STAGES})
        self.assertEqual(engine.run(self.session)['status'], 'complete')
        self.assertEqual(calls, list(state.STAGES))
        state.atomic_json(manifest, {'schema': 1, 'sourceCommit': 'b' * 40})
        calls.clear()
        self.assertEqual(engine.run(self.session)['status'], 'complete')
        self.assertEqual(calls, list(state.STAGES[1:]))
        self.assertIn('b' * 40, (self.store.session_dir(self.session) / 'source.txt').read_text())


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
