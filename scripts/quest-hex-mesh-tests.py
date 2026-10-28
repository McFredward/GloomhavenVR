#!/usr/bin/env python3
"""Qualify native hex vertex ownership, pooling, restoration and exact two-pass output."""
import argparse
import ctypes
import hashlib
import importlib.util
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-mask', type=Path, required=True)
    parser.add_argument('--control', choices=('no-upload', 'no-restore'))
    args = parser.parse_args()
    original_mask = args.native_mask.read_bytes()
    base = ROOT / '.planning/debug/quest-hex-mesh'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); project = run / 'unity'
    (project / 'Assets/Resources').mkdir(parents=True)
    inputs = {}
    for relative in ('src/GloomhavenVR/Board/QuestHexMesh.cs', 'src/GloomhavenVR/Core/QuestLitPrimitiveMesh.cs'):
        path = ROOT / relative; source = path.read_text(); inputs[relative] = hashlib.sha256(source.encode()).hexdigest()
        if path.name == 'QuestHexMesh.cs' and args.control:
            before, after = ('filter.sharedMesh = owned;', ';') if args.control == 'no-upload' else ('_filter.sharedMesh = _original;', ';')
            assert source.count(before) == 1
            source = source.replace(before, after, 1)
        namespace = 'GloomhavenVR.Board' if '/Board/' in relative else 'GloomhavenVR.Core'
        (project / 'Assets' / path.name).write_text('#nullable enable\n' + source.replace('namespace ' + namespace + ';', 'namespace ' + namespace + ' {') + '\n}\n')
    (project / 'Assets/Boundaries.cs').write_text('namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled; } internal static class VRLog { internal static bool WantsDebug => true; } }\n')
    probe = ROOT / 'tests/QuestHexMesh.Tests/Probe.cs'; shutil.copyfile(probe, project / 'Assets/Probe.cs')
    for name in ('HexDecalStable', 'Overlay'):
        path = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table' / (name + '.shader')
        shutil.copyfile(path, project / 'Assets/Resources' / path.name); inputs[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (project / 'Assets/Resources/NativeHexMask.png').write_bytes(original_mask)
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: 1\n')
    log = run / 'runner.log'; log.write_text('Quest hex mesh evidence: ' + str(run) + '\n'); print(log.read_text(), end='', flush=True)
    # Same scoped subreaper used by the existing native-artwork fixtures.
    spec = importlib.util.spec_from_file_location('artwork_fixture', ROOT / 'scripts/quest-artwork-tests.py')
    fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
    if sys.platform.startswith('linux') and ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'Could not own fixture children')
    command = ['/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-vulkan', '-projectPath', str(project),
               '-executeMethod', 'QuestHexMeshProbe.Run', '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True); code = 1
    try:
        code = process.wait(timeout=180)
        if args.control:
            expected = 'Native hex receives an independently owned upload' if args.control == 'no-upload' else 'Reset restores exact source identity'
            if code == 0 or expected not in (run / 'unity.log').read_text(): raise RuntimeError('Native hex control did not detect its targeted defect')
            (run / 'control.json').write_text(json.dumps({'control': args.control, 'expectedFailure': expected, 'observedExit': code, 'hardwareVerified': False}, indent=2) + '\n')
            print('PASS native hex causal control: ' + args.control, flush=True); code = 0; return
        if code: raise RuntimeError('Native hex fixture failed: ' + str(run / 'unity.log'))
        report = json.loads((project / 'ProbeOutput/results.json').read_text())
        if report['assertions'] < 70: raise RuntimeError('Incomplete native hex evidence')
        if args.native_mask.read_bytes() != original_mask: raise RuntimeError('Read-only native texture changed')
        (run / 'source-hashes.json').write_text(json.dumps({'sources': inputs, 'probe': hashlib.sha256(probe.read_bytes()).hexdigest(),
            'nativeMask': str(args.native_mask), 'nativeMaskSha256': hashlib.sha256(original_mask).hexdigest(), 'hardwareVerified': False}, indent=2) + '\n')
        print('PASS native hex mesh: ' + str(report['assertions']) + ' assertions; headset outcome unverified', flush=True)
    finally:
        if process.poll() is None: process.kill(); process.wait()
        reaped = fixture.reap_fixture_group(process.pid)
        (run / 'process-cleanup.json').write_text(json.dumps(reaped, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-hex-mesh', code, log, ROOT, process_reaped=True), indent=2) + '\n')


if __name__ == '__main__': main()
