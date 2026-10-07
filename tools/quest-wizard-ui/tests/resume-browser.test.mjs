// Real DOM/HTTP resume witness; the API fixture never executes build tools.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const ids=['tools','source','unity','profile','inspect','build','install'];
const retainedId='retained-owner-session',recentId='different-older-session';
const source=commit=>({kind:'bundled-release',modVersion:'1.1.0',modBuild:627,sourceCommit:commit.repeat(40)});
function retained(status='failed') {
  return {session:retainedId,status,choices:{gameRoot:'C:\\Owned GOG game',provider:'gog',language:'de',
    acceptUnityTerms:true,install:false,unityEditor:'C:\\Unity\\Editor\\Unity.exe',
    profile:{displayName:'Fixture owner',providerId:'dummy-owner-id'},ownedDlc:['jotl','solo']},
    modSource:source('a'),artwork:[],needsActions:status==='failed'?[{message:{de:'Vorheriger Konvertierungsfehler',en:'Previous conversion failure'}}]:[],
    stages:ids.map((id,index)=>({id,status:status==='complete'||index<5?'complete':index===5?status:'pending',
      progress:{stagePercent:status==='complete'||index<5?100:index===5?28.6:0,phase:index===5?'recovery-batches':'complete',percent:100}})),
    progress:{completed:status==='complete'?7:5,total:7,phase:'build'},
    result:status==='complete'?{apk:'C:\\Outputs\\Quest.apk'}:null};
}
async function fixture() {
  const setup={state:retained(),latest:retainedId,missing:false,requests:[]};
  const servers=[];
  async function open() {
    const server=createServer(async(request,response)=>{
      try {
        const url=new URL(request.url,'http://localhost');
        if(url.pathname.startsWith('/api/')) {
          let body;
          if(request.method==='POST'){let data='';for await(const chunk of request)data+=chunk;body=JSON.parse(data);}
          setup.requests.push({path:url.pathname,method:request.method,body,session:url.searchParams.get('session')});
          let value;
          if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:setup.latest,
            recentSessions:[{session:recentId}],games:[{id:'discovered-copy',gameRoot:'C:\\Other Steam copy',provider:'steam'}],
            unityEditors:[],capabilities:{browse:false,logs:false,support:false},modSource:source('b')};
          else if(url.pathname==='/api/status') {
            if(setup.missing){response.writeHead(404,{'Content-Type':'application/json'});response.end(JSON.stringify({schema:1,event:'error',code:'session_missing',message:{de:'Gespeicherter Vorgang fehlt.',en:'Saved session is missing.'}}));return;}
            value={schema:1,event:'status',state:setup.state};
          }else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
          else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
          else if(url.pathname==='/api/run') {assert.equal(body.session,setup.state.session);setup.state.status='running';value={schema:1,event:'started'};}
          else if(url.pathname==='/api/plan') {
            setup.state={...retained('ready'),session:body.session??'explicit-new-session',choices:body.choices};
            value={schema:1,event:'planned',session:setup.state.session,state:setup.state};
          }else throw Error('Unexpected API: '+url.pathname);
          response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;
        }
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});
        response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));servers.push(server);
    return 'http://127.0.0.1:'+server.address().port+'/#fixture-token';
  }
  return {setup,open,close:async()=>{for(const server of servers)await new Promise(resolve=>server.close(resolve));}};
}
function writes(setup){return setup.requests.filter(row=>row.method==='POST');}

test('startup restores owner choices and total, Continue reuses ID across loopback ports',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture();let client;
    try {
      client=await browser();
      await client.command('Page.navigate',{url:await server.open()});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.equal(writes(server.setup).length,0,'opening a retained session cannot plan or run work');
      assert.equal(await client.evaluate("document.getElementById('game-root').value"),'C:\\Owned GOG game');
      assert.equal(await client.evaluate("document.getElementById('provider').value"),'gog');
      assert.equal(await client.evaluate("document.getElementById('profile-name').value"),'Fixture owner');
      assert.equal(await client.evaluate("document.getElementById('install-choice').checked"),false);
      assert.equal(await client.evaluate("document.getElementById('unity-terms').checked"),true);
      assert.deepEqual(await client.evaluate("[...document.querySelectorAll('input[name=dlc]:checked')].map(row=>row.value)"),['jotl','solo']);
      assert.equal(await client.evaluate("document.getElementById('primary').firstElementChild.textContent"),'Fortsetzen');
      assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'28.6');
      assert.deepEqual(await client.evaluate("[...document.querySelectorAll('#stage-list progress')].map(row=>row.value)"),[100,100,100,100,100,28.6,0]);
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/Vorheriger Konvertierungsfehler/);
      assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/bbbbbbbbbb/);
      assert.match(await client.evaluate("document.getElementById('source-update').textContent"),/aaaaaaaaaa/);
      await client.picture('automatic-retained-build-new-release-de');
      // Origin-scoped local storage is empty at a new launcher's random port.
      await client.command('Page.navigate',{url:await server.open()});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      assert.equal(await client.evaluate("localStorage.getItem('quest-wizard-language')"),'en','new port has its own browser preference; owner build restoration is independent');
      assert.equal(writes(server.setup).length,0);
      assert.match(await client.evaluate("document.getElementById('session-detail').textContent"),new RegExp(retainedId));
      await client.evaluate("document.getElementById('primary').click()");
      await client.wait("!document.getElementById('cancel').hidden");
      assert.deepEqual(writes(server.setup),[{path:'/api/run',method:'POST',body:{session:retainedId},session:null}]);
      assert.equal(server.setup.requests.filter(row=>row.path==='/api/plan').length,0);
      assert.equal(await client.evaluate("document.getElementById('source-update').hidden"),true,'active source stays pinned to its actual receipt');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await server.close();}}
  });

test('saved cancelled/interrupted/ready builds reopen without work; completed build opens its result',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture();let client;
    try {
      client=await browser();const url=await server.open();
      for(const status of ['cancelled','interrupted','ready','complete']) {
        server.setup.state=retained(status);
        await client.command('Page.navigate',{url:url.replace('/#','/?test='+status+'#')});
        await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
        assert.equal(writes(server.setup).length,0,status);
        assert.equal(await client.evaluate("document.getElementById('primary').disabled"),status==='complete');
        assert.equal(await client.evaluate("document.getElementById('result-card').hidden"),status!=='complete');
        if(status==='complete') {
          assert.match(await client.evaluate("document.getElementById('result-path').textContent"),/Quest\.apk/);
          assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/aaaaaaaaaa/);
          assert.equal(await client.evaluate("document.getElementById('source-update').hidden"),true);
        }
      }
    }finally{try{await client?.close();}finally{await server.close();}}
  });

test('choice editing keeps session ID; only explicit new-build selection creates another session',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture();let client;
    try {
      client=await browser();await client.command('Page.navigate',{url:await server.open()});
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.getElementById('edit-choices').click();document.getElementById('primary').click()");
      await client.wait("!document.getElementById('setup-page').hidden");
      await client.evaluate("document.getElementById('primary').click()");
      await client.wait("!document.getElementById('cancel').hidden");
      assert.equal(writes(server.setup).find(row=>row.path==='/api/plan').body.session,retainedId);
      server.setup.state.status='failed';
      await client.evaluate("window.__resumeWitnessOldDocument=true");await client.command('Page.reload');
      // Page.reload acknowledges the command before the replacement document
      // necessarily exists. Wait for its restoration, rather than clicking
      // a still-visible control in the previous running document.
      await client.wait("!window.__resumeWitnessOldDocument&&document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden&&document.getElementById('progress-title').textContent==='Build failed'");
      const before=JSON.parse(JSON.stringify(server.setup.state));
      await client.evaluate("document.getElementById('new-build').click()");
      await client.wait("!document.getElementById('game-page').hidden");
      assert.deepEqual(server.setup.state,before,'new selection cannot delete retained owner state');
      assert.equal(await client.evaluate("document.getElementById('unity-terms').checked"),false,'new choice needs its own review');
      assert.ok(await client.evaluate("Boolean(document.getElementById('resume-session'))"),'retained build remains reopenable');
      await client.evaluate("document.getElementById('primary').click()");
      await client.wait("!document.getElementById('setup-page').hidden");
      await client.evaluate("document.getElementById('unity-terms').checked=true;document.getElementById('primary').click()");
      await client.wait("!document.getElementById('cancel').hidden");
      const plans=writes(server.setup).filter(row=>row.path==='/api/plan');
      assert.equal(plans.length,2);assert.equal(plans[1].body.session,undefined);
      assert.equal(writes(server.setup).at(-1).body.session,'explicit-new-session');
    }finally{try{await client?.close();}finally{await server.close();}}
  });

test('inaccessible retained state is visible and cannot silently start a new session',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const server=await fixture();server.setup.missing=true;let client;
    try {
      client=await browser();await client.command('Page.navigate',{url:await server.open()});
      // Navigation acknowledgment can precede the replacement document.
      await client.wait("Boolean(document.getElementById('error-banner'))&&!document.getElementById('error-banner').hidden&&Boolean(document.getElementById('resume-session'))&&!document.getElementById('resume-session').disabled");
      assert.equal(await client.evaluate("document.getElementById('primary').disabled"),true);
      assert.equal(await client.evaluate("document.getElementById('new-build').hidden"),false);
      assert.equal(await client.evaluate("document.getElementById('resume-notice').hidden"),false);
      await client.evaluate("document.getElementById('primary').click()");await delay(100);
      assert.equal(writes(server.setup).length,0);
      assert.equal(server.setup.requests.filter(row=>row.path==='/api/status').length,1,'no silent fallback to another session');
      await client.picture('inaccessible-retained-build-en');
      server.setup.missing=false;
      await client.evaluate("document.getElementById('resume-session').click()");
      await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      assert.equal(writes(server.setup).length,0);
      assert.equal(await client.evaluate("document.getElementById('error-banner').hidden"),true);
    }finally{try{await client?.close();}finally{await server.close();}}
  });
