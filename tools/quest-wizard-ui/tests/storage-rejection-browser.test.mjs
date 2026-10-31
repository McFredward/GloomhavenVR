// Real DOM over a bounded HTTP fixture. Backend tests prove which owned files
// are deleted; this test proves that failed cleanup requests remain actionable.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='cleanup-retained-owner';
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
const rejection={schema:1,event:'error',code:'already_running',message:{
  de:'Den laufenden Vorgang vor der Speicherbereinigung anhalten.',
  en:'Stop the active operation before managing storage.'}};
async function waitFor(predicate){for(let index=0;index<80;index++){if(predicate())return;await delay(50);}assert.fail('Fixture condition did not complete');}

async function fixture({failAt,transport=false,status='blocked',acceptedResponseLost=false}){
  let storageState={status:'idle'},inventory={status:'idle'},failed=false,allowRetry=false,storagePolls=0,planPolls=0,holdNext=false,heldPoll=null,operation=0;
  const requests=[],retained={session,status,choices:{gameRoot:'C:\\Owned game',provider:'gog',install:false,acceptUnityTerms:true},needsActions:[],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='build'?status:id==='install'?'pending':'complete',progress:id==='build'?{stagePercent:78.833}:undefined})),
    progress:{phase:'build',completed:5,total:7,percent:78.833}};
  const storage=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:2*1073741824,state:storageState,inventory});
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'cleanup-fixture-token');
        let raw='';for await(const chunk of request)raw+=chunk;
        const body=raw?JSON.parse(raw):undefined;requests.push({path,body});
        const reply=(value,code=200)=>{response.writeHead(code,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover')return reply({schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status}],
          games:[],unityEditors:[],capabilities:{browse:false,support:false,logs:false,storageInventory:true}});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/status')return reply({schema:1,event:'status',state:retained});
        if(path==='/api/events')return reply({schema:1,event:'events',events:[],lastEvent:0});
        if(path==='/api/storage/inventory'){inventory={status:'scanning',files:12345};return reply(storage());}
        if(path==='/api/storage'){
          storagePolls++;
          if(holdNext){holdNext=false;const snapshot=storage();heldPoll=()=>reply(snapshot);return;}
          if(storageState.status==='planning'&&++planPolls>2){
            inventory={status:'cancelled',files:12345};
            storageState={status:'preview',operationId:storageState.operationId,plan:{id:'fresh-owned-plan',mode:'duplicates',paths:[{path:'build/unused-copy',bytes:1073741824,files:3}],bytes:1073741824,files:3,exact:true}};
          }
          return reply(storage());
        }
        if(['/api/storage/plan','/api/storage/clean'].includes(path)){
          if(path.endsWith('/'+failAt)&&(!failed||(transport||acceptedResponseLost)&&!allowRetry)){
            if(acceptedResponseLost){
              if(!failed)storageState={status:'complete',operationId:(++operation).toString(16).padStart(32,'0'),result:{deletedFiles:3,freedBytes:1073741824}};
              failed=true;response.destroy();return;
            }
            failed=true;storageState={status:'idle',operationId:storageState.operationId};
            if(transport){response.destroy();return;}
            return reply(rejection,400);
          }
          const operationId=(++operation).toString(16).padStart(32,'0');
          if(path.endsWith('/plan')){assert.equal(body.mode,'duplicates');planPolls=0;storageState={status:'planning',phase:'inventory-pausing',operationId};}
          else{assert.equal(body.planId,'fresh-owned-plan');storageState={status:'complete',operationId,result:{deletedFiles:3,freedBytes:1073741824}};}
          return reply(storage());
        }
        throw Error('Unexpected API: '+path);
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not a static asset');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#cleanup-fixture-token',requests,retained,
    polls:()=>storagePolls,allowRetry:()=>{allowRetry=true;},holdIdlePoll:()=>{holdNext=true;},hasHeldPoll:()=>Boolean(heldPoll),
    publishOldJob:()=>{storageState={status:'deleting',operationId:storageState.operationId};},closeOldJob:()=>{storageState={status:'idle',operationId:storageState.operationId};},
    releasePoll:()=>{heldPoll?.();heldPoll=null;},close:()=>{heldPoll?.();return new Promise(resolve=>server.close(resolve));}};
}

for(const options of [{failAt:'plan',status:'blocked'},{failAt:'clean',status:'cancelled'},{failAt:'plan',transport:true,status:'blocked'}]){
  test('cleanup '+options.failAt+' rejection survives idle and build polls; '+options.status+(options.transport?' transport':''),
    {skip:!existsSync(chrome),timeout:30000},async()=>{
      const server=await fixture(options);let client;
      try{
        client=await browser();
        // Shorten only the idle storage/gallery refresh for the regression.
        // Production job percentages and real active-work timings are untouched.
        await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===15000?100:ms,...args);'});
        await client.command('Page.navigate',{url:server.url});await client.chooseHome('resume');
        await client.wait("!document.getElementById('progress-page').hidden");
        assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'78.833');
        await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');
        await client.evaluate("document.querySelector('[data-language=de]').click()");
        assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),false,'saved blocked/cancelled work does not lock cleanup');
        assert.equal(server.requests.filter(row=>row.path==='/api/storage/inventory').length,0,'launch never starts a hidden scan');
        await client.evaluate("document.getElementById('storage-refresh').click()");
        await client.wait("document.getElementById('storage-inventory-status').textContent.includes('12.345')");
        assert.match(await client.evaluate("document.getElementById('storage-inventory-status').textContent"),/12.345 Dateien/);
        await client.evaluate("document.getElementById('storage-plan-duplicates').click()");
        if(options.failAt==='clean'){
          await client.wait("document.getElementById('storage-status').textContent.includes('Belegungsermittlung')");
          await client.wait("!document.getElementById('storage-confirmation').hidden");
          await client.evaluate("document.getElementById('storage-clean').click()");
        }
        await client.wait("document.getElementById('storage-status').getAttribute('role')==='alert'");
        const initialPolls=server.polls();await waitFor(()=>server.polls()>=initialPolls+3);
        const expected=options.transport?/Bereinigung fehlgeschlagen.*[Vv]erbindung/:/Bereinigung fehlgeschlagen.*Den laufenden Vorgang/;
        assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),expected,'idle GET cannot erase a rejected request');
        assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true,'failed request cannot reuse a stale deletion plan');
        assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),false,'the rejected request releases its own lock');
        if(options.failAt==='clean'){
          server.publishOldJob();await client.wait("document.getElementById('storage-plan-duplicates').disabled");
          assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),expected,'the same operation cannot erase a newer request rejection');
          server.closeOldJob();await client.wait("!document.getElementById('storage-plan-duplicates').disabled");
        }
        await client.evaluate("document.getElementById('home').click()");await client.chooseHome('resume');
        await client.wait("!document.getElementById('progress-page').hidden");
        assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'78.833','cleanup cannot reset retained build progress');
        await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');
        assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),expected,'unrelated build status preserves cleanup cause');
        await client.picture('storage-rejected-'+options.failAt+(options.transport?'-network':'-http400'));
        await client.evaluate("document.querySelector('[data-language=en]').click()");
        assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),options.transport?/Cleanup failed.*[Cc]onnect/:/Cleanup failed.*Stop the active operation/);
        const stalePoll=options.failAt==='plan'&&!options.transport;
        if(stalePoll){server.holdIdlePoll();await waitFor(server.hasHeldPoll);}
        server.allowRetry();await client.evaluate("document.getElementById('storage-plan-duplicates').click()");
        await client.wait("document.getElementById('storage-status').textContent.includes('Pausing the disk-usage scan')");
        assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'status','an explicit retry clears the old failure');
        assert.equal(await client.evaluate("document.getElementById('storage-progress').hasAttribute('value')"),false,'pausing a scanner invents no percentage');
        if(stalePoll){
          server.releasePoll();await delay(150);
          assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Pausing the disk-usage scan/,'an earlier idle response cannot erase the new active request');
          assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),true,'the stale response cannot unlock active cleanup');
        }
        await client.wait("!document.getElementById('storage-confirmation').hidden");
        await client.evaluate("document.getElementById('storage-clean').click()");
        await client.wait("document.getElementById('storage-status').textContent.includes('Cleanup complete')");
        assert.equal(server.requests.filter(row=>row.path==='/api/run'||row.path==='/api/plan').length,0,'cleanup never restarts a saved build');
        if(stalePoll){
          server.retained.status='running';server.retained.stages.find(row=>row.id==='build').status='running';
          await client.evaluate("document.getElementById('home').click()");await client.chooseHome('resume');
          await client.wait("!document.getElementById('progress-page').hidden");
          await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');
          await client.wait("document.getElementById('storage-plan-duplicates').disabled");
          assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/builder is working on.*Build Quest version/,'a genuinely active job is identified instead of a saved blocked run');
          server.retained.status='blocked';server.retained.stages.find(row=>row.id==='build').status='blocked';
          await client.wait("!document.getElementById('storage-plan-duplicates').disabled");
        }
        assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
      }finally{try{await client?.close();}finally{await server.close();}}
    });
}

test('a lost cleanup response is reconciled by the new completed operation identity',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture({failAt:'clean',acceptedResponseLost:true,status:'cancelled'});let client;
    try{
      client=await browser();
      await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===15000?100:ms,...args);'});
      await client.command('Page.navigate',{url:server.url});await client.chooseHome('storage');
      await client.evaluate("document.querySelector('[data-language=en]').click();document.getElementById('storage-plan-duplicates').click()");
      await client.wait("!document.getElementById('storage-confirmation').hidden");
      await client.evaluate("document.getElementById('storage-clean').click()");
      await client.wait("document.getElementById('storage-status').textContent.includes('Cleanup complete')");
      assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'status','a verified new completion resolves the client transport failure');
      assert.doesNotMatch(await client.evaluate("document.getElementById('storage-status').textContent"),/failed/i);
      assert.ok(server.requests.some(row=>row.path==='/api/storage/clean'));
      assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),false);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });
