#!/usr/bin/env python3
"""Execute actual bounded retained performance evidence and compiling causal controls."""
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
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-root', type=Path, default=ROOT)
args = parser.parse_args()
source = args.source_root.resolve() / 'src/GloomhavenVR/Core/Perf'
out = ROOT / '.planning/debug/quest-performance-history'
out.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
files = {name: (source / name).read_text() for name in ('QuestPerformanceEvidence.cs', 'QuestPerformanceHistory.cs')}
controls = [
    ('retain-ordinary-player', 'QuestPerformanceEvidence.cs', '!QuestStandalonePlatform.Enabled || !VRLog.WantsDebug', '!QuestStandalonePlatform.Enabled'),
    ('omit-delivered-history', 'QuestPerformanceEvidence.cs', 'System.Array.Copy(timings, 0, result, 7 + hexes.Length + fire.Length, timings.Length);', '// omit original timing consumer'),
    ('unbounded-report-count', 'QuestPerformanceHistory.cs', 'Capacity = 24', 'Capacity = 25'),
    ('retain-old-session', 'QuestPerformanceHistory.cs', 'Array.Clear(_lines, 0, _lines.Length); _count = _cursor = 0;', '// retain previous session'),
    ('reverse-observation-order', 'QuestPerformanceHistory.cs', '_lines[(first + index) % Capacity]', '_lines[(_cursor - 1 - index + Capacity) % Capacity]'),
]
manifest = {'sourceRoot': str(args.source_root.resolve()), 'sourceSha256': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in files.items()}, 'cases': []}
dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
exit_code = 1
try:
    for name, control in [('production', None)] + [(item[0], item) for item in controls]:
        case = run / name
        case.mkdir()
        fixture = ROOT / 'tests/QuestPerformanceHistory.Tests'
        for filename in ('Program.cs', 'Boundaries.cs', 'QuestPerformanceHistory.Tests.csproj'):
            shutil.copyfile(fixture / filename, case / filename)
        for filename, text in files.items():
            if control and filename == control[1]:
                assert text.count(control[2]) == 1, 'Causal control source drift: ' + name
                text = text.replace(control[2], control[3])
            (case / filename).write_text(text)
        command = [dotnet, 'build', str(case / 'QuestPerformanceHistory.Tests.csproj'), '-c', 'Release', '--disable-build-servers', '-p:UseSharedCompilation=false', '-p:RuntimeSource=' + str(case)]
        log_path = case / 'result.log'
        with log_path.open('w') as log:
            child = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = child.wait(timeout=90)
            except BaseException:
                os.killpg(child.pid, signal.SIGKILL); child.wait()
                raise
        compiled = code == 0
        if compiled:
            with log_path.open('a') as log:
                child = subprocess.Popen([dotnet, str(case / 'bin/Release/net8.0/QuestPerformanceHistory.Tests.dll')], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                try:
                    code = child.wait(timeout=30)
                except BaseException:
                    os.killpg(child.pid, signal.SIGKILL); child.wait()
                    raise
        text = log_path.read_text()
        ok = code == 0 if control is None else code != 0 and compiled
        manifest['cases'].append({'name': name, 'exitCode': code, 'compiled': compiled, 'passed': ok, 'log': str(log_path)})
        print(name + ': ' + ('PASS' if ok else 'FAIL'), flush=True)
        if not ok: raise RuntimeError(text)
    exit_code = 0
finally:
    (run / 'results.json').write_text(json.dumps(manifest, indent=2) + '\n')
    cleanup_input = run / 'completion.log'
    cleanup_input.write_text('Evidence: ' + str(run) + '\n')
    receipt = cleanup_completed_run('quest-performance-history', exit_code, cleanup_input, ROOT, process_reaped=True)
    (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
