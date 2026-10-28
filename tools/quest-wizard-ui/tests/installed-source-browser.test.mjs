// Real DOM/browser with a declared device protocol fixture. Byte acquisition and
// APK validation are exercised by the independent installer/Wizard suites.
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
async function fixture({local=true,legacy=false,pcRequired=false}={}){
  const requests=[],session='a'.repeat(32);let device={status:'idle',devices:[]},polls=0;
  const source={deviceSerial:'USB_FIXTURE',versionCode:716,versionName:'1.2.0.B716.123456789abc',size:2097152000};
  const replyDevice=()=>({schema:1,event:'device',state:device});
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'installed-fixture');
        let raw='';for await(const chunk of request)raw+=chunk;const body=raw?JSON.parse(raw):undefined;requests.push({path,body});
        const reply=value=>{response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover')return reply({schema:1,event:'discovery',modSource:{modBuild:717},games:[],unityEditors:[],recentSessions:[],
          defaultApk:local?{path:'C:\\Users\\Fixture\\.ghvrq\\build\\latest.apk',modBuild:716}:null,
          capabilities:{browse:false,deviceTools:true,installedSources:true,spaceEstimate:true,logs:false}});
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:[]});
        if(path==='/api/storage')return reply({schema:1,event:'storage',state:{status:'idle'},inventory:{status:'idle'}});
        if(path==='/api/storage/runs')return reply({schema:1,event:'runs',runs:[],retiredRuns:[]});
        if(path==='/api/device/source-discover'){
          assert.deepEqual(body,{host:'',serial:''});device={status:'source-found',operation:'source-discover',devices:[{serial:'USB_FIXTURE',state:'device',kind:'usb'}],source,
            message:{en:'Package metadata only; no payload transfer.',de:'Nur Paketinformationen; keine Inhaltsübertragung.'}};return reply(replyDevice());
        }
        if(path==='/api/device/source-import'){
          device={status:'scanning',operation:'source-import',devices:[{serial:'USB_FIXTURE',state:'device',kind:'usb'}],
            progress:{done:1048576,total:4194304,unit:'bytes',percent:25,detail:'base.apk'}};polls=0;return reply(replyDevice());
        }
        if(path==='/api/device/status'){
          if(device.operation==='source-import'&&device.status==='scanning'){
            polls++;if(polls===1)device.progress={done:3145728,total:4194304,unit:'bytes',percent:75,detail:'base.apk'};
            if(polls>=3)device={...device,status:'source-ready',source:{...source,apk:'C:\\Users\\Fixture\\.ghvrq\\build\\device-inputs\\installed.apk',seed:{available:!legacy,reason:legacy?'missing':'ready'}},message:{en:'APK imported; banks and saves retained on Quest.',de:'APK eingelesen; Spielinhalte und Spielstände bleiben auf Quest.'}};
          }
          return reply(replyDevice());
        }
        if(path==='/api/qualify'){assert.equal(body.installedGame,true);assert.equal('gameRoot' in body,false);return reply({schema:1,event:'qualification',spaceEstimate:{additionalEstimatedBytes:40*1073741824},freeBytes:80*1073741824});}
        if(path==='/api/plan'){
          assert.equal(body.choices.installedGame,true);assert.equal('gameRoot' in body.choices,false);
          return reply({schema:1,event:'planned',session,state:{session,status:'ready',choices:body.choices,needsActions:[],stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:'pending',attempts:0}))}});
        }
        if(path==='/api/run')return reply({schema:1,event:'started',session});
        if(path==='/api/status')return reply({schema:1,event:'status',session,state:{session,status:pcRequired?'blocked':'running',choices:{mode:'update-mod',installedGame:true,baseApk:'C:\\Users\\Fixture\\.ghvrq\\build\\latest.apk',install:false},needsActions:pcRequired?[{code:'installed_update_pc_required',stage:'tools',message:{en:'One matching PC copy is needed once.',de:'Einmalig ist die passende PC-Kopie nötig.'},parameters:{requiresPcUpdateSource:true,cause:'This older Quest APK has no portable compiler references.'}}]:[],stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='tools'?(pcRequired?'blocked':'running'):'pending',attempts:0}))}});
        if(path==='/api/events')return reply({schema:1,event:'events',events:[],lastEvent:0});
        response.writeHead(404);return response.end();
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#installed-fixture',requests,close:()=>new Promise(resolve=>server.close(resolve))};
}

test('local installed-APK update keeps the existing path and needs no PC game or launcher', {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture();let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('mod');
    await client.wait("document.getElementById('source-device-summary').textContent.includes('B716')");
    assert.equal(await client.evaluate("document.getElementById('installed-game').checked"),true);
    assert.equal(await client.evaluate("document.getElementById('pc-game-choice').hidden"),true);
    assert.equal(await client.evaluate("document.getElementById('primary').disabled"),false);
    assert.match(await client.evaluate("document.getElementById('base-apk').value"),/latest\.apk$/);
    assert.equal(server.requests.some(row=>row.path==='/api/device/source-import'),false);
    await client.picture('installed-source-local');await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('setup-page').hidden");
    const qualification=server.requests.find(row=>row.path==='/api/qualify');assert.equal(qualification.body.installedGame,true);
    assert.equal(server.requests.some(row=>row.path==='/api/launcher/open'),false);
  }finally{await client?.close();await server.close();}
});

test('Quest source imports only on explicit action, reports measured bytes and freezes imported APK in the plan', {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({local:false});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('mod');
    await client.wait("document.getElementById('source-device-summary').textContent.includes('B716')");
    await client.evaluate("document.getElementById('update-source').value='quest';document.getElementById('update-source').dispatchEvent(new Event('change'))");
    assert.equal(await client.evaluate("document.getElementById('primary').disabled"),true);
    assert.equal(server.requests.some(row=>row.path==='/api/device/source-import'),false);
    await client.evaluate("document.getElementById('source-import').click()");
    await client.wait("!document.getElementById('source-device-progress').hidden&&document.getElementById('source-device-bar').value===25");
    assert.match(await client.evaluate("document.getElementById('source-device-detail').textContent"),/1 MiB.*4 MiB/);
    await client.picture('installed-source-transfer');
    await client.wait("document.getElementById('base-apk').value.includes('device-inputs')&&!document.getElementById('primary').disabled");
    await client.evaluate("document.getElementById('primary').click()");await client.wait("!document.getElementById('setup-page').hidden");
    await client.evaluate("document.getElementById('unity-terms').checked=true;document.getElementById('install-choice').checked=false;document.getElementById('primary').click()");
    await client.wait("!document.getElementById('progress-page').hidden");
    const choices=server.requests.find(row=>row.path==='/api/plan').body.choices;
    assert.equal(choices.baseApk,'C:\\Users\\Fixture\\.ghvrq\\build\\device-inputs\\installed.apk');
    assert.deepEqual(choices.deviceSource,{deviceSerial:'USB_FIXTURE',versionName:'1.2.0.B716.123456789abc'});
    assert.equal(choices.installedGame,true);assert.equal('profile' in choices,false);assert.equal('ownedDlc' in choices,false);
  }finally{await client?.close();await server.close();}
});

test('legacy installed APK asks for one PC-assisted update without launching conversion or silently changing sources', {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({local:false,legacy:true});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('mod');
    await client.wait("document.getElementById('source-device-summary').textContent.includes('B716')");
    await client.evaluate("document.getElementById('update-source').value='quest';document.getElementById('update-source').dispatchEvent(new Event('change'));document.getElementById('source-import').click()");
    await client.wait("!document.getElementById('source-seed-warning').hidden");
    assert.equal(await client.evaluate("document.getElementById('installed-game').checked"),true);
    assert.equal(await client.evaluate("document.getElementById('pc-game-choice').hidden"),true);
    assert.equal(server.requests.some(row=>['/api/plan','/api/run'].includes(row.path)),false);
    await client.picture('installed-source-legacy');await client.evaluate("document.getElementById('source-use-pc').click()");
    assert.equal(await client.evaluate("document.getElementById('pc-game-choice').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('primary').disabled"),true);
    assert.match(await client.evaluate("document.getElementById('base-apk').value"),/installed\.apk$/);
  }finally{await client?.close();await server.close();}
});


test('missing-seed preflight presents a PC-assisted input action while keeping the exact APK and update mode', {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({pcRequired:true});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('mod');
    await client.wait("document.getElementById('source-device-summary').textContent.includes('B716')");
    await client.evaluate("document.getElementById('primary').click()");await client.wait("!document.getElementById('setup-page').hidden");
    await client.evaluate("document.getElementById('unity-terms').checked=true;document.getElementById('install-choice').checked=false;document.getElementById('primary').click()");
    await client.wait("!document.getElementById('failure-use-pc').hidden");
    assert.equal(await client.evaluate("document.getElementById('failure-new-build').hidden"),true);
    await client.picture('installed-source-pc-action');
    await client.evaluate("document.getElementById('failure-use-pc').click()");
    assert.equal(await client.evaluate("document.getElementById('pc-game-choice').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('installed-game').checked"),false);
    assert.equal(await client.evaluate("document.getElementById('build-mode').value"),'update-mod');
    assert.match(await client.evaluate("document.getElementById('base-apk').value"),/latest\.apk$/);
    assert.equal(server.requests.filter(row=>row.path==='/api/run').length,1,'Opening input selection does not start another run');
  }finally{await client?.close();await server.close();}
});
