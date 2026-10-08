"""Early disk planning is bounded, cache-aware and does not read game assets."""
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import qualification
import state
import provision
import wizard


class SpaceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.workspace = self.root / 'w'; self.workspace.mkdir()
        self.game = self.root / 'OwnedGame'; self.game.mkdir()
        for name in ('one.bin', 'two.bin'):
            with (self.game / name).open('wb') as stream: stream.truncate(1024 * 1024)
        self.helper = SimpleNamespace(game_data=lambda _: self.game,
            import_workspace=SimpleNamespace(workspace_key=lambda *_: 'b' * 64))
        self.loader = mock.patch.object(discovery, 'builder', return_value=self.helper); self.loader.start()
    def tearDown(self): self.loader.stop(); self.temp.cleanup()
    def previous(self, source=None):
        state.atomic_json(self.workspace / 'build/latest-input.json', {'schema': 1, 'sourceGameRoot': str(source or self.game), 'manifest': 'manifests/' + 'c' * 64 + '.json'})
        state.atomic_json(self.workspace / 'build/manifests' / ('c' * 64 + '.json'), {'schema': 1, 'game': {'key': 'a' * 64,
            'unityVersion': '2021.3.5f1', 'files': [{'size': 1048576}, {'size': 1048576}]}})
    def file(self, name, size):
        path = self.workspace / 'build' / name; path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('wb') as stream: stream.truncate(size)
    def test_fresh_estimate_is_a_hard_gate_before_downloads(self):
        result = qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=32 * qualification.GIB, game_root=self.game)
        self.assertFalse(result['spaceWarning']); self.assertFalse(result['spaceEstimate']['certifiedExact'])
        self.assertTrue(result['buildSpaceCheckPassed']); self.assertFalse(result['fullBuildSpaceVerified'])
        self.assertEqual(result['spaceEstimate']['originalFiles'], 2)
        self.assertEqual(result['spaceEstimate']['freshEstimatedBytes'], 8 * 2097152 + 20 * qualification.GIB)
        with self.assertRaises(state.WizardError) as error:
            qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=8 * qualification.GIB, game_root=self.game)
        self.assertEqual(error.exception.code, 'workspace_build_space_low')
        self.assertEqual(error.exception.parameters['requiredBytes'], result['spaceEstimate']['additionalEstimatedBytes'])
        self.assertEqual(error.exception.parameters['workspaceRoot'], str(self.workspace))
        self.assertIn('8.0 GiB', error.exception.message['de'])
        persisted = json.loads((self.workspace / 'qualification.json').read_text())
        self.assertTrue(persisted['spaceWarning']); self.assertFalse(persisted['buildSpaceCheckPassed'])

    def test_reported_44_gib_cache_candidate_cannot_waive_124_gib_reserve(self):
        estimate = {'originalBytes': 17754574362, 'originalFiles': 5227, 'scanBounded': False,
                    'additionalEstimatedBytes': 133094165922, 'cacheReuseCandidate': True,
                    'cacheCreditBytes': 30417265454, 'cacheScanBounded': True, 'certifiedExact': False}
        with mock.patch.object(qualification, 'space_estimate', return_value=estimate):
            with self.assertRaises(state.WizardError) as error:
                qualification.qualify(self.workspace, system='Windows', machine='AMD64',
                                      free_bytes=47421988864, game_root=self.game)
        self.assertEqual(error.exception.code, 'workspace_build_space_low')
        self.assertIn('44.2 GiB', error.exception.message['de'])
        self.assertIn('124.0 GiB', error.exception.message['de'])
        self.assertEqual(error.exception.parameters['spaceEstimate'], estimate)

    def test_setup_low_space_can_open_ui_without_authorizing_work(self):
        result = qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=1024, enforce=False)
        self.assertTrue(result['spaceWarning']); self.assertFalse(result['buildSpaceCheckPassed'])
        with self.assertRaises(state.WizardError) as error:
            qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=1024)
        self.assertEqual(error.exception.code, 'workspace_space_low')

    def test_partial_game_scan_never_authorizes_a_low_estimate(self):
        with mock.patch.object(qualification, 'MAX_SCAN_FILES', 1):
            with self.assertRaises(state.WizardError) as error:
                qualification.qualify(self.workspace, free_bytes=100 * qualification.GIB, game_root=self.game)
        self.assertEqual(error.exception.code, 'workspace_space_estimate_incomplete')
        self.assertTrue(error.exception.parameters['spaceEstimate']['scanBounded'])

    def test_update_modes_use_their_own_required_reserves(self):
        for mode, required in [('update-profile', 4 * qualification.GIB), ('update-mod', 40 * qualification.GIB)]:
            with self.subTest(mode=mode):
                result = qualification.qualify(self.workspace, game_root=self.game, mode=mode, free_bytes=required)
                self.assertTrue(result['buildSpaceCheckPassed'])
                with self.assertRaises(state.WizardError):
                    qualification.qualify(self.workspace, game_root=self.game, mode=mode, free_bytes=required - 1)

    def test_runtime_guard_reads_only_free_space_and_retains_minimum(self):
        with mock.patch.object(qualification, 'tree_bytes', side_effect=AssertionError('No resumed cache walk')):
            self.assertEqual(qualification.check_runtime_space(self.workspace, free_bytes=qualification.MIN_RUNTIME_FREE_BYTES), qualification.MIN_RUNTIME_FREE_BYTES)
            with self.assertRaises(state.WizardError) as error:
                qualification.check_runtime_space(self.workspace, free_bytes=qualification.MIN_RUNTIME_FREE_BYTES - 1)
        self.assertEqual(error.exception.code, 'workspace_runtime_space_low')
        self.assertTrue(error.exception.parameters['completedWorkRetained'])
    def test_only_matching_retained_game_project_and_recovery_are_credited(self):
        self.previous()
        self.file('inputs/game/' + 'a' * 64 + '/asset', 1048576)
        self.file('inputs/game/' + 'd' * 64 + '/unrelated', 19000000)
        self.file('projects/' + 'b' * 64 + '/Library/asset', 2097152)
        self.file('cache/recovery/' + 'e' * 64 + '/project/asset', 1048576)
        state.atomic_json(self.workspace / 'build/cache/recovery' / ('e' * 64) / 'stage-owner.json',
            {'schema': 1, 'gameKey': 'a' * 64, 'key': 'e' * 64})
        estimate = qualification.space_estimate(self.workspace, self.game, ROOT)
        self.assertTrue(estimate['cacheReuseCandidate'])
        self.assertLess(estimate['cacheCreditBytes'], 5 * 1048576)
        self.assertGreaterEqual(estimate['cacheCreditBytes'], 4 * 1048576)
        self.previous(source=self.root / 'DifferentGame')
        self.assertEqual(qualification.space_estimate(self.workspace, self.game, ROOT)['cacheCreditBytes'], 0)

    def test_windows_order_cache_credit_requires_exact_canonical_snapshot_receipt(self):
        self.previous()
        rows = [{'path': 'one.bin', 'size': 1048576, 'sha256': 'a' * 64},
                {'path': 'Two.bin', 'size': 1048576, 'sha256': 'b' * 64}]
        game = {'key': state.value_hash({'files': rows}), 'unityVersion': '2021.3.5f1', 'files': rows}
        ordered = sorted(rows, key=lambda row: row['path']); correct = state.value_hash({'files': ordered})
        path = self.workspace / 'build/manifests' / ('c' * 64 + '.json')
        state.atomic_json(path, {'schema': 1, 'game': game})
        self.file('inputs/game/' + correct + '/asset', 1048576)
        estimate = qualification.space_estimate(self.workspace, self.game, ROOT)
        self.assertEqual(estimate['cacheGameKey'], game['key'])
        self.assertEqual(estimate['cacheCreditBytes'], 0)
        owner = self.workspace / 'build/inputs/game' / correct / '.snapshot.json'
        state.atomic_json(owner, {'schema': 1, 'files': ordered})
        estimate = qualification.space_estimate(self.workspace, self.game, ROOT)
        self.assertEqual(estimate['cacheGameKey'], correct)
        self.assertGreaterEqual(estimate['cacheCreditBytes'], 1048576)
        state.atomic_json(owner, {'schema': 1, 'files': rows})
        self.assertEqual(qualification.space_estimate(self.workspace, self.game, ROOT)['cacheGameKey'], game['key'])

    def test_cache_roots_share_one_scan_deadline(self):
        self.previous(); self.file('inputs/game/' + 'a' * 64 + '/asset', 1048576)
        deadlines = []; original = qualification.tree_bytes
        def bounded(root, **keywords):
            if 'deadline' in keywords: deadlines.append(keywords['deadline'])
            return original(root, **keywords)
        with mock.patch.object(qualification, 'tree_bytes', side_effect=bounded):
            qualification.space_estimate(self.workspace, self.game, ROOT)
        self.assertGreaterEqual(len(deadlines), 2)
        self.assertEqual(len(set(deadlines)), 1)
    def test_file_bounds_are_reported_and_asset_contents_are_never_opened(self):
        original_open = Path.open
        def guarded(path, *args, **kwargs):
            if path.parent == self.game: raise AssertionError('Game asset contents must not be opened')
            return original_open(path, *args, **kwargs)
        with mock.patch.object(Path, 'open', guarded), mock.patch.object(qualification, 'MAX_SCAN_FILES', 1):
            estimate = qualification.space_estimate(self.workspace, self.game, ROOT)
        self.assertTrue(estimate['scanBounded']); self.assertFalse(estimate['cacheReuseCandidate'])
        self.assertEqual(estimate['originalFiles'], 1)
    def test_pointer_escape_is_not_used_for_cache_credit(self):
        state.atomic_json(self.workspace / 'build/latest-input.json', {'schema': 1, 'sourceGameRoot': str(self.game), 'manifest': '../private.json'})
        self.assertEqual(qualification.space_estimate(self.workspace, self.game, ROOT)['cacheCreditBytes'], 0)

    def run_fixture(self, store, saved, calls, free, *, after=None, cancel_build=False):
        engine = wizard.Engine(store)
        def action(stage):
            def run(*_):
                calls.append(stage)
                if cancel_build and stage == 'build': raise state.Cancelled()
                output = store.session_dir(saved['session']) / (stage + '.output')
                output.write_text(stage)
                if after: after(stage)
                return [output], {}
            return run
        for stage in state.STAGES[1:]: setattr(engine, 'stage_' + stage, action(stage))
        with mock.patch.object(qualification.shutil, 'disk_usage', side_effect=lambda _: SimpleNamespace(free=free[0])), \
             mock.patch.object(provision, 'tools', side_effect=action('tools')):
            return engine.run(saved['session'])

    def test_insufficient_build_space_stops_before_tools_are_downloaded(self):
        store = state.Store(self.workspace)
        saved = store.create(wizard.choices({'gameRoot': str(self.game)})); calls = []
        result = self.run_fixture(store, saved, calls, [8 * qualification.GIB])
        self.assertEqual(result['status'], 'blocked'); self.assertEqual(calls, [])
        self.assertEqual(result['needsActions'][0]['code'], 'workspace_build_space_low')
        self.assertNotIn('tools', result.get('completed', {}))

    def test_reused_tools_do_not_bypass_new_capacity_check_or_remove_receipts(self):
        store = state.Store(self.workspace)
        saved = store.create(wizard.choices({'gameRoot': str(self.game)})); calls = []
        first = self.run_fixture(store, saved, calls, [32 * qualification.GIB], cancel_build=True)
        self.assertEqual(first['status'], 'cancelled')
        receipt = store.receipt(saved['session'], 'inspect').read_bytes()
        count = len(calls)
        second = self.run_fixture(store, saved, calls, [8 * qualification.GIB])
        self.assertEqual(second['status'], 'blocked'); self.assertEqual(len(calls), count)
        self.assertEqual(second['needsActions'][0]['stage'], 'tools')
        self.assertEqual(store.receipt(saved['session'], 'inspect').read_bytes(), receipt)
        self.assertIn('inspect', second['completed'])
        for stage in ('source', 'unity', 'profile', 'inspect'):
            retained = next(row for row in second['stages'] if row['id'] == stage)
            self.assertEqual(retained['progress']['stagePercent'], 100)

    def test_space_lost_after_setup_blocks_before_snapshots_and_before_build(self):
        for previous, blocked in [('profile', 'inspect'), ('inspect', 'build')]:
            with self.subTest(blocked=blocked):
                store = state.Store(self.workspace / blocked)
                saved = store.create(wizard.choices({'gameRoot': str(self.game)})); calls = []; free = [32 * qualification.GIB]
                result = self.run_fixture(store, saved, calls, free,
                                          after=lambda stage: free.__setitem__(0, 8 * qualification.GIB) if stage == previous else None)
                self.assertEqual(result['status'], 'blocked')
                self.assertEqual(result['needsActions'][0]['stage'], blocked)
                self.assertNotIn(blocked, calls)
                self.assertIn(previous, result['completed'])

if __name__ == '__main__': unittest.main()
