#!/usr/bin/env python3
"""Render physical MR backing depth with original native initiative transforms.

Uses exact production dock placement, plate fit and ink-corner depth. Read-only
native prefab metadata supplies the portrait ancestor chain; a real native
world-X reorder write under the rotated board causes the regression. The colour
image is a visible geometry witness, not a claim to render original portrait art
or reproduce an unrecorded headset transform.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/mr-backing-depth-runtime')
    p.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    p.add_argument('--mutation', choices=['host-plane', 'world-scale'])
    p.add_argument('--all', action='store_true')
    args = p.parse_args()
    if args.all:
        assert not args.mutation
        for mutation in (None, 'host-plane', 'world-scale'):
            subprocess.run([sys.executable, str(Path(__file__).resolve()), '--output-dir', str(args.output_dir),
                            '--unity', str(args.unity)] + (['--mutation', mutation] if mutation else []), check=True)
        return
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    log = run / 'completion.log'; log.write_text('Evidence: ' + str(run) + '\n')
    state = {'exit_code': 1}
    common = Path(subprocess.check_output(['git', 'rev-parse', '--git-common-dir'], cwd=ROOT, text=True).strip())
    integration = (ROOT / common).resolve().parent if not common.is_absolute() else common.resolve().parent
    cleanup_root = integration if run.is_relative_to(integration / '.planning/debug') else ROOT
    def cleanup():
        receipt = cleanup_completed_run('mr-backing-depth-runtime', state['exit_code'], log,
                                        cleanup_root, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    atexit.register(cleanup)
    ui = ROOT / 'src/GloomhavenVR/WorldUI'
    backing_path, ink_path = ui / 'MrBacking.cs', ui / 'Conversion/PanelInkBounds.cs'
    dock_path = ui / 'Surfaces/TablePanelSurfaces.cs'
    mirror_path = ROOT / 'src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs'
    backing, ink, dock, mirror = (path.read_text() for path in (backing_path, ink_path, dock_path, mirror_path))
    assert 'SeatBehindContent(entry.Plate, owned ? 0f : entry.BackZ);' in backing
    assert 'if (s is IDepthBacking depth) SeatBehindContent(e.Plate, depth.BackingDepth);' in backing
    assert 'entry.BackZ = entry.BoundsVisible ? ink.BackZ : 0f;' in backing
    assert 'if (backingGeometry) ink.BackZ = Mathf.Max(ink.BackZ, backZ);' in ink
    assert 'WorldUI.MrBacking.IDepthBacking.BackingDepth => _mrBackZ;' in mirror
    assert 'new Vector3(bounds.center.x, bounds.center.y, ink.BackZ))).z;' in mirror
    fit = method(backing, '    private static void Fit(')
    seat = method(backing, '    private static void SeatBehindContent(')
    bounds = method(ink, '    private static bool TryHostLocalBounds(')
    place = method(dock[dock.index('internal abstract class TrayMountedPanelSurface'):], '    protected override void Place(')
    if args.mutation == 'host-plane':
        assert seat.count('position.z + backZ') == 1
        seat = seat.replace('position.z + backZ', 'position.z')
    if args.mutation == 'world-scale':
        assert fit.count('PlateGapMeters * PanelLayout.WorldScale') == 1
        fit = fit.replace('PlateGapMeters * PanelLayout.WorldScale', 'PlateGapMeters')
    project = run / 'unity-project'
    for directory in ['Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings']:
        (project / directory).mkdir(parents=True, exist_ok=True)
    fixture = ROOT / 'tests/mr-backing-depth-runtime'
    shutil.copyfile(fixture / 'Editor/BackingDepthRunner.cs', project / 'Assets/Editor/BackingDepthRunner.cs')
    shutil.copyfile(ROOT / 'ressources/GH_Data/Managed/UnityEngine.UI.dll', project / 'Assets/Plugins/UnityEngine.UI.dll')
    shutil.copyfile(ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader', project / 'Assets/Overlay.shader')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    production = '''#nullable enable
using UnityEngine; namespace GloomhavenVR.WorldUI {
internal static partial class MrBacking {
private const float PlateGapMeters = .002f;
internal static void Apply(Transform plate,Transform anchor,Vector2 size,Vector2 center,float backZ)
{ Fit(plate,anchor,size,center); SeatBehindContent(plate,backZ); }
'''+fit+'\n'+seat+'''
}
internal static class PanelInkBounds {
private static readonly Vector3[] Corners = new Vector3[4];
internal static bool Measure(RectTransform host,RectTransform target,out Rect bounds,out float backZ)
=> TryHostLocalBounds(host,target,out bounds,out backZ);
'''+bounds+'''
}
internal partial class TrayMountedPanelSurface {
'''+place+'\n}}\n'
    (project / 'Assets/Editor/Production.cs').write_text(production)
    # The native parent layout applies a dynamic slot/root width at runtime; retain
    # original descendants byte-for-byte in this provenance-backed numeric fixture.
    metadata = json.loads((fixture / 'NativeInitiativeGeometry.json').read_text())
    source_asset = ROOT / 'ressources/GH_Data' / metadata['source']
    assert hashlib.file_digest(source_asset.open('rb'), 'sha256').hexdigest() == metadata['sourceSHA256']
    command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
               '-executeMethod', 'BackingDepthRunner.Run', '-mrDepthOutput', str(run),
               '-nativeInitiativeGeometry', str(fixture / 'NativeInitiativeGeometry.json'), '-logFile', str(run / 'Unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
    completed = subprocess.run(command, capture_output=True, text=True, timeout=240)
    (run / 'launcher.log').write_text(completed.stdout + completed.stderr)
    result = (run / 'result.txt').read_text() if (run / 'result.txt').is_file() else ''
    unity_log = (run / 'Unity.log').read_text()
    assert 'error CS' not in unity_log and 'Shader error' not in unity_log, 'Compilation is not a causal control: ' + str(run)
    if args.mutation:
        expected = {'host-plane': 'depth-bearing original initiative remains in front',
                    'world-scale': 'actual original plate separation remains two tracking millimetres'}[args.mutation]
        assert completed.returncode != 0 and expected in result, result + '\n' + str(run)
        print('PASS compiling causal control: ' + args.mutation)
    else:
        assert completed.returncode == 0 and 'actual Unity assertions passed' in result, result + '\n' + str(run)
        print(result, end='')
    (run / 'inputs.json').write_text(json.dumps({'mutation': args.mutation, 'sources': {
        str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (backing_path, ink_path, dock_path, mirror_path, fixture / 'NativeInitiativeGeometry.json',
                     fixture / 'Editor/BackingDepthRunner.cs')}, 'limits': [
        'Exact native transform chain and production dock/plate methods; UI art is a solid geometry witness.',
        'Hardware reports 31.51px foreign drift but did not log actual per-graphic Z; that matching mechanism remains an inference.',
        'GL rendered depth and ordinary UI pixels do not certify the Windows stereo headset picture.']}, indent=2)+'\n')
    state['exit_code'] = 0
    print('Evidence: ' + str(run))


if __name__ == '__main__': main()
