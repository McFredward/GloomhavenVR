"""Production failure selection supplies real bilingual update guidance to CDP."""
import json
from pathlib import Path
import sys
import tempfile
import time

repo = Path(sys.argv[1]).resolve()
sys.path.insert(0, str(repo / 'tools/quest-wizard'))
from failures import tool_failure
from state import Store, WizardError, atomic_json

with tempfile.TemporaryDirectory(prefix='quest-update-guidance-') as directory:
    root = Path(directory); Store(root)
    log = root / 'sessions/current/logs/build.log'; log.parent.mkdir(parents=True)
    cause = "The PC game does not match this APK's original assembly: Managed/Assembly-CSharp.dll; use the matching PC installation or create a full build."
    log.write_text('Quest builder: ' + cause)
    atomic_json(root / 'build/last-failure.json', {'schema': 1, 'stage': 'build', 'message': cause})
    update = tool_failure(root, 'build', log, time.time() - 1, 'python.exe', 1, workflow_mode='update-mod')
    # The mandatory-file writer is tested against actual OS errors separately;
    # this fixture exercises its exact public error ABI rather than simulating
    # Windows file locking on Linux.
    metadata = WizardError('workspace_metadata_busy',
        'A required workspace metadata file remains locked after automatic retries. Close programs using the workspace and retry.',
        'Eine erforderliche Verwaltungsdatei des Arbeitsordners bleibt nach automatischen Wiederholungen gesperrt. Programme schließen, die den Arbeitsordner verwenden, und erneut versuchen.',
        path=r'C:\Users\Fixture\.ghvrq\sessions\current\state.json', retryable=True,
        completedWorkRetained=True, cause='PermissionError: [WinError 5] Access is denied', retryAttempts=4)
    print(json.dumps({name: {'code': error.code, 'stage': 'build', 'message': error.message, 'parameters': error.parameters}
                      for name, error in (('update', update), ('metadata', metadata))}))
