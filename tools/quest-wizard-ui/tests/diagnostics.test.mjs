// Actual loopback adapter and browser, with explicitly isolated Engine fixtures.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import {mkdtemp,rm,readFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const backend=process.env.QUEST_WIZARD_BACKEND;
const promoPins=JSON.parse(await readFile(new URL('../promo-artwork.json',import.meta.url),'utf8')).images;
test('actual browser: measured progress, reopen/check Unity action, specific failure and live log',
  {skip:!backend||!existsSync(backend)||!existsSync(chrome),timeout:30000},async()=>{
  const stateRoot=await mkdtemp(join(tmpdir(),'quest-wizard-actions-'));
  const uiRoot=fileURLToPath(new URL('../',import.meta.url));
  const fixture=fileURLToPath(new URL('./prerequisite_server.py',import.meta.url));
  const child=spawn(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',fixture,backend,stateRoot,uiRoot,process.env.QUEST_WIZARD_PROMO_CACHE??join(stateRoot,'no-artwork')],{stdio:['ignore','pipe','pipe']});
  let client;
  try{
    let output='',errors='';child.stdout.on('data',data=>{output+=data;});child.stderr.on('data',data=>{errors+=data;});
    for(let index=0;index<100&&!output.includes('\n')&&child.exitCode===null;index++)await delay(50);
    assert.equal(child.exitCode,null,errors);const started=JSON.parse(output.split('\n')[0]);
    client=await browser();await client.command('Page.navigate',{url:started.url});
    await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    if(process.env.QUEST_WIZARD_PROMO_CACHE){
      await client.wait(`document.querySelectorAll('#gallery img').length===${promoPins.length}&&[...document.querySelectorAll('#gallery img')].every(image=>image.naturalWidth>0)`);
      assert.equal(await client.evaluate("[...document.querySelectorAll('#gallery > *')].filter(node=>!node.hidden).length"),1);
      await client.evaluate("document.getElementById('gallery-next').click()");
      assert.equal(await client.evaluate("document.getElementById('gallery-count').textContent"),'2 / '+promoPins.length);
      await client.evaluate("document.getElementById('gallery-pause').click()");
      assert.equal(await client.evaluate("document.getElementById('gallery-pause').textContent"),'Diashow fortsetzen');
    }
    await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('unity-actions').hidden");
    assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Benutzeraktion erforderlich');
    assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/In Unity Hub anmelden/);
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'50');
    assert.equal(await client.evaluate("document.getElementById('unity-check').getBoundingClientRect().bottom<window.innerHeight"),true,'required user action is visible without scrolling');
    const statusExpression=`(async()=>await(await fetch('/api/status?session=${started.session}',{headers:{'X-Quest-Token':location.hash.slice(1)}})).json())()`;
    const before=await client.evaluate(statusExpression);const nonce=before.state.stages.find(row=>row.id==='unity').waiting.nonce;
    assert.equal(Number(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')")),before.state.stages.find(row=>row.id==='unity').progress.stagePercent??0,'whole-stage value is independent of the 50% prerequisite phase');
    await client.picture('unity-action-official-slideshow-de');
    await client.evaluate("document.getElementById('unity-open').click()");
    await delay(1100);
    const again=await client.evaluate(statusExpression);
    assert.equal(again.state.status,'running');assert.equal(again.state.stages.find(row=>row.id==='unity').waiting.nonce,nonce);
    assert.equal(again.state.events.filter(row=>row.code==='fixture_window_opened').length,2);
    assert.equal(await client.evaluate("document.getElementById('unity-actions').hidden"),false);
    await client.evaluate("document.querySelector('#diagnostic-details').open=true;document.getElementById('unity-check').click()");
    await client.wait("document.getElementById('progress-title').textContent==='Build fehlgeschlagen'");
    assert.equal(await client.evaluate("document.getElementById('action-needed-title').textContent"),'Build fehlgeschlagen');
    assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/^Spielinhalte konvertieren: .*fehlgeschlagen/);
    assert.equal(await client.evaluate("document.getElementById('log-stage').value"),'build');
    await client.wait("document.getElementById('stage-log').textContent.includes('FAILED: original object identity missing')");
    assert.match(await client.evaluate("document.getElementById('event-log').textContent"),/Spielassets|original object/);
    assert.equal(await client.evaluate("document.getElementById('result-card').hidden"),true);
    assert.match(await client.evaluate("document.getElementById('substep-count').textContent"),/24,1 %/);
    await client.picture('specific-failure-live-log-de');
    const paths=client.events.filter(row=>row.method==='Network.requestWillBeSent').map(row=>new URL(row.params.request.url).pathname);
    assert.equal(paths.filter(path=>path==='/api/action').length,2);
  }finally{
    try{await client?.close();}finally{
      if(child.exitCode===null&&child.signalCode===null){const exited=new Promise(resolve=>child.once('exit',resolve));child.kill('SIGINT');await Promise.race([exited,delay(1500)]);if(child.exitCode===null&&child.signalCode===null){child.kill('SIGKILL');await exited;}}
      await rm(stateRoot,{recursive:true,force:true,maxRetries:5,retryDelay:100});
    }
  }
});
