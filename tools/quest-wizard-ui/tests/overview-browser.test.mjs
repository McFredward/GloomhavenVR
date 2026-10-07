// Actual planner projections served to Chrome; no exports or build tools run.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),repo=resolve(root,'../..');
const ids=['tools','source','unity','profile','inspect','build','install'],session='overview-owner-session';
test('resumed build names retained, current and remaining real work while observed child counts move total',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./overview_fixture.py',import.meta.url)),repo],{encoding:'utf8'}));
  let mode=0,client;const posts=[];
  const state=()=>({session,status:rows[mode].status,choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},artwork:[],
   needsActions:mode===0?[{code:'build_tool_failed',stage:'build',message:{de:'Projektkopie fehlgeschlagen.',en:'Project copy failed.'},parameters:{failureStage:'recovery',cause:'FileExistsError: retained copy target'}}]:[],
   stages:ids.map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0}})});
  const server=createServer(async(request,response)=>{
   try {
    const url=new URL(request.url,'http://localhost');let value;
    if(request.method==='POST'){let body='';for await(const part of request)body+=part;posts.push({path:url.pathname,body:JSON.parse(body)});}
    if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,games:[],unityEditors:[],capabilities:{browse:false,logs:true,support:false}};
    else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
    else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
    else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
    else if(url.pathname==='/api/log')value={schema:1,event:'log',text:'Fixture: original retained copy report',truncated:false};
    else if(url.pathname==='/api/run'){mode=1;value={schema:1,event:'started'};}
    if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
    const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
    if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
    response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
   }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try {
   client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
   await client.wait("document.getElementById('build-overview')&&!document.getElementById('build-overview').hidden");
   await client.evaluate("document.querySelector('[data-language=de]').click()");
   assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'47');
   assert.equal(await client.evaluate("document.getElementById('setup-overview').open"),false);
   assert.equal(await client.evaluate("document.querySelectorAll('.build-group').length"),6);
   assert.match(await client.evaluate("document.getElementById('build-overview-count').textContent"),/6 \/ 23.*17 ausstehend/);
   assert.match(await client.evaluate("document.querySelector('[data-group=recovery]').textContent"),/Datenpakete: 16 \/ 16.*7 \/ 8.*2 \/ 14/s);
   assert.match(await client.evaluate("document.querySelector('[data-group=recovery] [data-operation=copy]').textContent"),/Assets kopieren.*Fehlgeschlagen/);
   await client.evaluate("document.getElementById('build-overview').scrollIntoView({block:'start'})");await client.picture('retained-build-overview-failed-de');
   await client.evaluate("document.getElementById('failure-retry').click()");
   await client.wait("document.getElementById('build-current').textContent.includes('Spieldateien prüfen')");
   assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'47');
   assert.match(await client.evaluate("document.querySelector('[data-operation=game-inputs]').textContent"),/Erhaltenes Ergebnis erneut prüfen/);
   assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
   assert.deepEqual(posts,[{path:'/api/run',body:{session}}]);
   await client.picture('retained-build-overview-recheck-de');
   mode=2;await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='"+rows[2].progress.stagePercent+"'");
   assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/25\.000 \/ 189\.701 Dateien/);
   await client.evaluate("document.querySelector('[data-plan=staging]').open=true");
   assert.equal(await client.evaluate("document.querySelectorAll('[data-plan=staging] li').length"),14);
   assert.match(await client.evaluate("document.querySelector('[data-plan=staging]').textContent"),/Schriften vorbereiten.*Ausstehend/s);
   await delay(1100);assert.equal(await client.evaluate("document.querySelector('[data-plan=staging]').open"),true,'polling preserves expanded work details');
   mode=3;await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='"+rows[3].progress.stagePercent+"'");
   assert.ok(rows[3].progress.stagePercent>rows[2].progress.stagePercent,'real observed file counts advance whole-stage high-water');
   assert.match(await client.evaluate("document.querySelector('[data-group=import]').textContent"),/Unity-Import.*Ausstehend/);
   await client.evaluate("document.getElementById('build-overview').scrollIntoView({block:'start'})");await client.picture('retained-build-overview-copy-progress-de');
   await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
   assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
   await client.picture('retained-build-overview-mobile-de');
   await client.evaluate("document.getElementById('setup-overview').open=true;document.getElementById('diagnostic-details').open=true;document.getElementById('load-log').click()");
   await client.wait("document.getElementById('stage-log').textContent.includes('retained copy report')");
   mode=4;await client.wait("document.getElementById('build-current').textContent.includes('Menü und Startinhalte')");
   assert.match(await client.evaluate("document.querySelector('[data-operation=startup-content]').textContent"),/Wird erstellt/);
   await client.evaluate("document.querySelector('[data-language=en]').click()");
   assert.equal(await client.evaluate("document.getElementById('build-overview-title').textContent"),'Quest build workflow');
   assert.match(await client.evaluate("document.getElementById('build-current').textContent"),/Prepare menu and startup content/);
   assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}}
 });
