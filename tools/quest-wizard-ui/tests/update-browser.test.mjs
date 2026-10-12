// Real DOM workflow qualification; explicit preview runs no external tools.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css',
  '.png':'image/png','.jpg':'image/jpeg','.svg':'image/svg+xml'};

for(const mode of ['update-profile','update-mod'])test('native browser: '+mode+' accepts an optional APK override, restores choices and applies its Unity requirement',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=createServer(async(request,response)=>{
    try{
      const path=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch{response.writeHead(404);response.end();}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let client;
  try{
    client=await browser();
    await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/?preview=1&lang=de'});
    await client.chooseHome(mode==='update-profile'?'profile':'mod');
    await client.wait("document.getElementById('primary')&&!document.getElementById('primary').disabled").catch(async error=>{const details='\n'+JSON.stringify(await client.evaluate("({page:document.getElementById('game-page').hidden,game:document.getElementById('game-root').value,error:document.getElementById('error-banner').textContent,device:document.getElementById('device-status').textContent})"))+'\n'+JSON.stringify(client.events.filter(row=>row.method==='Runtime.exceptionThrown').map(row=>row.params?.exceptionDetails));assert.fail(error.message+details);});
    await client.evaluate("document.getElementById('build-mode').value="+JSON.stringify(mode)+";document.getElementById('build-mode').dispatchEvent(new Event('change'))");
    assert.equal(await client.evaluate("document.getElementById('base-apk-choice').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('update-signing-choice').hidden"),false);
    await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('setup-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('error-banner').hidden"),true,'a blank override is qualified by the backend workspace lookup');
    await client.evaluate("document.getElementById('back').click()");
    await client.evaluate("document.getElementById('base-apk').value='C:\\\\Quest\\\\Existing.apk';document.getElementById('signing-root').value='C:\\\\Workspace\\\\build\\\\signing';document.getElementById('primary').click()");
    await client.wait("!document.getElementById('setup-page').hidden");
    assert.equal(await client.evaluate("document.querySelector('.license-note').hidden"),mode==='update-profile');
    assert.equal(await client.evaluate("document.querySelector('[data-i18n=setupContent]').textContent"),mode==='update-profile'?'Offline-Profil ändern':'Mod und Quest-Code aktualisieren');
    if(mode==='update-profile')assert.match(await client.evaluate("document.querySelector('[data-i18n=setupContentCopy]').textContent"),/weder Unity/);
    assert.equal(await client.evaluate("document.getElementById('unity-terms').checked"),false);
    if(mode==='update-mod'){
      await client.evaluate("document.getElementById('primary').click()");
      await client.wait("!document.getElementById('error-banner').hidden");
      assert.match(await client.evaluate("document.getElementById('error-banner').textContent"),/Unity-Bedingungen/);
      assert.equal(await client.evaluate("document.getElementById('progress-page').hidden"),true);
      await client.evaluate("document.getElementById('unity-terms').checked=true");
    }
    await client.picture(mode+'-setup-de');
    await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('progress-page').hidden&&!document.getElementById('cancel').hidden");
    await client.evaluate("document.getElementById('cancel').click()");
    await client.wait("document.getElementById('progress-title').textContent==='Der Build ist angehalten.'");
    await client.evaluate("document.getElementById('edit-choices').click()");
    await client.wait("!document.getElementById('game-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('build-mode').value"),mode);
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'C:\\Quest\\Existing.apk');
    assert.equal(await client.evaluate("document.getElementById('signing-root').value"),'C:\\Workspace\\build\\signing');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);
    await client.picture(mode+'-restored-mobile-de');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{
    try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}
  }
 });

async function ownedApkFixture({hasApk=true,hasRestoredApk=true,selectedStatus='cancelled'}={}) {
  const requests=[],session='a'.repeat(32),defaultApk='C:\\Users\\Fixture\\.ghvrq\\build\\updates\\'+'b'.repeat(64)+'\\GloomhavenVR-Quest.apk';
  const customApk='D:\\Custom game backups\\Explicit Quest.apk';
  const updatedApk='C:\\Users\\Fixture\\.ghvrq\\build\\updates\\'+'c'.repeat(64)+'\\GloomhavenVR-Quest.apk';
  const retained={session,status:'cancelled',choices:{gameRoot:'C:\\Owned game',provider:'gog',mode:'update-profile',baseApk:hasRestoredApk?customApk:'',install:true},needsActions:[],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:'pending'})),
    progress:{phase:'build',completed:0,total:7},result:{apk:'C:\\Older result\\Quest.apk'}};
  const server=createServer(async(request,response)=>{
    try {
      const path=new URL(request.url,'http://localhost').pathname;
      if(path.startsWith('/api/')) {
        assert.equal(request.headers['x-quest-token'],'owned-apk-fixture');requests.push(path);
        let raw='';for await(const chunk of request)raw+=chunk;const body=raw?JSON.parse(raw):undefined;
        let value;
        if(path==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:selectedStatus},{session:'older-failed-session',status:'failed'}],defaultApk:hasApk?{path:defaultApk,modBuild:671,source:'latest-build'}:null,
          games:[{id:'owned',gameRoot:'C:\\Owned game',provider:'gog'}],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
        else if(path==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(path==='/api/storage')value={schema:1,event:'storage',workspaceRoot:'C:\\Users\\Fixture\\.ghvrq',state:{status:'idle'},inventory:{status:'idle'}};
        else if(path==='/api/status')value={schema:1,event:'status',session,state:retained};
        else if(path==='/api/events')value={schema:1,event:'events',events:[],lastEvent:0};
        else if(path==='/api/plan'){retained.choices=body.choices;retained.status='ready';value={schema:1,event:'planned',session,state:retained};}
        else if(path==='/api/run'){assert.equal(body.session,session);retained.status='complete';retained.result={apk:updatedApk};retained.stages.forEach(row=>row.status='complete');retained.progress={completed:7,total:7};value={schema:1,event:'started',session};}
        else throw Error('Unexpected API operation: '+path);
        response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;
      }
      const filename=resolve(root,'.'+(path==='/'?'/index.html':path));
      if(!filename.startsWith(root)||!mime[extname(filename)])throw Error('Not static');
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',message:String(error)}));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  return {url:'http://127.0.0.1:'+server.address().port+'/#owned-apk-fixture',requests,defaultApk,customApk,updatedApk,close:()=>new Promise(resolve=>server.close(resolve))};
}

test('home APK updates preselect the owned completed APK and preserve an explicit or restored custom path',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture();let client;
  try {
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk);
    assert.equal(await client.evaluate("document.getElementById('build-mode').value"),'update-mod');
    assert.equal(server.requests.some(path=>['/api/status','/api/run','/api/plan'].includes(path)),false,'form defaults do not resume or build');
    await client.evaluate("document.getElementById('base-apk').value="+JSON.stringify(server.customApk)+";document.getElementById('base-apk').dispatchEvent(new Event('input'));document.getElementById('home').click()");
    await client.chooseHome('profile');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.customApk,'typed path takes priority over the discovered default');
    await client.evaluate("document.getElementById('base-apk').value='';document.getElementById('home').click()");await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk,'clearing an override restores the default');
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('resume');
    await client.wait("!document.getElementById('progress-page').hidden");
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.customApk,'restored custom choices take priority over an older completed result');
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('build');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'','a new full build has no base APK');
    assert.equal(server.requests.some(path=>['/api/run','/api/plan'].includes(path)),false);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('a just-completed APK update replaces the previous automatic default for the next update',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture();let client;
  try {
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('profile');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk);
    await client.evaluate("document.getElementById('profile-name').value='New profile';document.getElementById('primary').click()");
    await client.wait("!document.getElementById('setup-page').hidden");
    await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('result-card').hidden");
    await client.evaluate("document.getElementById('home').click()");
    assert.equal(await client.evaluate("Boolean(document.querySelector('#home-runs-list .run-row:first-child [data-run-action=resume]'))"),false,'a completed row has no resume action');
    await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.updatedApk,'do not reuse the old automatic base after a completed update');
    assert.equal(server.requests.filter(path=>path==='/api/plan').length,1);
    assert.equal(server.requests.filter(path=>path==='/api/run').length,1);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

for(const mode of ['update-profile','update-mod'])test('the advanced '+mode+' selection uses the owned APK and retains an explicit override',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture();let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('build');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'');
    await client.evaluate("document.getElementById('build-mode').value="+JSON.stringify(mode)+";document.getElementById('build-mode').dispatchEvent(new Event('change'))");
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk);
    await client.evaluate("document.getElementById('base-apk').value="+JSON.stringify(server.customApk)+";document.getElementById('base-apk').dispatchEvent(new Event('input'));document.getElementById('build-mode').value='build';document.getElementById('build-mode').dispatchEvent(new Event('change'))");
    await client.evaluate("document.getElementById('build-mode').value="+JSON.stringify(mode)+";document.getElementById('build-mode').dispatchEvent(new Event('change'))");
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.customApk);
    assert.equal(server.requests.some(path=>['/api/status','/api/run','/api/plan'].includes(path)),false);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('both APK edit workflows leave the field empty when there is no completed owned APK',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture({hasApk:false});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    for(const action of ['profile','mod']){
      await client.chooseHome(action);
      assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'');
      await client.evaluate("document.getElementById('build-mode').value='build';document.getElementById('build-mode').dispatchEvent(new Event('change'));document.getElementById('build-mode').value='update-profile';document.getElementById('build-mode').dispatchEvent(new Event('change'))");
      assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'');
      await client.evaluate("document.getElementById('home').click()");
    }
    assert.equal(server.requests.some(path=>['/api/status','/api/run','/api/plan'].includes(path)),false);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });

test('an incomplete session result cannot fill the APK field when no completed APK is available',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture({hasApk:false,hasRestoredApk:false});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('resume');
    await client.wait("!document.getElementById('progress-page').hidden");
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('profile');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'');
    assert.equal(server.requests.some(path=>['/api/run','/api/plan'].includes(path)),false);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });


test('an update route shows only its operation and retains setup choices through utility/home navigation',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture();let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});await client.chooseHome('profile');
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.equal(await client.evaluate("['package-card','device-card','storage-card'].every(id=>document.getElementById(id).hidden)"),true);
    assert.equal(await client.evaluate("document.getElementById('build-mode').hidden"),true);
    assert.equal(await client.evaluate("document.querySelector('#game-page h2').textContent"),'Spielername ändern');
    assert.match(await client.evaluate("document.getElementById('base-apk-default').textContent"),/vorhandene Stand aus \.ghvrq/);
    await client.evaluate("document.getElementById('profile-name').value='Retained typed name';document.getElementById('primary').click()");
    await client.wait("!document.getElementById('setup-page').hidden");
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('storage');
    assert.equal(await client.evaluate("document.getElementById('storage-card').hidden"),false);
    assert.equal(await client.evaluate("document.activeElement.id"),'storage-title');
    assert.equal(await client.evaluate("document.getElementById('primary').hidden&&document.querySelector('.steps').hidden"),true);
    await client.evaluate("document.getElementById('primary').click()");
    assert.equal(server.requests.some(path=>['/api/plan','/api/run'].includes(path)),false,'hidden primary cannot trigger a build on a utility page');
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('profile');
    assert.equal(await client.evaluate("document.getElementById('setup-page').hidden"),false,'the selected workflow retains its last page');
    assert.equal(await client.evaluate("document.getElementById('profile-name').value"),'Retained typed name');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk);
    assert.equal(await client.evaluate("document.getElementById('storage-card').hidden"),true);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    await client.picture('focused-profile-setup-de');
  }finally{try{await client?.close();}finally{await server.close();}}
 });


for(const selectedStatus of ['complete','failed','running'])test('homepage resume reflects selected saved status '+selectedStatus+' without reopening older jobs',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const server=await ownedApkFixture({selectedStatus});let client;
  try{
    client=await browser();await client.command('Page.navigate',{url:server.url});
    await client.wait("document.getElementById('home-mod')&&!document.getElementById('home-mod').disabled");
    assert.equal(await client.evaluate("Boolean(document.querySelector('#home-runs-list .run-row:first-child [data-run-action=resume]'))"),selectedStatus==='failed');
    if(selectedStatus==='running'){
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.match(await client.evaluate("document.querySelector('#home-runs-list .run-row:first-child [data-run-action=open]').textContent"),/Laufenden Vorgang/);
    }
    assert.equal(server.requests.includes('/api/status'),false,'discovery metadata does not start/reopen any run');
    await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),server.defaultApk,'completed APK metadata remains available independently');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await server.close();}}
 });
