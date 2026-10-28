"""Owned deletion repairs exact Windows attributes without broad permission edits."""
import errno
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup_io
from state import Cancelled, WizardError


def windows_error(code):
    error = PermissionError(errno.EACCES, "simulated Windows denial")
    error.winerror = code
    return error


class CleanupIoTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "owned"; self.root.mkdir()
        self.leaf = self.root / "cache/file.bin"; self.leaf.parent.mkdir()
        self.leaf.write_bytes(b"accepted generated file")
        self.events = []

    def stamp(self, path):
        path = Path(path)
        for entry in (path, *path.parents):
            info = os.lstat(entry)
            if stat.S_ISLNK(info.st_mode):
                raise WizardError("cleanup_linked_path", "No links.")
        info = os.lstat(path)
        if (not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode))
                or (stat.S_ISREG(info.st_mode) and info.st_nlink != 1)):
            raise WizardError("cleanup_linked_path", "No hardlinks or special files.")
        return (info.st_dev, info.st_ino, info.st_mode, info.st_size,
                info.st_mtime_ns, info.st_ctime_ns, None)

    def remove(self, path=None, expected=None, **options):
        path = path or self.leaf
        cleanup_io.delete_owned(path, expected or self.stamp(path), self.stamp,
                                owned_root=self.root, progress=self.events.append, **options)

    def readonly_info(self, path, *args, **kwargs):
        info = os.lstat(path)
        return SimpleNamespace(st_mode=info.st_mode, st_nlink=info.st_nlink,
                               st_file_attributes=cleanup_io._READONLY)

    def fail_once(self, error, method="unlink"):
        original = getattr(Path, method); calls = []
        def invoke(path, *args, **kwargs):
            calls.append(path)
            if len(calls) == 1:
                raise error
            return original(path, *args, **kwargs)
        return calls, mock.patch.object(Path, method, invoke)

    def test_normal_deletion_never_changes_permissions(self):
        with mock.patch.object(cleanup_io.os, "chmod") as chmod:
            self.remove()
        chmod.assert_not_called()
        self.assertFalse(self.leaf.exists()); self.assertEqual(self.events, [])

    def test_windows_access_denied_repairs_only_exact_readonly_file_and_retries(self):
        self.leaf.chmod(0o444); original_chmod = os.chmod
        calls, fail = self.fail_once(windows_error(5))
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod", wraps=original_chmod) as chmod:
            self.remove()
        self.assertEqual(len(calls), 2); self.assertFalse(self.leaf.exists())
        chmod.assert_called_once_with(self.leaf, 0o644, follow_symlinks=False)
        self.assertEqual([row["phase"] for row in self.events], ["cleanup-readonly-repair"] * 2)
        self.assertEqual([row["done"] for row in self.events], [0, 1])
        self.assertEqual(self.events[-1]["detail"], "cache/file.bin")

    def test_readonly_directory_repair_is_exact_and_empty_only(self):
        folder = self.root / "empty"; folder.mkdir(); folder.chmod(0o555)
        calls, fail = self.fail_once(windows_error(5), "rmdir")
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)):
            self.remove(folder)
        self.assertEqual(len(calls), 2); self.assertFalse(folder.exists())
        self.assertTrue(self.leaf.exists())

    def test_plain_access_denied_is_actionable_without_permissions_repair(self):
        with mock.patch.object(Path, "unlink", side_effect=windows_error(5)), \
                mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(cleanup_io.os, "chmod") as chmod:
            with self.assertRaises(WizardError) as caught:
                self.remove()
        chmod.assert_not_called(); self.assertTrue(self.leaf.exists())
        self.assertEqual(caught.exception.code, "cleanup_file_denied")
        self.assertTrue(caught.exception.parameters["retryable"])
        self.assertEqual(caught.exception.parameters["path"], "cache/file.bin")
        self.assertIn("Löschvorschau", caught.exception.message["de"])

    def test_posix_denial_does_not_chmod_even_with_simulated_readonly_bit(self):
        with mock.patch.object(Path, "unlink", side_effect=PermissionError(errno.EACCES, "denied")), \
                mock.patch.object(cleanup_io, "_windows_host", return_value=False), \
                mock.patch.object(cleanup_io.os, "chmod") as chmod:
            with self.assertRaises(WizardError):
                self.remove()
        chmod.assert_not_called()

    def test_sharing_violation_retries_with_exact_witness_and_progress(self):
        calls, fail = self.fail_once(windows_error(32))
        with fail, mock.patch.object(cleanup_io.time, "sleep") as sleep:
            self.remove()
        self.assertEqual(len(calls), 2); self.assertFalse(self.leaf.exists())
        sleep.assert_called_once_with(.05)
        self.assertEqual(self.events[0]["phase"], "cleanup-delete-retry")

    def test_permanent_sharing_violation_has_bounded_retries(self):
        with mock.patch.object(Path, "unlink", side_effect=windows_error(33)) as unlink, \
                mock.patch.object(cleanup_io.time, "sleep") as sleep:
            with self.assertRaises(WizardError) as caught:
                self.remove()
        self.assertEqual(unlink.call_count, 5); self.assertEqual(sleep.call_count, 4)
        self.assertEqual(caught.exception.code, "cleanup_file_busy")
        self.assertEqual(caught.exception.parameters["recovery"], "refresh-preview")
        self.assertTrue(self.leaf.exists())

    def test_cancelled_before_delete_preserves_file(self):
        cancel = threading.Event(); cancel.set()
        with mock.patch.object(Path, "unlink") as unlink:
            with self.assertRaises(Cancelled):
                self.remove(cancel=cancel)
        unlink.assert_not_called(); self.assertTrue(self.leaf.exists())

    def test_cancellation_interrupts_sharing_wait(self):
        cancel = threading.Event()
        def report(row):
            self.events.append(row); cancel.set()
        with mock.patch.object(Path, "unlink", side_effect=windows_error(32)) as unlink:
            with self.assertRaises(Cancelled):
                cleanup_io.delete_owned(self.leaf, self.stamp(self.leaf), self.stamp,
                    owned_root=self.root, cancel=cancel, progress=report)
        self.assertEqual(unlink.call_count, 1); self.assertTrue(self.leaf.exists())

    def test_changed_preview_is_rejected_without_deletion(self):
        accepted = self.stamp(self.leaf); self.leaf.write_bytes(b"changed")
        with mock.patch.object(Path, "unlink") as unlink:
            with self.assertRaises(WizardError) as caught:
                self.remove(expected=accepted)
        unlink.assert_not_called(); self.assertEqual(caught.exception.code, "cleanup_preview_changed")

    def test_changed_file_during_retry_is_rejected(self):
        def deny_and_modify(path):
            self.leaf.write_bytes(b"replacement content"); raise windows_error(32)
        with mock.patch.object(Path, "unlink", deny_and_modify) as unlink, \
                mock.patch.object(cleanup_io.time, "sleep"):
            with self.assertRaises(WizardError) as caught:
                self.remove()
        self.assertEqual(caught.exception.code, "cleanup_preview_changed")
        self.assertEqual(self.leaf.read_bytes(), b"replacement content")

    def test_changed_file_during_attribute_repair_is_rejected(self):
        replacement = self.root / "replacement"; replacement.write_bytes(b"replacement content")
        def replace_instead_of_chmod(path, mode, **options):
            os.replace(replacement, self.leaf)
        calls, fail = self.fail_once(windows_error(5))
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod", replace_instead_of_chmod):
            with self.assertRaises(WizardError) as caught:
                self.remove()
        self.assertEqual(len(calls), 1); self.assertEqual(caught.exception.code, "cleanup_preview_changed")
        self.assertEqual(self.leaf.read_bytes(), b"replacement content")

    def test_repair_denial_is_actionable_and_does_not_retry_deletion(self):
        calls, fail = self.fail_once(windows_error(5))
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod", side_effect=windows_error(5)):
            with self.assertRaises(WizardError) as caught:
                self.remove()
        self.assertEqual(len(calls), 1); self.assertEqual(caught.exception.code, "cleanup_file_denied")

    def test_native_witness_is_promoted_from_metadata_only_preview(self):
        before = self.stamp(self.leaf)
        strong = before[:6] + (("win32-v1", 42, "native-file-id", before[3], 1234, 5678),)
        with mock.patch.object(Path, "unlink", side_effect=[windows_error(32), None]), \
                mock.patch.object(cleanup_io.time, "sleep"):
            cleanup_io.delete_owned(self.leaf, before, lambda path: strong,
                                    owned_root=self.root)

    def test_native_identity_change_during_repair_is_rejected(self):
        before = self.stamp(self.leaf)
        first = before[:6] + (("win32-v1", 42, "accepted-id", before[3], 1234, 5678),)
        changed = before[:6] + (("win32-v1", 42, "different-id", before[3], 1234, 6789),)
        calls, fail = self.fail_once(windows_error(5))
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod"):
            with self.assertRaises(WizardError) as caught:
                cleanup_io.delete_owned(self.leaf, first, mock.Mock(side_effect=[first, first, changed]),
                                        owned_root=self.root)
        self.assertEqual(len(calls), 1); self.assertEqual(caught.exception.code, "cleanup_preview_changed")

    def test_native_attribute_change_retains_identity_then_deletes(self):
        before = self.stamp(self.leaf)
        first = before[:6] + (("win32-v1", 42, "accepted-id", before[3], 1234, 5678),)
        after = (before[0], before[1], before[2] | stat.S_IWRITE, before[3], before[4], before[5] + 1,
                 ("win32-v1", 42, "accepted-id", before[3], 1234, 6789))
        calls, fail = self.fail_once(windows_error(5))
        with fail, mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod"):
            cleanup_io.delete_owned(self.leaf, first, mock.Mock(side_effect=[first, first, after, after]),
                                    owned_root=self.root)
        self.assertEqual(len(calls), 2); self.assertFalse(self.leaf.exists())

    def test_witness_read_denial_is_actionable_before_delete(self):
        accepted = self.stamp(self.leaf)
        with mock.patch.object(Path, "unlink") as unlink:
            with self.assertRaises(WizardError) as caught:
                cleanup_io.delete_owned(self.leaf, accepted, mock.Mock(side_effect=windows_error(5)),
                                        owned_root=self.root)
        unlink.assert_not_called(); self.assertEqual(caught.exception.code, "cleanup_file_denied")

    def test_missing_accepted_leaf_requires_a_new_preview(self):
        accepted = self.stamp(self.leaf); self.leaf.unlink()
        with self.assertRaises(WizardError) as caught:
            self.remove(expected=accepted)
        self.assertEqual(caught.exception.code, "cleanup_preview_changed")

    def test_changed_file_before_attribute_repair_never_chmods(self):
        accepted = self.stamp(self.leaf)
        def deny_and_modify(path):
            self.leaf.write_bytes(b"changed after denial"); raise windows_error(5)
        with mock.patch.object(Path, "unlink", deny_and_modify), \
                mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", lambda path: self.readonly_info(path)), \
                mock.patch.object(cleanup_io.os, "chmod") as chmod:
            with self.assertRaises(WizardError) as caught:
                self.remove(expected=accepted)
        chmod.assert_not_called(); self.assertEqual(caught.exception.code, "cleanup_preview_changed")

    def test_reparse_point_after_denial_is_never_repaired(self):
        def reparse_info(path):
            info = self.readonly_info(path)
            info.st_file_attributes |= cleanup_io._REPARSE
            return info
        with mock.patch.object(Path, "unlink", side_effect=windows_error(5)), \
                mock.patch.object(cleanup_io, "_windows_host", return_value=True), \
                mock.patch.object(Path, "lstat", reparse_info), \
                mock.patch.object(cleanup_io.os, "chmod") as chmod:
            with self.assertRaises(WizardError) as caught:
                self.remove()
        chmod.assert_not_called(); self.assertEqual(caught.exception.code, "cleanup_preview_changed")

    def test_directory_child_removal_does_not_invalidate_directory_identity(self):
        folder = self.leaf.parent; accepted = self.stamp(folder); self.leaf.unlink()
        self.remove(folder, accepted)
        self.assertFalse(folder.exists())

    def test_nonempty_directory_is_never_recursively_deleted(self):
        with self.assertRaises(OSError):
            self.remove(self.leaf.parent)
        self.assertTrue(self.leaf.exists())

    def test_external_path_and_traversal_are_rejected(self):
        external = self.root.parent / "external"; external.write_bytes(b"original")
        for candidate in (external, self.root / "cache/../../external", self.root):
            with self.subTest(path=candidate):
                with self.assertRaises(WizardError):
                    self.remove(candidate, self.stamp(external))
        self.assertEqual(external.read_bytes(), b"original")

    def test_hardlink_and_symlink_leaf_are_rejected(self):
        accepted = self.stamp(self.leaf)
        linked = self.root / "hardlinked"; os.link(self.leaf, linked)
        with self.assertRaises(WizardError):
            self.remove(expected=accepted)
        linked.unlink()
        original = self.root / "original"; os.replace(self.leaf, original)
        self.leaf.symlink_to(original)
        with self.assertRaises(WizardError):
            self.remove(expected=accepted)
        self.assertTrue(original.exists())

    def test_linked_parent_is_rejected(self):
        accepted = self.stamp(self.leaf); original = self.root / "original-cache"
        os.replace(self.leaf.parent, original); self.leaf.parent.symlink_to(original, target_is_directory=True)
        with self.assertRaises(WizardError):
            self.remove(expected=accepted)
        self.assertTrue((original / self.leaf.name).exists())

    @unittest.skipUnless(shutil.which("git") and os.name == "posix", "Requires POSIX Git")
    def test_real_git_reverse_index_is_readonly_but_posix_delete_needs_no_chmod(self):
        repo = self.root / "source"; repo.mkdir()
        def git(*args):
            subprocess.run(["git", "-C", str(repo), *args], check=True,
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        git("init", "-q"); (repo / "source.txt").write_text("owned snapshot fixture\n")
        git("add", "source.txt")
        git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "fixture")
        git("-c", "pack.writeReverseIndex=true", "repack", "-ad")
        indexes = list((repo / ".git/objects/pack").glob("*.rev"))
        self.assertEqual(len(indexes), 1)
        self.assertFalse(indexes[0].stat().st_mode & stat.S_IWUSR)
        with mock.patch.object(cleanup_io.os, "chmod") as chmod:
            self.remove(indexes[0])
        chmod.assert_not_called(); self.assertFalse(indexes[0].exists())


if __name__ == "__main__":
    unittest.main()
