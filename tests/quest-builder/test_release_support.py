"""Release completeness/tampering and support export's actual privacy boundaries."""
import importlib.util
import io
import json
import ntpath
import os
import shutil
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
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
        for name in release.REQUIRED | release.LINUX_REQUIRED | {'src/GloomhavenVR/Net/NetProtocol.cs', 'tools/quest-wizard/tools.lock.json'}:
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
    def test_actual_archive_excludes_owned_environment_data_and_keeps_its_generator(self):
        generated = {
            'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/static-100.bytes': b'GHEM1 ORIGINAL FIXTURE GEOMETRY',
            'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/static-100.bytes.meta': b'original mesh importer fixture',
            'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json': b'{"format":1,"entries":["private original"]}',
            'tools/environment-mesh/manifest.json': b'private original derivatives receipt',
            'tools/environment-mesh/bank.json': b'private original native bank receipt',
            'tools/environment-mesh/catalog.json.gz': b'private complete original mesh ancestry census',
            'tools/environment-mesh/catalog.json': b'private uncompressed original mesh ancestry census',
            'tools/environment-mesh/ornaments.json': b'private original decoration graph evidence',
        }
        for name, content in generated.items():
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(content)
        shader = self.root / 'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/ScenarioCheapTerrain.shader'
        shader.parent.mkdir(parents=True, exist_ok=True); shader.write_text('authored Shader fixture')
        (self.root / release.MANIFEST).unlink()
        subprocess.run(['git', 'init', '--quiet', str(self.root)], check=True)
        subprocess.run(['git', '-C', str(self.root), 'add', '.'], check=True)
        subprocess.run(['git', '-C', str(self.root), '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
                        'commit', '--quiet', '-m', 'Source and owned originals fixture'], check=True)
        archive = Path(self.temp.name) / 'source-builder.zip'
        report = release.assemble(self.root, archive)
        with zipfile.ZipFile(archive) as zipped:
            names = {name.removeprefix('GloomhavenVR-Quest-Builder/') for name in zipped.namelist()}
            manifest = json.loads(zipped.read('GloomhavenVR-Quest-Builder/' + release.MANIFEST))
        self.assertFalse(names.intersection(generated))
        self.assertFalse({row['path'] for row in manifest['files']}.intersection(generated))
        self.assertIn(shader.relative_to(self.root).as_posix(), names)
        for name in ('tools/quest-builder/environment_bank.py', 'tools/environment-mesh/export-native.py',
                     'scripts/generate-environment-meshes.py', 'tools/environment-mesh/geometry.py',
                     'tools/environment-mesh/roles.py'):
            self.assertIn(name, names)
        self.assertIn('original-derived figure/environment mesh banks', report['excluded'])

    def test_git_free_inventory_keeps_manifest_and_rejects_tamper(self):
        rows, commit, dirty = release.verified_source_inventory(self.root)
        self.assertEqual(commit, 'a' * 40); self.assertFalse(dirty)
        self.assertIn(release.MANIFEST, {row['path'] for row in rows})
        (self.root / 'tools/quest-wizard/wizard.py').write_text('tampered')
        with self.assertRaises(BuildError): release.verified_source_inventory(self.root)

    def test_release_cannot_publish_an_inventory_omitting_dynamic_installation_dependencies(self):
        for name in ('tools/quest-installer/installer.py', 'tools/quest-installer/signing_recovery.py',
                     'tools/quest-installer/native_rename.py', 'tools/quest-wizard/device_tools.py',
                     'tools/quest-wizard/server.py', 'tools/quest-wizard/cleanup.py',
                     'tools/quest-wizard/cleanup_io.py', 'tools/quest-wizard/run_storage.py'):
            with self.subTest(name=name):
                path = self.root / name; original = path.read_bytes()
                try:
                    path.unlink(); self.manifest()
                    with self.assertRaisesRegex(BuildError, 'Builder release is incomplete'):
                        release.verified_source_inventory(self.root)
                finally:
                    path.write_bytes(original); self.manifest()

    def test_legacy_release_still_valid_but_new_kind_requires_linux_launcher(self):
        for name in release.LINUX_REQUIRED: (self.root / name).unlink()
        value = self.manifest(); value['localDependencyRoots'] = list(release.LEGACY_LOCAL_DEPENDENCIES)
        write_json(self.root / release.MANIFEST, value)
        release.verified_source_inventory(self.root)
        value['kind'] = release.RELEASE_KIND; write_json(self.root / release.MANIFEST, value)
        with self.assertRaisesRegex(BuildError, 'missing its Linux launcher'):
            release.verified_source_inventory(self.root)

    def test_linux_runtime_and_adb_cache_are_local_outputs_not_release_tampering(self):
        for name in ('scripts/.quest-python-linux/runtime/python', 'scripts/.quest-adb/platform-tools/adb',
                     'scripts/.quest-bootstrap-linux.lock', 'scripts/.quest-adb.lock'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b'owner-created fixture')
        release.verified_source_inventory(self.root)
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
        # Fixture publication already measured this exact file. Its strong
        # unchanged stamp carries that proof; release admission still compares
        # the manifest below and must not read the same archive again.
        self.assertEqual(len(reads), 0)
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
            self.assertTrue(any(name.endswith('/Quest-Builder.sh') for name in archive.namelist()))
            for name in ('Quest-Builder.sh', 'scripts/quest-builder-wizard.sh', 'tools/quest-installer/bootstrap.sh'):
                mode = archive.getinfo('GloomhavenVR-Quest-Builder/' + name).external_attr >> 16
                self.assertEqual(mode & 0o777, 0o755)
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
    def install_log(self):
        self.state['stages'].append({'id': 'install', 'status': 'blocked', 'startedAt': 100})
        write_json(self.directory / 'state.json', self.state)
        name = 'wireless-install-20261011T095426Z-ef64c522.log'
        path = self.root / 'device' / name; path.parent.mkdir()
        path.write_text('adb devices\nNo authorized USB Quest\nSecretName\nAuthorization: Bearer PRIVATE\n')
        log = self.directory / 'logs/install.log'
        log.write_text('Quest installation stopped: No authorized USB Quest\nADB log: ' + str(path) + '\n')
        return name, path, log

    def test_portable_export_diagnostics_include_only_bounded_receipt_and_allowlisted_logs(self):
        write_json(self.root / 'packages/latest-export.json', {'result': {
            'schema': 1, 'package': 'dev.gloomhavenvr.quest', 'path': 'C:\\PrivateOwnedGame\\shared.zip',
            'size': 123456, 'sha256': 'a' * 64, 'displayName': 'SecretName',
            'signingKey': 'PRIVATE KEY MUST NOT SHIP', 'inventory': ['PRIVATE ORIGINAL ASSET']}})
        logs = self.root / 'logs'; logs.mkdir()
        (logs / 'package-export.jsonl').write_text('export_failed C:\\PrivateOwnedGame\\shared.zip\nAuthorization: Bearer PRIVATE\n')
        (logs / 'package-export.previous.jsonl').write_text('export_started\n')
        (logs / 'unrelated-export.log').write_text('UNRELATED LOG MUST NOT SHIP')
        (self.root / 'packages/shared.zip').write_bytes(b'GAME BYTES MUST NOT SHIP')
        (self.root / 'packages/private.keystore').write_bytes(b'PRIVATE KEY MUST NOT SHIP')
        _, files = self.export()
        receipt = json.loads(files['diagnostic.json'])['portableExport']
        self.assertEqual(receipt['size'], 123456)
        self.assertEqual(receipt['sha256'], 'a' * 64)
        self.assertIn('<owned-game>', receipt['path'])
        self.assertIn('wizard/package-export.jsonl', files)
        self.assertIn('wizard/package-export.previous.jsonl', files)
        text = '\n'.join(files.values())
        for secret in ('PrivateOwnedGame', 'SecretName', 'PRIVATE', 'GAME BYTES', 'UNRELATED LOG'):
            self.assertNotIn(secret, text)
    def test_support_exports_exact_current_adb_log_with_existing_redaction(self):
        name, path, log = self.install_log()
        before = path.read_bytes()
        other = path.with_name('wireless-install-20261010T000000Z-abcdef12.log')
        other.write_text('UNRELATED DEVICE HISTORY')
        _, files = self.export()
        self.assertIn('No authorized USB Quest', files['device/' + name])
        self.assertIn('wizard/install.log', files)
        text = '\n'.join(files.values())
        self.assertNotIn('SecretName', text); self.assertNotIn('PRIVATE', text)
        self.assertNotIn('UNRELATED DEVICE HISTORY', text)
        self.assertEqual(before, path.read_bytes())
    def test_current_windows_crlf_adb_reference_survives_mixed_child_output(self):
        name, path, log = self.install_log()
        log.write_bytes(('Quest installation stopped: Signing-key mismatch.\r\nADB log: '
                         + str(path).replace('/', '\\') + '\r\nSelected local APK: completed.apk\r\n').encode())
        _, files = self.export()
        self.assertIn('device/' + name, files)
        self.assertIn('Signing-key mismatch', files['wizard/install.log'])
    def signing_log(self):
        name, adb, log = self.install_log()
        home = self.root / 'device/signing-recovery/fixture-headset'
        transaction = 'a' * 32
        folder = home / transaction; folder.mkdir(parents=True)
        archive = 'private-' + 'c' * 32 + '.tar'
        (folder / archive).write_bytes(b'PRIVATE ARCHIVE BYTES NEVER EXPORT')
        (folder / 'original.apk').write_bytes(b'OLD APK BYTES NEVER EXPORT')
        value = {'schema': 1, 'package': 'dev.gloomhavenvr.quest', 'deviceSerial': 'fixture-headset',
                 'transaction': transaction, 'state': 'created', 'apkSha256': 'd' * 64, 'inputKey': 'e' * 64,
                 'banks': ['files/quest-owned-game', 'files/quest-mod-resources', 'UNKNOWN PRIVATE BANK'],
                 'roots': [{'name': 'private', 'archive': archive, 'inventory': {
                     'NativeSaveNeverShip': {'type': 'file', 'size': 32, 'sha256': 'f' * 64},
                     'HiddenProfileNeverShip': {'type': 'directory'},
                     'WineLinkNeverShip': {'type': 'symlink', 'target': 'PrivateTargetNeverShip'}}}],
                 'password': 'JOURNAL SECRET NEVER EXPORT'}
        write_json(home / 'active.json', value)
        write_json(self.root / 'build/latest-build.json', {'details': {'apkSha256': 'd' * 64, 'inputKey': 'e' * 64}})
        log.write_bytes(('Quest installation stopped: Signing migration could not safely continue: tar permission denied\r\n'
                         'Original recovery data is retained at ' + str(folder).replace('/', '\\')
                         + '. Retry with the same APK and headset.\r\nADB log: ' + str(adb) + '\r\n').encode())
        return home, folder, value
    def test_selected_signing_recovery_exports_frontier_without_app_bytes_or_member_names(self):
        home, folder, value = self.signing_log()
        _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']
        self.assertEqual(row['state'], 'created')
        self.assertEqual(row['roots'], [{'name': 'private', 'journalled': True, 'files': 1, 'directories': 1,
                                       'symlinks': 1, 'archivePresent': True, 'archiveBytes': 34,
                                       'archiveBlockRemainder': 34}])
        self.assertEqual(row['retainedBanks'], ['files/quest-owned-game', 'files/quest-mod-resources'])
        self.assertTrue(row['originalApkPresent'])
        combined = '\n'.join(files.values())
        for secret in ('PRIVATE ARCHIVE BYTES', 'OLD APK BYTES', 'NativeSaveNeverShip', 'HiddenProfileNeverShip',
                       'WineLinkNeverShip', 'PrivateTargetNeverShip', 'JOURNAL SECRET', 'UNKNOWN PRIVATE BANK'):
            self.assertNotIn(secret, combined)
        self.assertEqual((folder / 'original.apk').read_bytes(), b'OLD APK BYTES NEVER EXPORT')

    def test_explicit_reset_summary_keeps_exact_frontier_without_backup_payload_reads(self):
        home, folder, value = self.signing_log()
        for phase in ('reset-approved', 'reset-removing', 'reset-installing', 'reset-installed', 'reset-complete'):
            value.update(state=phase, saveDataReset=True, restorationSkipped=True, resetConsentTransaction=value['transaction'])
            write_json(home / 'active.json', value)
            with self.subTest(phase=phase):
                _, files = self.export()
                row = json.loads(files['diagnostic.json'])['signingRecovery']
                self.assertEqual(row['state'], phase)
                self.assertTrue(row['saveDataReset']); self.assertTrue(row['restorationSkipped'])
                self.assertTrue(row['resetConsentBound'])
                self.assertNotIn('PRIVATE ARCHIVE BYTES', '\n'.join(files.values()))
    def test_signing_summary_rejects_foreign_source_or_transaction_and_retains_failure(self):
        home, folder, value = self.signing_log()
        for key, changed in [('apkSha256', '0' * 64), ('inputKey', '1' * 64), ('transaction', '2' * 32),
                             ('deviceSerial', 'foreign-headset'), ('package', 'foreign.app')]:
            with self.subTest(key=key):
                write_json(home / 'active.json', value | {key: changed})
                _, files = self.export()
                self.assertNotIn('signingRecovery', json.loads(files['diagnostic.json']))
                self.assertEqual(json.loads(files['diagnostic.json'])['signingRecoveryUnavailable']['reason'],
                                 'build-identity-mismatch' if key in ('apkSha256', 'inputKey') else 'journal-identity-mismatch')
                self.assertIn('tar permission denied', files['wizard/install.log'])
    def test_signing_summary_rejects_journal_link_and_oversize_without_reading_backup(self):
        home, folder, value = self.signing_log()
        journal = home / 'active.json'
        foreign = self.root / 'foreign-journal.json'; write_json(foreign, value)
        journal.unlink(); journal.symlink_to(foreign)
        self.assertIsNone(support.signing_recovery_stats(self.root, self.directory, self.state))
        journal.unlink()
        with journal.open('wb') as stream: stream.truncate(32 * 1048576 + 1)
        self.assertIsNone(support.signing_recovery_stats(self.root, self.directory, self.state))
    def test_signing_summary_never_follows_archive_path_outside_transaction(self):
        home, folder, value = self.signing_log()
        value['roots'][0]['archive'] = '../../outside.tar'
        write_json(home / 'active.json', value)
        _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']['roots'][0]
        self.assertNotIn('archivePresent', row)
        self.assertNotIn('archiveBytes', row)
    def test_real_recovery_commit_with_windows_failure_exports_current_frontier(self):
        home, folder, value = self.signing_log()
        spec = importlib.util.spec_from_file_location('support_recovery_fixture', ROOT / 'tools/quest-installer/signing_recovery.py')
        recovery = importlib.util.module_from_spec(spec); spec.loader.exec_module(recovery)
        selected_apk = self.root / 'selected.apk'; selected_apk.write_bytes(b'fixture selected APK identity')
        source = SimpleNamespace(apk=selected_apk, sha256=value['apkSha256'], input_key=value['inputKey'])
        transaction = recovery.Recovery(None, 'fixture-address', 'fixture-headset', source, self.root / 'device')
        transaction.journal = value | {'roots': [value['roots'][0] | {
            'capturePaths': ['.', './NativeSaveNeverShip'], 'captureFiles': 2, 'captureBytes': 32, 'accessMode': 'app'}]}
        transaction.commit()
        name = 'wireless-install-20261011T095426Z-ef64c522.log'
        trailer = str(folder).replace('/', '\\')
        log = ('Retained signing migration backup: ' + trailer + '\r\n'
               'Quest installation stopped: Signing migration could not safely continue: '
               'Automatic app-data access repair exhausted shell and app identities: '
               "ADB failed: find: '/sdcard/Android/data/dev.gloomhavenvr.quest': Permission denied; "
               'the installed app was retained.. Original recovery data is retained at ' + trailer
               + '. Retry with the same APK and headset.\r\nADB log: '
               + str(self.root / 'device' / name).replace('/', '\\') + '\r\n')
        (self.directory / 'logs/install.log').write_bytes(log.encode())
        journal_before = (home / 'active.json').read_bytes()
        with mock.patch.object(support.os.path, 'normcase', side_effect=ntpath.normcase):
            _, files = self.export()
        meta = json.loads(files['diagnostic.json'])
        self.assertEqual(meta['signingRecovery']['apkSha256'], source.sha256)
        self.assertEqual(meta['signingRecovery']['inputKey'], source.input_key)
        self.assertEqual({key: meta['signingRecovery']['roots'][0][key]
                          for key in ('plannedMembers', 'plannedFileBytes', 'accessMode')},
                         {'plannedMembers': 1, 'plannedFileBytes': 32, 'accessMode': 'app'})
        self.assertNotIn('signingRecoveryUnavailable', meta)
        self.assertIn('Automatic app-data access repair exhausted', files['wizard/install.log'])
        self.assertEqual(journal_before, (home / 'active.json').read_bytes())
        self.assertNotIn('NativeSaveNeverShip', '\n'.join(files.values()))
    def test_recovery_census_summary_is_stat_only_and_never_reads_binary_backup(self):
        home, folder, value = self.signing_log()
        value['roots'][0].update(captureFiles=6, captureBytes=6200, accessMode='app-process',
                                capturePaths=['.', './PRIVATE CENSUS NAME'])
        archive = folder / value['roots'][0]['archive']
        archive.write_bytes(b'PRIVATE BINARY BACKUP' + b'\0' * (6679 - 21))
        self.assertEqual(archive.stat().st_size, 6679)
        write_json(home / 'active.json', value)
        original_open = Path.open
        def observed_open(path, *args, **kwargs):
            self.assertNotEqual(path, archive, 'Support must not open app-data backups.')
            return original_open(path, *args, **kwargs)
        with mock.patch.object(Path, 'open', observed_open):
            _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']['roots'][0]
        self.assertEqual((row['plannedMembers'], row['plannedFileBytes'], row['archiveBlockRemainder']),
                         (5, 6200, 23))
        self.assertEqual(row['accessMode'], 'app-process')
        combined = '\n'.join(files.values())
        self.assertNotIn('PRIVATE CENSUS NAME', combined)
        self.assertNotIn('PRIVATE BINARY BACKUP', combined)
    def test_invalid_optional_census_metadata_cannot_spoil_the_failure_report(self):
        home, folder, value = self.signing_log()
        for changed in ({'captureFiles': True, 'captureBytes': -1, 'accessMode': 'PRIVATE UNKNOWN MODE'},
                        {'captureFiles': 100002, 'captureBytes': 2**63},
                        {'captureFiles': 0, 'captureBytes': False}):
            with self.subTest(changed=changed):
                value['roots'][0].update(changed)
                write_json(home / 'active.json', value)
                _, files = self.export()
                row = json.loads(files['diagnostic.json'])['signingRecovery']['roots'][0]
                self.assertFalse({'plannedMembers', 'plannedFileBytes', 'accessMode'} & row.keys())
                self.assertIn('tar permission denied', files['wizard/install.log'])
    def test_bounded_capture_repair_summary_excludes_typed_member_names_and_unknown_fields(self):
        home, folder, value = self.signing_log()
        repair = {'root': 'private', 'method': 'host-pax', 'reason': 'verified',
                  'attempts': 2, 'totalAttempts': 2, 'expectedCount': 5, 'observedCount': 5,
                  'missingCount': 0, 'unexpectedCount': 0, 'typeMismatchCount': 0,
                  'sizeMismatchCount': 0, 'archiveBytes': 10240, 'archiveAligned': True}
        value['captureRepair'] = repair | {'privateCause': 'PRIVATE FAILURE PATH'}
        value['roots'][0].update(captureTransport='host-pax', captureEntries={
            'PRIVATE SAVE MEMBER': {'type': 'file', 'size': 12, 'mode': 33152}})
        write_json(home / 'active.json', value)
        _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']
        self.assertEqual(row['captureRepair'], repair)
        self.assertEqual(row['roots'][0]['captureTransport'], 'host-pax')
        self.assertFalse(row['hardwareVerified'])
        for secret in ('PRIVATE SAVE MEMBER', 'PRIVATE FAILURE PATH', 'captureEntries'):
            self.assertNotIn(secret, '\n'.join(files.values()))
        for changed in ({'root': 'PRIVATE UNKNOWN ROOT'}, {'missingCount': True},
                        {'archiveAligned': 'true'}, {'method': 'PRIVATE METHOD'},
                        {'attempts': 101}, {'archiveBytes': -1}):
            with self.subTest(changed=changed):
                value['captureRepair'] = repair | changed
                write_json(home / 'active.json', value)
                _, files = self.export()
                self.assertNotIn('captureRepair', json.loads(files['diagnostic.json'])['signingRecovery'])
    def test_windows_normalized_recovery_boundary_rejects_sibling_prefix(self):
        home, folder, value = self.signing_log()
        foreign = self.root / 'device/signing-recovery-foreign' / home.name / value['transaction']
        write_json(foreign.parent / 'active.json', value | {'password': 'FOREIGN SECRET'})
        log = self.directory / 'logs/install.log'
        log.write_text(log.read_text().replace(str(folder).replace('/', '\\'), str(foreign).replace('/', '\\')))
        original_read = support.read_object
        def observe(path, *args, **kwargs):
            self.assertNotEqual(Path(path), foreign.parent / 'active.json')
            return original_read(path, *args, **kwargs)
        with mock.patch.object(support.os.path, 'normcase', side_effect=ntpath.normcase), \
                mock.patch.object(support, 'read_object', side_effect=observe):
            _, files = self.export()
        meta = json.loads(files['diagnostic.json'])
        self.assertNotIn('signingRecovery', meta)
        self.assertEqual(meta['signingRecoveryUnavailable']['reason'], 'path-binding-mismatch')
        self.assertNotIn('FOREIGN SECRET', '\n'.join(files.values()))
    def test_save_policy_summary_exports_counts_without_omitted_private_paths(self):
        home, folder, value = self.signing_log()
        value.update(dataPolicy='saves', policyOmissions=[
            {'root': 'external', 'path': 'files/PRIVATE OMITTED NAME', 'type': 'file',
             'size': 430807, 'reason': 'unreadable-non-save'},
            {'root': 'private', 'path': 'PRIVATE PROFILE NAME', 'type': 'special',
             'size': 0, 'reason': 'unreadable-non-save'}])
        write_json(home / 'active.json', value)
        before = (home / 'active.json').read_bytes()
        with mock.patch.object(support.os.path, 'normcase', side_effect=ntpath.normcase):
            _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']
        self.assertEqual((row['dataPolicy'], row['omittedNonSaveEntries'], row['omittedNonSaveBytes']),
                         ('saves', 2, 430807))
        self.assertNotIn('PRIVATE OMITTED NAME', '\n'.join(files.values()))
        self.assertNotIn('PRIVATE PROFILE NAME', '\n'.join(files.values()))
        self.assertEqual(before, (home / 'active.json').read_bytes())
    def test_small_data_summary_distinguishes_app_data_from_legacy_apk_and_bank_parking(self):
        home, folder, value = self.signing_log()
        value.update(dataPolicy='saves', smallDataOnly=True, parkingMode='atomic')
        value['roots'][0].update(captureBytes=8192, captureFiles=4)
        write_json(home / 'active.json', value)
        before = (home / 'active.json').read_bytes()
        _, files = self.export()
        row = json.loads(files['diagnostic.json'])['signingRecovery']
        self.assertTrue(row['smallDataOnly'])
        self.assertEqual(row['parkingMode'], 'atomic')
        self.assertEqual(row['plannedAppDataBytes'], 8192)
        self.assertEqual(row['originalApkBytes'], (folder / 'original.apk').stat().st_size if (folder / 'original.apk').exists() else 0)
        self.assertEqual(before, (home / 'active.json').read_bytes())
        self.assertNotIn('NativeSaveNeverShip', '\n'.join(files.values()))
    def test_invalid_omission_summary_keeps_failure_without_claiming_a_policy(self):
        home, folder, value = self.signing_log()
        for changed in ({'dataPolicy': 'discard-saves'},
                        {'dataPolicy': 'all', 'policyOmissions': [{'size': 1, 'reason': 'unreadable-non-save'}]},
                        {'dataPolicy': 'saves', 'policyOmissions': [{'size': True, 'reason': 'unreadable-non-save'}]},
                        {'dataPolicy': 'saves', 'policyOmissions': [{'size': -1, 'reason': 'unreadable-non-save'}]}):
            with self.subTest(changed=changed):
                write_json(home / 'active.json', value | changed)
                _, files = self.export()
                meta = json.loads(files['diagnostic.json'])
                self.assertNotIn('signingRecovery', meta)
                self.assertEqual(meta['signingRecoveryUnavailable']['reason'], 'journal-invalid')
                self.assertIn('tar permission denied', files['wizard/install.log'])
    def test_missing_current_recovery_journal_has_bounded_reason_and_keeps_failure(self):
        home, folder, value = self.signing_log()
        (home / 'active.json').unlink()
        unrelated = home.parent / 'unrelated-headset' / 'active.json'
        write_json(unrelated, value | {'password': 'UNRELATED JOURNAL NEVER READ'})
        original_read = support.read_object
        def observe(path, *args, **kwargs):
            self.assertNotEqual(Path(path), unrelated)
            return original_read(path, *args, **kwargs)
        before = (self.directory / 'state.json').read_bytes()
        with mock.patch.object(support, 'read_object', side_effect=observe):
            _, files = self.export()
        meta = json.loads(files['diagnostic.json'])
        self.assertNotIn('signingRecovery', meta)
        self.assertEqual(meta['signingRecoveryUnavailable'], {'reason': 'journal-missing', 'hardwareVerified': False})
        self.assertIn('tar permission denied', files['wizard/install.log'])
        self.assertNotIn('UNRELATED JOURNAL', '\n'.join(files.values()))
        self.assertEqual(before, (self.directory / 'state.json').read_bytes())
    def test_named_recovery_summary_failure_reasons_do_not_export_exception_paths(self):
        home, folder, value = self.signing_log()
        journal = home / 'active.json'
        cases = [('journal-invalid', b'{ PRIVATE JOURNAL CONTENT'),
                 ('journal-oversized', None)]
        for reason, content in cases:
            with self.subTest(reason=reason):
                if content is None:
                    with journal.open('wb') as stream: stream.truncate(32 * 1048576 + 1)
                else: journal.write_bytes(content)
                _, files = self.export()
                self.assertEqual(json.loads(files['diagnostic.json'])['signingRecoveryUnavailable']['reason'], reason)
                self.assertNotIn('PRIVATE JOURNAL CONTENT', '\n'.join(files.values()))
                self.assertIn('tar permission denied', files['wizard/install.log'])
        write_json(journal, value)
        original_read = support.read_object
        def deny(path, *args, **kwargs):
            if Path(path) == journal: raise OSError('PRIVATE JOURNAL PATH NEVER EXPORT')
            return original_read(path, *args, **kwargs)
        with mock.patch.object(support, 'read_object', side_effect=deny):
            _, files = self.export()
        self.assertNotIn('PRIVATE JOURNAL PATH', '\n'.join(files.values()))
        self.assertEqual(json.loads(files['diagnostic.json'])['signingRecoveryUnavailable']['reason'], 'journal-unreadable')
    def test_missing_or_invalid_completed_build_receipt_does_not_adopt_recovery(self):
        home, folder, value = self.signing_log()
        latest = self.root / 'build/latest-build.json'
        latest.unlink()
        _, files = self.export()
        self.assertEqual(json.loads(files['diagnostic.json'])['signingRecoveryUnavailable']['reason'], 'build-receipt-missing')
        write_json(latest, {'details': []})
        diagnostic = {}
        self.assertIsNone(support.signing_recovery_stats(self.root, self.directory, self.state, diagnostic=diagnostic))
        self.assertEqual(diagnostic['reason'], 'build-receipt-invalid')
    def test_signing_diagnostic_never_follows_foreign_recovery_reference(self):
        home, folder, value = self.signing_log()
        foreign = self.root / 'foreign' / 'fixture-headset' / value['transaction']
        write_json(foreign.parent / 'active.json', value | {'password': 'FOREIGN SECRET'})
        log = self.directory / 'logs/install.log'
        log.write_text(log.read_text().replace(str(folder).replace('/', '\\'), str(foreign).replace('/', '\\')))
        original_read = support.read_object
        def observe(path, *args, **kwargs):
            self.assertNotEqual(Path(path), foreign.parent / 'active.json')
            return original_read(path, *args, **kwargs)
        with mock.patch.object(support, 'read_object', side_effect=observe):
            _, files = self.export()
        self.assertEqual(json.loads(files['diagnostic.json'])['signingRecoveryUnavailable']['reason'], 'path-binding-mismatch')
        self.assertNotIn('FOREIGN SECRET', '\n'.join(files.values()))
    def test_normal_install_and_stale_attempt_have_no_recovery_diagnostic(self):
        name, adb, log = self.install_log()
        _, files = self.export()
        self.assertNotIn('signingRecoveryUnavailable', json.loads(files['diagnostic.json']))
        log.write_text('Original recovery data is retained at ' + str(self.root / 'device/signing-recovery/fixture-headset' / ('a' * 32))
                       + '. Retry with the same APK and headset.\nADB log: ' + str(adb) + '\n')
        os.utime(adb, (99, 99))
        _, files = self.export()
        self.assertNotIn('signingRecoveryUnavailable', json.loads(files['diagnostic.json']))
    def test_support_excludes_stale_foreign_or_linked_install_logs(self):
        name, path, log = self.install_log()
        os.utime(path, (99, 99))
        self.assertEqual(support.installation_logs(self.root, self.directory, self.state), [])
        os.utime(path, (101, 101))
        log.write_text('ADB log: ' + str(path.with_name('other.log')) + '\n')
        self.assertEqual(support.installation_logs(self.root, self.directory, self.state), [])
        foreign = self.root / name; foreign.write_text('PRIVATE FOREIGN LOG')
        log.write_text('ADB log: ' + str(foreign) + '\n')
        self.assertEqual(support.installation_logs(self.root, self.directory, self.state), [])
        path.unlink(); path.symlink_to(foreign)
        log.write_text('ADB log: ' + str(path) + '\n')
        _, files = self.export()
        self.assertNotIn('device/' + name, files)
    def test_support_prioritizes_current_install_failure_over_historical_build_logs(self):
        name, path, log = self.install_log()
        build_logs = self.root / 'build/logs'; build_logs.mkdir(parents=True)
        for index in range(support.MAX_FILES + 5):
            other = build_logs / ('unity-build-history-' + str(index) + '.log')
            other.write_text('old build')
            os.utime(other, (2000000000, 2000000000))
        _, files = self.export()
        self.assertIn('device/' + name, files)
        self.assertIn('wizard/install.log', files)
        self.assertLessEqual(len(files), support.MAX_FILES + 1)
    def test_memory_retry_metrics_and_archived_failure_survive_support_export(self):
        memory = {'compilerProfile': 'release-line-tables', 'jobs': 4, 'attempt': 2,
                  'reason': 'retry-ready', 'resources': {'commitHeadroomBytes': 8 * 1024 ** 3}}
        self.state['events'] = [{'code': 'stage_progress', 'stage': 'build',
                                'parameters': {'phase': 'native-memory-retry', 'nativeMemory': memory}}]
        write_json(self.directory / 'state.json', self.state)
        log = self.root / 'build/logs/unity-build-abcdef.memory-attempt-1.log'
        log.parent.mkdir(parents=True)
        log.write_text('LLVM ERROR: out of memory\nAuthorization: Bearer private-token\n')
        _, files = self.export()
        parameters = json.loads(files['diagnostic.json'])['events'][0]['parameters']
        self.assertEqual(parameters['nativeMemory'], memory)
        archived = files['build/' + log.name]
        self.assertIn('LLVM ERROR: out of memory', archived)
        self.assertNotIn('private-token', archived)
    def test_preparation_frontier_is_exported_without_asset_or_profile_paths(self):
        key = 'a' * 64
        folder = self.root / 'build/cache/prepare-resume' / key
        value = {'schema': 1, 'owner': 'Quest preparation substage journal',
                 'project': 'projects/' + key, 'inputKey': 'c' * 64, 'recipe': 1, 'target': 'game',
                 'steps': [{'name': 'campaign-shaders', 'operation': 'graphics',
                            'outputs': [{'path': 'PRIVATE ASSET', 'profile': 'SecretName'}]}],
                 'pending': {'name': 'compiler-adapters', 'operation': 'compiler-adapters', 'undo': ['PRIVATE PATH']},
                 'profile': 'SecretName', 'metadataRefresh': {'before': 'PRIVATE CONTENT'}}
        write_json(folder / 'journal.json', value)
        write_json(folder / 'metadata-refresh.json', {'private': 'PRIVATE CONTENT'})
        before = (folder / 'journal.json').read_bytes()
        _, files = self.export()
        rows = json.loads(files['diagnostic.json'])['preparationReceipts']
        self.assertEqual(rows[0]['completedCount'], 1)
        self.assertEqual(rows[0]['completed'][0]['outputCount'], 1)
        self.assertEqual(rows[0]['pending']['name'], 'compiler-adapters')
        self.assertTrue(rows[0]['metadataRefreshPending'])
        self.assertFalse(rows[0]['contentVerified'])
        self.assertNotIn('PRIVATE', '\n'.join(files.values()))
        self.assertEqual(before, (folder / 'journal.json').read_bytes())
    def test_preparation_summary_is_bounded_and_skips_linked_or_unowned_journals(self):
        cache = self.root / 'build/cache/prepare-resume'
        write_json(cache / ('a' * 64) / 'journal.json', {'owner': 'unknown', 'profile': 'PRIVATE'})
        write_json(cache / 'not-owned' / 'journal.json', {'profile': 'PRIVATE'})
        stats = support.preparation_receipt_stats(self.root / 'build')
        self.assertEqual(len(stats), 1)
        self.assertEqual(stats[0]['journalStatus'], 'missing-or-unowned')
        self.assertNotIn('PRIVATE', json.dumps(stats))
        with (cache / ('a' * 64) / 'journal.json').open('wb') as stream:
            stream.truncate(support.MAX_PREPARATION_JOURNAL_BYTES + 1)
        self.assertEqual(support.preparation_receipt_stats(self.root / 'build')[0]['journalStatus'], 'unreadable-or-oversized')
    def test_compute_failure_exports_actual_owner_ids_without_programs_or_asset_reads(self):
        key = 'a' * 64
        project = self.root / 'build/projects' / key
        write_json(self.root / 'build/cache/prepare-resume' / key / 'journal.json', {
            'schema': 1, 'owner': 'Quest preparation substage journal', 'project': 'projects/' + key,
            'target': 'game', 'steps': [], 'pending': None})
        manifest = project / 'Assets/QuestOriginalCampaign/campaign-computes.json'
        owner = {'name': 'EyeHistogram', 'guid': '43dced0ca11f5ab4c850ff0e55f341a3', 'classId': 72,
                 'localFileId': 7200000, 'sourceSha256': 'c' * 64, 'profile': 'SecretName',
                 'source': 'PRIVATE COMPUTE PROGRAM', 'kernels': [{'name': 'KEyeHistogram',
                    'originalDxbcSha256': 'd' * 64, 'threadGroups': [16, 16, 1], 'code': 'PRIVATE DXBC'}]}
        write_json(manifest, {'schema': 1, 'shaders': [owner], 'originalInputs': {'PRIVATE ASSET': 'private'}})
        raw = manifest.read_bytes()
        actual_open = Path.open
        def bounded_open(path, *args, **kwargs):
            if project in path.parents and path != manifest:
                self.fail('Support must not open source programs or metadata assets')
            return actual_open(path, *args, **kwargs)
        with mock.patch.object(Path, 'open', bounded_open): _, files = self.export()
        summary = json.loads(files['diagnostic.json'])['computeContracts'][0]
        self.assertEqual(summary['manifestStatus'], 'observed')
        self.assertFalse(summary['contentVerified'])
        observed = summary['shaders'][0]
        self.assertEqual(observed['guid'], owner['guid'])
        self.assertEqual(observed['kernelCount'], 1)
        self.assertEqual(observed['kernels'][0]['originalDxbcSha256'], 'd' * 64)
        self.assertEqual(observed['kernels'][0]['threadGroups'], [16, 16, 1])
        self.assertNotIn('PRIVATE', '\n'.join(files.values()))
        self.assertNotIn('SecretName', '\n'.join(files.values()))
        self.assertEqual(raw, manifest.read_bytes())
    def test_compute_diagnostic_rejects_links_large_and_unowned_manifests_without_breaking_export(self):
        key = 'a' * 64
        build = self.root / 'build'
        observed = [{'workspaceKey': key, 'target': 'game', 'journalStatus': 'observed'}]
        path = build / 'projects' / key / 'Assets/QuestOriginalCampaign/campaign-computes.json'
        path.parent.mkdir(parents=True)
        private = self.root / 'private.json'; private.write_text('PRIVATE')
        path.symlink_to(private)
        self.assertEqual(support.compute_contract_stats(build, observed)[0]['manifestStatus'], 'unreadable-or-oversized')
        path.unlink()
        with path.open('wb') as stream: stream.truncate(1048577)
        with mock.patch.object(Path, 'open', side_effect=AssertionError('Oversized manifest must not be opened')):
            self.assertEqual(support.compute_contract_stats(build, observed)[0]['manifestStatus'], 'unreadable-or-oversized')
            self.assertEqual(support.compute_contract_stats(build, [{'workspaceKey': '../private', 'target': 'game',
                'journalStatus': 'observed'}, {'workspaceKey': key, 'target': 'game', 'journalStatus': 'unowned'}]), [])
        path.write_text('{broken manifest')
        self.assertEqual(support.compute_contract_stats(build, observed)[0]['manifestStatus'], 'unreadable-or-oversized')
    def test_compute_diagnostic_bounds_owner_and_kernel_counts_and_redacts_names(self):
        key = 'a' * 64
        build = self.root / 'build'
        owner = {'name': 'SecretName', 'guid': 'b' * 32, 'sourceSha256': 'c' * 64,
                 'kernels': [{'name': 'k' * 200, 'threadGroups': [16, 16, 1]}] * 50}
        write_json(build / 'projects' / key / 'Assets/QuestOriginalCampaign/campaign-computes.json',
                   {'schema': 1, 'shaders': [owner] * 20})
        rows = support.compute_contract_stats(build, [{'workspaceKey': key, 'target': 'game', 'journalStatus': 'observed'}],
                                              [('SecretName', '<profile>')])
        self.assertEqual(rows[0]['shaderCount'], 20)
        self.assertEqual(len(rows[0]['shaders']), 13)
        self.assertEqual(len(rows[0]['shaders'][0]['kernels']), 36)
        self.assertEqual(rows[0]['shaders'][0]['name'], '<profile>')
        self.assertEqual(len(rows[0]['shaders'][0]['kernels'][0]['name']), 128)
    def test_targeted_repairs_export_bounded_history_and_pending_state(self):
        key = 'a' * 64
        folder = self.root / 'build/cache/prepare-resume' / key
        history = [{'path': 'Assets/Scenes/Scene' + str(index) + '/LightingData.asset',
                    'owner': 'script-orders', 'sha256': 'b' * 64,
                    'recipe': 'retained-original-recovery'} for index in range(40)]
        value = {'schema': 1, 'owner': 'Quest preparation substage journal',
                 'project': 'projects/' + key, 'inputKey': 'c' * 64, 'recipe': 1, 'target': 'game',
                 'steps': [], 'pending': None, 'fileRepairs': {'count': 40, 'recent': history}}
        write_json(folder / 'journal.json', value)
        write_json(folder / 'repair.json', {'private': 'PRIVATE TRANSACTION DATA'})
        before = (folder / 'journal.json').read_bytes()
        _, files = self.export()
        row = json.loads(files['diagnostic.json'])['preparationReceipts'][0]
        self.assertEqual(row['fileRepairs'], {'count': 40, 'recent': history[-32:]})
        self.assertTrue(row['fileRepairPending'])
        self.assertNotIn('PRIVATE TRANSACTION DATA', '\n'.join(files.values()))
        self.assertEqual((folder / 'journal.json').read_bytes(), before)
    def test_large_completed_preparation_journal_exports_only_compact_frontier(self):
        key = 'a' * 64
        folder = self.root / 'build/cache/prepare-resume' / key; folder.mkdir(parents=True)
        journal = folder / 'journal.json'
        names = ('base-project', 'post-effects', 'loading-resources', 'startup-movies',
                 'native-sprites', 'loading-sprite', 'startup-audio', 'ui-recipes',
                 'startup-ui', 'startup-blur', 'dlc-selection', 'file-extras',
                 'native-runtime', 'bundled-audio', 'native-cubemaps',
                 'ordinary-texture-audit', 'native-texture2d', 'campaign-compute',
                 'campaign-shaders', 'startup-archive', 'archive-cleanup',
                 'package-settings', 'mod-resource-banks', 'compiler-contracts',
                 'case-paths', 'script-orders', 'final-settings')
        # A legitimate journal grows through ordinary per-file output records,
        # rather than one oversized artificial padding/string field.
        header = {'schema': 1, 'owner': 'Quest preparation substage journal',
                  'project': 'projects/' + key, 'inputKey': 'c' * 64,
                  'recipe': 1, 'target': 'game', 'pending': None}
        count = 100000
        with journal.open('w', encoding='utf-8') as stream:
            stream.write(json.dumps(header)[:-1] + ', "steps": [')
            stream.write('{"name":"base-project","operation":"project-files","outputs":[')
            for index in range(count):
                if index: stream.write(',')
                stream.write(json.dumps({'path': 'Assets/PRIVATE OWNED ORIGINAL/' + 'x' * 120 + '/' + str(index) + '.asset',
                                         'size': 1234, 'sha256': 'd' * 64}))
            stream.write(']}')
            for name in names[1:]:
                stream.write(',' + json.dumps({'name': name, 'operation': 'preparation-contracts', 'outputs': []}))
            stream.write(']}')
        before = journal.stat()
        self.assertGreater(before.st_size, 16 * 1048576)
        self.assertLess(before.st_size, support.MAX_PREPARATION_JOURNAL_BYTES)
        _, files = self.export()
        rows = json.loads(files['diagnostic.json'])['preparationReceipts']
        self.assertEqual(rows[0]['journalStatus'], 'observed')
        self.assertEqual(rows[0]['completedCount'], 27)
        self.assertEqual(rows[0]['completed'][0]['outputCount'], count)
        self.assertEqual(rows[0]['completed'][-1], {'name': 'final-settings', 'operation': 'preparation-contracts', 'outputCount': 0})
        self.assertIsNone(rows[0]['pending'])
        self.assertFalse(rows[0]['contentVerified'])
        self.assertLess(len(json.dumps(rows).encode()), 4096)
        self.assertNotIn('PRIVATE', '\n'.join(files.values()))
        self.assertFalse(any('journal.json' in name for name in files))
        self.assertEqual((before.st_size, before.st_mtime_ns), (journal.stat().st_size, journal.stat().st_mtime_ns))
    def test_preparation_journal_over_64_mib_is_rejected_before_open_and_generic_caps_stay(self):
        journal = self.root / 'build/cache/prepare-resume' / ('a' * 64) / 'journal.json'
        journal.parent.mkdir(parents=True)
        with journal.open('wb') as stream: stream.truncate(64 * 1048576 + 1)
        self.assertEqual(support.MAX_PREPARATION_JOURNAL_BYTES, 64 * 1048576)
        with mock.patch.object(Path, 'open', side_effect=AssertionError('Oversized journal must not be opened')):
            self.assertEqual(support.preparation_receipt_stats(self.root / 'build')[0]['journalStatus'], 'unreadable-or-oversized')
        generic = self.root / 'generic.json'
        with generic.open('wb') as stream: stream.truncate(4 * 1048576 + 1)
        with mock.patch.object(Path, 'open', side_effect=AssertionError('Generic metadata cap must not expand')):
            with self.assertRaises(BuildError): support.read_object(generic)
        self.assertEqual((support.MAX_FILE_BYTES, support.MAX_TOTAL_BYTES, support.MAX_FILES), (2 * 1048576, 24 * 1048576, 64))
    def test_preparation_journal_read_stays_bounded_when_file_grows_after_stat(self):
        journal = self.root / 'build/cache/prepare-resume' / ('a' * 64) / 'journal.json'
        journal.parent.mkdir(parents=True); journal.write_bytes(b'{}')
        reads = []
        class Growing(io.BytesIO):
            def read(self, size=-1):
                reads.append(size); return super().read(size)
        with mock.patch.object(support, 'MAX_PREPARATION_JOURNAL_BYTES', 128), \
                mock.patch.object(Path, 'open', return_value=Growing(b' ' * 1024)):
            rows = support.preparation_receipt_stats(self.root / 'build')
        self.assertEqual(rows[0]['journalStatus'], 'unreadable-or-oversized')
        self.assertEqual(reads, [129])
    @unittest.skipIf(os.name == 'nt', 'symlink privilege varies')
    def test_preparation_linked_journal_and_linked_workspace_are_never_followed(self):
        cache = self.root / 'build/cache/prepare-resume'; folder = cache / ('a' * 64)
        folder.mkdir(parents=True)
        private = self.root / 'private.json'; private.write_text('{"profile":"PRIVATE"}')
        (folder / 'journal.json').symlink_to(private)
        (cache / ('b' * 64)).symlink_to(folder, target_is_directory=True)
        with mock.patch.object(Path, 'open', side_effect=AssertionError('Linked journal must not be read')):
            rows = support.preparation_receipt_stats(self.root / 'build')
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]['journalStatus'], 'unreadable-or-oversized')
    def test_preparation_malformed_and_unowned_documents_reject_frontier(self):
        journal = self.root / 'build/cache/prepare-resume' / ('a' * 64) / 'journal.json'
        journal.parent.mkdir(parents=True)
        for value, expected in (('{broken', 'unreadable-or-oversized'), ('[]', 'missing-or-unowned'),
                                ('{"schema":1,"owner":"unknown","profile":"PRIVATE"}', 'missing-or-unowned')):
            with self.subTest(value=value):
                journal.write_text(value)
                rows = support.preparation_receipt_stats(self.root / 'build')
                self.assertEqual(rows[0]['journalStatus'], expected)
                self.assertNotIn('PRIVATE', json.dumps(rows))
    def test_cleanup_operation_summaries_survive_cache_deletion_in_support_archive(self):
        logs = self.root / 'logs'
        logs.mkdir()
        (logs / 'storage-cleanup.log').write_text('{"event":"completed","freedBytes":17754574362,"deletedFiles":5228}\n')
        (logs / 'storage-cleanup.previous.log').write_text('{"event":"preview","mode":"duplicates"}\n')
        (logs / 'unrelated-private.txt').write_text('Do not export arbitrary local files')
        _, files = self.export()
        selected = {name: raw for name, raw in files.items() if 'storage-cleanup' in name}
        self.assertEqual(len(selected), 2)
        self.assertTrue(any('17754574362' in raw for raw in selected.values()))
        self.assertFalse(any('unrelated-private' in name for name in files))

    def test_linux_hub_window_and_install_location_failures_are_exported(self):
        logs = self.directory / 'logs'
        (logs / 'unity-hub-window.log').write_text('error while loading shared libraries: libgtk-3.so.0\naccess_token="private"\nunityhub://login?code=private-oauth-code&state=private-state\n')
        (logs / 'unity-install-path.log').write_text('install-path failed\n')
        (logs / 'unity-protocol-register.log').write_text('desktop handler registration failed\n')
        _, files = self.export()
        self.assertIn('libgtk-3.so.0', files['wizard/unity-hub-window.log'])
        self.assertIn('install-path failed', files['wizard/unity-install-path.log'])
        self.assertIn('desktop handler registration failed', files['wizard/unity-protocol-register.log'])
        self.assertNotIn('private', '\n'.join(files.values()))
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

    def test_failed_delivery_log_precedes_hundreds_of_newer_unity_logs_with_same_bounds(self):
        logs = self.root / 'build/logs'; logs.mkdir(parents=True)
        failed = logs / 'update-align.log'
        failed.write_text("Unable to open 'GloomhavenVR-Quest.capable.apk' as zip archive.\n"
                          "GHVR_QUEST_SIGNING_PASSWORD=PRIVATE PASSWORD\n"
                          "-----BEGIN PRIVATE KEY-----\nPRIVATE KEY BYTES\n-----END PRIVATE KEY-----\n")
        os.utime(failed, (1, 1))
        for index in range(256):
            (logs / ('unity-build-' + f'{index:012x}' + '.log')).write_text('Historic Unity context\n')
        (self.directory / 'logs/build.log').write_text('Quest builder: zipalign.exe exited with 1\n')
        (self.directory / 'logs/progress.log').write_text('{"code":"build_tool_failed"}\n')
        write_json(self.root / 'build/last-failure.json', {
            'schema': 1, 'stage': 'build', 'message': 'zipalign.exe exited with 1; inspect ' + str(failed)})
        before = failed.read_bytes()
        result, rows = self.export()
        meta = json.loads(rows['diagnostic.json'])
        self.assertEqual((support.MAX_FILE_BYTES, support.MAX_TOTAL_BYTES, support.MAX_FILES), (2 * 1048576, 24 * 1048576, 64))
        self.assertEqual(len(meta['files']), 64)
        self.assertEqual(result['fileCount'], 65)
        self.assertEqual(meta['files'][0]['name'], 'build/update-align.log')
        self.assertIn('as zip archive', rows['build/update-align.log'])
        self.assertIn('wizard/build.log', rows); self.assertIn('wizard/progress.log', rows)
        self.assertTrue(meta['omitted'])
        self.assertLessEqual(sum(row['exportedBytes'] for row in meta['files']), support.MAX_TOTAL_BYTES)
        self.assertNotIn('PRIVATE', '\n'.join(rows.values()))
        self.assertEqual(before, failed.read_bytes())

    def test_known_update_logs_are_selected_without_accepting_account_or_signing_files(self):
        logs = self.root / 'build/logs'; logs.mkdir(parents=True)
        for name in support.UPDATE_LOGS: (logs / name).write_text('Error: current tool context\npassword: DO NOT LEAK\n')
        for name in ('update-profile.log', 'update-private-key.log', 'update-signing.json', 'update-sign.memory-attempt-1.log'):
            (logs / name).write_text('DO NOT SHIP')
        _, rows = self.export()
        self.assertEqual({name for name in rows if name.startswith('build/')}, {'build/' + name for name in support.UPDATE_LOGS})
        self.assertNotIn('DO NOT', '\n'.join(rows.values()))

    def test_failed_command_priority_rejects_unowned_and_traversal_paths(self):
        logs = self.root / 'build/logs'; logs.mkdir(parents=True)
        for name in ('update-align.log', 'unity-build-abcdef.log', 'update-profile.log'):
            message = 'tool exited with 1; inspect ' + str(logs / name)
            selected = support.failed_command_log(self.root / 'build', {'message': message})
            self.assertEqual(selected, 'build/' + name if name != 'update-profile.log' else None)
            for unsafe in (self.root / 'private' / name, logs / '..' / 'private' / name):
                self.assertIsNone(support.failed_command_log(self.root / 'build', {'message': 'tool exited; inspect ' + str(unsafe)}))
        cause = 'tool exited; inspect ' + str(logs / 'update-align.log') + '\nUnable to open archive'
        self.assertEqual(support.failed_command_log(self.root / 'build', {'message': cause}), 'build/update-align.log')

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
