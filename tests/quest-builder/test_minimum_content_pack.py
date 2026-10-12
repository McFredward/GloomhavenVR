"""Actual raw ZIP reuse, ZIP64, atomic failure and minimum readback."""
import hashlib
import json
import os
import subprocess
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
import native_content_pack as packer
import builder


class MinimumContentPack(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.native = self.root / 'native'; self.native.mkdir()
        (self.native / 'bank.bundle').write_bytes(b'UnityFS\0Android compressed bytes' * 100)
        self.archive = self.root / 'Assets/StreamingAssets/quest-startup-content.zip'
        self.archive.parent.mkdir(parents=True)
        self.name = 'StreamingAssets/Movies/original.mp4'; self.value = b'original captured content' * 100
        with zipfile.ZipFile(self.archive, 'w', compression=zipfile.ZIP_DEFLATED) as stream:
            with stream.open(self.name, 'w', force_zip64=True) as target: target.write(self.value)
        self.manifest = self.root / packer.MANIFEST; self.manifest.parent.mkdir(parents=True)
        self.manifest.write_text(json.dumps({'schema': 1, 'inputKey': 'a' * 64,
            'archive': self.archive.name, 'archiveSha256': packer.digest(self.archive),
            'files': [{'path': self.name, 'size': len(self.value), 'sha256': hashlib.sha256(self.value).hexdigest()}]}))
        self.request = {'schema': 1, 'projectRoot': str(self.root), 'nativeRoot': str(self.native)}
        self.env = patch.dict(os.environ, {'GHVR_QUEST_VERIFICATION': 'normal', 'GHVR_QUEST_VALIDATE_IMPORTED_ASSETS': '0'})
        self.env.start(); self.addCleanup(self.env.stop)

    def test_normal_pack_never_inflates_retained_files_and_preserves_actual_content(self):
        original_open = zipfile.ZipFile.open
        def checked(archive, name, mode='r', *args, **kwargs):
            if mode == 'r': raise AssertionError('retained payload inflated')
            return original_open(archive, name, mode, *args, **kwargs)
        with patch.object(zipfile.ZipFile, 'open', checked): first = packer.pack(self.request)
        self.assertTrue(first['compressedRecordsCopied']); self.assertFalse(first['allEntryBytesVerified'])
        with zipfile.ZipFile(self.archive) as stream:
            self.assertEqual(stream.read(self.name), self.value); self.assertIsNone(stream.testzip())
            self.assertEqual(stream.getinfo('StreamingAssets/aa/bank.bundle').compress_type, zipfile.ZIP_STORED)
        with patch.object(zipfile.ZipFile, 'open', side_effect=AssertionError('warm entry reread')):
            second = packer.pack(self.request)
        self.assertTrue(second['reused']); self.assertTrue(second['archiveBytesVerified'])
        self.assertEqual(first['archiveSha256'], second['archiveSha256'])

    def test_raw_copy_retains_zip64_compressed_payload_and_regenerates_offsets(self):
        with patch.object(zipfile, 'ZIP64_LIMIT', 1024): result = packer.pack(self.request)
        with zipfile.ZipFile(self.archive) as stream:
            self.assertEqual(stream.read(self.name), self.value); self.assertIsNone(stream.testzip())
        self.assertIn(b'PK\x06\x06', self.archive.read_bytes())
        self.assertEqual(result['archiveSha256'], packer.digest(self.archive))

    def test_truncated_compressed_record_does_not_replace_accepted_archive(self):
        before = self.archive.read_bytes(), self.manifest.read_bytes()
        with patch.object(packer, 'copy_compressed_entry', side_effect=OSError('interrupted copy')):
            with self.assertRaises(OSError): packer.pack(self.request)
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))
        self.assertEqual(list(self.archive.parent.glob('*.repack-*')), [])

    def test_archive_mutation_still_fails_before_copy_or_native_reads(self):
        with self.archive.open('ab') as stream: stream.write(b'changed')
        with patch.object(packer, 'copy_compressed_entry', side_effect=AssertionError('unqualified archive copied')):
            with self.assertRaisesRegex(ValueError, 'archive changed'): packer.pack(self.request)

    def test_source_replacement_between_qualification_and_copy_is_not_published(self):
        before = self.manifest.read_bytes(); read = packer.digest
        changed = False
        def mutate(path, progress=None):
            nonlocal changed
            result = read(path, progress)
            if path == self.native / 'bank.bundle' and not changed:
                # Replace with an equally valid ZIP after the earlier SHA pass.
                replacement = self.archive.with_suffix('.replacement')
                with zipfile.ZipFile(replacement, 'w') as stream:
                    stream.writestr(self.name, b'changed content'.ljust(len(self.value), b' '))
                os.replace(replacement, self.archive); changed = True
            return result
        with patch.object(packer, 'digest', mutate):
            with self.assertRaisesRegex(ValueError, 'changed during'): packer.pack(self.request)
        self.assertEqual(before, self.manifest.read_bytes())
        self.assertEqual(list(self.archive.parent.glob('*.repack-*')), [])

    def test_startup_pack_hashes_in_transfer_and_preserves_previous_archive_on_failure(self):
        source = self.archive.parent / 'Rulebase/rules.json'; source.parent.mkdir(); source.write_bytes(b'{"rules":true}')
        with patch.object(builder, 'digest', wraps=builder.digest) as reads:
            result = builder.package_startup_content(self.root, 'b' * 64, retain_sources=True)
        self.assertFalse(any(call.args[0] == source for call in reads.call_args_list))
        self.assertEqual(result['files'][0]['sha256'], hashlib.sha256(source.read_bytes()).hexdigest())
        before = self.archive.read_bytes()
        with patch.object(builder.zipfile.ZipFile, 'open', side_effect=OSError('fixture disk failure')):
            with self.assertRaises(OSError): builder.package_startup_content(self.root, 'b' * 64, retain_sources=True)
        self.assertEqual(before, self.archive.read_bytes())
        self.assertEqual(list(self.archive.parent.glob('*.packing-*')), [])

    def test_plugin_and_content_censuses_prune_native_runtime_payload_before_walking(self):
        plugins = self.root / 'Assets/Plugins'; plugins.mkdir()
        for path in (plugins / 'GH.Runtime.dll', plugins / 'StreamingAssets/actual-plugin.dll',
                     self.archive.parent / 'Quest/large-runtime/native.dll'):
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b'fixture')
        walk = os.walk; observed = []
        def tracked(*args, **kwargs):
            for row in walk(*args, **kwargs):
                observed.append(Path(row[0])); yield row
        with patch.object(builder.os, 'walk', tracked): paths = builder.project_plugin_paths(self.root)
        self.assertEqual({path.name for path in paths}, {'GH.Runtime.dll', 'actual-plugin.dll'})
        self.assertNotIn(self.archive.parent, observed)
        source = self.archive.parent / 'Rulebase/rules.json'; source.parent.mkdir(); source.write_bytes(b'{}')
        movie = self.archive.parent / 'Movies/intro.mp4'; movie.parent.mkdir(); movie.write_bytes(b'encoded video')
        pending = self.archive.with_name(self.archive.name + '.packing-' + 'd' * 32); pending.write_bytes(b'interrupted old scratch')
        observed.clear()
        with patch.object(builder.os, 'walk', tracked): result = builder.package_startup_content(self.root, 'c' * 64, retain_sources=True)
        self.assertEqual({row['path'] for row in result['files']}, {'StreamingAssets/Rulebase/rules.json', 'StreamingAssets/Movies/intro.mp4'})
        self.assertNotIn(self.archive.parent / 'Quest', observed)
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(archive.getinfo('StreamingAssets/Movies/intro.mp4').compress_type, zipfile.ZIP_STORED)

    def test_whole_graphics_readback_is_developer_opt_in(self):
        self.assertFalse(builder.full_asset_validation())
        with patch.dict(os.environ, {'GHVR_QUEST_VALIDATE_IMPORTED_ASSETS': '1'}):
            self.assertTrue(builder.full_asset_validation())
        self.assertFalse(builder.parser().parse_args(['build']).full_validation)
        self.assertTrue(builder.parser().parse_args(['build', '--full-validation']).full_validation)

    @unittest.skipIf(os.name == 'nt', 'Requires fixture hardlink permission')
    def test_transaction_preimage_uses_source_only_directory_change_stamp(self):
        import storage
        preimage = self.root / 'preimage.zip'; os.link(self.archive, preimage)
        actual = ('win32-v1', 99, 'file-id', self.archive.stat().st_size, 20, 30)
        observer = types.SimpleNamespace(directory=lambda path: {self.archive.name: actual})
        with patch.object(packer.sys, 'platform', 'win32'), patch.object(storage, '_file_witness_stamp', return_value=None), \
             patch.object(storage, '_windows_metadata', observer):
            self.assertEqual(packer.file_identity(self.archive)[1], actual)
        # Persistent file trust remains unchanged: links still get no digest
        # shortcut, and the consumer only reads the qualified source inode.
        self.assertEqual(self.archive.stat().st_nlink, 2)

    def test_minimum_builder_changes_keep_original_payload_update_compatible(self):
        root = Path(builder.__file__).resolve().parents[2]
        driver = builder._local_helper('update_driver')
        names = set(driver._PAYLOAD_AUDIT_ONLY_ROWS) | {'tools/quest-builder/builder.py'}
        previous, current = [], []
        for name in sorted(names):
            path = root / name
            current.append({'path': name, 'size': path.stat().st_size, 'sha256': packer.digest(path)})
            result = subprocess.run(['git', 'show', '2b0fe7928:' + name], cwd=root, capture_output=True)
            if result.returncode == 0:
                previous.append({'path': name, 'size': len(result.stdout), 'sha256': hashlib.sha256(result.stdout).hexdigest()})
        import preparation_identity
        raw = subprocess.check_output(['git', 'show', '2b0fe7928:tools/quest-builder/builder.py'], cwd=root)
        before = preparation_identity.builder_producer_digest(raw)
        after = preparation_identity.builder_producer_digest(Path(builder.__file__).read_bytes())
        self.assertEqual(before, after)
        self.assertEqual(driver.rebuild_reasons(current, previous, payload_producer={'sha256': after},
                                              previous_producer={'sha256': before}), [])
        helper = next(row for row in current if row['path'].endswith('/recovery_workers.py'))
        helper['sha256'] = 'f' * 64
        self.assertTrue(driver.rebuild_reasons(current, previous, payload_producer={'sha256': after},
                                             previous_producer={'sha256': before}))


if __name__ == '__main__': unittest.main()
