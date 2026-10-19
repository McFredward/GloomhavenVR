// Browser contracts for safe one-click cleanup and an explicit manual deletion.
// Filesystem retention/deletion safety is independently tested by the backend.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {extname,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),GiB=1073741824,session='ab'.repeat(16);
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
const impact={schema:1,status:'preserved',messages:{de:'Daten für schnelle Updates bleiben erhalten.',en:'Data for fast updates is retained.'},protectedPaths:['build/updates/code-projects','build/cache/quest-movies']};
async function fixture({fail=false,reject=false,hold=false,run=false}={}){
  const calls=[];let current={status:'idle'},operation=0,polls=0;
  const value=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:9*GiB,canDeleteBuildCache:true,state:current,inventory:{status:'stale',totalBytes:190*GiB,completedAt:1791800000,fastUpdateBytes:25*GiB,categories:[]}});
  const server=createServer(async(request,response)=>{try{
    const path=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
    if(path.startsWith('/api/')){
      assert.equal(request.headers['x-quest-token'],'cleanup-fixture-token');let text='';for await(const part of request)text+=part;
      const body=text?JSON.parse(text):null;calls.push({path,body});
      const reply=data=>{response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(data));};
      if(path==='/api/discover')return reply({schema:1,event:'discovery',games:[],unityEditors:[],capabilities:{autoStorageCleanup:true,storageInventory:true,storageCancel:true,runOverview:true,runRemoval:true,updateHistoryCleanup:true},...(run?{recentSessions:[{session,status:'cancelled'}]}:{})});
      if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
      if(path==='/api/storage/runs')return reply({schema:1,event:'runs',runs:run?[{session,status:'cancelled',created:1791800000,choices:{gameRoot:'C:\\Owned Game',mode:'build'}}]:[],retiredRuns:[]});
      if(path==='/api/storage/auto'){
        assert.deepEqual(body,{});polls=0;
        if(reject){reject=false;response.writeHead(400,{'Content-Type':'application/json'});return response.end(JSON.stringify({schema:1,event:'error',message:{de:'Die Bereinigung ist noch gesperrt.',en:'Cleanup is still locked.'}}));}
        current={status:'planning',automatic:true,operationId:String(++operation).padStart(32,'0'),selection:{mode:'safe'},phase:'cleanup-discover'};
        if(fail){fail=false;current={...current,status:'failed',error:{message:{de:'Eine Datei wird noch verwendet.',en:'A file is still in use.'}}};}
        return reply(value());
      }
      if(path==='/api/storage/plan'){
        assert.ok(['duplicates','session'].includes(body.mode));
        current={status:'preview',operationId:String(++operation).padStart(32,'0'),selection:body,plan:{id:'manual-displayed-plan',mode:body.mode,session:body.session,eligible:true,files:60,bytes:2*GiB,fastUpdateImpact:impact,paths:Array.from({length:60},(_,i)=>({path:'build/cache/obsolete-copy-'+i,bytes:100,files:1}))}};
        return reply(value());
      }
      if(path==='/api/storage/clean'){
        assert.equal(body.planId,'manual-displayed-plan');current={status:'complete',result:{freedBytes:2*GiB,deletedFiles:60}};return reply(value());
      }
      if(path==='/api/storage/cancel'){
        assert.equal(body.operationId,current.operationId);current={...current,status:'cancelled',result:{freedBytes:128,deletedFiles:1}};return reply(value());
      }
      if(path==='/api/storage'){
        if(current.automatic&&!hold&&['planning','deleting'].includes(current.status)){
          polls++;
          if(polls===2)current={...current,status:'deleting',phase:'cleanup-delete',unit:'entries',done:1,total:4,files:1};
          if(polls===4)current={...current,status:'complete',result:{freedBytes:3*GiB,deletedFiles:4,fastUpdateImpact:impact}};
        }
        return reply(value());
      }
      throw Error('Unexpected request '+path);
    }
    const file=resolve(root,'.'+(path==='/'?'/index.html':path));assert.ok(file.startsWith(root)&&mime[extname(file)]);
    response.writeHead(200,{'Content-Type':mime[extname(file)]});response.end(await readFile(file));
  }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#cleanup-fixture-token',calls,release:()=>{hold=false;},close:()=>new Promise(resolve=>server.close(resolve))};
}
async function launch(server,language='de'){
  const client=await browser();try{
    await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===750||ms===15000?100:ms,...args);'});
    await client.command('Page.navigate',{url:server.url});await client.chooseHome('storage');
    await client.evaluate('document.querySelector("[data-language='+language+']").click()');return client;
  }catch(error){await client.close();throw error;}
}
const shown=id=>`(()=>{const e=document.getElementById(${JSON.stringify(id)}),r=e.getBoundingClientRect();return !!e.getClientRects().length&&r.top>=0&&r.bottom<=innerHeight})()`;

test('automatic cleanup sends one authenticated empty request and design preview cannot delete files',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'storage',state:{status:'planning'}})};});
  await api.storageAuto();assert.equal(calls.length,1);assert.equal(calls[0].url,'http://127.0.0.1:1234/api/storage/auto');
  assert.equal(calls[0].options.method,'POST');assert.equal(calls[0].options.headers['X-Quest-Token'],'token');assert.deepEqual(JSON.parse(calls[0].options.body),{});
  const preview=new PreviewApi(),result=await preview.storageAuto();assert.equal(result.state.status,'complete');assert.equal(result.state.result.deletedFiles,0);
});

for(const language of ['de','en'])test('one click cleans automatically, retains fast updates and never starts a disk inventory '+language,{skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({hold:true});let client;try{
    client=await launch(server,language);assert.equal(await client.evaluate("document.getElementById('storage-details').open"),false);
    assert.equal(await client.evaluate(shown('storage-auto')),true);await client.picture('storage-automatic-idle-'+language);
    await client.evaluate("document.getElementById('storage-auto').click()");await client.wait("!document.getElementById('storage-cancel').disabled");
    assert.equal(await client.evaluate("document.getElementById('storage-auto').disabled"),true);assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true);
    server.release();await client.wait("document.getElementById('storage-operation').dataset.status==='complete'");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/3 GiB/);
    assert.doesNotMatch(await client.evaluate("document.getElementById('storage-status').textContent"),/Dateien entfernen|Removing files/,'completion does not retain the old live phase');
    assert.match(await client.evaluate("document.getElementById('storage-fast-update-impact').textContent"),/schnelle Updates|fast updates/);
    assert.equal(await client.evaluate(shown('storage-operation-title')),true);await client.picture('storage-automatic-complete-'+language);
    assert.equal(server.calls.filter(row=>row.path==='/api/storage/auto').length,1);
    assert.equal(server.calls.filter(row=>['/api/storage/clean','/api/storage/plan','/api/storage/inventory'].includes(row.path)).length,0);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
});

test('automatic cleanup exposes failure and cancellation with the correct automatic retry',{skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({fail:true,hold:true});let client;try{
    client=await launch(server);await client.evaluate("document.getElementById('storage-auto').click()");await client.wait("document.getElementById('storage-status').getAttribute('role')==='alert'");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Datei wird noch verwendet/);
    assert.match(await client.evaluate("document.getElementById('storage-retry').textContent"),/automatisch/);
    await client.evaluate("document.getElementById('storage-retry').click()");await client.wait("!document.getElementById('storage-cancel').disabled");
    await client.evaluate("document.getElementById('storage-cancel').click()");await client.wait("document.getElementById('storage-operation').dataset.status==='cancelled'");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/128 B freigegeben/);
    await client.picture('storage-automatic-stopped-de');await client.evaluate("document.getElementById('storage-retry').click()");
    await client.wait("!document.getElementById('storage-cancel').disabled");assert.equal(server.calls.filter(row=>row.path==='/api/storage/auto').length,3);
    assert.equal(server.calls.some(row=>row.path==='/api/storage/plan'),false);await client.evaluate("document.getElementById('storage-cancel').click()");
  }finally{try{await client?.close();}finally{await server.close();}}
});

test('a rejected automatic request retains its explanation and retry after idle status polls',{skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({reject:true,hold:true});let client;try{
    client=await launch(server);await client.evaluate("document.getElementById('storage-auto').click()");
    await client.wait("document.getElementById('storage-status').textContent.includes('noch gesperrt')");await delay(350);
    assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'alert');
    assert.equal(await client.evaluate("document.getElementById('storage-retry').hidden"),false);
    await client.evaluate("document.getElementById('storage-retry').click()");await client.wait("!document.getElementById('storage-cancel').disabled");
    assert.equal(server.calls.filter(row=>row.path==='/api/storage/auto').length,2);assert.equal(server.calls.some(row=>row.path==='/api/storage/plan'),false);
    await client.evaluate("document.getElementById('storage-cancel').click()");
  }finally{try{await client?.close();}finally{await server.close();}}
});

for(const mobile of [false,true])test('manual preview keeps confirmation visible above long file lists, even with advanced tools closed '+mobile,{skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({run:true});let client;try{
    client=await launch(server,mobile?'en':'de');
    if(mobile)await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('remove-run');
    await client.wait("!document.getElementById('storage-run-plan').disabled");await client.evaluate("document.getElementById('storage-run-plan').click()");
    await client.wait("!document.getElementById('storage-confirmation').hidden");
    await client.evaluate("document.getElementById('storage-details').open=false");
    assert.equal(await client.evaluate(shown('storage-clean')),true,'confirmation stays in the viewport outside both collapsed details');
    assert.match(await client.evaluate("document.getElementById('storage-confirm-summary').textContent"),/60.*2 GiB/);
    assert.equal(await client.evaluate("document.activeElement.id"),'storage-operation-title');
    assert.equal(await client.evaluate("document.getElementById('storage-preview-details').open"),false);
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
    assert.equal(server.calls.some(row=>row.path==='/api/storage/clean'),false,'preview alone never deletes');
    await client.picture('storage-manual-confirm-'+(mobile?'mobile-en':'desktop-de'));
    await client.evaluate("document.getElementById('storage-dismiss').click()");await delay(250);
    assert.equal(await client.evaluate("document.getElementById('storage-operation').hidden"),true,'a dismissed preview stays closed across backend status polls');
    assert.equal(server.calls.some(row=>row.path==='/api/storage/clean'),false);
    await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-operation').dataset.status==='complete'");
    assert.equal(server.calls.filter(row=>row.path==='/api/storage/clean').length,1);
  }finally{try{await client?.close();}finally{await server.close();}}
});
