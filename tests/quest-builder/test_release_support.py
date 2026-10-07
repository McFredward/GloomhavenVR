"""Release completeness/tampering and support export's actual privacy boundaries."""
import importlib.util
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import release
import support
from storage import BuildError, record_file, write_json


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name) / 'repo'; self.root.mkdir()
        for name in release.REQUIRED | {'src/GloomhavenVR/Net/NetProtocol.cs', 'tools/quest-wizard/tools.lock.json'}:
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True)
            if name in release.PUBLIC_PACKAGES: shutil.copyfile(ROOT / name, path); continue
            path.write_text('public const ushort ModBuild = 625;' if name.endswith('NetProtocol.cs') else 'public fixture source')
        self.manifest()
    def tearDown(self): self.temp.cleanup()
    def manifest(self):
        names = sorted(str(path.relative_to(self.root)) for path in self.root.rglob('*') if path.is_file() and path.name != release.MANIFEST)
        value = {'schema': 1, 'kind': 'GloomhavenVR game-free Windows builder', 'sourceCommit': 'a' * 40,
                 'modBuild': 625, 'files': [record_file(self.root / name, name) for name in names],
                 'localDependencyRoots': list(release.LOCAL_DEPENDENCIES)}
        write_json(self.root / release.MANIFEST, value); return value
    def test_git_free_inventory_keeps_manifest_and_rejects_tamper(self):
        rows, commit, dirty = release.verified_source_inventory(self.root)
        self.assertEqual(commit, 'a' * 40); self.assertFalse(dirty)
        self.assertIn(release.MANIFEST, {row['path'] for row in rows})
        (self.root / 'tools/quest-wizard/wizard.py').write_text('tampered')
        with self.assertRaises(BuildError): release.verified_source_inventory(self.root)
    def test_unlisted_code_binary_and_generated_exceptions(self):
        derived = self.root / 'libs/RuntimeDeps/Unity.XR.dll'; derived.parent.mkdir(parents=True); derived.write_bytes(b'owner-derived')
        release.verified_source_inventory(self.root)
        for name in ('tools/quest-builder/extra.py', 'Directory.Build.targets', 'libs/RefAsm/Assembly-CSharp.dll'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('unlisted')
            with self.assertRaises(BuildError): release.verified_source_inventory(self.root)
            path.unlink()
    def test_pinned_public_package_is_read_once_but_manifest_identity_remains_required(self):
        package = self.root / next(iter(release.PUBLIC_PACKAGES))
        original_open = Path.open; reads = []
        def observe(path, *args, **kwargs):
            if path == package and args and args[0] == 'rb': reads.append(path)
            return original_open(path, *args, **kwargs)
        with mock.patch.object(Path, 'open', observe):
            rows, _, _ = release.verified_source_inventory(self.root)
        self.assertEqual(len(reads), 1)
        self.assertIn(package.relative_to(self.root).as_posix(), {row['path'] for row in rows})
        value = self.manifest()
        next(row for row in value['files'] if row['path'] in release.PUBLIC_PACKAGES)['sha256'] = 'f' * 64
        write_json(self.root / release.MANIFEST, value)
        with self.assertRaisesRegex(BuildError, 'release file changed'):
            release.verified_source_inventory(self.root)

    def test_manifest_traversal_duplicate_and_case_alias(self):
        for name in ('../outside.py', 'C:/outside.py', 'tools\\extra.py', 'src/NUL.cs'):
            value = self.manifest(); value['files'][0]['path'] = name; write_json(self.root / release.MANIFEST, value)
            with self.assertRaises(BuildError): release.verified_source_inventory(self.root)
        value = self.manifest(); value['files'].append(dict(value['files'][0], path=value['files'][0]['path'].upper()))
        write_json(self.root / release.MANIFEST, value)
        with self.assertRaises(BuildError): release.verified_source_inventory(self.root)
    @unittest.skipIf(os.name == 'nt', 'symlink privilege varies')
    def test_symlink_is_rejected_without_following_it(self):
        path = self.root / 'tools/quest-builder/builder.py'; data = path.read_bytes(); path.unlink()
        target = self.root.parent / 'private'; target.write_bytes(data); path.symlink_to(target)
        with self.assertRaises(BuildError): release.verified_source_inventory(self.root)
    @unittest.skipIf(os.name == 'nt', 'symlink privilege varies')
    def test_exact_authored_link_is_materialized_and_other_targets_rejected(self):
        name, target_name = next(iter(release.AUTHORED_LINKS.items()))
        target = self.root / target_name; target.parent.mkdir(parents=True, exist_ok=True); target.write_text('one authored source')
        link = self.root / name; link.parent.mkdir(parents=True, exist_ok=True); link.symlink_to(target)
        self.assertEqual(release.authored_input(self.root, name), target)
        other = self.root / 'src/other.cs'; other.write_text('different source')
        link.unlink(); link.symlink_to(other)
        with self.assertRaises(BuildError): release.authored_input(self.root, name)

    def test_actual_assembled_archive_excludes_even_tracked_private_payloads(self):
        for name in ('libs/RefAsm/Assembly-CSharp.dll', 'prebuilt/ghvr-figure-meshes-mobile-1.bundle', 'ressources/Game/secret.txt', '.env'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('DO NOT SHIP')
        (self.root / release.MANIFEST).unlink()
        def git(*args): subprocess.run(['git', '-C', str(self.root), *args], check=True, capture_output=True)
        git('init', '-q'); git('add', '.'); git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture')
        output = self.root.parent / 'builder.zip'; report = release.assemble(self.root, output)
        self.assertFalse(report['windowsEndToEndVerified'])
        with zipfile.ZipFile(output) as archive:
            self.assertTrue(any(name.endswith('quest-builder-wizard.cmd') for name in archive.namelist()))
            self.assertFalse(any('RefAsm' in name or 'ghvr-figure-meshes' in name or '/ressources/' in name or name.endswith('/.env') for name in archive.namelist()))
            archive.extractall(self.root.parent / 'extracted')
        release.verified_source_inventory(self.root.parent / 'extracted/GloomhavenVR-Quest-Builder')


class SupportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name); self.session = 'b' * 32
        self.directory = self.root / 'sessions' / self.session; self.directory.mkdir(parents=True)
        write_json(self.root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})
        self.state = {'schema': 1, 'session': self.session, 'status': 'running', 'choices': {'gameRoot': 'C:\\PrivateOwnedGame'},
                      'stages': [{'id': 'build', 'status': 'failed', 'attempts': 1, 'details': {'inputKey': 'd' * 64, 'displayName': 'SecretName'}}],
                      'events': [{'time': 123, 'code': 'failed', 'parameters': {'private': 'must not ship'}}],
                      'needsActions': [{'code': 'failure', 'message': {'en': 'Owned game C:\\PrivateOwnedGame failed'}}]}
        write_json(self.directory / 'state.json', self.state)
        write_json(self.directory / 'profile.json', {'schema': 1, 'displayName': 'SecretName', 'steamId': '76561198000000001'})
        (self.directory / 'logs').mkdir()
    def tearDown(self): self.temp.cleanup()
    def export(self):
        result = support.export_support(self.root, self.session)
        with zipfile.ZipFile(result['path']) as archive:
            return result, {name: archive.read(name).decode() for name in archive.namelist()}
    def test_failed_running_support_has_identity_errors_and_no_assets_credentials_or_savegame(self):
        log = self.directory / 'logs/build.log'
        log.write_text('Source SecretName 76561198000000001 C:\\PrivateOwnedGame\nAuthorization: Bearer super-secret\naccess_token="another secret"\nMY_SECRET=private-env-value\nCookie: private-cookie\nclientSecret: private-client-secret\nError: missing native entrypoint\n')
        for name in ('build/logs/profile.json', 'build/signing/local-key.json', 'build/game/texture.png', 'build/saves/save.dat', '.env'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('MUST NEVER SHIP')
        write_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': 'build', 'error': 'BuildError', 'message': 'Shader identity mismatch', 'environment': {'token': 'NEVER EXPORT'}})
        evidence = self.root / 'build/evidence'; evidence.mkdir()
        write_json(evidence / 'resource-policy.json', {'schema': 1, 'physicalMemoryBytes': 32000000000, 'jobs': 2, 'accessToken': 'secret'})
        before = (self.directory / 'state.json').read_bytes(); result, rows = self.export()
        all_text = '\n'.join(rows.values())
        for forbidden in ('MUST NEVER SHIP', 'SecretName', '76561198000000001', 'super-secret', 'another secret', 'C:\\PrivateOwnedGame', 'must not ship', 'private-env-value', 'private-cookie', 'private-client-secret'):
            self.assertNotIn(forbidden, all_text)
        self.assertIn('Shader identity mismatch', all_text); self.assertNotIn('NEVER EXPORT', all_text)
        self.assertIn('missing native entrypoint', all_text); self.assertIn('32000000000', all_text)
        self.assertIn('d' * 64, all_text); self.assertEqual((self.directory / 'state.json').read_bytes(), before)
        self.assertEqual(result['fileCount'], 3)
    def test_archive_caps_preserve_log_header_and_final_error(self):
        path = self.directory / 'logs/build.log'; path.write_text('start\n' + 'x' * 20000 + '\nFINAL ERROR\n')
        with mock.patch.object(support, 'MAX_FILE_BYTES', 1024), mock.patch.object(support, 'MAX_TOTAL_BYTES', 2048): _, rows = self.export()
        self.assertLessEqual(len(rows['wizard/build.log'].encode()), 1024)
        self.assertIn('start', rows['wizard/build.log']); self.assertIn('FINAL ERROR', rows['wizard/build.log'])
        self.assertTrue(json.loads(rows['diagnostic.json'])['files'][0]['truncated'])
    def test_pre_session_request_diagnostics_are_selected_redacted_and_bounded(self):
        logs = self.root / 'logs'; logs.mkdir()
        (logs / 'wizard-requests.log').write_text('ModuleNotFoundError: native_plugins\nAuthorization: Bearer secret\n')
        (logs / 'wizard-requests.previous.log').write_text('TypeError: folder picker field\n')
        (logs / 'unrelated.log').write_text('MUST NOT EXPORT')
        _, rows = self.export()
        self.assertIn('native_plugins', rows['wizard/wizard-requests.log'])
        self.assertIn('folder picker field', rows['wizard/wizard-requests.previous.log'])
        self.assertNotIn('Bearer secret', '\n'.join(rows.values()))
        self.assertNotIn('MUST NOT EXPORT', '\n'.join(rows.values()))
    def test_oversized_structured_resources_do_not_bypass_secret_redaction(self):
        evidence = self.root / 'build/evidence'; evidence.mkdir(parents=True)
        write_json(evidence / 'resource-policy.json', {'schema': 1, 'huge': 'x' * 4000, 'password': 'DO NOT LEAK'})
        with mock.patch.object(support, 'MAX_FILE_BYTES', 1024): _, rows = self.export()
        self.assertNotIn('DO NOT LEAK', '\n'.join(rows.values()))
        self.assertIn('exceeds diagnostic size', rows['resources/resource-policy.json'])

    def test_explicit_destination_never_overwrites_file(self):
        output = self.root / 'existing.zip'; output.write_bytes(b'keep')
        with self.assertRaises(FileExistsError): support.export_support(self.root, self.session, output)
        self.assertEqual(output.read_bytes(), b'keep')
    def test_unknown_build_logs_and_nested_files_are_excluded(self):
        logs = self.root / 'build/logs'; logs.mkdir(parents=True)
        (logs / 'unity-build-abc.log').write_text('accepted')
        (logs / 'account-export.log').write_text('private')
        (logs / 'nested').mkdir(); (logs / 'nested/unity-build.log').write_text('private')
        _, rows = self.export(); self.assertIn('build/unity-build-abc.log', rows)
        self.assertFalse(any('account-export' in name or 'nested' in name for name in rows))

    def test_failed_recovery_exports_only_exact_known_core_and_batch_logs_with_redaction(self):
        key = 'c' * 64
        folder = self.root / 'build/cache/full-original-recovery' / key
        for name, text in (('core-export.log', 'core identities\nAuthorization: Bearer hidden\n'),
                           ('BundleRecovery/batch-000/export.log', 'FAILED object collection class pathId\n'),
                           ('BundleRecovery/batch-000/profile.json', 'MUST NEVER SHIP'),
                           ('Assets/Texture.log', 'MUST NEVER SHIP')):
            path = folder / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)
        write_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': 'recovery', 'key': key, 'message': 'identity mismatch'})
        self.state['events'][0]['parameters'].update(phase='recovery-batches', done=0, total=29, unit='batches', cause='identity mismatch')
        write_json(self.directory / 'state.json', self.state)
        _, rows = self.export()
        self.assertIn('recovery/' + key + '/core-export.log', rows)
        self.assertIn('recovery/' + key + '/batch-000/export.log', rows)
        text = '\n'.join(rows.values())
        self.assertNotIn('MUST NEVER SHIP', text); self.assertNotIn('Bearer hidden', text)
        self.assertNotIn('must not ship', text)
        self.assertEqual(json.loads(rows['diagnostic.json'])['events'][0]['parameters']['total'], 29)

    def test_migrated_recovery_exports_bound_workspace_logs_and_receipt_sizes_only(self):
        current, original = 'a' * 64, 'b' * 64
        folder = self.root / 'build/cache/full-original-recovery' / original
        folder.mkdir(parents=True)
        (folder / 'core-export.log').write_text('Retained core export completed\n')
        receipt = folder / 'core-recovery.json'
        with receipt.open('wb') as stream:
            stream.write(b'MUST NEVER EXPORT THE ASSET CATALOG')
            stream.truncate(24 * 1048576)
        write_json(self.root / 'build/cache/raw-recovery-resume' / (current + '.json'), {
            'schema': 1, 'owner': 'Quest raw recovery resume', 'currentRecipeKey': current,
            'originalWorkspaceKey': original, 'private': 'MUST NOT EXPORT'})
        write_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'recovery', 'key': current, 'message': 'Receipt failure'})
        before = receipt.stat()
        _, rows = self.export()
        self.assertIn('recovery/' + original + '/core-export.log', rows)
        stats = json.loads(rows['diagnostic.json'])['recoveryReceipts'][0]
        self.assertEqual(stats['workspaceKey'], original)
        self.assertEqual(stats['selection'], 'raw-resume-binding')
        self.assertFalse(stats['contentVerified'])
        self.assertEqual(stats['files'][0], {'name': 'core-recovery.json', 'exists': True, 'bytes': 24 * 1048576})
        self.assertNotIn('MUST NEVER EXPORT', '\n'.join(rows.values()))
        self.assertNotIn('MUST NOT EXPORT', '\n'.join(rows.values()))
        self.assertEqual(receipt.stat().st_mtime_ns, before.st_mtime_ns)

    def test_failure_before_binding_includes_recent_candidates_without_claiming_ownership(self):
        current, original = 'a' * 64, 'b' * 64
        folder = self.root / 'build/cache/full-original-recovery' / original
        folder.mkdir(parents=True)
        (folder / 'core-export.log').write_text('Prior export finished\n')
        write_json(self.root / 'build/last-failure.json', {'schema': 1, 'stage': 'recovery', 'key': current})
        _, rows = self.export()
        self.assertIn('recovery/' + original + '/core-export.log', rows)
        self.assertEqual(json.loads(rows['diagnostic.json'])['recoveryReceipts'][0]['selection'], 'recent-candidate')

    def test_corrupt_or_path_binding_cannot_follow_outside_diagnostic_roots(self):
        current = 'a' * 64
        folder = self.root / 'build/cache/full-original-recovery' / current
        folder.mkdir(parents=True)
        (folder / 'core-export.log').write_text('Current log\n')
        outside = self.root / 'private-export/core-export.log'
        outside.parent.mkdir(); outside.write_text('MUST NEVER SHIP')
        binding = self.root / 'build/cache/raw-recovery-resume' / (current + '.json')
        binding.parent.mkdir()
        failure = {'schema': 1, 'stage': 'recovery', 'key': current}
        write_json(self.root / 'build/last-failure.json', failure)
        for value in ('broken JSON', json.dumps({'schema': 1, 'owner': 'Quest raw recovery resume',
                      'currentRecipeKey': current, 'originalWorkspaceKey': '../../../private-export'})):
            with self.subTest(binding=value):
                binding.write_text(value)
                _, rows = self.export()
                self.assertIn('Current log', '\n'.join(rows.values()))
                self.assertNotIn('MUST NEVER SHIP', '\n'.join(rows.values()))

    def test_procedural_support_keeps_actual_failures_without_runtime_or_owner_data(self):
        key = 'd' * 64
        prefix = 'procedural/procedural-runtime-proton/' + key + '/'
        root = self.root / 'build/tool-cache/campaign-native/procedural-runtime-proton' / key
        root.mkdir(parents=True)
        (root / 'native-build.log').write_text('Native linker failed\nAuthorization: Bearer hidden\n')
        write_json(root / 'proton-stage-summary.json', {'status': 'failed', 'backend': 'proton-arm64ec-fex',
            'missing': ['Required native binding absent'], 'androidExecutionVerified': False})
        for name in ('payload/ApparanceEngine.dll', 'payload/runtime-manifest.json', 'downloads/private.log',
                     'proton-stage-audit.json'):
            path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('MUST NEVER SHIP')
        _, rows = self.export()
        self.assertIn('Native linker failed', rows[prefix + 'native-build.log'])
        audit = json.loads(rows[prefix + 'proton-stage-summary.json'])
        self.assertEqual(audit['missing'], ['Required native binding absent'])
        self.assertFalse(audit['androidExecutionVerified'])
        self.assertNotIn('MUST NEVER SHIP', '\n'.join(rows.values()))
        self.assertNotIn('Bearer hidden', '\n'.join(rows.values()))
    @unittest.skipIf(os.name == 'nt', 'symlink privilege varies')
    def test_symlink_logs_are_rejected(self):
        target = self.root / 'private'; target.write_text('secret')
        (self.directory / 'logs/build.log').symlink_to(target)
        with self.assertRaises(BuildError): self.export()

if __name__ == '__main__': unittest.main()
