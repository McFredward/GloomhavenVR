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
const sourceFor=index=>'https://store.steampowered.com/news/posts/?appids=780290&enddate='+String(1558613054+index);
const stageIds=['tools','source','unity','profile','inspect','build','install'];
test('browser keeps a whole-stage total across phase resets and preserves the visible offline slideshow',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
  let mode=0,client;
  const state=()=>({session:'fixture-local-session',status:mode===5?'complete':'running',choices:{install:true},artwork:[],
    modSource:{kind:'bundled-release',modVersion:'1.1.0',modBuild:627,sourceCommit:'a'.repeat(40)},
    stages:stageIds.map((id,index)=>({id,status:mode===5||index<5?'complete':index===5?'running':'pending',
      progress:index===5?{stagePercent:[70,70.0001,70.01,71,71.5,100][mode],percent:[25,25.001,25.2,5,null,100][mode],
        phase:['recovery-resume-verify','recovery-batch-export-verify','recovery-native-recipe-merge','stage:prepare','stage:build','complete'][mode],
        ...(mode===1||mode===2?{recoveryBatchIndex:mode,recoveryBatchTotal:29}:{}),
        ...(mode===2?{recoveryNativeIndex:2,recoveryNativeTotal:2,done:4,total:20,unit:'files'}:{}),
        activeWork:{operation:mode<3?'recovery':'unity-import',done:mode<3?2+mode:0,total:mode<3?22:1},
        detail:mode===4?'Unity is importing the project.':''}:{stagePercent:index<5?100:0},
      ...(index===5?{timing:{elapsedSeconds:3600+mode,active:mode!==5,elapsedBasis:mode===4?'since-update':'recorded-active',estimate:mode<3?{status:'estimated',scope:'conversion-batches',remainingSeconds:5400,lowerSeconds:4200,upperSeconds:7200,samples:3}:{status:mode===5?'complete':mode===4?'unknown':'learning',scope:'phase'}}}:{})})),
    timing:{elapsedSeconds:8100+mode,active:mode!==5,elapsedBasis:'recorded-active'},
    progress:{completed:mode===5?7:5,total:7,phase:'build'},needsActions:[]});
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
        id,sha256:String(index),url:'/api/promo-artwork?id='+id,altCode:'promoArtwork',source:sourceFor(index),caption:{en:id,de:id}}))};
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
      if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
      response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});
      response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
    client=await browser();
    await client.command('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
    await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
    await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden&&document.querySelectorAll('#gallery .art-card img').length===6");
    await client.wait("[...document.querySelectorAll('#gallery img')].every(node=>node.complete&&node.naturalWidth>0)");
    assert.equal(await client.evaluate("document.querySelector('.privacy')===null&&document.getElementById('footer-note')===null&&document.querySelector('.feature-list')===null&&document.querySelector('.hero-copy')===null&&document.getElementById('gallery-source')===null"),true);
    assert.deepEqual(await client.evaluate("[...document.querySelectorAll('.project-links a')].map(node=>[node.href,node.target,node.rel])"),[['https://github.com/McFredward/GloomhavenVR','_blank','noopener noreferrer'],['https://buymeacoffee.com/mcfredward','_blank','noopener noreferrer']]);
    await client.wait("[...document.querySelectorAll('.project-links img')].every(image=>image.complete&&image.naturalWidth>0)");
    assert.equal(await client.evaluate("document.getElementById('project-coffee').getAttribute('aria-label')"),'Support McFredward on Buy Me a Coffee');
    await client.wait("[...document.querySelectorAll('.hero-brand img')].every(image=>image.complete&&image.naturalWidth>0)");
    assert.deepEqual(await client.evaluate("[...document.querySelectorAll('.hero-brand img')].map(image=>image.alt)"),['GloomhavenVR','Meta Quest']);
    assert.equal(await client.evaluate("document.querySelector('.hero-for').textContent"),'for');
    assert.equal(await client.evaluate("document.querySelector('.hero-brand').getAttribute('aria-label')"),'GloomhavenVR for Meta Quest');
    assert.equal(await client.evaluate("document.querySelector('#gallery img').getBoundingClientRect().width>430"),true,'publisher images occupy a substantial desktop column');
    assert.equal(await client.evaluate("document.getElementById('gallery-controls').hidden"),false);
    assert.equal(await client.evaluate("document.getElementById('gallery-pause').textContent"),'Resume slideshow','reduced motion disables automatic rotation');
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'70');
    assert.equal(await client.evaluate("document.getElementById('substep-label').textContent"),'Teilabschnitt: Gespeicherten Zwischenstand einmalig prüfen');
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'25');
    assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value"),70);
    assert.equal(await client.evaluate("document.querySelectorAll('#gallery .art-card img').length"),6,'opening empty owned art preserves publisher slides');
    assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/1\.1\.0.*B627.*aaaaaaaaaa.*Builder-Paket/);
    assert.equal(await client.evaluate("document.getElementById('progress-completed').textContent"),'Spielinhalte konvertieren: 2 / 22 Teilaufgaben abgeschlossen');
    assert.equal(await client.evaluate("document.getElementById('elapsed-value').textContent"),'02:15:00');
    assert.equal(await client.evaluate("document.getElementById('stage-elapsed').textContent"),'Dieser Arbeitsschritt: 01:00:00');
    assert.equal(await client.evaluate("document.getElementById('eta-label').textContent"),'Geschätzte Restzeit der Datenpakete');
    assert.equal(await client.evaluate("document.getElementById('eta-value').textContent"),'Etwa 01:10:00 – 02:00:00');
    assert.equal(await client.evaluate("document.getElementById('project-coffee').getAttribute('aria-label')"),'McFredward auf Buy Me a Coffee unterstützen');
    await client.evaluate("document.getElementById('project-github').focus();document.querySelector('.site-footer').scrollIntoView({block:'end'})");
    assert.equal(await client.evaluate("document.activeElement.id"),'project-github');
    await client.picture('footer-project-links-desktop-de');
    await client.evaluate("window.scrollTo(0,0)");
    await client.picture('whole-stage-70-phase-25-slideshow-de');
    assert.equal(await client.evaluate("document.querySelector('.hero-for').textContent"),'für');
    assert.equal(await client.evaluate("document.querySelector('.hero-brand').getAttribute('aria-label')"),'GloomhavenVR für Meta Quest');
    mode=1;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='70.0001'");
    assert.equal(await client.evaluate("document.getElementById('progress-fill').style.width"),'70.0001%','bar precision is independent of the shorter percentage label');
    assert.equal(await client.evaluate("document.getElementById('substep-label').textContent"),'Teilabschnitt: Erhaltenen Export des Datenpakets übernehmen · Datenpaket 1 / 29');
    assert.equal(await client.evaluate("document.getElementById('progress-count').textContent"),'70 %');
    mode=2;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='70.01'");
    assert.equal(await client.evaluate("document.getElementById('progress-count').textContent"),'70,01 %','small observed child progress remains visible in the whole-stage label');
    assert.equal(await client.evaluate("document.getElementById('progress-fill').style.width"),'70.01%');
    assert.equal(await client.evaluate("document.getElementById('substep-label').textContent"),'Teilabschnitt: Native Asset-Daten zusammenführen · Datenpaket 2 / 29 · Asset-Verzeichnis 2 / 2');
    assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value"),70.01);
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'25.2');
    assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/4 \/ 20 Dateien/);
    assert.equal(await client.evaluate("document.getElementById('progress-completed').textContent"),'Spielinhalte konvertieren: 4 / 22 Teilaufgaben abgeschlossen');
    await delay(250);
    assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'70.01','elapsed time never invents additional progress');
    mode=3;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='71'");
    assert.equal(await client.evaluate("document.getElementById('substep-track').getAttribute('aria-valuenow')"),'5');
    assert.equal(await client.evaluate("document.querySelectorAll('#stage-list progress')[5].value"),71);
    mode=4;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='71.5'");
    assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false,'unknown native progress is not fabricated');
    assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/Unity is importing|nicht gemeldet/);
    assert.equal(await client.evaluate("document.getElementById('progress-completed').textContent"),'Spielinhalte in Unity importieren: 0 / 1 Teilaufgaben abgeschlossen');
    assert.equal(await client.evaluate("document.getElementById('stage-elapsed').textContent"),'Dieser Arbeitsschritt seit diesem Update: 01:00:04');
    assert.equal(await client.evaluate("document.getElementById('eta-value').textContent"),'Für diesen Abschnitt ist noch keine belastbare Schätzung möglich.');
    await client.picture('whole-stage-71-unknown-unity-phase-de');
    await client.evaluate("document.getElementById('gallery-next').focus()");
    await client.command('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r',unmodifiedText:'\r'});
    await client.command('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
    assert.equal(await client.evaluate("document.getElementById('gallery-count').textContent"),'2 / 6');
    assert.equal(await client.evaluate("document.getElementById('gallery-source')"),null,'publisher attribution remains in developer provenance, outside the product flow');
    await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
    assert.equal(await client.evaluate("getComputedStyle(document.getElementById('gallery')).display==='none'"),false,'slideshow remains available on small screens');
    await client.picture('slideshow-and-progress-mobile-de');
    await client.evaluate("document.querySelector('.site-footer').scrollIntoView({block:'end'})");
    assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'footer links fit a narrow screen');
    await client.picture('footer-project-links-mobile-de');
    await client.evaluate("document.getElementById('build-timing').scrollIntoView({block:'center'})");
    await client.picture('elapsed-eta-mobile-de');
    mode=5;
    await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='100'");
    assert.deepEqual(await client.evaluate("[...document.querySelectorAll('#stage-list progress')].map(node=>node.value)"),[100,100,100,100,100,100,100]);
    assert.equal(await client.evaluate("document.getElementById('substep-progress').hidden"),true);
    assert.equal(await client.evaluate("document.getElementById('progress-completed').hidden"),true);
    assert.equal(await client.evaluate("document.getElementById('eta-value').textContent"),'Abgeschlossen');
    assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
  }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
});
