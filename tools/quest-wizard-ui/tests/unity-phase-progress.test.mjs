import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {unityTaskView,buildOverviewView} from '../model.mjs';
import {translate} from '../i18n.mjs';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
test('measured Editor work shows actual count and elapsed without unknown graph estimates',()=>{
  const row={phase:'unity-validation-tasks',done:7,total:17,unit:'steps',detail:'Validate startup Sprite geometry',startedAt:100,updatedAt:140};
  const view=unityTaskView(row,160);
  assert.equal(view.done,7);assert.equal(view.total,17);assert.equal(view.elapsedSeconds,60);assert.equal(view.percent,100*7/17);
  assert.equal(unityTaskView({...row,status:'failed'},500).elapsedSeconds,40);
  assert.equal(unityTaskView({...row,done:true}),null);
  assert.equal(unityTaskView({...row,done:2**53}),null);
  assert.equal(unityTaskView({...row,total:6}).total,null);
  assert.equal(unityTaskView({...row,detail:'x'.repeat(2000)}).detail.length,1024);
  const gradle=unityTaskView({...row,phase:'unity-gradle-tasks',unit:'tasks',done:15,total:null});
  assert.equal(gradle.percent,null);assert.equal(gradle.total,null);
});

test('validation plan and current inner work remain distinct from parent completion',()=>{
  const passes=[{id:'unity-configuration',status:'complete',closed:true,percent:100},{id:'unity-validation-tasks',status:'running',closed:false,percent:100*7/17,counter:{phase:'unity-validation-tasks',done:7,total:17,unit:'steps'}}];
  const raw={schema:1,active:'unity-validation',groups:[{id:'import',operations:[{id:'unity-validation',closed:false,status:'running',percent:47,validation:{passes,active:'unity-validation-tasks'},unityTask:{phase:'unity-validation-sprites',done:8,total:20,unit:'sprites',detail:'DLC_Promo_JawsOfTheLion.asset'}}]}]};
  const row=buildOverviewView({stages:[{id:'build',progress:{buildOverview:raw}}]}).groups[0].operations[0];
  assert.equal(row.validation.done,1);assert.equal(row.validation.total,2);assert.equal(row.unityTask.done,8);assert.equal(row.unityTask.unit,'sprites');assert.equal(row.closed,false);
  for(const language of ['de','en'])for(const phase of ['unity-configuration','unity-validation-tasks','unity-sdk-tasks','unity-original-scenes','unity-validation-sprites','unity-validation-campaign-sprites','unity-addressables-keys','unity-addressables-entries','unity-addressables-shaders','unity-addressables-shader-roots','unity-addressables-build','unity-native-shader-audit','unity-content-post-tasks','unity-content-delivery','unity-player-scenes','unity-player-native-result','unity-player-memory','unity-gradle-tasks'])assert.notEqual(translate(language,'phase_'+phase),'phase_'+phase);
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
    store.begin_stage(session,'build','same-input');store.operation(session,'build','unity-validation')
    result={}
    def snapshot(name): result[name]=copy.deepcopy(next(row for row in store.load(session)['stages'] if row['id']=='build'))
    store.progress(session,'build','unity-configuration',6,6,'steps','Configure original mobile Android settings',operation='unity-validation',status='complete')
    store.progress(session,'build','unity-validation-tasks',7,17,'steps','Validate startup Sprite geometry',operation='unity-validation');snapshot('validation')
    store.progress(session,'build','unity-validation-sprites',2,20,'sprites','Assets/Sprite/Original.asset',operation='unity-validation');snapshot('sprites-start')
    store.progress(session,'build','unity-validation-sprites',12,20,'sprites','Assets/Sprite/DLC_Promo_JawsOfTheLion.asset',operation='unity-validation');snapshot('sprites-advance')
    store.progress(session,'build','unity-validation-tasks',9,17,'steps','Validate original video assets',operation='unity-validation');snapshot('advanced')
    store.operation(session,'build','unity-validation',complete=True);store.operation(session,'build','content-bank')
    store.progress(session,'build','unity-addressables-entries',1200,2400,'assets','Assets/GameObject/Original.prefab',operation='content-bank');snapshot('catalog')
    store.operation(session,'build','content-bank',complete=True);store.operation(session,'build','player')
    store.progress(session,'build','unity-content-delivery',128*1048576,256*1048576,'bytes','Copy complete Campaign bank',operation='player');snapshot('copy')
    store.progress(session,'build','unity-gradle-tasks',15,None,'tasks','> Task :launcher:compileDebugJavaWithJavac',operation='player');snapshot('gradle')
    print(json.dumps(result))
`;

test('browser displays validation/copy/catalog/Gradle live counters and phase ownership',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B','-c',snapshots,resolve(root,'../..')],{encoding:'utf8'}));
    let mode='validation',client;
    const state=()=>({session:'unity-phase-session',status:'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},needsActions:[],artwork:[],stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:'complete',percent:100}})});
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:'unity-phase-session',games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
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
      await client.wait("document.getElementById('progress-detail').textContent.includes('7 / 17 Schritte')");
      assert.match(await client.evaluate("document.querySelector('[data-task-owner=unity-validation]').textContent"),/7 \/ 17 Schritte.*Validate startup Sprite geometry/);
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/7 \/ 17/);
      const before=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='sprites-start';await client.wait("document.querySelector('[data-task-owner=unity-validation]').textContent.includes('2 / 20 Bildassets')");
      const spriteBefore=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='sprites-advance';await client.wait("document.querySelector('[data-task-owner=unity-validation]').textContent.includes('12 / 20 Bildassets')");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>spriteBefore);
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/7 \/ 17/);
      mode='advanced';await client.wait("document.getElementById('progress-detail').textContent.includes('9 / 17 Schritte')");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>before);
      mode='catalog';await client.wait("document.getElementById('substep-label').textContent.includes('Originale Inhaltseinträge vorbereiten')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'50 %');
      mode='copy';await client.wait("document.getElementById('progress-detail').textContent.includes('128 MiB / 256 MiB')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'50 %');
      mode='gradle';await client.wait("document.getElementById('progress-detail').textContent.includes('15 Gradle-Aufgaben gemeldet')");
      assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false);
      assert.match(await client.evaluate("document.querySelector('[data-task-owner=player]').textContent"),/15 Gradle-Aufgaben gemeldet.*compileDebugJavaWithJavac/);
    } finally{await client?.close();await new Promise(resolve=>server.close(resolve));}
  });
