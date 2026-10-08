"""Git-free release resume, qualifications and actual support ZIP HTTP/CLI."""
import http.client
import io
import json
from pathlib import Path
import subprocess
import shutil
from types import SimpleNamespace
import sys
import tempfile
import threading
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import discovery
import provision
import qualification
import server
import state
import wizard


class SupportIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name); self.store = state.Store(self.root / 'owned')
        self.saved = self.store.create(wizard.choices({'gameRoot': str(self.root / 'OwnedGame')}))
        log = self.store.session_dir(self.saved['session']) / 'logs/build.log'; log.parent.mkdir(); log.write_text('important failure\n')
        self.server = server.LocalServer(self.store, ROOT / 'tools/quest-wizard-ui')
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()
    def tearDown(self):
        self.server.shutdown(); self.server.close_owned(); self.thread.join(); self.temp.cleanup()
    def request(self, body, token=True, origin=True):
        connection = http.client.HTTPConnection(*self.server.server_address)
        headers = {'Content-Type': 'application/json'}
        if token: headers['X-Quest-Token'] = self.server.token
        if origin: headers['Origin'] = self.server.origin
        connection.request('POST', '/api/support', json.dumps(body), headers)
        response = connection.getresponse(); raw = response.read(); result = (response.status, response.getheaders(), raw); connection.close(); return result
    def test_actual_download_is_authenticated_zip_without_arbitrary_path_input(self):
        selected = {'session': self.saved['session']}
        for token, origin in ((False, True), (True, False)):
            status, _, _ = self.request(selected, token, origin); self.assertEqual(status, 403)
        status, headers, raw = self.request(selected)
        self.assertEqual(status, 200); self.assertEqual(dict(headers)['Content-Type'], 'application/zip')
        self.assertIn('attachment;', dict(headers)['Content-Disposition'])
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            self.assertEqual(archive.read('wizard/build.log'), b'important failure\n')
        status, _, _ = self.request(dict(selected, path='/arbitrary/private')); self.assertEqual(status, 400)
    def test_isolated_support_cli_exports_selected_session(self):
        output = self.root / 'support.zip'
        result = subprocess.run([sys.executable, '-I', '-B', str(ROOT / 'tools/quest-wizard/wizard.py'), 'support',
            '--state-root', str(self.store.root), '--session', self.saved['session'], '--output', str(output)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout)['event'], 'support_exported'); self.assertTrue(output.is_file())
    def test_helper_load_restores_application_modules(self):
        sentinel = object()
        with mock.patch.dict(sys.modules, {'storage': sentinel, 'release': sentinel, 'support': sentinel}):
            helper = discovery.local_support_module(ROOT, 'support')
            self.assertTrue(callable(helper.export_support))
            for name in ('storage', 'release', 'support'): self.assertIs(sys.modules[name], sentinel)
    def test_new_release_identity_invalidates_only_source_and_downstream(self):
        source = self.root / 'release'; source.mkdir(); manifest = source / 'quest-builder-release.json'; manifest.write_text('{"release":"one"}')
        self.saved['choices']['sourceRoot'] = str(source)
        engine = wizard.Engine(self.store)
        tool_key = engine.key(self.saved, 'tools'); source_key = engine.key(self.saved, 'source')
        manifest.write_text('{"release":"two"}')
        self.assertEqual(engine.key(self.saved, 'tools'), tool_key); self.assertNotEqual(engine.key(self.saved, 'source'), source_key)
        self.assertFalse((source / '.git').exists())
    def test_cancelled_release_copy_resumes_and_new_release_preserves_prior_workspace(self):
        release = discovery.local_support_module(ROOT, 'release')
        source = self.root / 'public-release'; source.mkdir()
        for name in release.REQUIRED:
            path = source / name; path.parent.mkdir(parents=True, exist_ok=True)
            if name in release.PUBLIC_PACKAGES: shutil.copyfile(ROOT / name, path)
            else: path.write_text('public source fixture')
        def manifest():
            records = [release.record_file(path, path.relative_to(source).as_posix()) for path in source.rglob('*') if path.is_file() and path.name != release.MANIFEST]
            release.write_json(source / release.MANIFEST, {'schema': 1, 'kind': 'GloomhavenVR game-free Windows builder',
                'sourceCommit': 'a' * 40, 'modBuild': 625, 'files': records, 'localDependencyRoots': list(release.LOCAL_DEPENDENCIES)})
        manifest()
        selected = dict(self.saved['choices'], sourceRoot=str(source))
        counter = [0]
        def cancel_once(_):
            counter[0] += 1
            if counter[0] == 3: raise state.Cancelled()
        def derive(checkout, *_):
            path = checkout / 'libs/RuntimeDeps/wizard-dependencies.json'
            state.atomic_json(path, {'schema': 1, 'assemblies': {}}); return path
        helper = SimpleNamespace(source_inventory=lambda folder: release.verified_source_inventory(folder))
        with mock.patch.object(discovery, 'local_support_module', return_value=release), mock.patch.object(discovery, 'builder', return_value=helper), mock.patch.object(provision, 'derive_runtime_dependencies', side_effect=derive):
            with mock.patch.object(self.store, 'check_cancel', side_effect=cancel_once):
                with self.assertRaises(state.Cancelled): provision.source_checkout(self.store, self.saved['session'], selected, {}, None, source)
            _, first = provision.source_checkout(self.store, self.saved['session'], selected, {}, None, source)
            self.assertFalse((Path(first['sourceRoot']) / '.git').exists())
            original = Path(first['sourceRoot']) / 'tools/quest-wizard/wizard.py'
            self.assertEqual(original.read_text(), 'public source fixture')
            (source / 'tools/quest-wizard/wizard.py').write_text('new mod source fixture'); manifest()
            _, second = provision.source_checkout(self.store, self.saved['session'], selected, {}, None, source)
            self.assertNotEqual(first['sourceRoot'], second['sourceRoot'])
            self.assertEqual(original.read_text(), 'public source fixture')
            self.assertEqual((Path(second['sourceRoot']) / 'tools/quest-wizard/wizard.py').read_text(), 'new mod source fixture')

    def test_qualification_rejects_known_platform_path_and_setup_space_failures(self):
        for settings, code in (({'machine': 'ARM64'}, 'windows_x64_required'), ({'free_bytes': 1024}, 'workspace_space_low')):
            with self.assertRaises(state.WizardError) as error:
                selected = dict(machine='AMD64', free_bytes=8 * qualification.GIB) | settings
                qualification.qualify(self.store.root, system='Windows', **selected)
            self.assertEqual(error.exception.code, code)
        short = self.root / 'short'; short.mkdir()
        result = qualification.qualify(short, system='Windows', machine='AMD64', free_bytes=8 * qualification.GIB)
        self.assertFalse(result['licenseVerified']); self.assertFalse(result['fullBuildSpaceVerified'])

if __name__ == '__main__': unittest.main()
