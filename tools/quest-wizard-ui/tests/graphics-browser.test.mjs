// Actual planner snapshots rendered by Chrome, without game/Unity/build tools.
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
const ids=['tools','source','unity','profile','inspect','build','install'],session='graphics-owner-session';
test('named shader passes expose real completed/remaining tasks and compiler variants stay under their bank',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./graphics_fixture.py',import.meta.url)),repo],{encoding:'utf8'}));
  let mode=0,client;
  const state=()=>({session,status:'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},artwork:[],needsActions:[],
   stages:ids.map((id,index)=>index===5?rows[mode]:{id,status:index<5?'complete':'pending',progress:{stagePercent:index<5?100:0}})});
  const server=createServer(async(request,response)=>{
   try{
    const url=new URL(request.url,'http://localhost');let value;
    if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,games:[],unityEditors:[],capabilities:{browse:false,logs:true,support:false}};
    else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
    else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
    else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
    if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
    const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
    if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
    response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
   }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
   client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
   await client.wait("document.querySelector('[data-plan=prepare-graphics]')!==null");
   await client.evaluate("document.querySelector('[data-language=de]').click()");
   assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/1 \/ 2 Teilaufgaben abgeschlossen.*1 ausstehend.*Aktuelle Teilaufgabe 2 \/ 2.*Spiel-Shader.*4 gemeldete Arbeitsgänge/s);
   await client.evaluate("document.querySelector('[data-plan=prepare-graphics]').open=true;document.querySelector('[data-plan=passes-campaign-shaders]').open=true");
   assert.equal(await client.evaluate("document.querySelectorAll('[data-plan=passes-campaign-shaders] li').length"),13);
   assert.match(await client.evaluate("document.querySelector('[data-plan=prepare-graphics]>summary').textContent"),/1 \/ 2.*1 ausstehend/);
   assert.match(await client.evaluate("document.querySelector('[data-plan=passes-campaign-shaders]>summary').textContent"),/4 gemeldete Arbeitsgänge.*Gesamtzahl weiterer Arbeitsgänge wird nicht gemeldet/);
   assert.match(await client.evaluate("document.querySelector('[data-operation=campaign-shaders-binary-materials]').textContent"),/Nur bei entsprechenden Spieldaten erforderlich/);
   assert.match(await client.evaluate("document.querySelector('[data-plan=prepare-graphics] [data-operation=campaign-compute]').textContent"),/Compute-Shader.*Übernommen.*vollständiger Zwischenstand/s);
   assert.match(await client.evaluate("document.querySelector('[data-operation=campaign-shaders-inventory]').textContent"),/0 \/ 3/);
   await client.picture('shader-checkpoint-and-pass-plan-de');
   const previous=Number(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"));
   mode=1;await client.wait("document.getElementById('progress-detail').textContent.includes('0 / 10')");
   assert.match(await client.evaluate("document.getElementById('progress-completed').textContent"),/Aktuelle Teilaufgabe 2 \/ 2.*4 gemeldete Arbeitsgänge/s);
   assert.equal(await client.evaluate("document.querySelector('[data-plan=passes-campaign-shaders]').open"),true,'polls preserve expanded passes');
   assert.ok(Number(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"))>=previous);
   mode=2;await client.wait("document.querySelector('[data-plan=passes-campaign-shaders]>summary').textContent.includes('5 / 11')");
   assert.match(await client.evaluate("document.querySelector('[data-plan=passes-campaign-shaders]>summary').textContent"),/6 ausstehend/);
   assert.equal(await client.evaluate("document.querySelector('[data-plan=passes-campaign-shaders] [data-operation=campaign-shaders-binary-materials]')===null"),true);
   mode=3;await client.wait("document.querySelector('[data-compiler-owner=mod-banks]')!==null");
   assert.equal(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks]').closest('[data-operation]').dataset.operation"),'mod-banks');
   assert.match(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks]').textContent"),/Unity-Shader kompilieren.*Aktueller Shader-Durchlauf.*geplante Unity-Arbeit.*4.903 \/ 12.288 Varianten/s);
   assert.equal(await client.evaluate("document.querySelector('[data-operation=unity-import]').classList.contains('pending')"),true);
   assert.ok(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks] progress').value>39"));
   mode=4;await client.wait("document.querySelector('[data-compiler-owner=mod-banks]').textContent.includes('9.467 / 12.288')");
   assert.ok(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks] progress').value>77"));
   await client.picture('actual-unity-shader-variant-progress-de');
   mode=5;await client.wait("document.querySelector('[data-compiler-owner=mod-banks]').textContent.includes('gemeldeten Kompilierungsaufgabe')");
   assert.equal(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks] progress').value"),20);
   await client.evaluate("document.querySelector('[data-language=en]').click()");
   assert.match(await client.evaluate("document.querySelector('[data-compiler-owner=mod-banks]').textContent"),/Compile Unity shaders.*compilation task reported by Unity/s);
   await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
   assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
   await delay(1100);assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}}
 });
