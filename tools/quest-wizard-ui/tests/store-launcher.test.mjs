import test from 'node:test';
import assert from 'node:assert/strict';
import {launcherView,failureView} from '../model.mjs';
import {LocalApi} from '../transport.mjs';

function state(provider='steam'){
 return {status:'blocked',choices:{provider},needsActions:[{code:'store_launcher_required',stage:'inspect',message:{en:'Account needs action',de:'Konto benötigt Aktion'},parameters:{failureStage:'inspect',launcherProvider:provider,launcherGuidance:{schema:1,provider,nextSteps:{en:'Sign in and continue this run.',de:'Anmelden und diesen Run fortsetzen.'}}}}]};
}
test('all providers require current matching guided action and never use old completed actions',()=>{
 for(const provider of ['steam','epic','gog']){
  const value=state(provider);assert.equal(launcherView(value).provider,provider);assert.equal(failureView(value).blocked,true);
  for(const status of ['running','ready','complete','cancelled'])assert.equal(launcherView({...value,status}),null);
  assert.equal(launcherView({...value,choices:{provider:'other'}}),null);
  value.needsActions[0].parameters.launcherGuidance.nextSteps.de='bad\ntext';assert.equal(launcherView(value),null);
 }
 assert.equal(launcherView({status:'blocked'}),null);
});
test('launcher transport sends only the allowlisted provider; invalid inputs cause no request',async()=>{
 const calls=[];const api=new LocalApi('http://127.0.0.1:1234','fixture',async(url,options)=>{
  calls.push({url:String(url),...options});return {ok:true,headers:{get:()=> 'application/json'},json:async()=>({schema:1,event:'launcher-opened'})};
 });
 for(const provider of ['steam','epic','gog'])await api.openLauncher(provider);
 assert.deepEqual(calls.map(row=>JSON.parse(row.body)),[{provider:'steam'},{provider:'epic'},{provider:'gog'}]);
 for(const value of ['Steam','bad',null,{},['steam']])assert.throws(()=>api.openLauncher(value));
 assert.equal(calls.length,3);
});
