// Actual shipped DOM/Chrome and production pause contract; no compiler or game build.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';

const ui=fileURLToPath(new URL('../',import.meta.url)),session='a'.repeat(32);
test('rebuild needs an explicit reversible confirmation; setup alone launches no work',
 {skip:!existsSync(chrome),timeout:30000},async()=>{
  const actions=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',fileURLToPath(new URL('./update_guidance_fixture.py',import.meta.url)),resolve(ui,'../..')],{encoding:'utf8'}));
  const posts=[],choices={mode:'update-mod',gameRoot:'C:\\Owned Game',provider:'gog',baseApk:'D:\\Quest\\existing.apk',signingRoot:'D:\\Quest\\keys',signingMode:'original',
    profile:{displayName:'Fixture Player',providerId:'fixture-123'},acceptUnityTerms:true,install:false,ownedDlc:['jotl','solo']};
  const state={session,status:'blocked',choices,needsActions:[actions.decision],stages:['tools','source','unity','profile','inspect','build','install'].map((id,index)=>({id,status:index<5?'complete':index===5?'blocked':'pending',progress:{stagePercent:index<5?100:40}}))};
  let client;
  const server=createServer(async(request,response)=>{
    try{
      const path=new URL(request.url,'http://localhost').pathname;
      if(request.method==='POST'){let body='';for await(const chunk of request)body+=chunk;posts.push({path,body:body?JSON.parse(body):null});}
      let value;
      if(path==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:'blocked',mode:'update-mod'}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:true}};
      else if(path==='/api/status')value={schema:1,event:'status',session,state};
      else if(path==='/api/events')value={schema:1,event:'events',events:[],lastEvent:0};
      else if(path==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
      if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
      const filename=resolve(ui,'.'+(path==='/'?'/index.html':path)),mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
      if(!filename.startsWith(ui)||!mime[extname(filename)])throw Error('Unexpected operation '+path);
      response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
    client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#decision-fixture'});
    await client.chooseHome('resume');await client.evaluate("document.querySelector('[data-language=de]').click()");
    await client.wait("!document.getElementById('failure-new-build').hidden&&!document.getElementById('failure-new-build').disabled");
    assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/Spielkonvertierung wird nicht automatisch/);
    assert.match(await client.evaluate("document.getElementById('failure-cause').textContent"),/Fixture.Script/);
    assert.equal(await client.evaluate("document.getElementById('install-reset-app-data').hidden"),true);
    const baseline=posts.length;
    await client.evaluate("document.getElementById('failure-new-build').click()");
    await client.wait("document.getElementById('update-rebuild-dialog').open");
    assert.equal(await client.evaluate('document.activeElement.id'),'update-rebuild-cancel');
    assert.match(await client.evaluate("document.getElementById('update-rebuild-reason').textContent"),/Fixture.Script/);
    assert.match(await client.evaluate("document.getElementById('update-rebuild-copy').textContent"),/mehrere Stunden.*zusätzlichen Speicher/s);
    assert.equal(await client.evaluate("document.getElementById('update-rebuild-reason').checkVisibility()"),true);
    await client.evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    await client.picture('update-rebuild-decision-de');
    await client.evaluate("document.getElementById('update-rebuild-cancel').click()");
    assert.equal(await client.evaluate("document.getElementById('update-rebuild-dialog').open"),false);
    assert.equal(await client.evaluate("document.getElementById('progress-page').hidden"),false);
    assert.equal(posts.length,baseline);
    // The existing footer entry must obey the same confirmation boundary.
    await client.evaluate("document.getElementById('new-build').click()");
    await client.wait("document.getElementById('update-rebuild-dialog').open");
    assert.match(await client.evaluate("document.getElementById('update-rebuild-reason').textContent"),/Fixture.Script/);
    assert.equal(await client.evaluate("document.getElementById('update-rebuild-reason').checkVisibility()"),true);
    await client.evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    await client.evaluate("document.querySelector('[data-language=en]').click()");
    assert.match(await client.evaluate("document.getElementById('update-rebuild-copy').textContent"),/several hours.*retained/s);
    assert.match(await client.evaluate("document.getElementById('update-rebuild-reason').textContent"),/Fixture.Script/);
    assert.equal(await client.evaluate("document.getElementById('update-rebuild-reason').checkVisibility()"),true);
    await client.evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    await client.picture('update-rebuild-decision-en');
    await client.evaluate("document.getElementById('update-rebuild-confirm').click()");
    await client.wait("!document.getElementById('game-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('build-mode').value"),'build');
    assert.equal(await client.evaluate("document.getElementById('game-root').value"),choices.gameRoot);
    assert.equal(await client.evaluate("document.getElementById('profile-name').value"),choices.profile.displayName);
    assert.equal(await client.evaluate("document.getElementById('profile-id').value"),choices.profile.providerId);
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),'');
    assert.equal(await client.evaluate("document.getElementById('install-choice').checked"),false);
    assert.deepEqual(await client.evaluate("[...document.querySelectorAll('input[name=dlc]:checked')].map(node=>node.value)"),choices.ownedDlc);
    assert.equal(posts.length,baseline,'choosing setup neither mutates retained run nor launches game conversion');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}}
 });
