"""Actual staged bytes qualify once; phase boundaries never imply native success."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
sys.path.insert(0, str(ROOT / 'tools/quest-recovery'))
import full_assets
from storage import BuildError


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def events(stream):
    prefix = full_assets.build_progress.PREFIX
    return [json.loads(line[len(prefix):]) for line in stream.getvalue().splitlines() if line.startswith(prefix)]


class Proxy:
    def __init__(self, stream, read=None, write=None):
        self.stream, self.read_hook, self.write_hook = stream, read, write

    def __enter__(self):
        self.stream.__enter__(); return self

    def __exit__(self, *args):
        return self.stream.__exit__(*args)

    def __getattr__(self, name):
        return getattr(self.stream, name)

    def read(self, count=-1):
        return self.read_hook(self.stream, count) if self.read_hook else self.stream.read(count)

    def write(self, raw):
        return self.write_hook(self.stream, raw) if self.write_hook else self.stream.write(raw)


class StagingTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source, self.target = self.root / 'owned', self.root / 'stage/copied'
        self.raw = b'original native export bytes' * 90000
        self.source.write_bytes(self.raw)
        self.stream = io.StringIO()
        setting = mock.patch.dict(os.environ, {full_assets.build_progress.ENV: '1'})
        setting.start(); self.addCleanup(setting.stop)

    def test_streamed_writer_and_unchanged_report_read_each_asset_only_once(self):
        proof = full_assets._StageProofs(); opening = Path.open; calls = []
        def observe(path, mode='r', *args, **kwargs):
            calls.append((path, mode)); return opening(path, mode, *args, **kwargs)
        with mock.patch.object(Path, 'open', observe), contextlib.redirect_stdout(self.stream), \
             mock.patch.object(full_assets, 'LARGE_FILE', 1), \
             mock.patch.object(full_assets.build_progress.time, 'monotonic', side_effect=range(1000)):
            full_assets._copy_recovered(self.source, self.target, digest(self.raw), expected_size=len(self.raw), proofs=proof)
            rows = full_assets._output_inventory(self.target.parent, proof)
        self.assertEqual(rows, [{'path': 'copied', 'size': len(self.raw), 'sha256': digest(self.raw)}])
        self.assertEqual([mode for path, mode in calls if path == self.source], ['rb'])
        self.assertEqual([mode for path, mode in calls if path == self.target], ['xb'])
        self.assertEqual(self.source.read_bytes(), self.raw)
        byte_events = [row for row in events(self.stream) if row['phase'] == 'staging-copy-file']
        self.assertTrue(any(0 < row['done'] < len(self.raw) for row in byte_events))
        self.assertEqual((byte_events[-1]['done'], byte_events[-1]['total'], byte_events[-1]['status']), (len(self.raw), len(self.raw), 'complete'))

    def test_hash_mismatch_removes_partial_copy_without_success_event(self):
        with contextlib.redirect_stdout(self.stream), mock.patch.object(full_assets, 'LARGE_FILE', 1), self.assertRaisesRegex(BuildError, 'differs'):
            full_assets._copy_recovered(self.source, self.target, '0' * 64)
        self.assertFalse(self.target.exists())
        self.assertFalse(any(row['status'] == 'complete' for row in events(self.stream)))
        self.assertEqual(self.source.read_bytes(), self.raw)

    def test_changed_source_is_rejected_even_when_copied_hash_matches(self):
        opening = Path.open; changed = False
        def read(stream, size):
            nonlocal changed
            raw = stream.read(size)
            if not changed:
                changed = True
                with opening(self.source, 'r+b') as mutate:
                    mutate.write(b'X')
            return raw
        def source_open(path, mode='r', *args, **kwargs):
            stream = opening(path, mode, *args, **kwargs)
            return Proxy(stream, read=read) if path == self.source and mode == 'rb' else stream
        with mock.patch.object(Path, 'open', source_open), self.assertRaisesRegex(BuildError, 'changed'):
            full_assets._copy_recovered(self.source, self.target, digest(self.raw))
        self.assertFalse(self.target.exists())

    def test_short_write_is_rejected_and_removed(self):
        opening = Path.open
        def sink_open(path, mode='r', *args, **kwargs):
            stream = opening(path, mode, *args, **kwargs)
            return Proxy(stream, write=lambda stream, raw: stream.write(raw[:-1])) if path == self.target and mode == 'xb' else stream
        with mock.patch.object(Path, 'open', sink_open), self.assertRaisesRegex(BuildError, 'incomplete'):
            full_assets._copy_recovered(self.source, self.target, digest(self.raw))
        self.assertFalse(self.target.exists())
        self.assertEqual(self.source.read_bytes(), self.raw)

    def test_read_failure_removes_only_its_partial_target(self):
        opening = Path.open
        def failing_open(path, mode='r', *args, **kwargs):
            stream = opening(path, mode, *args, **kwargs)
            return Proxy(stream, read=mock.Mock(side_effect=OSError('actual read failed'))) if path == self.source and mode == 'rb' else stream
        with mock.patch.object(Path, 'open', failing_open), self.assertRaises(OSError):
            full_assets._copy_recovered(self.source, self.target, digest(self.raw))
        self.assertFalse(self.target.exists())
        self.assertEqual(self.source.read_bytes(), self.raw)

    def test_existing_target_and_symlink_source_are_never_overwritten(self):
        self.target.parent.mkdir(); self.target.write_bytes(b'unrelated')
        with self.assertRaises(FileExistsError): full_assets._copy_recovered(self.source, self.target, digest(self.raw))
        self.assertEqual(self.target.read_bytes(), b'unrelated')
        link = self.root / 'source-link'; link.symlink_to(self.source)
        with self.assertRaisesRegex(BuildError, 'regular file'): full_assets._copy_recovered(link, self.root / 'other')

    def test_native_mutation_invalidates_writer_proof_and_rehashes_exact_output(self):
        proof = full_assets._StageProofs()
        full_assets._copy_recovered(self.source, self.target, digest(self.raw), proofs=proof)
        changed = b'native restored bytes'; self.target.write_bytes(changed)
        with contextlib.redirect_stdout(self.stream), mock.patch.object(full_assets, '_hash_output', wraps=full_assets._hash_output) as hash_output:
            rows = full_assets._output_inventory(self.target.parent, proof)
        self.assertEqual(hash_output.call_count, 1)
        self.assertEqual(rows[0]['sha256'], digest(changed))

    def test_runtime_metadata_reuses_current_writer_bytes_without_copy_or_hash(self):
        proof = full_assets._StageProofs()
        full_assets._copy_recovered(self.source, self.target, digest(self.raw), proofs=proof)
        with mock.patch.object(full_assets, '_copy_recovered', side_effect=AssertionError('duplicate copy')), \
             mock.patch.object(full_assets, '_hash_output', side_effect=AssertionError('duplicate hash')):
            self.assertEqual(full_assets._runtime_file(self.source, self.target, proof), digest(self.raw))

    def test_canonical_runtime_metadata_replaces_only_qualified_owned_output(self):
        proof = full_assets._StageProofs()
        full_assets._copy_recovered(self.source, self.target, digest(self.raw), proofs=proof)
        canonical = self.root / 'canonical'; canonical.write_bytes(b'old canonical identity proof')
        full_assets._runtime_file(canonical, self.target, proof)
        self.assertEqual(self.target.read_bytes(), canonical.read_bytes())
        self.assertEqual(proof.published(self.target), digest(canonical.read_bytes()))
        self.assertEqual(list(self.target.parent.glob('*.staging-*')), [])
        self.target.write_bytes(b'unrelated change')
        with self.assertRaisesRegex(BuildError, 'changed'): full_assets._runtime_file(canonical, self.target, proof)
        self.assertEqual(self.target.read_bytes(), b'unrelated change')

    def test_receipt_size_mismatch_never_creates_output(self):
        with self.assertRaisesRegex(BuildError, 'size differs'):
            full_assets._copy_recovered(self.source, self.target, digest(self.raw), expected_size=len(self.raw)+1)
        self.assertFalse(self.target.exists())

    def test_section_failure_never_finishes_or_restarts_completed_work(self):
        with contextlib.redirect_stdout(self.stream):
            with full_assets._section('catalog'): pass
            with self.assertRaises(RuntimeError), full_assets._section('canonical'):
                raise RuntimeError('actual witness failure')
        self.assertEqual([(row['phase'], row['status']) for row in events(self.stream)], [('staging-section:catalog','start'), ('staging-section:catalog','complete'), ('staging-section:canonical','start'), ('staging-section:canonical','failed')])

    def test_full_stage_keeps_thirteen_native_scenes_and_actual_section_schedule(self):
        modules = full_assets._modules()
        canonical, catalogs, identities, layouts, tmp, recover, startup = modules
        import canonical_contracts
        import native_stage
        project, game, output = self.root/'raw-project', self.root/'game', self.root/'full-stage'
        catalog = game/'StreamingAssets/aa/catalog.json'; catalog.parent.mkdir(parents=True); catalog.write_text('{}')
        project.mkdir(); rows, records, scene_paths = [], [], []
        def write(relative, raw):
            path=project/relative; path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(raw)
            records.append({'path':relative,'sha256':digest(raw),'size':len(raw)})
        for index in range(13):
            relative=f'Assets/Scene/Scene{index:02}.unity'; guid=f'{index+1:032x}'; scene_paths.append(relative)
            write(relative,b'%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &1\nGameObject:\n  m_Name: Original\n')
            write(relative+'.meta',f'fileFormatVersion: 2\nguid: {guid}\n'.encode())
            rows.append({'path':relative,'guid':guid,'exportCollection':'fixture','objects':[{'collection':f'level{index}','pathId':1,'fileId':1,'classId':1,'originalPath':relative}]})
        write('ProjectSettings/EditorBuildSettings.asset',('\n'.join('  path: '+path for path in scene_paths)+'\n').encode())
        write('Assets/Resources/srdebugger/unused.txt',b'unchanged excluded debug data')
        write('QuestRecovery/managed-types.json',b'{}')
        write('QuestRecovery/original-script-identities.json',b'[]')
        checkpoint=project/'quest-full-recovery-progress.json'
        checkpoint.write_text(json.dumps({'schema':1,'assetsRecovered':True,'catalogSha256':recover.sha256(catalog),'sourceFingerprint':'fixture-owned','sourceInventory':[],'identities':rows,'files':records}))
        owners=self.root/'owners.json'; owners.write_text('{}')
        catalog_manifest={'catalogSha256':recover.sha256(catalog),'entries':[]}
        proof={'schema':1,'proofs':[],'mappings':{},'rejected':[],'mappedOriginalObjectCount':0}
        before={row['path']:(project/row['path']).read_bytes() for row in records}
        def native(*args,**kwargs):
            (output/scene_paths[0]).write_text((output/scene_paths[0]).read_text().replace('Original','NativeRestored'))
            return list(rows), {'nativeFixtureRestoration':True}
        with contextlib.redirect_stdout(self.stream), \
             mock.patch.object(catalogs,'associate',return_value=catalog_manifest), \
             mock.patch.object(canonical_contracts,'witness',return_value=proof), \
             mock.patch.object(layouts,'restore',return_value={'repairedObjectCount':0}), \
             mock.patch.object(native_stage,'restore',side_effect=native), \
             mock.patch.object(tmp,'restore',return_value=[]):
            report=full_assets.stage(project,game,output,self.root/'unused-tmp.zip',managed_types=project/'QuestRecovery/managed-types.json',cab_bundles=owners,unitypy=types.SimpleNamespace())
        boundaries=[row for row in events(self.stream) if row['phase'].startswith('staging-section:')]
        self.assertEqual([(row['phase'].split(':')[1],row['status']) for row in boundaries],[(name,status) for name in full_assets.STAGING_SECTIONS for status in ('start','complete')])
        self.assertTrue(all(row['operation']=='recovery' for row in boundaries))
        copied=[row for row in events(self.stream) if row['phase']=='staging-copy'][-1]
        self.assertEqual((copied['done'],copied['total']), (len(records)-1,len(records)-1))
        self.assertEqual(len(report['selectedScenes']),13)
        self.assertTrue(report['readiness']['originalSceneClosureStaged'])
        self.assertFalse(report['fullGameReady'])
        self.assertEqual(report['missingReferences']['missingGuidCount'],0)
        self.assertEqual({row['path']:(project/row['path']).read_bytes() for row in records},before)
        self.assertEqual(json.loads((output/'quest-campaign-report.json').read_text()),report)
        self.assertFalse((output/'Assets/Resources/srdebugger/unused.txt').exists())
        final={row['path']:row for row in report['files']}
        self.assertEqual(final[scene_paths[0]]['sha256'],digest((output/scene_paths[0]).read_bytes()))


if __name__=='__main__':
    unittest.main()
