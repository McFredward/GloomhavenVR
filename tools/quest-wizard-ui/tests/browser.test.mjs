// Bounded real-browser workflow check. The explicit preview API executes no tools.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {mkdtemp,readFile,writeFile,rm,mkdir} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {resolve,join,extname} from 'node:path';
import {fileURLToPath} from 'node:url';

const chrome=process.env.QUEST_WIZARD_CHROME??'/usr/bin/google-chrome';
const root=fileURLToPath(new URL('../',import.meta.url));
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));

test('native browser: bilingual setup, consent, cancellation, choice edits and responsive keyboard access',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
  const profile=await mkdtemp(join(tmpdir(),'quest-wizard-browser-'));
  const server=createServer(async(request,response)=>{
    try {
      const pathname=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
      const filename=resolve(root,'.'+(pathname==='/'?'/index.html':pathname));
      if(!filename.startsWith(root)||!['.html','.mjs','.css','.png'].includes(extname(filename)))throw Error('not static');
      const body=await readFile(filename);
      response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png'}[extname(filename)]});response.end(body);
    }catch{response.writeHead(404);response.end();}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const child=spawn(chrome,['--headless=new','--no-sandbox','--disable-gpu','--disable-dev-shm-usage','--disable-background-networking','--no-first-run','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:'ignore'});
  let socket;
  try {
    let port;
    for(let attempt=0;attempt<80;attempt++){try{port=Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{await delay(50);}}
    assert.ok(port,'Chrome exposed its private CDP port');
    const targets=await (await fetch('http://127.0.0.1:'+port+'/json/list')).json();
    socket=new WebSocket(targets.find(row=>row.type==='page').webSocketDebuggerUrl);
    await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
    let next=0;const pending=new Map();
    socket.addEventListener('message',event=>{const data=JSON.parse(event.data);const callback=pending.get(data.id);if(callback){pending.delete(data.id);data.error?callback.reject(Error(JSON.stringify(data.error))):callback.resolve(data.result);}});
    const command=(method,params={})=>new Promise((resolve,reject)=>{const id=++next;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});
    const evaluate=async(expression)=>{const result=await command('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});assert.equal(result.exceptionDetails,undefined,expression);return result.result.value;};
    async function wait(expression){for(let index=0;index<80;index++){if(await evaluate(expression))return;await delay(50);}assert.fail('Browser condition: '+expression);}
    async function picture(name){if(!process.env.QUEST_WIZARD_SCREENSHOTS)return;await mkdir(process.env.QUEST_WIZARD_SCREENSHOTS,{recursive:true});const image=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(join(process.env.QUEST_WIZARD_SCREENSHOTS,name+'.png'),Buffer.from(image.data,'base64'));}
    await command('Page.enable');await command('Runtime.enable');
    await command('Page.bringToFront');await command('Emulation.setFocusEmulationEnabled',{enabled:true});
    await command('Emulation.setDeviceMetricsOverride',{width:1366,height:768,deviceScaleFactor:1,mobile:false});
    await command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/?preview=1&lang=de'});
    await wait("document.getElementById('primary')&&!document.getElementById('primary').disabled");
    assert.equal(await evaluate('document.documentElement.lang'),'de');
    assert.equal(await evaluate("document.getElementById('discovery-status').textContent"),'Eine lokale Spielkopie gefunden.');
    assert.equal(await evaluate("document.getElementById('provider').value"),'steam');
    assert.equal(await evaluate("document.getElementById('profile-summary').textContent"),'Wird beim Einrichten geprüft');
    assert.equal(await evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true);
    await picture('game-de-1366');
    for(const selector of ['.game-option','[data-language=en]','.advanced summary','#primary']) {
      await evaluate(`document.querySelector(${JSON.stringify(selector)}).focus()`);
      assert.equal(await evaluate(`document.activeElement===document.querySelector(${JSON.stringify(selector)})`),true,'keyboard focus '+selector);
    }
    await command('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r',unmodifiedText:'\r'});
    await command('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
    await wait("!document.getElementById('setup-page').hidden");
    assert.equal(await evaluate("document.activeElement===document.querySelector('#setup-page h2')"),true);
    await evaluate("document.getElementById('primary').click()");
    await wait("!document.getElementById('error-banner').hidden");
    assert.match(await evaluate("document.getElementById('error-banner').textContent"),/Unity-Bedingungen/);
    assert.equal(await evaluate("document.getElementById('progress-page').hidden"),true,'consent prevents even preview plan');
    await evaluate("document.getElementById('unity-terms').checked=true;document.getElementById('primary').click()");
    await wait("!document.getElementById('progress-page').hidden&&!document.getElementById('cancel').hidden");
    assert.equal(await evaluate("document.getElementById('result-card').hidden"),true);
    assert.equal(await evaluate("document.getElementById('progress-track').hasAttribute('aria-valuenow')"),false,'unknown phase percent remains unknown');
    await picture('build-de-1366');
    await evaluate("document.getElementById('cancel').click()");
    await wait("document.getElementById('progress-title').textContent==='Der Build ist angehalten.'");
    await delay(350);
    await picture('resume-de-1366');
    await evaluate("document.getElementById('load-log').click()");
    await wait("!document.getElementById('stage-log').hidden");
    assert.match(await evaluate("document.getElementById('stage-log').textContent"),/no external tools/);
    await evaluate("document.getElementById('edit-choices').click()");
    await wait("!document.getElementById('game-page').hidden");
    assert.equal(await evaluate("document.getElementById('unity-terms').checked"),true,'previous choices restored');
    await evaluate("document.querySelector('[data-language=en]').click()");
    assert.equal(await evaluate('document.documentElement.lang'),'en');
    assert.equal(await evaluate("document.getElementById('discovery-status').textContent"),'Found one local game copy.');
    await command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await evaluate('document.documentElement.scrollWidth<=window.innerWidth'),true,'mobile has no horizontal clipping');
    await picture('game-en-mobile');
  }finally{
    socket?.close();child.kill('SIGTERM');
    await Promise.race([new Promise(resolve=>child.once('exit',resolve)),delay(1000)]);
    if(child.exitCode===null)child.kill('SIGKILL');
    await new Promise(resolve=>server.close(resolve));await rm(profile,{recursive:true,force:true});
  }
});
