"""Workspace support remains useful after retirement and outside any build run."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support
from storage import BuildError, write_json


class WorkspaceSupportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        write_json(self.root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})

    def run_state(self, session='a' * 32, removed=False, status='cancelled'):
        folder = self.root / ('logs/removed-runs' if removed else 'sessions') / session
        value = {'schema': 1, 'session': session, 'status': status, 'created': 10, 'updated': 20,
                 'choices': {'mode': 'update-mod', 'gameRoot': 'D:\\PrivateGame', 'profile': {'displayName': 'PrivateOwner'}},
                 'stages': [{'id': 'build', 'status': 'failed', 'attempts': 2,
                             'progress': {'done': 4, 'total': 8, 'percent': 50},
                             'details': {'inputKey': 'b' * 64, 'gameKey': 'c' * 64}}],
                 'completed': {'inspect': {'details': {'inputKey': 'b' * 64, 'gameKey': 'c' * 64}}}}
        write_json(folder / 'state.json', value)
        write_json(folder / 'profile.json', {'displayName': 'PrivateOwner', 'accountId': 'PRIVATE-ACCOUNT'})
        return folder, value

    def export(self, session=None):
        result = support.export_support(self.root, session)
        with zipfile.ZipFile(result['path']) as archive:
            rows = {name: archive.read(name).decode() for name in archive.namelist()}
        return json.loads(rows['diagnostic.json']), rows

    def log(self, name, text):
        path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)
        return path

    def test_owned_empty_workspace_has_global_scope_without_synthetic_current_run(self):
        meta, rows = self.export()
        self.assertEqual(meta['scope'], 'workspace'); self.assertIsNone(meta['session'])
        for field in ('status', 'stages', 'events', 'created', 'updated', 'needsActions'):
            self.assertNotIn(field, meta)
        self.assertEqual(meta['workspace']['runs'], []); self.assertEqual(set(rows), {'diagnostic.json'})
        self.assertFalse((self.root / 'sessions').exists())

    def test_cleanup_only_export_retains_measured_storage_after_last_run_was_removed(self):
        folder, state = self.run_state(removed=True)
        self.log('logs/storage-cleanup.log', 'completed run retirement; retained shared 11000000000 bytes\n')
        self.log('logs/wizard-requests.log', 'cleanup failed admission on old builder\n')
        self.log('logs/storage-analysis.json', json.dumps({'schema': 1, 'status': 'complete',
            'totalBytes': 140000000000, 'sharedBytes': 11000000000, 'unassignedBytes': 70000000000,
            'runs': [], 'retiredRuns': [{'session': state['session'], 'gameRoot': 'D:\\PrivateGame',
                'exclusiveBytes': 400000000, 'sharedBytes': 11000000000, 'totalBytes': 11400000000}]}))
        self.log('logs/removed-runs/' + state['session'] + '/logs/progress.log',
                 'gameKey=' + 'c' * 64 + ' D:\\PrivateGame PrivateOwner PRIVATE-ACCOUNT\n')
        meta, rows = self.export()
        summary = meta['workspace']['runs'][0]
        self.assertTrue(summary['removed']); self.assertEqual(summary['status'], 'cancelled')
        self.assertEqual(summary['observedOwners']['gameKeys'], ['c' * 64])
        analysis = json.loads(rows['wizard/storage-analysis.json'])
        self.assertEqual(analysis['unassignedBytes'], 70000000000)
        self.assertEqual(analysis['retiredRuns'][0]['gameRoot'], '<owned-game>')
        self.assertIn('runs/' + state['session'] + '/progress.log', rows)
        all_text = '\n'.join(rows.values())
        for private in ('PrivateOwner', 'PRIVATE-ACCOUNT', 'D:\\PrivateGame'):
            self.assertNotIn(private, all_text)
        self.assertEqual(json.loads((folder / 'state.json').read_text()), state)

    def test_explicit_retired_session_preserves_its_original_failure_context(self):
        folder, state = self.run_state(removed=True)
        self.log('logs/removed-runs/' + state['session'] + '/logs/build.log', 'Error: native compiler exited\n')
        meta, rows = self.export(state['session'])
        self.assertEqual(meta['scope'], 'removed-session'); self.assertEqual(meta['session'], state['session'])
        self.assertEqual(meta['status'], 'cancelled'); self.assertIn('native compiler', rows['wizard/build.log'])
        self.assertFalse((self.root / 'sessions').exists())
        self.assertTrue(folder.is_dir())

    def test_workspace_export_does_not_mutate_live_session_or_latest_pointer(self):
        folder, state = self.run_state(status='running')
        write_json(self.root / 'latest-session.json', {'schema': 1, 'session': state['session']})
        before = {path: path.read_bytes() for path in (folder / 'state.json', self.root / 'latest-session.json')}
        meta, _ = self.export()
        self.assertEqual(meta['workspace']['runs'][0]['status'], 'running')
        self.assertIsNone(meta['session']); self.assertNotIn('status', meta)
        for path, raw in before.items(): self.assertEqual(path.read_bytes(), raw)

    def test_finite_manifest_summary_keeps_identity_but_never_exports_inventory_or_profile(self):
        key = 'b' * 64
        write_json(self.root / 'build/manifests' / (key + '.json'), {'schema': 1, 'inputKey': key,
            'target': 'game', 'recipe': 77, 'game': {'key': 'c' * 64, 'files': [{'path': 'SECRET-ASSET.png', 'size': 1}]},
            'mod': {'key': 'd' * 64, 'files': [{'path': 'PRIVATE-SOURCE.cs', 'size': 2}]},
            'profile': {'displayName': 'PROFILE MUST NOT EXPORT'}, 'signing': {'password': 'KEY MUST NOT EXPORT'}})
        meta, rows = self.export()
        row = meta['workspace']['inputManifests'][0]
        self.assertEqual(row['inputKey'], key); self.assertEqual(row['gameFileCount'], 1)
        self.assertEqual(row['gameKey'], 'c' * 64); self.assertFalse(row['contentVerified'])
        text = '\n'.join(rows.values())
        for private in ('SECRET-ASSET.png', 'PRIVATE-SOURCE.cs', 'PROFILE MUST NOT EXPORT', 'KEY MUST NOT EXPORT'):
            self.assertNotIn(private, text)

    def test_global_export_reads_no_payload_and_does_not_enter_cache_or_unity_trees(self):
        forbidden = ('build/cache', 'build/projects', 'build/inputs', 'build/signing', 'build/tool-cache')
        for relative in forbidden:
            self.log(relative + '/payload.bin', 'PRIVATE PAYLOAD')
        self.log('build/builds/' + 'b' * 64 + '/Game.apk', 'PRIVATE APK')
        self.log('build/builds/' + 'b' * 64 + '/Game.bank', 'PRIVATE BANK')
        self.log('saves/save.dat', 'PRIVATE SAVE'); self.log('.env', 'PRIVATE ENV')
        original_open = Path.open; original_iterdir = Path.iterdir; original_digest = support.digest
        def open_file(path, *args, **kwargs):
            if any(path == self.root / name or self.root / name in path.parents for name in forbidden) or path.suffix in ('.apk', '.bank', '.dat') or path.name == '.env':
                raise AssertionError('Support must not read payloads: ' + str(path))
            return original_open(path, *args, **kwargs)
        def list_directory(path):
            if any(path == self.root / name or self.root / name in path.parents for name in forbidden):
                raise AssertionError('Support must not scan payload trees: ' + str(path))
            return original_iterdir(path)
        def digest(path):
            self.assertEqual(path.parent, self.root / 'exports')
            return original_digest(path)
        with patch.object(Path, 'open', open_file), patch.object(Path, 'iterdir', list_directory), patch.object(support, 'digest', digest):
            _, rows = self.export()
        self.assertNotIn('PRIVATE', '\n'.join(rows.values()))

    def test_optional_partial_or_missing_diagnostics_do_not_block_other_failure_logs(self):
        self.log('logs/storage-analysis.json', '{ interrupted JSON')
        self.log('qualification.json', '{ interrupted JSON')
        self.log('build/last-failure.json', '{ interrupted JSON')
        self.log('packages/latest-export.json', '{ interrupted JSON')
        self.log('sessions/' + 'b' * 32 + '/state.json', '{ interrupted JSON')
        self.log('logs/wizard-requests.log', 'actual current API error\n')
        meta, rows = self.export()
        self.assertIn('actual current', rows['wizard/wizard-requests.log'])
        self.assertGreaterEqual(len(meta['omitted']), 5)

    def test_linked_optional_metadata_and_logs_are_omitted_without_following_them(self):
        outside = self.root.parent / ('private-' + self.root.name + '.log')
        outside.write_text('PRIVATE OUTSIDE'); self.addCleanup(outside.unlink)
        logs = self.root / 'logs'; logs.mkdir()
        try:
            (logs / 'storage-cleanup.log').symlink_to(outside)
            (logs / 'storage-analysis.json').symlink_to(outside)
            (self.root / 'build').symlink_to(outside.parent)
        except OSError: self.skipTest('symlinks unavailable')
        meta, rows = self.export()
        self.assertNotIn('PRIVATE OUTSIDE', '\n'.join(rows.values()))
        self.assertGreaterEqual(len(meta['omitted']), 2)

    def test_logs_disappearing_between_selection_and_read_are_optional(self):
        path = self.log('logs/storage-cleanup.log', 'temporary cleanup log')
        original_open = Path.open
        def open_file(item, *args, **kwargs):
            if item == path: raise FileNotFoundError('rotated log')
            return original_open(item, *args, **kwargs)
        with patch.object(Path, 'open', open_file): meta, _ = self.export()
        self.assertIn({'name': 'wizard/storage-cleanup.log', 'reason': 'log disappeared or became unavailable'}, meta['omitted'])

    def test_utility_logs_and_storage_analysis_have_priority_under_actual_archive_caps(self):
        self.log('logs/storage-cleanup.log', 'cleanup first\n' + 'x' * (support.MAX_FILE_BYTES + 10) + '\nFINAL CLEANUP ERROR\n')
        self.log('logs/wizard-requests.log', 'request error\n')
        self.log('logs/storage-analysis.json', '{"totalBytes": 180000000000}')
        for index in range(support.MAX_FILES + 5):
            self.log('build/logs/unity-build-' + str(index) + '.log', 'historical Unity\n' + 'x' * 100)
        meta, rows = self.export()
        for name in ('storage-cleanup.log', 'wizard-requests.log', 'storage-analysis.json'):
            self.assertIn('wizard/' + name, rows)
        self.assertIn('FINAL CLEANUP ERROR', rows['wizard/storage-cleanup.log'])
        self.assertLessEqual(len(rows), support.MAX_FILES + 1)
        self.assertLessEqual(sum(row['exportedBytes'] for row in meta['files']), support.MAX_TOTAL_BYTES)
        self.assertLessEqual(max(row['exportedBytes'] for row in meta['files']), support.MAX_FILE_BYTES)

    def test_global_export_preserves_nested_latest_apk_identity_without_reading_apk(self):
        write_json(self.root / 'build/latest-build.json', {'details': {'apkSha256': 'd' * 64,
            'displayName': 'PRIVATE PROFILE', 'apk': str(self.root / 'build/Game.apk'),
            'buildReport': {'inputKey': 'e' * 64, 'modBuild': 671, 'profile': {'accountId': 'PRIVATE ACCOUNT'}}}})
        meta, rows = self.export()
        self.assertEqual(meta['latest-build.json']['apkSha256'], 'd' * 64)
        self.assertEqual(meta['latest-build.json']['buildReport']['inputKey'], 'e' * 64)
        self.assertNotIn('PRIVATE', '\n'.join(rows.values()))

    def test_private_user_directories_are_redacted_even_with_no_retained_profile(self):
        self.log('logs/wizard-requests.log', 'C:\\Users\\UnknownPerson\\AppData\\failed\n/home/OtherPerson/build\n/Users/MacPerson/failure\nBearer ACCESS\n')
        _, rows = self.export(); text = '\n'.join(rows.values())
        for value in ('UnknownPerson', 'OtherPerson', 'MacPerson', 'ACCESS'): self.assertNotIn(value, text)

    def test_missing_or_stale_latest_cli_pointer_falls_back_to_workspace(self):
        for latest in (None, {'session': 'b' * 32}, {'session': '../unsafe'}):
            with self.subTest(latest=latest):
                if latest is not None: write_json(self.root / 'latest-session.json', latest)
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    code = support.main(['--state-root', str(self.root)])
                self.assertEqual(code, 0)
                result = json.loads(output.getvalue())
                with zipfile.ZipFile(result['path']) as archive: meta = json.loads(archive.read('diagnostic.json'))
                self.assertIsNone(meta['session']); self.assertEqual(meta['scope'], 'workspace')

    def test_explicit_invalid_session_and_foreign_workspace_keep_ownership_guards(self):
        with self.assertRaises(BuildError): support.export_support(self.root, '../unsafe')
        with self.assertRaises(BuildError): support.export_support(self.root, 'b' * 32)
        write_json(self.root / 'wizard-owner.json', {'owner': 'foreign'})
        with self.assertRaises(BuildError): self.export()


if __name__ == '__main__': unittest.main()
