import test from 'node:test';
import assert from 'node:assert/strict';
import {LocalApi,PreviewApi} from '../transport.mjs';
import {choicesFromForm} from '../model.mjs';

test('device tools and inventory use authenticated bounded local requests independently of builds',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    calls.push({path:url.pathname,query:url.search,options});
    return {ok:true,json:async()=>({schema:1,event:url.pathname.includes('storage')?'storage':'device',state:{status:'idle'}})};
  });
  await api.deviceStatus();await api.deviceConnect('192.168.0.5:5555',true,'USB serial');
  await api.deviceCapture('192.168.0.5:5555','');await api.deviceCancel();await api.storageInventory();
  assert.deepEqual(calls.map(row=>row.path),['/api/device/status','/api/device/connect','/api/device/capture','/api/device/cancel','/api/storage/inventory']);
  assert.equal(calls[0].options.method,'GET');
  assert.deepEqual(JSON.parse(calls[1].options.body),{host:'192.168.0.5:5555',setupWireless:true,serial:'USB serial'});
  assert.deepEqual(JSON.parse(calls[2].options.body),{host:'192.168.0.5:5555',serial:''});
  for(const row of calls)assert.equal(row.options.headers['X-Quest-Token'],'local-token');
  assert.ok(calls.every(row=>!row.options.body?.includes('session')),'device tooling does not create or rerun a build');
});

test('capture download accepts only an opaque result ID and an authenticated ZIP',async()=>{
  let request;const id='a'.repeat(32),blob=new Blob(['fixture report']),api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    request={url,options};return {ok:true,headers:new Headers({'Content-Type':'application/zip','Content-Disposition':'attachment; filename="quest-capture-fixture.zip"'}),blob:async()=>blob};
  });
  for(const invalid of ['../savegames','C:\\private.zip','a'.repeat(31),'f'.repeat(33),null])await assert.rejects(api.deviceDownload(invalid),{code:'invalidReply'});
  assert.equal(request,undefined);
  const result=await api.deviceDownload(id);
  assert.equal(request.url.pathname,'/api/device/capture');assert.equal(request.url.search,'?id='+id);
  assert.equal(request.options.headers['X-Quest-Token'],'local-token');
  assert.equal(result.name,'quest-capture-fixture.zip');assert.equal(result.blob,blob);
  const wrong=new LocalApi('http://127.0.0.1','token',async()=>({ok:true,headers:new Headers({'Content-Type':'text/html'})}));
  await assert.rejects(wrong.deviceDownload(id),{code:'invalidReply'});
});

test('the labelled preview cannot connect hardware, collect logs or download captures',async()=>{
  const api=new PreviewApi();assert.equal((await api.deviceStatus()).state.status,'idle');
  for(const operation of ['deviceConnect','deviceCapture','deviceStart','deviceCancel','deviceDownload','deviceStoragePreview','deviceStorageClean'])await assert.rejects(api[operation](),{code:'preview'});
  assert.ok((await api.storageInventory()).inventory.totalBytes>0);
});

test('Quest cleanup submits only an opaque preview nonce and never an arbitrary remote path',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    calls.push({path:url.pathname,body:JSON.parse(options.body)});return {ok:true,json:async()=>({schema:1,event:'device'})};
  });
  await api.deviceStoragePreview('','');await api.deviceStorageClean('a'.repeat(32));
  assert.deepEqual(calls,[{path:'/api/device/storage-preview',body:{host:'',serial:''}},{path:'/api/device/storage-clean',body:{planId:'a'.repeat(32)}}]);
  for(const invalid of ['../','C:\\private','f'.repeat(31),null])assert.throws(()=>api.deviceStorageClean(invalid),{code:'invalidReply'});
  assert.equal(calls.length,2);
});

test('editing just an APK username inherits its ID, while ordinary manual profiles still require both fields',()=>{
  const form={gameRoot:'C:\\Owned game',provider:'steam',baseApk:'C:\\Quest.apk',mode:'update-profile',profileName:'New name',install:false};
  assert.deepEqual(choicesFromForm(form,'en'),{gameRoot:form.gameRoot,provider:'steam',mode:'update-profile',baseApk:form.baseApk,profileName:'New name',install:false,acceptUnityTerms:false,language:'en'});
  for(const mode of ['build','update-mod'])assert.throws(()=>choicesFromForm({...form,mode},'en'),/missingProfile/);
  const explicit=choicesFromForm({...form,profileId:'76561198000000001'},'en');
  assert.equal(explicit.profileName,undefined);assert.equal(explicit.profile.providerId,'76561198000000001');
});


test('standalone game starts send an explicit debugger boolean and ordinary launches default to false',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    calls.push({path:url.pathname,body:JSON.parse(options.body),headers:options.headers});
    return {ok:true,json:async()=>({schema:1,event:'device',state:{status:'started'}})};
  });
  await api.deviceStart();await api.deviceStart('','usb-fixture',true);
  assert.deepEqual(calls.map(({path,body})=>({path,body})),[
    {path:'/api/device/start',body:{host:'',serial:'',waitForDebugger:false}},
    {path:'/api/device/start',body:{host:'',serial:'usb-fixture',waitForDebugger:true}}
  ]);
  for(const row of calls)assert.equal(row.headers['X-Quest-Token'],'local-token');
  assert.throws(()=>api.deviceStart('','','true'),{code:'invalidReply'});
  assert.equal(calls.length,2);
});
