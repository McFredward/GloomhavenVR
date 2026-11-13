#!/usr/bin/env python3
"""Exercise actual embedded-resource/provenance closure and explicit native-bank failure seams."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
MODES = ('success', 'missing-bank', 'missing-provenance', 'wrong-hash', 'wrong-target', 'wrong-api', 'shared-eye', 'load-null', 'wrong-bank', 'wrong-path', 'missing-shader', 'wrong-shader', 'unsupported')
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--case', choices=MODES, action='append', help='Rerun selected resource cases after an artifact-only change.')
parser.add_argument('--plugin', type=Path, help='Also require resource closure of this actually compiled plugin; independent host cases need no game SDK build.')
args = parser.parse_args()
out = ROOT / '.planning/debug/quest-loading-assets'
out.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
assets = ROOT / 'src/GloomhavenVR/Assets/QuestLoading'
fixture = ROOT / 'tests/QuestLoadingAssets.Tests'
runtime = [ROOT / 'src/GloomhavenVR/Core/QuestLoadingAssets.cs', ROOT / 'src/GloomhavenVR/Core/Perf/QuestEnvironmentJson.cs', ROOT / 'src/GloomhavenVR/Core/SelfUpdate/SelfUpdateRelease.cs']
inputs = [*runtime, *assets.iterdir(), *fixture.iterdir()]
manifest = {'inputSha256': {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs if path.is_file()}, 'cases': [],
            'limits': 'Actual embedded Android bytes, parser, SHA checks and caller lifecycle execute; native Android bank/shader APIs are modeled. Separate binary metadata qualification proves compiled program presence.'}
status = 1


def execute(argv, log, mode):
    with log.open(mode) as stream:
        child = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try: return child.wait(timeout=90)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL); child.wait()


try:
    provenance = json.loads((assets / 'provenance.json').read_text())
    assert provenance['sourceSha256'] == hashlib.sha256((assets / 'QuestLoadingSpinner.shader').read_bytes()).hexdigest()
    assert provenance['bundleSha256'] == hashlib.sha256((assets / 'ghvr-quest-loading.bundle').read_bytes()).hexdigest()
    csproj = (ROOT / 'src/GloomhavenVR/GloomhavenVR.csproj').read_text()
    assert 'GloomhavenVR.QuestLoading.spinner.bundle' in csproj and 'GloomhavenVR.QuestLoading.provenance.json' in csproj
    modes = args.case or MODES
    for mode in modes:
        case = run / mode
        case.mkdir()
        for path in fixture.iterdir():
            if path.is_file(): shutil.copyfile(path, case / path.name)
        for path in runtime: shutil.copyfile(path, case / path.name)
        if mode != 'missing-bank': shutil.copyfile(assets / 'ghvr-quest-loading.bundle', case / 'ghvr-quest-loading.bundle')
        value = dict(provenance)
        if mode == 'wrong-hash': value['bundleSha256'] = '0' * 64
        if mode == 'wrong-target': value['target'] = 'StandaloneWindows64'
        if mode != 'missing-provenance': (case / 'provenance.json').write_text(json.dumps(value))
        log = case / 'result.log'
        code = execute([dotnet, 'build', str(case / 'QuestLoadingAssets.Tests.csproj'), '-c', 'Release', '--disable-build-servers', '-p:UseSharedCompilation=false'], log, 'w')
        if code: raise RuntimeError(log.read_text())
        code = execute([dotnet, str(case / 'bin/Release/net8.0/QuestLoadingAssets.Tests.dll'), mode], log, 'a')
        manifest['cases'].append({'mode': mode, 'exitCode': code, 'log': str(log)})
        print(mode + ': ' + ('PASS' if code == 0 else 'FAIL'), flush=True)
        if code: raise RuntimeError(log.read_text())
    if args.plugin is not None:
        plugin = args.plugin.resolve()
        if not plugin.is_file(): raise RuntimeError('Build the selected plugin before qualifying embedded resource closure.')
        closure_log = run / 'compiled-plugin-resources.log'
        code = execute([dotnet, str(run / modes[0] / 'bin/Release/net8.0/QuestLoadingAssets.Tests.dll'), '--assembly', str(plugin)], closure_log, 'w')
        if code: raise RuntimeError(closure_log.read_text())
        manifest['compiledPlugin'] = {'path': str(plugin), 'sha256': hashlib.sha256(plugin.read_bytes()).hexdigest(), 'closurePassed': True}
    else:
        manifest['compiledPlugin'] = {'closurePassed': False, 'reason': 'Host-only cases; actual compiled-plugin closure is a separate explicit integration check.'}
    status = 0
finally:
    (run / 'results.json').write_text(json.dumps(manifest, indent=2) + '\n')
    log = run / 'completion.log'
    log.write_text('Evidence: ' + str(run) + '\n')
    receipt = cleanup_completed_run('quest-loading-assets', status, log, ROOT, process_reaped=True)
    (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
