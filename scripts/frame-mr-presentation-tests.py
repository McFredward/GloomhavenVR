#!/usr/bin/env python3
"""Execute production MR presentation and camera restoration in actual Unity graphics.

Native XR, settings and sky discovery are explicit boundaries. This verifies RGBA
pixels and live Camera state, not Proton, compositor, camera access or headset output.
"""
import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/frame-mr-presentation')
    parser.add_argument('--mutation', choices=['opaque-native', 'hdr-restore'])
    parser.add_argument('--all', action='store_true', help='Run production and both causal negative controls')
    args = parser.parse_args()
    if args.all:
        if args.mutation:
            parser.error('--all cannot be combined with --mutation')
        common = [sys.executable, str(Path(__file__).resolve()), '--unity', str(args.unity),
                  '--output-dir', str(args.output_dir)]
        for mutation in (None, 'opaque-native', 'hdr-restore'):
            subprocess.run(common + (['--mutation', mutation] if mutation else []), check=True)
        return
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir))
    source_path = ROOT / 'src/GloomhavenVR/Core/MixedReality/MixedReality.cs'
    camera_path = ROOT / 'src/GloomhavenVR/Core/MixedReality/MrNativeCamera.cs'
    source = source_path.read_text()
    if args.mutation == 'opaque-native':
        assert source.count('native ? Color.clear : KeyColor.Value') == 1
        source = source.replace('native ? Color.clear : KeyColor.Value', 'KeyColor.Value')
    if args.mutation == 'hdr-restore':
        assert source.count('MrNativeCamera.RestoreAll();') == 1
        source = source.replace('MrNativeCamera.RestoreAll();', '/* injected: restoration removed */')
    declarations = [method(source, signature) for signature in (
        'internal static void Tick()', 'private static void Record(Camera cam)',
        'private static void ForceSolid(Camera cam, Color key)', 'internal static void RestoreAll()')]
    start = source.index('internal static bool BackingsWanted =>')
    declarations.append(source[start:source.index(';', start) + 1])
    generated = run / 'Presentation.g.cs'
    generated.write_text('#nullable enable\nusing System.Collections.Generic; using UnityEngine; namespace GloomhavenVR.Core; '
        'internal static partial class MixedReality {\n' + '\n'.join(declarations) + '\n}\n')
    fixture = ROOT / 'tests/frame-mr-presentation'
    build = run / 'build'
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    command = [dotnet, 'build', str(fixture / 'Presentation.csproj'), '-c', 'Release',
        '-p:CaseName=FrameMrPresentation', '-p:PresentationSource=' + str(generated),
        '-p:CameraSource=' + str(camera_path), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
        '-p:BaseIntermediateOutputPath=' + str(build / 'obj') + '/',
        '-p:OutputPath=' + str(build / 'bin') + '/']
    compiled = subprocess.run(command, capture_output=True, text=True)
    (run / 'build.log').write_text(compiled.stdout + compiled.stderr)
    if compiled.returncode:
        raise SystemExit(compiled.stdout + compiled.stderr)
    project = run / 'unity-project'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
        (project / directory).mkdir(parents=True, exist_ok=True)
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    shutil.copy2(fixture / 'Editor/FrameMrRunner.cs', project / 'Assets/Editor/FrameMrRunner.cs')
    shutil.copy2(build / 'bin/FrameMrPresentation.dll', project / 'Assets/Plugins/FrameMrPresentation.dll')
    result = run / 'result.txt'
    command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'FrameMrRunner.Run', '-frameMrResult', str(result), '-logFile', str(run / 'Unity.log')]
    if not os.environ.get('DISPLAY'):
        command = ['xvfb-run', '-a'] + command
    completed = subprocess.run(command, capture_output=True, text=True, timeout=300)
    (run / 'launcher.log').write_text(completed.stdout + completed.stderr)
    log = (run / 'Unity.log').read_text() if (run / 'Unity.log').exists() else ''
    assert not re.search(r'error CS\d+|Shader error in', log), 'A compilation error is not a negative control'
    assert result.exists(), 'Unity did not produce the required rendered result: ' + str(run)
    output = result.read_text()
    if args.mutation:
        expected = {'opaque-native': 'accepted native Frame clear is transparent black',
                    'hdr-restore': 'MR off restores original head color and HDR'}[args.mutation]
        assert completed.returncode != 0 and expected in output, output
        print('PASS negative control: ' + args.mutation)
    else:
        print(output, end='')
        if completed.returncode:
            raise SystemExit(completed.returncode)
    (run / 'source-sha256.txt').write_text('\n'.join(str(path.relative_to(ROOT)) + ' '
        + hashlib.sha256(path.read_bytes()).hexdigest() for path in (source_path, camera_path)) + '\n')
    # Preserve small causal receipts, never whole editor caches from successful runs.
    shutil.rmtree(project / 'Library', ignore_errors=True)
    print('Evidence: ' + str(run))


if __name__ == '__main__':
    main()
