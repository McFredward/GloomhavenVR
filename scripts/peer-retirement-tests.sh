#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${GHVR_TEST_SOURCE_ROOT:-$repo_root}"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
python3 - "$repo_root" "$source_root" <<'PY'
from pathlib import Path
import hashlib, importlib.util, json, re, shutil, subprocess, sys, tempfile, time

root, source_root = map(Path, sys.argv[1:])
driver_path = source_root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs'
results_path = source_root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.Results.cs'
guard_path = source_root / 'src/GloomhavenVR/Core/Perf/TickGuard.cs'
fixture = root / 'tests/GloomhavenVR.PeerRetirementTests'
out = root / '.planning/debug/peer-retirement'; out.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
log = run / 'console.log'; log.write_text('Evidence: ' + str(run) + '\n')
print('Evidence: ' + str(run), flush=True)
started, status = time.monotonic(), 1
source = driver_path.read_text()
def extract(text, signature):
    assert text.count(signature) == 1, 'Production method binding drift: ' + signature
    start = text.index(signature); end = text.index('{', start) + 1; depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}'); end += 1
    return text[start:end]
names = ['    private void ForgetPeer(int playerId)', '    private void DestroyAllAvatars()', '    private void OnDisable()']
methods = [extract(source, name) for name in names]
# Keep the actual subscription and forwarding callback, with only its event
# payload/pose destination supplied by the explicit fixture boundary.
enable = extract(source, '    private void OnEnable()')
results_source = results_path.read_text()
callback = extract(results_source, '    private static void OnResultsWindowVisibility(')
# Only the unrelated sent-state assignment fields are type-erased boundary storage. The peer
# dictionaries, snapshots, callbacks, method bodies and TickGuard execute their actual logic.
scalars = sorted(set(re.findall(r'\b(_\w+)\s*=', methods[2])))
header = ('using System;using System.Collections.Generic;using GloomhavenVR.Core;using GloomhavenVR.Core.Events;'
    'namespace GloomhavenVR.Net;internal sealed partial class NetAvatarDriver {\n'
    + '\n'.join('internal object? ' + name + ';' for name in scalars) + '\n' + enable + '\n' + callback + '\n')
production = header + '\n'.join(methods) + '\n}'
cases = [('production', production, None)]
old_path = root / 'scripts/peer-retirement-tests/historical-757.cs'
historical = old_path.read_text()
for index, label in enumerate(('forget-peer', 'destroy-all', 'disable')):
    old = methods.copy(); old[index] = extract(historical, names[index])
    cases.append(('historical-' + label, header + '\n'.join(old) + '\n}', 'teardown must isolate a failed owner'))
for label, before, after, expected in [
    ('late-identity-retirement', '_avatars.Remove(playerId);', '', 'driver identity is retired before destructor lifecycle callbacks'),
    ('blanket-clear-new-peers', '\n    private void OnDisable()', '\n    private void OnDisable()', None),
    ('lost-snapshot-identity', '&& ReferenceEquals(current, peer.Value)', '&& current != null', 'snapshot identity check preserves a same-ID replacement'),
    ('unprotected-preview', 'TickGuard.Run("Net.ForgetPeer.DamagePreview", () => WorldUI.Surfaces.DamageDecisionPreview.ResetRemote(avatar), "Net");', 'WorldUI.Surfaces.DamageDecisionPreview.ResetRemote(avatar);', 'teardown must isolate a failed owner'),
    ('unprotected-props', 'TickGuard.Run("Net.ForgetPeer.Props", () => NetProps.ReleaseRemote(playerId), "Net");', 'NetProps.ReleaseRemote(playerId);', 'teardown must isolate a failed owner'),
    ('unprotected-session-reset', 'TickGuard.Run("Net.DestroyAvatars.NativePresentation", ResetNativePresentation, "Net");', 'ResetNativePresentation();', 'teardown must isolate a failed owner'),
    ('unprotected-disable-video', 'TickGuard.Run("Net.Disable.Video", RemoteVideoPlayback.Reset, "Net");', 'RemoteVideoPlayback.Reset();', 'teardown must isolate a failed owner'),
    ('retained-results-callback', 'VREvents.WindowVisibility -= OnResultsWindowVisibility;', '/* Results unsubscribe omitted by causal control */', 'disable retires the exact Results visibility subscription'),
    ('lost-scroll-visibility-forwarding', 'SharedWindowScrollSync.ObserveWindowVisibility(change.Window, change.Shown);', '', 'shared scroll observes the original exact window visibility transition'),
]:
    if label == 'blanket-clear-new-peers':
        text = production.replace(methods[1], methods[1][:-1] + '_avatars.Clear();\n    }')
        expected = 'teardown snapshot preserves a new peer created during a destructor callback'
    else:
        assert production.count(before) == 1, 'Mutation binding drift: ' + label
        text = production.replace(before, after)
        if label == 'late-identity-retirement':
            text = text.replace('TickGuard.Run("Net.ForgetPeer.Avatar", avatar.Destroy, "Net");',
                'TickGuard.Run("Net.ForgetPeer.Avatar", avatar.Destroy, "Net");\n            _avatars.Remove(playerId);')
    cases.append((label, text, expected))
bindings = [driver_path, results_path, guard_path, old_path, root / 'scripts/peer-retirement-tests.sh',
    root / 'scripts/town_run_retention.py', *fixture.glob('*.cs'), *fixture.glob('*.csproj')]
(run / 'source-bindings.json').write_text(json.dumps({str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in bindings}, indent=2) + '\n')
try:
    for name, text, expected in cases:
        case = run / name; project = case / 'project'; project.mkdir(parents=True)
        for original in fixture.iterdir():
            if original.suffix in ('.cs', '.csproj'): shutil.copy2(original, project / original.name)
        method_path = case / 'Retirement.fixture'; method_path.write_text(text)
        result = subprocess.run(['dotnet', 'run', '--project', str(project / 'GloomhavenVR.PeerRetirementTests.csproj'),
            '--configuration', 'Release', '--property:RetirementSource=' + str(method_path),
            '--property:GuardSource=' + str(guard_path)], capture_output=True, text=True)
        transcript = result.stdout + result.stderr; (case / 'result.log').write_text(transcript)
        executed = case / 'executed-assemblies'; executed.mkdir()
        for artifact in (project / 'bin/Release/net8.0').glob('*'):
            if artifact.is_file(): shutil.copy2(artifact, executed / artifact.name)
        (case / 'executed-pins.json').write_text(json.dumps({p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in executed.iterdir()}, indent=2) + '\n')
        with log.open('a') as stream: stream.write(transcript + '\n')
        if expected is None:
            print(transcript, end='', flush=True)
            if result.returncode: raise RuntimeError('Production retirement fixture failed: ' + str(case / 'result.log'))
        elif result.returncode == 0 or expected not in transcript:
            raise RuntimeError('Defect control escaped or failed at an unrelated boundary: ' + str(case / 'result.log'))
        else: print('Peer retirement control: ' + name + ' rejected.', flush=True)
    status = 0
finally:
    (run / 'validation.json').write_text(json.dumps({'suite': 'peer-retirement', 'exit_code': status,
        'elapsed_seconds': time.monotonic() - started, 'cases': len(cases), 'negative_controls': len(cases) - 1}, indent=2) + '\n')
    spec = importlib.util.spec_from_file_location('retention', root / 'scripts/town_run_retention.py')
    retention = importlib.util.module_from_spec(spec); spec.loader.exec_module(retention)
    receipt = retention.cleanup_completed_run('peer-retirement', status, log, root, process_reaped=True)
    (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
PY
