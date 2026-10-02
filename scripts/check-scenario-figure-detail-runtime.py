#!/usr/bin/env python3
"""Run the complete production scenario-figure quality driver in Unity 2021.3.5."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-figure-detail-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required; this proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/scenario-figure-detail-runtime'
    base = args.source_root / 'src/GloomhavenVR/Core'
    figures = (base / 'Perf/ScenarioFigureDetailBudget.cs').read_text()
    sources = {'Figures.cs': figures.replace('Time.unscaledTime', 'FigureClock.Now')}
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': {key: hashlib.sha256(text.encode()).hexdigest() for key, text in sources.items()}}, indent=2)+'\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('miss-local-hold', 'Figures.cs', 'HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor)', 'false || NetHeldFigures.Owns(actor)', 'local hold restores original'),
            ('miss-remote-hold', 'Figures.cs', 'HeldFigures.Owns(actor) || NetHeldFigures.Owns(actor)', 'HeldFigures.Owns(actor) || false', 'remote hold restores original'),
            ('admit-empty-lod', 'Figures.cs', 'if (coarse > 0 && coarse < full) admitted.Add(i);', 'if (coarse < full) admitted.Add(i);', 'empty far-cull LOD'),
            ('overwrite-foreign-table', 'Figures.cs', 'if (!SameTable(current, Applied ?? Original))', 'if (!SameTable(current, Applied ?? Original) && PerfConfig.PlayerFigureDetailPercent < 0)', 'foreign LOD controller table'),
            ('miss-steady-ownership', 'Figures.cs', 'if (Group == null || !SameTable(Group.GetLODs(), Applied))', 'if (Group == null)', 'bounded steady ownership check'),
            ('cancel-native-reset', 'Figures.cs', 'if (!forcingPosition && !Cloth.enabled) Cloth.enabled = true;', 'if (!Cloth.enabled) Cloth.enabled = true;', 'native cloth teleport reset'),
            ('admit-map-actors', 'Figures.cs', 'if (root.scene != scene) continue;', 'if (root.scene != scene && PerfConfig.PlayerFigureDetailPercent < 0) continue;', 'map models and immersive NPCs'),
            ('skip-shutdown-restoration', 'Figures.cs', 'foreach (ActorRecord record in _actors)\n            {\n                try { record.Apply(true); }', 'foreach (ActorRecord record in _actors)\n            {\n                try { if (_faulted) record.Apply(true); }', 'VR off restores exact original'),
        ]
    manifest = {'result': str(run/'results.txt'), 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        for path, text in sources.items():
            if path == filename:
                if text.count(before) != 1: raise SystemExit('Production mutation binding drift: '+name)
                text = text.replace(before, after, 1)
            if name == 'skip-shutdown-restoration' and path == 'Figures.cs':
                text = text.replace('try { lod.Restore(); } catch', 'try { if (_faulted) lod.Restore(); } catch', 1)
            (production/path).write_text(text)
        project=build/'Figures.csproj'; shutil.copyfile(fixture/'Figures.csproj', project)
        assembly='ScenarioFigureDetail_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path=run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2))
    project=run/'unity'; (project/'Assets/Editor').mkdir(parents=True); (project/'Packages').mkdir(); (project/'ProjectSettings').mkdir()
    shutil.copyfile(ROOT/'scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.animation":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result=subprocess.run([str(args.unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    report=Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__=='__main__': main()
