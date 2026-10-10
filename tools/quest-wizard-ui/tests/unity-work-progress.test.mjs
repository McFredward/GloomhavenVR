import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {unityWorkView,unityImportView,activeWorkView,buildOverviewView,stageProgress} from '../model.mjs';
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
  for(const language of ['en','de'])for(const key of ['unityWorkSummary','unityWorkScope','unityWork_assets','unityWork_method','unityWork_result','unityWork_native','unityWork_packaging','unityWork_il2cpp','unityWork_audit','phase_unity-native-shader-audit-read','phase_unity-native-shader-audit-objects','counterBundles','phase_unity-work-plan','importScopeCoverage','importScopeNative','counterAssetCoverage'])assert.notEqual(translate(language,key),key);
});

test('completed content parts keep their100% proof while a later audit is still running',()=>{
  const row={phases:[{id:'assets',closed:true,status:'reused',percent:100},{id:'method',closed:false,status:'running',percent:75,
    parts:[{id:'shaders',closed:true,status:'complete',percent:100,counter:{phase:'unity-addressables-shader-roots',done:688,total:688,unit:'shaders'}},
      {id:'audit',closed:false,status:'running',percent:15,counter:{phase:'unity-native-shader-audit-read',done:64,total:128,unit:'bytes'}}]},
    {id:'result',closed:false,status:'pending',percent:0}],active:'method',percent:75};
  const view=unityWorkView(row);
  assert.equal(view.phases[1].parts[0].percent,100);
  assert.equal(view.phases[1].parts[0].status,'complete');
  assert.equal(view.phases[1].parts[1].id,'audit');
  assert.equal(view.phases[1].parts[1].closed,false);
  assert.equal(view.done,1);
});

test('completed compiler hides stale last-pass counts while failed compiler retains them',()=>{
  const observed={phase:'unity-shader-compile',scope:'pass',done:90,total:100,unit:'variants',percent:100,status:'complete'};
  const overview=compiler=>buildOverviewView({stages:[{id:'build',progress:{buildOverview:{schema:1,groups:[{id:'export',operations:[{id:'content-bank',closed:false,status:'running',percent:45,compiler}]}]}}}]}).groups[0].operations[0].compiler;
  const complete=overview(observed);assert.equal(complete.counter,null);assert.equal(complete.percent,100);
  const failed=overview({...observed,status:'failed',percent:90});assert.equal(failed.counter.done,90);assert.equal(failed.counter.total,100);assert.equal(failed.percent,90);
});

test('source-qualified return units prevent live N/Nfrom masquerading as whole phase completion',()=>{
  const progress=(phase,done,total,status='progress')=>stageProgress({status:'running',progress:{phase,done,total,unit:'variants',percent:100*done/total,operationStatus:status,
    activeWork:{operation:'player'},buildOverview:{active:'player',groups:[{operations:[{id:'player',unityWork:{phases:[{id:'method',parts:[{id:'scenes',percent:100*14/15,closed:false},{id:'shaders',percent:80,closed:false}]}]}}]}]}}});
  assert.equal(progress('unity-player-scenes',14,14).phasePercent,100*14/15);
  assert.equal(progress('unity-shader-compile',6,6).phasePercent,100*6/7);
  const completedPass=progress('unity-shader-compile',6,6,'complete');
  assert.equal(completedPass.displayPhase,'unity-player-shader-work');assert.equal(completedPass.phasePercent,80);
  const close=1-Number.EPSILON;assert.ok(progress('unity-shader-compile',999998,999999).phasePercent<100*close);
  for(const language of ['en','de'])for(const key of ['unityNativeGraphScope','unityNativeLocalScope','unityShaderSourceScope','unitySceneReturnScope','shaderPassReturnScope','shaderWholeReturnScope','phase_unity-player-shader-work','phase_unity-player-shaders','counterSceneEntries'])assert.notEqual(translate(language,key),key);
});

test('terminal children leave the active bar while their proof cards stay complete',()=>{
  const measured=(phase,parts,result={id:'result',closed:false,percent:0,counter:{done:0,total:1,unit:'tasks'}},status='complete')=>stageProgress({status:'running',progress:{phase,done:100,total:100,unit:'tasks',percent:100,operationStatus:status,
    activeWork:{operation:'player'},buildOverview:{active:'player',groups:[{operations:[{id:'player',closed:false,unityWork:{phases:[{id:'method',parts},result]}}]}]}}});
  const native={id:'native',closed:true,percent:100,status:'complete'},packaging={id:'packaging',closed:false,percent:0,status:'pending',counter:{done:0,total:1,unit:'tasks'}};
  const nativeDone=measured('unity-native-build-plan',[native,packaging]);
  assert.equal(nativeDone.displayPhase,'unity-work-stage:packaging');assert.equal(nativeDone.phasePercent,0);assert.equal(nativeDone.progressScope,'next-phase');
  const packaged=measured('unity-gradle-tasks',[native,{...packaging,closed:true,percent:100,status:'complete'}]);
  assert.equal(packaged.displayPhase,'unity-player-finalization');assert.equal(packaged.phasePercent,0);assert.equal(packaged.scopeCounter.total,1);
  const computed=measured('unity-compute-kernels',[native,{...packaging,closed:true}],{id:'result',closed:false,percent:50*2/13,counter:{phase:'unity-compute-identities',done:2,total:13,unit:'shaders'}});
  assert.equal(computed.phasePercent,50*2/13);assert.equal(computed.scopeCounter.total,13);
  const returning=measured('unity-work-invocation',[],{id:'result',closed:true,percent:100});
  assert.equal(returning.displayPhase,'unity-owner-publication');assert.equal(returning.phasePercent,0);
  const live=measured('unity-sdk-tasks',[],undefined,'progress');
  assert.equal(live.phasePercent,100*100/101);assert.equal(live.progressScope,'scope-return');
  for(const language of ['en','de'])for(const key of ['nextSubstep','nextPhaseScope','playerFinalizationScope','wholePhaseContinuation','unityScopeReturn','phase_unity-player-finalization','phase_unity-owner-publication'])assert.notEqual(translate(language,key),key);
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
    report('unity-player-scenes',14,14,'scenes','Unity Player scene: Assets/Scenes/Intro.unity');snapshot('scene-entry')
    report('unity-player-shaders',0,101,'tasks','[shader-name-coverage] 100 source names and platform handoff');snapshot('shader-before-partial')
    report('unity-shader-compile',3,6,'variants','[shader-source:unseen] [shader-pass:1555] Known Shader READY');snapshot('shader-partial')
    report('unity-shader-compile',999998,999999,'variants','[shader-pass:1556] variants ready');snapshot('shader-precision')
    report('unity-shader-compile',6,6,'variants','[shader-pass:1557] TextCore variants ready');snapshot('shader-ready')
    report('unity-player-shaders',100,101,'tasks','[shader-name-coverage] 100 known source names +1platform handoff');snapshot('shader-coverage')
    report('unity-shader-compile',6,6,'variants','[shader-pass:1557] TextCore pass finished',status='complete');snapshot('shader-pass')
    report('unity-player-platform-handoff',1,1,status='complete');snapshot('platform')
    report('bee-actions:copy:9233:0',9233,9233,'actions','[bee-scope:staging] [bee-graph:pre-codegen] CopyFiles');snapshot('copy-queue')
    report('unity-il2cpp',None,None,None,'[indivisible-task] Invoking il2cpp');snapshot('codegen')
    report('unity-il2cpp',2,2,'tasks','[bee-scope:il2cpp] IL2CPP_CodeGen succeeded',status='complete');snapshot('codegen-complete')
    report('unity-progress:77:8',0,1,'tasks','Unity: opaque compilation [indivisible-task]');snapshot('native-single')
    report('unity-native-build-plan',100,201,'tasks','[bee-native-plan] reachable nodes and backend result');report('bee-actions:compile:200:1',100,200,'actions');snapshot('native')
    report('bee-actions:compile:200:1',200,200,'actions',status='complete')
    report('bee-actions:link:300:2',0,300,'actions');snapshot('next-graph')
    report('unity-native-build-plan',201,201,'tasks','[bee-native-plan] native graph succeeded',status='complete');snapshot('native-returned')
    report('unity-gradle-tasks',20,40,'tasks','[gradle-graph:fixture-uuid] Gradle: :launcher:packageDebug');snapshot('gradle')
    report('unity-gradle-tasks',40,40,'tasks','[gradle-graph:fixture-uuid] Gradle finished',status='complete');snapshot('gradle-returned')
    report('unity-compute-identities',2,13,'shaders','Compute identity census');
    report('unity-compute-kernels',4,4,'kernels','One dispatch set retained',status='complete');snapshot('kernel-returned')
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
      mode='scene-entry';await client.wait("document.getElementById('progress-detail').textContent.includes('Begonnene Szenen: 14 / 14')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'93,333 %');
      mode='shader-before-partial';await client.wait("document.getElementById('substep-label').textContent.includes('Shader-Aufbereitung insgesamt')");
      const shaderBefore=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='shader-partial';await client.wait("document.getElementById('progress-detail').textContent.includes('Known Shader READY')");
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>shaderBefore);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/shader-source:|shader-pass:/);
      mode='shader-precision';await client.wait("document.getElementById('progress-detail').textContent.includes('999.998 / 999.999 Varianten')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'99,999 %','An unfinished nearly-complete pass cannot round to100');
      mode='shader-ready';await client.wait("document.getElementById('progress-detail').textContent.includes('6 / 6 Varianten')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'85,714 %','The actual pass-return unit is still pending');
      mode='shader-coverage';await client.wait("document.getElementById('substep-label').textContent.includes('Shader-Aufbereitung insgesamt')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'80 %');
      mode='shader-pass';await client.wait("document.querySelector('[data-compiler-owner=player]')?.classList.contains('complete')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'80 %');
      assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Shader-Daten fertigstellen/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/6 \/ 6 Varianten|shader-pass:/);
      assert.match(await client.evaluate("document.querySelector('[data-compiler-owner=player]').textContent"),/Abgeschlossen.*100 %/s);
      assert.doesNotMatch(await client.evaluate("document.querySelector('[data-compiler-owner=player]').textContent"),/Wird erstellt|6 \/ 6/);
      mode='platform';await client.wait("document.querySelector('[data-plan=unity-work-player] [data-operation=scenes]').classList.contains('complete')");
      for(const id of ['scenes','shaders'])assert.match(await client.evaluate("document.querySelector('[data-plan=unity-work-player] [data-operation="+id+"]').textContent"),/Abgeschlossen.*100 %/);
      assert.deepEqual(await client.evaluate("[...document.querySelector('[data-plan=unity-work-player] [data-operation=method] > div > ol').children].map(row=>row.dataset.operation)"),['scenes','shaders','native','packaging']);
      mode='copy-queue';await client.wait("document.getElementById('progress-detail').textContent.includes('Aktuelle Aufgabengruppe: 9.233 / 9.233 Aufgaben')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'0 %','The completed preliminarycopy queue cannot claim native-build100');
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/bee-scope:|bee-graph:/);
      mode='codegen';await client.wait("document.getElementById('progress-detail').textContent.includes('Invoking il2cpp')");
      assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'0');
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Einzelaufgabe/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/\[indivisible-task\]/);
      assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/1 \/ 3 Abschnitte abgeschlossen/);
      mode='codegen-complete';await client.wait("document.querySelector('[data-plan=unity-work-player] [data-operation=native] [data-operation=il2cpp]').classList.contains('complete')");
      assert.match(await client.evaluate("document.querySelector('[data-plan=unity-work-player] [data-operation=native]').textContent"),/Code-Umwandlung.*Abgeschlossen.*100 %/s);
      assert.equal(await client.evaluate("document.querySelector('[data-plan=unity-work-player] [data-operation=native]').classList.contains('running')"),true);
      mode='native-single';await client.wait("document.getElementById('progress-detail').textContent.includes('opaque compilation')");
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Einzelaufgabe/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/\[indivisible-task\]/);
      mode='native';await client.wait("document.querySelector('[data-plan=unity-work-player]').textContent.includes('100 / 200 Aufgaben')");
      mode='next-graph';await client.wait("document.querySelector('[data-plan=unity-work-player]').textContent.includes('0 / 300 Aufgaben')");
      const native=await client.evaluate("Number(document.querySelector('[data-plan=unity-work-player] > progress').value)");
      assert.ok(native>15&&native<100);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=player]').classList.contains('running')"),true);
      mode='native-returned';await client.wait("document.getElementById('substep-label').textContent.includes('Nächster Teilabschnitt: Android-Paket erstellen')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'0 %');
      assert.match(await client.evaluate("document.querySelector('[data-plan=unity-work-player] [data-operation=native]').textContent"),/Abgeschlossen.*100 %/s);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/201 \/ 201/);
      mode='gradle';await client.wait("document.getElementById('progress-detail').textContent.includes('20 / 40 Schritte')");
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Gradle: :launcher:packageDebug/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/gradle-graph|fixture-uuid/);
      assert.ok(await client.evaluate("Number(document.querySelector('[data-plan=unity-work-player] > progress').value)")>native);
      mode='gradle-returned';await client.wait("document.getElementById('substep-label').textContent.includes('Build-Ergebnis und abschließende Nachweise fertigstellen')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'0 %');
      assert.match(await client.evaluate("document.querySelector('[data-plan=unity-work-player] [data-operation=packaging]').textContent"),/Abgeschlossen.*100 %/s);
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/40 \/ 40/);
      mode='kernel-returned';await client.wait("document.getElementById('progress-detail').textContent.includes('2 / 13 Shader')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'7,692 %');
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/4 \/ 4/);
      mode='method';await client.wait("document.querySelector('[data-plan=unity-work-player] summary').textContent.includes('2 / 3 Abschnitte')");
      mode='returned';await client.wait("document.querySelector('[data-plan=unity-work-player] summary').textContent.includes('3 / 3 Abschnitte')");
      assert.equal(await client.evaluate("document.querySelector('[data-operation=player]').classList.contains('running')"),true,'command return remains separate from qualified Player publication');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
      await client.picture('unity-counted-work-de');
    }finally{await client?.close();await new Promise(resolve=>server.close(resolve));}
  });


const contentSnapshots=String.raw`
import copy,json,sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(sys.argv[1])/'tools/quest-wizard'))
from state import Store
import state,wizard
state.PROGRESS_INTERVAL=0
with tempfile.TemporaryDirectory() as temp:
    store=Store(Path(temp)/'owned');session=store.create(wizard.choices({'gameRoot':str(Path(temp)/'Game')}))['session']
    store.begin_stage(session,'build','same-input');store.operation(session,'build','content-bank')
    result={}
    def report(phase,done,total,unit='steps',status='progress'):
        store.progress(session,'build',phase,done,total,unit,'Actual source-backed progress',operation='content-bank',status=status)
    def snapshot(name): result[name]=copy.deepcopy(next(row for row in store.load(session)['stages'] if row['id']=='build'))
    report('unity-work-invocation',0,1,'invocations','start');report('unity-work-plan',38,76778,'assets','start')
    report('unity-work-stage:method',0,1,status='start')
    for phase,count,unit in [('unity-addressables-keys',6531,'assets'),('unity-addressables-entries',6531,'assets'),('unity-addressables-shaders',688,'shaders'),('unity-addressables-shader-roots',688,'shaders')]:
        report(phase,count,count,unit,'complete')
    report('unity-shader-compile',90,100,'variants')
    report('unity-addressables-build',1,1,status='complete');report('unity-native-shader-audit',0,7,'bundles','start')
    report('unity-native-shader-audit-read',8388608,134217728,'bytes');snapshot('read-start')
    report('unity-native-shader-audit-read',67108864,134217728,'bytes');snapshot('read-half')
    report('unity-native-shader-audit-read',134217728,134217728,'bytes','complete');snapshot('read-done')
    report('unity-native-shader-audit-objects',100,1000,'objects');snapshot('objects')
    failed=copy.deepcopy(next(row for row in store.load(session)['stages'] if row['id']=='build'));failed['status']='failed'
    store.save(dict(store.load(session),stages=[failed if row['id']=='build' else row for row in store.load(session)['stages']]))
    snapshot('failed');print(json.dumps(result))
`;

test('browser shows completed Shader parts at100%, fine measured bytes and isolated later failure',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B','-c',contentSnapshots,resolve(root,'../..')],{encoding:'utf8'}));
    let mode='read-start',client;
    const state=()=>({session:'content-work-session',status:mode==='failed'?'failed':'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},needsActions:[],artwork:[],stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:'complete',percent:100}})});
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:'content-work-session',games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
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
      const part=id=>"document.querySelector('[data-plan=unity-work-content-bank] [data-operation="+id+"]')";
      await client.wait(part('shaders')+".textContent.includes('100 %')");
      for(const id of ['catalog','shaders','build']){
        assert.equal(await client.evaluate(part(id)+".classList.contains('complete')"),true);
        assert.doesNotMatch(await client.evaluate(part(id)+'.textContent'),/Ausstehend|99 %/);
      }
      const compiler="document.querySelector('[data-compiler-owner=content-bank]')";
      assert.equal(await client.evaluate(compiler+'.querySelector("progress").value'),100);
      assert.match(await client.evaluate(compiler+'.textContent'),/Abgeschlossen.*100 %/s);
      assert.doesNotMatch(await client.evaluate(compiler+'.textContent'),/90 \/ 100/);
      await client.wait("document.getElementById('progress-detail').textContent.includes('8 MiB')");
      const start=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");
      mode='read-half';await client.wait("document.getElementById('progress-detail').textContent.includes('64 MiB')");
      const half=await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))");assert.ok(half>start);
      mode='read-done';await client.wait("document.getElementById('substep-label').textContent.includes('Gebautes Shader-Ergebnis bestätigen')");
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'30 %','A returned byte-read scope leaves the remaining object/result gate active');
      assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/128 MiB \/ 128 MiB/);
      assert.ok(await client.evaluate("Number(document.getElementById('progress-track').getAttribute('aria-valuenow'))")>half);
      assert.equal(await client.evaluate(part('audit')+".classList.contains('running')"),true,'read completion leaves the authoritative result pending');
      mode='objects';await client.wait("document.getElementById('substep-label').textContent.includes('Gebaute Shader-Objekte bestätigen')");
      mode='failed';await client.wait(part('audit')+".classList.contains('failed')");
      assert.equal(await client.evaluate(part('shaders')+".classList.contains('complete')"),true);
      assert.equal(await client.evaluate(part('postprocess')+".classList.contains('pending')"),true);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
      await client.picture('content-shader-terminal-and-byte-progress-de');
    }finally{await client?.close();await new Promise(resolve=>server.close(resolve));}
  });
