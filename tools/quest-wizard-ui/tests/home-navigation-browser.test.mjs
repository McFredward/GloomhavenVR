// Actual Chrome/loopback witness. The declared job and artifacts are fixtures;
// no Unity, ADB, game conversion or ownership validation is performed here.
import test from 'node:test';
import assert from 'node:assert/strict';
import {execFile} from 'node:child_process';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {promisify} from 'node:util';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='a'.repeat(32);
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml','.jpg':'image/jpeg'};
const readyUpdate={status:'available',available:true,inputsReady:true,installedBuild:675,bundledBuild:676,
  apkPath:'C:\\Users\\Fixture\\.ghvrq\\build\\Quest.apk',gameRoot:'C:\\Owned Gloomhaven',reason:'bundled-newer',missing:[]};
async function fixture({recommendation=readyUpdate,discoveryOverride={},held=false,active=false,device=false,manualRoot=false}={}){
  const requests=[];let releaseDiscovery;const gate=new Promise(resolve=>releaseDiscovery=resolve);
  const stages=['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='build'?'running':id==='install'?'pending':'complete',
    ...(id==='build'?{progress:{stagePercent:35.25,phase:'unity-il2cpp',detail:'Fixture code compilation'}}:{})}));
  const state={session,status:'running',choices:{mode:'update-mod',gameRoot:readyUpdate.gameRoot,provider:'gog',baseApk:readyUpdate.apkPath,install:false,acceptUnityTerms:true},
    stages,progress:{phase:'build',completed:5,total:7},needsActions:[]};
  let deviceState={status:'idle',devices:[]};
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost'),path=url.pathname;
      if(path.startsWith('/api/')){
        assert.equal(request.headers['x-quest-token'],'home-ui-fixture');
        let raw='';for await(const chunk of request)raw+=chunk;requests.push({path,body:raw?JSON.parse(raw):undefined});
        const reply=value=>{response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));};
        if(path==='/api/discover'){
          if(held)await gate;
          return reply({schema:1,event:'discovery',games:[{id:'owned',gameRoot:readyUpdate.gameRoot,provider:'gog'}],unityEditors:[],
            defaultApk:{path:readyUpdate.apkPath,modBuild:675},modSource:{kind:'bundled-release',modBuild:676,modVersion:'1.1.1-dev',sourceCommit:'b'.repeat(40)},
            recentSessions:active?[{session,status:'running',mode:'update-mod'}]:[],updateRecommendation:manualRoot?{...recommendation,gameRoot:'D:\\Manually selected Gloomhaven'}:recommendation,
            capabilities:{browse:false,deviceTools:device},...discoveryOverride});
        }
        if(path==='/api/gallery')return reply({schema:1,event:'gallery',artwork:['brute.jpg','spellweaver.png','scoundrel.png'].map((name,index)=>({id:String(index),url:'/api/promo-artwork?name='+name,altCode:'ownedArtwork'}))});
        if(path==='/api/promo-artwork'){
          const name=url.searchParams.get('name');assert.ok(['brute.jpg','spellweaver.png','scoundrel.png'].includes(name));
          const bytes=await readFile(resolve(root,'assets/promo',name));response.writeHead(200,{'Content-Type':mime[extname(name)],'Content-Length':bytes.length});return response.end(bytes);
        }
        if(path==='/api/status')return reply({schema:1,event:'status',session,state});
        if(path==='/api/events')return reply({schema:1,event:'events',events:[],lastEvent:0});
        if(path==='/api/device/status')return reply({schema:1,event:'device',state:deviceState});
        if(path==='/api/device/capture'){
          deviceState={status:'capturing',progress:{percent:20,done:1,total:5,label:'logcat'},activity:{detail:'quest-startup.log'}};
          return reply({schema:1,event:'device',state:deviceState});
        }
        response.writeHead(404);return response.end();
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));assert.ok(filename.startsWith(root)&&mime[extname(filename)]);
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#home-ui-fixture',requests,state,releaseDiscovery,
    publishCurrent(){
      const path=readyUpdate.apkPath.replace('Quest.apk','Updated-Quest.apk');
      state.status='complete';state.stages.forEach(row=>row.status='complete');state.result={apk:path};
      recommendation={...readyUpdate,status:'current',available:false,installedBuild:676,apkPath:path,reason:'already-current'};
      discoveryOverride.defaultApk={path,modBuild:676};
    },
    advance(percent){stages[5].progress.stagePercent=percent;},
    deviceProgress(percent){deviceState={...deviceState,progress:{...deviceState.progress,percent}};},
    close:()=>new Promise(resolve=>{releaseDiscovery();server.close(resolve);server.closeAllConnections();})};
}

async function ready(client,server){
  await client.command('Page.navigate',{url:server.url});
  await client.wait('location.origin==='+JSON.stringify(new URL(server.url).origin)+"&&document.getElementById('home-page')&&!document.getElementById('home-page').hidden&&!document.getElementById('home-build').disabled");
  await client.wait("document.querySelectorAll('#gallery .art-card img').length===3&&[...document.querySelectorAll('#gallery img')].every(image=>image.complete&&image.naturalWidth>0)");
}
async function noOverflow(client){assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);}
function noMutations(server){assert.equal(server.requests.some(row=>['/api/plan','/api/run','/api/cancel'].includes(row.path)),false,'navigation never starts, resumes or cancels work');}

test('home promotes a real metadata recommendation after twelve retained updates and rejects incomplete histories',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const {stdout,stderr}=await promisify(execFile)(process.env.QUEST_WIZARD_TEST_PYTHON??'python3',
    ['-B',resolve(root,'tests/home-update-fixture.py')],{maxBuffer:256*1024});
  assert.equal(stderr,'');
  const cases=JSON.parse(stdout);assert.equal(cases.length,5);
  assert.equal(cases[0].receiptCount,13,'fixture includes a full build and twelve compact updates');
  let client;
  try{
    client=await browser();
    for(const row of cases){
      const promoted=['twelve-retained-updates','cleaned-ancestor-apks'].includes(row.name);
      const server=await fixture({discoveryOverride:row.discovery});
      try{
        await ready(client,server);
        if(promoted)await client.picture('home-update-'+row.name);
        assert.equal(await client.evaluate("document.getElementById('home-mod').classList.contains('recommended')"),promoted,row.name);
        assert.equal(await client.evaluate("document.getElementById('home-mod-badge').hidden"),!promoted,row.name);
        assert.equal(await client.evaluate("document.getElementById('home-mod-recommendation').hidden"),!promoted,row.name);
        if(promoted){
          assert.equal(row.discovery.updateRecommendation.inputsReady,true,
            'real retained-content ancestry remains ready beyond the old eight-receipt limit');
          assert.ok(row.metadataReads.length>=13,'fixture follows real closed ancestry receipts');
          assert.equal(await client.evaluate("getComputedStyle(document.getElementById('home-mod')).borderTopColor!==getComputedStyle(document.getElementById('home-build')).borderTopColor"),true,
            'the real recommendation has a visibly distinct tile border');
          await client.evaluate("document.querySelector('[data-language=de]').click()");
          assert.match(await client.evaluate("document.getElementById('home-mod-recommendation').textContent"),/B682 → B683/);
          await client.chooseHome('mod');
          assert.equal(await client.evaluate("document.getElementById('base-apk').value"),row.discovery.defaultApk.path);
          assert.equal(await client.evaluate("document.getElementById('game-root').value"),row.discovery.games[0].gameRoot);
        }
        noMutations(server);
      }finally{await server.close();}
    }
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{await client?.close();}
 });

test('home checks the bundled update on arrival, highlights only ready local inputs and keeps one game logo',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({held:true,manualRoot:true});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-update-status')&&!document.getElementById('home-update-status').hidden");
    assert.equal(await client.evaluate("document.getElementById('home-mod').disabled"),true);
    server.releaseDiscovery();await client.wait("!document.getElementById('home-mod').disabled");
    await client.wait("document.querySelector('#gallery .art-card img')?.naturalWidth>0");
    await client.evaluate("document.querySelector('[data-language=de]').click();document.getElementById('gallery-pause').click()");
    assert.equal(await client.evaluate("document.getElementById('home-mod').classList.contains('recommended')"),true);
    assert.match(await client.evaluate("document.getElementById('home-mod-recommendation').textContent"),/B675 → B676.*APK.*vorhanden/);
    assert.equal(await client.evaluate("document.getElementById('home-update-status').hidden"),true);
    assert.equal(await client.evaluate("document.querySelectorAll('img[src$=\"gloomhavenvr-logo.png\"]').length"),1);
    assert.equal(await client.evaluate("document.querySelector('.topbar img')===null"),true);
    for(const id of ['project-github','project-coffee']){
      const action=await client.evaluate(`(()=>{const element=document.getElementById('${id}');return {width:element.getBoundingClientRect().width,height:element.getBoundingClientRect().height,text:element.textContent};})()`);
      assert.ok(action.width>=90&&action.height>=44,JSON.stringify(action));assert.ok(action.text.trim().length>3);
      assert.equal(await client.evaluate(`(()=>{const rect=document.getElementById('${id}').getBoundingClientRect();return rect.top>=0&&rect.bottom<=innerHeight;})()`),true,'project actions are visible in the first desktop viewport');
    }
    assert.equal(server.requests.some(row=>row.path==='/api/status'),false);
    await client.picture('home-update-desktop-de');
    await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),readyUpdate.apkPath);
    assert.equal(await client.evaluate("document.getElementById('game-root').value"),'D:\\Manually selected Gloomhaven','the qualified manual PC installation takes precedence over another detected copy');
    assert.equal(await client.evaluate("document.getElementById('provider').value"),'gog','an unknown game path does not invent a different provider');
    assert.ok(await client.evaluate("document.getElementById('home').getBoundingClientRect().height>=44"));
    assert.equal(await client.evaluate("document.getElementById('home').hidden"),false);
    await client.evaluate("document.getElementById('game-root').value='D:\\\\Explicit game';document.getElementById('base-apk').value='D:\\\\Explicit.apk';document.getElementById('base-apk').dispatchEvent(new Event('input'))");
    await client.evaluate("document.getElementById('home').click();document.getElementById('home-mod').click()");
    assert.equal(await client.evaluate("document.getElementById('game-root').value"),'D:\\Explicit game','returning from Home keeps a typed game selection');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'D:\\Explicit.apk','returning from Home keeps a typed APK override');
    await client.picture('update-workflow-desktop-de');
    await client.evaluate('window.scrollTo(0,Math.min(300,document.documentElement.scrollHeight-innerHeight))');
    assert.equal(await client.evaluate("document.querySelector('.adventure').getBoundingClientRect().top>=document.querySelector('.topbar').getBoundingClientRect().bottom"),true,'sticky artwork clears the navigation');
    await client.evaluate('window.scrollTo(0,document.documentElement.scrollHeight)');
    assert.equal(await client.evaluate("(()=>{const rect=document.getElementById('home').getBoundingClientRect();return rect.top>=0&&rect.bottom<=innerHeight;})()"),true,'Home remains reachable after scrolling a long workflow');
    await client.evaluate("document.getElementById('home').click();document.querySelector('[data-language=en]').click()");
    assert.match(await client.evaluate("document.getElementById('home-mod-recommendation').textContent"),/local game content found/);
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    await noOverflow(client);await client.picture('home-update-mobile-en');
    await client.chooseHome('mod');await noOverflow(client);
    await client.evaluate("document.querySelector('#game-page h2').scrollIntoView({block:'start'})");
    await client.wait("document.querySelector('#game-page h2').getBoundingClientRect().top>=document.querySelector('.topbar').getBoundingClientRect().bottom");
    assert.equal(await client.evaluate("document.querySelector('#game-page h2').getBoundingClientRect().top>=document.querySelector('.topbar').getBoundingClientRect().bottom"),true,'a focused workflow heading clears wrapped navigation');
    await client.picture('update-workflow-mobile-en');
    await client.evaluate('window.scrollTo(0,document.documentElement.scrollHeight)');
    assert.equal(await client.evaluate("(()=>{const rect=document.getElementById('home').getBoundingClientRect();return rect.top>=0&&rect.bottom<=innerHeight;})()"),true,'Home remains reachable after scrolling on a narrow screen');
    noMutations(server);assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('home does not promote missing inputs, current builds, newer installed builds or old discovery replies',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  let client;
  try{
    client=await browser();
    for(const recommendation of [
      {...readyUpdate,inputsReady:false,missing:['game-root']},
      {...readyUpdate,available:false,status:'current',installedBuild:676},
      {...readyUpdate,available:false,status:'newer-installed',installedBuild:677},
      null,
    ]){
      const server=await fixture({recommendation});
      try{
        await ready(client,server);
        assert.equal(await client.evaluate("document.getElementById('home-mod').classList.contains('recommended')"),false);
        assert.equal(await client.evaluate("document.getElementById('home-mod-badge').hidden"),true);
        assert.equal(await client.evaluate("document.getElementById('home-mod-recommendation').hidden"),true);
        assert.equal(await client.evaluate("document.getElementById('home-mod').disabled"),false,'manual update remains available');
        noMutations(server);
      }finally{await server.close();}
    }
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{await client?.close();}
 });

test('going home retains an active update, continues measured polling and returns to its current details',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({active:true});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-active')&&!document.getElementById('home-active').hidden&&!document.getElementById('home-active-open').disabled");
    assert.equal(server.requests.some(row=>row.path==='/api/status'),false,'discovery does not reopen a run by itself');
    assert.equal(await client.evaluate("document.getElementById('home-build').disabled"),true,'an already active task blocks conflicting builds');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    await noOverflow(client);
    await client.command('Emulation.setDeviceMetricsOverride',{width:1366,height:900,deviceScaleFactor:1,mobile:false});
    await client.evaluate("document.querySelector('[data-language=de]').click();document.getElementById('home-active-open').click()");
    await client.wait("!document.getElementById('progress-page').hidden");
    await client.evaluate("document.getElementById('home').click()");
    assert.equal(await client.evaluate("document.getElementById('home-active-bar').value"),35.25);
    assert.match(await client.evaluate("document.getElementById('home-active-detail').textContent"),/Fixture code compilation/);
    const previous=server.requests.filter(row=>row.path==='/api/status').length;
    server.advance(61.125);
    await client.wait("document.getElementById('home-active-bar').value===61.125");
    assert.ok(server.requests.filter(row=>row.path==='/api/status').length>previous,'authoritative status keeps polling on home');
    assert.match(await client.evaluate("document.getElementById('home-active-percent').textContent"),/61,125/);
    assert.match(await client.evaluate("document.getElementById('home-active-note').textContent"),/Arbeit läuft weiter/);
    assert.equal(await client.evaluate("document.getElementById('home-mod').disabled"),true);
    await client.picture('home-active-update-desktop-de');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    await noOverflow(client);await client.picture('home-active-update-mobile-de');
    await client.evaluate("document.getElementById('home-active-open').click()");
    assert.equal(await client.evaluate("document.getElementById('progress-page').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'61.125');
    await client.evaluate("document.getElementById('home').click()");server.state.status='complete';
    server.state.stages.forEach(row=>row.status='complete');
    await client.wait("document.getElementById('home-active').hidden&&!document.getElementById('home-mod').disabled");
    noMutations(server);assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('active device capture is visible on home, keeps polling and returns to the device page',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({device:true});let client;
  try{
    client=await browser();await ready(client,server);await client.chooseHome('device');
    await client.evaluate("document.getElementById('device-capture').click()");
    await client.wait("!document.getElementById('device-progress').hidden&&document.getElementById('device-progress').value===20");
    await client.evaluate("document.getElementById('home').click()");
    assert.equal(await client.evaluate("document.getElementById('home-active').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('home-active-bar').value"),20);
    server.deviceProgress(60);await client.wait("document.getElementById('home-active-bar').value===60");
    await client.evaluate("document.getElementById('home-active-open').click()");
    assert.equal(await client.evaluate("document.getElementById('device-card').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('device-progress').value"),60);
    noMutations(server);assert.equal(server.requests.some(row=>row.path==='/api/device/cancel'),false);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('a completed update refreshes the published APK recommendation once without restarting work',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await fixture({active:true});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-active')&&!document.getElementById('home-active').hidden&&!document.getElementById('home-active-open').disabled");
    assert.equal(await client.evaluate("document.getElementById('home-mod').classList.contains('recommended')"),true);
    await client.evaluate("document.getElementById('home-active-open').click()");
    await client.wait("!document.getElementById('progress-page').hidden");
    Object.assign(server.state.stages[5].progress,{phase:'figure-banks',unit:'work-shares',done:125,total:1000,
      detail:'Extract original figure bundles 2/16'});
    await client.wait("document.getElementById('progress-detail').textContent.includes('12.5 %')");
    assert.doesNotMatch(await client.evaluate("document.getElementById('progress-detail').textContent"),/125\s*\/\s*1[,.]?000/,
      'weighted work shares are a percentage, while the real local task count remains in the detail');
    server.publishCurrent();
    await client.wait("document.getElementById('progress-title').textContent.includes('complete')||document.getElementById('progress-title').textContent.includes('abgeschlossen')");
    await client.evaluate("document.getElementById('home').click()");
    await client.wait("document.getElementById('home-active').hidden&&!document.getElementById('home-mod').disabled&&document.getElementById('home-update-status').hidden");
    assert.equal(await client.evaluate("document.getElementById('home-mod').classList.contains('recommended')"),false);
    assert.equal(await client.evaluate("document.getElementById('home-mod-badge').hidden"),true);
    assert.equal(server.requests.filter(row=>row.path==='/api/discover').length,2,'one initial and one closed-output metadata read');
    await client.chooseHome('mod');
    assert.match(await client.evaluate("document.getElementById('base-apk').value"),/Updated-Quest\.apk$/);
    await client.evaluate("document.getElementById('home').click();document.getElementById('home-mod').click();document.getElementById('home').click()");
    assert.equal(server.requests.filter(row=>row.path==='/api/discover').length,2,'unchanged navigation reuses refreshed metadata');
    noMutations(server);assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });
