#!/usr/bin/env python3
"""Compile production distance/skinning/NPC helpers and execute real original bodies in Unity."""
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
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--native-dir', type=Path)
    parser.add_argument('--case', action='append')
    args = parser.parse_args(); root = args.source_root.resolve()
    out = args.output_dir or root / '.planning/debug/figure-distance-runtime'
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=out.resolve()))
    native = args.native_dir or run / 'native'
    if args.native_dir is None:
        subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(root / 'tools/figure-mesh/export-native.py'),
                        '--source-root', str(root), '--output-dir', str(native), '--only',
                        'hero_berserker_assets_all', 'npc_spittingdrake_assets_all', 'npc_sundemon_assets_all'], check=True)
    files = ['FigureDistanceLodPolicy.cs', 'ScenarioFigureMeshBank.cs', 'FigureSkinningBudget.cs', 'TownNpcDistanceDetail.cs']
    source = {name: (root / 'src/GloomhavenVR/Core/Perf' / name).read_text() for name in files}
    station = (root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceStation.cs').read_text()
    start = station.index('    private static void PreserveActorDetail(Transform? actor)')
    end = station.index('    internal static TownServiceStation? Create(', start)
    native_detail = station[start:end].replace('private static void', 'internal static void', 1)
    source['StationDetail.cs'] = 'using System; using UnityEngine; namespace GloomhavenVR.Core { internal static class StationDetail {\n' + native_detail + '} }\n'
    source['NativeFigureMeshStream.cs'] = (root / 'tools/figure-mesh/NativeFigureMeshStream.cs').read_text()
    variants = [('production', '', '', '', ''),
                ('skip-npc-reduction', 'TownNpcDistanceDetail.cs', 'mesh.Apply(wanted);', 'mesh.Apply(100);', 'NPC mid actually reduces original 100k body'),
                ('keep-explicit-four-bones', 'FigureSkinningBudget.cs', 'Renderer.quality = _applied;', 'Renderer.quality = live;', 'explicit original FourBones renderer is capped'),
                ('far-only-old-tier', 'ScenarioFigureMeshBank.cs', 'detail < 0 ? 5 :', 'detail < 0 ? 20 :', 'far materially reduces already simplified native body')]
    if args.case:
        unknown = set(args.case) - {v[0] for v in variants}
        if unknown: parser.error('Unknown cases: ' + ', '.join(sorted(unknown)))
        variants = [v for v in variants if v[0] in args.case]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in source.items():
            if path == filename:
                if text.count(before) != 1: raise SystemExit('Exact mutation anchor drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Figures.csproj'
        shutil.copyfile(root / 'scripts/scenario-figure-detail-runtime/Figures.csproj', project)
        assembly = 'FigureDistance_' + name.replace('-', '_')
        result = subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                                 '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(root / 'scripts/figure-distance-runtime'),
                                 '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompile failures never pass as negative controls')
        dll = build / 'bin/Release/netstandard2.1' / (assembly + '.dll')
        for bank in (root / 'prebuilt').glob('ghvr-figure-meshes-*'):
            if bank.suffix in ('.bundle', '.json'): (dll.parent / bank.name).symlink_to(bank.resolve())
        manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
    (run / 'source-hashes.json').write_text(json.dumps({path: hashlib.sha256(text.encode()).hexdigest() for path, text in source.items()}, indent=2) + '\n')
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(root / 'scripts/figure-distance-runtime/Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start',
               '-interactionManifest', str(manifest_path), '-figureNativeDir', str(native.resolve()), '-townOriginalBundle', str(root / 'prebuilt/ghvr-town.bundle'),
               '-figureRenders', str(run / 'renders'), '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a', '-s', '-screen 0 640x480x24'] + command
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=600)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL actual original bodies; see ' + str(run / 'unity.log'))
    print('PASS focused production/negative controls; evidence: ' + str(run))

if __name__ == '__main__': main()
