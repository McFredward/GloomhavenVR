"""Restart selection comes from the retained owner store, not browser storage."""
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

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


class DefaultApkDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / 'Owner workspace with spaces')
        self.build = self.store.root / 'build'

    def completed(self, key='a' * 64, *, update=False, publish=True):
        name = ('updates' if update else 'builds') + '/' + key + '/GloomhavenVR-Quest.apk'
        apk = self.build / name
        apk.parent.mkdir(parents=True, exist_ok=True)
        apk.write_bytes(b'Discovery does not inspect APK bytes; update preflight owns that qualification.')
        details = {'package': 'dev.gloomhavenvr.quest', 'inputKey': 'b' * 64,
                   'apkSha256': 'c' * 64, 'isDiagnostic': False, 'modBuild': 671}
        receipt_name = 'receipts/build/' + key + '.json'
        receipt = {'schema': 1, 'stage': 'build', 'key': key, 'details': details,
                   'outputs': [{'path': name, 'sha256': details['apkSha256'], 'size': apk.stat().st_size}]}
        state.atomic_json(self.build / receipt_name, receipt)
        if publish:
            state.atomic_json(self.build / 'latest-build.json',
                              {'schema': 1, 'apk': name, 'receipt': receipt_name, 'details': details})
        if update:
            state.atomic_json(self.build / 'latest-update-input.json',
                              {'schema': 1, 'plan': 'updates/' + key + '/plan.json'})
        return apk, receipt_name, receipt

    def test_latest_completed_build_is_default_without_payload_reads_or_directory_scan(self):
        apk, _, _ = self.completed()
        opened = []
        original = Path.open
        def metadata_only(path, *args, **kwargs):
            self.assertNotEqual(path.suffix, '.apk', 'discovery must not open large APK payloads')
            opened.append(path)
            return original(path, *args, **kwargs)
        with patch.object(Path, 'open', metadata_only), patch.object(Path, 'glob', side_effect=AssertionError('No scan')):
            self.assertEqual(discovery.default_apk(self.store),
                             {'path': str(apk), 'modBuild': 671, 'source': 'latest-build'})
        self.assertEqual(len(opened), 2)

    def test_discovery_response_exposes_the_same_default_for_home_update_actions(self):
        apk, _, _ = self.completed()
        def no_owned_game(path): raise ValueError('This fixture has no PC installation.')
        helper = SimpleNamespace(discover_steam_root=lambda: None, game_data=no_owned_game)
        with patch.object(discovery, 'builder', return_value=helper), patch.object(discovery, 'unity_paths', return_value=([], [])):
            value = discovery.discover(ROOT, self.store)
        self.assertEqual(value['defaultApk']['path'], str(apk))
        self.assertEqual(value['event'], 'discovery')

    def test_multi_gigabyte_apk_selection_uses_only_its_recorded_size(self):
        apk, receipt_name, receipt = self.completed()
        with apk.open('r+b') as stream: stream.truncate(3 * 1024 ** 3)
        receipt['outputs'][0]['size'] = apk.stat().st_size
        state.atomic_json(self.build / receipt_name, receipt)
        original = Path.open
        def metadata_only(path, *args, **kwargs):
            self.assertNotEqual(path, apk, 'multi-gigabyte payloads must stay unopened')
            return original(path, *args, **kwargs)
        with patch.object(Path, 'open', metadata_only):
            self.assertEqual(discovery.default_apk(self.store)['path'], str(apk))

    def test_last_successful_build_wins_over_an_incomplete_new_update(self):
        apk, _, _ = self.completed()
        state.atomic_json(self.build / 'latest-update-input.json',
                          {'schema': 1, 'plan': 'updates/' + 'd' * 64 + '/plan.json'})
        self.assertEqual(discovery.default_apk(self.store)['path'], str(apk))

    def test_stale_or_missing_build_pointer_falls_back_to_completed_update_without_reading_plan(self):
        apk, _, _ = self.completed('d' * 64, update=True, publish=False)
        variants = [None, '{broken', '{"schema":1,"apk":"builds/missing.apk"}']
        for raw in variants:
            with self.subTest(pointer=raw):
                pointer = self.build / 'latest-build.json'
                if raw is None: pointer.unlink(missing_ok=True)
                else: pointer.write_text(raw)
                self.assertEqual(discovery.default_apk(self.store),
                                 {'path': str(apk), 'modBuild': 671, 'source': 'completed-update'})
        self.assertFalse((apk.parent / 'plan.json').exists())

    def test_failed_update_cannot_be_suggested_even_if_an_apk_file_exists(self):
        apk, receipt_name, _ = self.completed('d' * 64, update=True, publish=False)
        (self.build / receipt_name).unlink()
        self.assertTrue(apk.is_file())
        self.assertIsNone(discovery.default_apk(self.store))

    def test_removed_or_truncated_apk_has_no_default(self):
        apk, _, _ = self.completed()
        apk.write_bytes(b'truncated')
        self.assertIsNone(discovery.default_apk(self.store))
        apk.unlink()
        self.assertIsNone(discovery.default_apk(self.store))

    def test_receipt_identity_or_completion_mismatch_has_no_default(self):
        _, receipt_name, receipt = self.completed()
        for field, value in (('stage', 'inspect'), ('key', 'e' * 64), ('schema', 2), ('outputs', []),
                             ('outputs', receipt['outputs'] * 2), ('details', {**receipt['details'], 'isDiagnostic': True}),
                             ('details', {**receipt['details'], 'package': 'other.package'}),
                             ('details', {**receipt['details'], 'apkSha256': 'e' * 64})):
            with self.subTest(field=field, value=value):
                state.atomic_json(self.build / receipt_name, {**receipt, field: value})
                self.assertIsNone(discovery.default_apk(self.store))

    def test_pointer_cannot_escape_owned_build_tree_or_switch_to_a_different_receipt(self):
        _, receipt_name, receipt = self.completed()
        pointer = {'schema': 1, 'apk': receipt['outputs'][0]['path'], 'receipt': receipt_name,
                   'details': receipt['details']}
        for field, value in (('apk', '../Outside.apk'), ('apk', str(self.root / 'Outside.apk')),
                             ('apk', r'C:\Outside.apk'), ('apk', ('builds/' + 'a' * 64 + '/Outside.apk').replace('/', chr(92))),
                             ('apk', 'builds/' + 'a' * 64 + '/drive:Outside.apk'),
                             ('receipt', '../outside.json'), ('receipt', 'receipts/build/' + 'e' * 64 + '.json')):
            with self.subTest(field=field, value=value):
                state.atomic_json(self.build / 'latest-build.json', {**pointer, field: value})
                self.assertIsNone(discovery.default_apk(self.store))

    def test_linked_apk_or_metadata_is_not_suggested(self):
        for link_target in ('apk', 'receipt', 'pointer', 'directory'):
            with self.subTest(link_target=link_target):
                key = format(len(link_target), 'x') * 64
                apk, receipt_name, _ = self.completed(key)
                target = {'apk': apk, 'receipt': self.build / receipt_name,
                          'pointer': self.build / 'latest-build.json', 'directory': apk.parent}[link_target]
                moved = target.with_name(target.name + '.original')
                target.rename(moved)
                target.symlink_to(moved, target_is_directory=link_target == 'directory')
                try: self.assertIsNone(discovery.default_apk(self.store))
                finally: target.unlink(); moved.rename(target)

    def test_nonregular_or_oversized_metadata_is_rejected_before_opening(self):
        _, _, _ = self.completed()
        pointer = self.build / 'latest-build.json'
        pointer.write_text('x' * 65537)
        self.assertIsNone(discovery.default_apk(self.store))
        pointer.unlink()
        pointer.mkdir()
        self.assertIsNone(discovery.default_apk(self.store))


if __name__ == '__main__': unittest.main()
