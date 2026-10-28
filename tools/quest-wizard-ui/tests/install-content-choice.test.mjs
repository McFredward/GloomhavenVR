import test from 'node:test';
import assert from 'node:assert/strict';
import {installationView} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {translate} from '../i18n.mjs';

function fixture(){
  const session='a'.repeat(32),nonce='b'.repeat(32),inputKey='c'.repeat(64),apkSha256='d'.repeat(64),transaction='e'.repeat(32),deviceSerial='quest-fixture';
  return {session,status:'blocked',installationAction:{session,nonce,inputKey,apkSha256,transaction,deviceSerial,kind:'content-reinstall',reason:'native-rename-unavailable',attempt:3},
    needsActions:[{code:'quest_signing_content_reinstall_required',stage:'install',parameters:{failureStage:'install',saveDataProtected:true,requiresContentReinstall:true,
      contentReinstallReason:'native-rename-unavailable',contentReinstallTransaction:transaction,contentReinstallDeviceSerial:deviceSerial,installationActionNonce:nonce,estimatedBytes:10929781172}}],
    stages:['tools','source','unity','profile','inspect','build','install'].map(id=>({id,status:id==='install'?'blocked':'complete',attempts:id==='install'?3:1,details:id==='build'?{inputKey,apkSha256}: {}}))};
}

test('content transfer compromise is available only for the current APK, device, transaction and blocked install attempt',()=>{
  const state=fixture(),view=installationView(state);assert.equal(view.contentReinstallRequired,true);assert.equal(view.canReinstallContent,true);
  assert.equal(view.contentActionNonce,'b'.repeat(32));assert.equal(view.contentBytes,10929781172);
  for(const field of ['session','nonce','inputKey','apkSha256','transaction','deviceSerial','kind','reason','attempt']){
    const changed=structuredClone(state);changed.installationAction[field]=field==='attempt'?4:'foreign';
    assert.equal(installationView(changed).canReinstallContent,false,field);assert.equal(installationView(changed).contentActionNonce,undefined,field);
  }
  for(const field of ['saveDataProtected','requiresContentReinstall']){
    const changed=structuredClone(state);changed.needsActions[0].parameters[field]=false;
    assert.equal(installationView(changed).canReinstallContent,undefined,field);
  }
  const active=structuredClone(state);active.status=active.stages[6].status='running';assert.equal(installationView(active).canReinstallContent,undefined);
  const incomplete=structuredClone(state);incomplete.stages[5].status='failed';assert.equal(installationView(incomplete),null);
});

test('content transfer approval posts a scoped nonce, and the preview never approves a real transfer',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,request)=>{
    calls.push({path:url.pathname,body:JSON.parse(request.body)});return {ok:true,json:async()=>({schema:1,event:'status'})};
  });
  await api.installContentReinstall('a'.repeat(32),'b'.repeat(32));
  assert.deepEqual(calls,[{path:'/api/install/content-reinstall',body:{session:'a'.repeat(32),nonce:'b'.repeat(32)}}]);
  for(const [session,nonce] of [['../outside','b'.repeat(32)],['a'.repeat(32),'stale'],['a'.repeat(32),null]])assert.throws(()=>api.installContentReinstall(session,nonce),{code:'invalidReply'});
  await assert.rejects(new PreviewApi().installContentReinstall(),{code:'preview'});
  for(const language of ['de','en'])for(const key of ['installationContentTitle','installationContentCopy','installationContentNext','installationContentSize','installationContentAllow'])assert.notEqual(translate(language,key),key);
  assert.match(translate('de','installationContentNext'),/Ohne deine Zustimmung.*nicht deinstalliert/);
  assert.match(translate('en','installationContentNext'),/not uninstalled without your approval/);
});
