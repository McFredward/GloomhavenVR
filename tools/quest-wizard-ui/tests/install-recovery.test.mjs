// The captured build finished before installation failed. These witnesses
// exercise the local UI/API boundary, not a Quest or a native Player build.
import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {resolve,extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {installationView,stageProgress,stageStatus} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {strings,translate} from '../i18n.mjs';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url)),session='completed-install-session';
const ids=['tools','source','unity','profile','inspect','build','install'];
const apk='C:\\Users\\Owner\\.ghvrq\\build\\builds\\input\\GloomhavenVR-Quest.apk';
function fixture(code='quest_connection_required',cause='No authorized USB Quest was found.',status='blocked') {
  return {session,status,choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:true},artwork:[],
    modSource:{sourceCommit:'3'.repeat(40),modBuild:665,modVersion:'1.1.0',kind:'bundled-release'},
    needsActions:[{code,stage:'install',message:{de:'Keine freigegebene Quest gefunden.',en:'No authorized Quest was found.'},
      parameters:{failureStage:'install',cause,completedBuildRetained:true,canSkipInstall:true,retryAction:'install'}}],
    stages:ids.map(id=>({id,status:id==='install'?status:'complete',details:id==='build'?{apk,inputKey:'1'.repeat(64),apkSha256:'2'.repeat(64),modBuild:665}: {},
      progress:{stagePercent:id==='install'?0:100,phase:id==='install'?'operation:connect':'complete',percent:id==='install'?null:100}}))};
}

test('connection prerequisite preserves completed build and exposes guarded installation actions',()=>{
  const state=fixture(),view=installationView(state);
  assert.deepEqual(view,{attention:true,connectionRequired:true,canSkip:true,cause:'No authorized USB Quest was found.',paths:{apk}});
  assert.equal(stageProgress(state.stages[5]).percent,100);
  assert.equal(stageStatus(state,state.stages[5]),'complete');
  for(const language of ['de','en'])for(const key of ['questConnectionTitle','questConnectionNext','installationFailedTitle','installationNext','installationRetry','installationSkip']){
    assert.notEqual(translate(language,key),key);
    assert.deepEqual([...strings.de[key].matchAll(/\{([^}]+)\}/g)], [...strings.en[key].matchAll(/\{([^}]+)\}/g)]);
  }
  assert.match(translate('de','questConnectionNext'),/USB-Debugging-Freigabe/);
  assert.doesNotMatch(translate('de','questConnectionNext'),/korrigierten Builder/);
});

test('live connection waiting preserves measured content and does not enable a second install',()=>{
  const state=fixture();state.status='running';state.needsActions=[];
  state.stages[6].status='running';
  state.stages[6].progress={phase:'install-connection-wait',operationStatus:'waiting',stageOperation:'content',
    done:536870912,total:1073741824,unit:'bytes',percent:50,stagePercent:60};
  assert.equal(installationView(state).connectionWaiting,true);
  assert.equal(installationView(state).attention,false);
  assert.equal(installationView(state).canSkip,false);
  assert.equal(stageProgress(state.stages[6]).percent,60);
  assert.equal(stageProgress(state.stages[6]).phasePercent,50);
  for(const status of ['cancelling','cancel_requested','complete','failed','blocked']){
    const stopped=structuredClone(state);stopped.status=status;
    assert.equal(installationView(stopped).connectionWaiting,undefined,status);
  }
  for(const phase of ['install-content','operation:connect']){
    const resumed=structuredClone(state);resumed.stages[6].progress.phase=phase;
    assert.equal(installationView(resumed).connectionWaiting,undefined,phase);
  }
  for(const operationStatus of ['complete','reuse']){
    const returned=structuredClone(state);returned.stages[6].progress.operationStatus=operationStatus;
    assert.equal(installationView(returned).connectionWaiting,undefined,operationStatus);
  }
  for(const language of ['de','en'])for(const key of ['questConnectionWaitingTitle','questConnectionWaitingCopy',
    'questConnectionWaitingNext','questConnectionWaitingActivity','questGuideStartLiveWait','phase_install-connection-wait']){
    assert.notEqual(translate(language,key),key);
    assert.deepEqual([...strings.de[key].matchAll(/\{([^}]+)\}/g)],[...strings.en[key].matchAll(/\{([^}]+)\}/g)]);
  }
});

test('legacy child failure and precise signing error never become a connection prerequisite',()=>{
  for(const [code,cause] of [['child_failed','No error summary was emitted; inspect the retained tool log.'],
    ['installation_tool_failed','INSTALL_PARSE_FAILED_NO_CERTIFICATES: APK has no valid signature']]){
    const state=fixture(code,cause,'failed');
    state.needsActions[0].parameters={failureStage:'install',cause};
    assert.deepEqual(installationView(state),{attention:true,connectionRequired:false,canSkip:true,cause,paths:{apk}});
  }
  assert.doesNotMatch(translate('en','installationNext'),/authorize|USB|corrected builder/);
});

test('automatic signature migration and safety refusals retain the APK without USB or key-specific advice',()=>{
  const state=fixture('quest_signing_recovery_required','Signing migration could not safely continue: restore access failed. Retained backup: device/saved-data','blocked');
  state.needsActions[0].parameters.cause="Signing migration could not safely continue: tar: cannot read unknown save\r\n\r\nRetained backup: device/saved-data";
  state.needsActions[0].parameters.backupPath='C:\\Users\\Owner\\.ghvrq\\device\\signing-recovery\\Quest\\saved-data';
  assert.equal(installationView(state).recoveryRequired,true);
  assert.equal(installationView(state).connectionRequired,false);
  assert.equal(installationView(state).canSkip,true);
  assert.match(installationView(state).cause,/^Signing migration could not safely continue: tar: cannot read unknown save/);
  assert.equal(installationView(state).backupPath,state.needsActions[0].parameters.backupPath);
  for(const path of ['x'.repeat(2049),'folder\nunsafe']){
    const changed=structuredClone(state);changed.needsActions[0].parameters.backupPath=path;
    assert.equal(installationView(changed).backupPath,undefined);
  }
  for(const phase of ['install-signature-backup','install-signature-replace','install-signature-restore','install-signature-verify','install-recovery-backup','install-recovery-restore','install-recovery-access','install-recovery-backup-work']){
    const active=structuredClone(state);active.status='running';active.needsActions=[];
    active.stages[6].status='running';active.stages[6].progress.phase=phase;
    assert.equal(installationView(active).recoveryActive,true,phase);
    assert.equal(installationView(active).canSkip,false,phase);
    for(const language of ['de','en'])assert.notEqual(translate(language,'phase_'+phase),'phase_'+phase);
  }
  const restored=structuredClone(state);restored.status='running';restored.stages[6].status='running';restored.stages[6].progress.phase='install-content';
  assert.equal(installationView(restored).recoveryActive,undefined);
  for(const language of ['de','en'])for(const key of ['installationRecoveryRunningTitle','installationRecoveryRunningCopy','installationRecoveryTitle','installationRecoveryCopy','installationRecoveryAction','installationRecoveryNext']){
    assert.notEqual(translate(language,key),key);
    assert.deepEqual([...strings.de[key].matchAll(/\{([^}]+)\}/g)], [...strings.en[key].matchAll(/\{([^}]+)\}/g)]);
  }
  assert.match(translate('de','installationRecoveryNext'),/ohne die APK neu zu bauen/);
  assert.doesNotMatch(translate('de','installationRecoveryNext'),/USB-Debugging|original.*Schlüssel|B625/i);
});

test('earlier failed work, conflicting stages and active runs cannot authorize skipping installation',()=>{
  const state=fixture();
  for(let index=0;index<6;index++){
    const changed=structuredClone(state);changed.stages[index].status='failed';
    assert.equal(installationView(changed),null,ids[index]);
  }
  assert.equal(installationView({...state,stages:state.stages.slice(1)}),null);
  assert.equal(installationView({...state,stages:[...state.stages,state.stages[5]]}),null);
  for(const status of ['running','cancelling','cancel_requested','complete','cancelled','interrupted']){
    const changed=structuredClone(state);changed.status=status;changed.stages[6].status=status;
    assert.equal(installationView(changed).canSkip,false,status);
    assert.equal(installationView(changed).attention,false,status);
  }
  const unqualified=structuredClone(state);unqualified.stages[5].details={apk};unqualified.needsActions[0].parameters={failureStage:'install'};
  assert.equal(installationView(unqualified).canSkip,false);
  assert.equal(installationView({...state,session:'../unsafe'}).canSkip,false);
});

test('install transport posts only the retained session and cannot fetch whole APK bytes',async()=>{
  const calls=[];
  const api=new LocalApi('http://127.0.0.1:1234','private-token',async(url,options)=>{
    calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'status',session,state:{session,status:'complete'}})};
  });
  const result=await api.skipInstall(session);
  assert.equal(result.state.status,'complete');assert.equal(calls.length,1);
  assert.equal(calls[0].url,'http://127.0.0.1:1234/api/install/skip');
  assert.equal(calls[0].options.method,'POST');assert.equal(calls[0].options.headers['X-Quest-Token'],'private-token');
  assert.deepEqual(JSON.parse(calls[0].options.body),{session});
  for(const value of ['short','../unsafe',null,'x'.repeat(129)])assert.throws(()=>api.skipInstall(value),error=>error.code==='invalidReply');
  assert.equal(calls.length,1);
  await assert.rejects(()=>new PreviewApi().skipInstall(session),error=>error.code==='preview');
});

test('live cable loss shows actionable guidance and resumes in place without resetting measured bytes',
  {skip:!existsSync(chrome),timeout:20000},async()=>{
    let current=fixture(),client;const posts=[];
    current.status='running';current.needsActions=[];current.stages[6].status='running';
    current.stages[6].progress={phase:'install-content',operationStatus:'running',stageOperation:'content',
      done:536870912,total:1073741824,unit:'bytes',percent:50,stagePercent:60};
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(request.method==='POST'){let body='';for await(const part of request)body+=part;posts.push({path:url.pathname,body:JSON.parse(body)});}
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:current.status}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
        else if(url.pathname==='/api/status')value={schema:1,event:'status',session,state:current};
        else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    try{
      client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
      await client.chooseHome('resume');await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Quest-Installation läuft.');
      const measured=await client.evaluate("[document.getElementById('progress-track').getAttribute('aria-valuenow'),document.getElementById('substep-track').getAttribute('aria-valuenow'),document.getElementById('progress-detail').textContent]");
      current.stages[6].progress={...current.stages[6].progress,phase:'install-connection-wait',operationStatus:'waiting'};
      await client.wait("document.getElementById('progress-title').textContent==='Installation wartet auf Quest-Verbindung.'");
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),false);
      assert.equal(await client.evaluate("document.getElementById('action-needed').getAttribute('role')"),'status');
      assert.equal(await client.evaluate("document.getElementById('action-needed').classList.contains('failure')"),false);
      assert.equal(await client.evaluate("document.getElementById('failure-cause').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('failure-actions').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('install-skip').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('primary').disabled&&document.getElementById('failure-retry').disabled"),true);
      assert.equal(await client.evaluate("!document.getElementById('cancel').hidden&&!document.getElementById('cancel').disabled"),true);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/dieselbe Quest.*USB-Datenkabel.*entsperre.*USB-Debugging.*nicht erneut starten/s);
      assert.match(await client.evaluate("document.getElementById('install-guide').textContent"),/automatisch fortgesetzt.*Fortschritt bleibt erhalten.*Start ist nicht nötig/s);
      assert.doesNotMatch(await client.evaluate("document.getElementById('action-needed').innerText"),/Fehler|Installation erneut versuchen/);
      assert.equal(await client.evaluate("document.getElementById('activity-status').classList.contains('quiet')"),false);
      assert.deepEqual(await client.evaluate("[document.getElementById('progress-track').getAttribute('aria-valuenow'),document.getElementById('substep-track').getAttribute('aria-valuenow'),document.getElementById('progress-detail').textContent]"),measured);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      assert.match(await client.evaluate("document.querySelector('#stage-list li:nth-child(7) .stage-state').textContent"),/60 % · Wartet/);
      await client.picture('live-install-connection-wait-de');
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Installation waiting for Quest connection.');
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/same Quest.*USB data cable.*unlock.*USB debugging.*not need to restart/s);
      await client.picture('live-install-connection-wait-en');
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      current.stages[6].progress={...current.stages[6].progress,phase:'install-content',operationStatus:'running'};
      await client.wait("document.getElementById('progress-title').textContent==='APK fertig. Quest-Installation läuft.'&&document.getElementById('action-needed').hidden");
      assert.deepEqual(await client.evaluate("[document.getElementById('progress-track').getAttribute('aria-valuenow'),document.getElementById('substep-track').getAttribute('aria-valuenow'),document.getElementById('progress-detail').textContent]"),measured);
      current.stages[6].progress={...current.stages[6].progress,done:805306368,percent:75,stagePercent:75};
      await client.wait("document.getElementById('progress-track').getAttribute('aria-valuenow')==='75'");
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/768 MiB/);
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      await client.picture('live-install-content-resumed-de');
      // An initial connection pause has no reported byte total or synthetic
      // animation claiming that disconnected work is advancing.
      current.stages[6].progress={phase:'install-connection-wait',operationStatus:'waiting',stageOperation:'connect',stagePercent:0,percent:null};
      await client.wait("document.getElementById('progress-title').textContent==='Installation wartet auf Quest-Verbindung.'");
      assert.equal(await client.evaluate("document.getElementById('substep-track').hasAttribute('aria-valuenow')"),false);
      assert.equal(await client.evaluate("document.getElementById('substep-track').classList.contains('indeterminate')"),false);
      assert.doesNotMatch(await client.evaluate("document.getElementById('substep-count').textContent"),/%/);
      assert.deepEqual(posts,[],'automatic continuation needs no restart, rebuild or overlapping install request');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });

test('completed APK is prominent while connection is blocked; retry and skip retain the same session',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    let current=fixture(),client,heldSkip,releaseSkip;const posts=[];
    const server=createServer(async(request,response)=>{
      try{
        const url=new URL(request.url,'http://localhost');let value;
        if(request.method==='POST'){let body='';for await(const part of request)body+=part;posts.push({path:url.pathname,body:JSON.parse(body)});}
        if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:current.status}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false},modSource:{sourceCommit:'4'.repeat(40),modBuild:668,modVersion:'1.1.0',kind:'bundled-release'}};
        else if(url.pathname==='/api/status')value={schema:1,event:'status',session,state:current};
        else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
        else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
        else if(url.pathname==='/api/run'){
          current={...current,status:'running',needsActions:[]};current.stages[6].status='running';value={schema:1,event:'started',session};
        }else if(url.pathname==='/api/install/skip'){
          heldSkip=true;await new Promise(resolve=>releaseSkip=resolve);
          current={...current,status:'complete',needsActions:[]};current.choices.install=false;current.stages[6]={id:'install',status:'complete',details:{requested:false}};
          value={schema:1,event:'status',session,state:current};
        }
        if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
        const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
        if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
        response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
      }catch(error){response.writeHead(500);response.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    let navigation=0;
    const reopen=async()=>{
      const query='?case='+(++navigation);
      await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/'+query+'#fixture-token'});
      await client.chooseHome('resume');
      await client.wait("location.search==="+JSON.stringify(query)+"&&document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden&&!document.getElementById('result-card').hidden");
      await client.evaluate("document.querySelector('[data-language=de]').click()");
    };
    try{
      client=await browser();await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Quest-Verbindung erforderlich.');
      assert.equal(await client.evaluate("document.getElementById('action-needed').classList.contains('failure')"),false);
      assert.equal(await client.evaluate("document.getElementById('action-needed').getAttribute('role')"),'status');
      assert.equal(await client.evaluate("document.getElementById('failure-cause').hidden"),true);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/USB-Debugging-Freigabe/);
      assert.equal(await client.evaluate("document.getElementById('result-card').hidden"),false);
      assert.equal(await client.evaluate("document.getElementById('result-path').textContent"),apk);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      assert.equal(await client.evaluate("document.querySelector('#stage-list li:nth-child(6) progress').value"),100);
      assert.equal(await client.evaluate("document.getElementById('primary').firstElementChild.textContent"),'Installation erneut versuchen');
      assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/B665/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('mod-source').textContent"),/B668/);
      assert.equal(await client.evaluate("document.getElementById('source-update').hidden"),true,'an install retry does not replace the completed APK source');
      assert.equal(await client.evaluate("document.getElementById('install-skip').hidden"),false);
      assert.equal(await client.evaluate("document.getElementById('edit-choices').hidden"),true);
      await client.picture('apk-ready-connection-required-de');
      await client.command('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
      assert.equal(await client.evaluate('document.documentElement.scrollWidth<=innerWidth'),true);
      await client.picture('apk-ready-connection-required-mobile-de');
      await client.command('Emulation.setDeviceMetricsOverride',{width:1366,height:900,deviceScaleFactor:1,mobile:false});
      await client.evaluate("document.getElementById('failure-retry').click()");
      await client.wait("!document.getElementById('cancel').hidden");
      assert.deepEqual(posts,[{path:'/api/run',body:{session}}]);
      assert.equal(await client.evaluate("document.getElementById('install-guide').hidden"),false,'setup guidance remains available during the first connection attempt');
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Quest-Installation läuft.');
      assert.equal(await client.evaluate("document.querySelectorAll('#install-guide li').length"),4);
      assert.match(await client.evaluate("document.getElementById('install-guide').textContent"),/entsperren.*Entwicklermodus.*Meta-Horizon-App.*USB-Datenkabel.*nicht erforderlich.*USB-Debugging.*Dateizugriff.*bevorzugt USB/s);
      assert.equal(await client.evaluate("document.getElementById('install-skip').hidden"),true);
      await client.evaluate("document.getElementById('install-skip').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
      await delay(100);assert.equal(posts.length,1,'active install cannot be skipped programmatically');
      current.stages[6].progress={phase:'install-recovery-access',done:1,total:3,unit:'files',percent:100/3,stagePercent:20,stageOperation:'apk'};
      await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Installation wird automatisch angepasst.');
      assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Zugriff auf Spielstände und Einstellungen vorbereiten/);
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/1 \/ 3 Dateien/);
      let previous;
      for(const done of [1,20,40]){
        current.stages[6].progress={phase:'install-recovery-backup-work',done,total:41,unit:'tasks',percent:100*done/41,stagePercent:20+(40/7)*done/41,stageOperation:'apk'};
        await reopen();assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Datensicherung erstellen und prüfen/);
        const value=await client.evaluate("document.querySelector('#stage-list li:nth-child(7) progress').value");
        if(previous!==undefined)assert.ok(value>previous,'completed actual backup tasks move the parent install bar');previous=value;
        assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      }
      current.stages[6].progress={phase:'install-recovery-backup',done:104857600,total:null,unit:'bytes',percent:null,stagePercent:previous,stageOperation:'apk'};
      await reopen();assert.equal(await client.evaluate("document.getElementById('substep-count').textContent"),'100 MiB übertragen');
      assert.match(await client.evaluate("document.getElementById('progress-detail').textContent"),/100 MiB übertragen/);
      assert.equal(await client.evaluate("document.getElementById('substep-track').classList.contains('indeterminate')"),true,'a byte-only observation cannot invent a total');
      current.stages[6].progress={phase:'install-signature-backup',done:2,total:7,unit:'tasks',percent:200/7,stagePercent:25.7,stageOperation:'apk'};
      await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Installation wird automatisch angepasst.');
      assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/andere Signatur.*Spielstände.*bevor er die App ersetzt.*wieder her/s);
      assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),/Spielstände und Einstellungen sichern/);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      assert.equal(await client.evaluate("document.querySelector('#stage-list li:nth-child(6) progress').value"),100);
      assert.match(await client.evaluate("document.getElementById('mod-source').textContent"),/B665/);
      for(const [phase,label] of [['install-signature-restore','wiederherstellen'],['install-signature-verify','Daten prüfen']]){
        current.stages[6].progress={phase,done:phase.endsWith('verify')?7:5,total:7,unit:'tasks',percent:phase.endsWith('verify')?100:500/7,stagePercent:phase.endsWith('verify')?39.8:34.3,stageOperation:'apk'};
        await reopen();assert.match(await client.evaluate("document.getElementById('substep-label').textContent"),new RegExp(label));
        assert.equal(await client.evaluate("document.querySelector('#stage-list li:nth-child(7) progress').value<100"),true,'migration completion cannot complete content, launch and receipt');
      }
      current.stages[6].progress={phase:'install-content',done:1,total:2,unit:'files',percent:50,stagePercent:50};
      await reopen();assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Quest-Installation läuft.');
      current=fixture('quest_signing_recovery_required','Signing migration could not safely continue: restore access failed.\r\nRetained backup: device/saved-data','blocked');
      current.needsActions[0].parameters.backupPath='C:\\Users\\Owner\\.ghvrq\\device\\signing-recovery\\Quest\\saved-data';await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Sichere Wiederherstellung erforderlich.');
      assert.match(await client.evaluate("document.getElementById('failure-cause').textContent"),/restore access failed.*device\/saved-data/s);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/Sicherungen behalten.*ohne die APK neu zu bauen.*nicht manuell deinstallieren/s);
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/Sicherungsordner: C:\\Users\\Owner\\\.ghvrq\\device\\signing-recovery\\Quest\\saved-data/);
      assert.doesNotMatch(await client.evaluate("document.getElementById('failure-next').textContent"),/Zugriffs.*problem beheben/);
      assert.equal(await client.evaluate("document.getElementById('install-guide').hidden"),true,'signing recovery refusal is not a missing USB prerequisite');
      await client.evaluate("document.getElementById('failure-retry').click()");await client.wait("!document.getElementById('cancel').hidden");
      assert.deepEqual(posts.at(-1),{path:'/api/run',body:{session}},'recovery retries only the retained installation');
      current=fixture('installation_tool_failed','INSTALL_PARSE_FAILED_NO_CERTIFICATES: APK has no valid signature','failed');await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Installation fehlgeschlagen.');
      assert.match(await client.evaluate("document.getElementById('failure-cause').textContent"),/INSTALL_PARSE_FAILED_NO_CERTIFICATES/);
      assert.equal(await client.evaluate("document.getElementById('action-needed').classList.contains('failure')"),true);
      assert.equal(await client.evaluate("document.getElementById('install-guide').hidden"),true,'a signing failure is not a USB prerequisite');
      assert.doesNotMatch(await client.evaluate("document.getElementById('failure-next').textContent"),/USB/);
      current=fixture('child_failed','No error summary was emitted; inspect the retained tool log.','failed');
      current.needsActions[0].parameters={failureStage:'install',cause:'No error summary was emitted; inspect the retained tool log.'};await reopen();
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK fertig. Installation fehlgeschlagen.');
      assert.equal(await client.evaluate("document.getElementById('install-skip').hidden"),false);
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'APK ready. Installation failed.');
      await client.evaluate("document.getElementById('install-skip').click()");
      for(let index=0;index<80&&!heldSkip;index++)await delay(25);
      assert.equal(heldSkip,true);
      assert.equal(await client.evaluate("document.getElementById('install-skip').disabled&&document.getElementById('failure-retry').disabled&&document.getElementById('primary').disabled"),true);
      await client.evaluate("document.getElementById('install-skip').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
      await delay(100);assert.equal(posts.filter(row=>row.path==='/api/install/skip').length,1,'busy skip cannot be duplicated');
      releaseSkip();await client.wait("document.getElementById('progress-title').textContent==='Build completed.'");
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      assert.match(await client.evaluate("document.querySelector('#stage-list li:nth-child(7)').textContent"),/Not selected/);
      assert.equal(await client.evaluate("document.getElementById('result-path').textContent"),apk);
      assert.ok(posts.every(row=>['/api/run','/api/install/skip'].includes(row.path)),'no planning, rebuild or APK download action');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{releaseSkip?.();try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}}
  });
