// Real shipped DOM/CDP/HTTP; no Unity, game conversion, install or deletion.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome} from './browser-harness.mjs';

const ui=fileURLToPath(new URL('../',import.meta.url)),session='a'.repeat(32),ids=['tools','source','unity','profile','inspect','build','install'];
test('current metadata pause and APK input mismatch have distinct actions; retry preserves owner and edit preserves choices',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const actions=JSON.parse(execFileSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-B',fileURLToPath(new URL('./update_guidance_fixture.py',import.meta.url)),resolve(ui,'../..')],{encoding:'utf8'}));
    let kind='metadata',client;const posts=[];
    const choices={mode:'update-mod',gameRoot:'C:\Owned Game',provider:'gog',baseApk:'D:\Quest\existing.apk',signingRoot:'D:\Quest\keys',signingMode:'original',
      profile:{displayName:'Fixture Player',providerId:'fixture-123'},acceptUnityTerms:true,install:false,ownedDlc:[1809490]};
    const state=()=>({session,status:kind==='metadata'?'blocked':kind==='update'?'failed':'running',choices,needsActions:actions[kind]?[actions[kind]]:[],
      stages:ids.map((id,index)=>({id,status:index<5?'complete':index===5?(kind==='metadata'?'blocked':kind==='update'?'failed':'running'):'pending',progress:{stagePercent:index<5?100:index===5?48:0,phase:index===5?'operation:update-code':'complete'}}))});
    const server=createServer(async(request,response)=>{
      try{
        const path=new URL(request.url,'http://localhost').pathname;
        if(request.method==='POST'){let body='';for await(const row of request)body+=row;posts.push({path,body:JSON.parse(body)});}
        let value;
        if(path==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:'blocked',mode:'update-mod'}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:true}};
        else if(path==='/api/status')value={schema:1,event:'status',session,state:state()};
        else if(path==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(path==='/api/events')value={schema:1,event:'events',events:[],lastEvent:0};
        else if(path==='/api/run'){kind='running';value={schema:1,event:'started',session};}
        else if(path==='/api/support'){response.writeHead(200,{'Content-Type':'application/zip','Content-Disposition':'attachment; filename="update-diagnostics.zip"'});response.end('fixture');return;}
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(ui,'.'+(path==='/'?'/index.html':path)),mime={'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'};
        if(!filename.startsWith(ui)||!mime[extname(filename)])throw Error('Not a static asset');
        response.writeHead(200,{'Content-Type':mime[extname(filename)]});response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    try{
      client=await browser();await client.command('Browser.setDownloadBehavior',{behavior:'deny'});
      await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture'});
      await client.chooseHome('resume');await client.evaluate("document.querySelector('[data-language=de]').click()");
      await client.wait("!document.getElementById('failure-actions').hidden");
      assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/Verwaltungsdatei/);
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/automatischen Wiederholungen/);
      assert.match(await client.evaluate("document.getElementById('failure-cause').textContent"),/WinError 5/);
      assert.match(await client.evaluate("document.getElementById('failure-location').textContent"),/\.ghvrq.*state.json/);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/Nicht .ghvrq/);
      assert.equal(await client.evaluate("document.getElementById('failure-edit-inputs').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('edit-choices').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('install-reset-app-data').hidden"),true);
      await client.picture('metadata-pause-retained-retry-de');
      await client.evaluate("document.getElementById('failure-retry').click()");
      await client.wait("document.getElementById('action-needed').hidden");
      assert.deepEqual(posts.at(-1),{path:'/api/run',body:{session}});
      assert.equal(await client.evaluate("document.getElementById('failure-actions').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('failure-location').hidden"),true);
      kind='update';await client.wait("!document.getElementById('failure-edit-inputs').hidden");
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/PC-Spieldateien/);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/Besitz- und Assembly-Prüfungen können nicht übersprungen/);
      assert.equal(await client.evaluate("document.getElementById('install-reset-app-data').hidden"),true);
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.equal(await client.evaluate("document.getElementById('failure-edit-inputs').textContent"),'Check update inputs');
      await client.picture('update-owned-input-guidance-en');
      await client.evaluate("document.getElementById('failure-support').click()");
      await client.wait("!document.getElementById('failure-support').disabled");
      assert.deepEqual(posts.at(-1),{path:'/api/support',body:{session}});
      const prior=posts.length;
      await client.evaluate("document.getElementById('failure-edit-inputs').click()");
      await client.wait("!document.getElementById('game-page').hidden");
      for(const [id,value] of Object.entries({'game-root':choices.gameRoot,'base-apk':choices.baseApk,'signing-root':choices.signingRoot,'profile-name':choices.profile.displayName,'profile-id':choices.profile.providerId}))
        assert.equal(await client.evaluate('document.getElementById('+JSON.stringify(id)+').value'),value);
      assert.equal(posts.length,prior,'reviewing inputs neither replans, signs, resets nor deletes');
      assert.equal(posts.some(row=>row.path==='/api/plan'||/delete|clean|install/.test(row.path)),false);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });
