import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {unityImportView,buildOverviewView,stageProgress} from '../model.mjs';
import {translate} from '../i18n.mjs';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));

test('import observations show real completed operations and no fabricated denominator',()=>{
  const progress={phase:'unity-asset-import',done:521,total:null,unit:'assets',stagePercent:71.3,
    unityImport:{phase:'unity-asset-import',done:521,total:null,detail:'Character.asset',startedAt:100,updatedAt:140}};
  assert.deepEqual(unityImportView(progress,160),{done:521,total:null,detail:'Character.asset',elapsedSeconds:60,percent:null,status:'running'});
  assert.equal(stageProgress({status:'running',progress}).percent,71.3);
  assert.equal(unityImportView({...progress.unityImport,status:'complete'},500).elapsedSeconds,40,'closed import activity does not keep counting wall time');
  assert.equal(unityImportView({...progress.unityImport,total:520},160).total,null,'completed actions cannot exceed an alleged denominator');
  assert.equal(unityImportView({...progress.unityImport,done:2**53,total:null},160).done,null);
  assert.equal(unityImportView({...progress.unityImport,done:true},160).done,null);
  assert.equal(unityImportView({...progress.unityImport,detail:'x'.repeat(4000)},160).detail.length,1024);
  assert.equal(unityImportView({phase:'bee-actions:run:4:1',done:3,total:4}),null);
  for(const language of ['de','en'])for(const key of ['counterImportsCompleted','counterImports','importTotalUnknown','importElapsed','importElapsedRecorded','phase_unity-import-activity'])assert.notEqual(translate(language,key),key);
});

test('expanded Unity group retains count-only import detail separately from API binding',()=>{
  const source={schema:1,active:'unity-import',groups:[{id:'code',operations:[{id:'weave',closed:true,status:'complete',percent:100}]},
    {id:'import',operations:[{id:'unity-import',closed:false,status:'running',percent:0,
      import:{phase:'unity-asset-import',done:521,total:null,detail:'Character.asset',status:'running'}},
      {id:'package-api',closed:false,status:'pending',percent:0},{id:'unity-validation',closed:false,status:'pending',percent:0}]}]};
  const view=buildOverviewView({stages:[{id:'build',progress:{buildOverview:source}}]});
  assert.equal(view.groups[1].operations[0].import.done,521);
  assert.equal(view.groups[1].operations[0].import.total,null);
  assert.equal(view.groups[1].operations[1].status,'pending');
  assert.equal(view.done,1);
  assert.equal(view.total,4);
});

test('content packaging exposes all four actual byte passes, not repeated one-task plans',()=>{
  const passes=['native-content-source-hash','native-content-native-hash','native-content-write','native-content-final-hash'].map((id,index)=>({id,closed:index<2,status:index<2?'complete':index===2?'running':'pending',percent:index<2?100:index===2?25:0,
    counter:index===2?{phase:id,done:25,total:100,unit:'bytes'}:undefined}));
  const raw={schema:1,active:'content-bank',groups:[{id:'export',operations:[{id:'content-bank',closed:false,status:'running',percent:78,pack:{passes,active:'native-content-write'}}]}]};
  const view=buildOverviewView({stages:[{id:'build',progress:{buildOverview:raw}}]});
  assert.equal(view.groups[0].operations[0].pack.done,2);
  assert.equal(view.groups[0].operations[0].pack.total,4);
  assert.deepEqual(view.groups[0].operations[0].pack.passes[2].counter,{phase:'native-content-write',done:25,total:100,unit:'bytes'});
  const duplicate={...raw,groups:[{id:'export',operations:[{id:'content-bank',closed:false,status:'running',percent:78,pack:{passes:[passes[0],passes[0],passes[2],passes[3]]}}]}]};
  assert.equal(buildOverviewView({stages:[{id:'build',progress:{buildOverview:duplicate}}]}).groups[0].operations[0].pack,null);
  for(const language of ['de','en'])for(const row of passes)assert.notEqual(translate(language,'phase_'+row.id),'phase_'+row.id);
});

test('API binding exposes the three actual assembly/publication passes',()=>{
  const passes=['package-api-bind','package-api-output','package-api-publish'].map((id,index)=>({id,closed:index===0,status:index===0?'complete':index===1?'running':'pending',percent:index===0?100:0}));
  const raw={schema:1,active:'package-api',groups:[{id:'import',operations:[{id:'package-api',closed:false,status:'running',percent:65,api:{passes,active:'package-api-output'}}]}]};
  const api=buildOverviewView({stages:[{id:'build',progress:{buildOverview:raw}}]}).groups[0].operations[0].api;
  assert.equal(api.done,1);assert.equal(api.total,3);assert.equal(api.active,'package-api-output');
  for(const language of ['de','en'])for(const key of [...passes.map(row=>'phase_'+row.id),'packageApiSummary','counterAssemblies','phase_unity-bee-activity'])assert.notEqual(translate(language,key),key);
});

const snapshots=String.raw`
import copy,json,sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(sys.argv[1])/'tools/quest-wizard'))
from state import Store
import state
import wizard
state.PROGRESS_INTERVAL=0
with tempfile.TemporaryDirectory() as temp:
    store=Store(Path(temp)/'owned')
    session=store.create(wizard.choices({'gameRoot':str(Path(temp)/'Game')}))['session']
    store.begin_stage(session,'build','same-input')
    store.operation(session,'build','unity-import')
    result={}
    def snapshot(name):
        row=next(row for row in store.load(session)['stages'] if row['id']=='build')
        result[name]=copy.deepcopy(row)
    store.progress(session,'build','unity-asset-import',521,None,'assets','Character.asset',operation='unity-import')
    snapshot('first')
    store.progress(session,'build','unity-import-activity',524,None,'assets','Refreshing imported assets',operation='unity-import')
    snapshot('activity')
    store.progress(session,'build','starting')
    store.operation(session,'build','unity-import')
    store.progress(session,'build','unity-asset-import',1,4,'assets','Current measured Unity task',operation='unity-import')
    snapshot('measured')
    store.progress(session,'build','unity-asset-import',2,4,'assets','Current measured Unity task',operation='unity-import')
    snapshot('advanced')
    store.operation(session,'build','unity-import',complete=True)
    store.operation(session,'build','package-api')
    snapshot('api')
    store.progress(session,'build','package-api-bind',20,20,'assemblies','Actual API assembly binding',operation='package-api',status='complete')
    store.progress(session,'build','package-api-output',10,20,'assemblies','Actual API assemblies written',operation='package-api',status='progress')
    snapshot('apiOutput')
    store.progress(session,'build','package-api-output',20,20,'assemblies','Actual API assemblies written',operation='package-api',status='complete')
    store.progress(session,'build','package-api-publish',20,20,'files','Actual API publication',operation='package-api',status='complete')
    snapshot('apiFinished')
    store.operation(session,'build','package-api',complete=True)
    store.operation(session,'build','unity-validation',complete=True)
    store.operation(session,'build','content-bank')
    for phase in ('native-content-source-hash','native-content-native-hash'):
        store.progress(session,'build',phase,100,100,'bytes',operation='content-bank',status='complete')
    store.progress(session,'build','native-content-write',25,100,'bytes','Real package bytes written',operation='content-bank',status='progress')
    snapshot('pack')
    store.progress(session,'build','native-content-write',75,100,'bytes','Real package bytes written',operation='content-bank',status='progress')
    snapshot('packAdvanced')
    print(json.dumps(result))
`;

test('browser reports long initial import counts, action, elapsed, real task percentages and correct heading',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B','-c',snapshots,resolve(root,'../..')],{encoding:'utf8'}));
    let mode='first',client;
    const state=()=>({session:'unity-import-session',status:'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},needsActions:[],artwork:[],
      stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:'complete',percent:100}})});
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:'unity-import-session',games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
        else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
        else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    try{
      client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      await client.wait("document.getElementById('substep-count').textContent==='521 Importaufgaben abgeschlossen'");
      const before=await client.evaluate("document.getElementById('progress-count').textContent");
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/521 Importaufgaben abgeschlossen.*Importereignisse des gespeicherten Laufs.*Aktueller Import seit.*Character.asset/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-completed').textContent"),/0 \/ 1|1 ausstehend/,'unknown importer plans do not become an invented one-task count');
      assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=package-api]').classList.contains('pending')"),true);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=package-api]').closest('details').dataset.group"),'import');
      assert.equal(await client.evaluate("document.querySelector('[data-import-owner=unity-import]').textContent.includes('521 Importaufgaben abgeschlossen')"),true);
      await client.picture('unity-initial-import-count-de');
      mode='activity';
      await client.wait("document.getElementById('substep-count').textContent==='524 Importaufgaben abgeschlossen'");
      assert.equal(await client.evaluate("document.getElementById('progress-count').textContent"),before);
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Refreshing imported assets/);
      mode='measured';await client.wait("document.getElementById('substep-count').textContent==='25 %'");
      const measured=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='advanced';await client.wait("document.getElementById('substep-count').textContent==='50 %'");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>measured);
      mode='api';await client.wait("document.getElementById('substep-label').textContent==='Teilabschnitt: Unity-API anbinden'");
      assert.equal(await client.evaluate("document.querySelector('[data-operation=unity-import]').classList.contains('complete')"),true);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=package-api]').classList.contains('running')"),true);
      mode='apiOutput';await client.wait("document.getElementById('substep-label').textContent==='Teilabschnitt: Angepasste Assemblies schreiben'");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'50 %');
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/1 \/ 3 Teilaufgaben abgeschlossen.*2 ausstehend/);
      assert.equal(await client.evaluate("document.querySelector('[data-plan=package-api] summary').textContent"),'Unity-API: 1 / 3 Teilaufgaben abgeschlossen');
      const apiPercent=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='apiFinished';await client.wait("document.querySelector('[data-plan=package-api] summary').textContent==='Unity-API: 3 / 3 Teilaufgaben abgeschlossen'");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>apiPercent);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=package-api]').classList.contains('running')"),true,'last child cannot close its API owner');
      mode='pack';await client.wait("document.getElementById('substep-count').textContent==='25 %'");
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/2 \/ 4 Teilaufgaben abgeschlossen.*2 ausstehend/);
      assert.equal(await client.evaluate("document.querySelector('[data-plan=content-pack] summary').textContent"),'Inhaltspaket: 2 / 4 Teilaufgaben abgeschlossen');
      const packed=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='packAdvanced';await client.wait("document.getElementById('substep-count').textContent==='75 %'");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>packed);
      await client.picture('content-package-byte-progress-de');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });
