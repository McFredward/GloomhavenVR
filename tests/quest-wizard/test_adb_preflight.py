"""A slow endpoint is not offline proof; selected Unity ADB needs no download."""
import io
import json
import os
from pathlib import Path
import ssl
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import urllib.error
import urllib.parse

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import network_preflight as network
import state
import wizard


class Response(io.BytesIO):
    status = 206
    def geturl(self): return 'https://dl-ssl.google.com/android/repository/pinned.zip'


class AdbProbeTests(unittest.TestCase):
    def dependency(self):
        return {'id': 'adb', 'label': 'Android platform-tools for installation',
                'urls': ['https://primary.invalid/pinned.zip', 'https://alternate.invalid/pinned.zip'],
                'local': False, 'conditional': False}

    def test_primary_read_timeout_uses_next_endpoint_without_blame_on_proxy(self):
        opener = Mock(side_effect=[TimeoutError('The read operation timed out'), Response(b'pinned archive byte')])
        result = network.probe(self.dependency(), opener=opener)
        self.assertEqual(result['status'], 'reachable')
        self.assertEqual(result['attempts'][0]['failureKind'], 'timeout')
        self.assertEqual(opener.call_count, 2)
        self.assertNotIn('proxy', json.dumps(result).lower())

    def test_all_short_probes_timeout_is_logged_but_actual_downloader_can_continue(self):
        received = []
        result = network.check([self.dependency()], report=received.append,
            probe_fn=lambda row: network.probe(row, opener=Mock(side_effect=TimeoutError('The read operation timed out'))))
        self.assertEqual(received[0]['status'], 'probe-timeout')
        self.assertEqual(result['results'][0]['status'], 'probe-timeout')
        self.assertTrue(result['results'][0]['downloadRequired'])
        self.assertFalse(result['offlineClosureVerified'])

    def test_real_certificate_refusal_is_not_hidden_as_a_timeout(self):
        error = ssl.SSLCertVerificationError(1, 'fixture certificate refused')
        error.verify_message = 'fixture certificate refused'
        result = network.probe(self.dependency(), opener=Mock(side_effect=urllib.error.URLError(error)))
        self.assertEqual(result['status'], 'unreachable')
        with self.assertRaises(state.WizardError) as caught:
            network.check([self.dependency()], probe_fn=lambda _: result)
        self.assertIn('Zertifikatsprüfung', caught.exception.message['de'])
        self.assertNotIn('Proxy', caught.exception.message['de'])
        self.assertNotIn('Internetverbindung', caught.exception.message['de'])

    def test_mixed_timeout_and_http_refusal_cannot_claim_successful_connectivity(self):
        opener = Mock(side_effect=[TimeoutError('slow'), urllib.error.HTTPError('https://alternate.invalid/pinned.zip', 403, 'Forbidden', {}, io.BytesIO())])
        result = network.probe(self.dependency(), opener=opener)
        self.assertEqual(result['status'], 'unreachable')
        with self.assertRaises(state.WizardError): network.check([self.dependency()], probe_fn=lambda _: result)


class LocalAdbTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.workspace = self.root / 'workspace'
        self.saved = {'session': 'a' * 32, 'choices': {'mode': 'build', 'install': True}}

    def sdk(self, host):
        editor = self.root / ('Win editor/Unity.exe' if host == 'windows-x64' else 'Linux editor/Unity')
        editor.parent.mkdir(parents=True, exist_ok=True); editor.write_bytes(b'Inert selected Editor fixture')
        executable = editor.parent / 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools' / ('adb.exe' if host == 'windows-x64' else 'adb')
        executable.parent.mkdir(parents=True); executable.write_bytes(b'Inert selected SDK ADB fixture')
        return editor, executable

    def test_selected_unity_sdk_adb_omits_download_for_both_hosts(self):
        for host in ('windows-x64', 'linux-x64'):
            editor, executable = self.sdk(host)
            with patch.object(network.shutil, 'which', return_value=None):
                rows = network.inventory(self.workspace, ROOT, self.saved, pending={'install'}, editor=editor, host=host)
                self.assertTrue(rows[0]['local'])
                self.assertEqual(network.local_adb(self.workspace, ROOT, editor=editor, host=host), executable)
                network.check(rows, probe_fn=lambda _: self.fail('Selected Unity SDK already has ADB'))

    def test_current_installer_mirror_recipe_does_not_read_old_frozen_mod_sources(self):
        old = self.root / 'frozen old sources'; old.mkdir()
        with patch.object(network.shutil, 'which', return_value=None):
            for host in ('windows-x64', 'linux-x64'):
                rows = network.inventory(self.workspace, old, self.saved, pending={'install'}, host=host, tool_source=ROOT)
                self.assertEqual(len(rows[0]['urls']), 3)
                self.assertEqual([urllib.parse.urlsplit(url).hostname for url in rows[0]['urls']],
                                 ['dl.google.com', 'dl-ssl.google.com', 'redirector.gvt1.com'])

    def test_managed_candidate_keeps_lexical_path_for_installer_owner_validation(self):
        repo = self.root / 'live builder'; executable = repo / 'scripts/.quest-adb/platform-tools/adb.exe'
        executable.parent.mkdir(parents=True); executable.write_bytes(b'Inert managed ADB fixture')
        with patch.object(network.shutil, 'which', return_value=None):
            self.assertEqual(network.local_adb(self.workspace, repo, host='windows-x64'), executable)

    def test_installation_passes_selected_unity_adb_to_the_actual_installer_command(self):
        host = 'windows-x64' if os.name == 'nt' else 'linux-x64'
        editor, executable = self.sdk(host)
        store = state.Store(self.workspace)
        saved = store.create(wizard.choices({'gameRoot': str(self.root / 'Game'), 'unityEditor': str(editor)}))
        engine = wizard.Engine(store, repo=ROOT)
        completed = {'outputRoot': str(self.workspace / 'build'), 'inputKey': 'b' * 64, 'apkSha256': 'c' * 64}
        supervisor = SimpleNamespace(run=Mock())
        config = self.workspace / 'device/wireless-last-install.json'
        config.parent.mkdir(parents=True)
        config.write_text(json.dumps({'schema': 1, **completed, 'launched': True}))
        with patch.object(engine, 'details', return_value=completed), \
             patch.object(engine, 'installation_policy', return_value={'policy': 'preserve'}), \
             patch.object(engine, 'installation_content_approval', return_value=None), \
             patch.object(network.shutil, 'which', return_value=None):
            engine.stage_install(saved, supervisor)
        args = supervisor.run.call_args.args[0]
        self.assertEqual(args[args.index('--adb') + 1], str(executable))
        self.assertIn(str(ROOT / 'scripts/install-quest-wireless.py'), args)


if __name__ == '__main__': unittest.main()
