import test from 'node:test';
import assert from 'node:assert/strict';
import {LocalApi,PreviewApi} from '../transport.mjs';

test('storage plans and explicit cleanup use authenticated local requests without a build session',async()=>{
  const calls=[],api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'storage',state:{status:'preview'}})};
  });
  await api.storage();await api.storagePlan();await api.storagePlan('build-cache');await api.storageClean('explicit-plan-id');
  assert.equal(calls[0].url,'http://127.0.0.1:1234/api/storage');assert.equal(calls[0].options.method,'GET');
  assert.deepEqual(calls.slice(1).map(row=>JSON.parse(row.options.body)),[{mode:'duplicates'},{mode:'build-cache'},{planId:'explicit-plan-id'}]);
  for(const row of calls){assert.equal(row.options.headers['X-Quest-Token'],'local-token');assert.equal(row.options.cache,'no-store');}
  for(const mode of ['all','',null,true])assert.throws(()=>api.storagePlan(mode),error=>error.code==='invalidReply');
  for(const plan of ['',null,true,'a'.repeat(257)])assert.throws(()=>api.storageClean(plan),error=>error.code==='invalidReply');
  assert.equal(calls.length,4,'invalid user values never issue a cleanup request');
});

test('the labelled design preview never contains deletable workspace files',async()=>{
  const api=new PreviewApi();assert.equal((await api.storage()).state.status,'idle');
  const plan=await api.storagePlan('build-cache');assert.deepEqual(plan.state.plan.paths,[]);assert.equal(plan.state.plan.files,0);
  assert.deepEqual((await api.storageClean()).state.result,{freedBytes:0,deletedFiles:0});
});

test('stopped-run previews validate the explicit cleanup choice before authenticated requests',async()=>{
  const calls=[],session='ab'.repeat(16),api=new LocalApi('http://127.0.0.1:1234','local-token',async(url,options)=>{
    calls.push({url:String(url),options});return {ok:true,json:async()=>({schema:1,event:'storage',state:{status:'preview'}})};
  });
  await api.storageRuns();await api.storagePlan('session',session);await api.storagePlan('session',session,true);
  assert.equal(calls[0].url,'http://127.0.0.1:1234/api/storage/runs');assert.equal(calls[0].options.method,'GET');
  assert.deepEqual(calls.slice(1).map(row=>JSON.parse(row.options.body)),[{mode:'session',session,removeData:false},{mode:'session',session,removeData:true}]);
  for(const row of calls)assert.equal(row.options.headers['X-Quest-Token'],'local-token');
  for(const id of ['',null,{},'../other','AB'.repeat(16),'ab'.repeat(17)])assert.throws(()=>api.storagePlan('session',id,true),error=>error.code==='invalidReply');
  for(const choice of ['yes',1,null,{}])assert.throws(()=>api.storagePlan('session',session,choice),error=>error.code==='invalidReply');
  assert.throws(()=>api.storagePlan('build-cache',session,true),error=>error.code==='invalidReply');
  assert.equal(calls.length,3,'invalid or cross-mode inputs never trigger deletion planning');
});
