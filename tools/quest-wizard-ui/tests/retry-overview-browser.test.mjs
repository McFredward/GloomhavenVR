// Actual Engine, receipt ownership, loopback API and Chrome retry witness.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const fixture=fileURLToPath(new URL('./retry_server.py',import.meta.url));
const repo=resolve(fileURLToPath(new URL('../',import.meta.url)),'../..');
const python=process.env.QUEST_WIZARD_PYTHON??'python3';

test('actual runner clears old failures before prerequisites, clears nested retry pointers, and reports only a new failure',
 {skip:!existsSync(chrome),timeout:45000},async t=>{
  const root=await mkdtemp(join(tmpdir(),'quest-wizard-retry-overview-'));
  const child=spawn(python,['-I','-B',fixture,repo,root],{stdio:['ignore','pipe','pipe']});
  let stderr='',client,startupTimer;
  child.stderr.on('data',data=>{stderr+=data;});
  const started=new Promise((resolve,reject)=>{
    let output='';child.stdout.on('data',data=>{output+=data;const line=output.split('\n')[0];if(output.includes('\n')){try{resolve(JSON.parse(line));}catch(error){reject(error);}}});
    child.once('exit',code=>reject(Error('Fixture stopped '+code+': '+stderr)));
  });
  const waitFile=async name=>{for(let index=0;index<200;index++){if(existsSync(join(root,name)))return;await delay(20);}assert.fail('Owned runner pause '+name+': '+stderr);};
  const release=async(attempt,phase)=>writeFile(join(root,phase+'-'+attempt+'.continue'),'continue');
  try{
    const metadata=await Promise.race([started,new Promise((_,reject)=>{startupTimer=setTimeout(()=>reject(Error('No fixture startup: '+stderr)),10000);})]);
    clearTimeout(startupTimer);
    client=await browser();await client.command('Page.navigate',{url:metadata.url});
    await client.wait("document.getElementById('action-needed')&&!document.getElementById('action-needed').hidden&&!document.getElementById('failure-retry').disabled&&document.querySelector('[data-plan=staging] [data-operation=canonical]')");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.match(await client.evaluate("document.querySelector('[data-plan=staging] [data-operation=canonical]').textContent"),/Fehlgeschlagen/);
    const retained=await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value");
    assert.ok(retained>0);
    for(const attempt of [2,3]){
      t.diagnostic('Starting owned retry '+attempt);
      await client.evaluate("document.getElementById('failure-retry').click()");
      await waitFile('prerequisites-'+attempt+'.waiting');
      await client.wait("document.getElementById('action-needed').hidden&&document.getElementById('build-current').textContent.includes('Wartet')");
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value")>=retained,true,'retained build percentage survives its queued retry');
      assert.equal(await client.evaluate("document.querySelectorAll('#build-groups li.failed,#build-groups li.running,#build-groups li.checking').length"),0,'old attempt has no current or failed nested rows while prerequisite receipts wait');
      assert.equal(await client.evaluate("document.getElementById('build-retained').hidden"),false);
      await client.picture('actual-retry-'+attempt+'-prerequisites-de');
      await release(attempt,'prerequisites');await waitFile('recovery-'+attempt+'.waiting');
      await client.wait("document.getElementById('build-current').textContent.includes('Spielinhalte konvertieren')");
      assert.equal(await client.evaluate("document.querySelectorAll('[data-plan=staging] li.running,[data-plan=staging] li.failed,[data-plan=staging] li.checking').length"),0,'reopening recovery does not reopen the previous failed child');
      await client.picture('actual-retry-'+attempt+'-recovery-before-child-de');
      await release(attempt,'recovery');await waitFile('copy-'+attempt+'.waiting');
      await client.wait("document.querySelector('[data-plan=staging] [data-operation=copy]').className==='running'");
      assert.equal(await client.evaluate("document.querySelector('[data-plan=staging] [data-operation=canonical]').className"),'reused');
      assert.equal(await client.evaluate("document.querySelectorAll('#build-groups li.failed').length"),0);
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/10 \/ 100.*current attempt copy item/);
      await client.picture('actual-retry-'+attempt+'-copy-de');
      await release(attempt,'copy');
      t.diagnostic('Released copy '+attempt);
      if(attempt===2){
        await client.wait("!document.getElementById('action-needed').hidden&&document.getElementById('failure-cause').textContent.includes('current copy failure')");
        assert.deepEqual(await client.evaluate("[...document.querySelectorAll('[data-plan=staging] li.failed')].map(row=>row.dataset.operation)"),['copy']);
        assert.equal(await client.evaluate("document.querySelector('[data-plan=staging] [data-operation=canonical]').className"),'reused');
        t.diagnostic('Confirmed new copy failure');
      }else await client.wait("document.getElementById('progress-title').textContent==='Build abgeschlossen.'");
    }
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{
    clearTimeout(startupTimer);
    try{await client?.close();}finally{
      if(child.exitCode===null){const stopped=new Promise(resolve=>child.once('exit',resolve));child.kill('SIGINT');await Promise.race([stopped,delay(1500)]);if(child.exitCode===null){child.kill('SIGKILL');await stopped;}}
      await rm(root,{recursive:true,force:true});
    }
  }
 });
