"""Real reads and modeled Windows timestamp seams, with mutation controls."""
from contextlib import redirect_stdout, redirect_stderr
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import campaign_native_shaders as gate


def changed_stat(value, **changes):
    fields = {name: getattr(value, name) for name in
        ('st_dev', 'st_ino', 'st_size', 'st_mtime_ns', 'st_ctime_ns', 'st_mode')}
    fields.update(changes)
    return SimpleNamespace(**fields)


class NativeFileReadTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / 'owned native-ß.bundle'
        self.raw = b'actual bank bytes\n' * 41
        self.path.write_bytes(self.raw)
        self.identities = {}
        self.fstat = os.fstat

    def read(self, **options):
        return gate._read_native_file(self.path, self.identities, **options)

    def test_regular_file_bytes_read_in_bounded_chunks_and_retains_exact_path_stamp(self):
        counter = gate._NativeAuditCounter('unity-native-shader-audit-read', len(self.raw), 'bytes')
        with patch.object(gate, 'NATIVE_READ_CHUNK', 37), patch.object(counter, 'add', wraps=counter.add) as measured:
            self.assertEqual(self.read(progress=counter), self.raw)
        self.assertEqual(counter.done, len(self.raw))
        self.assertGreater(measured.call_count, 1)
        self.assertTrue(all(call.args[0] <= 37 for call in measured.call_args_list))
        self.assertEqual(self.identities[self.path], gate._file_identity(self.path))

    def test_windows_creation_and_change_times_differ_but_each_api_is_stable(self):
        def windows(fd):
            value = self.fstat(fd)
            return changed_stat(value, st_ctime_ns=value.st_ctime_ns + 123456789)
        with patch.object(gate.sys, 'platform', 'win32'), patch.object(gate.os, 'fstat', side_effect=windows) as observe:
            self.assertEqual(self.read(), self.raw)
        self.assertEqual(observe.call_count, 2)
        self.assertEqual(self.identities[self.path], gate._file_identity(self.path))

    def test_posix_unexplained_cross_api_ctime_difference_is_rejected(self):
        with patch.object(gate.sys, 'platform', 'linux'), patch.object(gate.os, 'fstat',
                side_effect=lambda fd: changed_stat(self.fstat(fd), st_ctime_ns=17)):
            with self.assertRaisesRegex(gate.ClosureError, 'path-before/opened-handle'): self.read()
        self.assertEqual(self.identities, {})

    def test_windows_common_identity_size_and_mtime_are_never_waived(self):
        for field in ('st_dev', 'st_ino', 'st_size', 'st_mtime_ns'):
            with self.subTest(field=field), patch.object(gate.sys, 'platform', 'win32'), \
                    patch.object(gate.os, 'fstat', side_effect=lambda fd: changed_stat(self.fstat(fd),
                        **{field: getattr(self.fstat(fd), field) + 1})):
                with self.assertRaisesRegex(gate.ClosureError, 'path-before/opened-handle'): self.read()
        self.assertEqual(self.identities, {})

    def test_full_open_handle_guard_rejects_ctime_only_changes_on_windows(self):
        calls = []
        def changing(fd):
            calls.append(fd); value = self.fstat(fd)
            return changed_stat(value, st_ctime_ns=value.st_ctime_ns + len(calls))
        with patch.object(gate.sys, 'platform', 'win32'), patch.object(gate.os, 'fstat', side_effect=changing):
            with self.assertRaisesRegex(gate.ClosureError, 'handle-before/after-read'): self.read()
        self.assertEqual(self.identities, {})

    def test_full_open_handle_guard_rejects_all_common_field_changes(self):
        for field in ('st_dev', 'st_ino', 'st_size', 'st_mtime_ns'):
            calls = []
            def changing(fd):
                calls.append(fd); value = self.fstat(fd)
                return value if len(calls) == 1 else changed_stat(value, **{field: getattr(value, field) + 1})
            with self.subTest(field=field), patch.object(gate.os, 'fstat', side_effect=changing):
                with self.assertRaisesRegex(gate.ClosureError, 'handle-before/after-read'): self.read()
        self.assertEqual(self.identities, {})

    def test_full_path_pre_post_guard_rejects_ctime_only_change_on_windows(self):
        before = gate._file_identity(self.path)
        after = (*before[:4], before[4] + 1)
        with patch.object(gate.sys, 'platform', 'win32'), patch.object(gate, '_file_identity', side_effect=[before, after]):
            with self.assertRaisesRegex(gate.ClosureError, 'path-before/after-read'): self.read()
        self.assertEqual(self.identities, {})

    def test_closed_path_guard_rejects_same_size_new_identity(self):
        before = gate._file_identity(self.path)
        after = (before[0], before[1] + 1, *before[2:])
        with patch.object(gate, '_file_identity', side_effect=[before, before, after]):
            with self.assertRaisesRegex(gate.ClosureError, 'path-after-close'): self.read()
        self.assertEqual(self.identities, {})

    def test_real_replacement_between_path_observation_and_open_is_rejected(self):
        original_open = Path.open; stamp = self.path.stat()
        replacement = self.path.with_name('replacement.bundle'); replacement.write_bytes(self.raw)
        os.utime(replacement, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        def replace(path, *args, **kwargs):
            if path == self.path: replacement.replace(path)
            return original_open(path, *args, **kwargs)
        with patch.object(Path, 'open', replace):
            with self.assertRaisesRegex(gate.ClosureError, 'path-before/opened-handle'): self.read()
        self.assertEqual(self.identities, {})

    def test_actual_same_size_write_with_restored_mtime_during_read_is_rejected(self):
        original_open = Path.open; changed = []
        stamp = self.path.stat()
        counter = gate._NativeAuditCounter('unity-native-shader-audit-read', len(self.raw), 'bytes')
        def mutate(amount, detail):
            if changed: return
            changed.append(True)
            with original_open(self.path, 'r+b') as writer: writer.write(b'other bytes')
            os.utime(self.path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        with patch.object(counter, 'add', side_effect=mutate), patch.object(gate, 'NATIVE_READ_CHUNK', 37):
            with self.assertRaisesRegex(gate.ClosureError, 'handle-before/after-read'): self.read(progress=counter)
        self.assertEqual(self.identities, {})

    def test_growth_after_open_is_bounded_and_rejected(self):
        original_open = Path.open; changed = []
        counter = gate._NativeAuditCounter('unity-native-shader-audit-read', len(self.raw), 'bytes')
        def grow(amount, detail):
            if changed: return
            changed.append(True)
            with original_open(self.path, 'ab') as writer: writer.write(b'growth')
        with patch.object(counter, 'add', side_effect=grow), patch.object(gate, 'NATIVE_READ_CHUNK', 37):
            with self.assertRaisesRegex(gate.ClosureError, 'read-extent'): self.read(progress=counter)
        self.assertEqual(self.identities, {})

    def test_short_read_with_modeled_unchanged_metadata_is_rejected(self):
        before = gate._file_identity(self.path)
        real = self.path.stat(); fake = changed_stat(real)
        class ShortStream(io.BytesIO):
            def fileno(self): return 12
        with patch.object(gate, '_file_identity', return_value=before), patch.object(gate.os, 'fstat', return_value=fake), \
                patch.object(Path, 'open', return_value=ShortStream(self.raw[:-1])):
            with self.assertRaisesRegex(gate.ClosureError, 'read-length'): self.read()
        self.assertEqual(self.identities, {})

    def test_symbolic_file_leaf_is_rejected_before_any_read(self):
        actual = self.path.with_name('actual.bundle'); self.path.rename(actual)
        self.path.symlink_to(actual)
        with patch.object(Path, 'open', side_effect=AssertionError('linked bytes read')):
            with self.assertRaisesRegex(gate.ClosureError, 'regular unlinked file'): self.read()
        self.assertEqual(self.identities, {})

    def test_windows_reparse_attribute_is_rejected_before_any_read(self):
        observed = changed_stat(self.path.lstat(), st_file_attributes=0x400)
        with patch.object(Path, 'lstat', return_value=observed), patch.object(Path, 'open', side_effect=AssertionError('linked bytes read')):
            with self.assertRaisesRegex(gate.ClosureError, 'regular unlinked file'): self.read()
        self.assertEqual(self.identities, {})

    def test_failure_diagnostics_include_exact_path_check_and_both_full_stamps(self):
        before = gate._file_identity(self.path); after = (*before[:4], before[4] + 1)
        with patch.object(gate, '_file_identity', side_effect=[before, after]):
            with self.assertRaises(gate.ClosureError) as error: self.read()
        evidence = error.exception.file_diagnostics
        self.assertEqual(evidence['path'], str(self.path))
        self.assertEqual(evidence['check'], 'path-before/after-read')
        self.assertEqual(evidence['before'], before); self.assertEqual(evidence['after'], after)
        self.assertEqual(evidence['stampFields'][-1], 'ctimeNs')
        self.assertLess(len(json.dumps(evidence)), 2048)

    def test_cli_failure_preserves_structured_metadata_in_one_stderr_line(self):
        before = gate._file_identity(self.path)
        with self.assertRaises(gate.ClosureError) as caught:
            gate._require_stable_file(False, self.path, 'fixture-mutation', before, before)
        output = io.StringIO()
        with patch.object(gate, 'validate_native_directory', side_effect=caught.exception), redirect_stderr(output):
            self.assertEqual(gate.main(['--source', '.', '--project', '.', '--native-root', '.', '--evidence', 'receipt.json']), 1)
        self.assertEqual(len(output.getvalue().splitlines()), 1)
        self.assertEqual(json.loads(output.getvalue())['fileDiagnostics']['check'], 'fixture-mutation')


class NativeAuditCounterTests(unittest.TestCase):
    def test_absent_opt_in_does_not_emit_progress(self):
        output = io.StringIO()
        with patch.dict(os.environ, {'GHVRQ_WIZARD_PROGRESS': '0'}), redirect_stdout(output):
            counter = gate._NativeAuditCounter('unity-native-shader-audit-read', 300, 'bytes')
            counter.add(300, 'actual.bundle'); counter.publish('complete', force=True)
        self.assertFalse(output.getvalue())

    def test_measured_counts_publish_at_bounded_cadence_and_force_final_progress(self):
        output = io.StringIO(); clock = [0.]
        with patch.dict(os.environ, {'GHVRQ_WIZARD_PROGRESS': '1'}), redirect_stdout(output), \
                patch.object(gate.time, 'monotonic', side_effect=lambda: clock[0]):
            counter = gate._NativeAuditCounter('unity-native-shader-audit', 101, 'bundles')
            for _ in range(100): counter.add(1, 'actual.bundle')
            clock[0] = .3; counter.add(1, 'last.bundle'); counter.publish('progress', force=True)
        rows = [json.loads(line.removeprefix('GHVRQ_PROGRESS ')) for line in output.getvalue().splitlines()]
        self.assertEqual([(row['done'], row['total']) for row in rows], [(0, 101), (101, 101), (101, 101)])
        self.assertEqual([row['status'] for row in rows], ['start', 'progress', 'progress'])
        self.assertTrue(all(row['operation'] == 'content-bank' for row in rows))
        self.assertTrue(all(len(line) < 2048 for line in output.getvalue().splitlines()))

    def test_counts_exceeding_actual_census_and_noninteger_work_are_rejected(self):
        counter = gate._NativeAuditCounter('unity-native-shader-audit', 1, 'bundles')
        for value in (2, -1, .2, True):
            with self.subTest(value=value), self.assertRaises(gate.ClosureError): counter.add(value, 'actual.bundle')
        self.assertEqual(counter.done, 0)

    def test_partial_work_cannot_publish_completion_even_without_progress_opt_in(self):
        counter = gate._NativeAuditCounter('unity-native-shader-audit-read', 30, 'bytes')
        counter.add(10, 'actual.bundle')
        with self.assertRaisesRegex(gate.ClosureError, 'Unfinished'): counter.publish('complete', force=True)


if __name__ == '__main__': unittest.main()
