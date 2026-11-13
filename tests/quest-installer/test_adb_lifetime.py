"""Owned-daemon lifetime, shared-server isolation and Windows job controls."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_adb_lifetime", ROOT / "tools/quest-installer/adb_lifetime.py")
lifetime = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(lifetime)
SPEC = importlib.util.spec_from_file_location("quest_adb_lifetime_installer", ROOT / "tools/quest-installer/installer.py")
installer = importlib.util.module_from_spec(SPEC); sys.modules[SPEC.name] = installer; SPEC.loader.exec_module(installer)


def _fake_adb(root):
    path = root / "adb"
    path.write_text("#!" + str(Path(sys.executable).resolve()) + "\n" + '''import json,os,socket,sys
from pathlib import Path
assert sys.argv[1] == '-L' and sys.argv[3:] == ['server', 'nodaemon']
port=int(sys.argv[2].rsplit(':',1)[1]); root=Path(__file__).parent
(root/'daemon.json').write_text(json.dumps({'pid':os.getpid(),'args':sys.argv[1:]}))
s=socket.socket(); s.bind(('127.0.0.1',port)); s.listen(8)
while True:
 c,_=s.accept()
 with c:
  c.settimeout(1)
  try:
   if c.recv(1024): c.sendall(b'OKAY00040029')
  except OSError: pass
''')
    path.chmod(0o755)
    return path


def _alive(pid):
    try:
        os.kill(pid, 0)
        # A detached child's briefly unreaped zombie is no running daemon.
        proc = Path('/proc') / str(pid) / 'stat'
        return not proc.exists() or proc.read_text().split()[2] != 'Z'
    except ProcessLookupError: return False


class AdbLifetimeTests(unittest.TestCase):
    def test_existing_shared_server_is_borrowed_without_killing_or_environment_changes(self):
        env = {}
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(lifetime.subprocess, 'Popen') as popen:
            session = lifetime.Session(directory, environ=env, probe=lambda port: True).start()
            self.assertIsNone(session.process); self.assertEqual(env, {})
            session.close(); popen.assert_not_called()

    def test_explicit_external_endpoint_is_preserved(self):
        for name, value in (('ADB_SERVER_SOCKET', 'tcp:localhost:5041'),
                            ('ANDROID_ADB_SERVER_PORT', '5042'), ('ANDROID_ADB_SERVER_ADDRESS', 'localhost')):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                env = {name: value}
                with mock.patch.object(lifetime.subprocess, 'Popen') as popen:
                    session = lifetime.Session(directory, environ=env, probe=lambda port: False).start()
                    session.close(); popen.assert_not_called()
                self.assertEqual(env, {name: value})

    @unittest.skipIf(os.name == 'nt', 'The executable fake daemon uses a POSIX shebang.')
    def test_lazy_foreground_server_is_reused_and_reaped_on_normal_close(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ, {}, clear=True):
            root = Path(directory); executable = _fake_adb(root)
            session = lifetime.Session(root, probe=lambda port: False).start()
            guard = session.process
            try:
                self.assertFalse((root / 'daemon.json').exists())
                lifetime.ensure_server(executable)
                first = json.loads((root / 'daemon.json').read_text())
                lifetime.ensure_server(executable)
                self.assertEqual(first, json.loads((root / 'daemon.json').read_text()))
                self.assertTrue(_alive(first['pid']))
                self.assertEqual(first['args'], ['-L', os.environ['ADB_SERVER_SOCKET'], 'server', 'nodaemon'])
                self.assertNotEqual(first['args'][1], 'tcp:127.0.0.1:5037')
                spec = importlib.util.spec_from_file_location('lifetime_binary', ROOT / 'tools/quest-installer/adb_binary.py')
                binary = importlib.util.module_from_spec(spec); spec.loader.exec_module(binary)
                self.assertEqual(binary._server_endpoint(), ('tcp', ('127.0.0.1', int(first['args'][1][4:]))))
            finally: session.close()
            self.assertIsNotNone(guard.poll()); self.assertFalse(_alive(first['pid']))
            self.assertNotIn('ADB_SERVER_SOCKET', os.environ)
            self.assertNotIn(lifetime.CONTROL_ENV, os.environ)
            report = ''.join(p.read_text() for p in root.glob('adb-lifetime-*.log'))
            self.assertIn('guard-stopped', report)

    @unittest.skipIf(os.name == 'nt', 'The executable fake daemon uses a POSIX shebang.')
    def test_abrupt_owner_termination_closes_pipe_and_reaps_only_its_daemon(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); executable = _fake_adb(root)
            script = '''import importlib.util,json,os,sys,time
from pathlib import Path
spec=importlib.util.spec_from_file_location('lifetime',sys.argv[1]); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
for key in (m.CONTROL_ENV,m.SOCKET_ENV,'ANDROID_ADB_SERVER_ADDRESS','ANDROID_ADB_SERVER_PORT'): os.environ.pop(key,None)
s=m.Session(Path(sys.argv[2]),probe=lambda port:False).start(); m.ensure_server(sys.argv[3])
print(json.dumps({'guard':s.process.pid,'daemon':json.loads((Path(sys.argv[2])/'daemon.json').read_text())['pid']}),flush=True)
time.sleep(120)
'''
            owner = subprocess.Popen([sys.executable, '-I', '-B', '-c', script,
                                      str(ROOT / 'tools/quest-installer/adb_lifetime.py'), str(root), str(executable)],
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                line = owner.stdout.readline(); self.assertTrue(line, owner.stderr.read() if owner.poll() is not None else '')
                pids = json.loads(line)
                owner.kill(); owner.wait(timeout=5)
                deadline = time.monotonic() + 8
                while (_alive(pids['guard']) or _alive(pids['daemon'])) and time.monotonic() < deadline: time.sleep(.05)
                self.assertFalse(_alive(pids['guard'])); self.assertFalse(_alive(pids['daemon']))
            finally:
                if owner.poll() is None: owner.kill(); owner.wait(timeout=5)
                owner.stdout.close(); owner.stderr.close()

    @unittest.skipIf(os.name == 'nt', 'The executable fake daemon uses a POSIX shebang.')
    def test_unauthorized_control_message_does_not_stop_the_owned_server(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ, {}, clear=True):
            root = Path(directory); executable = _fake_adb(root)
            session = lifetime.Session(root, probe=lambda port: False).start()
            try:
                lifetime.ensure_server(executable)
                pid = json.loads((root / 'daemon.json').read_text())['pid']
                bad = dict(session.descriptor, token='wrong')
                with self.assertRaisesRegex(RuntimeError, 'unauthorized'): lifetime._request(bad, {'executable': str(executable)})
                self.assertTrue(_alive(pid)); lifetime.ensure_server(executable)
            finally: session.close()

    @unittest.skipIf(os.name == 'nt', 'The executable fake daemon uses a POSIX shebang.')
    def test_changed_client_executable_replaces_only_the_owned_foreground_server(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ, {}, clear=True):
            root = Path(directory); first_dir = root / 'first'; second_dir = root / 'second'
            first_dir.mkdir(); second_dir.mkdir()
            first = _fake_adb(first_dir); second = _fake_adb(second_dir)
            session = lifetime.Session(root, probe=lambda port: False).start()
            try:
                lifetime.ensure_server(first)
                old = json.loads((first_dir / 'daemon.json').read_text())
                lifetime.ensure_server(second)
                new = json.loads((second_dir / 'daemon.json').read_text())
                self.assertFalse(_alive(old['pid'])); self.assertTrue(_alive(new['pid']))
                self.assertNotEqual(old['pid'], new['pid'])
                self.assertEqual(old['args'][1], new['args'][1])
            finally: session.close()
            self.assertFalse(_alive(new['pid']))

    def test_cli_and_binary_adapter_both_request_the_same_owned_server(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ, {lifetime.CONTROL_ENV: 'owned'}):
            calls = []
            helper = SimpleNamespace(ensure_server=lambda path: calls.append(path))
            binary = SimpleNamespace(stream=lambda *args: 'binary-result')
            runner = mock.Mock(return_value=SimpleNamespace(returncode=0, stdout='List of devices attached\n', stderr=''))
            with mock.patch.dict(sys.modules, {'_ghvr_quest_adb_lifetime': helper, '_ghvr_quest_adb_binary': binary}):
                adb = installer.Adb('owned-adb', Path(directory) / 'adb.log', runner)
                self.assertEqual(adb.devices(), {})
                self.assertEqual(adb.binary_stream('quest', 'local', 'script', 5), 'binary-result')
            self.assertEqual(calls, ['owned-adb', 'owned-adb'])
            self.assertNotIn('kill-server', repr(runner.call_args_list))

    def test_windows_job_enables_kill_on_close_and_assigns_only_the_owned_process(self):
        class Function:
            def __init__(self, call): self.call = call
            def __call__(self, *args): return self.call(*args)
        import ctypes
        events = []
        kernel = SimpleNamespace(
            CreateJobObjectW=Function(lambda *args: 123),
            SetInformationJobObject=Function(lambda handle, kind, pointer, size: events.append(('limits', handle, kind,
                ctypes.cast(pointer, ctypes.POINTER(ctypes.c_uint64))[2] & 0xffffffff)) or 1),
            AssignProcessToJobObject=Function(lambda job, process: events.append(('assign', job, process)) or 1),
            CloseHandle=Function(lambda handle: events.append(('close', handle)) or 1))
        job = lifetime._WindowsJob(kernel)
        job.assign(SimpleNamespace(_handle=987)); job.close(); job.close()
        self.assertEqual(events, [('limits', 123, 9, 0x2000), ('assign', 123, 987), ('close', 123)])

    def test_windows_job_configuration_failure_closes_only_its_own_handle(self):
        class Function:
            def __init__(self, call): self.call = call
            def __call__(self, *args): return self.call(*args)
        closed = []
        kernel = SimpleNamespace(CreateJobObjectW=Function(lambda *args: 123),
            SetInformationJobObject=Function(lambda *args: 0),
            AssignProcessToJobObject=Function(lambda *args: 1),
            CloseHandle=Function(lambda handle: closed.append(handle) or 1))
        with self.assertRaisesRegex(OSError, 'configure'): lifetime._WindowsJob(kernel)
        self.assertEqual(closed, [123])


if __name__ == '__main__': unittest.main()
