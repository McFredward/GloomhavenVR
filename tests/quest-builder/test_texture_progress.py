"""Measure texture post-processing without changing native bytes or ownership."""
from pathlib import Path
import contextlib
import hashlib
import io
import json
import os
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-recovery'))
import full_textures
from storage import BuildError, write_json


class TexturePostProcessingProgressTests(unittest.TestCase):
    def project(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        project = Path(temporary.name)
        folder = project / 'Assets/QuestOriginalCampaign'; folder.mkdir(parents=True)
        (project / 'QuestRecovery').mkdir()
        guid = 'a' * 32
        target = project / 'Assets/T.texture2D'
        target.write_text('%YAML 1.1\n--- !u!28 &2800000\nTexture2D:\n  m_Name: Probe\n')
        Path(str(target) + '.meta').write_text('fileFormatVersion: 2\nguid: ' + guid +
            '\nNativeFormatImporter:\n  mainObjectFileID: 2800000\n')
        write_json(folder / 'native-texture2d.json', {'assets': [
            {'assetPath': 'Assets/T.texture2D', 'guid': guid, 'fileId': 2800000}]})
        pointer = '{fileID: 2800000, guid: ' + guid + ', type: 3}'
        owner = project / 'Assets/Owner.mat'
        owner.write_text('%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: "' + pointer +
                         '"\n  main: ' + pointer + '\n  second: ' + pointer + '\n')
        digest = hashlib.sha256(owner.read_bytes()).hexdigest()
        for name in ('Assets/QuestOriginalCampaign/native-sprites.json', 'QuestRecovery/packed-a.json'):
            write_json(project / name, {'assets': [{'assetPath': 'Assets/Owner.mat',
                       'sha256': digest, 'originalObjectSha256': 'unchanged owned provenance'}]})
        return project, owner, pointer

    def observe(self, function):
        stream = io.StringIO()
        with mock.patch.dict(os.environ, {full_textures.build_progress.ENV: '1'}), \
             contextlib.redirect_stdout(stream), \
             mock.patch.object(full_textures.build_progress.time, 'monotonic', side_effect=range(100000)):
            result = function()
        prefix = full_textures.build_progress.PREFIX
        rows = [json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines()
                if line.startswith(prefix)]
        return result, rows

    def phase(self, rows, suffix):
        return [row for row in rows if row['phase'] == 'prepare-items:native-texture2d-' + suffix]

    def test_reference_scan_measures_one_observed_walk_and_preserves_exact_pointer_roles(self):
        project, owner, pointer = self.project()
        expected = list((project / 'Assets').rglob('*'))
        walks = []
        original = Path.rglob
        def rglob(path, pattern, *args, **kwargs):
            if path == project / 'Assets': walks.append(pattern)
            return original(path, pattern, *args, **kwargs)
        with mock.patch.object(Path, 'rglob', rglob):
            result, rows = self.observe(lambda: full_textures.restore_native_texture_pointer_types(
                project, progress_scope='native-texture2d'))
        self.assertEqual(walks, ['*'])
        discovery, scan, receipts = [self.phase(rows, suffix) for suffix in
                                    ('reference-discovery', 'reference-scan', 'reference-receipts')]
        self.assertEqual((discovery[0]['done'], discovery[0]['total']), (None, None))
        self.assertEqual((discovery[-1]['done'], discovery[-1]['total']), (len(expected), len(expected)))
        self.assertEqual((scan[0]['done'], scan[0]['total']), (0, len(expected)))
        self.assertEqual((scan[-1]['done'], scan[-1]['total'], scan[-1]['status']),
                         (len(expected), len(expected), 'complete'))
        self.assertEqual([row['done'] for row in scan], list(range(len(expected))) + [len(expected)])
        self.assertEqual((receipts[-1]['done'], receipts[-1]['total'], receipts[-1]['status']), (6, 6, 'complete'))
        self.assertEqual((result['referenceCount'], result['changedReferenceCount']), (2, 2))
        self.assertIn('m_Name: "' + pointer + '"', owner.read_text())
        self.assertEqual(owner.read_text().count('type: 2'), 2)
        after = hashlib.sha256(owner.read_bytes()).hexdigest()
        for name in result['refreshedManifests']:
            row = json.loads((project / name).read_text())['assets'][0]
            self.assertEqual(row['sha256'], after)
            self.assertEqual(row['originalObjectSha256'], 'unchanged owned provenance')

    def test_failed_owner_processing_never_completes_scan_or_reference_receipts(self):
        project, owner, _ = self.project()
        owner.write_text(owner.read_text().replace('type: 3}', 'type: 4}'))
        stream = io.StringIO()
        with mock.patch.dict(os.environ, {full_textures.build_progress.ENV: '1'}), contextlib.redirect_stdout(stream), \
             mock.patch.object(full_textures.build_progress.time, 'monotonic', side_effect=range(100000)), \
             self.assertRaisesRegex(BuildError, 'unwitnessed source reference type'):
            full_textures.restore_native_texture_pointer_types(project, progress_scope='native-texture2d')
        prefix = full_textures.build_progress.PREFIX
        rows = [json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        scan = self.phase(rows, 'reference-scan')
        self.assertTrue(scan)
        self.assertFalse(any(row['status'] == 'complete' for row in scan))
        self.assertFalse(self.phase(rows, 'reference-receipts'))
        self.assertFalse((project / 'Assets/QuestOriginalCampaign/native-texture-references.json').exists())

    def test_final_receipt_write_failure_never_closes_receipt_counter(self):
        project, _, _ = self.project()
        writer = full_textures.write_json
        def fail_final(path, value):
            if path.name == 'native-texture-references.json': raise OSError('final receipt publication failed')
            return writer(path, value)
        stream = io.StringIO()
        with mock.patch.dict(os.environ, {full_textures.build_progress.ENV: '1'}), contextlib.redirect_stdout(stream), \
             mock.patch.object(full_textures.build_progress.time, 'monotonic', side_effect=range(100000)), \
             mock.patch.object(full_textures, 'write_json', side_effect=fail_final), \
             self.assertRaisesRegex(OSError, 'final receipt publication failed'):
            full_textures.restore_native_texture_pointer_types(project, progress_scope='native-texture2d')
        prefix = full_textures.build_progress.PREFIX
        rows = [json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        self.assertFalse(any(row['status'] == 'complete' for row in self.phase(rows, 'reference-receipts')))
        self.assertEqual(self.phase(rows, 'reference-receipts')[-1]['status'], 'failed')

    def test_known_ledger_progress_keeps_logical_source_keys_and_counts_absent_paths(self):
        project, _, _ = self.project()
        write_json(project / 'QuestRecovery/original-asset-identities.json',
                   {'identities': [{'path': 'Assets/T.png', 'originalPath': 'Assets/T.png', 'guid': 'a' * 32}]})
        folder = project / 'Assets/QuestOriginalCampaign'
        write_json(folder / 'campaign-addressables.json', {'entries': [
            {'assetPath': 'Assets/T.png', 'keys': ['Assets/T.png'], 'nativeKeys': ['Assets/T.png']}]})
        write_json(folder / 'script-bindings.json', {'assetPaths': ['Assets/T.png']})
        changed, rows = self.observe(lambda: full_textures.remap_manifests(project,
            {'Assets/T.png': 'Assets/T.texture2D'}, progress_scope='native-texture2d'))
        values = self.phase(rows, 'manifests')
        self.assertEqual((values[-1]['done'], values[-1]['total'], values[-1]['status']), (7, 7, 'complete'))
        self.assertEqual(len(changed), 3)
        self.assertEqual(json.loads((folder / 'campaign-addressables.json').read_text())['entries'][0],
                         {'assetPath': 'Assets/T.texture2D', 'keys': ['Assets/T.png'], 'nativeKeys': ['Assets/T.png']})
        for row in changed:
            self.assertEqual(row['sha256'], hashlib.sha256((project / row['path']).read_bytes()).hexdigest())

    def test_actual_decode_completion_precedes_separately_measured_post_processing(self):
        from test_full_texture2d import OrdinaryTextureTests
        helper = OrdinaryTextureTests('runTest'); self.addCleanup(helper.doCleanups)
        _, rows = helper.staged_textures()
        last = next(index for index, row in enumerate(rows)
                    if row['phase'] == 'prepare-items:native-texture2d' and row['status'] == 'complete')
        for suffix in ('manifests', 'reference-discovery', 'reference-scan', 'reference-receipts'):
            positions = [index for index, row in enumerate(rows)
                         if row['phase'] == 'prepare-items:native-texture2d-' + suffix and row['status'] == 'complete']
            self.assertEqual(len(positions), 1)
            self.assertGreater(positions[0], last)


if __name__ == '__main__': unittest.main()
