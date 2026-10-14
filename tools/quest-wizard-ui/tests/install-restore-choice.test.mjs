import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve,extname} from 'node:path';
import {installationView} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {translate} from '../i18n.mjs';
import {browser,chrome,delay} from './browser-harness.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
const session='a'.repeat(32),nonce='b'.repeat(32),inputKey='c'.repeat(64),apkSha256='d'.repeat(64),transaction='e'.repeat(32),deviceSerial='quest-fixture';
function fixture(){
  return {session,status:'blocked',choices:{gameRoot:'C:\\Owned game',provider:'gog',acceptUnityTerms:true,install:true},artwork:[],
    installationAction:{session,nonce,inputKey,apkSha256,transaction,deviceSerial,kind:'restore-choice',reason:'restore-app-launch-unavailable',attempt:3},
    needsActions:[{code:'quest_signing_repair_wait',stage:'install',parameters:{failureStage:'install',saveDataProtected:true,
      repairReason:'restore-app-launch-unavailable',repairRoot:'external',repairAttempts:2,repairTotalAttempts:2,
      requiresRestoreChoice:true,manualLaunchAvailable:true,resetAppDataAvailable:true,restoreChoiceTransaction:transaction,
      restoreChoiceDeviceSerial:deviceSerial,installationActionNonce:nonce,backupPath:'C:\\Owner\\.ghvrq\\device\\retained'}}],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='install'?'blocked':'complete',attempts:id==='install'?3:1,
      progress:{phase:id==='install'?'install-recovery-access':'complete',stagePercent:id==='install'?20:100},
      details:id==='build'?{inputKey,apkSha256,apk:'C:\\Owner\\.ghvrq\\complete.apk'}:{}}))};
}

test('manual and destructive choices are bound to the exact current restoration, not arbitrary backup failures',()=>{
  const state=fixture(),view=installationView(state);
  assert.equal(view.canManualLaunch,true);assert.equal(view.canResetAppData,true);assert.equal(view.restoreActionNonce,nonce);
  for(const field of ['session','nonce','inputKey','apkSha256','transaction','deviceSerial','kind','reason','attempt']){
    const bad=structuredClone(state);bad.installationAction[field]=field==='attempt'?4:'foreign';
    const declined=installationView(bad);assert.equal(declined.canManualLaunch,false,field);assert.equal(declined.canResetAppData,false,field);
    assert.equal(declined.restoreActionNonce,undefined,field);
  }
  for(const field of ['saveDataProtected','requiresRestoreChoice']){
    const bad=structuredClone(state);bad.needsActions[0].parameters[field]=false;
    assert.equal(installationView(bad).canManualLaunch,undefined);assert.equal(installationView(bad).canResetAppData,undefined);
  }
  for(const reason of ['backup-census-mismatch','backup-transfer-unavailable']){
    const bad=structuredClone(state);bad.needsActions[0].parameters.repairReason=bad.installationAction.reason=reason;
    assert.equal(installationView(bad).canManualLaunch,undefined);assert.equal(installationView(bad).canResetAppData,undefined);
  }
  for(const field of ['manualLaunchAvailable','resetAppDataAvailable']){
    const bad=structuredClone(state);bad.needsActions[0].parameters[field]=1;
    assert.equal(installationView(bad)[field==='manualLaunchAvailable'?'canManualLaunch':'canResetAppData'],false);
  }
  const active=structuredClone(state);active.status=active.stages[6].status='running';
  assert.equal(installationView(active).restoreActionNonce,undefined);
});

test('manual waiting is an actionable live wait, not completion of a numerical work queue',()=>{
  const state=fixture();state.status=state.stages[6].status='running';
  state.stages[6].progress={phase:'install-recovery-manual-launch',operationStatus:'waiting',done:0,total:1,unit:'tasks',stagePercent:20};
  const view=installationView(state);assert.equal(view.manualLaunchWaiting,true);assert.equal(view.canSkip,false);assert.equal(view.canManualLaunch,undefined);
  for(const status of ['complete','reuse']){
    const finished=structuredClone(state);finished.stages[6].progress.operationStatus=status;
    assert.equal(installationView(finished).manualLaunchWaiting,undefined);
  }
});

test('restore choice transport admits only explicit known choices with valid session and nonce',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,request)=>{
    calls.push({path:url.pathname,body:JSON.parse(request.body)});return {ok:true,json:async()=>({schema:1,event:'status'})};
  });
  for(const choice of ['manual-launch','reset-app-data'])await api.installRestoreChoice(session,nonce,choice);
  assert.deepEqual(calls,['manual-launch','reset-app-data'].map(choice=>({path:'/api/install-restore-choice',body:{session,nonce,choice}})));
  for(const [s,n,c] of [['../foreign',nonce,'manual-launch'],[session,'stale','manual-launch'],[session,nonce,'defer-restoration'],[session,nonce,null]])
    assert.throws(()=>api.installRestoreChoice(s,n,c),{code:'invalidReply'});
  await assert.rejects(new PreviewApi().installRestoreChoice(),{code:'preview'});
  for(const language of ['de','en'])for(const key of ['installationManualOption','installationResetOption','installationManualConfirmCopy',
    'installationResetConfirmCopy','installationResetAcknowledge','installationManualWaitingCopy','installationManualWaitingElapsed'])assert.notEqual(translate(language,key),key);
  assert.match(translate('de','installationResetConfirmCopy'),/Aktuelle und archivierte Spielstände.*endgültig.*11 GB.*nicht automatisch.*nicht erforderlich/s);
  assert.match(translate('en','installationResetConfirmCopy'),/Current and archived.*permanently.*11 GB.*not be restored automatically.*No new Unity/s);
});

async function pageFixture(){
  let current=fixture(),client,choice=null;const posts=[];
  const server=createServer(async(request,response)=>{
    try{
      const url=new URL(request.url,'http://localhost');let value,body;
      if(request.method==='POST'){let text='';for await(const part of request)text+=part;body=JSON.parse(text);posts.push({path:url.pathname,body});}
      if(url.pathname==='/api/discover')value={schema:1,event:'discovery',latestSession:session,recentSessions:[{session,status:current.status}],games:[],unityEditors:[],capabilities:{browse:false,logs:false,support:false}};
      else if(url.pathname==='/api/status')value={schema:1,event:'status',session,state:current};
      else if(url.pathname==='/api/gallery')value={schema:1,event:'gallery',artwork:[]};
      else if(url.pathname==='/api/events')value={schema:1,event:'events',events:[]};
      else if(url.pathname==='/api/install-restore-choice'){
        assert.deepEqual(body,{session,nonce,choice:body.choice});choice=body.choice;
        current.status='ready';current.needsActions=[];delete current.installationAction;
        value={schema:1,event:'status',session,state:current};
      }else if(url.pathname==='/api/run'){
        assert.deepEqual(body,{session});
        current.status=current.stages[6].status=choice==='manual-launch'?'running':'complete';
        current.stages[6].progress=choice==='manual-launch'?{phase:'install-recovery-manual-launch',operationStatus:'waiting',done:0,total:1,unit:'tasks',stagePercent:20}
          :{phase:'complete',stagePercent:100};
        value={schema:1,event:'status',session,state:current};
      }
      if(value){response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify(value));return;}
      const filename=resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
      if(!filename.startsWith(root)||!['.html','.mjs','.css','.png','.svg'].includes(extname(filename)))throw Error('not static');
      response.writeHead(200,{'Content-Type':{'.html':'text/html','.mjs':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'}[extname(filename)]});response.end(await readFile(filename));
    }catch(error){response.writeHead(500);response.end(String(error));}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const close=async()=>{try{await client?.close();}finally{await new Promise(resolve=>server.close(resolve));}};
  try{
    client=await browser();await client.command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port+'/#fixture-token'});
    await client.chooseHome('resume');await client.wait("!document.getElementById('install-manual-launch').hidden&&!document.getElementById('install-manual-launch').disabled");
    return {client,posts,set(value){current=value;},close};
  }catch(error){await close();throw error;}
}

test('actual Chrome requires explicit reset acknowledgment and never posts on cancel; manual guidance survives live wait',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const owner=await pageFixture(),{client,posts}=owner;
    try{
      await client.evaluate("document.querySelector('[data-language=de]').click()");
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/manuell.*empfohlen.*ausdrücklicher Bestätigung/);
      await client.picture('restore-options-de');
      await client.evaluate("document.getElementById('install-reset-app-data').click()");
      await client.wait("document.getElementById('install-restore-dialog').open");
      assert.equal(await client.evaluate("document.getElementById('install-reset-acknowledged').checked"),false);
      assert.equal(await client.evaluate("document.getElementById('install-restore-confirm').disabled"),true);
      assert.equal(await client.evaluate("document.activeElement.id"),'install-restore-cancel');
      assert.match(await client.evaluate("document.getElementById('install-restore-dialog-copy').textContent"),/archivierte Spielstände.*endgültig.*11 GB.*nicht automatisch/s);
      await client.picture('restore-reset-confirmation-de');
      await client.evaluate("document.getElementById('install-restore-confirm').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
      await delay(100);assert.equal(posts.length,0,'synthetic click without acknowledgment cannot approve reset');
      await client.evaluate("document.getElementById('install-manual-launch').click()");
      await client.wait("document.getElementById('install-restore-dialog').open");
      assert.equal(posts.length,0,'opening instructional dialog does not yet arm the real helper');
      await client.evaluate("document.getElementById('install-restore-cancel').click()");
      assert.equal(posts.length,0,'cancel is not an implicit consent or run');
      await client.evaluate("document.querySelector('[data-language=en]').click();document.getElementById('install-manual-launch').click()");
      assert.match(await client.evaluate("document.getElementById('install-manual-instructions').textContent"),/First choose.*Then put on.*Unknown Sources.*Waiting for debugger.*before gameplay/s);
      await client.picture('restore-manual-confirmation-en');
      await client.evaluate("document.getElementById('install-restore-confirm').click()");
      await client.wait("document.getElementById('progress-title').textContent==='Restoration is waiting for you to launch the app.'");
      assert.deepEqual(posts,[{path:'/api/install-restore-choice',body:{session,nonce,choice:'manual-launch'}},{path:'/api/run',body:{session}}]);
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),false);
      assert.match(await client.evaluate("document.getElementById('action-needed-copy').textContent"),/Unknown Sources.*Waiting for debugger.*resumes restoration automatically/s);
      assert.equal(await client.evaluate("document.getElementById('substep-progress').hidden"),true,'0/1 launch wait is not a computation progress bar');
      assert.equal(await client.evaluate("document.getElementById('progress-track').getAttribute('aria-valuenow')"),'20');
      assert.equal(await client.evaluate("document.getElementById('install-manual-launch').hidden&&document.getElementById('install-reset-app-data').hidden"),true);
      await client.picture('restore-manual-live-wait-en');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{await owner.close();}
  });

test('actual Chrome posts reset only after the separate checked confirmation, without planning a new build',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const owner=await pageFixture(),{client,posts}=owner;
    try{
      await client.evaluate("document.querySelector('[data-language=en]').click();document.getElementById('install-reset-app-data').click()");
      await client.wait("document.getElementById('install-restore-dialog').open");
      await client.picture('restore-reset-confirmation-en');
      await client.evaluate("document.getElementById('install-reset-acknowledged').click()");
      assert.equal(await client.evaluate("document.getElementById('install-restore-confirm').disabled"),false);
      assert.equal(posts.length,0,'acknowledgment alone does not execute the compromise');
      await client.evaluate("document.getElementById('install-restore-confirm').click()");
      await client.wait("document.getElementById('progress-title').textContent==='Build completed.'");
      assert.deepEqual(posts,[{path:'/api/install-restore-choice',body:{session,nonce,choice:'reset-app-data'}},{path:'/api/run',body:{session}}]);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),7);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0);
    }finally{await owner.close();}
  });

test('actual Chrome closes stale confirmation when the exact restoration action changes',
  {skip:!existsSync(chrome),timeout:20000},async()=>{
    const owner=await pageFixture(),{client,posts}=owner;
    try{
      await client.evaluate("document.getElementById('install-reset-app-data').click();document.getElementById('install-reset-acknowledged').click()");
      const changed=fixture();changed.installationAction.nonce=changed.needsActions[0].parameters.installationActionNonce='f'.repeat(32);owner.set(changed);
      await client.wait("!document.getElementById('install-restore-dialog').open");
      await client.evaluate("document.getElementById('install-restore-confirm').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
      assert.equal(posts.length,0,'old confirmation cannot apply to a new attempt nonce');
    }finally{await owner.close();}
  });
