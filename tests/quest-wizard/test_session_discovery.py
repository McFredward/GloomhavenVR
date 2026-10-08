"""Restart selection comes from the retained owner store, not browser storage."""
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import state
import wizard


class RestartDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / 'owned')

    def create(self):
        return self.store.create(wizard.choices({'gameRoot': str(self.root / 'Game')}))

    def test_failed_session_survives_new_store_and_has_no_startup_mutation(self):
        saved = self.create()
        saved['status'] = 'failed'
        saved['stages'][5]['status'] = 'failed'
        saved['stages'][0]['status'] = 'complete'
        self.store.save(saved)
        path = self.store.session_dir(saved['session']) / 'state.json'
        before = path.read_bytes()
        recent, latest = discovery.recent_sessions(state.Store(self.store.root))
        self.assertEqual(latest, saved['session'])
        self.assertEqual(recent[0]['status'], 'failed')
        self.assertEqual(path.read_bytes(), before)
        self.assertFalse((self.store.root / 'run.lock').exists())

    def test_saved_running_status_is_not_reconciled_by_discovery(self):
        saved = self.create()
        saved['status'] = 'running'
        self.store.save(saved)
        recent, latest = discovery.recent_sessions(self.store)
        self.assertEqual(latest, saved['session'])
        self.assertEqual(recent[0]['status'], 'running')
        self.assertEqual(state.read_json(self.store.session_dir(latest) / 'state.json')['status'], 'running')

    def test_stale_missing_or_corrupt_pointer_uses_readable_recent_session(self):
        first, second = self.create(), self.create()
        for content in ('{"schema":1,"session":"' + 'a' * 32 + '"}', '{broken', None):
            with self.subTest(pointer=content):
                pointer = self.store.root / 'latest-session.json'
                if content is None: pointer.unlink(missing_ok=True)
                else: pointer.write_text(content)
                recent, latest = discovery.recent_sessions(self.store)
                self.assertEqual(latest, second['session'])
                self.assertEqual({row['session'] for row in recent}, {first['session'], second['session']})

    def test_wrong_directory_corrupt_status_or_unusable_choices_are_not_selected(self):
        valid, invalid = self.create(), self.create()
        path = self.store.session_dir(invalid['session']) / 'state.json'
        variants = [dict(invalid, session=valid['session']), dict(invalid, choices={}),
                    dict(invalid, updated='invalid'), dict(invalid, updated=float('nan')),
                    dict(invalid, status='unknown')]
        for value in variants:
            with self.subTest(value=value.get('status')):
                state.atomic_json(path, value)
                recent, latest = discovery.recent_sessions(self.store)
                self.assertEqual(latest, valid['session'])
                self.assertEqual(len(recent), 1)
        path.write_text('invalid JSON')
        self.assertEqual(discovery.recent_sessions(self.store)[1], valid['session'])

    def test_explicitly_continued_older_session_becomes_next_restart_selection(self):
        old, newer = self.create(), self.create()
        calls = []
        def complete(stage):
            def action(saved, supervisor):
                calls.append(stage)
                path = self.store.session_dir(saved['session']) / (stage + '.txt')
                path.write_text(stage)
                return [path], {}
            return action
        engine = wizard.Engine(self.store, actions={name: complete(name) for name in state.STAGES})
        self.assertEqual(engine.run(old['session'])['status'], 'complete')
        self.assertEqual(discovery.recent_sessions(state.Store(self.store.root))[1], old['session'])
        self.assertEqual(calls, list(state.STAGES))
        self.assertEqual(self.store.load(newer['session'])['status'], 'ready')

    def test_pointer_remains_available_even_outside_recent_eight(self):
        saved = [self.create() for _ in range(10)]
        state.atomic_json(self.store.root / 'latest-session.json', {'schema': 1, 'session': saved[0]['session']})
        recent, latest = discovery.recent_sessions(self.store)
        self.assertEqual(len(recent), 8)
        self.assertEqual(latest, saved[0]['session'])
        self.assertIn(latest, [row['session'] for row in recent])


if __name__ == '__main__': unittest.main()
