"""Private HTTP artwork fixture projection; never a fresh inspect/build claim.

Copies only the actual three witnessed PNGs and unchanged recovery report. The
manually established inspect receipt inherits an existing native input manifest.
No original inputs or external project/cache are changed, and no tool is run.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import sys


def main():
    parser = argparse.ArgumentParser()
    for name in ('backend', 'state-root', 'session', 'ui-root', 'recovery', 'input'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    sys.path.insert(0, args.backend)
    from state import Store, digest
    from wizard import Engine
    spec = importlib.util.spec_from_file_location('ui_owned_art', Path(args.ui_root) / 'artwork.py')
    adapter = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(adapter)
    source = Path(args.recovery)
    original_input = json.loads(Path(args.input).read_text(encoding='utf-8'))
    key = original_input['game']['key']
    rows = adapter.verified_project_artwork(source, key)
    if len(rows) != 3:
        raise RuntimeError('The actual input game key must witness three original PNGs.')
    store = Store(args.state_root)
    state = store.load(args.session)
    if not Path(state['choices']['gameRoot']).is_dir():
        raise RuntimeError('The explicitly selected owned-game directory is unavailable.')
    project = store.root / 'build/cache/recovery' / ('f' * 64) / 'project'
    project.mkdir(parents=True)
    shutil.copyfile(source / 'quest-campaign-report.json', project / 'quest-campaign-report.json')
    for row in rows:
        destination = project / row['assetPath']
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / row['assetPath'], destination)
    owner = store.session_dir(args.session) / 'inherited-owned-input.json'
    shutil.copyfile(args.input, owner)
    store.publish(args.session, 'inspect', Engine(store).key(state, 'inspect'), [owner],
                  {'gameKey': key, 'fixture': True, 'inheritedInputSha256': digest(owner)})
    print(json.dumps({'count': len(rows), 'sha256': [row['sha256'] for row in rows],
                      'recoveryReportSha256': digest(project / 'quest-campaign-report.json'),
                      'scope': 'manually-established-original-artwork-transport-fixture'}))


if __name__ == '__main__':
    main()
