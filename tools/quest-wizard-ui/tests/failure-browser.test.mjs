// Direct DOM/HTTP failure witness. This fixture never starts build tools.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const ids=['tools','source','unity','profile','inspect','build','install'];
const retained='visible-failure-session';
test('failed status survives unavailable events and prominently shows cause, retry and diagnostics',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const capacity=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./native_memory_fixture.py',import.meta.url)),resolve(root,'../..')],{encoding:'utf8'}));
    let mode='running',client;const posts=[];
    const state=()=>({session:retained,status:mode==='memory'?'blocked':mode,choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},
      needsActions:mode==='failed'?[{code:'build_tool_failed',stage:'build',message:{de:'Die Konvertierung der Spielassets ist fehlgeschlagen.',en:'Game asset conversion failed.'},
        parameters:{failureStage:'recovery',cause:"ModuleNotFoundError: No module named 'recover'"}}]:mode==='memory'?[capacity.action]:[],artwork:[],
      stages:ids.map((id,index)=>index===5&&mode==='memory'?capacity.stage:({id,status:index<5?'complete':index===5?mode:'pending',
        ...(index===5&&mode==='failed'?{waiting:{since:100}}:{}), // An old prerequisite cannot disguise terminal failure.
        progress:{stagePercent:index<5?100:index===5?46.3:0,phase:index===5?(mode==='memory'?'native-memory-check':'recovery-asset-references'):'complete',percent:null,updatedAt:Date.now()/1000-180}}))});
    const server=createServer(async(request,response)=>{
      try {
        const url=new URL(request.url,'http://localhost');let value;
        if(request.method==='POST'){let body='';for await(const part of request)body+=part;posts.push({path:url.pathname,body:JSON.parse(body)});}
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:retained,games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:true}};
        else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
        else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(url.pathname==='/api/events'){
          if(mode==='failed')await delay(1200);
          response.writeHead(503,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',code:'log_unavailable'}));return;
        }else if(url.pathname==='/api/run'){mode='running';value={schema:1,event:'started'};}
        else if(url.pathname==='/api/support'){
          response.writeHead(200,{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="fixture-support.zip"'});response.end('fixture');return;
        }
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});
        response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    try {
      client=await browser();await client.command('Browser.setDownloadBehavior',{behavior:'deny'});
      await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.equal(await client.evaluate("document.getElementById('substep-label').textContent"),'Teilabschnitt: Verknüpfungen der Spielassets erfassen');
      assert.match(await client.evaluate("document.getElementById('activity-status').textContent"),/Seit 00:03:00.*nicht bestätigt/);
      assert.equal(await client.evaluate("document.getElementById('activity-status').classList.contains('quiet')"),true);
      assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false);
      mode='failed';
      await client.wait("document.getElementById('progress-title').textContent==='Build fehlgeschlagen'");
      // Failure is rendered before the slow, failing /events response returns.
      assert.equal(await client.evaluate("document.getElementById('action-needed').getAttribute('role')"),'alert');
      assert.equal(await client.evaluate("document.getElementById('action-needed').classList.contains('failure')"),true);
      assert.equal(await client.evaluate("document.getElementById('failure-cause').textContent"),"Ursache: ModuleNotFoundError: No module named 'recover'");
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/^Spielinhalte konvertieren: .*fehlgeschlagen/);
      assert.equal(await client.evaluate("document.getElementById('unity-actions').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('cancel').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('primary').disabled"),false);
      assert.equal(await client.evaluate("document.querySelector('.progress-track.indeterminate')===null"),true,'a failed tool must not keep animating');
      assert.equal(await client.evaluate("document.getElementById('activity-status').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('failure-retry').getBoundingClientRect().bottom<=innerHeight"),true,'the error and next action are visible without opening logs');
      await client.wait("!document.getElementById('log-status').hidden");
      assert.equal(await client.evaluate("document.getElementById('error-banner').hidden"),true,'unavailable logs do not override the build error');
      await client.picture('asset-conversion-failure-prominent-de');
      await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
      await client.evaluate("document.getElementById('action-needed').scrollIntoView({block:'start'})");
      assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
      await client.picture('asset-conversion-failure-mobile-de');
      await client.evaluate("document.getElementById('failure-support').click()");
      await client.wait("!document.getElementById('failure-support').disabled");
      assert.deepEqual(posts.at(-1),{path:'/api/support',body:{session:retained}});
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Build failed');
      assert.match(await client.evaluate("document.getElementById('failure-cause').textContent"),/^Cause: ModuleNotFoundError/);
      await client.evaluate("document.getElementById('failure-retry').click()");
      await client.wait("document.getElementById('action-needed').hidden&&!document.getElementById('cancel').hidden");
      assert.deepEqual(posts.at(-1),{path:'/api/run',body:{session:retained}});
      assert.equal(posts.filter(row=>row.path==='/api/plan').length,0,'retry resumes the retained owner session');
      assert.equal(await client.evaluate("document.getElementById('failure-cause').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('failure-actions').hidden"),true);
      mode='memory';
      await client.wait("document.getElementById('progress-title').textContent==='Prerequisite missing'");
      assert.equal(await client.evaluate("document.getElementById('action-needed').getAttribute('role')"),'alert');
      assert.equal(await client.evaluate("document.getElementById('failure-actions').hidden"),false);
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/Native compilation memory capacity:.*32\.8 GiB.*24\.5 GiB.*44\.4 GiB/);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/Free memory capacity/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('failure-next').textContent"),/corrected builder/);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=weave]').classList.contains('complete')"),true,'the next compiler prerequisite cannot mark completed weaving as failed');
      assert.equal(await client.evaluate("document.querySelectorAll('#build-groups .failed,#build-groups .running,#build-groups .checking').length"),0);
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/Speicherkapazität.*Auslagerungsdatei/);
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Voraussetzung fehlt');
      await client.picture('native-memory-prerequisite-de');
      await client.evaluate("document.getElementById('failure-retry').click()");
      await client.wait("document.getElementById('action-needed').hidden");
      assert.deepEqual(posts.at(-1),{path:'/api/run',body:{session:retained}});
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });
