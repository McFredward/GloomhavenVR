import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {buildOverviewView,stageProgress} from '../model.mjs';
import {browser,chrome} from './browser-harness.mjs';

function stage(passDone=6,passTotal=6,passClosed=true,shaderClosed=false){
  const aggregate={phase:'unity-player-shaders',done:250,total:250,unit:'shader-names',scope:'sources',percent:shaderClosed?100:80,status:shaderClosed?'complete':'running',closed:shaderClosed};
  return {id:'build',status:'running',progress:{phase:'unity-shader-compile',done:passDone,total:passTotal,percent:100*passDone/passTotal,
    operationStatus:passClosed?'complete':'progress',activeWork:{operation:'player'},buildOverview:{schema:1,active:'player',groups:[{id:'export',operations:[{
      id:'player',closed:false,status:'running',compiler:{phase:'unity-shader-compile',done:passDone,total:passTotal,unit:'variants',scope:'pass',percent:aggregate.percent,status:aggregate.status,closed:shaderClosed,aggregate,
        detail:'[shader-pass:1557] [shader-source:outside] Unity shader compilation: Hidden/TextCore/Distance Field SSD / unnamed pass · pass #1557 · completed passes 1557 · completed variants 167610 · pass finished',
        currentPass:{phase:'unity-shader-compile',done:passDone,total:passTotal,unit:'variants',scope:'pass',percent:100*passDone/passTotal,status:passClosed?'complete':'running'}},
      unityWork:{phases:[{id:'assets',closed:true,status:'reused',percent:100},{id:'method',closed:false,status:'running',percent:50,parts:[{id:'shaders',closed:shaderClosed,status:aggregate.status,percent:aggregate.percent},
        {id:'native',closed:false,status:'pending',percent:0}]},{id:'result',closed:false,status:'pending',percent:0}]}}]}]}}};
}

test('all Shader progress never changes to a single pass fraction during ready/finished/repeated passes',()=>{
  for(const value of [stage(),stage(2,10,false),stage(999998,999999,false),stage(0,500,false)]){
    const view=stageProgress(value),compiler=buildOverviewView({stages:[value]}).groups[0].operations[0].compiler;
    assert.equal(view.phasePercent,80);assert.equal(view.displayPhase,'unity-shader-overall');
    assert.equal(view.scopeCounter.unit,'shader-names');assert.equal(view.scopeCounter.total,250);
    assert.equal(compiler.aggregate.percent,80);assert.equal(compiler.aggregate.closed,false);assert.equal(compiler.status,'running');
    assert.equal(compiler.currentPass.counter.total,value.progress.total);
  }
  const returned=stageProgress(stage(6,6,true,true));
  assert.equal(returned.displayPhase,'unity-work-stage:native');assert.equal(returned.progressScope,'next-phase');
  assert.equal(returned.phasePercent,0);
  const compiler=buildOverviewView({stages:[stage(6,6,true,true)]}).groups[0].operations[0].compiler;
  assert.equal(compiler.aggregate.percent,100);assert.equal(compiler.aggregate.closed,true);
});

test('unqualified individual pass cannot masquerade as an exact global queue',()=>{
  const value=stage();delete value.progress.buildOverview.groups[0].operations[0].compiler.aggregate;
  delete value.progress.buildOverview.groups[0].operations[0].unityWork;
  assert.equal(stageProgress(value).phasePercent,null);
  const compiler=buildOverviewView({stages:[value]}).groups[0].operations[0].compiler;
  assert.equal(compiler.aggregate.scope,'caller');assert.equal(compiler.aggregate.percent,null);
});

test('old saved local-pass completion is projected from retained whole Shader evidence',()=>{
  const value=stage();const owner=value.progress.buildOverview.groups[0].operations[0];
  delete owner.compiler.aggregate;delete owner.compiler.currentPass;
  owner.compiler.closed=true;owner.compiler.status='complete';owner.compiler.percent=100;
  owner.unityWork.phases[1].parts[0].counter={phase:'unity-player-shaders',done:250,total:251,unit:'tasks'};
  const compiler=buildOverviewView({stages:[value]}).groups[0].operations[0].compiler;
  assert.equal(compiler.status,'running');assert.equal(compiler.percent,80);
  assert.equal(compiler.aggregate.counter.total,250);assert.equal(compiler.currentPass.percent,100);
  const current=stageProgress(value);assert.equal(current.phasePercent,80);assert.equal(current.scopeCounter.total,250);
});

test('browser separates captured last-pass100% from overall80% and shows retained storage recovery',
  {skip:!existsSync(chrome),timeout:25000},async()=>{
    const root=fileURLToPath(new URL('../',import.meta.url)),session='shader-total-session';
    let mode='shaders',client;
    const current=()=>{
      const build=stage(6,6,true,mode==='handoff');build.progress.stagePercent=83;
      const state={session,status:'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:true},needsActions:[],artwork:[],
        stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?build:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:index<5?'complete':'pending',percent:index<5?100:0}})};
      if(mode==='restore'){
        state.status='blocked';build.status='complete';build.details={apk:'retained.apk',inputKey:'a'.repeat(64),apkSha256:'b'.repeat(64)};
        build.progress={stagePercent:100,phase:'complete',percent:100};
        state.stages[6].status='blocked';
        state.needsActions=[{code:'quest_signing_repair_wait',stage:'install',parameters:{failureStage:'install',completedBuildRetained:true,canSkipInstall:true,saveDataProtected:true,repairReason:'restore-app-access',repairRoot:'private',repairAttempts:3,repairTotalAttempts:3}}];
      }
      return state;
    };
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:'running'}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
        else if(url.pathname==='/api/status')value={schema:1,event:'status',state:current()};
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
      await client.chooseHome('resume');await client.evaluate("document.querySelector('[data-language=de]').click()");
      const compiler="document.querySelector('[data-compiler-owner=player]')";
      await client.wait(compiler+"?.querySelector('[data-shader-overall]')?.value===80");
      assert.equal(await client.evaluate(compiler+".querySelector('[data-shader-pass]').value"),100);
      assert.equal(await client.evaluate(compiler+".classList.contains('running')"),true);
      assert.match(await client.evaluate(compiler+'.textContent'),/Beobachtete Shader-Namen: 250 \/ 250.*80 %.*completed passes 1557.*Aktueller Shader-Durchlauf.*6 \/ 6 Varianten.*100 %/s);
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'80 %');
      await client.evaluate(compiler+".scrollIntoView({block:'center'})");await client.picture('captured-1557-pass100-overall80-de');
      mode='handoff';await client.wait(compiler+"?.querySelector('[data-shader-overall]')?.value===100");
      assert.equal(await client.evaluate(compiler+".querySelector('[data-shader-pass]')===null"),true);
      assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'0 %');
      assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Nächster Teilabschnitt/);
      await client.evaluate(compiler+".scrollIntoView({block:'center'})");await client.picture('captured-whole-shader-handoff100-de');
      mode='restore';await client.wait("document.getElementById('progress-title').textContent==='Installation wartet: App-Speicher öffnen.'");
      assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/Spielstände sind gesichert.*beschreibbaren App-Speicher/);
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/entsperren.*Nur die Wiederherstellung.*nicht neu gebaut.*nicht zurückgesetzt/);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      assert.equal(await client.evaluate("document.getElementById('failure-retry').disabled"),false);
      await client.evaluate("document.getElementById('action-needed').scrollIntoView({block:'center'})");await client.picture('restore-app-access-retained-build-de');
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/unlock.*Only restoration resumes.*not rebuilt.*not reset/);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{await client?.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
  });
