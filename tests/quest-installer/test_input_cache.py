"""Persistent retry-cache controls using actual files and concurrent processes."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_input_cache", REPO / "tools/quest-installer/input_cache.py")
cache = importlib.util.module_from_spec(spec); spec.loader.exec_module(cache)


def witness(path):
    info = path.stat()
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


def worker(cache_path, file_path, gate):
    deadline = time.monotonic() + 8
    while not Path(gate).exists():
        if time.monotonic() > deadline:
            return 3
        time.sleep(.01)
    file_path = Path(file_path)
    result = cache.remember(Path(cache_path), file_path, witness(file_path), hashlib.sha256(file_path.read_bytes()).hexdigest())
    return 0 if result else 4


class InputCacheTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="quest-input-cache-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.file = self.root / "completed APK.apk"
        self.file.write_bytes(b"already hashed complete output")
        self.cache = self.root / "state" / "installer-inputs.json"
        self.sha = hashlib.sha256(self.file.read_bytes()).hexdigest()
        self.stat = witness(self.file)

    def remember(self):
        self.assertTrue(cache.remember(self.cache, self.file, self.stat, self.sha))

    def record(self):
        return {"schema": 1, "entries": [{"path": os.path.normcase(str(self.file.absolute())), "witness": list(self.stat), "sha256": self.sha}]}

    def write_record(self, row):
        self.cache.parent.mkdir(exist_ok=True)
        self.cache.write_text(json.dumps(row), encoding="utf-8")

    def test_unchanged_file_reused_without_reading_payload(self):
        self.remember()
        original = os.open
        opened = []
        def tracked(path, *args, **kwargs):
            opened.append(str(path))
            return original(path, *args, **kwargs)
        with mock.patch.object(cache.os, "open", side_effect=tracked):
            self.assertEqual(cache.lookup(self.cache, self.file, self.stat), self.sha)
        self.assertNotIn(str(self.file), opened)

    def test_new_process_reuses_saved_digest(self):
        self.remember()
        code = ("import importlib.util,sys;from pathlib import Path;"
                "s=importlib.util.spec_from_file_location('cache',sys.argv[1]);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);"
                "p=Path(sys.argv[3]);i=p.stat();w=(i.st_dev,i.st_ino,i.st_size,i.st_mtime_ns,i.st_ctime_ns);"
                "print(m.lookup(Path(sys.argv[2]),p,w))")
        result = subprocess.run([sys.executable, "-I", "-B", "-c", code, str(REPO / "tools/quest-installer/input_cache.py"), str(self.cache), str(self.file)], capture_output=True, text=True, timeout=8)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), self.sha)

    def test_missing_cache_is_a_miss(self):
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_file_size_mutation_invalidates_old_digest(self):
        self.remember()
        self.file.write_bytes(b"short")
        self.assertIsNone(cache.lookup(self.cache, self.file, witness(self.file)))
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_same_size_changed_payload_invalidates_old_digest(self):
        self.remember()
        self.file.write_bytes(b"x" * self.stat[2])
        os.utime(self.file, ns=(self.stat[3] + 10000000, self.stat[3] + 10000000))
        self.assertIsNone(cache.lookup(self.cache, self.file, witness(self.file)))

    def test_replacement_same_size_and_mtime_invalidates_inode(self):
        self.remember()
        replacement = self.root / "replacement.apk"
        replacement.write_bytes(b"y" * self.stat[2])
        os.utime(replacement, ns=(self.stat[3], self.stat[3]))
        os.replace(replacement, self.file)
        self.assertNotEqual(witness(self.file)[1], self.stat[1])
        self.assertIsNone(cache.lookup(self.cache, self.file, witness(self.file)))

    def test_restoring_mtime_does_not_restore_ctime(self):
        self.remember()
        time.sleep(.005)
        self.file.write_bytes(b"z" * self.stat[2])
        os.utime(self.file, ns=(self.stat[3], self.stat[3]))
        self.assertNotEqual(witness(self.file)[4], self.stat[4])
        self.assertIsNone(cache.lookup(self.cache, self.file, witness(self.file)))

    def test_each_witness_component_must_match_actual_stat(self):
        self.remember()
        for position in range(5):
            with self.subTest(position=position):
                changed = list(self.stat); changed[position] += 1
                self.assertIsNone(cache.lookup(self.cache, self.file, changed))
                self.assertFalse(cache.remember(self.cache, self.file, changed, self.sha))

    def test_path_alias_does_not_copy_digest_to_another_file(self):
        self.remember()
        other = self.root / "other.apk"; other.write_bytes(self.file.read_bytes())
        self.assertIsNone(cache.lookup(self.cache, other, witness(other)))

    def test_relative_and_absolute_path_are_same_input(self):
        self.remember()
        relative = Path(os.path.relpath(self.file))
        self.assertEqual(cache.lookup(self.cache, relative, self.stat), self.sha)

    def test_missing_input_is_a_miss_and_cannot_be_remembered(self):
        self.remember(); self.file.unlink()
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))
        self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))

    def test_directory_input_is_not_qualified(self):
        self.assertFalse(cache.remember(self.cache, self.root, witness(self.root), self.sha))

    def test_symlink_input_is_not_qualified(self):
        link = self.root / "linked.apk"
        try: link.symlink_to(self.file)
        except (OSError, NotImplementedError): self.skipTest("Symlinks unavailable")
        self.assertFalse(cache.remember(self.cache, link, witness(link), self.sha))
        self.assertIsNone(cache.lookup(self.cache, link, witness(link)))

    def test_symlink_parent_is_not_qualified(self):
        link = self.root / "alias"
        try: link.symlink_to(self.root, target_is_directory=True)
        except (OSError, NotImplementedError): self.skipTest("Symlinks unavailable")
        aliased = link / self.file.name
        self.assertFalse(cache.remember(self.cache, aliased, witness(aliased), self.sha))

    def test_junction_parent_is_not_qualified(self):
        # Path.is_junction is platform-specific; exercise the Windows admission
        # boundary even when this focused suite runs on a POSIX host.
        with mock.patch.object(Path, "is_junction", create=True, side_effect=lambda: True):
            self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
            self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_hardlinked_input_is_not_qualified(self):
        link = self.root / "linked.apk"
        try: os.link(self.file, link)
        except (OSError, NotImplementedError): self.skipTest("Hardlinks unavailable")
        self.assertFalse(cache.remember(self.cache, self.file, witness(self.file), self.sha))
        self.assertIsNone(cache.lookup(self.cache, self.file, witness(self.file)))

    def test_symlink_cache_is_ignored_without_writing_target(self):
        target = self.root / "target.json"; target.write_text("unchanged")
        self.cache.parent.mkdir()
        try: self.cache.symlink_to(target)
        except (OSError, NotImplementedError): self.skipTest("Symlinks unavailable")
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))
        self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
        self.assertEqual(target.read_text(), "unchanged")

    def test_hardlinked_cache_is_ignored(self):
        target = self.root / "target.json"; target.write_text("unchanged")
        self.cache.parent.mkdir()
        try: os.link(target, self.cache)
        except (OSError, NotImplementedError): self.skipTest("Hardlinks unavailable")
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))
        self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
        self.assertEqual(target.read_text(), "unchanged")

    def test_linked_lock_cannot_change_target(self):
        target = self.root / "target.json"; target.write_text("unchanged")
        self.cache.parent.mkdir()
        lock = self.cache.with_name(self.cache.name + ".lock")
        try: lock.symlink_to(target)
        except (OSError, NotImplementedError): self.skipTest("Symlinks unavailable")
        self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
        self.assertEqual(target.read_text(), "unchanged")

    def test_malformed_and_unbounded_records_are_misses(self):
        variants = [None, [], {"schema": True, "entries": []}, {"schema": 2, "entries": []}]
        for digest in [None, 3, "", "F" * 64, "g" * 64, "a" * 63, "a" * 65]:
            row = self.record(); row["entries"][0]["sha256"] = digest; variants.append(row)
        for stat_value in [None, [], [True] * 5, [-1] * 5, [2 ** 64] * 5, [str(value) for value in self.stat]]:
            row = self.record(); row["entries"][0]["witness"] = stat_value; variants.append(row)
        row = self.record(); row["entries"] *= cache.MAX_ENTRIES + 1; variants.append(row)
        row = self.record(); row["entries"][0]["path"] = "relative.apk"; variants.append(row)
        for row in variants:
            with self.subTest(row=row):
                self.write_record(row)
                self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_duplicate_json_keys_are_not_trusted(self):
        self.cache.parent.mkdir()
        self.cache.write_text('{"schema":2,"schema":1,"entries":[]}')
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_oversized_cache_is_not_read(self):
        self.cache.parent.mkdir()
        self.cache.write_bytes(b" " * (cache.MAX_BYTES + 1))
        with mock.patch.object(cache.os, "open", side_effect=AssertionError("Oversized cache was opened")):
            self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_invalid_cache_is_replaced_by_valid_bounded_record(self):
        self.cache.parent.mkdir()
        self.cache.write_bytes(b"invalid\xff-json")
        self.remember()
        self.assertEqual(cache.lookup(self.cache, self.file, self.stat), self.sha)

    def test_invalid_digest_is_never_saved(self):
        for digest in [None, "", "x" * 64, "A" * 64]:
            self.assertFalse(cache.remember(self.cache, self.file, self.stat, digest))
        self.assertFalse(self.cache.exists())

    def test_unwritable_cache_is_best_effort(self):
        self.cache.parent.mkdir()
        with mock.patch.object(cache.os, "open", side_effect=PermissionError("Read-only cache directory")):
            self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
            self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))
        self.assertEqual(hashlib.sha256(self.file.read_bytes()).hexdigest(), self.sha)

    def test_unreadable_existing_cache_is_a_miss(self):
        self.remember()
        with mock.patch.object(cache.os, "open", side_effect=PermissionError("Cache inaccessible")):
            self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_cache_below_linked_parent_is_ignored(self):
        physical = self.root / "physical"; physical.mkdir()
        alias = self.root / "alias"
        try: alias.symlink_to(physical, target_is_directory=True)
        except (OSError, NotImplementedError): self.skipTest("Symlinks unavailable")
        linked_cache = alias / "cache.json"
        self.assertFalse(cache.remember(linked_cache, self.file, self.stat, self.sha))
        self.assertFalse((physical / "cache.json").exists())

    def test_failed_atomic_replace_preserves_previous_cache(self):
        self.remember(); before = self.cache.read_bytes()
        with mock.patch.object(cache.os, "replace", side_effect=PermissionError("File busy")):
            self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
        self.assertEqual(self.cache.read_bytes(), before)
        self.assertFalse(list(self.cache.parent.glob("*.tmp")))

    def test_mutation_during_cache_read_is_not_reused(self):
        self.remember(); original = cache._read
        def replacing(path):
            result = original(path); self.file.write_bytes(b"changed during cache read"); return result
        with mock.patch.object(cache, "_read", side_effect=replacing):
            self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))

    def test_mutation_during_atomic_write_is_not_saved(self):
        original = cache.os.fsync
        def changed(descriptor):
            original(descriptor); self.file.write_bytes(b"changed during cache write")
        with mock.patch.object(cache.os, "fsync", side_effect=changed):
            self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
        self.assertFalse(self.cache.exists())
        self.assertFalse(list(self.cache.parent.glob("*.tmp")))

    def test_entry_bound_evicts_oldest_input(self):
        self.remember()
        for number in range(cache.MAX_ENTRIES):
            path = self.root / (str(number) + ".apk"); path.write_bytes(str(number).encode())
            self.assertTrue(cache.remember(self.cache, path, witness(path), hashlib.sha256(path.read_bytes()).hexdigest()))
        entries = json.loads(self.cache.read_text())["entries"]
        self.assertEqual(len(entries), cache.MAX_ENTRIES)
        self.assertIsNone(cache.lookup(self.cache, self.file, self.stat))
        self.assertLess(self.cache.stat().st_size, cache.MAX_BYTES)

    def test_parallel_process_writes_preserve_all_complete_records(self):
        files = []
        for number in range(8):
            path = self.root / ("parallel-" + str(number) + ".apk")
            path.write_bytes(("complete file " + str(number)).encode()); files.append(path)
        gate = self.root / "start"
        processes = [subprocess.Popen([sys.executable, "-I", "-B", str(Path(__file__).resolve()), "--writer", str(self.cache), str(path), str(gate)], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE) for path in files]
        try:
            gate.touch()
            deadline = time.monotonic() + 10
            while any(process.poll() is None for process in processes):
                if self.cache.exists():
                    row = json.loads(self.cache.read_bytes())
                    self.assertLessEqual(len(row["entries"]), cache.MAX_ENTRIES)
                if time.monotonic() > deadline:
                    self.fail("Concurrent cache writers exceeded deadline")
                time.sleep(.002)
            for process in processes:
                _, error = process.communicate(timeout=2)
                self.assertEqual(process.returncode, 0, error.decode(errors="replace"))
        finally:
            for process in processes:
                if process.poll() is None: process.kill()
                process.wait(timeout=3)
                if process.stderr: process.stderr.close()
        self.assertEqual(len(json.loads(self.cache.read_text())["entries"]), len(files))
        for path in files:
            self.assertEqual(cache.lookup(self.cache, path, witness(path)), hashlib.sha256(path.read_bytes()).hexdigest())

    @unittest.skipUnless(cache.fcntl is not None, "Actual POSIX lock control")
    def test_busy_cache_lock_falls_back_within_short_bound(self):
        self.remember()
        lock = self.cache.with_name(self.cache.name + ".lock")
        with lock.open("a+b") as held:
            cache.fcntl.flock(held, cache.fcntl.LOCK_EX | cache.fcntl.LOCK_NB)
            try:
                started = time.monotonic()
                self.assertFalse(cache.remember(self.cache, self.file, self.stat, self.sha))
                self.assertLess(time.monotonic() - started, cache.LOCK_SECONDS + .4)
            finally:
                cache.fcntl.flock(held, cache.fcntl.LOCK_UN)


if __name__ == "__main__":
    if len(sys.argv) == 5 and sys.argv[1] == "--writer":
        raise SystemExit(worker(*sys.argv[2:]))
    unittest.main()
