"""Interrupted owned exports and merge journals, with actual GUID/object mapping."""
import hashlib
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-recovery'))
import recover
import bundle_recovery as bundles
import export_identity
import full_recovery


def identity(guid, path, collection, path_id):
    return {'guid': guid, 'path': str(path), 'objects': [{'collection': collection, 'pathId': path_id, 'fileId': 2100000, 'classId': 21, 'className': 'Material'}]}


class Fixture(unittest.TestCase):
    def setUp(self): self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
    def tearDown(self): self.temp.cleanup()
    def asset(self, project, guid, collection, path_id):
        path = project / 'Assets' / (collection + '.mat'); path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: original\n')
        path.with_name(path.name + '.meta').write_text('guid: ' + guid + '\n')
        return identity(guid, path, collection, path_id)
    def records(self, root):
        return [{'path': path.relative_to(root).as_posix(), 'bytes': path.stat().st_size, 'sha256': recover.sha256(path)} for path in sorted(root.rglob('*')) if path.is_file()]


class InputRestartTests(Fixture):
    def test_copy_cancel_resumes_only_unfinished_member_and_original_stays_readonly(self):
        source = self.root / 'GH_Data'; source.mkdir(); (source / 'one').write_bytes(b'original one'); (source / 'two').write_bytes(b'original two')
        stage = self.root / 'Input/GH_Data'; before = recover.source_inventory(source); real_copy = recover.shutil.copy2
        def stop(original, target):
            if original.name == 'two': raise KeyboardInterrupt()
            return real_copy(original, target)
        with patch.object(recover.shutil, 'copy2', stop), self.assertRaises(KeyboardInterrupt): recover.stage_input(source, stage, [])
        with patch.object(recover.shutil, 'copy2', wraps=real_copy) as copying: recover.stage_input(source, stage, [])
        self.assertEqual(copying.call_count, 1); self.assertEqual(copying.call_args.args[0].name, 'two')
        self.assertEqual(recover.source_inventory(source), before)
    def test_unowned_partial_stage_is_not_deleted(self):
        source = self.root / 'GH_Data'; source.mkdir(); (source / 'one').write_bytes(b'original')
        stage = self.root / 'Input/GH_Data'; stage.mkdir(parents=True); (stage / 'foreign').write_bytes(b'keep')
        with self.assertRaises(recover.RecoveryError): recover.stage_input(source, stage, [])
        self.assertEqual((stage / 'foreign').read_bytes(), b'keep')
    def test_atomic_checkpoint_failure_keeps_previous_valid_bytes(self):
        checkpoint = self.root / 'checkpoint.json'; recover.write_json(checkpoint, {'before': 'durable'})
        with patch.object(recover.os, 'replace', side_effect=KeyboardInterrupt()), self.assertRaises(KeyboardInterrupt): recover.write_json(checkpoint, {'after': 'incomplete'})
        self.assertEqual(json.loads(checkpoint.read_text()), {'before': 'durable'}); self.assertEqual(list(self.root.glob('*.tmp-*')), [])


class MergeRestartTests(Fixture):
    def setUp(self):
        super().setUp(); self.output = self.root / 'project'; self.workspace = self.root / 'workspace'; self.workspace.mkdir()
        base = self.asset(self.output, 'a'*32, 'native-core', 1)
        self.index = self.output / bundles.MUTABLE_NATIVE_INDICES[0]; self.index.parent.mkdir(); self.index.write_text('original index bytes\n')
        self.progress = {'schema': 1, 'completedGroups': [], 'files': self.records(self.output), 'identities': [dict(base, path='Assets/native-core.mat')]}
        self.checkpoint = self.output / 'quest-full-recovery-progress.json'; recover.write_json(self.checkpoint, self.progress)
        self.incoming_project = self.root / 'incoming'; self.incoming = self.asset(self.incoming_project, 'b'*32, 'native-bundle', 2)
        self.incoming['path'] = 'Assets/native-bundle.mat'
        self.evidence = self.root / 'Evidence'; (self.evidence / 'QuestRecovery').mkdir(parents=True)
        (self.evidence / bundles.MUTABLE_NATIVE_INDICES[0]).write_text('incoming native evidence\n')
    def begin(self): bundles.begin_merge(self.output, self.workspace, 0, [self.incoming], self.evidence, export_identity.object_index(self.progress['identities']))
    def test_interrupted_actual_guid_merge_rolls_back_new_files_and_modified_index(self):
        self.begin(); merged = bundles.merge_export(self.output, self.incoming_project, [self.incoming], export_identity.object_index(self.progress['identities']))
        self.index.write_text('torn merged index')
        bundles.recover_merge(self.output, self.workspace)
        self.assertEqual(self.index.read_text(), 'original index bytes\n'); self.assertFalse((self.output / merged['files'][0]['path']).exists())
        bundles.verified_records(self.output, self.progress['files'])
        self.begin(); again = bundles.merge_export(self.output, self.incoming_project, [self.incoming], export_identity.object_index(self.progress['identities']))
        self.assertEqual(again['identities'], merged['identities'])
    def test_completed_checkpoint_survives_death_before_journal_cleanup(self):
        self.begin(); merged = bundles.merge_export(self.output, self.incoming_project, [self.incoming], export_identity.object_index(self.progress['identities']))
        self.progress['completedGroups'] = [0]; self.progress['files'].extend(merged['files']); recover.write_json(self.checkpoint, self.progress)
        bundles.recover_merge(self.output, self.workspace)
        self.assertTrue((self.output / merged['files'][0]['path']).exists()); self.assertFalse((self.workspace / 'merge-pending.json').exists())
    def test_changed_backup_refuses_any_rollback_mutation(self):
        self.begin(); backup = self.workspace / 'merge-index-0.backup'; backup.write_bytes(b'changed')
        self.index.write_bytes(b'current index to preserve')
        with self.assertRaises(recover.RecoveryError): bundles.recover_merge(self.output, self.workspace)
        self.assertEqual(self.index.read_bytes(), b'current index to preserve')


class FullBatchRestartTests(Fixture):
    def test_three_real_batches_never_repeat_growing_output_hash_sweeps(self):
        source = self.root / 'GH_Data'; source.mkdir(); (source / 'core-input').write_bytes(b'original core input')
        tool = self.root / 'exporter-fixture.dll'; tool.write_bytes(b'qualified tool fixture')
        core = self.root / 'core'; base = self.asset(core, 'a' * 32, 'native-core', 1)
        native_dir = core / 'QuestRecovery/NativeRecipes'; native_dir.mkdir(parents=True)
        recipe = native_dir / 'original.yaml'; recipe.write_bytes(b'original native recipe\n')
        native_row = {'collection': 'native-core', 'pathId': 1, 'yamlPath': recipe.name, 'yamlSha256': recover.sha256(recipe)}
        (native_dir / 'index.jsonl').write_text(json.dumps(native_row) + '\n')
        identity_path = self.root / 'core-identities.jsonl'; identity_path.write_text(json.dumps(base) + '\n')
        groups = []
        for index in range(3):
            path = source / (str(index) + '.bundle'); path.write_bytes(b'UnityFS\0' + str(index).encode())
            groups.append({'bundles': [{'path': path.name, 'bytes': path.stat().st_size, 'sha256': recover.sha256(path)}]})
        plan = {'catalogSha256': 'c' * 64, 'groups': groups}
        output, workspace = self.root / 'merged', self.root / 'batches'; exports = []
        def export(command, stage, target, log, evidence, settings, **_):
            index = len(exports); exports.append(index)
            project = target / 'ExportedProject'
            row = self.asset(project, str(index + 1) * 32, 'native-bundle-' + str(index), index + 2)
            Path(os.environ['QUEST_EXPORT_IDENTITIES']).write_text(json.dumps(row) + '\n')
            directory = evidence / 'QuestRecovery/NativeRecipes'; directory.mkdir(parents=True)
            (directory / recipe.name).write_bytes(recipe.read_bytes())
            (directory / 'index.jsonl').write_text(json.dumps(native_row) + '\n')
            log.write_text('original export succeeded\n')
            return project, [], []
        with patch.object(bundles, 'catalog_bundle_plan', return_value=plan), \
             patch.object(bundles, 'repair_managed_plugins'), \
             patch.object(bundles, 'audit_asset_references', return_value={'unexpectedUnresolvedCount': 0}) as references, \
             patch.object(bundles, 'run_export', side_effect=export), \
             patch.object(bundles, 'verified_records', wraps=bundles.verified_records) as verify, \
             patch.object(bundles, '_hash_file', wraps=bundles._hash_file) as hashes, \
             patch.object(bundles, 'sha256', wraps=bundles.sha256) as input_hashes:
            result = bundles.run_recovery(source, core, identity_path, output, workspace, [str(tool)])
        self.assertEqual(exports, [0, 1, 2]); self.assertEqual(result['completedGroups'], [0, 1, 2])
        self.assertEqual(verify.call_count, 0)
        self.assertEqual(sum(Path(call.args[0]) == tool for call in input_hashes.call_args_list), 1)
        self.assertEqual(sum(call.args[0] == output / 'QuestRecovery/NativeRecipes/original.yaml' for call in hashes.call_args_list), 1)
        self.assertEqual(sum(call.args[0] == output / 'Assets/native-core.mat' for call in hashes.call_args_list), 1)
        for row in result['files']:
            if row['path'].startswith('Assets/QuestRecoveredBundles/'):
                self.assertEqual(sum(call.args[0] == output / row['path'] for call in hashes.call_args_list), 1)
        self.assertEqual(references.call_count, 1)
        # A new child hashes the completed output once, skips exporter staging,
        # and retains the completed reference audit. No loop or re-export occurs.
        with patch.object(bundles, 'catalog_bundle_plan', return_value=plan), \
             patch.object(bundles, 'run_export', side_effect=AssertionError('Completed exports must not restart')), \
             patch.object(bundles, 'stage_input', side_effect=AssertionError('No exporter needs staged inputs')), \
             patch.object(bundles, 'audit_asset_references', side_effect=AssertionError('Completed reference audit is retained')), \
             patch.object(bundles, 'verified_records', wraps=bundles.verified_records) as verify, \
             patch.object(bundles, '_hash_file', wraps=bundles._hash_file) as hashes, \
             patch.object(bundles, 'sha256', wraps=bundles.sha256) as input_hashes:
            resumed = bundles.run_recovery(source, core, identity_path, output, workspace, [str(tool)])
        self.assertEqual(resumed, result); self.assertEqual(verify.call_count, 1)
        self.assertEqual(sum(Path(call.args[0]) == tool for call in input_hashes.call_args_list), 0)
        self.assertEqual(verify.call_args.kwargs['phase'], 'recovery-resume-verify')
        for row in result['files']:
            self.assertEqual(sum(call.args[0] == output / row['path'] for call in hashes.call_args_list), 1)
        self.assertFalse((workspace / 'merge-pending.json').exists())

    def test_two_real_identity_batches_reuse_completed_export_after_interrupted_merge(self):
        source = self.root / 'GH_Data'; source.mkdir(); (source / 'core-input').write_bytes(b'core original')
        core = self.root / 'core'; base = self.asset(core, 'a'*32, 'native-core', 1)
        identity_path = self.root / 'core-identities.jsonl'; identity_path.write_text(json.dumps(base)+'\n')
        original_bundles = []
        for index in range(2):
            path=source / (str(index)+'.bundle');path.write_bytes(b'UnityFS\0'+str(index).encode());original_bundles.append({'path':path.name,'bytes':path.stat().st_size,'sha256':recover.sha256(path)})
        plan={'catalogSha256':'c'*64,'groups':[{'index':n,'bundles':[original_bundles[n]]} for n in range(2)]}
        output=self.root/'merged';workspace=self.root/'batches';exports=[];real_merge=bundles.merge_export
        def export(command,stage,target,log,evidence,settings,**_):
            index=len(exports);exports.append(index);project=target/'ExportedProject';row=self.asset(project, str(index+1)*32, 'native-bundle-'+str(index), index+2)
            Path(os.environ['QUEST_EXPORT_IDENTITIES']).write_text(json.dumps(row)+'\n');log.write_text('original export succeeded\n')
            return project, [], []
        failed=[False]
        def interrupt(project,incoming,rows,canonical):
            result=real_merge(project,incoming,rows,canonical)
            if not failed[0] and rows[0]['objects'][0]['pathId']==3:failed[0]=True;raise KeyboardInterrupt()
            return result
        with patch.object(bundles,'catalog_bundle_plan',return_value=plan),patch.object(bundles,'repair_managed_plugins'),patch.object(bundles,'audit_asset_references',return_value={'unexpectedUnresolvedCount':0}),patch.object(bundles,'run_export',side_effect=export):
            with patch.object(bundles,'merge_export',side_effect=interrupt),self.assertRaises(KeyboardInterrupt):bundles.run_recovery(source,core,identity_path,output,workspace,[])
            self.assertEqual(json.loads((output/'quest-full-recovery-progress.json').read_text())['completedGroups'],[0])
            with patch.object(bundles, 'stage_input', side_effect=AssertionError('Retained export does not need staging')), \
                 patch.object(bundles.shutil, 'copy2', side_effect=AssertionError('Retained batch must not recopy bundles')):
                result=bundles.run_recovery(source,core,identity_path,output,workspace,[])
        self.assertEqual(exports,[0,1]);self.assertEqual(result['completedGroups'],[0,1]);self.assertTrue(result['assetsRecovered'])
        self.assertEqual(len(export_identity.object_index(result['identities'])),3)


class SourceAcquisitionTests(Fixture):
    def archive(self):
        archive=self.root/'source.tar.gz';name='AssetRipper-'+export_identity.REVISION+'/Source/original.cs'
        with tarfile.open(archive,'w:gz') as package:
            raw=b'original open source';info=tarfile.TarInfo(name);info.size=len(raw);package.addfile(info,io.BytesIO(raw))
        return archive
    def test_owned_partial_extraction_restarts_from_verified_archive_then_reuses(self):
        archive=self.archive();cache=self.root/'cache';cache.mkdir();cached=cache/'assetripper-source-1ac666f.tar.gz';cached.write_bytes(archive.read_bytes());pinned=recover.sha256(cached)
        real=export_identity.safe_extract
        def stop(path,target):
            partial=target/('AssetRipper-'+export_identity.REVISION);partial.mkdir();(partial/'unfinished').write_bytes(b'partial');raise KeyboardInterrupt()
        with patch.object(export_identity,'SOURCE_SHA256',pinned):
            with patch.object(export_identity,'safe_extract',side_effect=stop),self.assertRaises(KeyboardInterrupt):export_identity.acquire_source(cache)
            with patch.object(export_identity,'safe_extract',wraps=real) as extraction:source=export_identity.acquire_source(cache)
            self.assertEqual(extraction.call_count,1);self.assertEqual((source/'Source/original.cs').read_bytes(),b'original open source');self.assertFalse((source/'unfinished').exists())
            with patch.object(export_identity,'safe_extract',side_effect=AssertionError('no new extraction')):self.assertEqual(export_identity.acquire_source(cache),source)
    def test_unowned_source_directory_is_preserved(self):
        archive=self.archive();cache=self.root/'cache';cache.mkdir();cached=cache/'assetripper-source-1ac666f.tar.gz';cached.write_bytes(archive.read_bytes());source=cache/('AssetRipper-'+export_identity.REVISION);source.mkdir();(source/'foreign').write_bytes(b'keep')
        with patch.object(export_identity,'SOURCE_SHA256',recover.sha256(cached)),self.assertRaises(recover.RecoveryError):export_identity.acquire_source(cache)
        self.assertEqual((source/'foreign').read_bytes(),b'keep')
    def test_exact_http_range_publishes_only_pinned_source_archive(self):
        raw=b'actual pinned source archive';archive=self.root/'source.gz';partial=archive.with_suffix('.gz.download');partial.write_bytes(raw[:5])
        response=io.BytesIO(raw[5:]);response.status=206;response.headers={'Content-Range':'bytes 5-'+str(len(raw)-1)+'/'+str(len(raw))}
        with patch.object(recover.urllib.request,'urlopen',return_value=response) as request:recover.download_pinned('https://official.test/source',archive,hashlib.sha256(raw).hexdigest())
        self.assertEqual(request.call_args.args[0].get_header('Range'),'bytes=5-');self.assertEqual(archive.read_bytes(),raw)

class CoreExportRestartTests(Fixture):
    def test_complete_bundle_schedule_is_announced_before_slow_core_work(self):
        source=self.root/'owned/GH_Data';(source/'Managed').mkdir(parents=True)
        for name in ('Managed/GH.Runtime.dll','globalgamemanagers','level0','resources.assets','ScriptingAssemblies.json'):
            (source/name).write_bytes(b'actual original '+name.encode())
        output=io.StringIO()
        with patch.dict(os.environ,{recover.build_progress.ENV:'1'}),contextlib.redirect_stdout(output), \
             patch.object(full_recovery,'catalog_bundle_plan',return_value={'groups':[{},{}]}) as plan, \
             patch.object(full_recovery,'build_tool',side_effect=RuntimeError('observed start of core tool')):
            with self.assertRaisesRegex(RuntimeError,'start of core tool'):
                full_recovery.prepare(source,self.root/'workspace',self.root/'tools','dotnet')
        events=[json.loads(line.removeprefix(recover.build_progress.PREFIX)) for line in output.getvalue().splitlines()
                if line.startswith(recover.build_progress.PREFIX)]
        schedule=next(event for event in events if event['phase']=='recovery-plan')
        self.assertEqual((schedule['done'],schedule['total'],schedule['unit']),(0,2,'batches'))
        self.assertLess(events.index(schedule),next(index for index,event in enumerate(events) if event['phase']=='recovery-section:core'))
        self.assertEqual(len(plan.call_args.kwargs['source_inventory']),5)

    def test_unfinished_core_retries_without_restaging_verified_inputs(self):
        source=self.root/'owned/GH_Data';(source/'Managed').mkdir(parents=True)
        for name in ('Managed/GH.Runtime.dll','globalgamemanagers','level0','resources.assets','ScriptingAssemblies.json'):(source/name).write_bytes(b'actual original '+name.encode())
        workspace=self.root/'workspace';calls=[];before=recover.source_inventory(source)
        def export(command,stage,target,log,evidence,settings):
            calls.append('export');project=target/'ExportedProject';row=self.asset(project,'a'*32,'native-core',1)
            if len(calls)==1:raise KeyboardInterrupt()
            Path(os.environ['QUEST_EXPORT_IDENTITIES']).write_text(json.dumps(row)+'\n');return project,[],[]
        def merged(game,core,identities,output,where,command,**_):
            import shutil
            shutil.copytree(core,output)
            return {'schema':1,'files':self.records(output),'completedGroups':[],'assetsRecovered':True}
        def managed(game,target,cache,**_):recover.write_json(target,{'schema':1})
        with patch.object(full_recovery,'build_tool',return_value=([],{'source':{'pinned':'exporter'}})),patch.object(full_recovery,'run_export',side_effect=export),patch.object(full_recovery,'repair_managed_plugins'),patch.object(full_recovery,'managed_inventory',side_effect=managed),patch.object(full_recovery,'run_recovery',side_effect=merged),patch.object(full_recovery,'catalog_bundle_plan',return_value={'groups':[]}):
            with self.assertRaises(KeyboardInterrupt):full_recovery.prepare(source,workspace,self.root/'tools','dotnet')
            self.assertFalse((workspace/'core-recovery.json').exists())
            with patch.object(recover.shutil,'copy2',wraps=recover.shutil.copy2) as copying:result=full_recovery.prepare(source,workspace,self.root/'tools','dotnet')
            self.assertEqual(copying.call_count,0)
        self.assertTrue(result['fullOriginalCatalogRecovered']);self.assertEqual(calls,['export','export']);self.assertEqual(recover.source_inventory(source),before)

if __name__=='__main__':unittest.main()
