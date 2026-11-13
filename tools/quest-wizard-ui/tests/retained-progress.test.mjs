import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {progressView,buildOverviewView,stageProgress} from '../model.mjs';

test('real Store keeps closed game work primary across a changed release and current prerequisite keys',()=>{
  const fixture=fileURLToPath(new URL('./retained_fixture.py',import.meta.url));
  const repo=fileURLToPath(new URL('../../../',import.meta.url));
  const result=spawnSync(process.env.QUEST_WIZARD_PYTHON??'python3',['-I','-B',fixture,repo],{encoding:'utf8',timeout:15000});
  assert.equal(result.status,0,result.stderr);
  const snapshots=JSON.parse(result.stdout);
  const before=progressView(snapshots[0]);
  const retained=48+3/11;
  assert.ok(before.percent>retained,'the previous attempt includes one unfinished item fraction');
  for(const [index,id] of [[1,'tools'],[2,'source']]){
    const view=progressView(snapshots[index]),overview=buildOverviewView(snapshots[index]);
    assert.equal(view.phase,'build');assert.equal(view.current.id,'build');assert.equal(view.active.id,id);
    assert.equal(view.retaining,true);assert.equal(view.percent,Number(retained.toFixed(6)));
    assert.equal(overview.active,null);assert.equal(overview.done,8);
    assert.deepEqual(overview.recovery.batches,{done:16,total:16});
    assert.equal(overview.recovery.staging.filter(row=>row.closed).length,14);
    assert.equal(overview.groups.flatMap(row=>row.operations).some(row=>['failed','running','checking'].includes(row.status)),false);
    assert.equal(snapshots[index].needsActions.length,0);
    assert.ok(stageProgress(view.active).phasePercent!==null,'actual prerequisite counter remains separate');
  }
  const running=progressView(snapshots[3]),overview=buildOverviewView(snapshots[3]);
  assert.equal(running.active.id,'build');assert.equal(running.retaining,false);assert.equal(running.percent,Number(retained.toFixed(6)));
  assert.equal(overview.active,'game-inputs');
  assert.equal(overview.groups[0].operations[0].status,'checking');
  assert.equal(overview.groups[0].operations[1].status,'retained');
  const resumed=progressView(snapshots[4]);
  assert.ok(resumed.percent>running.percent,'new measured items advance from the retained closed-work floor');
  assert.equal(stageProgress(resumed.current).done,100);assert.equal(stageProgress(resumed.current).total,3369);
  assert.equal(buildOverviewView(snapshots[4]).active,'startup-content');
});
