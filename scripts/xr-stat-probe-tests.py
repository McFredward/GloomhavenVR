#!/usr/bin/env python3
"""Execute the actual provider probe with controlled APIs and optional real SDK metadata.

Provider availability, frame timing and headset speed remain hardware questions.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--sdk-managed', type=Path)
args = parser.parse_args()
raw = (ROOT / 'src/GloomhavenVR/Core/Perf/PerfMonitor.cs').read_text()
start = raw.index('    private static class XrProbe\n')
end = raw.rindex('\n    }\n}') + len('\n    }')
probe = raw[start:end]
header = 'using System;\nusing System.Collections.Generic;\nusing System.Reflection;\nusing System.Text;\nusing UnityEngine;\nusing UnityEngine.XR;\nnamespace GloomhavenVR.Core { internal static partial class PerfMonitor {\n'
controls = [
    ('provider-api-lost', '"TryGetAppGPUTimeLastFrame"', '"TryGetGPUTimeLastFrame"', 'provider seconds become millisecond samples'),
    ('seconds-not-converted', 'ms = seconds * 1000f;', 'ms = seconds;', 'provider seconds become millisecond samples'),
    ('invalid-provider-samples', 'seconds >= 0f && !float.IsInfinity(seconds) && !float.IsNaN(seconds)', 'true', 'invalid provider data remains unavailable'),
    ('provider-counters-lost', '_dropped = Bind<DisplayIntStat>(typeof(XRDisplaySubsystem), "TryGetDroppedFrameCount");', '_dropped = null;', 'display-owned counters and seconds retain units'),
]
out = ROOT / '.planning/debug/xr-stat-probe'
out.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
log = run / 'completion.log'; log.write_text('Evidence: ' + str(run) + '\n')
print('Evidence: ' + str(run), flush=True)
manifest = {'productionSha256': hashlib.sha256(raw.encode()).hexdigest(), 'scope': 'Exact nested production probe; simulated provider values and real SDK method signatures only, no hardware execution.', 'cases': []}
exit_code = 1
dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
try:
    for name, before, after, expected in [('production', '', '', '')] + controls:
        case = run / name; case.mkdir()
        for path in (ROOT / 'tests/XrStatProbe.Tests').iterdir():
            if path.is_file(): shutil.copyfile(path, case / path.name)
        body = probe
        if before:
            assert body.count(before) == 1, name
            body = body.replace(before, after)
        (case / 'Production.cs').write_text(header + body + '\n}}\n')
        build = subprocess.run([dotnet, 'build', str(case / 'XrStatProbe.Tests.csproj'), '-c', 'Release', '--disable-build-servers', '-p:UseSharedCompilation=false'], capture_output=True, text=True, timeout=90)
        (case / 'build.log').write_text(build.stdout + build.stderr)
        assert build.returncode == 0, build.stdout + build.stderr
        command = [dotnet, str(case / 'bin/Release/net8.0/XrStatProbe.Tests.dll')]
        if args.sdk_managed and not before: command.append(str(args.sdk_managed.resolve()))
        result = subprocess.run(command, capture_output=True, text=True, timeout=30)
        text = result.stdout + result.stderr; (case / 'result.log').write_text(text)
        passed = result.returncode == 0 if not before else result.returncode != 0 and expected in text
        manifest['cases'].append({'name': name, 'exitCode': result.returncode, 'expected': expected, 'passed': passed})
        assert passed, text
        print(name + ': PASS ' + (expected or text.strip()), flush=True)
    exit_code = 0
finally:
    (run / 'result.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('xr-stat-probe', exit_code, log, ROOT, process_reaped=True), indent=2) + '\n')
