"""Focused wizard workflow, durable resume, process and HTTP boundary proofs."""
import hashlib
import http.client
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import state
import provision
import wizard
import server
from processes import Supervisor
import qualification


class Fixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / 'owned')
    def tearDown(self): self.temp.cleanup()
    def plan(self, **changes): return self.store.create(wizard.choices({'gameRoot': str(self.root / 'Game'), **changes}))
    def actions(self, calls, failure=None):
        def action(stage):
            def run(saved, supervisor):
                calls.append(stage)
                if failure and failure(stage, saved): raise state.Cancelled()
                output = self.store.session_dir(saved['session']) / (stage + '.output')
                output.write_text(stage)
                return [output], {'name': stage}
            return run
        return {name: action(name) for name in state.STAGES}


class ResumeTests(Fixture):
    def test_completed_stages_reuse_after_cancel_and_fresh_store(self):
        saved = self.plan(); calls = []
        first = wizard.Engine(self.store, actions=self.actions(calls, lambda s, _: s == 'build')).run(saved['session'])
        self.assertEqual(first['status'], 'cancelled')
        second_store = state.Store(self.store.root)
        second = wizard.Engine(second_store, actions=self.actions(calls)).run(saved['session'])
        self.assertEqual(second['status'], 'complete')
        self.assertEqual(calls.count('tools'), 1)
        self.assertEqual(calls.count('build'), 2)
        self.assertEqual(second['progress']['percent'], 100)
        self.assertNotIn('outputs', second['completed']['tools'])

    def test_install_retry_does_not_repeat_build(self):
        saved = self.plan(); calls = []
        wizard.Engine(self.store, actions=self.actions(calls, lambda s, _: s == 'install')).run(saved['session'])
        wizard.Engine(self.store, actions=self.actions(calls)).run(saved['session'])
        self.assertEqual(calls.count('build'), 1)
        self.assertEqual(calls.count('install'), 2)

    def test_changed_output_rebuilds_affected_dependency_chain(self):
        saved = self.plan(); calls = []
        wizard.Engine(self.store, actions=self.actions(calls)).run(saved['session'])
        (self.store.session_dir(saved['session']) / 'profile.output').write_text('tampered')
        wizard.Engine(self.store, actions=self.actions(calls)).run(saved['session'])
        # Repairs this exact stage. Unchanged restored bytes/identity allow
        # downstream receipts to remain valid; no invented cache invalidation.
        self.assertEqual(calls.count('profile'), 2)
        self.assertEqual(calls.count('source'), 1)

    def test_amended_profile_reuses_tools_source_unity(self):
        saved = self.plan(); calls = []
        wizard.Engine(self.store, actions=self.actions(calls)).run(saved['session'])
        selected = dict(saved['choices'], steamId='76561198000000001')
        self.store.amend(saved['session'], selected)
        wizard.Engine(self.store, actions=self.actions(calls)).run(saved['session'])
        for name in ('tools', 'source', 'unity'): self.assertEqual(calls.count(name), 1)
        for name in ('profile', 'inspect', 'build', 'install'): self.assertEqual(calls.count(name), 2)

    def test_kernel_lock_conflict_and_release_after_death(self):
        script = 'import sys,time;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from state import file_lock;\nwith file_lock(Path(sys.argv[2])):\n print("locked",flush=True);time.sleep(30)'
        process = subprocess.Popen([sys.executable, '-I', '-B', '-c', script, str(ROOT/'tools/quest-wizard'), str(self.store.root/'run.lock')], stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(process.stdout.readline().strip(), 'locked')
            with self.assertRaises(state.WizardError) as error:
                with state.file_lock(self.store.root/'run.lock'): pass
            self.assertEqual(error.exception.code, 'already_running')
            process.kill(); process.wait()
            with state.file_lock(self.store.root/'run.lock'): pass
        finally:
            if process.poll() is None: process.kill(); process.wait()
            process.stdout.close()

    def test_receipt_escape_and_symlink_rejected(self):
        saved = self.plan(); output = self.store.session_dir(saved['session'])/'output'; output.write_text('valid')
        receipt = self.store.publish(saved['session'], 'tools', 'key', [output], {})
        for unsafe in ('../outside', 'C:/outside', 'folder\\outside'):
            receipt['outputs'][0]['path'] = unsafe
            state.atomic_json(self.store.receipt(saved['session'], 'tools'), receipt)
            self.assertIsNone(self.store.valid(saved['session'], 'tools', 'key'))
        if os.name != 'nt':
            link = self.store.root/'link'; link.symlink_to(output)
            with self.assertRaises(state.WizardError): self.store.records([link])

    def test_no_receipt_on_failed_action(self):
        saved=self.plan(); calls=[]
        actions=self.actions(calls)
        def failed(*_): raise OSError('fixture failure')
        actions['tools']=failed
        result=wizard.Engine(self.store, actions=actions).run(saved['session'])
        self.assertEqual(result['status'],'failed')
        self.assertFalse(self.store.receipt(saved['session'],'tools').exists())

    def test_isolated_cli_plan_and_status(self):
        selected=self.root/'choices.json';selected.write_text(json.dumps({'gameRoot':str(self.root/'Game')}))
        command=[sys.executable,'-I','-B',str(ROOT/'tools/quest-wizard/wizard.py')]
        result=subprocess.run(command+['plan','--state-root',str(self.store.root),'--choices-file',str(selected)],capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stderr)
        value=json.loads(result.stdout)
        status=subprocess.run(command+['status','--state-root',str(self.store.root),'--session',value['session']],capture_output=True,text=True)
        self.assertEqual(json.loads(status.stdout)['state']['choices']['gameRoot'],str(self.root/'Game'))

    def test_invalid_dlc_value_clean_error(self):
        with self.assertRaises(state.WizardError): wizard.choices({'gameRoot':'G','ownedDlc':[{}]})


class Response(io.BytesIO):
    def __init__(self, raw, status=200, **headers): super().__init__(raw);self.status=status;self.headers=headers


class ProvisionTests(Fixture):
    def spec(self, raw): return {'url':'https://example.invalid/tool','algorithm':'sha256','hash':hashlib.sha256(raw).hexdigest(),'size':len(raw),'executable':'bin/tool.exe'}
    def test_range_resume_exact_bytes(self):
        raw=b'original fixture tool'; path=self.store.root/'tool.zip'
        path.with_name('tool.zip.partial').write_bytes(raw[:5]); requests=[]
        def opener(request,**_):
            requests.append(request.get_header('Range'))
            return Response(raw[5:],206,**{'Content-Range':f'bytes 5-{len(raw)-1}/{len(raw)}'})
        provision.download(self.spec(raw),path,opener=opener)
        self.assertEqual(path.read_bytes(),raw);self.assertEqual(requests,['bytes=5-'])
    def test_complete_partial_needs_no_network(self):
        raw=b'complete';path=self.store.root/'tool';path.with_name('tool.partial').write_bytes(raw)
        provision.download(self.spec(raw),path,opener=lambda *_a,**_k:self.fail('network requested'))
        self.assertEqual(path.read_bytes(),raw)
    def test_incorrect_range_retains_bytes(self):
        raw=b'fixture';path=self.store.root/'tool';partial=path.with_name('tool.partial');partial.write_bytes(raw[:2])
        with self.assertRaises(state.WizardError): provision.download(self.spec(raw),path,opener=lambda *_a,**_k:Response(raw[2:],206,**{'Content-Range':'bytes 1-5/7'}))
        self.assertEqual(partial.read_bytes(),raw[:2]);self.assertFalse(path.exists())
    def test_ignored_range_restarts_verified_download(self):
        raw=b'fixture';path=self.store.root/'tool';path.with_name('tool.partial').write_bytes(b'old')
        provision.download(self.spec(raw),path,opener=lambda *_a,**_k:Response(raw,200,**{'Content-Length':str(len(raw))}))
        self.assertEqual(path.read_bytes(),raw)
    def test_checksum_failure_has_no_completed_output(self):
        path=self.store.root/'tool'
        with self.assertRaises(state.WizardError): provision.download(self.spec(b'good'),path,opener=lambda *_a,**_k:Response(b'bad'))
        self.assertFalse(path.exists());self.assertFalse(path.with_name('tool.partial').exists())
    def test_cancelled_extract_resumes_and_repairs_crc(self):
        archive=self.store.root/'archive.zip'
        with zipfile.ZipFile(archive,'w') as z:z.writestr('bin/tool.exe',b'tool');z.writestr('data/file',b'original')
        count=0
        def check():
            nonlocal count
            count+=1
            if count==4:raise state.Cancelled()
        destination=self.store.root/'tooltree';spec=self.spec(archive.read_bytes())
        with self.assertRaises(state.Cancelled):provision.extract_owned(archive,destination,spec,check)
        (destination/'bin/tool.exe').write_bytes(b'fake')
        executable=provision.extract_owned(archive,destination,spec)
        self.assertEqual(executable.read_bytes(),b'tool');self.assertEqual((destination/'data/file').read_bytes(),b'original')
    def test_unsafe_zip_never_escapes_owned_root(self):
        for name in ('../outside','C:/outside','bin\\outside'):
            archive=self.store.root/'bad.zip'
            with zipfile.ZipFile(archive,'w') as z:z.writestr(name,b'bad')
            with self.assertRaises(state.WizardError): provision.extract_owned(archive,self.store.root/('dest'+str(len(name))),self.spec(archive.read_bytes()))
        self.assertFalse((self.root/'outside').exists())


@unittest.skipIf(os.name=='nt','POSIX fixture; Windows uses suspended process Job Objects')
class ProcessTests(Fixture):
    def test_low_space_prevents_launching_another_heavy_child(self):
        saved = self.plan(); supervisor = Supervisor(self.store, saved['session'], poll=.01)
        supervisor.set_stage('build')
        with mock.patch.object(qualification.shutil, 'disk_usage', return_value=type('Usage', (), {'free': 1})()), \
             mock.patch('processes.subprocess.Popen', side_effect=AssertionError('Heavy child must not start')):
            with self.assertRaises(state.WizardError) as error:
                supervisor.run([sys.executable, '-c', 'pass'], self.store.session_dir(saved['session']) / 'logs/build.log')
        self.assertEqual(error.exception.code, 'workspace_runtime_space_low')

    def test_live_space_guard_stops_owned_child_tree_and_preserves_logs(self):
        saved = self.plan(); session = saved['session']; childfile = self.root / 'space-grandchild.pid'
        log = self.store.session_dir(session) / 'logs/build.log'
        script = 'import subprocess,sys,time;p=subprocess.Popen([sys.executable,"-c","import time;time.sleep(60)"]);open(sys.argv[1],"w").write(str(p.pid));print("ready",flush=True);time.sleep(60)'
        supervisor = Supervisor(self.store, session, grace=.1, poll=.01); supervisor.set_stage('build')
        def free(_):
            return type('Usage', (), {'free': 100 * qualification.GIB if not childfile.exists() else qualification.GIB})()
        with mock.patch.object(qualification.shutil, 'disk_usage', side_effect=free), \
             mock.patch.object(qualification, 'SPACE_CHECK_SECONDS', .01), \
             mock.patch.object(qualification, 'tree_bytes', side_effect=AssertionError('No cache scanning during polling')):
            with self.assertRaises(state.WizardError) as error:
                supervisor.run([sys.executable, '-c', script, str(childfile)], log, timeout=5)
        self.assertEqual(error.exception.code, 'workspace_runtime_space_low')
        self.assertIn('ready', log.read_text())
        pid = int(childfile.read_text()); proc = Path('/proc') / str(pid) / 'stat'
        self.assertTrue(not proc.exists() or proc.read_text().rsplit(')', 1)[1].split()[0] == 'Z')
        self.assertFalse((self.store.session_dir(session) / 'child.json').exists())
        self.assertIn('process_finished', (log.parent / 'progress.log').read_text())

    def test_cancel_kills_owned_descendants_and_retains_log(self):
        saved=self.plan();session=saved['session'];childfile=self.root/'grandchild.pid';log=self.store.session_dir(session)/'logs/build.log'
        script='import subprocess,sys,time; p=subprocess.Popen([sys.executable,"-c","import time;time.sleep(60)"]);open(sys.argv[1],"w").write(str(p.pid));print("ready",flush=True);time.sleep(60)'
        caught=[]
        def run():
            try:Supervisor(self.store,session,grace=.2,poll=.01).run([sys.executable,'-c',script,str(childfile)],log)
            except state.Cancelled:caught.append(True)
        thread=threading.Thread(target=run);thread.start()
        deadline=time.monotonic()+5
        while not childfile.exists() and time.monotonic()<deadline:time.sleep(.01)
        self.assertTrue(childfile.exists());pid=int(childfile.read_text());self.store.cancel(session);thread.join(5)
        self.assertFalse(thread.is_alive());self.assertEqual(caught,[True]);self.assertIn('ready',log.read_text())
        proc=Path('/proc')/str(pid)/'stat'
        self.assertTrue(not proc.exists() or proc.read_text().rsplit(')',1)[1].split()[0]=='Z')
        self.assertFalse((self.store.session_dir(session)/'child.json').exists())


class HttpTests(Fixture):
    def setUp(self):
        super().setUp();ui=self.root/'ui';ui.mkdir();(ui/'index.html').write_text('<html>owned UI</html>');self.calls=[]
        engine=lambda store:wizard.Engine(store,actions=self.actions(self.calls))
        self.http=server.LocalServer(self.store,ui,engine_factory=engine,discover=lambda *_:{'schema':1,'event':'discovery','capabilities':{'artwork':False}})
        self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):self.http.shutdown();self.http.close_owned();self.thread.join();super().tearDown()
    def request(self,route,body=None,headers=None):
        connection=http.client.HTTPConnection(*self.http.server_address,timeout=3)
        selected={'X-Quest-Token':self.http.token,'Origin':self.http.origin}
        if headers is not None:selected=headers
        if body is not None:selected['Content-Type']='application/json'
        connection.request('POST' if body is not None else 'GET',route,None if body is None else json.dumps(body),selected)
        response=connection.getresponse();raw=response.read();code=response.status;connection.close();return code,json.loads(raw)
    def test_origin_and_token_reject_foreign_local_requests(self):
        for headers in ({},{'Origin':self.http.origin,'X-Quest-Token':'wrong'},{'Origin':'http://evil.invalid','X-Quest-Token':self.http.token}):
            code,value=self.request('/api/discover',headers=headers);self.assertEqual(code,403);self.assertEqual(value['event'],'error')
    def test_same_origin_browser_get_fetch_metadata(self):
        code,_=self.request('/api/discover',headers={'X-Quest-Token':self.http.token,'Sec-Fetch-Site':'same-origin'});self.assertEqual(code,200)
        code,_=self.request('/api/discover',headers={'X-Quest-Token':self.http.token,'Sec-Fetch-Site':'cross-site'});self.assertEqual(code,403)
    def test_plan_run_events_and_resume_same_session(self):
        code,result=self.request('/api/plan',{'choices':{'gameRoot':str(self.root/'Game')}});self.assertEqual(code,200)
        session=result['session'];self.assertEqual(self.request('/api/run',{'session':session})[1]['event'],'started')
        deadline=time.monotonic()+3
        while self.store.load(session)['status']!='complete' and time.monotonic()<deadline:time.sleep(.01)
        result=self.request('/api/status?session='+session)[1];self.assertEqual(result['state']['status'],'complete')
        events=self.request('/api/events?session='+session+'&after=0')[1];self.assertGreater(events['lastEvent'],0)
        amended=self.request('/api/plan',{'session':session,'choices':{'gameRoot':str(self.root/'Game'),'install':False}})[1]
        self.assertEqual(amended['session'],session)
    def test_static_traversal_and_arbitrary_log_stage_rejected(self):
        code,_=self.request('/%2e%2e/outside');self.assertEqual(code,400)
        saved=self.plan();code,_=self.request('/api/log?session='+saved['session']+'&stage=../../outside');self.assertEqual(code,400)
    def test_bounded_log_tail(self):
        saved=self.plan();path=self.store.session_dir(saved['session'])/'logs/build.log';path.parent.mkdir();path.write_text('x'*70000)
        code,value=self.request('/api/log?session='+saved['session']+'&stage=build');self.assertEqual(code,200);self.assertTrue(value['truncated']);self.assertEqual(len(value['text']),65536)
    def test_unity_stage_log_includes_linux_hub_window_and_install_path_failures(self):
        saved=self.plan();logs=self.store.session_dir(saved['session'])/'logs';logs.mkdir()
        (logs/'unity-hub-window.log').write_text('missing desktop library fixture')
        (logs/'unity-install-path.log').write_text('owned editor path fixture failure')
        (logs/'unity-protocol-confirm.log').write_text('desktop callback fixture failure')
        (logs/'unrelated-private.log').write_text('must not appear')
        code,value=self.request('/api/log?session='+saved['session']+'&stage=unity')
        self.assertEqual(code,200);self.assertIn('missing desktop library fixture',value['text'])
        self.assertIn('owned editor path fixture failure',value['text']);self.assertNotIn('must not appear',value['text'])
        self.assertIn('desktop callback fixture failure',value['text'])
    def test_build_log_includes_actual_nested_failure_without_arbitrary_cache_reads(self):
        saved=self.plan();session=saved['session'];key='e'*64
        failure=self.store.root/'build/last-failure.json'
        state.atomic_json(failure,{'schema':1,'stage':'recovery','key':key,'message':'export failed'})
        folder=self.store.root/'build/cache/full-original-recovery'/key
        for relative,text in (('core-export.log','loading original core'),('BundleRecovery/batch-000/export.log','FAILED: original object key'),('BundleRecovery/batch-000/private.json','must not appear')):
            path=folder/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(text)
        code,value=self.request('/api/log?session='+session+'&stage=build')
        self.assertEqual(code,200);self.assertIn('FAILED: original object key',value['text'])
        self.assertIn('loading original core',value['text']);self.assertNotIn('must not appear',value['text'])
    def test_api_cannot_dump_arbitrary_file_or_command(self):
        code,value=self.request('/api/browse',{'kind':'game','command':'malicious'});self.assertEqual(code,400)
        code,value=self.request('/api/artwork?session='+self.plan()['session']+'&id=../secret');self.assertEqual(code,400)

    def test_startup_import_and_type_errors_are_structured_logged_and_recoverable(self):
        for exception in (ModuleNotFoundError, TypeError):
            with self.subTest(exception=exception.__name__):
                def fail(*_): raise exception('fixture startup failure ' + self.http.token)
                with mock.patch.object(self.http, 'discover', side_effect=fail):
                    code, value = self.request('/api/discover')
                self.assertEqual(code, 400)
                self.assertEqual(value['code'], 'request_failed')
                self.assertNotEqual(value['message']['en'], value['message']['de'])
                log = (self.store.root / 'logs/wizard-requests.log').read_text()
                self.assertIn(exception.__name__ + ': fixture startup failure', log)
                self.assertIn('Traceback', log)
                self.assertNotIn(self.http.token, log)
                self.assertNotIn(self.http.token, json.dumps(value))
                self.assertEqual(self.request('/api/discover')[0], 200)

    def test_request_failure_log_rotation_retains_one_previous_file(self):
        log = self.store.root / 'logs/wizard-requests.log'
        log.parent.mkdir(); log.write_text('previous bounded history\n' + 'x' * 262144)
        self.http.request_failure(TypeError('fixture picker failed'))
        self.assertIn('fixture picker failed', log.read_text())
        self.assertNotIn('previous bounded history', log.read_text())
        self.assertIn('previous bounded history', log.with_name('wizard-requests.previous.log').read_text())



class SourceDependencyTests(Fixture):
    def test_declared_projects_fetch_all_before_build_and_use_owned_game(self):
        import types
        checkout=self.store.root/'checkout';logs=self.store.root/'logs';logs.mkdir()
        recipe=checkout/'scripts/build-runtimedeps.sh';recipe.parent.mkdir(parents=True)
        package_rows=(('Management','management','4.5.0'),('CoreUtils','core-utils','2.2.3'),('OpenXR','openxr','1.10.0'))
        recipe.write_text('\n'.join('"com.unity.xr.'+p+' '+v+'"' for _,p,v in package_rows))
        for name,_,version in package_rows:
            project=checkout/'tools/RuntimeDepsBuild'/('Unity.XR.'+name)/('Unity.XR.'+name+'.csproj');project.parent.mkdir(parents=True)
            project.write_text('<Project><PackageSourceVersion>'+version+'</PackageSourceVersion></Project>')
        managed=self.root/'OwnedGame/Gloomhaven_Data';managed.mkdir(parents=True)
        calls=[]
        class SimulatedTools:
            def run(inner,argv,log,**kwargs):
                argv=list(map(str,argv));calls.append(argv)
                if 'init' in argv:(Path(argv[2])/'.git').mkdir()
                elif 'checkout' in argv:
                    directory=Path(argv[2]);version=next(v for _,p,v in package_rows if directory.name=='com.unity.xr.'+p)
                    (directory/'package.json').write_text(json.dumps({'version':version}))
                elif 'build' in argv:
                    self.assertEqual(sum('checkout' in c for c in calls),3)
                    self.assertIn('-p:GameManaged='+str(managed/'Managed'),argv)
                    project=Path(argv[2]);built=project.parent/'bin/Release/net472'/(project.stem+'.dll');built.parent.mkdir(parents=True);built.write_bytes(b'compiled '+project.stem.encode())
        import discovery
        with mock.patch.object(discovery,'builder',return_value=types.SimpleNamespace(game_data=lambda _:managed)):
            result=provision.derive_runtime_dependencies(checkout,managed,{'git':'git.exe','dotnet8':'dotnet.exe'},SimulatedTools(),logs,lambda:None)
        proof=state.read_json(result);self.assertEqual(len(proof['assemblies']),3)
        for name,_,_ in package_rows:self.assertTrue((checkout/'libs/RuntimeDeps'/('Unity.XR.'+name+'.dll')).is_file())

    def test_existing_local_assemblies_need_no_network_build(self):
        import types,discovery
        checkout=self.store.root/'checkout';recipe=checkout/'scripts/build-runtimedeps.sh';recipe.parent.mkdir(parents=True)
        rows=(('Management','management','4.5.0'),('CoreUtils','core-utils','2.2.3'),('OpenXR','openxr','1.10.0'))
        recipe.write_text('\n'.join('"com.unity.xr.'+p+' '+v+'"' for _,p,v in rows))
        for name,_,version in rows:
            project=checkout/'tools/RuntimeDepsBuild'/('Unity.XR.'+name)/('Unity.XR.'+name+'.csproj');project.parent.mkdir(parents=True);project.write_text('<PackageSourceVersion>'+version+'</PackageSourceVersion>')
            target=checkout/'libs/RuntimeDeps'/('Unity.XR.'+name+'.dll');target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(b'actual supplied input')
        with mock.patch.object(discovery,'builder',return_value=types.SimpleNamespace(game_data=lambda _:self.root/'Game_Data')):
            result=provision.derive_runtime_dependencies(checkout,self.root/'Game',{},mock.Mock(),self.root/'logs',lambda:None)
        self.assertEqual({r['source'] for r in state.read_json(result)['assemblies'].values()},{'supplied-local-assembly'})

class InspectIntegrationTests(Fixture):
    def fixture(self):
        saved=self.plan();source=self.store.root/'source';source.mkdir();game=self.root/'Game_Data';game.mkdir()
        saved['completed']={'source':{'details':{'sourceRoot':str(source)}},'tools':{'details':{'git':'git','dotnet8':'dotnet','dotnet10':'dotnet'}},'unity':{'details':{'unityEditor':'Unity'}},'profile':{'details':{'profilePath':'profile.json','steamLogo':'logo.png'}}}
        files=[{'path':'resources.assets','size':3,'sha256':'a'*64}]
        original={'schema':1,'target':'game','game':{'files':files,'key':state.value_hash({'files':files})},'mod':{'files':[],'key':state.value_hash({'files':[]}),'modBuild':623},'padding':'x'*(2*1048576)}
        original['inputKey']=state.value_hash(original)
        manifest=self.store.root/'build/manifests'/(original['inputKey']+'.json');manifest.parent.mkdir(parents=True);state.atomic_json(manifest,original)
        pointer=self.store.root/'build/latest-input.json'
        pointer.write_text(json.dumps({'manifest':manifest.relative_to(self.store.root/'build').as_posix(),'sourceGameRoot':str(game),'sourceRepo':str(source)}))
        import types
        return saved,source,game,manifest,pointer,types.SimpleNamespace(game_data=lambda _:game)
    def test_real_schema_less_pointer_and_large_manifest_are_accepted(self):
        saved,_,_,manifest,pointer,module=self.fixture()
        with mock.patch.object(wizard.discovery,'builder',return_value=module):
            outputs,details=wizard.Engine(self.store).stage_inspect(saved,mock.Mock())
        self.assertEqual(outputs,[pointer,manifest]);self.assertEqual(details['inputKey'],manifest.stem)
    def test_pointer_escape_wrong_source_or_manifest_identity_rejected(self):
        saved,source,game,manifest,pointer,module=self.fixture()
        valid=json.loads(pointer.read_text())
        for changes in ({'manifest':'../outside.json'},{'sourceRepo':str(source/'other')},{'manifest':str(manifest)}):
            pointer.write_text(json.dumps({**valid,**changes}))
            with mock.patch.object(wizard.discovery,'builder',return_value=module):
                with self.assertRaises(state.WizardError):wizard.Engine(self.store).stage_inspect(saved,mock.Mock())
        pointer.write_text(json.dumps(valid));value=state.read_json(manifest,limit=64*1048576);value['mod']['modBuild']+=1;state.atomic_json(manifest,value)
        with mock.patch.object(wizard.discovery,'builder',return_value=module):
            with self.assertRaises(state.WizardError):wizard.Engine(self.store).stage_inspect(saved,mock.Mock())
    def test_malformed_receipt_paths_are_cache_misses(self):
        saved=self.plan();output=self.store.session_dir(saved['session'])/'out';output.write_text('valid')
        proof=self.store.publish(saved['session'],'tools','key',[output],{})
        for value in (None,False,{},[],27):
            proof['outputs'][0]['path']=value;state.atomic_json(self.store.receipt(saved['session'],'tools'),proof)
            self.assertIsNone(self.store.valid(saved['session'],'tools','key'))

class UiModuleIntegrationTests(Fixture):
    def setUp(self):
        super().setUp();ui=self.root/'ui';ui.mkdir();(ui/'index.html').write_text('<html>owned UI</html>')
        self.http=server.LocalServer(self.store,ui)
        self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):self.http.shutdown();self.http.close_owned();self.thread.join();super().tearDown()
    def test_native_module_route_mime_and_owned_blob_csp(self):
        (self.http.ui_root/'app.mjs').write_text('export const value = 1;')
        connection=http.client.HTTPConnection(*self.http.server_address,timeout=3)
        connection.request('GET','/app.mjs')
        response=connection.getresponse();self.assertEqual(response.status,200)
        self.assertEqual(response.getheader('Content-Type'),'text/javascript; charset=utf-8')
        self.assertIn('img-src \'self\' data: blob:',response.getheader('Content-Security-Policy'))
        self.assertIn(b'export const value',response.read());connection.close()

class UnityProvisionTests(Fixture):
    def setUp(self):
        super().setUp()
        import unity_setup
        desktop = mock.patch.object(unity_setup, '_linux_desktop')
        desktop.start(); self.addCleanup(desktop.stop)
    def test_missing_hub_download_stops_at_explicit_setup_action(self):
        import unity_setup
        saved=self.plan(acceptUnityTerms=True);setup=self.store.root/'setup.exe';setup.write_bytes(b'fixture verified setup')
        with mock.patch.object(unity_setup.discovery,'unity_paths',return_value=([],[])),mock.patch.object(provision,'download',return_value=setup) as download,mock.patch.object(unity_setup,'_wait',side_effect=state.Cancelled()) as wait, \
             mock.patch.object(unity_setup, 'os', type('WindowsFixture', (), {'name': 'nt'})()), \
             mock.patch.object(provision, 'host_key', return_value='windows-x64'):
            with self.assertRaises(state.Cancelled):wizard.Engine(self.store).stage_unity(saved,mock.Mock())
        download.assert_called_once();self.assertEqual(wait.call_args.args[2],'unity_hub_setup')
        self.assertEqual(download.call_args.args[0]['algorithm'],'sha512')
        self.assertFalse((self.store.session_dir(saved['session'])/'unity.json').exists())
    def test_terms_gate_precedes_every_download_or_install(self):
        import unity_setup
        saved=self.plan();supervisor=mock.Mock()
        with mock.patch.object(unity_setup.discovery,'unity_paths',return_value=([],[])),mock.patch.object(provision,'download') as download:
            with self.assertRaises(state.WizardError) as error:wizard.Engine(self.store).stage_unity(saved,supervisor)
        self.assertEqual(error.exception.code,'unity_terms_required');download.assert_not_called();supervisor.run.assert_not_called()
    def test_unsupported_hub_enters_repeatable_manual_install_action(self):
        import unity_setup
        saved=self.plan(acceptUnityTerms=True,unityHub=str(self.root/'Hub.exe'));supervisor=mock.Mock();codes=[]
        def wait(_store,_session,code,*_args,**_kwargs):
            codes.append(code)
            if code=='unity_install_incomplete':raise state.Cancelled()
        def process(argv,log,**_):log.parent.mkdir(parents=True,exist_ok=True);log.write_text('CLI unsupported')
        supervisor.run.side_effect=process
        with mock.patch.object(unity_setup.discovery,'unity_paths',return_value=([],[])),mock.patch.object(unity_setup,'_wait',side_effect=wait):
            with self.assertRaises(state.Cancelled):wizard.Engine(self.store).stage_unity(saved,supervisor)
        self.assertEqual(codes,['unity_login_required','unity_install_incomplete']);self.assertEqual(supervisor.run.call_count,1)
    def test_existing_editor_gets_modules_then_requires_actual_probe(self):
        import unity_setup
        saved=self.plan(acceptUnityTerms=True,unityHub=str(self.root/'Hub.exe'));editor=self.root/'Unity/Editor/Unity.exe';editor.parent.mkdir(parents=True);editor.write_bytes(b'editor fixture');calls=[]
        def process(argv,log,**_):
            calls.append(list(map(str,argv)));log.parent.mkdir(parents=True,exist_ok=True)
            if 'help' in argv:log.write_text('install editors install-modules')
            elif 'install-modules' in argv:
                for relative in ['NDK/source.properties','SDK/platform-tools/adb','OpenJDK/bin/java']:
                    f=editor.parent/'Data/PlaybackEngines/AndroidPlayer'/relative;f.parent.mkdir(parents=True,exist_ok=True);f.write_text('fixture')
                log.write_text('installed')
            else:log.write_text('2021.3.5f1')
        supervisor=mock.Mock();supervisor.run.side_effect=process
        rows=[{'path':str(editor),'version':'2021.3.5f1','androidSupport':False}]
        probe=self.store.session_dir(saved['session'])/'probe.log';probe.write_text(unity_setup.PROBE_MARKER)
        with mock.patch.object(unity_setup.discovery,'unity_paths',return_value=(rows,[])),mock.patch.object(unity_setup,'_wait'),mock.patch.object(unity_setup,'_probe',return_value=probe) as checked:
            outputs,details=wizard.Engine(self.store).stage_unity(saved,supervisor)
        self.assertIn('install-modules',calls[1]);self.assertNotIn('install',calls[1]);self.assertTrue(details['licenseVerified']);checked.assert_called_once();self.assertTrue(outputs[0].is_file())

class HardDeathStateTests(Fixture):
    def test_running_state_without_kernel_owner_becomes_retryable(self):
        saved=self.plan();calls=[]
        wizard.Engine(self.store,actions=self.actions(calls,lambda stage,_:stage=='build')).run(saved['session'])
        value=self.store.load(saved['session']);value['status']='running';value['stages'][5]['status']='running';self.store.save(value)
        resumed=state.Store(self.store.root).load(saved['session'])
        self.assertEqual(resumed['status'],'interrupted');self.assertEqual(resumed['needsActions'][0]['code'],'interrupted')
        final=wizard.Engine(self.store,actions=self.actions(calls)).run(saved['session'])
        self.assertEqual(final['status'],'complete');self.assertEqual(calls.count('tools'),1)
    def test_active_external_kernel_owner_remains_protected(self):
        saved=self.plan();saved['status']='running';saved['stages'][0]['status']='running';self.store.save(saved)
        script='import sys,time;from pathlib import Path;sys.path.insert(0,sys.argv[1]);from state import file_lock;\nwith file_lock(Path(sys.argv[2])):\n print("locked",flush=True);time.sleep(30)'
        process=subprocess.Popen([sys.executable,'-I','-B','-c',script,str(ROOT/'tools/quest-wizard'),str(self.store.root/'run.lock')],stdout=subprocess.PIPE,text=True)
        try:
            self.assertEqual(process.stdout.readline().strip(),'locked')
            self.assertEqual(self.store.load(saved['session'])['status'],'running')
            with self.assertRaises(state.WizardError):self.store.amend(saved['session'],dict(saved['choices'],install=False))
            process.kill();process.wait()
            self.assertEqual(self.store.load(saved['session'])['status'],'interrupted')
        finally:
            if process.poll() is None:process.kill();process.wait()
            process.stdout.close()

class ArtworkHttpTests(Fixture):
    def setUp(self):
        super().setUp();self.ui=self.root/'ui';self.ui.mkdir();(self.ui/'index.html').write_text('owned UI')
        # Adapter contract fixture: its own tests verify actual recovered PNG
        # identity selection. These tests verify backend ownership, HTTP privacy
        # and revalidation rather than reimplementing the image selection rules.
        (self.ui/'artwork.py').write_text('''import json,hashlib
from pathlib import Path
def verified_project_artwork(project,key,limit):
 report=json.loads((project/'quest-campaign-report.json').read_text())
 if report['sourceBuilderFingerprint']!=key:return []
 return [dict(report['image'],projectRoot=str(project))]
def read_artwork(row):
 raw=(Path(row['projectRoot'])/row['assetPath']).read_bytes()
 return raw if hashlib.sha256(raw).hexdigest()==row['sha256'] else None
''')
        self.saved=self.plan();key='a'*64
        self.saved['completed']={name:{'key':'fixture-'+name,'details':{}} for name in ('tools','source','unity','profile')}
        inspect_key=wizard.Engine(self.store).key(self.saved,'inspect')
        manifest=self.store.session_dir(self.saved['session'])/'input.json';state.atomic_json(manifest,{'schema':1,'gameKey':key})
        self.store.publish(self.saved['session'],'inspect',inspect_key,[manifest],{'gameKey':key});self.store.save(self.saved)
        self.project=self.store.root/'build/cache/recovery'/('b'*64)/'project';self.project.mkdir(parents=True)
        self.image=self.project/'Assets/Texture2D/portrait.png';self.image.parent.mkdir(parents=True);self.raw=b'\x89PNG\r\n\x1a\n'+b'fixture bounded original artwork';self.image.write_bytes(self.raw)
        state.atomic_json(self.project/'quest-campaign-report.json',{'schema':1,'sourceBuilderFingerprint':key,'image':{'id':'c'*32,'assetPath':'Assets/Texture2D/portrait.png','sha256':hashlib.sha256(self.raw).hexdigest(),'size':len(self.raw),'altCode':'ownedArtwork'}})
        self.http=server.LocalServer(self.store,self.ui);self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):self.http.shutdown();self.http.close_owned();self.thread.join();super().tearDown()
    def get(self,route,headers=None):
        connection=http.client.HTTPConnection(*self.http.server_address,timeout=3)
        connection.request('GET',route,headers=headers if headers is not None else {'X-Quest-Token':self.http.token,'Origin':self.http.origin})
        response=connection.getresponse();result=(response.status,response.getheader('Content-Type'),response.read());connection.close();return result
    def test_opaque_gallery_and_token_guarded_actual_image(self):
        _,_,raw=self.get('/api/status?session='+self.saved['session']);value=json.loads(raw)
        self.assertTrue(value['state']['capabilities']['artwork']);descriptor=value['state']['artwork'][0]
        self.assertEqual(set(descriptor),{'id','url','altCode'});self.assertNotIn(str(self.project),raw.decode())
        status,mime,raw=self.get(descriptor['url']);self.assertEqual((status,mime,raw),(200,'image/png',self.raw))
        self.assertEqual(self.get(descriptor['url'],headers={})[0],403)
    def test_changed_image_unknown_id_or_invalid_inspect_receipt_refused(self):
        route='/api/artwork?session='+self.saved['session']+'&id='+'c'*32
        self.image.write_bytes(b'changed');self.assertEqual(self.get(route)[0],400)
        self.assertEqual(self.get(route.replace('c'*32,'../private'))[0],400)
        self.store.receipt(self.saved['session'],'inspect').unlink();self.assertEqual(self.get(route)[0],400)
    def test_symlinked_recovery_directory_cannot_expose_outside_image(self):
        if os.name=='nt':self.skipTest('symlink rights vary on Windows')
        outside=self.root/'foreign';outside.mkdir();(outside/'project').mkdir()
        (self.store.root/'build/cache/recovery'/('d'*64)).symlink_to(outside,target_is_directory=True)
        _,_,raw=self.get('/api/status?session='+self.saved['session']);self.assertFalse(json.loads(raw)['state']['capabilities']['artwork'])

class SourceHistoryTests(Fixture):
    def test_remote_source_is_shallow_and_explicit_commit_fetch_is_bounded(self):
        chosen='e'*40;saved=self.plan(sourceCommit=chosen);calls=[]
        checkout=self.store.root/'source'/saved['session'];(checkout/'.git').mkdir(parents=True)
        state.atomic_json(self.store.session_dir(saved['session'])/'source-attempt.json',{'schema':1,'session':saved['session'],'origin':provision.LOCK['source']['url']})
        def run(argv,log,**_):
            args=list(map(str,argv));calls.append(args);log.parent.mkdir(parents=True,exist_ok=True);log.write_text('')
            if 'clone' in args:
                checkout=Path(args[-1]);(checkout/'.git').mkdir(parents=True);(checkout/'source.py').write_text('selected immutable source')
            elif 'rev-parse' in args:raise state.WizardError('child_failed','clone interrupted before HEAD')
            elif 'cat-file' in args:raise state.WizardError('child_failed','not present in shallow ref')
            elif '-c' in args and 'import sys;' in args[args.index('-c')+1]:
                record=Path(args[-1]);checkout=Path(args[-2]);state.atomic_json(record,{'schema':1,'files':[{'path':'source.py','sha256':state.digest(checkout/'source.py'),'size':(checkout/'source.py').stat().st_size}],'commit':chosen,'dirty':False})
        supervisor=mock.Mock();supervisor.run.side_effect=run
        details={'git':'portable-git','dotnet8':'portable-net8','dotnet10':'portable-net10'}
        derived=self.root/'dependencies.json';state.atomic_json(derived,{'schema':1})
        with mock.patch.object(provision,'derive_runtime_dependencies',return_value=derived):
            _,proof=provision.source_checkout(self.store,saved['session'],saved['choices'],details,supervisor,self.root/'release-archive')
        clone=next(row for row in calls if 'clone' in row);self.assertEqual(clone[clone.index('--depth')+1],'1')
        fetch=next(row for row in calls if 'fetch' in row);self.assertEqual(fetch[-1],chosen);self.assertEqual(fetch[fetch.index('--depth')+1],'1')
        self.assertEqual(proof['commit'],chosen)


class ExternalRunnerTests(Fixture):
    def test_rejected_run_cannot_overwrite_another_servers_live_session(self):
        saved=self.plan();ui=self.root/'ui';ui.mkdir();(ui/'index.html').write_text('UI')
        engine=mock.Mock();engine.run.side_effect=state.WizardError('already_running','other owner')
        http=server.LocalServer(self.store,ui,engine_factory=lambda _:engine)
        try:
            with state.file_lock(self.store.root/'run.lock'):
                saved['status']='running';self.store.save(saved)
                before=(self.store.session_dir(saved['session'])/'state.json').read_bytes()
                http.run_session(saved['session']);http.jobs[saved['session']].join(timeout=3)
                self.assertFalse(http.jobs[saved['session']].is_alive())
                self.assertEqual((self.store.session_dir(saved['session'])/'state.json').read_bytes(),before)
                self.assertEqual(http.job_errors[saved['session']]['code'],'already_running')
        finally:http.close_owned()

if __name__=='__main__':unittest.main()
