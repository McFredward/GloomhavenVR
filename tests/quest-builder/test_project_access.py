"""Observe exact project ownership without confusing stale locks with Editors."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import project_access


class ProjectAccessTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform.startswith("linux"), "Requires Linux process command lines")
    def test_real_linux_process_releases_its_project_when_it_exits(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            process = subprocess.Popen(["/opt/Unity/Editor/Unity", "-c", "import time; time.sleep(30)",
                                        "-projectPath", str(project)], executable=sys.executable)
            try:
                self.assertTrue(project_access.editor_busy(project))
            finally:
                process.terminate(); process.wait(timeout=10)
            self.assertFalse(project_access.editor_busy(project))

    @unittest.skipUnless(sys.platform.startswith("linux"), "Requires Linux process working directories")
    def test_relative_project_path_uses_the_editors_working_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            process = subprocess.Popen(["/opt/Unity/Editor/Unity", "-c", "import time; time.sleep(30)",
                                        "-projectPath", "."], executable=sys.executable, cwd=project)
            try:
                self.assertTrue(project_access.editor_busy(project))
            finally:
                process.terminate(); process.wait(timeout=10)
            self.assertFalse(project_access.editor_busy(project))

    @unittest.skipUnless(os.name == "nt", "Requires Windows file sharing")
    def test_actual_windows_sharing_lock_waits_only_while_handle_is_open(self):
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                           ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        create.restype = wintypes.HANDLE
        close = kernel.CloseHandle; close.argtypes = [wintypes.HANDLE]
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            lock = project / "Temp/UnityLockfile"; lock.parent.mkdir(); lock.touch()
            handle = create(str(lock), 0x80000000, 0, None, 3, 0x80, None)
            self.assertNotEqual(handle, ctypes.c_void_p(-1).value)
            try:
                self.assertTrue(project_access.editor_busy(project))
            finally:
                close(handle)
            self.assertFalse(project_access.editor_busy(project))
            self.assertTrue(lock.exists())

    def test_linux_observes_only_unity_for_the_exact_project(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); project = root / "project with spaces"
            proc = root / "proc"; proc.mkdir()
            for pid, arguments in ((1, ["/opt/Unity/Editor/Unity", "-projectPath", str(root / "other")]),
                                   (2, ["/bin/python3", "-projectPath", str(project)]),
                                   (3, ["/opt/Unity/Editor/Unity", "-projectPath", str(project)])):
                path = proc / str(pid); path.mkdir()
                (path / "cmdline").write_bytes(b"\0".join(arg.encode() for arg in arguments) + b"\0")
            self.assertTrue(project_access._linux_busy(project, proc))
            (proc / "3/cmdline").unlink()
            self.assertFalse(project_access._linux_busy(project, proc))

    def test_stale_unity_lock_is_retained_and_does_not_wait(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            lock = project / "Temp/UnityLockfile"; lock.parent.mkdir()
            lock.write_bytes(b"stale editor lock")
            with patch.object(project_access, "_linux_busy", return_value=False):
                self.assertFalse(project_access.editor_busy(project))
            self.assertEqual(lock.read_bytes(), b"stale editor lock")

    def test_running_editor_waits_then_continues_without_deleting_or_killing(self):
        events = []
        class Progress:
            def event(self, *args, **kwargs): events.append((args, kwargs))
        with patch.object(project_access, "editor_busy", side_effect=[True, True, False]), \
             patch.object(project_access.time, "sleep") as sleep:
            project_access.wait_for_editor(Path("unused"), Progress())
        self.assertEqual(sleep.call_count, 2)
        self.assertEqual(events[0][0][0], "prepare-editor-wait")
        self.assertEqual(events[0][1]["status"], "start")
        self.assertEqual(events[-1][1]["status"], "complete")


if __name__ == "__main__": unittest.main()
