"""Linux tool extraction and orchestration; fixtures execute real bounded children."""
import io
import json
import os
from pathlib import Path
import stat
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import provision
import unity_setup
from state import WizardError


class FixtureStore:
    def __init__(self, root): self.root = root; self.events = []
    def session_dir(self, session):
        target = self.root / 'sessions' / session; (target / 'logs').mkdir(parents=True, exist_ok=True); return target
    def check_cancel(self, session): pass
    def progress(self, *args): self.events.append(args)
    def operation(self, *args, **kwargs): self.events.append((args, kwargs))
    def record(self, *args, **kwargs): self.events.append((args, kwargs))


class ActualSupervisor:
    def run(self, argv, log, **kwargs):
        import subprocess
        result = subprocess.run([str(value) for value in argv], stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=5, check=True)
        Path(log).write_bytes(result.stdout)


class LinuxToolsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def archive(self, rows):
        archive = self.root / 'tool.tar.gz'
        with tarfile.open(archive, 'w:gz') as package:
            for name, value, kind in rows:
                row = tarfile.TarInfo(name); row.mode = 0o755
                if kind == 'file':
                    row.size = len(value); package.addfile(row, io.BytesIO(value))
                else:
                    row.type = tarfile.SYMTYPE if kind == 'symlink' else tarfile.LNKTYPE
                    row.linkname = value; package.addfile(row)
        return archive

    def spec(self, executable='bin/tool'):
        return {'archive': 'tar.gz', 'executable': executable, 'version': 'fixture'}

    def test_tar_preserves_executable_internal_symlink_and_independent_hardlink(self):
        archive = self.archive([('./bin/tool', b'#!/bin/sh\necho ready\n', 'file'),
                                ('bin/alias', 'tool', 'symlink'), ('bin/copy', 'bin/tool', 'hardlink')])
        target = provision.extract_owned(archive, self.root / 'owned', self.spec())
        self.assertTrue(target.stat().st_mode & stat.S_IXUSR)
        alias = target.with_name('alias'); self.assertTrue(alias.is_symlink()); self.assertEqual(alias.read_bytes(), target.read_bytes())
        self.assertNotEqual(target.stat().st_ino, target.with_name('copy').stat().st_ino)
        self.assertEqual(json.loads((target.parent.parent / 'wizard-tool.json').read_text())['links'], {'bin/alias': 'tool'})
        target.write_bytes(b'corrupt')
        provision.extract_owned(archive, self.root / 'owned', self.spec())
        self.assertIn(b'ready', target.read_bytes())

    def test_tar_cancelled_extract_can_resume_completed_files_and_links(self):
        archive = self.archive([('bin/tool', b'complete', 'file'), ('bin/second', b'remaining', 'file'), ('bin/alias', 'second', 'symlink')])
        calls = 0
        def cancel():
            nonlocal calls
            calls += 1
            if calls == 4: raise RuntimeError('fixture cancellation')
        with self.assertRaises(RuntimeError): provision.extract_owned(archive, self.root / 'owned', self.spec(), cancel)
        self.assertFalse(json.loads((self.root / 'owned/wizard-tool.json').read_text())['complete'])
        provision.extract_owned(archive, self.root / 'owned', self.spec())
        self.assertEqual((self.root / 'owned/bin/alias').read_bytes(), b'remaining')

    def test_tar_rejects_escape_cycles_dangling_devices_duplicate_and_linked_parents(self):
        fixtures = [
            [('bin/tool', b'ok', 'file'), ('../escape', b'bad', 'file')],
            [('bin/tool', b'ok', 'file'), ('bin/link', '../../outside', 'symlink')],
            [('bin/tool', b'ok', 'file'), ('bin/link', '/etc/passwd', 'symlink')],
            [('bin/tool', b'ok', 'file'), ('bin/one', 'two', 'symlink'), ('bin/two', 'one', 'symlink')],
            [('bin/tool', b'ok', 'file'), ('bin/link', 'missing', 'symlink')],
            [('bin/tool', b'ok', 'file'), ('bin/tool', b'bad', 'file')],
            [('bin/tool', b'ok', 'file'), ('link', 'bin', 'symlink'), ('link/file', b'bad', 'file')],
        ]
        for index, rows in enumerate(fixtures):
            with self.subTest(index=index):
                archive = self.archive(rows)
                with self.assertRaises(WizardError): provision.extract_owned(archive, self.root / ('owned' + str(index)), self.spec())
        self.assertFalse((self.root / 'escape').exists())

    def test_tar_refuses_existing_symlink_parent_without_touching_outside(self):
        archive = self.archive([('bin/tool', b'ok', 'file')]); root = self.root / 'owned'
        root.mkdir(); (root / 'wizard-tool.json').write_text(json.dumps({'key': provision.value_hash(self.spec())}))
        outside = self.root / 'outside'; outside.mkdir(); (root / 'bin').symlink_to(outside, target_is_directory=True)
        with self.assertRaises(WizardError): provision.extract_owned(archive, root, self.spec())
        self.assertEqual(list(outside.iterdir()), [])

    def test_zip_restores_linux_executable_permissions(self):
        archive = self.root / 'tool.zip'
        with zipfile.ZipFile(archive, 'w') as package:
            row = zipfile.ZipInfo('bin/tool'); row.external_attr = (stat.S_IFREG | 0o755) << 16
            package.writestr(row, b'#!/bin/sh\necho ready\n')
        spec = {'executable': 'bin/tool'}
        target = provision.extract_owned(archive, self.root / 'owned', spec)
        self.assertTrue(os.access(target, os.X_OK)); target.chmod(0o600)
        provision.extract_owned(archive, self.root / 'owned', spec)
        self.assertTrue(os.access(target, os.X_OK))

    def test_host_keys_select_native_linux_specs_and_preserve_windows_specs(self):
        with mock.patch('provision.platform.machine', return_value='x86_64'):
            self.assertEqual(provision.host_key(), 'linux-x64')
            self.assertEqual(provision.spec_for('dotnet8')['executable'], 'dotnet')
            with mock.patch('provision.os.name', 'nt'):
                self.assertEqual(provision.host_key(), 'windows-x64')
                self.assertIs(provision.spec_for('dotnet8'), provision.LOCK['dotnet8'])
                self.assertEqual(provision.spec_for('apkJdk')['executable'], 'jdk-17.0.16+8/bin/java.exe')
        with mock.patch('provision.platform.machine', return_value='aarch64'):
            with self.assertRaises(WizardError) as error: provision.host_key()
            self.assertEqual(error.exception.code, 'host_architecture')

    def test_tools_use_verified_system_git_without_claiming_it_is_pinned(self):
        store = FixtureStore(self.root / 'owner'); actual = ActualSupervisor()
        def sdk(name, _store, _session):
            target = self.root / name; target.write_text('#!/bin/sh\necho ' + provision.spec_for(name)['version'] + '\n'); target.chmod(0o755)
            return target, []
        # System Git is actually executed; SDK fixtures are real bounded shell children.
        with mock.patch('provision._provision_tool', side_effect=sdk): outputs, details = provision.tools(store, 'run', actual)
        receipt = json.loads(outputs[0].read_text())
        self.assertEqual(receipt['pins']['git']['kind'], 'system-unpinned')
        self.assertEqual(receipt['host'], 'linux-x64')
        self.assertNotIn(Path(details['git']), outputs)
        self.assertTrue(any(row.name == 'git-version.log' for row in outputs))

    def test_missing_linux_git_stops_before_tool_download_with_actionable_error(self):
        with mock.patch('provision.shutil.which', return_value=None), mock.patch('provision.download') as download:
            with self.assertRaises(WizardError) as error: provision.tools(FixtureStore(self.root), 'run', ActualSupervisor())
        self.assertEqual(error.exception.code, 'linux_git_missing'); download.assert_not_called()

    def test_profile_tools_use_linux_java_keytool_and_zipalign_names(self):
        store = FixtureStore(self.root / 'owner')
        def signing(name, _store, _session):
            root = store.root / 'tools' / name; root.mkdir(parents=True)
            if name == 'apkJdk':
                root = root / 'jdk/bin'; root.mkdir(parents=True)
                for filename in ('java', 'keytool'):
                    (root / filename).write_text('#!/bin/sh\necho openjdk version 17.0.16 >&2\n'); (root / filename).chmod(0o755)
                return root / 'java', list(root.iterdir())
            (root / 'lib').mkdir(); (root / 'lib/apksigner.jar').write_bytes(b'fixture')
            for filename in ('aapt', 'zipalign'): (root / filename).write_bytes(b'fixture')
            return root / 'aapt', provision._tool_outputs(root)
        with mock.patch('provision._provision_tool', side_effect=signing): outputs, details = provision.profile_tools(store, 'run', ActualSupervisor())
        selected = details['apkTools']
        self.assertTrue(selected['keytool'].endswith('/keytool')); self.assertTrue(selected['zipalign'].endswith('/zipalign'))
        self.assertEqual(json.loads(outputs[0].read_text())['host'], 'linux-x64')

    def test_hub_cli_uses_linux_separator_and_fuse_free_owned_appimage(self):
        command = unity_setup._hub_command('/owned/UnityHub.AppImage', 'help', '--errors')
        self.assertEqual(command, ['/owned/UnityHub.AppImage', '--appimage-extract-and-run', '--headless', 'help', '--errors'])
        self.assertEqual(unity_setup._hub_command('/usr/bin/unityhub', 'help'), ['/usr/bin/unityhub', '--headless', 'help'])
        with mock.patch('unity_setup.os.name', 'nt'):
            self.assertEqual(unity_setup._hub_command('Hub.exe', 'help'), ['Hub.exe', '--', '--headless', 'help'])

    def test_linux_hub_requires_desktop_and_does_not_disable_sandbox(self):
        with mock.patch.dict(os.environ, {}, clear=True):
            with self.assertRaises(WizardError) as error: unity_setup._linux_desktop()
        self.assertEqual(error.exception.code, 'unity_linux_desktop')
        self.assertNotIn('--no-sandbox', unity_setup._hub_command('/owned/UnityHub.AppImage'))

    def test_linux_prepare_downloads_hub_sets_owned_editor_path_and_probes_modules(self):
        import state
        import wizard
        store = state.Store(self.root / 'owner')
        saved = store.create(wizard.choices({'gameRoot': str(self.root / 'Game'), 'acceptUnityTerms': True}))
        editor = store.root / ('tools/unity-editors/' + unity_setup.VERSION + '/Editor/Unity')
        hub = store.root / 'tools/unity-hub-3.22.2/UnityHub.AppImage'
        hub.parent.mkdir(parents=True); hub.write_bytes(b'fixture Hub')
        calls = []
        def discover(workspace=None):
            return ([{'path': str(editor), 'version': unity_setup.VERSION, 'androidSupport': True}] if editor.exists() else [], [])
        class FixtureSupervisor:
            def run(self, argv, log, **kwargs):
                calls.append(list(map(str, argv))); log = Path(log); log.parent.mkdir(parents=True, exist_ok=True)
                if 'help' in argv: log.write_text('editors install install-modules install-path\n')
                elif 'install-path' in argv: log.write_text('Owned installation path selected\n')
                elif 'install' in argv:
                    editor.parent.mkdir(parents=True); editor.write_bytes(b'fixture Editor')
                    support = editor.parent / 'Data/PlaybackEngines/AndroidPlayer'
                    for name in ('NDK/source.properties', 'SDK/platform-tools/adb', 'OpenJDK/bin/java'):
                        path = support / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b'fixture module')
                    log.write_text('Editor and Android modules installed\n')
                elif '-version' in argv: log.write_text(unity_setup.VERSION + '\n')
                else:
                    Path(argv[argv.index('-logFile') + 1]).write_text(unity_setup.PROBE_MARKER + '\n')
                    log.write_text('Fresh licence probe returned\n')
        with mock.patch('unity_setup.discovery.unity_paths', side_effect=discover), \
             mock.patch('unity_setup._linux_desktop'), mock.patch('unity_setup._linux_hub', return_value=str(hub)) as downloaded, \
             mock.patch('unity_setup._wait'):
            outputs, details = unity_setup.prepare(store, saved, FixtureSupervisor())
        downloaded.assert_called_once()
        self.assertEqual(calls[0][:3], [str(hub), '--appimage-extract-and-run', '--headless'])
        self.assertIn('install-path', calls[1]); self.assertEqual(calls[1][-1], str(store.root / 'tools/unity-editors'))
        self.assertIn('install', calls[2]); self.assertIn('android', calls[2]); self.assertIn('--childModules', calls[2])
        self.assertTrue(details['licenseVerified']); self.assertEqual(details['unityEditor'], str(editor))
        self.assertTrue(outputs[0].is_file()); self.assertIn('-nographics', calls[-1])


if __name__ == '__main__': unittest.main()
