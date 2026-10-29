"""Measured October9 capacity rendered through actual failure/planner code."""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(sys.argv[1]).resolve() / 'tools/quest-wizard'))
from failures import native_memory_error
from processes import ProgressParser
import stage_plan
from state import stage_progress

resources = {'phase': 'il2cpp', 'totalMemoryBytes': 68438433792,
             'availableMemoryBytes': 35246387200, 'commitHeadroomBytes': 26303938560,
             'requiredCommitHeadroomBytes': 47646032691, 'nativeLaunchAllowed': False}
row = {'id': 'build', 'status': 'running'}
row['progress'] = stage_plan.advance(row, stage_progress('operation:weave'), 'weave', 'complete')
fields = ProgressParser().parse('resources: ' + json.dumps(resources), 'build.log')
status = fields.pop('status', None)
row['progress'] = stage_plan.advance(row, stage_progress(**fields), status=status)
row['status'] = 'blocked'
stage_plan.advance(row, row['progress'])
error = native_memory_error({'cause': 'Native compiler memory admission refused.'}, resources)
print(json.dumps({'stage': row, 'action': {'code': error.code, 'stage': 'build',
                                       'message': error.message, 'parameters': error.parameters}}))
