"""Actual HTTP/Engine fixture: repeatable wait and failed conversion, no tools."""
import json
from pathlib import Path
import sys

backend, state_root, ui_root, artwork = map(Path, sys.argv[1:5])
sys.path.insert(0, str(backend.parent))
from state import Store, WizardError, STAGES
from wizard import Engine, choices
from server import LocalServer
from promotional import Gallery
from unity_setup import _wait

store = Store(state_root)
game = state_root / 'Fixture owned game'
state = store.create(choices({'gameRoot': str(game), 'language': 'de', 'acceptUnityTerms': True, 'install': False}))

def action(name):
    def run(state, supervisor):
        session = state['session']
        if name == 'unity':
            store.progress(session, name, 'unity-prerequisites', 2, 4, 'checks')
            def opener(*_, **__):
                store.record(session, 'fixture_window_opened', name)
            _wait(store, session, 'unity_login_required',
                  'Sign in and activate an eligible Unity license. Then check prerequisites.',
                  'In Unity Hub anmelden und eine gültige Lizenz aktivieren. Anschließend Voraussetzungen prüfen.',
                  lambda: (state_root / 'Fixture Unity Hub.exe', False), opener=opener, poll=.01)
        if name == 'build':
            store.progress(session, name, 'recovery-batches', 7, 29, 'batches', 'Fixture batch 7 / 29')
            log = store.session_dir(session) / 'logs/build.log'
            log.write_text('Fixture: no external tools were executed.\nFAILED: original object identity missing\n')
            raise WizardError('build_tool_failed', 'Game asset conversion failed. Save the diagnostic package.',
                              'Die Konvertierung der Spielassets ist fehlgeschlagen. Diagnosepaket speichern.',
                              failureStage='recovery', cause='FAILED: original object identity missing', exitCode=1)
        path = store.session_dir(session) / (name + '.txt'); path.write_text('fixture')
        return [path], {}
    return run

actions = {name: action(name) for name in STAGES}
def discover(*_):
    return {'schema': 1, 'event': 'discovery', 'capabilities': {'logs': True, 'support': True, 'browse': False},
            'games': [], 'unityEditors': [], 'recentSessions': [{'session': state['session']}]}

server = LocalServer(store, ui_root, discover=discover, engine_factory=lambda value: Engine(value, actions=actions))
gallery = Gallery(ui_root, state_root / 'artwork/publisher', opener=lambda *_, **__: (_ for _ in ()).throw(OSError('fixture network disabled')))
if artwork.is_dir():
    import shutil
    gallery.cache.mkdir(parents=True, exist_ok=True)
    for row in gallery.pins:
        source = artwork / (row['id'] + Path(row['url']).suffix)
        if source.is_file(): shutil.copyfile(source, gallery.cache / source.name)
gallery.fetch(); server.promo = gallery
print(json.dumps({'url': server.url, 'session': state['session']}), flush=True)
try: server.serve_forever()
except KeyboardInterrupt: pass
finally: server.close_owned()
