"""Named preparation/Unity compiler snapshots from the real progress planner."""
import copy
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(sys.argv[1]).resolve() / 'tools/quest-wizard'))
import stage_plan

row = {'id': 'build', 'status': 'running'}
clock = 1000
def event(phase, done=None, total=None, unit=None, operation=None, status=None):
    global clock
    clock += 1
    value = {'phase': phase, 'done': done, 'total': total, 'unit': unit, 'detail': None,
             'percent': None if total is None else 100 if total == 0 else 100 * done / total, 'updatedAt': clock}
    row['progress'] = stage_plan.advance(row, value, operation, status)

event('operation:graphics', operation='graphics', status='start')
event('prepare-substage:campaign-compute', 1, 2, 'steps', 'graphics', 'reuse')
event('prepare-substage:campaign-shaders', 1, 2, 'steps', 'graphics', 'start')
for name in ('backup', 'identities', 'bundles', 'containers'):
    event('prepare-items:campaign-shaders-' + name, 100, 100, 'files', status='complete')
event('prepare-items:campaign-shaders-inventory', 0, 3, 'shaders')
event('prepare-items:campaign-shaders-variants', 50, 100, 'variants')
rows = [copy.deepcopy(row)]
event('prepare-items:campaign-shaders-inventory', 1, 3, 'shaders')
event('prepare-items:campaign-shaders-variants', 0, 10, 'variants')
rows.append(copy.deepcopy(row))
event('prepare-items:campaign-shaders-inventory', 3, 3, 'shaders', status='complete')
event('prepare-items:campaign-shaders-programs', 10, 20, 'programs')
rows.append(copy.deepcopy(row))
event('prepare-substage:campaign-shaders', 2, 2, 'steps', 'graphics', 'complete')
event('operation:graphics', 1, 1, 'operations', 'graphics', 'complete')
event('operation:mod-banks', operation='mod-banks', status='start')
event('prepare-substage:mod-resource-banks', 3, 4, 'steps', 'mod-banks', 'start')
event('unity-shader-compile', 4903, 12288, 'variants', 'mod-banks')
rows.append(copy.deepcopy(row))
event('unity-shader-compile', 9467, 12288, 'variants', 'mod-banks')
rows.append(copy.deepcopy(row))
event('unity-shader-task', 2, 10, 'steps', 'mod-banks')
rows.append(copy.deepcopy(row))
print(json.dumps(rows))
