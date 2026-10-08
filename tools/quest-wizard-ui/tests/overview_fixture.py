"""UI snapshots from the real durable work planner; no game or Unity tools."""
import copy
import json
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(sys.argv[1]).resolve() / 'tools/quest-wizard'))
import stage_plan

row = {'id': 'build', 'status': 'running'}
clock = time.time() - 1
def event(phase, done=None, total=None, unit=None, operation=None, status=None):
    global clock
    clock += .01
    value = {'phase': phase, 'done': done, 'total': total, 'unit': unit, 'detail': None,
             'percent': None if total is None else 100 if total == 0 else 100 * done / total,
             'updatedAt': clock}
    row['progress'] = stage_plan.advance(row, value, operation, status)

def raw():
    event('recovery-plan', 16, 16, 'batches', status='reuse')
    for section in stage_plan.RECOVERY_SECTIONS[:-1]:
        event('recovery-section:' + section, 1, 1, 'sections', status='reuse')
        if section == 'batches': event('recovery-batches', 16, 16, 'batches', status='reuse')
    event('recovery-section:staging', status='start')
    for step in ('catalog', 'canonical'): event('staging-section:' + step, 1, 1, 'steps', status='reuse')
    event('staging-section:copy', 0, 1, 'steps', status='start')

event('operation:recovery', operation='recovery', status='start')
raw(); event('staging-copy', 0, 189701, 'files')
row['progressPlan']['percent'] = 47
row['progressPlan']['workRevision'] = 4
row['status'] = 'failed'; stage_plan.advance(row, row['progress'])
states = [copy.deepcopy(row)]
row['status'] = 'running'; event('starting'); event('operation:game-inputs', operation='game-inputs', status='start')
states.append(copy.deepcopy(row))
for operation in stage_plan.PLANS['build'][:6]: event('operation:' + operation, 1, 1, 'operations', operation, 'reuse')
event('operation:recovery', operation='recovery', status='start'); raw()
event('staging-copy', 25000, 189701, 'files'); states.append(copy.deepcopy(row))
event('staging-copy', 150000, 189701, 'files'); states.append(copy.deepcopy(row))
event('operation:project-files', 1, 1, 'operations', 'project-files', 'reuse')
event('operation:startup-content', operation='startup-content', status='start')
event('prepare-substage:startup-movies', 2, 6, 'checkpoints', 'startup-content', 'complete'); states.append(copy.deepcopy(row))
print(json.dumps(states))
