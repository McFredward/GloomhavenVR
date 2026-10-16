import test from 'node:test';
import assert from 'node:assert/strict';
import {failureView,updateFailureView} from '../model.mjs';
import {strings,translate} from '../i18n.mjs';

const guidance={schema:1,kind:'owned-input',nextSteps:{en:'Select the matching game.',de:'Passende Spielkopie auswählen.'},retryRetained:true,editInputs:true};
const action={code:'build_tool_failed',stage:'build',parameters:{failureStage:'update-mod',cause:'Actual current mismatch',updateGuidance:guidance}};
const failed={status:'failed',choices:{mode:'update-mod'},needsActions:[action]};
test('only current failed update actions expose bounded input-review suggestions',()=>{
  assert.deepEqual(updateFailureView(failed),{kind:'owned-input',nextSteps:guidance.nextSteps,editInputs:true});
  for(const status of ['running','complete','cancelled','blocked'])assert.equal(updateFailureView({...failed,status}),null);
  assert.equal(updateFailureView({...failed,choices:{mode:'build'}}),null);
  for(const value of [{...guidance,schema:2},{...guidance,kind:'reset-app-data'},{...guidance,editInputs:false},
    {...guidance,nextSteps:{en:'a'.repeat(2049),de:'OK'}},{...guidance,nextSteps:{en:'unsafe\nstep',de:'OK'}}])
    assert.equal(updateFailureView({...failed,needsActions:[{...action,parameters:{...action.parameters,updateGuidance:value}}]}),null);
  assert.equal(updateFailureView({...failed,needsActions:[{...action,parameters:{...action.parameters,failureStage:'recovery'}}]}),null);
  const actual={...failed,needsActions:[{...action,parameters:{...action.parameters,failureStage:'build',workflowMode:'update-mod'}}]};
  assert.equal(updateFailureView(actual).kind,'owned-input','legacy nested build stage uses the explicit current workflow');
  for(const workflowMode of [undefined,'build','update-profile'])assert.equal(updateFailureView({...actual,needsActions:[{...actual.needsActions[0],parameters:{...actual.needsActions[0].parameters,workflowMode}}]}),null);
});
test('blocked mandatory metadata error exposes actual cause without adopting a memory explanation',()=>{
  const action={code:'workspace_metadata_busy',stage:'build',message:{en:'File remains locked.',de:'Datei bleibt gesperrt.'},parameters:{cause:'PermissionError: WinError 5'}};
  assert.deepEqual(failureView({status:'blocked',needsActions:[action]}),{stage:'build',message:action.message,cause:action.parameters.cause,blocked:true,workspace:true});
  const path=String.raw`C:\Users\Fixture\.ghvrq\state.json`;
  assert.equal(failureView({status:'blocked',needsActions:[{...action,parameters:{...action.parameters,path}}]}).workspacePath,path);
  for(const path of ['x'.repeat(2049),'bad\npath',123])assert.equal(failureView({status:'blocked',needsActions:[{...action,parameters:{...action.parameters,path}}]}).workspacePath,undefined);
  for(const status of ['running','complete','cancelled'])assert.equal(failureView({status,needsActions:[action]}),null);
  assert.equal(failureView({status:'blocked',needsActions:[{...action,code:'quest_signing_repair_wait'}]}),null);
  for(const language of ['en','de'])for(const key of ['workspaceMetadataNext','workspaceMetadataBlockedCopy','updateFailureEditInputs'])assert.equal(typeof strings[language][key],'string');
  assert.match(translate('en','workspaceMetadataNext'),/Do not delete .ghvrq/);
  assert.doesNotMatch(translate('en','workspaceMetadataNext'),/paging|proxy|Defender/);
});
test('only an explicit current rebuild pause exposes the new-build decision',()=>{
  const rebuild={...guidance,kind:'rebuild-decision',newBuild:true};
  const action={code:'update_rebuild_decision_required',stage:'build',parameters:{workflowMode:'update-mod',failureStage:'build',cause:'Exact compatibility change',updateGuidance:rebuild}};
  const state={status:'blocked',choices:{mode:'update-mod'},needsActions:[action]};
  assert.equal(failureView(state).blocked,true);
  assert.deepEqual(updateFailureView(state),{kind:'rebuild-decision',nextSteps:rebuild.nextSteps,editInputs:true,newBuild:true});
  for(const newBuild of [false,undefined,'true'])assert.equal(updateFailureView({...state,needsActions:[{...action,parameters:{...action.parameters,updateGuidance:{...rebuild,newBuild}}}]}),null);
  for(const code of ['build_tool_failed','quest_signing_repair_wait'])assert.equal(updateFailureView({...state,needsActions:[{...action,code}]}),null);
  for(const status of ['running','complete','cancelled'])assert.equal(updateFailureView({...state,status}),null);
  for(const language of ['en','de'])for(const key of ['updateRebuildPausedCopy','updateRebuildChoice','updateRebuildConfirmTitle','updateRebuildConfirmCopy','updateRebuildConfirm','updateRebuildCancel'])assert.equal(typeof strings[language][key],'string');
  assert.match(translate('en','updateRebuildConfirmCopy'),/several hours.*additional disk space.*retained/s);
});
