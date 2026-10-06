import test from 'node:test';
import assert from 'node:assert/strict';
import {strings,translate} from '../i18n.mjs';
import {choicesFromForm,progressView,macroStep,isActive,stageStatus,stageProgress,artworkUrl,sessionId} from '../model.mjs';
import {LocalApi,PreviewApi} from '../transport.mjs';

test('German and English expose the same strings and parameter ABI',()=>{
  assert.deepEqual(Object.keys(strings.de).sort(),Object.keys(strings.en).sort());
  for(const key of Object.keys(strings.de)) {
    assert.deepEqual([...strings.de[key].matchAll(/\{([A-Za-z]+)\}/g)].map(row=>row[1]).sort(),[...strings.en[key].matchAll(/\{([A-Za-z]+)\}/g)].map(row=>row[1]).sort(),key);
    assert.ok(strings.de[key].length&&strings.en[key].length,key);
  }
  assert.equal(translate('en','foundOne'),'Found one local game copy.');
  assert.equal(translate('de','foundMany',{count:3}),'3 lokale Spielkopien gefunden.');
});

test('ownership is never inferred from selected checkboxes unless explicitly declared',()=>{
  const base={gameRoot:' C:\\Owned Game ',provider:'gog',ownedDlc:['jotl'],install:true};
  assert.deepEqual(choicesFromForm(base,'de'),{gameRoot:'C:\\Owned Game',provider:'gog',install:true,acceptUnityTerms:false,language:'de'});
  assert.deepEqual(choicesFromForm({...base,declareDlc:true,ownedDlc:[]},'de').ownedDlc,[]);
  assert.deepEqual(choicesFromForm({...base,declareDlc:true,ownedDlc:['solo','solo','not-dlc','jotl']},'de').ownedDlc,['solo','jotl']);
  assert.throws(()=>choicesFromForm({gameRoot:' '},'de'),/missingGame/);
  assert.throws(()=>choicesFromForm({...base,profileName:'Name'},'de'),/missingProfile/);
  assert.deepEqual(choicesFromForm({...base,provider:'steam',profileName:'Name',profileId:'123'},'en').profile,{schema:1,provider:'steam',displayName:'Name',providerId:'123',steamId:'123'});
  assert.equal(choicesFromForm({...base,profileName:'Name',profileId:'gog-id'},'en').profile.steamId,undefined);
});

test('progress preserves an unknown percentage instead of inventing elapsed-time progress',()=>{
  const state={status:'running',stages:[{id:'tools',status:'complete'},{id:'build',status:'running'}]};
  assert.deepEqual(progressView(state),{completed:1,total:2,phase:'build',percent:null,width:50,indeterminate:true,current:state.stages[1]});
  for(const percent of [NaN,Infinity,-1,101,'50',null])assert.equal(progressView({...state,progress:{percent}}).percent,null);
  assert.equal(progressView({...state,progress:{percent:0}}).percent,0);
  assert.equal(progressView({...state,progress:{percent:72}}).width,72);
  assert.equal(macroStep(state),2);assert.equal(macroStep({...state,status:'complete'}),3);
  assert.equal(isActive({status:'cancel_requested'}),true);assert.equal(isActive({status:'cancelled'}),false);
  const interrupted={status:'interrupted',stages:[{id:'tools',status:'complete'},{id:'build',status:'interrupted'},{id:'install',status:'pending'}],progress:{phase:null,percent:null}};
  assert.equal(progressView(interrupted).phase,'build');assert.equal(macroStep(interrupted),2);assert.equal(isActive(interrupted),false);
  assert.equal(stageStatus({choices:{install:false}},{id:'install',status:'complete'}),'skipped');
  assert.equal(stageStatus({choices:{install:true}},{id:'install',status:'complete',details:{requested:false}}),'skipped');
  assert.equal(stageStatus({choices:{install:true}},{id:'install',status:'complete',details:{requested:true}}),'complete');
});

test('browser artwork URLs and session IDs remain local and bounded',()=>{
  const origin='http://127.0.0.1:1234';
  assert.equal(artworkUrl('/api/artwork?session=session-123&id=image',origin),origin+'/api/artwork?session=session-123&id=image');
  for(const value of ['https://other.invalid/api/artwork','file:///original.png','data:image/png,test','/Assets/Texture.png','//other.invalid/api/artwork'])assert.equal(artworkUrl(value,origin),null);
  assert.equal(sessionId('session-123'),'session-123');
  for(const value of ['../secret','short','x'.repeat(129),null])assert.equal(sessionId(value),null);
});

test('measured substeps and prerequisite actions survive without invented percentages',async()=>{
  const stage={id:'unity',status:'running',progress:{phase:'unity-prerequisites',done:3,total:4,percent:75,unit:'checks'},waiting:{nonce:'a'.repeat(32)}};
  assert.equal(progressView({status:'running',stages:[stage],progress:{percent:null}}).percent,75);
  assert.equal(stageProgress(stage).percent,75);assert.equal(stageProgress(stage).waiting.nonce,'a'.repeat(32));
  assert.equal(stageProgress({...stage,progress:{phase:'unity-install',percent:null}}).percent,null);
  assert.equal(stageProgress({status:'complete'}).percent,100);assert.equal(stageProgress({status:'pending'}).percent,0);
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'action_requested'})};});
  await api.action('session-123','unity-open','a'.repeat(32));assert.deepEqual(JSON.parse(calls[0].options.body),{session:'session-123',action:'unity-open',nonce:'a'.repeat(32)});
  assert.throws(()=>api.action('session-123','cmd.exe','a'.repeat(32)));assert.throws(()=>api.action('session-123','unity-check','old-nonce'));
  assert.equal(artworkUrl('/api/promo-artwork?id=brute','http://127.0.0.1:1234'),'http://127.0.0.1:1234/api/promo-artwork?id=brute');
  assert.ok(!strings.de.progressEyebrow.includes('ABENTEUER'));assert.ok(!strings.en.progressEyebrow.includes('ADVENTURE'));
});

test('real transport sends token, structured choices and explicit optional session',async()=>{
  const calls=[];const api=new LocalApi('http://127.0.0.1:1234','private-token',async(url,options)=>{calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'planned'})};});
  await api.plan({gameRoot:'C:\\Owned'},'session-123');
  assert.deepEqual(JSON.parse(calls[0].options.body),{choices:{gameRoot:'C:\\Owned'},session:'session-123'});
  assert.equal(calls[0].options.headers['X-Quest-Token'],'private-token');assert.equal(calls[0].options.method,'POST');
  await api.events('session-123',NaN);assert.ok(calls[1].url.endsWith('after=0'));
  await api.log('session-123','build');assert.ok(calls[2].url.endsWith('stage=build'));
  assert.throws(()=>api.log('session-123','../../private'),error=>error.code==='invalidReply');
  assert.throws(()=>api.status('../bad'),error=>error.code==='invalidReply');
  assert.throws(()=>api.plan({},'../bad'),error=>error.code==='invalidReply');
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','',()=>{throw Error('should never fetch');}).discover(),error=>error.code==='noToken');
});

test('transport retains bilingual blocked errors and rejects malformed API responses',async()=>{
  const denied={schema:1,event:'error',code:'unity-required',message:{de:'Unity fehlt',en:'Unity missing'}};
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:false,json:async()=>denied})).discover(),error=>error===denied);
  await assert.rejects(()=>new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:true,json:async()=>({schema:2})})).discover(),error=>error.code==='invalidReply');
});

test('preview can never report a completed real build or invoke external tools',async()=>{
  const api=new PreviewApi();const discovery=await api.discover();assert.equal(discovery.capabilities.browse,false);
  await api.plan({gameRoot:'preview'});await api.run();
  for(let index=0;index<30;index++)await api.status();
  assert.equal(api.state.status,'blocked');assert.equal(api.state.needsActions[0].code,'preview-only');
  assert.equal(api.state.stages.find(row=>row.id==='build').status,'blocked');assert.notEqual(api.state.stages.find(row=>row.id==='install').status,'complete');
  await api.cancel();assert.equal(api.state.status,'cancelled');
  assert.ok(api.state.stages.every(row=>row.status!=='running'));
});


test('support transport downloads a token-protected ZIP and rejects foreign content',async()=>{
  let call;
  const blob=new Blob(['fixture']);
  const api=new LocalApi('http://127.0.0.1:1234','token',async(url,options)=>{
    call={url:String(url),options};return {ok:true,headers:new Map([['Content-Type','application/zip'],['Content-Disposition','attachment; filename="quest-build-support.zip"']]),blob:async()=>blob};
  });
  const result=await api.support('session-123');assert.equal(result.blob,blob);assert.equal(result.name,'quest-build-support.zip');
  assert.equal(call.options.headers['X-Quest-Token'],'token');assert.deepEqual(JSON.parse(call.options.body),{session:'session-123'});
  await assert.rejects(()=>api.support('../private'),error=>error.code==='invalidReply');
  const bad=new LocalApi('http://127.0.0.1:1234','token',async()=>({ok:true,headers:new Map([['Content-Type','text/html']])}));
  await assert.rejects(()=>bad.support('session-123'),error=>error.code==='invalidReply');
});
