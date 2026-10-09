"""UI snapshots from the real durable work planner; no game or Unity tools."""
import copy
import json
import os
from pathlib import Path
import sys
import time
from zipfile import ZipFile

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

# Real opened startup checkpoint: byte observations advance its bounded share
# and the whole build while the movie producer has not yet returned.
row = {'id': 'build', 'status': 'running'}
for operation in stage_plan.PLANS['build'][:8]: event('operation:' + operation, 1, 1, 'operations', operation, 'reuse')
event('operation:startup-content', operation='startup-content', status='start')
event('prepare-substage:post-effects', 1, 11, 'checkpoints', 'startup-content', 'reuse')
event('prepare-substage:loading-resources', 2, 11, 'checkpoints', 'startup-content', 'reuse')
event('prepare-substage:startup-movies', 2, 11, 'checkpoints', 'startup-content', 'start')
event('prepare-items:startup-movies-backup', 1024, 1024, 'bytes', status='complete')
event('prepare-items:startup-movies-clips', 3, 3, 'files', status='complete')
event('prepare-items:startup-movies-scenes', 13, 13, 'scenes', status='complete')
event('prepare-items:startup-movies-assets', 0, 64 * 1048576, 'bytes'); states.append(copy.deepcopy(row))
event('prepare-items:startup-movies-assets', 16 * 1048576, 64 * 1048576, 'bytes'); states.append(copy.deepcopy(row))
event('prepare-items:startup-movies-assets', 48 * 1048576, 64 * 1048576, 'bytes'); states.append(copy.deepcopy(row))

# Capture 070011 reused the entire recovered project, then prepared display
# content. It emitted one staging receipt, not fourteen inner task boundaries.
row = {'id': 'build', 'status': 'running'}
event('operation:recovery', operation='recovery', status='start')
for section in stage_plan.RECOVERY_SECTIONS[:-1]:
    event('recovery-section:' + section, 1, 1, 'sections', status='reuse')
event('recovery-section:staging', 1, 1, 'sections', status='complete')
event('operation:recovery', 1, 1, 'operations', 'recovery', 'complete')
for operation in ('project-files', 'startup-content', 'native-runtime', 'audio'):
    event('operation:' + operation, 1, 1, 'operations', operation, 'complete')
event('operation:textures', operation='textures', status='start')
states.append(copy.deepcopy(row))
stage_plan.begin_attempt(row, 'retained-display-attempt')
event('operation:game-inputs', operation='game-inputs', status='start')
states.append(copy.deepcopy(row))
if os.environ.get('QUEST_WIZARD_PROGRESS_CAPTURE'):
    # Optional replay uses only existing bounded support exports. It downloads
    # no game data and preserves the capture's actual status/percent/owner.
    with ZipFile(os.environ['QUEST_WIZARD_PROGRESS_CAPTURE']) as archive:
        diagnostic = json.loads(archive.read('diagnostic.json'))
        first = json.loads(archive.read('wizard/progress.previous.log').splitlines()[0])
        states.append({'id': 'build', 'status': 'running', 'progress': first['parameters']})
        states.append(next(item for item in diagnostic['stages'] if item['id'] == 'build'))
print(json.dumps(states))
