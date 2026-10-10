import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {unityWorkView,unityImportView,activeWorkView,buildOverviewView} from '../model.mjs';
import {translate} from '../i18n.mjs';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
test('finite Unity phases retain scoped counts and reject duplicate or unbounded plans',()=>{
  const phases=['assets','method','result'].map((id,index)=>({id,status:index===0?'complete':'running',closed:index===0,percent:index===0?100:0,counter:{done:index===0?1800:0,total:index===0?2000:1,unit:index===0?'assets':'tasks'},
    ...(id==='method'?{parts:[{id:'native',status:'running',closed:false,percent:50,counter:{phase:'bee-actions:compile:200:1',done:100,total:200,unit:'actions'}}]}:{})}));
  const view=unityWorkView({phases,active:'method',percent:40});
  assert.equal(view.done,1);assert.equal(view.total,3);assert.equal(view.phases[1].parts[0].counter.total,200);
  assert.equal(view.phases[1].closed,false);
  assert.equal(unityWorkView({phases:[phases[0],phases[0],phases[2]]}),null);
  assert.equal(unityWorkView({phases:[...phases,{id:'unplanned'}]}),null);
  assert.equal(activeWorkView({activeWork:{done:0,total:1,operation:'player',unity:{done:1,total:3,active:'method'}}}).unity.done,1);
  const coverage=unityImportView({phase:'unity-work-plan',done:500,total:1000,detail:'[asset-coverage] Known project/package assets'},150);
  assert.equal(coverage.percent,50);assert.equal(coverage.scope,'coverage');assert.doesNotMatch(coverage.detail,/\[asset-coverage\]/);
  const native=unityImportView({phase:'unity-asset-import',done:2,total:10,detail:'[unity-native-total] Importing 2 assets of 10'},150);
  assert.equal(native.scope,'native');assert.equal(native.percent,20);
  for(const language of ['en','de'])for(const key of ['unityWorkSummary','unityWorkScope','unityWork_assets','unityWork_method','unityWork_result','unityWork_native','unityWork_packaging','unityWork_il2cpp','phase_unity-work-plan','importScopeCoverage','importScopeNative','counterAssetCoverage'])assert.notEqual(translate(language,key),key);
});

const snapshots=String.raw`
import copy,json,sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(sys.argv[1])/'tools/quest-wizard'))
from state import Store
import state,wizard
state.PROGRESS_INTERVAL=0
with tempfile.TemporaryDirectory() as temp:
    store=Store(Path(temp)/'owned');session=store.create(wizard.choices({'gameRoot':str(Path(temp)/'Game')}))['session']
    store.begin_stage(session,'build','same-input');store.operation(session,'build','player')
    result={}
    def report(phase,done,total,unit='steps',detail='Actual source-backed progress',status='progress'):
        store.progress(session,'build',phase,done,total,unit,detail,operation='player',status=status)
    def snapshot(name): result[name]=copy.deepcopy(next(row for row in store.load(session)['stages'] if row['id']=='build'))
    report('unity-work-invocation',0,1,'invocations',status='start')
    report('unity-work-plan',500,1000,'assets','[asset-coverage] Known project/package assets',status='start');snapshot('census')
    report('unity-asset-import',800,1000,'assets','[asset-coverage] Assets/GameObject/Character.prefab');snapshot('census-advance')
    report('unity-work-stage:method',0,1,status='start')
    report('unity-il2cpp',None,None,None,'[indivisible-task] Invoking il2cpp');snapshot('codegen')
    report('bee-actions:compile:200:1',100,200,'actions');snapshot('native')
    report('bee-actions:compile:200:1',200,200,'actions',status='complete')
    report('bee-actions:link:300:2',0,300,'actions');snapshot('next-graph')
    report('unity-gradle-tasks',20,40,'tasks');snapshot('gradle')
    report('unity-work-stage:method',1,1,status='complete');snapshot('method')
    report('unity-work-invocation',1,1,'invocations',status='complete');snapshot('returned')
    print(json.dumps(result))
`;

test('browser presents asset census, native graphs and Gradle inside one stable Unity plan',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B','-c',snapshots,resolve(root,'../..')],{encoding:'utf8'}));
    let mode='census',client;
    const state=()=>({session:'unity-work-session',status:'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},needsActions:[],artwork:[],stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:'complete',percent:100}})});
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:'unity-work-session',games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
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
      await client.wait("document.getElementById('progress-detail').textContent.includes('500 / 1.000 Assets importiert oder wiederverwendet')");
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/0 \/ 3 Abschnitte abgeschlossen/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/Unity meldet keine Gesamtzahl|\[asset-coverage\]/);
      const before=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='census-advance';await client.wait("document.getElementById('progress-detail').textContent.includes('800 / 1.000 Assets')");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>before);
      mode='codegen';await client.wait("document.getElementById('substep-label').textContent.includes('Android-Spielcode kompilieren')");
      assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'0');
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Einzelaufgabe/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/\[indivisible-task\]/);
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/1 \/ 3 Abschnitte abgeschlossen/);
      mode='native';await client.wait("document.querySelector('[data-plan=unity-work-player]').textContent.includes('100 / 200 Aufgaben')");
      mode='next-graph';await client.wait("document.querySelector('[data-plan=unity-work-player]').textContent.includes('0 / 300 Aufgaben')");
      const native=await client.evaluate("Number(document.querySelector('[data-plan=unity-work-player] > progress').value)");
      assert.ok(native>15&&native<100);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=player]').classList.contains('running')"),true);
      mode='gradle';await client.wait("document.getElementById('progress-detail').textContent.includes('20 / 40 Schritte')");
      assert.ok(await client.evaluate("Number(document.querySelector('[data-plan=unity-work-player] > progress').value)")>native);
      mode='method';await client.wait("document.querySelector('[data-plan=unity-work-player] summary').textContent.includes('2 / 3 Abschnitte')");
      mode='returned';await client.wait("document.querySelector('[data-plan=unity-work-player] summary').textContent.includes('3 / 3 Abschnitte')");
      assert.equal(await client.evaluate("document.querySelector('[data-operation=player]').classList.contains('running')"),true,'command return remains separate from qualified Player publication');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
      await client.picture('unity-counted-work-de');
    }finally{await client?.close();await new Promise(resolve=>server.close(resolve));}
  });
