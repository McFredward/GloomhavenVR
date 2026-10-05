"""Transactional restart proofs; no Unity import or tool downloads."""
import json
import hashlib
import io
import zipfile
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import venv

HERE = Path(__file__).resolve().parents[2] / 'tools/quest-builder'
sys.path.insert(0, str(HERE))
import storage
import dependencies


class Fixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.output = self.root / 'output'; self.output.mkdir()
        self.key = 'b' * 64; self.project = self.output / 'projects' / ('a' * 64)
    def tearDown(self): self.temp.cleanup()
    def pair(self, raw=b'initial original content'):
        storage.project_generation_paths(self.output, self.project, self.key)
        archive = self.project / storage.CONTENT_PATHS[0]; archive.parent.mkdir(parents=True, exist_ok=True); archive.write_bytes(raw)
        manifest = self.project / storage.CONTENT_PATHS[1]
        storage.write_json(manifest, {'schema': 1, 'inputKey': self.key, 'archive': archive.name, 'archiveSha256': storage.digest(archive)})
        return archive, manifest


class KernelOwnershipTests(Fixture):
    def test_hard_death_releases_guard_and_stale_status_is_recovered(self):
        code = 'import sys,time;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from storage import output_lock;\nwith output_lock(Path(sys.argv[2])):\n print("owned",flush=True);time.sleep(30)'
        child = subprocess.Popen([sys.executable, '-I', '-B', '-c', code, str(HERE), str(self.output)], stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(child.stdout.readline().strip(), 'owned')
            with self.assertRaises(storage.BuildError):
                with storage.output_lock(self.output): pass
            child.kill(); child.wait()
            self.assertTrue((self.output / '.builder.lock').exists())
            with storage.output_lock(self.output): self.assertTrue((self.output / '.builder.guard').exists())
            self.assertFalse((self.output / '.builder.lock').exists())
        finally:
            if child.poll() is None: child.kill(); child.wait()
            child.stdout.close()
    def test_live_legacy_pid_and_unrecognized_status_remain_protected(self):
        marker = self.output / '.builder.lock'; marker.write_text(str(os.getpid()))
        with self.assertRaises(storage.BuildError):
            with storage.output_lock(self.output): pass
        self.assertEqual(marker.read_text(), str(os.getpid()))
        marker.write_text('arbitrary content')
        with self.assertRaises(storage.BuildError):
            with storage.output_lock(self.output): pass
        self.assertEqual(marker.read_text(), 'arbitrary content')


class SnapshotRestartTests(Fixture):
    def test_interrupted_copy_reuses_verified_members_without_success_receipt(self):
        source = self.root / 'source'; source.mkdir()
        for name in ('one', 'two'): (source / name).write_bytes(name.encode())
        records = storage.inventory(source); destination = self.output / 'inputs/snapshot'; real_copy = storage.shutil.copyfile
        def interrupted(original, target):
            if original.name == 'two': raise KeyboardInterrupt()
            return real_copy(original, target)
        with patch.object(storage.shutil, 'copyfile', interrupted), self.assertRaises(KeyboardInterrupt): storage.snapshot(source, records, destination)
        self.assertFalse(destination.exists()); self.assertFalse((destination.with_name('snapshot.staging') / '.snapshot.json').exists())
        with patch.object(storage.shutil, 'copyfile', wraps=real_copy) as copying: storage.snapshot(source, records, destination)
        self.assertEqual(copying.call_count, 1); self.assertEqual(copying.call_args.args[0].name, 'two')
        self.assertTrue(storage.verify_files(destination, records)); self.assertTrue((destination / '.snapshot.json').exists())
    def test_unowned_staging_and_link_cannot_be_adopted(self):
        source = self.root / 'source'; source.mkdir(); (source / 'one').write_bytes(b'owned original')
        target = self.output / 'snapshot'; target.with_name('snapshot.staging').mkdir()
        with self.assertRaises(storage.BuildError): storage.snapshot(source, storage.inventory(source), target)
        if os.name != 'nt':
            target.with_name('snapshot.staging').rmdir(); target.with_name('snapshot.staging').symlink_to(source, target_is_directory=True)
            with self.assertRaises(storage.BuildError): storage.snapshot(source, storage.inventory(source), target)
        self.assertEqual((source / 'one').read_bytes(), b'owned original')


class ProjectRestartTests(Fixture):
    def test_generator_hard_death_preserves_library_and_repairs_owned_project(self):
        storage.project_generation_paths(self.output, self.project, self.key)
        library = self.project / 'Library'; library.mkdir(parents=True); (library / 'artifact').write_bytes(b'expensive imported artifact')
        code = 'import sys,os;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from storage import regenerate_project;\nwith regenerate_project(Path(sys.argv[2]),Path(sys.argv[3]),sys.argv[4]):\n Path(sys.argv[3]).mkdir();(Path(sys.argv[3])/"partial").write_bytes(b"interrupted");os._exit(0)'
        subprocess.run([sys.executable, '-I', '-B', '-c', code, str(HERE), str(self.output), str(self.project), self.key], check=True)
        self.assertFalse(library.exists())
        with storage.regenerate_project(self.output, self.project, self.key):
            self.project.mkdir(); (self.project / 'repaired').write_bytes(b'complete generated source')
        self.assertEqual((library / 'artifact').read_bytes(), b'expensive imported artifact')
        self.assertFalse((self.project / 'partial').exists())
    def test_foreign_partial_project_and_second_library_are_never_deleted(self):
        self.project.mkdir(parents=True); (self.project / 'foreign').write_text('keep')
        with self.assertRaises(storage.BuildError): storage.restore_project_library(self.output, self.project, self.key)
        self.assertEqual((self.project / 'foreign').read_text(), 'keep')
        storage.write_json(self.project / 'QuestBuilderSettings.json', {'inputKey': self.key})
        _, backup = storage.project_generation_paths(self.output, self.project, self.key); backup.mkdir(); (self.project / 'Library').mkdir()
        with self.assertRaises(storage.BuildError): storage.restore_project_library(self.output, self.project, self.key)
        self.assertTrue(backup.exists()); self.assertTrue((self.project / 'Library').exists())


class ContentRestartTests(Fixture):
    def test_interrupted_exclusion_and_torn_repack_restore_original_pair(self):
        archive, manifest = self.pair(); before = (archive.read_bytes(), manifest.read_bytes())
        with self.assertRaises(KeyboardInterrupt):
            with storage.project_content_transaction(self.output, self.project, self.key):
                excluded = self.project / 'QuestCampaignEvidence/excluded-payload' / archive.name
                excluded.parent.mkdir(parents=True); archive.replace(excluded); manifest.write_text('half published manifest')
                (archive.parent / (archive.name + '.repack-' + 'c' * 32)).write_bytes(b'incomplete repack')
                raise KeyboardInterrupt()
        storage.recover_project_content(self.output, self.project, self.key)
        self.assertEqual((archive.read_bytes(), manifest.read_bytes()), before)
        self.assertFalse(excluded.exists()); self.assertEqual(list(archive.parent.glob('*.repack-*')), [])
        self.assertTrue(storage.project_content_valid(self.output, self.project, self.key))
    def test_successful_native_repack_does_not_invalidate_immutable_prepare(self):
        archive, manifest = self.pair(); immutable = self.project / 'source.cs'; immutable.write_bytes(b'original source')
        storage.Stages(self.output).run('prepare', self.key, lambda: ([immutable], {'project': 'fixture'}))
        storage.publish_project_content(self.output, self.project, self.key)
        with storage.project_content_transaction(self.output, self.project, self.key):
            temporary = archive.with_suffix('.new'); temporary.write_bytes(b'actual cooked addressables'); temporary.replace(archive)
            current = json.loads(manifest.read_text()); current['archiveSha256'] = storage.digest(archive); storage.write_json(manifest, current)
        self.assertTrue(storage.project_content_valid(self.output, self.project, self.key))
        self.assertIsNotNone(storage.Stages(self.output).valid('prepare', self.key))
        archive.unlink(); self.assertFalse(storage.project_content_valid(self.output, self.project, self.key))
    def test_changed_backup_and_partial_journal_refuse_overwrite(self):
        archive, manifest = self.pair()
        with self.assertRaises(RuntimeError):
            with storage.project_content_transaction(self.output, self.project, self.key): raise RuntimeError('cancelled')
        journal = self.output / 'cache/project-content-transactions' / self.project.name / 'pending.json'
        value = json.loads(journal.read_text()); original = archive.read_bytes()
        value['files'] = value['files'][:1]; storage.write_json(journal, value)
        with self.assertRaises(storage.BuildError): storage.recover_project_content(self.output, self.project, self.key)
        self.assertEqual(archive.read_bytes(), original)


class PythonAbiTests(Fixture):
    def test_actual_child_abi_and_prefix_are_checked_before_native_imports(self):
        root = self.root / 'venv'; venv.EnvBuilder(with_pip=False).create(root)
        executable = root / ('Scripts/python.exe' if os.name == 'nt' else 'bin/python')
        self.assertTrue(dependencies.matching_python(executable, root))
        self.assertFalse(dependencies.matching_python(executable, self.root / 'other'))
        actual = dependencies.python_abi()
        with patch.object(dependencies, 'python_abi', return_value={**actual, 'cacheTag': 'cpython-incompatible'}):
            self.assertFalse(dependencies.matching_python(executable, root))

class ShaderAuditModeTests(unittest.TestCase):
    def test_full_compile_audit_is_explicit_and_inherited_flags_are_removed(self):
        import builder
        normal=builder.parser().parse_args(['build']);explicit=builder.parser().parse_args(['build','--validate-campaign-shaders'])
        self.assertFalse(normal.validate_campaign_shaders);self.assertTrue(explicit.validate_campaign_shaders)
        env={'GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS':'1','unrelated':'keep'}
        builder.campaign_shader_environment(normal,env);self.assertEqual(env,{'unrelated':'keep'})
        builder.campaign_shader_environment(explicit,env);self.assertEqual(env['GHVR_QUEST_VALIDATE_CAMPAIGN_SHADERS'],'1')

class SdkProvisionRestartTests(Fixture):
    def test_pinned_sdk_range_resume_and_complete_partial_reuse(self):
        raw=b'official pinned sdk fixture';archive=self.output/'sdk.zip';partial=archive.with_suffix('.zip.download');partial.write_bytes(raw[:7])
        spec={'url':'https://official.test/sdk.zip','hash':hashlib.sha512(raw).hexdigest()}
        response=io.BytesIO(raw[7:]);response.status=206;response.headers={'Content-Range':'bytes 7-'+str(len(raw)-1)+'/'+str(len(raw))}
        with patch.object(dependencies.urllib.request,'urlopen',return_value=response) as request:dependencies.download_sdk(spec,archive)
        self.assertEqual(request.call_args.args[0].get_header('Range'),'bytes=7-');self.assertEqual(archive.read_bytes(),raw)
        archive.replace(partial)
        with patch.object(dependencies.urllib.request,'urlopen',side_effect=AssertionError('already complete')):dependencies.download_sdk(spec,archive)
        self.assertEqual(archive.read_bytes(),raw)
    def test_owned_interrupted_extraction_repairs_without_redownloading(self):
        source=self.root/'source';cache=self.output/'tools';cache.mkdir();archive=cache/'sdk.zip'
        with zipfile.ZipFile(archive,'w') as zipped:zipped.writestr('dotnet.exe' if os.name=='nt' else 'dotnet',b'exact pinned sdk bytes')
        with archive.open('rb') as stream:pinned=hashlib.file_digest(stream,'sha512').hexdigest()
        rid={'win32':'win','linux':'linux','darwin':'osx'}[sys.platform]+'-'+('arm64' if dependencies.platform.machine().lower() in ('arm64','aarch64') else 'x64')
        version='10.0.fixture';storage.write_json(source/'tools/quest-builder/dependencies-lock.json',{'dotnetSdkVersion':version,'dotnet':{rid:{'url':'https://official.test/sdk.zip','hash':pinned}}})
        stage=cache/('dotnet-'+version+'-'+rid+'.extracting');stage.mkdir();(stage/'unfinished').write_bytes(b'partial')
        storage.write_json(stage.with_name(stage.name+'.json'),{'schema':1,'owner':'Quest recovery SDK extraction','version':version,'archiveSha512':pinned})
        with patch.object(dependencies.urllib.request,'urlopen',side_effect=AssertionError('cached archive')):executable=dependencies.dotnet10(cache,source)
        self.assertEqual(executable.read_bytes(),b'exact pinned sdk bytes');self.assertFalse((executable.parent/'unfinished').exists())

if __name__ == '__main__': unittest.main()
