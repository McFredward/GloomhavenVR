// Actual Chrome exercises run-specific admission, measured ownership and the
// workspace diagnostic action without any Unity, game or device process.
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
const ids={blocked:'ab'.repeat(16),cancelled:'cd'.repeat(16),complete:'ef'.repeat(16),running:'01'.repeat(16),retired:'23'.repeat(16)};
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
const row=(session,status,exclusive,shared,reclaimable,mode='build')=>({session,status,mode,created:1791800000,updated:1791800000,
  gameRoot:'C:\\Owned game',exclusiveBytes:exclusive*GiB,sharedBytes:shared*GiB,reclaimableBytes:reclaimable*GiB,
  intermediateBytes:status==='complete'?0:(exclusive+shared)*GiB,deliverableBytes:status==='complete'?exclusive*GiB:0,totalBytes:(exclusive+shared)*GiB,complete:true,exact:true});
async function fixture({single=false,running=false,holdRuns=false,failSupport=false,scanProgress=false}={}){
  let runs=[row(ids.blocked,'blocked',50,30,50),...single?[]:[row(ids.cancelled,'cancelled',8,30,8,'update-mod'),row(ids.complete,'complete',12,0,0,'update-profile')],...running?[row(ids.running,'running',3,30,3)]:[]];
  let retired=single?[]:[{...row(ids.retired,'cancelled',7,0,7),retired:true}],current={status:'idle'},operation=0,heldRuns=null;
  const requests=[];
  const inventory=()=>({status:scanProgress?'scanning':'complete',done:scanProgress?123456:543210,detail:scanProgress?'build/cache/remaining-data':'',totalBytes:130*GiB,files:543210,completedAt:1791800000,exclusiveBytes:80*GiB,sharedBytes:30*GiB,unassignedBytes:20*GiB,
    toolBytes:10*GiB,deliverableBytes:12*GiB,reusableBytes:100*GiB,diagnosticBytes:GiB,categories:[{id:'tools',bytes:10*GiB},{id:'apk-content',bytes:12*GiB},{id:'unity-caches',bytes:80*GiB}],runs,retiredRuns:retired});
  const storage=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:5*GiB,volume:{totalBytes:1000*GiB,usedBytes:995*GiB,freeBytes:5*GiB},canDeleteBuildCache:true,state:current,inventory:inventory()});
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost'),path=url.pathname;
      const reply=(value,code=200)=>{response.writeHead(code,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'run-overview-token');let raw='';for await(const part of request)raw+=part;
        const body=raw?JSON.parse(raw):undefined;requests.push({path,body,method:request.method});
        if(path==='/api/discover')return reply({schema:1,event:'discovery',latestSession:runs[0]?.session??null,recentSessions:runs,
          capabilities:{browse:false,runOverview:true,runRemoval:true,workspaceSupport:true,support:true,logs:false,storageInventory:true,portablePackages:true,deviceTools:true},
          games:[{id:'owned-game',gameRoot:'C:\\Owned game',provider:'gog'}],unityEditors:[],defaultApk:{path:'C:\\Finished\\Quest.apk'}});
        if(path==='/api/storage/runs'){
          const snapshot={schema:1,event:'runs',runs:[...runs],retiredRuns:[...retired],latestSession:runs[0]?.session??null};
          if(holdRuns&&requests.filter(item=>item.path===path).length===2){heldRuns=()=>reply(snapshot);return;}
          return reply(snapshot);
        }
        if(path==='/api/storage')return reply(storage());
        if(path==='/api/storage/inventory')return reply(storage());
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/device/status')return reply({schema:1,event:'device',state:{status:'idle',devices:[]}});
        if(path==='/api/package/status')return reply({schema:1,event:'package',state:{status:'idle'},defaults:{source:'C:\\Finished\\Quest.apk',destination:'C:\\Export.zip'}});
        if(path==='/api/qualify')return reply({schema:1,event:'qualification',spaceEstimate:{additionalEstimatedBytes:GiB},freeBytes:10*GiB});
        if(path==='/api/events')return reply({schema:1,event:'events',events:[]});
        if(path==='/api/status'){
          const value=runs.find(item=>item.session===url.searchParams.get('session'));assert.ok(value);
          return reply({schema:1,event:'status',session:value.session,state:{...value,choices:{gameRoot:value.gameRoot,provider:'gog',mode:value.mode,install:false},needsActions:[],
            stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:value.status==='complete'?'complete':id==='build'?value.status:id==='install'?'pending':'complete'})),result:value.status==='complete'?{apk:'C:\\Finished\\Quest.apk'}:null}});
        }
        if(path==='/api/storage/plan'){
          assert.equal(body.mode,'session');const value=[...runs,...retired].find(item=>item.session===body.session);assert.ok(value);
          assert.ok(value.retired||['blocked','cancelled'].includes(value.status));if(value.retired)assert.equal(body.removeData,true);
          current={status:'preview',operationId:(++operation).toString(16).padStart(32,'0'),plan:{id:'preview-'+operation,mode:'session',session:value.session,retired:value.retired===true,eligible:true,removeData:body.removeData,
            bytes:body.removeData?value.reclaimableBytes:0,files:body.removeData?500000:0,paths:body.removeData?[{path:'build/projects/owned-by-'+value.session,bytes:value.reclaimableBytes,files:500000}]:[],skipped:[]}};return reply(storage());
        }
        if(path==='/api/storage/clean'){
          assert.equal(body.planId,current.plan.id);const plan=current.plan,value=[...runs,...retired].find(item=>item.session===plan.session);
          runs=runs.filter(item=>item.session!==value.session);retired=retired.filter(item=>item.session!==value.session);
          if(!plan.retired&&!plan.removeData)retired.push({...value,retired:true});
          current={status:'complete',operationId:(++operation).toString(16).padStart(32,'0'),result:{sessionsRemoved:plan.retired?[]:[plan.session],freedBytes:plan.bytes,deletedFiles:plan.files}};return reply(storage());
        }
        if(path==='/api/support'){
          assert.deepEqual(body,{});
          if(failSupport)return reply({schema:1,event:'error',code:'support_failed',message:{de:'Diagnoseverzeichnis nicht verfügbar.',en:'Diagnostic folder unavailable.'}},400);
          await delay(60);response.writeHead(200,{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="quest-build-support-workspace.zip"'});response.end('fixture diagnostic archive');return;
        }
        throw Error('Unexpected API '+path);
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));assert.ok(filename.startsWith(root)&&mime[extname(filename)]);
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){if(!response.headersSent){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}else response.destroy();}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#run-overview-token',requests,
    completeExternally:session=>{runs=runs.map(item=>item.session===session?{...item,status:'complete',updated:item.updated+60}:item);},
    held:()=>Boolean(heldRuns),release:()=>{heldRuns?.();heldRuns=null;},close:()=>{heldRuns?.();return new Promise(resolve=>server.close(resolve));}};
}
async function launch(server){const client=await browser();try{
  await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===15000?100:ms,...args);'});
  await client.command('Page.navigate',{url:server.url});await client.wait("document.querySelector('#home-runs-list .run-row')&&!document.getElementById('export-support').disabled");
  await client.evaluate("document.querySelector('[data-language=de]').click()");return client;
}catch(error){await client.close();throw error;}}
const select=(id,action)=>`#home-runs-list [data-session="${id}"] [data-run-action=${action}]`;
async function exportLogs(client){
  assert.equal(await client.evaluate("document.getElementById('export-support').hidden||document.getElementById('export-support').disabled"),false);
  await client.evaluate("document.getElementById('export-support').click()");
  await client.wait("document.getElementById('support-status').textContent.includes('heruntergeladen')&&!document.getElementById('export-support').disabled");
}

test('workspace support is session-optional but rejects an invalid explicit session before any fetch',async()=>{
  const calls=[],blob=new Blob(['diagnostic']),api=new LocalApi('http://127.0.0.1','token',async(url,options)=>{
    calls.push({url,options});return {ok:true,headers:new Headers({'Content-Type':'application/zip'}),blob:async()=>blob};
  });
  await api.support();await api.support(ids.blocked);assert.deepEqual(calls.map(item=>JSON.parse(item.options.body)),[{}, {session:ids.blocked}]);
  for(const invalid of [null,'../session','',{},true])await assert.rejects(api.support(invalid),{code:'invalidReply'});
  assert.equal(calls.length,2);
});

test('main run rows expose each status and storage owner without singleton actions or double-counted shared totals',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({running:true});let client;try{
    client=await launch(server);await client.wait("document.querySelectorAll('#home-runs-list .run-row').length===4");
    assert.equal(await client.evaluate("document.getElementById('home-resume')||document.getElementById('home-remove-run')"),null);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.blocked,'resume'))}))`),true);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.cancelled,'delete'))}))`),true);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.complete,'resume'))}))`),false);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.complete,'delete'))}))`),false);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.running,'open'))}))`),true);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.running,'delete'))}))`),false);
    assert.equal(await client.evaluate(`document.querySelector('#home-runs-list [data-session="${ids.blocked}"] h4').textContent.includes('${ids.blocked}')`),false,'opaque full identifiers are not the main label');
    assert.match(await client.evaluate(`document.querySelector('#home-runs-list [data-session="${ids.blocked}"] .run-usage').textContent`),/50 GiB.*30 GiB.*50 GiB/);
    const reads=server.requests.filter(item=>item.path==='/api/storage/runs').length;await client.chooseHome('storage');
    assert.equal(server.requests.filter(item=>item.path==='/api/storage/runs').length,reads,'navigation reuses cached metadata');
    assert.equal(await client.evaluate("document.querySelectorAll('#storage-runs-list .run-row').length"),4);
    assert.equal(await client.evaluate("document.querySelectorAll('#storage-retired-list .run-row').length"),1);
    assert.equal(await client.evaluate("[...document.querySelectorAll('#storage-ownership dd')].map(node=>node.textContent).join('|')"),'80 GiB|30 GiB|20 GiB');
    assert.match(await client.evaluate("document.getElementById('storage-used').textContent"),/130 GiB/);
    assert.match(await client.evaluate("document.getElementById('storage-volume').textContent"),/995 GiB.*1\.000 GiB.*außerhalb/);
    assert.match(await client.evaluate("document.getElementById('storage-retired-list').textContent"),/Gelöscht.*7 GiB.*Behaltene Daten prüfen/);
    await client.picture('run-owned-storage-de');await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);
    await client.picture('run-owned-storage-mobile');assert.equal(client.events.filter(item=>item.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('deleting the last run removes every main action while retained data remains separately cleanable',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({single:true});let client;try{
    client=await launch(server);await client.evaluate(`document.querySelector(${JSON.stringify(select(ids.blocked,'delete'))}).click()`);
    await client.wait("!document.getElementById('storage-run-plan').disabled");
    assert.equal(await client.evaluate("document.getElementById('storage-run-keep').checked"),true);
    await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-status').textContent.includes('Run gelöscht')");
    await client.evaluate("document.getElementById('home').click()");await client.wait("document.getElementById('home-runs').hidden");
    assert.equal(await client.evaluate("document.querySelectorAll('#home-runs-list .run-row').length"),0);
    await exportLogs(client);await client.chooseHome('storage');await client.wait("document.querySelector('#storage-retired-list [data-run-action=cleanup]')");
    await client.evaluate("document.querySelector('#storage-retired-list [data-run-action=cleanup]').click()");
    assert.equal(await client.evaluate("document.getElementById('storage-run-remove-data').checked"),true);
    assert.equal(await client.evaluate("document.getElementById('storage-run-keep').disabled"),true);
    await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Behaltene Daten.*50 GiB/);
    assert.match(await client.evaluate("document.getElementById('storage-confirm-copy').textContent"),/bereits gelöschten Runs/);
    const reads=server.requests.filter(item=>item.path==='/api/storage/runs').length;
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-status').textContent.includes('50 GiB freigegeben')&&document.getElementById('storage-retired').hidden");
    assert.equal(server.requests.filter(item=>item.path==='/api/storage/runs').length,reads+1,'cleanup with no sessionsRemoved still refreshes retained ownership');
    await delay(250);assert.equal(server.requests.filter(item=>item.path==='/api/storage/runs').length,reads+1,'status polling does not enumerate runs again');
    await client.evaluate("document.getElementById('home').click()");assert.equal(await client.evaluate("document.getElementById('home-runs').hidden"),true);
    await exportLogs(client);assert.equal(server.requests.filter(item=>item.path==='/api/run').length,0);
    assert.equal(client.events.filter(item=>item.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('inventory displays the actual live file counter and current folder instead of its stale final count',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({scanProgress:true});let client;try{
    client=await launch(server);await client.chooseHome('storage');
    assert.match(await client.evaluate("document.getElementById('storage-inventory-status').textContent"),/123\.456.*build\/cache\/remaining-data/);
    assert.doesNotMatch(await client.evaluate("document.getElementById('storage-inventory-status').textContent"),/543\.210/);
    assert.equal(server.requests.filter(item=>item.path==='/api/storage/inventory').length,0,'an existing read-only scan is not restarted');
    await exportLogs(client);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('every page exports workspace diagnostics including empty/deleted sessions, with a visible persistent failure',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;try{
    client=await launch(server);await exportLogs(client);
    await client.chooseHome('build');await exportLogs(client);await client.evaluate("document.getElementById('primary').click()");await client.wait("!document.getElementById('setup-page').hidden");await exportLogs(client);
    for(const action of ['export','device','storage','profile','mod']){await client.evaluate("document.getElementById('home').click()");await client.chooseHome(action);await exportLogs(client);}
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('resume');await client.wait("!document.getElementById('progress-page').hidden");await exportLogs(client);
    await client.evaluate("document.getElementById('home').click()");await client.evaluate(`document.querySelector(${JSON.stringify(select(ids.complete,'result'))}).click()`);await client.wait("!document.getElementById('result-card').hidden");await exportLogs(client);
    assert.equal(server.requests.filter(item=>item.path==='/api/support').length,10);assert.equal(server.requests.filter(item=>item.path==='/api/run').length,0);
    assert.equal(client.events.filter(item=>item.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
  const failed=await fixture({failSupport:true});client=null;try{
    client=await launch(failed);await client.evaluate("document.getElementById('export-support').click()");await client.wait("document.getElementById('support-status').getAttribute('role')==='alert'");
    await client.chooseHome('storage');await delay(250);assert.match(await client.evaluate("document.getElementById('support-status').textContent"),/Protokollexport fehlgeschlagen.*Diagnoseverzeichnis nicht verfügbar/);
    assert.equal(await client.evaluate("document.getElementById('export-support').disabled"),false,'a failed export remains retryable');
  }finally{try{await client?.close();}finally{await failed.close();}}
 });

test('a delayed metadata response cannot restore a deleted main row',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({single:true,holdRuns:true});let client;try{
    client=await launch(server);await client.wait("!document.getElementById('home-runs-refresh').disabled");await client.evaluate("document.getElementById('home-runs-refresh').click()");for(let index=0;index<50&&!server.held();index++)await delay(50);assert.equal(server.held(),true);
    await client.evaluate(`document.querySelector(${JSON.stringify(select(ids.blocked,'delete'))}).click()`);await client.wait("!document.getElementById('storage-card').hidden");
    // A metadata refresh is read-only, so it cannot lock explicit retirement.
    await client.wait("!document.getElementById('storage-run-plan').disabled");
    await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-status').textContent.includes('Run gelöscht')");server.release();await delay(250);
    await client.evaluate("document.getElementById('home').click()");await client.wait("document.getElementById('home-runs').hidden");assert.equal(await client.evaluate("document.querySelectorAll('#home-runs-list .run-row').length"),0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('newer completed run metadata supersedes an older blocked detail view and removes resume/delete actions',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({single:true});let client;try{
    client=await launch(server);await client.chooseHome('resume');await client.wait("!document.getElementById('progress-page').hidden");
    await client.evaluate("document.getElementById('home').click()");server.completeExternally(ids.blocked);
    await client.wait("!document.getElementById('home-runs-refresh').disabled");await client.evaluate("document.getElementById('home-runs-refresh').click()");
    await client.wait(`Boolean(document.querySelector(${JSON.stringify(select(ids.blocked,'result'))}))`);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.blocked,'resume'))}))`),false);
    assert.equal(await client.evaluate(`Boolean(document.querySelector(${JSON.stringify(select(ids.blocked,'delete'))}))`),false);
    assert.equal(await client.evaluate("document.getElementById('storage-run').options.length"),0,'old loaded state cannot supply a deletable storage selector either');
    assert.equal(server.requests.filter(item=>item.path==='/api/run'||item.path==='/api/storage/plan').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });
