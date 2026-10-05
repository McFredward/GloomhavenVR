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
    def test_api_cannot_dump_arbitrary_file_or_command(self):
        code,value=self.request('/api/browse',{'kind':'game','command':'malicious'});self.assertEqual(code,400)
        code,value=self.request('/api/artwork?session='+self.plan()['session']+'&id=../secret');self.assertEqual(code,400)


if __name__=='__main__':unittest.main()

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
