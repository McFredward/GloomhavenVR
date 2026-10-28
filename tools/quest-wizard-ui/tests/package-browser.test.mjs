// Actual Chrome over production HTTP and ZIP writer; no Unity or headset claim.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import {mkdtemp,readFile,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const repo=resolve(fileURLToPath(new URL('../',import.meta.url)),'../..');
test('portable export measures one job, cancels/retries safely, and ZIP updates keep explicit paths and automatic signing',
 {skip:!existsSync(chrome),timeout:45000},async()=>{
  const root=await mkdtemp(join(tmpdir(),'quest-package-browser-'));
  const child=spawn(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',fileURLToPath(new URL('./package_server.py',import.meta.url)),repo,root],{stdio:['ignore','pipe','pipe']});
  let output='',errors='',client;
  child.stdout.on('data',chunk=>output+=chunk);child.stderr.on('data',chunk=>errors+=chunk);
  try{
    for(let i=0;i<100&&!output.includes('\n')&&child.exitCode===null;i++)await delay(50);
    assert.ok(output.includes('\n'),errors);const setup=JSON.parse(output.split('\n')[0]);
    client=await browser();await client.command('Page.navigate',{url:setup.url});
    await client.wait("document.getElementById('home-export')&&!document.getElementById('home-export').hidden&&!document.getElementById('home-export').disabled");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    assert.equal(await client.evaluate("document.getElementById('package-source').value"),setup.source);
    await client.wait("document.getElementById('package-destination').value&&!document.getElementById('package-export').disabled");
    assert.equal(await client.evaluate("document.getElementById('package-destination').value"),join(setup.root,'exports/GloomhavenVR-Quest-B671.zip'));
    assert.equal(await client.evaluate("['package-card','device-card','storage-card'].every(id=>document.getElementById(id).hidden)"),true,'home shows only operation tiles');
    await client.chooseHome('export');
    assert.equal(await client.evaluate("document.getElementById('package-card').hidden"),false);
    assert.equal(await client.evaluate("document.activeElement.id"),'package-title');
    assert.equal(await client.evaluate("document.querySelector('.steps').hidden&&document.getElementById('primary').hidden"),true,'export is a dedicated utility page');
    await client.evaluate("document.getElementById('package-export').click()");
    await client.wait("!document.getElementById('package-progress').hidden&&document.getElementById('package-progress').value>0");
    const before=await client.evaluate("document.getElementById('package-progress').value");assert.ok(before>0&&before<100);
    assert.equal(await client.evaluate("document.getElementById('package-progress').offsetWidth>=document.getElementById('package-card').clientWidth*.8"),true);
    assert.equal(await client.evaluate("document.getElementById('home-profile').disabled"),true);
    await client.evaluate("document.getElementById('home').click()");
    assert.equal(await client.evaluate("document.getElementById('home-export').disabled"),false,'navigation can return to the live export');
    await client.chooseHome('export');
    assert.equal(await client.evaluate("document.getElementById('package-cancel').hidden"),false,'navigation retains the export job');
    assert.equal(await client.evaluate("document.getElementById('device-connect').disabled"),true);
    assert.match(await client.evaluate("document.getElementById('package-status').textContent"),/%.*KiB/s);
    await client.picture('portable-export-progress-de');
    await client.evaluate("document.getElementById('package-cancel').click()");
    await client.wait("document.getElementById('package-cancel').hidden&&document.getElementById('package-status').textContent.includes('angehalten')");
    assert.equal(existsSync(join(setup.root,'exports/GloomhavenVR-Quest-B671.zip')),false);
    assert.ok(existsSync(setup.source));
    await client.evaluate("document.getElementById('package-export').click()");
    await client.wait("!document.getElementById('package-cancel').hidden");
    await writeFile(join(root,'continue-2'),'continue');
    await client.wait("document.getElementById('package-progress').value===100&&!document.getElementById('package-result').hidden");
    const exported=await client.evaluate("document.getElementById('package-result').textContent");assert.ok(existsSync(exported));
    await client.picture('portable-export-complete-de');
    await client.evaluate('document.getElementById("package-source").value='+JSON.stringify(exported)+';document.getElementById("package-source").dispatchEvent(new Event("input"))');
    assert.equal(await client.evaluate("document.getElementById('package-destination').value"),exported.replace(/\.zip$/i,'-copy.zip'));
    const customDestination=join(root,'chosen-destination.zip');
    await client.evaluate('document.getElementById("package-destination").value='+JSON.stringify(customDestination)+';document.getElementById("package-destination").dispatchEvent(new Event("input"));document.querySelector("[data-language=en]").click()');
    assert.equal(await client.evaluate("document.getElementById('package-destination').value"),customDestination);
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('profile');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),setup.source);
    assert.equal(await client.evaluate("['package-card','device-card','storage-card'].every(id=>document.getElementById(id).hidden)"),true,'profile form exposes no utilities');
    await client.evaluate('document.getElementById("base-apk").value='+JSON.stringify(exported)+';document.getElementById("base-apk").dispatchEvent(new Event("input"));document.getElementById("profile-name").value="Recipient";document.getElementById("primary").click()');
    await client.wait("!document.getElementById('setup-page').hidden");
    assert.equal(await client.evaluate("document.querySelector('.license-note').hidden"),true);
    assert.equal(await client.evaluate("document.getElementById('signing-mode').value"),'auto');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),exported);
    await client.evaluate("document.querySelector('[data-language=en]').click()");
    assert.match(await client.evaluate("document.querySelector('label[for=base-apk]').textContent"),/ZIP/);
    assert.match(await client.evaluate("document.querySelector('[data-i18n=signingHint]').textContent"),/local key/);
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
    await client.picture('portable-profile-zip-mobile-en');
    await client.evaluate("document.getElementById('home').click()");await client.chooseHome('mod');
    assert.equal(await client.evaluate("document.getElementById('base-apk').value"),exported);
    assert.equal(await client.evaluate("document.getElementById('build-mode').value"),'update-mod');
    assert.equal(await client.evaluate("document.getElementById('signing-mode').value"),'auto');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    const lines=(await readFile(join(setup.root,'logs/package-export.jsonl'),'utf8')).trim().split('\n').map(JSON.parse);
    assert.ok(lines.some(row=>row.event==='export_cancelled'));
    assert.ok(lines.some(row=>row.event==='export_complete'));
  }finally{
    await client?.close();
    if(child.exitCode===null&&child.signalCode===null){const exit=new Promise(resolve=>child.once('exit',resolve));child.kill('SIGINT');await Promise.race([exit,delay(3000)]);if(child.exitCode===null&&child.signalCode===null){child.kill('SIGKILL');await exit;}}
    await rm(root,{recursive:true,force:true});
  }
});
