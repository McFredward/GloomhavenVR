"""Early download failure, local-only modes and real TLS boundary controls."""
import http.server
import io
import json
import os
from pathlib import Path
import shutil
import ssl
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import network_preflight as network
import provision
import state
import wizard


def dependency(**changes):
    return dict(id='opus', label='Opus voice codec', urls=['https://primary.invalid/source', 'https://fallback.invalid/source'], local=False, **changes)


class Response(io.BytesIO):
    status = 206
    def geturl(self): return 'https://cdn.invalid/archive?signature=secret'


class ProbeTests(unittest.TestCase):
    def test_reads_one_byte_and_logs_no_signed_query(self):
        response = Response(b'fixture archive'); response.read = Mock(wraps=response.read)
        opener = Mock(return_value=response)
        result = network.probe(dependency(), opener=opener)
        self.assertEqual(result['status'], 'reachable')
        response.read.assert_called_once_with(1)
        request = opener.call_args.args[0]
        self.assertEqual(request.get_header('Range'), 'bytes=0-0')
        self.assertEqual(opener.call_args.kwargs['timeout'], 5)
        self.assertNotIn('secret', json.dumps(result))

    def test_tls_primary_failure_uses_actual_declared_fallback(self):
        error = ssl.SSLCertVerificationError(1, 'fixture issuer missing'); error.verify_message = 'fixture issuer missing'
        opener = Mock(side_effect=[urllib.error.URLError(error), Response(b'archive')])
        result = network.probe(dependency(), opener=opener)
        self.assertEqual(result['status'], 'reachable')
        self.assertIn('TLS certificate verification failed', result['attempts'][0]['reason'])
        self.assertEqual(opener.call_args.args[0].full_url, 'https://fallback.invalid/source')

    def test_all_endpoints_failed_are_named_before_work(self):
        result = network.probe(dependency(), opener=Mock(side_effect=urllib.error.URLError('fixture DNS failure')))
        self.assertEqual(result['status'], 'unreachable')
        self.assertEqual(len(result['attempts']), 2)
        with self.assertRaises(state.WizardError) as caught:
            network.check([dependency()], probe_fn=lambda _: result)
        self.assertEqual(caught.exception.code, 'network_preflight_failed')
        self.assertTrue(caught.exception.parameters['completedWorkRetained'])
        for language in ('en', 'de'): self.assertIn('Opus voice codec', caught.exception.message[language])

    def test_local_dependencies_do_not_call_network(self):
        row = dependency(); row['local'] = True
        result = network.check([row], probe_fn=lambda _: self.fail('Offline cache must not be probed'))
        self.assertEqual(result['checkedDependencies'], 0)
        self.assertEqual(result['localDependencies'], 1)
        self.assertFalse(result['offlineClosureVerified'])

    def test_conditional_restore_probe_does_not_reject_possible_local_closure(self):
        row = dependency(conditional=True)
        result = network.check([row], probe_fn=lambda r: {**r, 'status': 'unreachable'})
        self.assertEqual(result['results'][0]['status'], 'unreachable')
        self.assertFalse(result['offlineClosureVerified'])

    def test_cancel_is_observed_during_preflight(self):
        def cancel(): raise state.Cancelled()
        with self.assertRaises(state.Cancelled): network.check([dependency()], check_cancel=cancel, probe_fn=lambda r: {**r, 'status': 'reachable'})

    def test_http_url_never_reaches_opener(self):
        row = dependency(); row['urls'] = ['http://unsafe.invalid/archive']
        self.assertEqual(network.probe(row, opener=lambda *_a, **_k: self.fail('HTTP request'))['status'], 'unreachable')

    def test_removed_download_is_distinguished_from_no_internet(self):
        result = network.probe(dependency(), opener=Mock(side_effect=urllib.error.HTTPError('https://primary.invalid/source', 404, 'Not Found', {}, None)))
        with self.assertRaises(state.WizardError) as caught:
            network.check([dependency()], probe_fn=lambda _: result)
        self.assertEqual(caught.exception.code, 'network_dependency_unavailable')
        self.assertIn('HTTP 404', caught.exception.parameters['failedDependencies'][0]['attempts'][0]['reason'])
        self.assertNotIn('Internetverbindung', caught.exception.message['de'])

    def test_error_responses_are_closed_before_trying_fallback(self):
        body = io.BytesIO(b'not found')
        failure = urllib.error.HTTPError('https://primary.invalid/source', 404, 'Not Found', {}, body)
        result = network.probe(dependency(), opener=Mock(side_effect=[failure, Response(b'archive byte')]))
        self.assertEqual(result['status'], 'reachable')
        self.assertTrue(body.closed)


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.saved = {'session': 'a' * 32, 'choices': {'mode': 'update-profile', 'install': False}}
    def tearDown(self): self.temp.cleanup()
    def inventory(self, **changes):
        return network.inventory(self.root, ROOT, self.saved, pending={'tools', 'source', 'unity', 'profile', 'build'}, host='windows-x64', **changes)

    def test_profile_update_has_no_compiler_or_unity_downloads(self):
        rows = self.inventory()
        self.assertEqual({r['id'] for r in rows}, {'apkJdk', 'apkBuildTools', 'steam-logo'})

    def test_profile_update_all_local_is_network_free(self):
        for name in ('apkJdk', 'apkBuildTools'):
            spec = provision.LOCK[name]
            path = self.root / 'tools/downloads' / (name + '-' + spec['version'] + '.zip')
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open('wb') as output: output.truncate(spec['size'])
        logo = self.root / 'local-logo.png'; logo.write_bytes(b'local fixture logo')
        self.saved['choices']['steamLogo'] = str(logo)
        rows = self.inventory(); self.assertTrue(all(r['local'] for r in rows))
        network.check(rows, probe_fn=lambda _: self.fail('No download needed'))

    def test_incomplete_archive_requires_download(self):
        spec = provision.LOCK['apkJdk']
        path = self.root / 'tools/downloads' / ('apkJdk-' + spec['version'] + '.zip')
        path.parent.mkdir(parents=True); path.write_bytes(b'incomplete')
        self.assertFalse(next(r for r in self.inventory() if r['id'] == 'apkJdk')['local'])

    def test_install_only_does_not_require_conversion_packages(self):
        self.saved['choices'].update(mode='build', install=True)
        rows = network.inventory(self.root, ROOT, self.saved, pending={'install'}, host='windows-x64')
        self.assertEqual([r['id'] for r in rows], ['adb'])

    def test_existing_sidequest_adb_needs_no_download(self):
        self.saved['choices'].update(mode='build', install=True)
        adb = self.root / 'roaming/SideQuest/platform-tools/adb.exe'
        adb.parent.mkdir(parents=True); adb.write_bytes(b'fixture executable')
        with patch.dict(os.environ, {'APPDATA': str(self.root / 'roaming')}):
            rows = network.inventory(self.root, ROOT, self.saved, pending={'install'}, host='windows-x64')
        self.assertTrue(rows[0]['local'])
        network.check(rows, probe_fn=lambda _: self.fail('No download needed'))

    def test_installed_hub_is_not_a_missing_dependency(self):
        self.saved['choices']['mode'] = 'build'
        hub = self.root / 'Unity Hub.exe'; hub.write_bytes(b'fixture executable')
        rows = self.inventory(hub=hub)
        self.assertNotIn('unity-hub', {r['id'] for r in rows})
        self.assertIn('unity-install', {r['id'] for r in rows})

    def test_current_game_build_includes_its_late_native_sources(self):
        self.saved['choices']['mode'] = 'build'
        rows = self.inventory(); selected = {r['id']: r for r in rows}
        for name in ('tmp', 'post-effects', 'asset-exporter', 'opus', 'python-packages', 'proton-proton-11.0-2-arm64ec.wcp'):
            self.assertIn(name, selected)
        self.assertEqual(selected['opus']['urls'][0], 'https://github.com/xiph/opus/releases/download/v1.5.2/opus-1.5.2.tar.gz')
        self.assertFalse(selected['opus']['local'])

    def test_old_opus_key_does_not_claim_current_archive_ready(self):
        self.saved['choices']['mode'] = 'build'
        old = self.root / 'build/tool-cache/campaign-native/voice/old-key/verified-source/opus-1.5.2.tar.gz'
        old.parent.mkdir(parents=True); old.write_bytes(b'old key archive')
        self.assertFalse(next(r for r in self.inventory() if r['id'] == 'opus')['local'])

    def test_recipe_reader_does_not_execute_top_level_calls(self):
        source = self.root / 'recipe.py'; witness = self.root / 'unexpected'
        source.write_text('from pathlib import Path\nPath(' + repr(str(witness)) + ').write_text("executed")\nVERSION="1"\nURL=f"https://example.invalid/{VERSION}"\n')
        self.assertEqual(network.constants(source)['URL'], 'https://example.invalid/1')
        self.assertFalse(witness.exists())

    def test_requirements_identity_matches_actual_conversion_producer(self):
        with patch.object(sys, 'path', [str(ROOT / 'tools/quest-builder'), *sys.path]):
            module = wizard.discovery.builder(ROOT)._local_helper('dependencies')
        paths = [ROOT / name for name in ('tools/quest-builder/requirements.txt', 'tools/quest-builder/requirements-bootstrap.txt',
                                          'tools/quest-builder/requirements-source.txt', 'tools/quest-procedural-runtime/requirements.txt')]
        self.assertEqual(network.requirements_key(ROOT), module.requirements_key(paths))


class EngineTests(unittest.TestCase):
    def test_capacity_failure_precedes_endpoint_and_receipt_checks(self):
        with tempfile.TemporaryDirectory() as temporary:
            store = state.Store(Path(temporary) / 'workspace')
            saved = store.create(wizard.choices({'gameRoot': str(Path(temporary) / 'Game'), 'install': False}))
            engine = wizard.Engine(store)
            error = state.WizardError('workspace_build_space_low', 'Insufficient space', 'Zu wenig Speicherplatz')
            with patch.object(engine, 'qualify', side_effect=error), \
                 patch.object(engine, 'check_downloads') as endpoints, patch.object(store, 'valid') as receipts:
                result = engine.run(saved['session'])
            self.assertEqual(result['needsActions'][0]['code'], 'workspace_build_space_low')
            endpoints.assert_not_called(); receipts.assert_not_called()

    def test_early_failure_preserves_outputs_and_never_launches_stages(self):
        with tempfile.TemporaryDirectory() as temporary:
            store = state.Store(Path(temporary) / 'workspace')
            saved = store.create(wizard.choices({'gameRoot': str(Path(temporary) / 'Game'), 'install': False}))
            witness = store.root / 'retained.bin'; witness.write_bytes(b'retained completed data')
            engine = wizard.Engine(store)
            error = state.WizardError('network_preflight_failed', 'Opus unavailable', 'Opus nicht erreichbar', completedWorkRetained=True)
            with patch.object(engine, 'qualify'), patch.object(engine, 'check_downloads', side_effect=error), patch.object(engine, 'stage_tools', side_effect=AssertionError('No tool action should start')):
                result = engine.run(saved['session'])
            self.assertEqual(result['status'], 'blocked')
            self.assertEqual(result['needsActions'][0]['code'], 'network_preflight_failed')
            self.assertEqual(witness.read_bytes(), b'retained completed data')
            self.assertFalse(store.receipt(saved['session'], 'tools').exists())

    def test_actual_check_persists_failure_report_and_can_retry(self):
        with tempfile.TemporaryDirectory() as temporary:
            store = state.Store(Path(temporary) / 'workspace')
            saved = store.create(wizard.choices({'gameRoot': str(Path(temporary) / 'Game'), 'install': False}))
            engine = wizard.Engine(store); checked = []
            failed = {**dependency(), 'status': 'unreachable', 'attempts': [{'url': 'https://primary.invalid/source', 'reason': 'fixture DNS failure'}]}
            real_check = network.check
            with patch.object(engine, 'qualify'), patch.object(network, 'inventory', return_value=[dependency()]), \
                 patch.object(network, 'check', side_effect=lambda rows, **kw: real_check(rows, probe_fn=lambda _: failed, **kw)):
                result = engine.run(saved['session'])
            report = json.loads((store.session_dir(saved['session']) / 'network-preflight.json').read_text())
            self.assertEqual(report['status'], 'network_preflight_failed')
            self.assertEqual(report['results'][0]['attempts'][0]['reason'], 'fixture DNS failure')
            self.assertEqual(result['status'], 'blocked')
            for name in state.STAGES:
                def action(saved, supervisor, stage=name):
                    checked.append(stage)
                    path = store.session_dir(saved['session']) / (stage + '.fixture'); path.write_text(stage)
                    return [path], {'sourceRoot': str(ROOT)}
                setattr(engine, 'stage_' + name, action)
            with patch.object(network, 'inventory', return_value=[]), patch.object(engine, 'qualify'):
                result = engine.run(saved['session'])
            self.assertEqual(result['status'], 'complete')
            self.assertEqual(result['needsActions'], [])
            self.assertEqual(checked, list(state.STAGES))


@unittest.skipUnless(shutil.which('openssl'), 'Actual TLS fixtures require OpenSSL')
class TlsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(); cls.root = Path(cls.temp.name)
        subprocess.run([shutil.which('openssl'), 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=localhost',
                        '-addext', 'subjectAltName=DNS:localhost', '-keyout', str(cls.root/'key.pem'), '-out', str(cls.root/'cert.pem')],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    @classmethod
    def tearDownClass(cls): cls.temp.cleanup()
    def serve(self, redirect=False):
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *_): pass
            def do_GET(self):
                if redirect:
                    self.send_response(302); self.send_header('Location', 'http://localhost:1/unsafe'); self.end_headers()
                else:
                    self.send_response(206); self.end_headers(); self.wfile.write(b'x')
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); context.load_cert_chain(str(self.root/'cert.pem'), str(self.root/'key.pem'))
        server.socket = context.wrap_socket(server.socket, server_side=True)
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        def stop(): server.shutdown(); server.server_close(); thread.join()
        self.addCleanup(stop)
        row = dependency(); row['urls'] = ['https://localhost:' + str(server.server_port) + '/archive']
        return row
    def test_actual_untrusted_certificate_fails_preflight(self):
        result = network.probe(self.serve())
        self.assertEqual(result['status'], 'unreachable')
        self.assertIn('TLS certificate verification failed', result['attempts'][0]['reason'])
    def test_actual_trusted_certificate_succeeds_with_verification_enabled(self):
        context = ssl.create_default_context(cafile=str(self.root/'cert.pem'))
        with patch.object(network.ssl, 'create_default_context', return_value=context):
            self.assertEqual(network.probe(self.serve())['status'], 'reachable')
        self.assertEqual(context.verify_mode, ssl.CERT_REQUIRED)
    def test_actual_https_to_http_redirect_is_rejected(self):
        context = ssl.create_default_context(cafile=str(self.root/'cert.pem'))
        with patch.object(network.ssl, 'create_default_context', return_value=context):
            result = network.probe(self.serve(redirect=True))
        self.assertEqual(result['status'], 'unreachable')
        self.assertIn('outside HTTPS', result['attempts'][0]['reason'])


if __name__ == '__main__': unittest.main()
