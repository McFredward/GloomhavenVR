"""Store actions are guided prerequisites; launch success never grants ownership."""
import http.client
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import store_launcher
import server
import wizard
from failures import tool_failure
from state import Store, STAGES, WizardError


class LauncherTests(unittest.TestCase):
    def test_exact_current_failure_becomes_guided_action_but_unrelated_errors_do_not(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); log = root / 'inspect.log'
            for marker in ('Start Steam with the account owning Gloomhaven before building its Quest copy.',
                           '[launcher-action:steam] Wrong selected Steam account.'):
                log.write_text('Quest builder: ' + marker)
                value = tool_failure(root, 'inspect', log, time.time()-2, 'python', 1, workflow_mode='update-mod')
                self.assertEqual(value.code, 'store_launcher_required')
                self.assertTrue(value.parameters['completedWorkRetained'])
                self.assertIn(marker, value.parameters['cause'])
            self.assertIsNone(store_launcher.failure({'failureStage':'install','cause':'[launcher-action:steam]'}))
            self.assertIsNone(store_launcher.failure({'failureStage':'build','cause':'Owned card pixel spool exceeds its compressed byte bound.'}))

    def test_provider_whitelist_precedes_loading_and_browser_never_receives_commands(self):
        helper = SimpleNamespace(launch=lambda p:{'provider':p,'launched':True,'executable':'private/path','arguments':['private']}, LauncherError=RuntimeError)
        with mock.patch.object(store_launcher, 'module', return_value=helper) as loading:
            for bad in ('Steam', '../steam', None, {}, 'steam;anything'):
                with self.assertRaises(WizardError): store_launcher.launch(ROOT, bad)
            loading.assert_not_called()
            for provider in store_launcher.LABELS:
                value = store_launcher.launch(ROOT, provider)
                self.assertEqual(value['provider'], provider)
                self.assertEqual(value['event'], 'launcher-opened')
                self.assertNotIn('private', json.dumps(value))

    def test_real_blocked_run_reuses_closed_predecessors_after_user_action(self):
        with tempfile.TemporaryDirectory() as temp:
            store = Store(Path(temp)); state=store.create(wizard.choices({'gameRoot':'fixture-game'})); calls=[]
            missing = True
            def action(stage):
                def run(saved, _supervisor):
                    calls.append(stage)
                    if stage=='inspect' and missing: raise store_launcher.required('steam',cause='Steam client is unavailable.')
                    path=store.session_dir(saved['session'])/(stage+'.output');path.write_text(stage)
                    return [path], {'fixture':stage}
                return run
            engine=wizard.Engine(store,actions={stage:action(stage) for stage in STAGES})
            first=engine.run(state['session'])
            self.assertEqual(first['status'],'blocked')
            self.assertEqual(first['needsActions'][0]['code'],'store_launcher_required')
            for stage in ('tools','source','unity','profile'): self.assertEqual(first['stages'][STAGES.index(stage)]['status'],'complete')
            missing=False
            second=engine.run(state['session'])
            self.assertEqual(second['status'],'complete')
            self.assertEqual(second['needsActions'],[])
            for stage in ('tools','source','unity','profile'): self.assertEqual(calls.count(stage),1)
            self.assertEqual(calls.count('inspect'),2)
            self.assertEqual(calls.count('build'),1)

    def test_profile_prerequisite_tries_matching_launcher_once_without_granting_identity(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(Path(temp));engine=wizard.Engine(store)
            for provider in store_launcher.LABELS:
                state=store.create(wizard.choices({'gameRoot':'fixture','provider':provider}))
                with mock.patch.object(store_launcher,'launch',return_value={'provider':provider}) as launch:
                    with self.assertRaises(WizardError) as failure:engine.profile_launcher_required(state,'Identity missing')
                    launch.assert_called_once_with(ROOT,provider)
                self.assertEqual(failure.exception.code,'store_launcher_required')
                self.assertNotIn('profile',state.get('completed',{}))


class LauncherHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.store=Store(Path(self.temp.name))
        self.http=server.LocalServer(self.store,ROOT/'tools/quest-wizard-ui',discover=lambda *_:{'schema':1,'event':'discovery','capabilities':{}})
        self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):
        self.http.shutdown();self.http.close_owned();self.thread.join(5);self.temp.cleanup()
    def request(self,body,*,token=True,origin=True,method='POST'):
        connection=http.client.HTTPConnection(*self.http.server_address,timeout=3)
        headers={'X-Quest-Token':self.http.token if token else 'invalid','Content-Type':'application/json'}
        if origin:headers['Origin']=self.http.origin
        connection.request(method,'/api/launcher/open',json.dumps(body),headers)
        reply=connection.getresponse();value=json.loads(reply.read());connection.close();return reply.status,value
    def test_exact_authenticated_allowlisted_provider_only_and_no_run_mutation(self):
        calls=[]
        helper=SimpleNamespace(launch=lambda p:(calls.append(p) or {'provider':p,'launched':True}),LauncherError=RuntimeError)
        saved=self.store.create(wizard.choices({'gameRoot':'fixture'}));before=self.store.load(saved['session'])
        with mock.patch.object(store_launcher,'module',return_value=helper):
            for body in ({'provider':'steam','command':'shell'},{'provider':'steam','path':'x'}, {'provider':[]},{'provider':'bad'},{}):
                self.assertEqual(self.request(body)[0],400)
            self.assertEqual(self.request({'provider':'steam'},token=False)[0],403)
            self.assertEqual(self.request({'provider':'steam'},origin=False)[0],403)
            self.assertEqual(self.request({'provider':'steam'},method='GET')[0],400)
            self.assertEqual(calls,[])
            for provider in store_launcher.LABELS:
                status,value=self.request({'provider':provider})
                self.assertEqual(status,200);self.assertEqual(value['provider'],provider)
            self.assertEqual(calls,['steam','epic','gog'])
        self.assertEqual(self.store.load(saved['session']),before)

if __name__=='__main__':unittest.main()
