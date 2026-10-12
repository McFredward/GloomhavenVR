// Real browser over a declared asynchronous device/storage fixture. ADB execution
// and cleanup safety are validated by their independent backend suites.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='home-retained-build';
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
async function fixture(contentDecision=false){
  const requests=[],captureId='b'.repeat(32);let device={status:'idle',devices:[]},inventory={status:'idle'},storageState={status:'idle'},polls=0;
  const retained={session,status:'cancelled',choices:{gameRoot:'C:\\Owned game',provider:'gog',install:false,acceptUnityTerms:true},needsActions:[],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='install'?'pending':id==='build'?'cancelled':'complete',progress:id==='build'?{stagePercent:88}:undefined})),progress:{phase:'build',completed:5,total:7},result:{apk:'C:\\Retained\\Quest.apk'}};
  if(contentDecision){
    const nonce='c'.repeat(32),transaction='d'.repeat(32),inputKey='e'.repeat(64),apkSha256='f'.repeat(64),deviceSerial='quest-fixture';
    retained.status='blocked';retained.stages[5].status='complete';retained.stages[5].details={inputKey,apkSha256};retained.stages[6].status='blocked';retained.stages[6].attempts=3;
    retained.installationAction={session,nonce,transaction,inputKey,apkSha256,deviceSerial,kind:'content-reinstall',reason:'native-rename-unavailable',attempt:3};
    retained.needsActions=[{stage:'install',code:'quest_signing_content_reinstall_required',parameters:{failureStage:'install',saveDataProtected:true,requiresContentReinstall:true,contentReinstallReason:'native-rename-unavailable',contentReinstallTransaction:transaction,contentReinstallDeviceSerial:deviceSerial,installationActionNonce:nonce,estimatedBytes:10929781172}}];
  }
  const deviceReply=()=>({schema:1,event:'device',state:device}),storageReply=()=>({schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',freeBytes:40*1073741824,state:storageState,inventory,canDeleteBuildCache:true});
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost'),path=url.pathname;
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'home-fixture-token');let raw='';for await(const chunk of request)raw+=chunk;
        const body=raw?JSON.parse(raw):undefined;requests.push({path,body});
        const reply=value=>{response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover')return reply({schema:1,event:'discovery',latestSession:session,
          defaultApk:{path:'C:\\Retained\\Quest.apk',modBuild:671,source:'latest-build'},
          games:[{id:'owned',gameRoot:'C:\\Owned game',provider:'gog'}],unityEditors:[],capabilities:{browse:false,logs:false,support:false,deviceTools:true,storageInventory:true}});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/status')return reply({schema:1,event:'status',session,state:retained});
        if(path==='/api/install/content-reinstall'){
          assert.equal(contentDecision,true);assert.deepEqual(body,{session,nonce:'c'.repeat(32)});
          retained.installationContentReinstallApproval={...retained.installationAction};return reply({schema:1,event:'status',session,state:retained});
        }
        if(path==='/api/run'){
          assert.equal(contentDecision,true);assert.equal(body.session,session);
          if(retained.installationContentReinstallApproval){retained.status='running';retained.stages[6].status='running';retained.needsActions=[];}
          return reply({schema:1,event:'started',session});
        }
        if(path==='/api/events')return reply({schema:1,event:'events',events:[],lastEvent:0});
        if(path==='/api/storage/inventory'){inventory={status:'complete',totalBytes:6*1073741824+6679,completedAt:1791720000,files:31,categories:[{id:'unity-caches',bytes:6*1073741824,files:30},{id:'signing-backups',bytes:6679,files:1}]};return reply(storageReply());}
        if(path==='/api/storage')return reply(storageReply());
        if(path==='/api/device/connect'){assert.deepEqual(body,{host:'192.168.0.5:5555',setupWireless:true,serial:''});device={status:'waiting',message:{de:'USB-Debugging in der Brille bestätigen.',en:'Authorize USB debugging in the headset.'},devices:[{serial:'usb-fixture',state:'unauthorized',kind:'usb',model:'Quest_3'}]};return reply(deviceReply());}
        if(path==='/api/device/capture'&&request.method==='POST'){assert.deepEqual(body,{host:'192.168.0.5:5555',serial:''});device={status:'capturing',progress:{done:1,total:4,percent:25,label:'logcat'}};polls=0;return reply(deviceReply());}
        if(path==='/api/device/status'){if(device.status==='capturing'&&++polls>1)device={status:'complete',message:{de:'Testbericht gespeichert.',en:'Test report saved.'},capture:{id:captureId,name:'quest-capture-fixture.zip',path:'C:\\Users\\Fixture\\.ghvrq\\captures\\quest-capture-fixture.zip'}};return reply(deviceReply());}
        if(path==='/api/device/capture'&&request.method==='GET'){assert.equal(url.searchParams.get('id'),captureId);response.writeHead(200,{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="quest-capture-fixture.zip"'});return response.end('fixture ZIP');}
        if(path==='/api/device/cancel'){device={status:'cancelled'};return reply(deviceReply());}
        if(path==='/api/storage/plan'){assert.equal(body.mode,'build-cache');storageState={status:'preview',plan:{id:'scoped-plan',mode:'build-cache',files:30,bytes:6*1073741824,paths:[{path:'build/projects/fixture/Library',files:30,bytes:6*1073741824}]}};return reply(storageReply());}
        if(path==='/api/storage/clean'){assert.equal(body.planId,'scoped-plan');storageState={status:'complete',result:{deletedFiles:30,freedBytes:6*1073741824}};return reply(storageReply());}
        response.writeHead(404);return response.end();
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('not static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#home-fixture-token',requests,retained,close:()=>new Promise(resolve=>server.close(resolve))};
}

test('home shows every APK action and opens retained progress only on an explicit resume click',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-page')&&!document.getElementById('home-page').hidden&&!document.getElementById('home-build').disabled");
    assert.equal(server.requests.some(row=>row.path==='/api/status'),false,'home does not reopen or rerun an interrupted job');
    assert.equal(server.requests.some(row=>['/api/run','/api/plan'].includes(row.path)),false);
    assert.equal(await client.evaluate("document.getElementById('progress-page').hidden"),true);
    await client.chooseHome('resume');await client.wait("!document.getElementById('progress-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('game-root').value"),'C:\\Owned game');
    assert.equal(await client.evaluate("document.getElementById('provider').value"),'gog');
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'88');
    for(const [action,mode] of [['mod','update-mod'],['profile','update-profile'],['build','build']]){
      await client.evaluate("document.getElementById('home').click()");await client.chooseHome(action);
      assert.equal(await client.evaluate("document.getElementById('build-mode').value"),mode);
      assert.equal(await client.evaluate("document.getElementById('game-page').hidden"),false);
      if(action==='mod')assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'C:\\Retained\\Quest.apk');
      if(action==='profile')assert.equal(await client.evaluate("document.querySelector('#game-page .advanced').open"),true);
    }
    assert.equal(server.requests.some(row=>['/api/run','/api/plan'].includes(row.path)),false,'choosing a workflow never starts work');
    await client.evaluate("document.getElementById('home').click();document.querySelector('[data-language=de]').click()");
    assert.match(await client.evaluate("document.getElementById('home-profile').textContent"),/Spielername/);
    await client.picture('home-device-storage-de');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0,JSON.stringify(client.events.filter(row=>row.method==='Runtime.exceptionThrown').map(row=>row.params?.exceptionDetails)));
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('an unavoidable content transfer waits for a separate explicit approval; ordinary retry never grants it',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture(true);let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('resume');
    await client.wait("document.getElementById('install-content-reinstall')&&!document.getElementById('install-content-reinstall').hidden");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/Spielstände und Einstellungen.*geschützt.*Entscheidung/);
    assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/Ohne deine Zustimmung.*nicht deinstalliert.*10,2 GiB/);
    assert.equal(server.requests.some(row=>row.path==='/api/install/content-reinstall'),false);
    await client.evaluate("document.getElementById('failure-retry').click()");await client.wait("!document.getElementById('failure-retry').disabled");
    assert.equal(server.requests.some(row=>row.path==='/api/install/content-reinstall'),false,'ordinary Retry does not approve redeploy');
    assert.equal(server.retained.status,'blocked');
    await client.evaluate("document.getElementById('install-content-reinstall').click()");await client.wait("!document.getElementById('cancel').hidden");
    assert.equal(server.requests.filter(row=>row.path==='/api/install/content-reinstall').length,1);
    assert.equal(server.requests.some(row=>row.path==='/api/plan'),false,'approval reuses the completed build and original choices');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('device tools guide wireless setup, expose real capture progress and download its ZIP; cleanup is scoped and explicit',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('storage-used')&&document.getElementById('storage-used').textContent.includes('6 GiB')&&!document.getElementById('device-wireless').disabled");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.equal(await client.evaluate("['package-card','device-card','storage-card'].every(id=>document.getElementById(id).hidden)"),true);
    await client.chooseHome('storage');
    assert.match(await client.evaluate("document.getElementById('storage-categories').textContent"),/Unity-Import.*6 GiB.*Signierschlüssel.*6,5 KiB/);
    assert.equal(await client.evaluate("document.getElementById('storage-plan-cache').hidden"),false,'retained APK permits cache cleanup from home without opening a build');
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('device');
    assert.equal(await client.evaluate("document.getElementById('storage-card').hidden"),true);
    assert.equal(await client.evaluate("document.activeElement.id"),'device-title');
    await client.evaluate("document.getElementById('device-host').value='192.168.0.5:5555';document.getElementById('device-wireless').click()");
    await client.wait("document.getElementById('device-status').textContent.includes('USB-Debugging')");
    assert.equal(await client.evaluate("document.getElementById('device-guide').open"),true);
    await client.evaluate("document.getElementById('device-capture').click()");
    await client.wait("!document.getElementById('device-progress').hidden&&document.getElementById('device-progress').value===25");
    assert.equal(await client.evaluate("document.getElementById('home-build').disabled"),true,'no concurrent build while capture is active');
    await client.wait("!document.getElementById('device-capture-result').hidden");
    await client.evaluate("document.getElementById('device-download').click()");
    await client.wait("!document.getElementById('device-download').disabled");
    assert.ok(server.requests.some(row=>row.path==='/api/device/capture'&&row.body===undefined));
    assert.equal(server.requests.some(row=>['/api/run','/api/plan'].includes(row.path)),false);
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');
    await client.evaluate("document.getElementById('storage-plan-cache').click()");await client.wait("!document.getElementById('storage-confirmation').hidden");
    assert.equal(server.requests.some(row=>row.path==='/api/storage/clean'),false);
    assert.match(await client.evaluate("document.getElementById('storage-cache-consequence').textContent"),/APK.*Spiel-Datenpakete.*Signierschlüssel/);
    await client.evaluate("document.getElementById('storage-clean').click()");await client.wait("document.getElementById('storage-status').textContent.includes('6 GiB freigegeben')");
    await client.evaluate("document.querySelector('[data-language=en]').click()");
    assert.match(await client.evaluate("document.getElementById('device-title').textContent"),/Quest connection/);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    await delay(50);
  }finally{try{await client?.close();}finally{await server.close();}}
 });
