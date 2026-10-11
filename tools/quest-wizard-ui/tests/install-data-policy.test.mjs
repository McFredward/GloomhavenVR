// Current and archived saves are protected through the actual HTTP/Engine path.
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {existsSync} from 'node:fs';
import {mkdtemp,readFile,writeFile,mkdir,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {installationView} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {strings,translate} from '../i18n.mjs';
import {browser,chrome,delay} from './browser-harness.mjs';

const repo=resolve(fileURLToPath(new URL('../',import.meta.url)),'../..');
const execute=promisify(execFile),python=process.env.QUEST_WIZARD_PYTHON??'python3';
const ids=['tools','source','unity','profile','inspect','build','install'];
function fixture(reason='non-save-inaccessible'){
  const session='a'.repeat(32),nonce='b'.repeat(32),inputKey='c'.repeat(64),apkSha256='d'.repeat(64);
  return {session,status:'blocked',installationAction:{nonce,session,policy:'all',reason,attempt:3,inputKey,apkSha256},
    needsActions:[{code:'quest_signing_data_required',stage:'install',parameters:{failureStage:'install',saveDataProtected:true,
      recoveryDataReason:reason,recoveryDataPolicy:'all',installationActionNonce:nonce}}],
    stages:ids.map(id=>({id,status:id==='install'?'blocked':'complete',attempts:id==='install'?3:1,
      details:id==='build'?{apk:'retained.apk',inputKey,apkSha256}: {}}))};
}

test('only the current completed APK and declared all-data compromise expose keep-saves action',()=>{
  const state=fixture(),view=installationView(state);
  assert.equal(view.recoveryRequired,true);assert.equal(view.dataIssue,'non-save-inaccessible');
  assert.equal(view.canUseSaves,true);assert.equal(view.actionNonce,'b'.repeat(32));
  assert.equal(installationView(fixture('save-data-inaccessible')).canUseSaves,false);
  for(const change of ['session','nonce','inputKey','apkSha256','attempt','policy','reason']){
    const changed=structuredClone(state);changed.installationAction[change]=change==='attempt'?4:'foreign';
    assert.equal(installationView(changed).canUseSaves,false,change);
    assert.equal(installationView(changed).actionNonce,undefined,change);
  }
  const active=structuredClone(state);active.status=active.stages[6].status='running';
  assert.equal(installationView(active).canUseSaves,undefined);
});

test('save choices have bilingual current/history protection and disclose other-file loss',()=>{
  for(const language of ['de','en'])for(const key of ['installationSaveWaitTitle','installationSaveWaitCopy','installationSaveWaitNext',
    'installationDataChoiceTitle','installationDataChoiceCopy','installationDataChoiceNext','installationKeepSaves','installationRetryAll']){
    assert.notEqual(translate(language,key),key);
    assert.deepEqual([...strings.de[key].matchAll(/\{([^}]+)\}/g)], [...strings.en[key].matchAll(/\{([^}]+)\}/g)]);
  }
  assert.match(translate('de','installationDataChoiceNext'),/gespeicherte Historie.*andere Dateien.*verloren/s);
  assert.match(translate('en','installationDataChoiceNext'),/stored history.*files outside save folders.*lost/s);
  assert.match(translate('de','installationSaveWaitCopy'),/App wird nicht entfernt/);
  assert.match(translate('en','installationSaveWaitNext'),/saves are never reset/);
});

test('policy transport permits only a current nonce and save-preserving compromise',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,request)=>{
    calls.push({url:String(url),body:JSON.parse(request.body)});return {ok:true,json:async()=>({schema:1,event:'status'})};
  });
  await api.installDataPolicy('a'.repeat(32),'b'.repeat(32));
  assert.deepEqual(calls,[{url:'http://127.0.0.1:1234/api/install/data-policy',body:{session:'a'.repeat(32),nonce:'b'.repeat(32),policy:'saves'}}]);
  for(const arguments_ of [['../private','b'.repeat(32)],['a'.repeat(32),'invalid'],['a'.repeat(32),'b'.repeat(32),'reset']]){
    assert.throws(()=>api.installDataPolicy(...arguments_));
  }
  await assert.rejects(new PreviewApi().installDataPolicy('a'.repeat(32),'b'.repeat(32)),value=>value.code==='preview');
});

const hash=raw=>createHash('sha256').update(raw).digest('hex');
async function actualServer(protectedSave){
  const root=await mkdtemp(join(tmpdir(),'quest-wizard-install-policy-'));
  const child=spawn(python,['-I','-B',fileURLToPath(new URL('./install_policy_server.py',import.meta.url)),repo,root,...(protectedSave?['--protected']:[])],{stdio:['ignore','pipe','pipe']});
  let output='',errors='';child.stdout.on('data',data=>{output+=data.toString();});child.stderr.on('data',data=>{errors+=data.toString();});
  let client;
  try{
    for(let attempt=0;attempt<100&&!output.includes('\n')&&child.exitCode===null;attempt++)await delay(50);
    assert.equal(child.exitCode,null,errors);assert.ok(output.includes('\n'),errors);
    const event=JSON.parse(output.split('\n')[0]),witness=JSON.parse(await readFile(join(root,'fixture-witness.json'),'utf8'));
    const origin=new URL(event.url).origin;
    const api=new LocalApi(origin,new URL(event.url).hash.slice(1),(url,options)=>
      fetch(url,{...options,headers:{...options.headers,Origin:origin}}));
    client=await browser();await client.command('Page.navigate',{url:event.url});
    await client.wait("document.getElementById('progress-page')&&!document.getElementById('progress-page').hidden");
    await client.evaluate("document.querySelector('[data-language=de]').click()");
    await client.wait("!document.getElementById('action-needed').hidden");
    const closed=async()=>{
      const current=(await api.status(event.session)).state;
      assert.equal(current.stages.slice(0,6).filter(row=>row.status==='complete'&&row.attempts===1).length,6);
      assert.equal(hash(await readFile(join(root,'owned/build/builds/player/complete.apk'))),witness.apkSha256);
      assert.equal(hash(await readFile(join(root,'owned/device/retained-save-history.tar'))),witness.backupSha256);
      for(const stage of ids.slice(0,6)){
        const receipt=await readFile(join(root,'owned/sessions',event.session,'receipts',stage+'.json'));
        assert.equal(hash(receipt),witness.receiptSha256[stage],stage);
      }
      return current;
    };
    return {root,child,client,event,api,closed,errors:()=>errors,async close(){
      try{await client?.close();}finally{
        if(child.exitCode===null&&child.signalCode===null){
          const exited=new Promise(resolve=>child.once('exit',resolve));child.kill('SIGINT');
          await Promise.race([exited,delay(2000)]);if(child.exitCode===null&&child.signalCode===null){child.kill('SIGKILL');await exited;}
        }
        // Same completion API as production suite runs, after child reaping.
        await execute(python,['-I','-B','-c',
          "import pathlib,sys;sys.path.insert(0,sys.argv[1]);from town_run_retention import cleanup_completed_run;cleanup_completed_run('quest-wizard-install-policy',0,pathlib.Path(sys.argv[2]),pathlib.Path(sys.argv[3]),process_reaped=True)",
          join(repo,'scripts'),join(root,'owned/sessions',event.session,'logs/install.log'),repo]);
        await rm(root,{recursive:true,force:true});
      }
    }};
  }catch(error){
    await client?.close();if(child.exitCode===null&&child.signalCode===null){const ended=new Promise(resolve=>child.once('exit',resolve));child.kill('SIGKILL');await ended;}
    await execute(python,['-I','-B','-c',
      "import pathlib,sys;sys.path.insert(0,sys.argv[1]);from town_run_retention import cleanup_completed_run;cleanup_completed_run('quest-wizard-install-policy',1,pathlib.Path(sys.argv[2]),pathlib.Path(sys.argv[3]),process_reaped=True)",
      join(repo,'scripts'),join(root,'fixture-failure.log'),repo]);
    await rm(root,{recursive:true,force:true});throw error;
  }
}

test('actual Chrome/backend asks before dropping inaccessible non-save files and resumes only installation',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const owner=await actualServer(false),{client,api,event}=owner;
    try{
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Installation wartet: Datenauswahl erforderlich.');
      assert.equal(await client.evaluate("document.getElementById('install-keep-saves').hidden"),false);
      assert.equal(await client.evaluate("document.getElementById('failure-retry').textContent"),'Erneut alle Dateien sichern');
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/gespeicherte Historie.*andere Dateien.*verloren/s);
      assert.equal(await client.evaluate("document.querySelectorAll('#stage-list li.complete').length"),6);
      const initial=await owner.closed(),nonce=initial.installationAction.nonce;
      await assert.rejects(api.installDataPolicy(event.session,'f'.repeat(32)),value=>value.code==='installation_action_stale');
      await client.picture('install-keep-current-and-archived-saves-de');
      await client.evaluate("document.querySelector('[data-language=en]').click()");
      assert.equal(await client.evaluate("document.getElementById('install-keep-saves').textContent"),'Keep saves and continue');
      assert.match(await client.evaluate("document.getElementById('failure-next').textContent"),/stored history.*files outside save folders.*lost/s);
      await client.evaluate("document.getElementById('install-keep-saves').click()");
      await client.wait("document.getElementById('progress-count').textContent==='100%'");
      const current=await owner.closed();assert.equal(current.status,'complete');
      assert.equal(current.installationRecoveryPolicy.policy,'saves');
      assert.equal(current.stages[6].attempts,3);assert.equal(current.needsActions.length,0);
      assert.equal(current.installationAction,undefined);
      assert.equal(await client.evaluate("document.getElementById('action-needed').hidden"),true);
      assert.equal(await client.evaluate("document.getElementById('install-keep-saves').hidden"),true);
      await assert.rejects(api.installDataPolicy(event.session,nonce),value=>value.code==='installation_action_stale');
      await client.picture('install-saves-policy-complete-en');
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0,owner.errors());
      if(process.env.QUEST_WIZARD_SCREENSHOTS){await mkdir(process.env.QUEST_WIZARD_SCREENSHOTS,{recursive:true});await writeFile(join(process.env.QUEST_WIZARD_SCREENSHOTS,'install-policy-browser-witness.json'),JSON.stringify({schema:1,actualChromeBackend:true,currentAndArchivedSavesRetained:true,earlierReceiptsUnchanged:true,finishedApkUnchanged:true,policyApprovalScoped:true,hardwareVerified:false},null,2));}
    }finally{await owner.close();}
  });

test('actual Chrome/backend pauses inaccessible save folders without a reset or non-save bypass',
  {skip:!existsSync(chrome),timeout:30000},async()=>{
    const owner=await actualServer(true),{client,api,event}=owner;
    try{
      assert.equal(await client.evaluate("document.getElementById('progress-title').textContent"),'Installation wartet: Spielstände schützen.');
      assert.equal(await client.evaluate("document.getElementById('install-keep-saves').hidden"),true);
      assert.match(await client.evaluate("document.getElementById('progress-copy').textContent"),/Aktuelle und gespeicherte Spielstände.*App wird nicht entfernt/s);
      const initial=await owner.closed(),nonce=initial.installationAction.nonce;
      await assert.rejects(api.installDataPolicy(event.session,nonce),value=>value.code==='installation_action_stale');
      await client.picture('install-protected-save-paused-de');
      await client.evaluate("document.getElementById('failure-retry').click()");
      for(let attempt=0;attempt<80;attempt++){const current=await api.status(event.session);if(current.state.status==='blocked'&&current.state.installationAction?.nonce!==nonce)break;await delay(50);}
      await client.wait("document.getElementById('progress-title').textContent==='Installation wartet: Spielstände schützen.'&&!document.getElementById('install-skip').disabled");
      const retried=await owner.closed();assert.equal(retried.status,'blocked');
      assert.equal(retried.stages[6].attempts,3);assert.notEqual(retried.installationAction.nonce,nonce);
      assert.equal(retried.installationRecoveryPolicy.policy,'saves');
      await client.evaluate("document.getElementById('install-skip').click()");
      await client.wait("document.getElementById('progress-count').textContent==='100 %'");
      const finished=await owner.closed();assert.equal(finished.status,'complete');assert.equal(finished.stages[6].details.requested,false);
      assert.equal(finished.installationAction,undefined);
      assert.equal(client.events.filter(row=>row.method==='Runtime.exceptionThrown').length,0,owner.errors());
    }finally{await owner.close();}
  });
