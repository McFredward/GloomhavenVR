#!/usr/bin/env python3
"""Qualify bounded passive playback evidence, not headset audio quality."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / 'src/GloomhavenVR/Core/QuestCombatAudioEvidence.cs').read_text()
base = ROOT / '.planning/debug/quest-combat-audio'
base.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
print('Combat audio evidence: ' + str(run), flush=True)
controls = (
    ('production', '', '', ''),
    ('debug-gate', '!VRLog.Wants(VRLogLevel.Debug)', 'false', 'disabled-cost'),
    ('total-budget', 'Reports.Count >= 8', 'Reports.Count >= 80', 'total-bound'),
    ('clip-budget', 'if (count >= 2) return;', 'if (count >= 20) return;', 'per-clip'),
    ('playback-write', 'Reports.Add(facts);', 'source.pitch = 1; Reports.Add(facts);', 'read-only'),
    ('background-budget', 'if (Background.Count > 4)', 'if (Background.Count > 40)', 'background-bound'),
)
results = []
for name, before, after, expected in controls:
    folder = run / name
    shutil.copytree(ROOT / 'tests/QuestCombatAudio.Tests', folder, ignore=shutil.ignore_patterns('bin', 'obj'))
    if before:
        assert source.count(before) == (2 if name == 'debug-gate' else 1), name
    (folder / 'Evidence.cs').write_text(source.replace(before, after) if before else source)
    result = subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'run', '--project', str(folder), '-c', 'Release',
        '-p:EvidenceSource=' + str(folder / 'Evidence.cs')], capture_output=True, text=True, timeout=90)
    (folder / 'console.log').write_text(result.stdout + result.stderr)
    passed = result.returncode == 0 if not expected else result.returncode != 0 and expected in result.stderr + result.stdout
    results.append({'name': name, 'exitCode': result.returncode, 'expected': expected, 'passed': passed})
    (run / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    if not passed:
        raise SystemExit('FAIL ' + name + ': ' + str(folder / 'console.log'))
    print((result.stdout.strip() if not expected else 'PASS rejected ' + name + ' at ' + expected), flush=True)
