"""Home-page diagnostics retain bounded, explicitly associated installation evidence."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support
from storage import write_json


class InstallationSupportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        write_json(self.root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})

    def installation(self, digit='a', removed=False, started=100, signing=True):
        session = digit * 32
        folder = self.root / ('logs/removed-runs' if removed else 'sessions') / session
        state = {'schema': 1, 'session': session, 'status': 'blocked', 'created': started - 10,
                 'updated': started + 20, 'choices': {'gameRoot': 'C:\\PrivateOriginal',
                                                     'profile': {'displayName': 'PrivateOwner'}},
                 'completed': {'build': {'details': {'apkSha256': digit * 64, 'inputKey': 'e' * 64}}},
                 'stages': [{'id': 'install', 'status': 'blocked', 'attempts': 9,
                             'startedAt': started, 'durationSeconds': 17.026}]}
        write_json(folder / 'state.json', state)
        name = 'wireless-install-20261014T181310Z-' + digit * 8 + '.log'
        adb = self.root / 'device' / name; adb.parent.mkdir(parents=True, exist_ok=True)
        adb.write_text('CURRENT ADB ' + digit + '\nPrivateOwner C:\\PrivateOriginal\nAuthorization: Bearer NEVER EXPORT\n')
        os.utime(adb, (started + 1, started + 1))
        log = folder / 'logs/install.log'; log.parent.mkdir(parents=True)
        text = 'Quest installation stopped: storage access failed\n'
        home = self.root / 'device/signing-recovery' / ('headset-' + digit)
        transaction = home / (digit * 32)
        if signing:
            transaction.mkdir(parents=True)
            archive = 'private-' + digit * 32 + '.tar'
            (transaction / archive).write_bytes(b'PRIVATE SAVE BYTES')
            (transaction / 'original.apk').write_bytes(b'ORIGINAL APK BYTES')
            write_json(home / 'active.json', {'schema': 1, 'package': 'dev.gloomhavenvr.quest',
                'deviceSerial': home.name, 'transaction': digit * 32, 'state': 'restoring',
                'apkSha256': digit * 64, 'inputKey': 'e' * 64, 'banks': ['files/quest-owned-game'],
                'roots': [{'name': 'private', 'archive': archive,
                           'inventory': {'PRIVATE SAVE NAME': {'type': 'file'}}}]})
            text += ('Original recovery data is retained at ' + str(transaction)
                     + '. Retry with the same APK and headset.\n')
        text += 'ADB log: ' + str(adb) + '\n'
        log.write_text(text); os.utime(log, (started + 2, started + 2))
        return folder, state, log, adb, home, transaction

    def export(self, session=None):
        result = support.export_support(self.root, session)
        with zipfile.ZipFile(result['path']) as archive:
            files = {name: archive.read(name).decode() for name in archive.namelist()}
        return json.loads(files['diagnostic.json']), files

    def test_home_export_has_exact_tail_named_adb_and_per_run_signing_frontier(self):
        folder, state, log, adb, _, _ = self.installation()
        orphan = adb.with_name('wireless-install-20261014T181311Z-bbbbbbbb.log')
        orphan.write_text('ORPHAN ADB MUST NOT EXPORT'); os.utime(orphan, (2000000000, 2000000000))
        text = log.read_text()
        log.write_text('ADB log: ' + str(orphan) + '\n' + 'x' * (support.MAX_FILE_BYTES + 10) + '\n' + text)
        before = {path: path.read_bytes() for path in (folder / 'state.json', log, adb)}
        meta, files = self.export()
        self.assertEqual((meta['scope'], meta['session']), ('workspace', None))
        for key in ('stages', 'events', 'status', 'signingRecovery'): self.assertNotIn(key, meta)
        row = meta['installations'][0]
        self.assertEqual((row['session'], row['removed'], row['attempts']), (state['session'], False, 9))
        self.assertTrue(row['currentAttemptLog'])
        self.assertEqual(row['signingRecovery']['state'], 'restoring')
        self.assertEqual(row['signingRecovery']['apkSha256'], 'a' * 64)
        self.assertIn('CURRENT ADB a', files[row['adbLog']])
        self.assertIn('storage access failed', files[row['installLog']])
        self.assertIn('ADB log:', files[row['installLog']])
        self.assertTrue(next(record['truncated'] for record in meta['files'] if record['name'] == row['installLog']))
        for secret in ('ORPHAN ADB', 'PrivateOwner', 'PrivateOriginal', 'NEVER EXPORT', 'PRIVATE SAVE', 'ORIGINAL APK BYTES'):
            self.assertNotIn(secret, '\n'.join(files.values()))
        for path, content in before.items(): self.assertEqual(path.read_bytes(), content)

    def test_live_and_retired_installations_use_their_own_build_receipts(self):
        self.installation('a', started=100)
        _, retired, _, _, _, _ = self.installation('b', removed=True, started=200)
        write_json(self.root / 'build/latest-build.json', {'details': {'apkSha256': 'f' * 64, 'inputKey': 'f' * 64}})
        meta, files = self.export()
        self.assertEqual([row['session'] for row in meta['installations']], ['b' * 32, 'a' * 32])
        for row in meta['installations']:
            self.assertEqual(row['signingRecovery']['apkSha256'], row['session'] * 2)
            self.assertIn(row['installLog'], files); self.assertIn(row['adbLog'], files)
        self.assertTrue(meta['installations'][0]['removed'])
        self.assertEqual(retired['session'], meta['installations'][0]['session'])
        selected, selected_files = self.export(retired['session'])
        self.assertEqual(selected['scope'], 'removed-session')
        self.assertEqual(selected['signingRecovery']['apkSha256'], 'b' * 64)
        self.assertIn('wizard/install.log', selected_files)

    def test_installation_selection_is_independent_of_newer_non_install_runs(self):
        _, state, _, _, _, _ = self.installation()
        for digit in 'bcdef':
            write_json(self.root / 'sessions' / (digit * 32) / 'state.json', {
                'schema': 1, 'session': digit * 32, 'status': 'running', 'updated': 2000,
                'stages': [{'id': 'install', 'status': 'pending'}]})
        meta, files = self.export()
        self.assertEqual(meta['installations'][0]['session'], state['session'])
        self.assertIn(meta['installations'][0]['adbLog'], files)

    def test_recent_installations_are_bounded_and_prioritized_over_newer_unity_logs(self):
        for index, digit in enumerate('abcd'): self.installation(digit, started=100 + index * 10, signing=False)
        logs = self.root / 'build/logs'; logs.mkdir(parents=True)
        for index in range(support.MAX_FILES + 5):
            path = logs / ('unity-build-old-' + str(index) + '.log')
            path.write_text('OLD UNITY'); os.utime(path, (2000000000, 2000000000))
        with patch.object(support, 'MAX_FILES', 6): meta, files = self.export()
        self.assertEqual(len(meta['installations']), support.MAX_INSTALLATION_RUNS)
        self.assertEqual([row['session'] for row in meta['installations']], [digit * 32 for digit in 'dcb'])
        for row in meta['installations']:
            self.assertIn(row['installLog'], files); self.assertIn(row['adbLog'], files)
        self.assertEqual(len(files), 7)
        self.assertLessEqual(sum(row['exportedBytes'] for row in meta['files']), support.MAX_TOTAL_BYTES)
        self.assertTrue(any(row['reason'] == 'recent installation run limit' for row in meta['omitted']))

    def test_incomplete_install_stage_keeps_log_without_claiming_current_adb(self):
        for malformed in (None, {}, 'incomplete', [None, 'partial', {'id': 'install', 'startedAt': 'not-a-time'}]):
            with self.subTest(stages=malformed):
                if not (self.root / 'sessions').exists(): folder, state, _, _, _, _ = self.installation(signing=False)
                state['stages'] = malformed; state['events'] = [None, {'parameters': None}]
                write_json(folder / 'state.json', state)
                for session in (None, state['session']):
                    meta, files = self.export(session)
                    self.assertNotIn('device/wireless-install-20261014T181310Z-aaaaaaaa.log', files)
                    self.assertIn('runs/' + state['session'] + '/install.log' if session is None else 'wizard/install.log', files)
                    if session is None:
                        self.assertFalse(meta['installations'][0]['currentAttemptLog'])
                        self.assertNotIn('signingRecovery', meta['installations'][0])

    def test_missing_run_build_does_not_borrow_global_identity(self):
        folder, state, _, _, _, _ = self.installation()
        state.pop('completed'); write_json(folder / 'state.json', state)
        write_json(self.root / 'build/latest-build.json', {'details': {'apkSha256': 'a' * 64, 'inputKey': 'e' * 64}})
        meta, files = self.export()
        row = meta['installations'][0]
        self.assertEqual(row['signingRecoveryUnavailable']['reason'], 'run-build-receipt-missing')
        self.assertIn(row['adbLog'], files)
        selected, _ = self.export(state['session'])
        self.assertEqual(selected['signingRecovery']['apkSha256'], 'a' * 64)

    def test_mismatched_journal_is_associated_only_as_unavailable_for_its_run(self):
        _, _, _, _, home, _ = self.installation()
        value = json.loads((home / 'active.json').read_text()); value['apkSha256'] = 'f' * 64
        write_json(home / 'active.json', value)
        meta, files = self.export()
        row = meta['installations'][0]
        self.assertNotIn('signingRecovery', row)
        self.assertEqual(row['signingRecoveryUnavailable']['reason'], 'build-identity-mismatch')
        self.assertIn(row['adbLog'], files)

    def test_unavailable_optional_journal_keeps_its_installation_failure(self):
        _, _, _, _, home, _ = self.installation()
        journal = home / 'active.json'; original = journal.read_bytes()
        foreign = self.root / 'private-journal'; foreign.write_bytes(original)
        for case, reason in (('invalid', 'journal-invalid'), ('deeply-nested', 'journal-invalid'), ('missing', 'journal-missing'),
                             ('linked', 'journal-path-unsafe'), ('oversized', 'journal-oversized')):
            with self.subTest(case=case):
                if journal.exists() or journal.is_symlink(): journal.unlink()
                if case == 'invalid': journal.write_text('{unfinished')
                elif case == 'deeply-nested': journal.write_text('{"deep":' * 1500 + '0' + '}' * 1500)
                elif case == 'linked': journal.symlink_to(foreign)
                elif case == 'oversized':
                    with journal.open('wb') as stream: stream.truncate(32 * 1048576 + 1)
                meta, files = self.export()
                row = meta['installations'][0]
                # New Python JSON parsers can accept this nesting without a
                # recursion exception; the object still lacks journal identity.
                reasons = (reason, 'journal-identity-mismatch') if case == 'deeply-nested' else (reason,)
                self.assertIn(row['signingRecoveryUnavailable']['reason'], reasons)
                self.assertIn(row['installLog'], files); self.assertIn(row['adbLog'], files)

    def test_linked_run_install_log_is_omitted_without_following_it(self):
        _, _, log, _, _, _ = self.installation()
        foreign = self.root / 'foreign-install.log'; foreign.write_text('FOREIGN INSTALL MUST NOT EXPORT')
        log.unlink(); log.symlink_to(foreign)
        meta, files = self.export()
        self.assertNotIn('installations', meta)
        self.assertNotIn('FOREIGN INSTALL', '\n'.join(files.values()))
        self.assertTrue(any(row['name'].endswith('/install.log') and row['reason'] == 'unsafe or unavailable log'
                            for row in meta['omitted']))

    def test_same_explicit_adb_log_is_archived_once_with_both_run_associations(self):
        _, _, _, adb, _, _ = self.installation('a', signing=False)
        _, _, log, other, _, _ = self.installation('b', signing=False)
        log.write_text('ADB log: ' + str(adb) + '\n')
        meta, files = self.export()
        name = 'device/' + adb.name
        self.assertEqual([row['adbLog'] for row in meta['installations']], [name, name])
        self.assertEqual(sum(row['name'] == name for row in meta['files']), 1)
        self.assertIn(name, files); self.assertNotIn('device/' + other.name, files)

    def test_stale_foreign_or_linked_adb_is_not_admitted_by_home_export(self):
        _, _, log, adb, _, _ = self.installation(signing=False)
        foreign = self.root / adb.name; foreign.write_text('FOREIGN PRIVATE ADB')
        for case in ('stale', 'foreign', 'linked', 'latest-marker-invalid'):
            with self.subTest(case=case):
                if adb.is_symlink(): adb.unlink(); adb.write_text('OWNED ADB')
                os.utime(adb, (99 if case == 'stale' else 101, 99 if case == 'stale' else 101))
                if case == 'linked': adb.unlink(); adb.symlink_to(foreign)
                reference = foreign if case == 'foreign' else adb
                log.write_text('ADB log: ' + str(reference) + '\n')
                if case == 'latest-marker-invalid':
                    with log.open('a') as stream: stream.write('ADB log: ' + str(self.root / 'device/orphan.log') + '\n')
                meta, files = self.export()
                self.assertNotIn('adbLog', meta['installations'][0])
                self.assertNotIn('device/' + adb.name, files)
                self.assertNotIn('FOREIGN PRIVATE ADB', '\n'.join(files.values()))

    def test_no_game_save_backup_or_original_payload_is_opened_or_enumerated(self):
        _, _, _, _, _, transaction = self.installation()
        game = self.root / 'private-original'; game.mkdir(); (game / 'save').write_text('PRIVATE SAVE')
        original_open, original_iterdir = Path.open, Path.iterdir
        def opened(path, *args, **kwargs):
            if path == game or game in path.parents or transaction in path.parents:
                raise AssertionError('Payload read: ' + str(path))
            return original_open(path, *args, **kwargs)
        def enumerated(path):
            if path == game or game in path.parents or path == transaction or transaction in path.parents:
                raise AssertionError('Payload enumeration: ' + str(path))
            return original_iterdir(path)
        with patch.object(Path, 'open', opened), patch.object(Path, 'iterdir', enumerated):
            meta, files = self.export()
        self.assertEqual(meta['installations'][0]['signingRecovery']['roots'][0]['files'], 1)
        self.assertIn(meta['installations'][0]['adbLog'], files)


if __name__ == '__main__': unittest.main()
