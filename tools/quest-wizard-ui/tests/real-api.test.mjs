// This witness calls the actual HTTP backend but never starts a child build.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {existsSync} from 'node:fs';
import {mkdtemp,mkdir,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const backend=process.env.QUEST_WIZARD_BACKEND;
const execute=promisify(execFile);
test('actual loopback HTTP browser: module/CSP/token flow, plan, reopen, safe logs; no tools',
  {skip:!backend||!existsSync(backend)||!existsSync(chrome),timeout:30000},async()=>{
  const stateRoot=await mkdtemp(join(tmpdir(),'quest-wizard-real-api-'));
  const uiRoot=fileURLToPath(new URL('../',import.meta.url));
  const child=spawn(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',backend,'serve','--state-root',stateRoot,'--ui-root',uiRoot],{stdio:['ignore','pipe','pipe']});
  let client;
  try {
    let output='',startupError='';child.stdout.on('data',data=>{output+=data.toString();});child.stderr.on('data',data=>{startupError+=data.toString();});
    for(let attempt=0;attempt<100&&!output.includes('\n')&&child.exitCode===null;attempt++)await delay(50);
    assert.equal(child.exitCode,null,startupError);
    const event=JSON.parse(output.split('\n')[0]);assert.equal(event.event,'server');
    client=await browser();
    // Fail closed even if a future UI change accidentally attempts to run tools.
    await client.command('Fetch.enable',{patterns:[{urlPattern:'*/api/run',requestStage:'Request'}]});
    await client.command('Page.navigate',{url:event.url});
    await client.wait("document.getElementById('discovery-status')&&!document.getElementById('discovery-status').textContent.includes('gesucht')");
    assert.equal(await client.evaluate("document.getElementById('preview-notice').hidden"),true);
    assert.equal(await client.evaluate("document.getElementById('error-banner').hidden"),true,'modules/discovery: '+await client.evaluate("document.getElementById('error-banner').textContent")+' backend: '+startupError);
    assert.equal(await client.evaluate("document.querySelector('.brand img').naturalWidth"),1280);
    const source=await client.evaluate("(async()=>{const r=await fetch('app.mjs');return {mime:r.headers.get('Content-Type'),csp:r.headers.get('Content-Security-Policy')}})()");
    assert.match(source.mime,/javascript/);assert.match(source.csp,/img-src[^;]*blob:/);
    const fixtureGame=process.env.QUEST_WIZARD_OWNED_GAME??'/private fixture/Owned Game';
    const planBody=JSON.stringify({choices:{gameRoot:fixtureGame,provider:'steam',language:'de',acceptUnityTerms:true,install:false}});
    const planned=await client.evaluate(`(async()=>{const r=await fetch('/api/plan',{method:'POST',headers:{'X-Quest-Token':location.hash.slice(1),'Content-Type':'application/json'},body:${JSON.stringify(planBody)}});return {status:r.status,value:await r.json()}})()`);
    assert.equal(planned.status,200);assert.equal(planned.value.state.status,'ready');assert.equal(planned.value.state.progress.percent,null);
    const session=planned.value.session;
    let ownedArt=null;
    if(process.env.QUEST_WIZARD_OWNED_RECOVERY&&process.env.QUEST_WIZARD_OWNED_INPUT) {
      assert.ok(process.env.QUEST_WIZARD_OWNED_GAME,'Actual owned artwork needs an explicitly selected owned game path.');
      // Inherit original input/recovery provenance in a manually established,
      // isolated fixture receipt. This is not a fresh inspect/build invocation.
      const seed=await execute(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',fileURLToPath(new URL('./seed_owned_artwork.py',import.meta.url)),
        '--backend',dirname(backend),'--state-root',stateRoot,'--session',session,'--ui-root',uiRoot,
        '--recovery',process.env.QUEST_WIZARD_OWNED_RECOVERY,'--input',process.env.QUEST_WIZARD_OWNED_INPUT]);
      ownedArt=JSON.parse(seed.stdout);
      assert.equal(ownedArt.count,3);
    }
    const logs=join(stateRoot,'sessions',session,'logs');await mkdir(logs,{recursive:true});
    const literal='<script>window.unsafelyExecuted=true</script>\nfixture: no tools were run';
    await writeFile(join(logs,'tools.log'),literal);
    await client.command('Page.reload');await client.wait("Boolean(document.getElementById('resume-session'))");
    await client.evaluate("document.querySelector('[data-language=de]').click();document.getElementById('resume-session').click()");
    await client.wait("!document.getElementById('progress-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('result-card').hidden"),true);
    if(ownedArt) {
      await client.wait("document.querySelectorAll('#gallery img').length===3&&[...document.querySelectorAll('#gallery img')].every(image=>image.naturalWidth>0)");
      const pixels=await client.evaluate("(async()=>{const state=await(await fetch('/api/status?session="+session+"',{headers:{'X-Quest-Token':location.hash.slice(1)}})).json();const rows=[];for(const image of state.state.artwork){const r=await fetch(image.url,{headers:{'X-Quest-Token':location.hash.slice(1)}});const bytes=await r.arrayBuffer();rows.push({id:image.id,status:r.status,type:r.headers.get('Content-Type'),sha256:[...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(value=>value.toString(16).padStart(2,'0')).join('')});}return rows})()");
      assert.deepEqual(pixels.map(row=>row.sha256),ownedArt.sha256);
      assert.ok(pixels.every(row=>row.status===200&&row.type==='image/png'));
      const images=await client.evaluate("[...document.querySelectorAll('#gallery img')].map(image=>({ratio:image.naturalWidth/image.naturalHeight,fit:getComputedStyle(image).objectFit,caption:image.parentElement.querySelector('span')!==null}))");
      assert.ok(images.filter(image=>image.ratio>=1.45).length>=2);
      assert.ok(images.filter(image=>image.ratio>=1.45).every(image=>image.fit==='contain'),'landscape titles remain fully visible');
      assert.ok(images.every(image=>image.caption===false),'no repeated caption covers original artwork');
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.ok((await client.evaluate("[...document.querySelectorAll('#gallery img')].map(node=>node.alt)")).every(value=>value==='Artwork from your local game copy'));
      assert.equal(await client.evaluate("document.getElementById('gallery-note').textContent"),'Artwork from your local game copy');
      await client.evaluate("document.querySelector('[data-language=de]').click()");
    }
    await client.evaluate("document.querySelector('#progress-page details').open=true;document.getElementById('load-log').click()");
    await client.wait("!document.getElementById('stage-log').hidden");
    assert.equal(await client.evaluate("document.getElementById('stage-log').textContent"),literal);
    assert.equal(await client.evaluate('window.unsafelyExecuted===true'),false);
    const denied=await client.evaluate("(async()=>{const r=await fetch('/api/discover',{headers:{'X-Quest-Token':'wrong-token'}});return {status:r.status,value:await r.json()}})()");
    assert.equal(denied.status,403);assert.equal(denied.value.code,'request_token');
    assert.equal(client.events.filter(row=>row.method==='Fetch.requestPaused').length,0,'no run request was attempted');
    const requests=client.events.filter(row=>row.method==='Network.requestWillBeSent').map(row=>new URL(row.params.request.url).pathname);
    for(const path of ['/api/discover','/api/plan','/api/status','/api/log'])assert.ok(requests.includes(path),path);
    await client.picture(ownedArt?'real-api-owned-artwork-de':'real-api-ready-de');
  }finally{
    await client?.close();child.kill('SIGINT');await Promise.race([new Promise(resolve=>child.once('exit',resolve)),delay(1500)]);if(child.exitCode===null)child.kill('SIGKILL');
    await rm(stateRoot,{recursive:true,force:true});
  }
});
