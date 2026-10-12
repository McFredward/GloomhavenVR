// Actual Chrome verifies cancellation admission and truthful scan presentation;
// HTTP fixture counters are deliberate observations, never a headset result.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';
import {LocalApi} from '../transport.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),GiB=1073741824;
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
async function fixture({failure=false}={}){
  const requests=[];let operation=0,current={status:'idle'},inventory={status:'stale',totalBytes:130*GiB,files:999999,completedAt:1791800000,categories:[]},timer;
  const id=()=>String(++operation).padStart(32,'0');
  const observed=()=>({done:371712,files:371712,folders:4500,bytes:70*GiB,currentPath:'build/projects/owned/Library',detail:'build/projects/owned/Library',startedAt:Date.now()/1000-7,total:null});
  const plan=()=>({id:'current-confirmed-plan',mode:'build-cache',paths:[{path:'build/projects/owned',bytes:70*GiB,files:371712}],bytes:70*GiB,files:371712});
  const value=()=>({schema:1,event:'storage',workspaceRoot:'C:\Users\Fixture\.ghvrq',freeBytes:5*GiB,canDeleteBuildCache:true,state:current,inventory});
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      const reply=(data,code=200)=>{response.writeHead(code,{'Content-Type':'application/json'});response.end(JSON.stringify(data));};
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'storage-cancel-token');let raw='';for await(const chunk of request)raw+=chunk;
        const body=raw?JSON.parse(raw):undefined;requests.push({path,body});
        if(path==='/api/discover')return reply({schema:1,event:'discovery',games:[],unityEditors:[],recentSessions:[],capabilities:{browse:false,storageInventory:true,storageCancel:true,runOverview:true,workspaceSupport:true}});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/storage/runs')return reply({schema:1,event:'runs',runs:[],retiredRuns:[]});
        if(path==='/api/storage')return reply(value());
        if(path==='/api/storage/inventory'){assert.deepEqual(body,{});inventory={...observed(),status:'scanning',operationId:id()};return reply(value());}
        if(path==='/api/storage/plan'){
          assert.deepEqual(body,{mode:'build-cache'});
          current={...observed(),status:'planning',phase:'cleanup-scan',operationId:id(),selection:{mode:'build-cache'}};
          if(failure){failure=false;current={...current,status:'failed',endedAt:Date.now()/1000,error:{code:'cleanup_file_busy',message:{de:'Das Programm mit Dateizugriff schließen, die Vorschau erneuern und wiederholen.',en:'Close the program using the file, refresh the preview and retry.'},parameters:{retryable:true,recovery:'refresh-preview',path:'source/owned/locked.bin'}}};}
          return reply(value());
        }
        if(path==='/api/storage/clean'){
          assert.equal(body.planId,current.plan.id);
          current={status:'deleting',phase:'cleanup-delete',operationId:id(),selection:{mode:'build-cache'},startedAt:Date.now()/1000-7,
            done:2,total:5,unit:'entries',files:1,folders:1,bytes:256,currentPath:'build/cache/owned'};
          return reply(value());
        }
        if(path==='/api/storage/cancel'){
          const selected=body.target==='inventory'?inventory:current;assert.equal(body.operationId,selected.operationId);
          selected.status='cancelling';
          timer=setTimeout(()=>{selected.status='cancelled';selected.endedAt=Date.now()/1000;selected.workerStopped=true;if(body.target==='cleanup'&&selected.files===1)selected.result={freedBytes:256,deletedFiles:1,sessionsRemoved:[]};},350);
          return reply(value());
        }
        if(path==='/api/support'){assert.deepEqual(body,{});response.writeHead(200,{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="storage-cancel-support.zip"'});response.end('fixture archive');return;}
        throw Error('Unexpected API: '+path);
      }
      const file=resolve(root,'.'+(path==='/'?'/index.html':path));assert.ok(file.startsWith(root)&&mime[extname(file)]);
      response.writeHead(200,{'Content-Type':mime[extname(file)]});response.end(await readFile(file));
    }catch(error){if(!response.headersSent){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}else response.destroy();}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#storage-cancel-token',requests,
    change:value=>{current={...current,...value};},ready:()=>{current={...current,status:'preview',plan:plan()};},
    close:()=>{clearTimeout(timer);return new Promise(resolve=>server.close(resolve));}};
}
async function launch(server){const client=await browser();try{
  await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===750||ms===15000?100:ms,...args);'});
  await client.command('Page.navigate',{url:server.url});await client.chooseHome('storage');
  await client.evaluate("document.querySelector('[data-language=de]').click()");return client;
}catch(error){await client.close();throw error;}}

test('storage cancellation transport rejects unknown targets and stale identities before fetch',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1','token',async(url,options)=>{calls.push({url:String(url),body:JSON.parse(options.body)});return {ok:true,json:async()=>({schema:1,event:'storage',state:{status:'cancelling'}})};});
  await api.storageCancel('inventory','1'.repeat(32));await api.storageCancel('cleanup','2'.repeat(32));
  assert.deepEqual(calls.map(row=>row.body.target),['inventory','cleanup']);
  for(const [target,id] of [['build','1'.repeat(32)],['cleanup',''],['inventory',null],['cleanup','../path']])await assert.rejects(async()=>api.storageCancel(target,id));
  assert.equal(calls.length,2);
});

test('preview shows global counters and current folder, can stop and repreview, and never restarts hidden scans',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;try{
    client=await launch(server);assert.equal(server.requests.some(row=>row.path==='/api/storage/inventory'),false);
    assert.match(await client.evaluate("document.getElementById('storage-inventory-status').textContent"),/Letzte Messung.*abweichen/);
    await client.evaluate("document.getElementById('storage-plan-cache').click()");await client.wait("!document.getElementById('storage-cancel').hidden&&!document.getElementById('storage-cancel').disabled");
    const text=await client.evaluate("document.getElementById('storage-status').textContent");
    assert.match(text,/Insgesamt 371\.712 Dateien.*4\.500 Ordner.*70 GiB.*Dauer: 00:00:0[7-9].*build\/projects\/owned\/Library/);
    assert.doesNotMatch(text,/im aktuellen Ordner|100\s*%/);
    assert.equal(await client.evaluate("document.getElementById('storage-progress').hasAttribute('value')"),false,'unknown total stays indeterminate');
    await client.picture('storage-global-scan-and-cancel-de');
    await client.evaluate("document.getElementById('storage-cancel').click()");await client.wait("document.getElementById('storage-status').textContent.includes('Wird angehalten')");
    assert.equal(await client.evaluate("document.getElementById('storage-retry').hidden"),true,'acknowledgement waits for worker exit');
    await client.wait("!document.getElementById('storage-retry').hidden&&!document.getElementById('storage-retry').disabled");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Angehalten.*371\.712/);
    await client.evaluate("document.getElementById('export-support').click()");await client.wait("document.getElementById('support-status').textContent.includes('heruntergeladen')");
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');await delay(250);
    assert.equal(server.requests.filter(row=>row.path==='/api/storage/plan').length,1);assert.equal(server.requests.filter(row=>row.path==='/api/storage/inventory').length,0);
    await client.evaluate("document.getElementById('storage-retry').click()");await client.wait("!document.getElementById('storage-cancel').hidden");
    assert.equal(server.requests.filter(row=>row.path==='/api/storage/plan').length,2);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
});

test('explicit inventory cancellation stays stopped across navigation and permits a new explicit measurement',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;try{
    client=await launch(server);await client.evaluate("document.getElementById('storage-refresh').click()");await client.wait("!document.getElementById('storage-inventory-cancel').hidden");
    await client.evaluate("document.getElementById('storage-inventory-cancel').click()");await client.wait("document.getElementById('storage-inventory-status').textContent.includes('Ermittlung angehalten')");
    assert.match(await client.evaluate("document.getElementById('storage-inventory-status').textContent"),/371\.712.*70 GiB/);
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');await delay(250);
    assert.equal(server.requests.filter(row=>row.path==='/api/storage/inventory').length,1);
    await client.evaluate("document.getElementById('storage-refresh').click()");await client.wait("!document.getElementById('storage-inventory-cancel').hidden");
    assert.equal(server.requests.filter(row=>row.path==='/api/storage/inventory').length,2);
  }finally{try{await client?.close();}finally{await server.close();}}
});

test('stopping deletion retains partial removal and uses file count independently of retry entry counters',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;try{
    client=await launch(server);await client.evaluate("document.getElementById('storage-plan-cache').click()");server.ready();await client.wait("!document.getElementById('storage-confirmation').hidden");
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("!document.getElementById('storage-cancel').disabled");
    server.change({phase:'cleanup-readonly-repair',done:0,total:1});await client.wait("document.getElementById('storage-status').textContent.includes('Schreibschutz')");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Insgesamt 1 Dateien/);
    assert.equal(await client.evaluate("document.getElementById('storage-progress').hasAttribute('value')"),false,'repair action count is not overall deletion progress');
    await client.evaluate("document.getElementById('storage-cancel').click()");await client.wait("document.getElementById('storage-status').textContent.includes('Bereits gelöscht: 1 Dateien')");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/256 B freigegeben/);
    assert.equal(server.requests.some(row=>row.path==='/api/storage/inventory'),false);
    await client.picture('storage-cancelled-partial-removal-de');
  }finally{try{await client?.close();}finally{await server.close();}}
});

test('unrepaired locked file keeps actionable recovery and counters until explicit retry',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({failure:true});let client;try{
    client=await launch(server);await client.evaluate("document.getElementById('storage-plan-cache').click()");await client.wait("!document.getElementById('storage-retry').hidden");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Dateizugriff schließen.*Vorschau erneuern.*371\.712/);
    assert.doesNotMatch(await client.evaluate("document.getElementById('storage-status').textContent"),/Bereinigung fehlgeschlagen/);
    assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'alert');
    await delay(300);assert.equal(server.requests.filter(row=>row.path==='/api/storage/plan').length,1);
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);await client.picture('storage-locked-file-recovery-mobile');
    await client.evaluate("document.getElementById('storage-retry').click()");await client.wait("!document.getElementById('storage-cancel').hidden");
    assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'status');
    assert.equal(server.requests.filter(row=>row.path==='/api/storage/plan').length,2);
  }finally{try{await client?.close();}finally{await server.close();}}
});
