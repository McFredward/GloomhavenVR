#!/usr/bin/env python3
"""Exercise the actual initiative slide/surface coordinator, Unity layout and native LeanTween IL.

The native controller's actor/phase/bootstrap is an explicit data boundary. This
does not replay the reporter's save, a Photon session or headset rendering.
"""
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


def method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 1
    cursor = opening + 1
    while depth:
        depth += (source[cursor] == '{') - (source[cursor] == '}')
        cursor += 1
    return source[start:cursor]


def execute(command, log, env=None, timeout=240):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT,
                                 start_new_session=True, env=env)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--asset-root', type=Path, help='Read-only asset checkout for sparse workers.')
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/initiative-row-runtime')
    parser.add_argument('--baseline', action='store_true', help='Prove both faults against untouched old production.')
    parser.add_argument('--case', action='append', help='Selected production/control cases; partial evidence only.')
    args = parser.parse_args()
    root = args.source_root.resolve()
    asset_root = (args.asset_root or root).resolve()
    out = args.output_dir.resolve(); out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    status = 1
    env = dict(os.environ)
    own_adb = not env.get('ADB_SERVER_SOCKET')
    if own_adb:
        env['ADB_SERVER_SOCKET'] = 'localfilesystem:/tmp/gvr-row-' + run.name + '.sock'
        env['ADB_MDNS_AUTO_CONNECT'] = ''
    try:
        world = root / 'src/GloomhavenVR/WorldUI'
        table_path = world / 'Surfaces/TablePanelSurfaces.cs'
        table = table_path.read_text()
        initiative = table[table.index('internal sealed class InitiativeTrackSurface'):
                           table.index('internal sealed class ElementBoardSurface')]
        properties = initiative[:initiative.index('    // ---- portrait depth normalization')]
        properties = properties.replace('internal sealed class InitiativeTrackSurface',
                                        'internal sealed partial class InitiativeTrackSurface')
        signatures = ['    public override void Tick()', '    public override void LateTick()',
                      '    protected override void ReleasePanel(', '    protected override void OnConverted()',
                      '    private void UnregisterDepthPick()', '    bool IDepthPortraitPicker.TryPickPortrait(',
                      '    private static bool TryIntersectRect(', '    private void UpdateFitHold()',
                      '    private static int RowLayoutSignature(', '    private void NormalizeDepth()',
                      '    private void RestoreDepth()']
        methods = '\n'.join(method(initiative, signature) for signature in signatures)
        start = initiative.index('    private bool OwnsCurrentTrack()')
        methods += '\n' + initiative[start:initiative.index(';', start) + 1]
        coordinator = ('#nullable enable\nusing UnityEngine; using UnityEngine.UI; using System.Collections.Generic;\n'
                       'using GloomhavenVR.Core; using GloomhavenVR.Cards; using GloomhavenVR.Hands.Interact;\n'
                       'namespace GloomhavenVR.WorldUI.Surfaces {\n' +
                       table[table.index('internal abstract class SlotPanelSurface'):
                             table.index("/// <summary>\n/// The game's REAL initiative track")] +
                       properties + methods + '\n}}\n')
        fixture = ROOT / 'scripts/initiative-row-runtime'
        paths = [world / 'Surfaces/WorldSurface.cs', world / 'Surfaces/InitiativeReorderSlide.cs',
                 world / 'Conversion/UiHierarchyInventory.cs']
        sources = {path.name: path.read_text() for path in paths}
        sources.update({path.name: path.read_text() for path in fixture.glob('*.cs')})
        sources['Coordinator.cs'] = coordinator
        if args.baseline:
            cases = [('baseline-changed', 'changed', None, None, None,
                      'native entry plane and final pixels restored after changed-add'),
                     ('baseline-cold', 'cold', None, None, None,
                      'native entry plane and final pixels restored after cold-conversion')]
        else:
            cases = [('production-changed', 'changed', None, None, None, ''),
                     ('production-remove', 'remove', None, None, None, ''),
                     ('production-paused', 'paused', None, None, None, ''),
                     ('production-cold', 'cold', None, None, None, ''),
                     ('old-changed', 'changed', 'InitiativeReorderSlide.cs',
                      '        RestoreEntryPlanes(track, holder);', '',
                      'native entry plane and final pixels restored after changed-add'),
                     ('old-cold', 'cold', 'Coordinator.cs', '        _slide.Prime(_convertedTrack);', '',
                      'native entry plane and final pixels restored after cold-conversion'),
                     ('linear-waveform', 'changed', 'InitiativeReorderSlide.cs',
                      'float e = done ? 1f : 1f - (1f - t) * (1f - t);', 'float e = t;',
                      'actual native easeOutQuad waveform and full duration survive row-plane repair'),
                     ('missing-release', 'cold', 'Coordinator.cs', '_slide.Abort("panel released");', '',
                      'release retires the real slide before native home restoration')]
        if args.case:
            assert set(args.case) <= {case[0] for case in cases}, 'Unknown selected case'
            cases = [case for case in cases if case[0] in args.case]
        unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
        managed = root / 'ressources/GH_Data/Managed'
        project = run / 'unity-project'
        for folder in ['Assets/Editor', 'Assets/Plugins', 'Assets/Resources', 'ProjectSettings', 'Packages']:
            (project / folder).mkdir(parents=True)
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run / 'captures'), 'cases': []}
        for name, scenario, file, old, new, expected in cases:
            case = run / name; generated = case / 'sources'; generated.mkdir(parents=True)
            for filename, source in sources.items():
                if filename == file:
                    assert old in source, 'Missing causal seam: ' + name
                    source = source.replace(old, new)
                (generated / filename).write_text(source)
            csproj = case / 'Row.csproj'; shutil.copyfile(fixture / 'Row.csproj', csproj)
            assembly = 'InitiativeRow_' + name.replace('-', '_')
            command = [str(Path.home() / '.dotnet/dotnet'), 'build', str(csproj), '-c', 'Release',
                       '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false', '-nodeReuse:false',
                       '-p:CaseName=' + assembly, '-p:ProductionDir=' + str(generated),
                       '-p:FixtureDir=' + str(case / 'none'), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
                       '-p:GameManaged=' + str(managed)]
            assert execute(command, case / 'build.log', timeout=120) == 0, 'Fixture build failed: ' + str(case / 'build.log')
            dll = case / 'bin/Release/netstandard2.1' / (assembly + '.dll')
            target = project / 'Assets/Plugins' / dll.name; shutil.copyfile(dll, target)
            manifest['cases'].append({'name': name, 'scenario': scenario, 'dll': str(target), 'expected': expected})
        native = ['UnityEngine.UI.dll', 'GH.Runtime.FirstPass.dll', 'SM.Utils.dll']
        for name in native:
            shutil.copyfile(managed / name, project / 'Assets/Plugins' / name)
        runner = fixture / 'Editor/RowRunner.cs'
        shutil.copyfile(runner, project / 'Assets/Editor/RowRunner.cs')
        overlay = asset_root / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader'
        shutil.copyfile(overlay, project / 'Assets/Resources/Overlay.shader')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
            name: '1.0.0' for name in ['com.unity.modules.ui', 'com.unity.modules.imgui',
                                     'com.unity.modules.jsonserialize', 'com.unity.modules.physics',
                                     'com.unity.modules.imageconversion']}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        pins = [table_path, *paths, Path(__file__), *fixture.rglob('*.cs'), fixture / 'Row.csproj',
                overlay, *[managed / name for name in native], unity,
                root / 'decompiled/GH.Runtime/InitiativeTrack.cs',
                root / 'tests/mr-backing-depth-runtime/NativeInitiativeGeometry.json']
        (run / 'source-pins.json').write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                                                        for path in pins}, indent=2) + '\n')
        path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                   '-executeMethod', 'RowRunner.Start', '-rowManifest', str(path), '-logFile', str(run / 'Unity.log')]
        if not os.environ.get('DISPLAY'):
            command = ['xvfb-run', '-a', *command]
        code = execute(command, run / 'launcher.log', env=env, timeout=300)
        result = (run / 'results.txt').read_text() if (run / 'results.txt').is_file() else ''
        print(result, end=''); print('Evidence: ' + str(run))
        assert code == 0, 'Actual Unity proof failed: ' + str(run)
        assert result.count('PASS negative control ') == sum(bool(case[-1]) for case in cases), 'Missing causal control'
        assert result.count('PASS production-') == sum(not case[-1] for case in cases), 'Missing production case'
        (run / 'limits.txt').write_text(__doc__ + '\nExact production slide, surface Tick/LateTick/OnConverted/Release, fit-hold and depth normalization execute. Canvas host acquisition, mip/popup/pin/Chronos-diagnostic bootstrap and native actor/phase data are declared ports; original LeanTween IL, actual Unity layout/clocks/uGUI/Overlay pixels are real. Historical screenshot/save/Photon/headset trigger remains unverified.\n')
        status = 0
    finally:
        if own_adb:
            adb = shutil.which('adb')
            if adb:
                result = subprocess.run([adb, 'kill-server'], env=env, capture_output=True, text=True, timeout=20)
                (run / 'adb-cleanup.log').write_text(result.stdout + result.stderr)
        receipt = cleanup_completed_run('initiative-row-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    main()
