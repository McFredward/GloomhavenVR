"""Actual isolated HTTP discovery and pointer-safe Windows folder selection."""
import ctypes
from ctypes import wintypes
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import server
from state import WizardError


ISOLATED_HTTP = """
import http.client, json, profile, sys, threading
from pathlib import Path
repo, workspace = map(Path, sys.argv[1:3])
sys.path.insert(0, str(repo / 'tools/quest-wizard'))
import server, state
paths = list(sys.path)
original_profile = sys.modules['profile']
store = state.Store(workspace)
local = server.LocalServer(store, repo / 'tools/quest-wizard-ui')
thread = threading.Thread(target=local.serve_forever, daemon=True)
thread.start()
try:
    connection = http.client.HTTPConnection(*local.server_address, timeout=10)
    connection.request('GET', '/api/discover', headers={
        'X-Quest-Token': local.token, 'Origin': local.origin})
    response = connection.getresponse()
    value = json.loads(response.read())
    connection.close()
    assert response.status == 200, value
    assert value['event'] == 'discovery', value
    assert all(isinstance(value[key], list) for key in ('games', 'unityEditors', 'unityHubs', 'recentSessions'))
    assert sys.path == paths, 'discovery changed import search paths'
    assert sys.modules['profile'] is original_profile
    assert 'native_plugins' not in sys.modules
    print(json.dumps({'discoveryStatus': response.status, 'stdlibProfilePreserved': True}))
finally:
    local.shutdown()
    local.close_owned()
    thread.join(timeout=5)
"""


class StartupTests(unittest.TestCase):
    def test_real_http_discovery_in_fresh_isolated_process(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, "-I", "-B", "-c", ISOLATED_HTTP,
                                     str(ROOT), str(Path(directory) / "workspace")],
                                    cwd=directory, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('"discoveryStatus": 200', result.stdout)


class NativeFunction:
    def __init__(self, action): self.action, self.calls = action, []
    def __call__(self, *args):
        self.calls.append(args)
        return self.action(*args)


class FolderPickerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "Game folder"
        self.path.mkdir()
        self.pidl = 0x100000042 if ctypes.sizeof(ctypes.c_void_p) == 8 else 0x42
        self.initialized, self.cancel, self.valid_path = 0, False, True
        self.shell = SimpleNamespace(
            SHBrowseForFolderW=NativeFunction(self.choose),
            SHGetPathFromIDListW=NativeFunction(self.resolve))
        self.ole = SimpleNamespace(
            CoInitializeEx=NativeFunction(lambda *_: self.initialized),
            CoTaskMemFree=NativeFunction(lambda *_: None),
            CoUninitialize=NativeFunction(lambda: None))
    def tearDown(self): self.temp.cleanup()
    def choose(self, pointer):
        info = ctypes.cast(pointer, self.shell.SHBrowseForFolderW.argtypes[0]).contents
        self.assertEqual(info.flags, 0x41)
        self.assertIn("Select", info.title)
        self.assertIs(dict(type(info)._fields_)["display"], wintypes.LPWSTR)
        address = ctypes.c_void_p.from_address(ctypes.addressof(info) + type(info).display.offset).value
        label = ctypes.create_unicode_buffer("Gloomhaven")
        ctypes.memmove(address, label, ctypes.sizeof(label))
        self.assertEqual(info.display, "Gloomhaven")
        return None if self.cancel else self.pidl
    def resolve(self, item, output):
        self.assertEqual(item, self.pidl)
        output.value = str(self.path)
        return self.valid_path
    def browse(self, kind="game"):
        with mock.patch.object(server, "os", SimpleNamespace(name="nt")), \
             mock.patch.object(server.ctypes, "WinDLL", create=True,
                               side_effect=lambda name: {"ole32": self.ole, "shell32": self.shell}[name]):
            return server.browse(kind)
    def test_game_selection_uses_live_wide_buffer_and_pointer_sized_pidl(self):
        self.assertEqual(self.browse(), str(self.path))
        self.assertEqual(self.ole.CoTaskMemFree.calls, [(self.pidl,)])
        self.assertEqual(self.ole.CoUninitialize.calls, [()])
        self.assertIs(self.shell.SHBrowseForFolderW.restype, ctypes.c_void_p)
        self.assertEqual(self.shell.SHGetPathFromIDListW.argtypes, [ctypes.c_void_p, wintypes.LPWSTR])
        self.assertIs(self.ole.CoInitializeEx.restype, ctypes.c_int32)
        self.assertIsNone(self.ole.CoTaskMemFree.restype)
    def test_unity_selection_resolves_editor_executable(self):
        editor = self.path / "Editor/Unity.exe"
        editor.parent.mkdir(); editor.write_bytes(b"fixture")
        self.assertEqual(self.browse("unity"), str(editor))
    def test_cancel_only_uninitializes_com(self):
        self.cancel = True
        self.assertIsNone(self.browse())
        self.assertFalse(self.shell.SHGetPathFromIDListW.calls)
        self.assertFalse(self.ole.CoTaskMemFree.calls)
        self.assertEqual(self.ole.CoUninitialize.calls, [()])
    def test_invalid_filesystem_selection_frees_pidl_and_com(self):
        self.valid_path = False
        with self.assertRaises(WizardError) as raised: self.browse()
        self.assertEqual(raised.exception.code, "browse_failed")
        self.assertNotEqual(raised.exception.message["de"], raised.exception.message["en"])
        self.assertEqual(self.ole.CoTaskMemFree.calls, [(self.pidl,)])
        self.assertEqual(self.ole.CoUninitialize.calls, [()])
    def test_failed_com_initialization_does_not_open_or_uninitialize(self):
        self.initialized = -2147417850
        with self.assertRaises(WizardError) as raised: self.browse()
        self.assertEqual(raised.exception.code, "browse_failed")
        self.assertFalse(self.shell.SHBrowseForFolderW.calls)
        self.assertFalse(self.ole.CoUninitialize.calls)
    def test_existing_com_apartment_still_balances_initialization(self):
        self.initialized = 1
        self.assertEqual(self.browse(), str(self.path))
        self.assertEqual(self.ole.CoUninitialize.calls, [()])


if __name__ == "__main__": unittest.main()
