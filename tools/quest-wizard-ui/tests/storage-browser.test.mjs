// Native DOM test over a declared asynchronous storage fixture. It does not
// exercise deletion itself; real owned-workspace safety belongs to backend tests.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
const session='storage-owned-fixture';
for(const completed of [false,true])test('native browser storage: explicit preview/deletion before build, completed cache option '+completed,
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  let storageState={status:'idle'},storagePolls=0,cleanCalls=0,requestedMode=null,failNext=false;
  const storage=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:44*1073741824,state:storageState});
  const server=createServer(async(request,response)=>{
    try{
      const path=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'storage-fixture-token');
        let body='';for await(const data of request)body+=data;
        const input=body?JSON.parse(body):{},reply=value=>{response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover')return reply({schema:1,event:'discovery',capabilities:{browse:false,support:false,logs:false},games:[],unityEditors:[],...(completed?{latestSession:session}:{})});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/status')return reply({schema:1,event:'status',session,state:{session,status:'complete',needsActions:[],choices:{gameRoot:'C:\\OwnedGame',provider:'steam',install:false},stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:'complete',details:id==='install'?{requested:false}:{}})),result:{apk:'C:\\Users\\Fixture\\.ghvrq\\build\\result.apk'}}});
        if(path==='/api/events')return reply({schema:1,event:'events',session,events:[],lastEvent:0});
        if(path==='/api/storage/plan'){
          if(failNext){storageState={status:'failed',error:{code:'storage_changed',message:{de:'Die geplante Datei wurde verändert.',en:'A planned file changed.'}}};return reply(storage());}
          requestedMode=input.mode;storagePolls=0;storageState={status:'planning',done:0,total:4};return reply(storage());
        }
        if(path==='/api/storage/clean'){
          assert.equal(input.planId,'displayed-only-plan');cleanCalls++;storagePolls=0;storageState={status:'deleting',done:0,total:4};return reply(storage());
        }
        if(path==='/api/storage'){
          if(['planning','deleting'].includes(storageState.status)){
            storagePolls++;
            if(storagePolls===1)storageState={...storageState,done:1};
            else if(storageState.status==='planning')storageState={status:'preview',plan:{id:'displayed-only-plan',mode:requestedMode,paths:[{path:'C:\\Users\\Fixture\\.ghvrq\\build\\unused-copy',bytes:2*1073741824,files:4}],bytes:2*1073741824,files:4,exact:true}};
            else storageState={status:'complete',result:{freedBytes:2*1073741824,deletedFiles:4}};
          }
          return reply(storage());
        }
        response.writeHead(404);return response.end();
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#storage-fixture-token'});
    await client.wait("document.getElementById('storage-card')&&!document.getElementById('storage-card').hidden&&!document.getElementById('storage-plan-duplicates').disabled");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.equal(await client.evaluate("document.getElementById('storage-root').textContent"),'C:\\Users\\Fixture\\.ghvrq');
    assert.match(await client.evaluate("document.getElementById('storage-free').textContent"),/44 GiB/);
    if(completed)await client.wait("!document.getElementById('storage-plan-cache').hidden");
    else assert.equal(await client.evaluate("document.getElementById('storage-plan-cache').hidden"),true,'no cache deletion before a completed build');
    assert.equal(cleanCalls,0,'opening or completing a build never deletes automatically');
    await client.evaluate("document.getElementById('storage-plan-duplicates').click()");
    await client.wait("document.getElementById('storage-progress').value===25");
    assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),true);
    assert.equal(await client.evaluate("document.getElementById('primary').disabled"),true,'no build starts during cleanup work');
    await client.wait("!document.getElementById('storage-confirmation').hidden");
    assert.equal(requestedMode,'duplicates');assert.equal(cleanCalls,0,'a preview is not consent to delete');
    assert.match(await client.evaluate("document.getElementById('storage-paths').textContent"),/unused-copy.*2 GiB.*4 Dateien/);
    assert.equal(await client.evaluate("document.getElementById('storage-cache-consequence').hidden"),true);
    await client.picture('storage-duplicates-preview-de-'+completed);
    await client.evaluate("document.getElementById('storage-clean').click()");
    await client.wait("document.getElementById('storage-progress').value===25");
    assert.equal(cleanCalls,1);assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true,'cannot reuse the stale preview during deletion');
    await client.wait("document.getElementById('storage-status').textContent.includes('2 GiB freigegeben')");
    assert.equal(await client.evaluate("document.getElementById('storage-plan-duplicates').disabled"),false);
    if(completed){
      await client.evaluate("document.getElementById('storage-plan-cache').click()");
      await client.wait("!document.getElementById('storage-confirmation').hidden");
      assert.equal(requestedMode,'build-cache');assert.equal(cleanCalls,1);
      assert.equal(await client.evaluate("document.getElementById('storage-cache-consequence').hidden"),false);
      assert.match(await client.evaluate("document.getElementById('storage-cache-consequence').textContent"),/Signierschlüssel.*erneut Konvertierung und Unity-Import.*APK-Updates/);
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.match(await client.evaluate("document.getElementById('storage-cache-consequence').textContent"),/signing keys.*conversion and Unity import again/);
      await client.picture('storage-build-cache-preview-en');
      await client.evaluate("document.getElementById('storage-dismiss').click()");
      assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true);
      assert.equal(cleanCalls,1,'closing the cache preview never deletes');
    }
    failNext=true;await client.evaluate("document.getElementById('storage-plan-duplicates').click()");
    await client.wait("document.getElementById('storage-status').getAttribute('role')==='alert'");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/fehlgeschlagen.*Datei wurde verändert/);
    assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true);
    assert.equal(cleanCalls,1,'a failed preview never retains a deletable stale plan');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true,'long workspace path remains responsive');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
 });
