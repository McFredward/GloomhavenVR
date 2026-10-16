import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {activityView,buildOverviewView,stageProgress} from '../model.mjs';
import {translate} from '../i18n.mjs';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),repo=resolve(root,'../..');
const snapshots=()=>JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./compact_update_fixture.py',import.meta.url)),repo],{encoding:'utf8'}));

test('actual progress and activity use separate clocks; preparation counts only its finite planned scopes',()=>{
  assert.deepEqual(activityView({progress:{lastProgressAt:100,lastActivityAt:200}},240),{seconds:140,activitySeconds:40,quiet:false,measured:true});
  assert.deepEqual(activityView({progress:{lastProgressAt:null,lastActivityAt:200}},240),{seconds:null,activitySeconds:40,quiet:false,measured:true});
  const rows=snapshots(),value=stageProgress(rows.roster),refreshed=stageProgress(rows.refresh);
  assert.equal(value.phasePercent,35);assert.equal(refreshed.phasePercent,35);
  assert.equal(value.displayPhase,'update-code-preparation');
  assert.equal(refreshed.progressScope,'update-preparation');
  const overview=buildOverviewView({stages:[rows.roster]});
  assert.equal(overview.workflow,'update-mod');assert.equal(overview.done,2);assert.equal(overview.total,6);
  const code=overview.groups.flatMap(row=>row.operations).find(row=>row.id==='update-code');
  assert.equal(code.updateCode.done,4);assert.equal(code.updateCode.total,6);
  assert.equal(code.updateCode.preparation.done,1);assert.equal(code.updateCode.preparation.total,5);
  assert.equal(code.updateCode.preparation.passes[1].counter.done,64);
  const handover=stageProgress(rows.handover);
  assert.equal(handover.displayPhase,'unity-native-initialization');assert.equal(handover.phasePercent,0);
  assert.equal(handover.progressScope,'next-phase');assert.ok(handover.percent<100);
  for(const language of ['de','en'])for(const key of ['updateOverviewTitle','updateCodeSummary','updatePreparationSummary','activityMeasured','activityOnly','phase_update-code-preparation','phase_update-code-roster'])assert.notEqual(translate(language,key),key);
});

test('Chrome shows the actual update workflow, counted script roster, and independent activity clocks',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const rows=snapshots(),session='compact-update-session';let mode='roster',client;
  const state=()=>({session,status:'running',choices:{mode:'update-mod',gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},artwork:[],needsActions:[],
    stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0}})});
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost');let value;
      if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:'running'}],games:[],unityEditors:[],capabilities:{browse:false,logs:true,support:false}};
      else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
      else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
      else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
      else if(url.pathname==='/api/log')value={schema:1,event:'log',text:'Actual compact-update counted progress fixture',truncated:false};
      if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
      const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
      if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
      response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
    client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
    await client.chooseHome('resume');
    await client.wait("document.getElementById('build-overview')&&!document.getElementById('build-overview').hidden");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.equal(await client.evaluate("document.getElementById('build-overview-title').textContent"),'Ablauf der APK-Aktualisierung');
    assert.match(await client.evaluate("document.getElementById('build-overview-count').textContent"),/2.*6/);
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'35');
    assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Nativen Code-Build vorbereiten/);
    assert.match(await client.evaluate("document.querySelector('[data-operation=update-code-roster]').textContent"),/64.*128/);
    assert.equal(await client.evaluate("document.querySelector('[data-operation=update-owned-game]').classList.contains('complete')"),true);
    assert.match(await client.evaluate("document.getElementById('activity-status').textContent"),/Arbeitsfortschritt.*Werkzeugaktivität/);
    await client.evaluate("document.getElementById('build-overview').scrollIntoView({block:'start'})");await client.picture('compact-update-roster-de');
    mode='refresh';await client.wait("document.getElementById('progress-detail').textContent.includes('Asset Pipeline Refresh')");
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'35');
    mode='handover';await client.wait("document.getElementById('substep-label').textContent.includes('Native Unity-Kompilierung starten')");
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'0');
    assert.equal(await client.evaluate("document.querySelectorAll('[data-update-preparation]>ol>li.complete').length"),5);
    await client.evaluate("document.getElementById('build-overview').scrollIntoView({block:'start'})");await client.picture('compact-update-preparation-handover-de');
    mode='code-complete';await client.wait("document.querySelector('[data-group=code]>ol>[data-operation=update-code]').classList.contains('complete')");
    assert.equal(await client.evaluate("document.querySelectorAll('[data-plan=update-code]>ol>li:not(.complete)').length"),0);
    await client.picture('compact-update-completed-code-de');
    await client.evaluate("document.querySelector('[data-language=en]').click()");
    assert.equal(await client.evaluate("document.getElementById('build-overview-title').textContent"),'APK update workflow');
  }finally{await client?.close();await new Promise(resolve=>server.close(resolve));}
});
