#!/usr/bin/env python3
"""Exercise the production initiative/WorldSurface lifetime coordinator in real Unity.

The native scenario's singleton may change while the previous original is still
alive. This fixture uses actual Unity hierarchy, world geometry and ray/portrait
intersection. CanvasConversion's native adoption boundary is modeled; it does
not claim a full multiplayer session or the reporter's unlogged headset cause.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run
ROOT = Path(__file__).resolve().parents[1]

def method(source, signature):
    begin = source.index(signature)
    opening = source.index('{', begin)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[begin:end]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/initiative-binding-runtime')
    args = parser.parse_args()
    output = args.output_dir.resolve(); output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    log = run/'completion.log'; log.write_text('Evidence: '+str(run)+'\n')
    status = {'exit_code': 1}
    def cleanup():
        receipt = cleanup_completed_run('initiative-binding-runtime', status['exit_code'], log, ROOT, process_reaped=True)
        (run/'cleanup.json').write_text(json.dumps(receipt, indent=2)+'\n')
    atexit.register(cleanup)
    fixture = ROOT/'scripts/initiative-binding-runtime'
    world = ROOT/'src/GloomhavenVR/WorldUI'
    table_path = world/'Surfaces/TablePanelSurfaces.cs'
    table = table_path.read_text()
    ini = table[table.index('internal sealed class InitiativeTrackSurface'):table.index('internal sealed class ElementBoardSurface')]
    properties = ini[:ini.index('    // ---- portrait depth normalization')]
    properties = properties.replace('internal sealed class InitiativeTrackSurface', 'internal sealed partial class InitiativeTrackSurface')
    signatures = ['    public override void Tick()', '    public override void LateTick()',
        '    protected override void ReleasePanel(', '    protected override void OnConverted()',
        '    private void UnregisterDepthPick()', '    bool IDepthPortraitPicker.TryPickPortrait(',
        '    private static bool TryIntersectRect(']
    methods = '\n'.join(method(ini, sig) for sig in signatures)
    start = ini.index('    private bool OwnsCurrentTrack()'); end = ini.index(';',start)+1
    methods += '\n'+ini[start:end]
    source = '''#nullable enable
using UnityEngine; using UnityEngine.UI; using System.Collections.Generic;
using GloomhavenVR.Core; using GloomhavenVR.Cards; using GloomhavenVR.Hands.Interact;
namespace GloomhavenVR.WorldUI.Surfaces {
'''+table[table.index('internal abstract class SlotPanelSurface'):table.index('/// <summary>\n/// The game\'s REAL initiative track')]+properties+methods+'\n}}\n'
    sources = {'Coordinator.cs': source, 'WorldSurface.cs': (world/'Surfaces/WorldSurface.cs').read_text(),
               'Boundary.cs': (fixture/'Boundary.cs').read_text(), 'Checks.cs': (fixture/'Checks.cs').read_text()}
    variants = [
        ('production', None, None, None, ''),
        ('old-live-target', 'Coordinator.cs', 'if (Panel != null && !OwnsCurrentTrack())', 'if (Panel != null && !Panel.IsAlive)',
         'replacement native singleton is adopted while old panel is still alive'),
        ('late-stale-target', 'Coordinator.cs', '            Tick();\n        base.LateTick();', '            { }\n        base.LateTick();',
         'late native replacement is rebound before row animation and capture'),
        ('restore-new-original', 'Coordinator.cs', 'PanelMipBake.Restore(_convertedTrack);', 'PanelMipBake.Restore(InitiativeTrack.Instance);',
         'retarget restores mip ownership to the old original, never the replacement'),
        ('stale-picker', 'Coordinator.cs', '        UnregisterDepthPick();', '        // injected missing unregister',
         'only current host retains its registered portrait picker'),
        ('scale-dependent-normal', 'Coordinator.cs', 'Vector3.Cross(right / Mathf.Sqrt(rightLen2),\n            up / Mathf.Sqrt(upLen2)).normalized',
         'Vector3.Cross(right, up).normalized', 'ray resolves the exact displayed actor after native rebind: index=0 scale=(0.00001667'),
        ('retired-host-lease', 'Coordinator.cs',
         '        && Panel.HostGo != null && Panel.HostCanvas != null && Panel.HostRect != null\n        && Panel.Target.IsChildOf(Panel.HostTransform)\n', '',
         'same current native original re-adopts after its host lease was retired'),
        ('double-native-restore', 'Coordinator.cs',
         '        var active = CanvasConversion.ActivePanels;\n        for (int i = 0; i < active.Count; i++)\n            if (ReferenceEquals(active[i], panel))\n            {\n                base.ReleasePanel(panel);\n                break;\n            }',
         '        base.ReleasePanel(panel);', 'retired lease cannot overwrite subsequent native layout before new adoption')]

    manifest = {'result': str(run/'results.txt'), 'cases': []}
    dotnet = os.environ.get('DOTNET_EXE','/home/claw/.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
    for name, file, old, new, expected in variants:
        case = run/name; generated = case/'production'; generated.mkdir(parents=True)
        for path, code in sources.items():
            if path == file:
                assert old in code, 'missing production mutation seam: '+str(old)
                code = code.replace(old,new)
            (generated/path).write_text(code)
        assembly = 'InitiativeBinding_'+name.replace('-','_')
        project = case/'Binding.csproj'; shutil.copyfile(fixture/'Binding.csproj',project)
        built = subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(case/'empty'),'-p:ProductionDir='+str(generated),
            '-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(ROOT/'ressources/GH_Data/Managed')],capture_output=True,text=True)
        (case/'build.log').write_text(built.stdout+built.stderr)
        assert built.returncode == 0, built.stdout+built.stderr
        manifest['cases'].append({'name': name, 'dll': str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')), 'expected': expected})
    project = run/'unity-project'
    for folder in ['Assets/Editor','Assets/Plugins','Packages','ProjectSettings']: (project/folder).mkdir(parents=True)
    for entry in manifest['cases']:
        dest = project/'Assets/Plugins'/Path(entry['dll']).name; shutil.copyfile(entry['dll'],dest); entry['dll'] = str(dest)
    shutil.copyfile(fixture/'Editor/BindingRunner.cs',project/'Assets/Editor/BindingRunner.cs')
    shutil.copyfile(ROOT/'ressources/GH_Data/Managed/UnityEngine.UI.dll',project/'Assets/Plugins/UnityEngine.UI.dll')
    (project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    manifest_path = run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    pins = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in [table_path, world/'Surfaces/WorldSurface.cs', *fixture.glob('*.cs'), fixture/'Editor/BindingRunner.cs']}
    (run/'source-hashes.json').write_text(json.dumps(pins,indent=2)+'\n')
    command=[str(unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','BindingRunner.Run',
        '-bindingManifest',str(manifest_path),'-logFile',str(run/'Unity.log')]
    result = subprocess.run(command,capture_output=True,text=True,timeout=240)
    (run/'launcher.log').write_text(result.stdout+result.stderr)
    text=(run/'results.txt').read_text() if (run/'results.txt').exists() else ''
    print(text,end=''); print('Evidence: '+str(run))
    assert result.returncode == 0, 'Unity runtime proof failed: '+str(run)
    assert 'PASS production:' in text and text.count('PASS negative control')==7, 'missing runtime evidence'
    # The unit-normal repair changes input math, never the displayed geometry.
    good = json.loads((run/'InitiativeBinding_production-geometry.json').read_text())
    old = json.loads((run/'InitiativeBinding_scale_dependent_normal-geometry.json').read_text())
    for field in ('origin','direction','center','corners','hostScale','actorScale'):
        assert good[field] == old[field], 'input repair unexpectedly changed physical '+field
    assert good['hit'] and not old['hit'], 'negative control must differ only in actual portrait hit'
    assert good['normalizedArea'] == {'x':0.0,'y':0.0,'z':0.0}, 'original Unity normalization cutoff not reproduced'
    (run/'geometry-comparison.json').write_text(json.dumps({'identical_geometry':True,
        'production_hit':good['hit'],'old_area_normal_hit':old['hit'],
        'old_area_normal':good['normalizedArea'],'new_axis_normal':good['normalizedAxes']},indent=2)+'\n')
    print('PASS identical-world-geometry witness: only the portrait hit differs at small zoom.')
    status['exit_code']=0
if __name__=='__main__': main()
