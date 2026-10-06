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
    def test_fresh_estimate_and_provable_snapshot_floor_precede_downloads(self):
        result = qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=8 * qualification.GIB, game_root=self.game)
        self.assertTrue(result['spaceWarning']); self.assertFalse(result['spaceEstimate']['certifiedExact'])
        self.assertEqual(result['spaceEstimate']['originalFiles'], 2)
        self.assertEqual(result['spaceEstimate']['freshEstimatedBytes'], 8 * 2097152 + 20 * qualification.GIB)
        with self.assertRaises(state.WizardError) as error:
            qualification.qualify(self.workspace, system='Windows', machine='AMD64', free_bytes=4 * qualification.GIB + 1024, game_root=self.game)
        self.assertEqual(error.exception.code, 'workspace_game_space_low')
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

if __name__ == '__main__': unittest.main()
