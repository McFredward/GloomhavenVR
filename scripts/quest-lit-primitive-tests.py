#!/usr/bin/env python3
"""Qualify the exact Quest lit Cube re-upload against ordinary Standard GPU output."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--graphics', choices=('vulkan', 'glcore'), default='vulkan')
    args = parser.parse_args()
    base = ROOT / '.planning/debug/quest-lit-primitive'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); project = run / 'unity'; editor = project / 'Assets/Editor'
    editor.mkdir(parents=True)
    helper_path = ROOT / 'src/GloomhavenVR/Core/QuestLitPrimitiveMesh.cs'
    helper = helper_path.read_text().replace('namespace GloomhavenVR.Core;', 'namespace GloomhavenVR.Core {') + '\n}\n'
    (editor / 'QuestLitPrimitiveMesh.cs').write_text('#nullable enable\n' + helper)
    (editor / 'Boundaries.cs').write_text('namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled; } internal static class VRLog { internal static bool WantsDebug => true; } }\n')
    probe = ROOT / 'tests/QuestLitPrimitive.Tests/Probe.cs'; (editor / 'Probe.cs').write_bytes(probe.read_bytes())
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: 1\n')
    log = run / 'runner.log'; log.write_text('Quest lit primitive evidence: ' + str(run) + '\n'); print(log.read_text(), end='', flush=True)
    command = ['/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-' + args.graphics,
        '-projectPath', str(project), '-executeMethod', 'QuestLitPrimitiveProbe.Run', '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT); code = 1
    try:
        code = process.wait(timeout=180)
        if code: raise RuntimeError('Native lit Cube fixture failed: ' + str(run / 'unity.log'))
        report = json.loads((project / 'ProbeOutput/results.json').read_text())
        if report['assertions'] < 100: raise RuntimeError('Incomplete native lit Cube evidence')
        (run / 'source-hashes.json').write_text(json.dumps({'helper': hashlib.sha256(helper_path.read_bytes()).hexdigest(),
            'probe': hashlib.sha256(probe.read_bytes()).hexdigest(), 'graphics': args.graphics, 'hardwareVerified': False}, indent=2) + '\n')
        print('PASS Quest lit Cube ' + args.graphics + ': ' + str(report['assertions']) + ' assertions', flush=True)
    finally:
        if process.poll() is None: process.kill(); process.wait()
        cleanup = cleanup_completed_run('quest-lit-primitive', code, log, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': main()
