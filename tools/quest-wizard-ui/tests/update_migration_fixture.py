"""Production Store/ProgressParser snapshots; no Unity or APK build tools."""
import copy
from contextlib import ExitStack
import json
from pathlib import Path
import sys
import tempfile

repo = Path(sys.argv[1]).resolve()
sys.path.insert(0, str(repo / 'tools/quest-wizard'))
from processes import ProgressParser
import state
from state import Store
from wizard import choices

# Persist each controlled snapshot boundary without introducing wall-clock
# delays. Production progress calculations and parser validation are unchanged.
state.PROGRESS_INTERVAL = 0


with tempfile.TemporaryDirectory(prefix='quest-migration-ui-') as directory, ExitStack() as owned:
    root = Path(directory)
    store = Store(root / 'owned')
    # The production reader correctly marks an unlocked RUNNING owner as
    # interrupted. Hold the real kernel guard while projecting a live run.
    owned.enter_context(state.file_lock(store.root / 'run.lock'))
    saved = store.create(choices({'mode': 'update-mod', 'gameRoot': str(root / 'PC'),
                                  'baseApk': str(root / 'base.apk'), 'acceptUnityTerms': True,
                                  'install': False}))
    session = saved['session']
    parser = ProgressParser()
    store.begin_run(session)
    store.begin_stage(session, 'build', 'same-key', work_key='same-work')

    def running():
        saved = store.load(session)
        saved['status'] = 'running'
        for row in saved['stages']:
            row['status'] = 'complete' if row['id'] in ('tools', 'source', 'unity', 'profile', 'inspect') else 'running' if row['id'] == 'build' else 'pending'
        store.save(saved)

    def feed(**value):
        fields = parser.parse('GHVRQ_PROGRESS ' + json.dumps({'schema': 1, **value}), 'build.log')
        assert fields is not None, value
        store.progress(session, 'build', **fields)

    def snapshot():
        return copy.deepcopy(store.load(session))

    running()
    for name in ('update-owned-game', 'update-mod-source'):
        feed(phase='operation:' + name, operation=name, done=1, total=1, unit='operations', status='complete')
    feed(phase='operation:update-code', operation='update-code', status='start')
    feed(phase='unity-progress', operation='update-code', done=1, total=2, unit='tasks', status='progress')
    states = {'compact': snapshot()}
    feed(phase='update-rebuild-required', operation='update-code', status='progress',
         detail=r'INTERNAL_C:\private\changed-renderer.asset; shader-source-drift')
    states['migration'] = snapshot()
    for done in (1, 3):
        feed(phase='game-hash', operation='game-inputs', done=done, total=4, unit='bytes', status='progress')
        states['measured-' + str(done)] = snapshot()
    for name in ('game-inputs', 'mod-inputs', 'profile-inputs', 'source-snapshot', 'game-snapshot', 'snapshot-check'):
        feed(phase='operation:' + name, operation=name, done=1, total=1, unit='operations', status='reuse')
    feed(phase='operation:recovery', operation='recovery', status='start')
    feed(phase='recovery-plan', operation='recovery', done=0, total=10, unit='batches', status='progress')
    feed(phase='recovery-section:source', operation='recovery', done=0, total=1, unit='sections', status='start')
    feed(phase='recovery-source-hash', operation='recovery', done=4, total=10, unit='bytes', status='progress')
    states['reused'] = snapshot()
    saved = snapshot()
    saved['status'] = 'failed'
    saved['stages'][5]['status'] = 'failed'
    saved['needsActions'] = [{'code': 'build_tool_failed', 'stage': 'build',
                             'message': {'de': 'Aktueller Testschritt unterbrochen.', 'en': 'Current fixture step interrupted.'},
                             'parameters': {'failureStage': 'recovery', 'cause': 'Fixture current recovery failure'}}]
    store.save(saved)
    states['failed'] = snapshot()
    store.begin_run(session)
    store.begin_stage(session, 'build', 'same-key', work_key='same-work')
    states['retry'] = snapshot()
    running()
    feed(phase='operation:recovery', operation='recovery', status='start')
    feed(phase='recovery-plan', operation='recovery', done=0, total=10, unit='batches', status='progress')
    feed(phase='recovery-section:source', operation='recovery', done=0, total=1, unit='sections', status='start')
    feed(phase='recovery-source-hash', operation='recovery', done=8, total=10, unit='bytes', status='progress')
    states['retry-current'] = snapshot()
    print(json.dumps(states))
