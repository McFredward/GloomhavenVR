// Real DOM/HTTP witness with controlled observations; never runs a build tool.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {resolve,extname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const artRoot=process.env.QUEST_WIZARD_PROMO_ASSETS??join(root,'assets','promo');
const ids=['cragheart','spellweaver','brute','scoundrel','bandit-guard','bandit-archer'];
const imageNames=['cragheart.png','spellweaver.png','brute.jpg','scoundrel.png','bandit-guard.jpg','bandit-archer.jpg'];
const stageIds=['tools','source','unity','profile','inspect','build','install'];
test('browser keeps a whole-stage total across phase resets and preserves the visible offline slideshow',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
  let mode=0,client;
  const state=()=>({session:'fixture-local-session',status:mode===3?'complete':'running',choices:{install:true},artwork:[],
    modSource:{kind:'bundled-release',modVersion:'1.1.0',modBuild:627,sourceCommit:'a'.repeat(40)},
    stages:stageIds.map((id,index)=>({id,status:mode===3||index<5?'complete':index===5?'running':'pending',
      progress:index===5?{stagePercent:[70,71,71.5,100][mode],percent:[100,5,null,100][mode],
        phase:['recovery-batches','stage:prepare','stage:build','complete'][mode],
        detail:mode===2?'Unity is importing the project.':''}:{stagePercent:index<5?100:0}})),
    progress:{completed:mode===3?7:5,total:7,phase:'build'},needsActions:[]});
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost');
      let value;
      if(url.pathname==='/api/discover'){
        await delay(100); // Empty auto-selection races a slow publisher image.
        value={schema:1,event:'discovery',capabilities:{browse:false,logs:false,support:false},games:[
          {id:'fixture-game',gameRoot:'C:\\Fixture game',provider:'steam',displayName:'Gloomhaven',artwork:[]}],
          recentSessions:[{session:'fixture-local-session'}],unityEditors:[],modSource:state().modSource};
      }else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:ids.map((id,index)=>({
        id,sha256:String(index),url:'/api/promo-artwork?id='+id,altCode:'promoArtwork',caption:{en:id,de:id}}))};
      else if(url.pathname==='/api/status')value={schema:1,event:'status',state:state()};
      else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
      else if(url.pathname==='/api/promo-artwork'){
        await delay(70);
        const index=ids.indexOf(url.searchParams.get('id'));assert.ok(index>=0);
        let filename=join(artRoot,imageNames[index]);
        if(!existsSync(filename))filename=join(root,'assets','gloomhavenvr-logo.png');
        const body=await readFile(filename);
        response.writeHead(200,{'Content-Type':extname(filename)==='.jpg'?'image/jpeg':'image/png','Content-Length':body.length});response.end(body);return;
      }
      if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
      const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
      if(!filename.startsWith(root)||!['.html','.mjs','.css','.png'].includes(extname(filename)))throw Error('not static');
      response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png'}[extname(filename)]});
      response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
    client=await browser();
    await client.command('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
    await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
    await client.wait("document.getElementById('resume-session')&&document.querySelectorAll('#gallery .art-card img').length===6");
    await client.wait("[...document.querySelectorAll('#gallery img')].every(node=>node.complete&&node.naturalWidth>0)");
    assert.equal(await client.evaluate("document.querySelector('.privacy')===null&&document.getElementById('footer-note')===null"),true);
    assert.equal(await client.evaluate("document.querySelector('#gallery img').getBoundingClientRect().width>430"),true,'publisher images occupy a substantial desktop column');
    assert.equal(await client.evaluate("document.getElementById('gallery-controls').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('gallery-pause').textContent"),'Resume slideshow','reduced motion disables automatic rotation');
    await client.evaluate("document.querySelector('[data-language=de]').click();document.getElementById('resume-session').click()");
    await client.wait("!document.getElementById('progress-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'70');
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'100');
    assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value"),70);
    assert.equal(await client.evaluate("document.querySelectorAll('#gallery .art-card img').length"),6,'opening empty owned art preserves publisher slides');
    assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/1\.1\.0.*B627.*aaaaaaaaaa.*Builder-Paket/);
    await client.picture('whole-stage-70-phase-100-slideshow-de');
    mode=1;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='71'");
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'5');
    assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value"),71);
    mode=2;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='71.5'");
    assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false,'unknown native progress is not fabricated');
    assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Unity is importing|nicht gemeldet/);
    await client.picture('whole-stage-71-unknown-unity-phase-de');
    await client.evaluate("document.getElementById('gallery-next').focus()");
    await client.command('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r',unmodifiedText:'\r'});
    await client.command('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
    assert.equal(await client.evaluate("document.getElementById('gallery-count').textContent"),'2 / 6');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
    assert.equal(await client.evaluate("getComputedStyle(document.getElementById('gallery')).display==='none'"),false,'slideshow remains available on small screens');
    await client.picture('slideshow-and-progress-mobile-de');
    mode=3;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='100'");
    assert.deepEqual(await client.evaluate("[...document.querySelectorAll('#stage-list progress')].map(node=>node.value)"),[100,100,100,100,100,100,100]);
    assert.equal(await client.evaluate("document.getElementById('substep-progress').hidden"),true);
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
});
