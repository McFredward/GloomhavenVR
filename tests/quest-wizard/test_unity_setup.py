"""Actual wait/action and owned-child fixtures; no installed Unity is invoked."""
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import state
import unity_setup
import wizard
from processes import Supervisor


class UnitySetupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.store = state.Store(Path(self.temp.name) / 'owner')
        self.saved = self.store.create(wizard.choices({'gameRoot': str(Path(self.temp.name) / 'Game'), 'acceptUnityTerms': True}))
        self.session = self.saved['session']

    def running(self):
        self.saved['status'] = 'running'; self.saved['stages'][2]['status'] = 'running'; self.store.save(self.saved)

    def test_pending_login_cannot_complete_missing_editor_or_android_modules(self):
        root = Path(self.temp.name) / 'Editor'; root.mkdir()
        editor = root / 'Unity.exe'; editor.write_text('fixture Editor')
        hub = Path(self.temp.name) / 'UnityHub.exe'; hub.write_text('fixture Hub')
        entered = threading.Event(); release = threading.Event(); completed = threading.Event(); failures = []
        installed = []
        owner = self
        class FixtureSupervisor:
            store, session = owner.store, owner.session
            def run(self, argv, log, **kwargs):
                log = Path(log); log.parent.mkdir(exist_ok=True)
                if '--headless' in argv and 'help' in argv:
                    log.write_text('editors install install-modules\n')
                elif '--headless' in argv:
                    installed.append(list(argv))
                    support = root / 'Data/PlaybackEngines/AndroidPlayer'
                    for name in ('NDK/source.properties', 'SDK/platform-tools/adb', 'OpenJDK/bin/java'):
                        target = support / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_text('observed module')
                    log.write_text('installation complete\n')
                elif '-version' in argv:
                    log.write_text(unity_setup.VERSION + '\n')
                else:
                    marker = Path(argv[argv.index('-logFile') + 1])
                    marker.write_text(unity_setup.PROBE_MARKER + '\n'); log.write_text('probe returned\n')
                return 0
        original_wait = unity_setup._wait
        def pending(*args, **kwargs):
            entered.set(); release.wait(3)
            return original_wait(*args, **kwargs, opener=lambda *a, **k: None, poll=.005)
        def work():
            try:
                unity_setup.prepare(self.store, self.saved, FixtureSupervisor()); completed.set()
            except BaseException as error: failures.append(error)
        # Only the external discovery/process boundary is substituted. Actual
        # module checks, prerequisite orchestration, wait nonce and probe witness
        # validation run against on-disk fixture outputs.
        editors = [{'path': str(editor), 'version': unity_setup.VERSION, 'androidSupport': False}]
        with state.file_lock(self.store.root / 'run.lock'), self.store.active(self.saved), \
             mock.patch('unity_setup.discovery.unity_paths', return_value=(editors, [str(hub)])), \
             mock.patch('unity_setup._wait', side_effect=pending):
            self.running()
            thread = threading.Thread(target=work); thread.start()
            try:
                self.assertTrue(entered.wait(2)); self.assertEqual(installed, [])
                row = self.store.load(self.session)['stages'][2]
                self.assertEqual(row['progress']['stagePercent'], 25)
                self.assertNotIn('editor', row['progressPlan']['completed'])
                release.set()
                deadline = time.monotonic() + 2
                while not self.store.load(self.session)['stages'][2].get('waiting') and time.monotonic() < deadline:
                    time.sleep(.005)
                waiting = self.store.load(self.session)['stages'][2]['waiting']
                self.assertEqual(waiting['code'], 'unity_login_required')
                self.assertEqual(self.store.load(self.session)['stages'][2]['progress']['stagePercent'], 25)
                unity_setup.request_action(self.store, self.session, 'unity-check', waiting['nonce'])
                thread.join(3); self.assertFalse(thread.is_alive())
                self.assertTrue(completed.is_set()); self.assertEqual(failures, [])
                self.assertEqual(len(installed), 1)
                row = self.store.load(self.session)['stages'][2]
                self.assertIn('editor', row['progressPlan']['completed'])
                self.assertLess(row['progress']['stagePercent'], 100, 'Wizard must still verify/publish stage outputs')
            finally:
                release.set(); self.store.cancel(self.session); thread.join(3)

    def test_closed_window_stays_pending_reopens_and_rechecks_without_new_run(self):
        opened = []; failures = []; completed = threading.Event()
        with state.file_lock(self.store.root / 'run.lock'):
            self.running()
            def work():
                try:
                    unity_setup._wait(self.store, self.session, 'unity_login_required', 'Sign in', 'Anmelden',
                                      lambda: ('fixture-hub', False), opener=lambda *a, **k: opened.append(a[0]), poll=0.005)
                    completed.set()
                except BaseException as error: failures.append(error)
            thread = threading.Thread(target=work); thread.start()
            try:
                deadline = time.monotonic() + 2
                while not opened and time.monotonic() < deadline: time.sleep(0.005)
                self.assertTrue(opened); time.sleep(0.025)
                self.assertFalse(completed.is_set(), 'Window exit alone cannot finish prerequisites')
                waiting = self.store.load(self.session)['stages'][2]['waiting']
                with self.assertRaises(state.WizardError): unity_setup.request_action(self.store, self.session, 'unity-open', '0' * 32)
                unity_setup.request_action(self.store, self.session, 'unity-open', waiting['nonce'])
                deadline = time.monotonic() + 2
                while len(opened) < 2 and time.monotonic() < deadline: time.sleep(0.005)
                self.assertEqual(len(opened), 2)
                self.assertEqual(self.store.load(self.session)['stages'][2]['waiting']['nonce'], waiting['nonce'])
                unity_setup.request_action(self.store, self.session, 'unity-check', waiting['nonce'])
                thread.join(2); self.assertFalse(thread.is_alive()); self.assertTrue(completed.is_set())
                self.assertFalse(self.store.receipt(self.session, 'unity').exists())
                with self.assertRaises(state.WizardError): unity_setup.request_action(self.store, self.session, 'unity-open', waiting['nonce'])
            finally:
                self.store.cancel(self.session); thread.join(2)
            self.assertFalse(failures)

    def test_cancellation_interrupts_user_wait_without_receipt(self):
        with state.file_lock(self.store.root / 'run.lock'):
            self.running(); self.store.cancel(self.session)
            with self.assertRaises(state.Cancelled):
                unity_setup._wait(self.store, self.session, 'unity_login_required', 'Sign in', 'Anmelden', lambda: ('Hub', False), opener=mock.Mock())
        self.assertFalse(self.store.receipt(self.session, 'unity').exists())

    def test_probe_requires_fresh_exact_success_marker_and_zero_exit(self):
        for message, code, accepted in [(unity_setup.PROBE_MARKER + '\n', 0, True),
                                         ('Debug.Log("' + unity_setup.PROBE_MARKER + '")\n', 0, False),
                                         ('version=2021.3.5f1\n', 0, False),
                                         (unity_setup.PROBE_MARKER + '\n', 1, False)]:
            with self.subTest(message=message, code=code):
                owner = self
                class FixtureSupervisor:
                    def run(self, argv, log, **kwargs):
                        target = str(argv[argv.index('-logFile') + 1])
                        script = 'import pathlib,sys;pathlib.Path(sys.argv[1]).write_text(sys.argv[2]);sys.exit(int(sys.argv[3]))'
                        return Supervisor(owner.store, owner.session, grace=0.01, poll=0.005).run(
                            [sys.executable, '-I', '-B', '-c', script, target, message, str(code)], log, **kwargs)
                if accepted:
                    self.assertTrue(unity_setup._probe(self.store, self.session, 'fixture-editor', FixtureSupervisor()).is_file())
                else:
                    with self.assertRaises(state.WizardError): unity_setup._probe(self.store, self.session, 'fixture-editor', FixtureSupervisor())

    def test_probe_timeout_stops_owned_child(self):
        owner = self
        class FixtureSupervisor:
            def run(self, argv, log, **kwargs):
                kwargs['timeout'] = 0.03
                return Supervisor(owner.store, owner.session, grace=0.01, poll=0.005).run(
                    [sys.executable, '-I', '-B', '-c', 'import time;time.sleep(30)'], log, **kwargs)
        with self.assertRaises(state.WizardError) as error: unity_setup._probe(self.store, self.session, 'fixture-editor', FixtureSupervisor())
        self.assertEqual(error.exception.code, 'child_timeout')
        self.assertFalse((self.store.session_dir(self.session) / 'child.json').exists())

    def test_authentication_prompt_stops_tool_and_becomes_actionable(self):
        log = self.store.session_dir(self.session) / 'logs/unity-install.log'
        guard = unity_setup._login_guard(log)
        with self.assertRaises(state.WizardError) as error:
            Supervisor(self.store, self.session, grace=0.01, poll=0.005).run(
                [sys.executable, '-I', '-B', '-c', 'import time;print("Authentication required",flush=True);time.sleep(30)'],
                log, timeout=3, on_poll=guard)
        self.assertEqual(error.exception.code, 'unity_login_required')
        self.assertFalse((self.store.session_dir(self.session) / 'child.json').exists())

    def test_action_does_not_accept_arbitrary_command_or_completed_stage(self):
        with state.file_lock(self.store.root / 'run.lock'):
            self.running(); waiting = self.store.waiting(self.session, 'unity', 'fixture', {'en': 'Wait', 'de': 'Warten'})
            for action in ('cmd.exe', '../install', 'https://example.invalid'):
                with self.assertRaises(state.WizardError): unity_setup.request_action(self.store, self.session, action, waiting['nonce'])
            unity_setup.request_action(self.store, self.session, 'unity-check', waiting['nonce'])
            with self.assertRaises(state.WizardError) as error: unity_setup.request_action(self.store, self.session, 'unity-check', waiting['nonce'])
            self.assertEqual(error.exception.code, 'action_pending')


if __name__ == '__main__': unittest.main()
