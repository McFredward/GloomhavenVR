// Real Chrome over bounded API fixtures. Python integration tests separately
// prove deletion ownership and bank preservation; these tests exercise consent,
// inactive-run selection and browser state after explicit retirement.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='ab'.repeat(16),completed='cd'.repeat(16);
const apk='C:\\Users\\Fixture\\.ghvrq\\build\\builds\\latest\\GloomhavenVR-Quest.apk';
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
async function fixture({status='blocked',failClean=false,lostClean=false,many=false,activeUtility=false,holdStatus=false}={}){
  let current={status:'idle'},latest=session,failNext=failClean,op=0,heldStatus=null;
  let runs=[{session,status,gameRoot:'C:\\Owned game',updated:1791800000},
    ...Array.from({length:many?15:1},(_,index)=>({session:(index+1).toString(16).padStart(32,'0'),status:'cancelled',gameRoot:'D:\\Earlier game',updated:1791700000-index}))];
  const requests=[],retained={session,status,choices:{gameRoot:'C:\\Owned game',provider:'gog',install:false},needsActions:[],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='build'?status:id==='install'?'pending':'complete'})),
    progress:{phase:'build',completed:5,total:7,percent:71}};
  const storage=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:40*1073741824,state:current});
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'stopped-run-token');
        let raw='';for await(const chunk of request)raw+=chunk;
        const body=raw?JSON.parse(raw):undefined;requests.push({path,method:request.method,body});
        const reply=(value,code=200)=>{response.writeHead(code,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover')return reply({schema:1,event:'discovery',capabilities:{runRemoval:true,runOverview:true,browse:false,support:false,logs:false,portablePackages:activeUtility},games:[],unityEditors:[],latestSession:latest,
          recentSessions:[...runs,{session:completed,status:'complete'}],defaultApk:{path:apk}});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/storage/runs')return reply({schema:1,event:'runs',runs:[...runs,{session:completed,status:'complete'}],latestSession:latest});
        if(path==='/api/storage')return reply(storage());
        if(path==='/api/status'){
          const value={schema:1,event:'status',session,state:retained};
          if(holdStatus&&requests.filter(row=>row.path==='/api/status').length>1){heldStatus=()=>reply(value);return;}
          return reply(value);
        }
        if(path==='/api/events')return reply({schema:1,event:'events',events:[],lastEvent:0});
        if(path==='/api/package/status')return reply({schema:1,event:'package',state:{status:'exporting'},defaults:{source:apk}});
        if(path==='/api/storage/plan'){
          const operationId=(++op).toString(16).padStart(32,'0');
          if(body.mode==='duplicates'){current={status:'preview',operationId,plan:{id:'no-duplicates',mode:'duplicates',paths:[],skipped:[],files:0,bytes:0}};return reply(storage());}
          assert.equal(body.mode,'session');assert.ok(runs.some(row=>row.session===body.session));assert.equal(typeof body.removeData,'boolean');
          current={status:'preview',operationId,plan:{id:'retire-run-preview',mode:'session',session:body.session,removeData:body.removeData,eligible:true,
            paths:body.removeData?[{path:'build/projects/exclusive-project',bytes:2*1073741824,files:123}]:[],
            skipped:body.removeData?[{path:'build/cache/shared-import',reason:'shared_or_delivered'},{path:'build/builds/latest',reason:'protected'}]:[],bytes:body.removeData?2*1073741824:0,files:body.removeData?123:0}};
          return reply(storage());
        }
        if(path==='/api/storage/clean'){
          assert.equal(body.planId,'retire-run-preview');
          if(failNext){failNext=false;return reply({schema:1,event:'error',code:'cleanup_changed',message:{de:'Die geplanten Daten wurden verändert.',en:'The planned data changed.'}},400);}
          const removed=current.plan.session,freedBytes=current.plan.bytes,deletedFiles=current.plan.files;
          runs=runs.filter(row=>row.session!==removed);if(latest===removed)latest=completed;
          current={status:'complete',operationId:(++op).toString(16).padStart(32,'0'),result:{sessionsRemoved:[removed],freedBytes,deletedFiles}};
          if(lostClean){response.destroy();return;}return reply(storage());
        }
        throw Error('Unexpected API: '+path);
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not a static asset');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#stopped-run-token',requests,retained,
    hasHeldStatus:()=>Boolean(heldStatus),releaseStatus:()=>{heldStatus?.();heldStatus=null;},
    close:()=>{heldStatus?.();return new Promise(resolve=>server.close(resolve));}};
}
async function launch(server){
  const client=await browser();
  try{
    await client.command('Page.addScriptToEvaluateOnNewDocument',{source:'const nativeTimeout=window.setTimeout.bind(window);window.setTimeout=(fn,ms,...args)=>nativeTimeout(fn,ms===15000?100:ms,...args);'});
    await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-storage')&&!document.getElementById('home-storage').hidden&&!document.getElementById('home-storage').disabled");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    return client;
  }catch(error){await client.close();throw error;}
}
function mutations(server){return server.requests.filter(row=>row.method==='POST');}

for(const status of ['blocked','cancelled'])test('explicit '+status+' run removal retains default APK and clears old browser session',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture({status});let client;
    try{
      client=await launch(server);await client.wait("document.querySelectorAll('#home-runs-list .run-row').length===3");assert.equal(server.requests.filter(row=>row.path==='/api/storage/runs').length,1,'home loads bounded run metadata once');
      await client.chooseHome('resume');await client.wait("!document.getElementById('progress-page').hidden");
      await client.evaluate("document.getElementById('home').click()");await client.chooseHome('remove-run');
      await client.wait("document.getElementById('storage-run').value==='"+session+"'&&!document.getElementById('storage-run-plan').disabled");
      assert.equal(await client.evaluate("document.getElementById('storage-run-keep').checked"),true,'retaining intermediates is the safe initial choice');
      assert.match(await client.evaluate("document.getElementById('storage-run-consequence').textContent"),/nicht mehr fortgesetzt.*Diagnoseprotokolle bleiben erhalten.*APKs.*Signierschlüssel/);
      assert.equal(mutations(server).length,0,'opening removal is not consent to plan or delete');
      await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
      assert.deepEqual(mutations(server).map(row=>row.body),[{mode:'session',session,removeData:false}]);
      assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Run.*entfernen.*0 Dateien/);
      assert.match(await client.evaluate("document.getElementById('storage-confirm-copy').textContent"),/nicht mehr fortgesetzt.*Diagnoseprotokolle bleiben erhalten/);
      assert.equal(mutations(server).filter(row=>row.path==='/api/storage/clean').length,0);
      await client.picture('run-retirement-keep-preview-'+status);
      await client.evaluate("document.getElementById('storage-clean').click()");
      await client.wait("document.getElementById('storage-status').textContent.includes('Run gelöscht')&&!document.getElementById('storage-run-plan').disabled");
      assert.equal(await client.evaluate("document.getElementById('storage-card').hidden"),false,'success stays on storage page');
      assert.equal(await client.evaluate("document.getElementById('progress-page').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('event-log').textContent"),'');
      assert.equal(await client.evaluate("document.getElementById('session-detail').textContent"),'');
      await client.evaluate("document.getElementById('home').click()");
      assert.equal(await client.evaluate("Boolean(document.querySelector('#home-runs-list [data-session=\""+session+"\"]'))"),false,'the retired run never returns as resume');
      assert.equal(await client.evaluate("document.getElementById('home-remove-run')"),null,'singleton retirement tile is removed');
      await client.chooseHome('mod');assert.equal(await client.evaluate("document.getElementById('base-apk').value"),apk,'APK remains selected for compact updates');
      await client.evaluate("document.getElementById('home').click()");await client.chooseHome('profile');
      assert.equal(await client.evaluate("document.getElementById('base-apk').value"),apk,'same APK remains available for profile editing');
      assert.equal(server.requests.filter(row=>row.path==='/api/run'||row.path==='/api/plan').length,0,'no unrelated build or automatic resume');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });

test('all stopped runs are selectable, old intermediate cleanup is optional and skipped shared data is visible',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture({many:true});let client;
    try{
      client=await launch(server);await client.chooseHome('storage');
      await client.wait("document.getElementById('storage-run').options.length===17");
      await client.evaluate("document.getElementById('storage-details').open=true;document.getElementById('storage-plan-duplicates').click()");
      await client.wait("document.getElementById('storage-status').textContent.includes('Zwischendaten angehaltener Runs bleiben erhalten')");
      assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true,'empty duplicate preview never auto-retires a run');
      const older='f'.padStart(32,'0');
      await client.evaluate("document.getElementById('storage-run').value='"+older+"';document.getElementById('storage-run').dispatchEvent(new Event('change'));document.getElementById('storage-run-remove-data').click();document.getElementById('storage-run-plan').click()");
      await client.wait("!document.getElementById('storage-confirmation').hidden");
      assert.deepEqual(mutations(server).at(-1).body,{mode:'session',session:older,removeData:true});
      assert.match(await client.evaluate("document.getElementById('storage-paths').textContent"),/exclusive-project.*2 GiB.*123 Dateien/);
      assert.match(await client.evaluate("document.getElementById('storage-skipped').textContent"),/shared-import.*von anderen Runs genutzt.*latest.*geschützte Daten/);
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.match(await client.evaluate("document.getElementById('storage-skipped').textContent"),/used by other runs.*protected data/);
      await client.picture('run-cleanup-optional-preview-en');
      await client.evaluate("document.getElementById('storage-dismiss').click()");
      assert.equal(mutations(server).filter(row=>row.path==='/api/storage/clean').length,0,'dismissing a preview does not authorize retirement');
      await client.evaluate("document.getElementById('storage-run').value='"+session+"';document.getElementById('storage-run').dispatchEvent(new Event('change'))");
      assert.equal(await client.evaluate("document.getElementById('storage-run-keep').checked"),true,'a different run resets the optional cleanup decision');
      const reads=server.requests.filter(row=>row.path==='/api/storage/runs').length;await delay(350);
      assert.equal(server.requests.filter(row=>row.path==='/api/storage/runs').length,reads,'storage polling does not repeatedly enumerate runs');
      await client.evaluate("document.getElementById('storage-runs-refresh').click()");await client.wait("!document.getElementById('storage-runs-refresh').disabled");
      assert.equal(server.requests.filter(row=>row.path==='/api/storage/runs').length,reads+1);
      await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
      assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true,'run identifiers and paths remain responsive');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });

for(const lostClean of [false,true])test('run removal '+(lostClean?'lost response reconciles successful retirement':'failure remains visible and requires a fresh confirmed preview'),
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture({failClean:!lostClean,lostClean});let client;
    try{
      client=await launch(server);await client.chooseHome('remove-run');await client.wait("!document.getElementById('storage-run-plan').disabled");
      await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
      await client.evaluate("document.getElementById('storage-clean').click()");
      if(!lostClean){
        await client.wait("document.getElementById('storage-status').getAttribute('role')==='alert'");await delay(350);
        assert.match(await client.evaluate("document.getElementById('storage-status').textContent"),/Bereinigung fehlgeschlagen.*Daten wurden verändert/);
        assert.equal(await client.evaluate("document.getElementById('storage-confirmation').hidden"),true);
        assert.equal(await client.evaluate("Boolean(document.querySelector('#home-runs-list [data-session=\""+session+"\"] [data-run-action=resume]'))"),true,'failed retirement preserves the resumable record');
        await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
        assert.equal(mutations(server).filter(row=>row.path==='/api/storage/clean').length,1,'retry preview requires another final confirmation');
        await client.evaluate("document.getElementById('storage-clean').click()");
      }
      await client.wait("document.getElementById('storage-status').textContent.includes('Run gelöscht')");
      assert.equal(await client.evaluate("document.getElementById('storage-status').getAttribute('role')"),'status');
      await client.evaluate("document.getElementById('home').click()");await client.wait("!document.querySelector('#home-runs-list [data-session=\""+session+"\"]')");
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });

test('an old in-flight build status cannot revive a retired run',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture({holdStatus:true});let client;
    try{
      client=await launch(server);await client.chooseHome('resume');await client.wait("!document.getElementById('progress-page').hidden");
      for(let index=0;index<50&&!server.hasHeldStatus();index++)await delay(50);
      assert.equal(server.hasHeldStatus(),true,'fixture holds a real subsequent status request');
      await client.evaluate("document.getElementById('home').click()");await client.chooseHome('remove-run');
      await client.wait("!document.getElementById('storage-run-plan').disabled");
      await client.evaluate("document.getElementById('storage-run-plan').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
      await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-status').textContent.includes('Run gelöscht')");
      server.releaseStatus();await delay(250);
      assert.equal(await client.evaluate("document.getElementById('storage-card').hidden"),false);
      await client.evaluate("document.getElementById('home').click()");await client.wait("!document.querySelector('#home-runs-list [data-session=\""+session+"\"]')");
      assert.equal(await client.evaluate("document.getElementById('session-detail').textContent"),'');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });

for(const options of [{status:'running'},{status:'complete'},{activeUtility:true}])test('run retirement respects active ownership and permits completed history for '+JSON.stringify(options),
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture(options);let client;
    try{
      client=await launch(server);
      await client.wait("document.querySelectorAll('#home-runs-list .run-row').length===3");
      if(options.activeUtility)await client.wait("document.querySelector('#home-runs-list [data-session=\""+session+"\"] [data-run-action=delete]').disabled");
      else if(options.status==='complete')assert.equal(await client.evaluate("document.querySelector('#home-runs-list [data-session=\""+session+"\"] [data-run-action=delete]').disabled"),false);
      else assert.equal(await client.evaluate("document.querySelector('#home-runs-list [data-session=\""+session+"\"] [data-run-action=delete]')"),null);
      await client.chooseHome('storage');await client.wait("document.getElementById('storage-run').options.length>=1");
      assert.equal(await client.evaluate("[...document.getElementById('storage-run').options].some(row=>row.value==='"+session+"')"),options.activeUtility===true||options.status==='complete','active builds stay protected while completed history is eligible');
      if(options.activeUtility)assert.equal(await client.evaluate("document.getElementById('storage-run-plan').disabled"),true,'active export remains protected');
      assert.equal(mutations(server).length,0);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });
