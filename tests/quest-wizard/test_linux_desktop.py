"""Linux desktop discovery, path selection and host qualification boundaries."""
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import host_paths
import qualification
import server
import wizard
from state import Store, WizardError


class LinuxDesktopTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='quest linux spaces ')
        self.root = Path(self.temp.name)
        self.home = self.root / 'home'; self.home.mkdir()
        self.workspace = self.root / 'workspace'; self.workspace.mkdir()
    def tearDown(self):
        self.temp.cleanup()
    def editor(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('Editor fixture')
        return path

    def test_linux_editor_discovery_includes_hub_owned_tools_and_configured_editor(self):
        installed = self.editor(self.home / 'Unity/Hub/Editor/2021.3.5f1/Editor/Unity')
        tools = self.editor(self.workspace / 'tools/unity-editors/2021.3.5f1/Editor/Unity')
        configured = self.editor(self.root / 'custom version/Editor/Unity')
        ndk = installed.parent / 'Data/PlaybackEngines/AndroidPlayer/NDK/source.properties'
        ndk.parent.mkdir(parents=True); ndk.write_text('Android module fixture')
        hub = self.editor(self.workspace / 'tools/unity-hub-3.22.2/UnityHub.AppImage'); hub.chmod(0o755)
        registry = self.home / '.config/UnityHub/editors-v2.json'
        registry.parent.mkdir(parents=True); registry.write_text(json.dumps({'editors': [{'location': str(configured.parent.parent)}]}))
        with mock.patch.object(Path, 'home', return_value=self.home), \
             mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(discovery.shutil, 'which', return_value=None):
            editors, hubs = discovery.unity_paths(self.workspace)
        self.assertEqual({row['path'] for row in editors}, {str(installed), str(tools), str(configured)})
        self.assertTrue(next(row for row in editors if row['path'] == str(installed))['androidSupport'])
        self.assertEqual(hubs, [str(hub)])

    def test_linux_paths_include_xdg_config_and_explicit_editor_without_full_disk_scan(self):
        selected = self.editor(self.root / 'own editor/Editor/Unity')
        config = self.root / 'xdg/UnityHub/editors-v2.json'; config.parent.mkdir(parents=True)
        config.write_text(json.dumps([{'location': str(selected)}]))
        with mock.patch.object(Path, 'home', return_value=self.home), \
             mock.patch.dict(os.environ, {'XDG_CONFIG_HOME': str(config.parent.parent), 'UNITY_EDITOR_PATH': str(selected)}, clear=True), \
             mock.patch.object(discovery.shutil, 'which', return_value=None):
            editors, _ = discovery.unity_paths(self.workspace)
        self.assertEqual([row['path'] for row in editors], [str(selected)])

    def picker(self, kind, result, name='zenity'):
        with mock.patch.object(host_paths, 'linux_picker', return_value=(name, '/usr/bin/' + name)), \
             mock.patch.object(host_paths.subprocess, 'run', return_value=result) as run:
            selected = host_paths.browse_linux(kind)
        return selected, run.call_args

    def test_picker_returns_unicode_path_as_one_argument_without_shell(self):
        path = str(self.root / 'Spiel ä $(touch NEVER)')
        selected, call = self.picker('game', SimpleNamespace(returncode=0, stdout=path + '\n'))
        self.assertEqual(selected, path)
        self.assertIn('--directory', call.args[0])
        self.assertNotIn('shell', call.kwargs)

    def test_picker_normalizes_editor_folder_and_kde_apk_file(self):
        editor = self.editor(self.root / 'Unity 2021/Editor/Unity')
        result, _ = self.picker('unity', SimpleNamespace(returncode=0, stdout=str(editor.parent.parent) + '\n'))
        self.assertEqual(result, str(editor))
        result, call = self.picker('apk', SimpleNamespace(returncode=0, stdout=str(self.root / 'Owned.apk') + '\n'), name='kdialog')
        self.assertIn('--getopenfilename', call.args[0]); self.assertTrue(result.endswith('/Owned.apk'))

    def test_picker_cancel_failure_and_invalid_outputs_are_distinct(self):
        self.assertIsNone(self.picker('game', SimpleNamespace(returncode=1, stdout=''))[0])
        for result in (SimpleNamespace(returncode=2, stdout=''), SimpleNamespace(returncode=0, stdout='relative/path'),
                       SimpleNamespace(returncode=0, stdout='/one\n/two\n')):
            with self.assertRaises(WizardError) as error: self.picker('game', result)
            self.assertEqual(error.exception.code, 'browse_failed')

    def test_unavailable_picker_has_manual_path_guidance(self):
        with mock.patch.object(host_paths, 'linux_picker', return_value=None):
            with self.assertRaises(WizardError) as error: server.browse('game')
        self.assertIn('Pfad', error.exception.message['de'])

    def test_headless_linux_does_not_offer_unusable_browse_button(self):
        with mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(host_paths.shutil, 'which', return_value='/usr/bin/zenity'):
            self.assertFalse(host_paths.browse_available())

    def test_linux_is_supported_with_space_guard_and_arm_host_rejected(self):
        result = qualification.qualify(self.workspace, system='Linux', machine='x86_64', free_bytes=200 * qualification.GIB)
        self.assertTrue(result['supportedAutomation']); self.assertTrue(result['buildSpaceCheckPassed'])
        with self.assertRaises(WizardError) as error:
            qualification.qualify(self.workspace, system='Linux', machine='x86_64', free_bytes=0)
        self.assertEqual(error.exception.code, 'workspace_space_low')
        with self.assertRaises(WizardError) as error:
            qualification.qualify(self.workspace, system='Linux', machine='aarch64', free_bytes=200 * qualification.GIB)
        self.assertEqual(error.exception.code, 'linux_x64_required')
        with self.assertRaises(WizardError):
            qualification.qualify(self.workspace, system='Darwin', machine='x86_64', free_bytes=200 * qualification.GIB)

    def test_real_discovery_exposes_linux_host_and_retains_sessions(self):
        store = Store(self.workspace)
        with mock.patch.object(Path, 'home', return_value=self.home), \
             mock.patch.dict(os.environ, {}, clear=True):
            result = discovery.discover(ROOT, store)
        self.assertEqual(result['host']['system'], 'Linux')
        self.assertFalse(result['capabilities']['browse'])
        self.assertGreater(result['modSource']['modBuild'], 0)

    def test_added_linux_tools_keep_the_delivered_windows_stage_key(self):
        store = Store(self.workspace)
        state = {'choices': {'mode': 'build'}, 'completed': {}}
        # Captured from the actual B647/085e037ce Windows tool recipes before
        # Linux was added. It must remain reusable after replacing the ZIP.
        with mock.patch.object(wizard, 'os', SimpleNamespace(name='nt')):
            key = wizard.Engine(store).key(state, 'tools')
        self.assertEqual(key, 'a154873e599e749617c3da1a849331489b193ccac24a09b803596b1eacd65156')
        self.assertNotEqual(wizard.Engine(store).key(state, 'tools'), key)

    def test_linux_steam_dlc_request_precedes_tool_downloads_and_unity_import(self):
        store = Store(self.workspace)
        saved = store.create(wizard.choices({'gameRoot': str(self.root / 'Owned Game'), 'provider': 'steam'}))
        engine = wizard.Engine(store)
        with mock.patch.object(engine, 'qualify'), mock.patch.object(wizard.provision, 'tools') as tools:
            with self.assertRaises(WizardError) as error: engine.stage_tools(saved, mock.Mock())
            self.assertEqual(error.exception.code, 'linux_dlc_required'); tools.assert_not_called()
            saved['choices']['ownedDlc'] = []
            engine.stage_tools(saved, mock.Mock()); tools.assert_called_once()


if __name__ == '__main__': unittest.main()
