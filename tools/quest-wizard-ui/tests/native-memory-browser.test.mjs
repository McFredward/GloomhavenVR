// Real parser/planner observations rendered through authenticated local HTTP.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
test('memory wait and retry retain completed work and resume automatically without a fake ETA',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const rows=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./native_adaptive_fixture.py',import.meta.url)),resolve(root,'../..')],{encoding:'utf8'}));
    let mode='retry',client;
    const state=()=>({session:'adaptive-memory-session',status:mode==='complete'?'complete':'running',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:false},needsActions:[],artwork:[],
      timing:{schema:1,elapsedSeconds:600},stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>index===5?rows[mode]:{id,status:index<5||mode==='complete'?'complete':'pending',progress:{stagePercent:index<5?100:0,phase:'complete',percent:100}})});
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:'adaptive-memory-session',games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
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
      const before=await client.evaluate("document.getElementById('progress-count').textContent");
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Release mit reduzierten Debugdaten.*1 Arbeitsprozess.*Versuch 2.*36,1 GiB.*27,5 GiB.*60,5 MiB.*50,6 MiB/);
      assert.equal(await client.evaluate("document.querySelector('[data-operation=weave]').classList.contains('complete')"),true);
      mode='wait';
      await client.wait("document.getElementById('substep-count').textContent==='Wartet auf Speicher'");
      assert.equal(await client.evaluate("document.getElementById('progress-count').textContent"),before);
      assert.match(await client.evaluate("document.getElementById('activity-status').textContent"),/setzt automatisch fort/);
      assert.equal(await client.evaluate("document.getElementById('eta-value').textContent"),'Wartezeit auf freien Speicher ist nicht abschätzbar');
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false);
      assert.equal(await client.evaluate("document.getElementById('substep-track').classList.contains('indeterminate')"),false);
      assert.equal(await client.evaluate("document.querySelectorAll('#build-groups .failed').length"),0);
      await client.picture('native-memory-auto-wait-de');
      mode='resumed';
      await client.wait("document.getElementById('substep-label').textContent==='Teilabschnitt: Compiler an verfügbare Ressourcen anpassen'");
      assert.equal(await client.evaluate("document.getElementById('progress-count').textContent"),before);
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      mode='counter';
      await client.wait("document.getElementById('progress-detail').textContent.includes('actual-next.cpp')");
      assert.equal(await client.evaluate("document.getElementById('progress-detail').textContent.includes('Versuch 2')"),false,'old pressure observations do not replace actual resumed work');
      mode='complete';
      await client.wait("document.getElementById('progress-count').textContent==='100 %'");
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });
