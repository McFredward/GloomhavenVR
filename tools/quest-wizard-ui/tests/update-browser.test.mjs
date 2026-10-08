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

for(const mode of ['update-profile','update-mod'])test('native browser: '+mode+' requires the APK, restores choices and applies its Unity requirement',
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
    await client.wait("document.getElementById('primary')&&!document.getElementById('primary').disabled");
    await client.evaluate("document.getElementById('build-mode').value="+JSON.stringify(mode)+";document.getElementById('build-mode').dispatchEvent(new Event('change'))");
    assert.equal(await client.evaluate("document.getElementById('base-apk-choice').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('update-signing-choice').hidden"),false);
    await client.evaluate("document.getElementById('primary').click()");
    await client.wait("!document.getElementById('error-banner').hidden");
    assert.match(await client.evaluate("document.getElementById('error-banner').textContent"),/APK/);
    assert.equal(await client.evaluate("document.getElementById('setup-page').hidden"),true);
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
