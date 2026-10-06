// Installed Chrome only; no driver package, network download or root cache.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp,readFile,writeFile,rm,mkdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

export const chrome=process.env.QUEST_WIZARD_CHROME??'/usr/bin/google-chrome';
export const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
export async function browser() {
  const profile=await mkdtemp(join(tmpdir(),'quest-wizard-api-browser-'));
  // A fresh test profile must not wait for an interactive host keyring. No
  // credentials are stored here; Chrome and its profile are removed below.
  const child=spawn(chrome,['--headless=new','--no-sandbox','--disable-gpu','--disable-dev-shm-usage','--disable-background-networking','--password-store=basic','--use-mock-keychain','--no-first-run','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{stdio:'ignore'});
  let socket;
  async function close(){
    socket?.close();
    if(child.exitCode===null&&child.signalCode===null){
      const exited=new Promise(resolve=>child.once('exit',resolve));
      child.kill('SIGTERM');
      await Promise.race([exited,delay(1000)]);
      if(child.exitCode===null&&child.signalCode===null){child.kill('SIGKILL');await exited;}
    }
    // Chrome's separate writers may release their private profile just after
    // the browser exits; retry directory removal without masking test failures.
    await rm(profile,{recursive:true,force:true,maxRetries:5,retryDelay:100});
  }
  try {
    let port;
    for(let attempt=0;attempt<80;attempt++){try{port=Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{await delay(50);}}
    assert.ok(port,'Chrome exposed its private CDP port');
    const targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
    socket=new WebSocket(targets.find(row=>row.type==='page').webSocketDebuggerUrl);
    await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
    let next=0;const pending=new Map(),events=[];
    socket.addEventListener('message',event=>{const data=JSON.parse(event.data);if(data.method){events.push(data);return;}const callback=pending.get(data.id);if(callback){pending.delete(data.id);clearTimeout(callback.timer);data.error?callback.reject(Error(JSON.stringify(data.error))):callback.resolve(data.result);}});
    socket.addEventListener('close',()=>{for(const row of pending.values()){clearTimeout(row.timer);row.reject(Error('Browser CDP connection closed'));}pending.clear();});
    const command=(method,params={})=>new Promise((resolve,reject)=>{const id=++next;
      const timer=setTimeout(()=>{pending.delete(id);reject(Error('Browser CDP command timed out: '+method));},10000);
      pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));});
    async function evaluate(expression){const result=await command('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});assert.equal(result.exceptionDetails,undefined,expression);return result.result.value;}
    async function wait(expression){for(let index=0;index<80;index++){if(await evaluate(expression))return;await delay(50);}assert.fail('Browser condition: '+expression);}
    async function picture(name){if(!process.env.QUEST_WIZARD_SCREENSHOTS)return;await mkdir(process.env.QUEST_WIZARD_SCREENSHOTS,{recursive:true});const image=await command('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(join(process.env.QUEST_WIZARD_SCREENSHOTS,name+'.png'),Buffer.from(image.data,'base64'));}
    await command('Page.enable');await command('Runtime.enable');await command('Network.enable');
    await command('Emulation.setDeviceMetricsOverride',{width:1366,height:900,deviceScaleFactor:1,mobile:false});
    return {command,evaluate,wait,picture,events,close};
  }catch(error){await close();throw error;}
}
